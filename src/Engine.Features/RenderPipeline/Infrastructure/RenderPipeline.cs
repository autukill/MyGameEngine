namespace GameEngine.Features.RenderPipeline.Infrastructure;

using Silk.NET.OpenGL;
using GameEngine.Features.RenderPipeline.Domain;

/// <summary>
/// Pass 调度器：拓扑排序后依次执行。
/// </summary>
public sealed class RenderPipeline : IDisposable
{
    private readonly List<RenderPass> _passes = new();
    private readonly GL _gl;
    private int _screenWidth;
    private int _screenHeight;
    private bool _disposed;

    public RenderPipeline(GL gl, int screenWidth, int screenHeight)
    {
        _gl = gl;
        _screenWidth = screenWidth;
        _screenHeight = screenHeight;
    }

    public void AddPass(RenderPass pass) => _passes.Add(pass);
    public void RemovePass(string name) => _passes.RemoveAll(p => p.Name == name);
    public IReadOnlyList<RenderPass> Passes => _passes;

    /// <summary>窗口 resize 后更新默认 framebuffer 的 Viewport 尺寸。</summary>
    public void Resize(int screenWidth, int screenHeight)
    {
        if (screenWidth <= 0 || screenHeight <= 0) return;
        _screenWidth = screenWidth;
        _screenHeight = screenHeight;
    }

    public void Execute(in RenderPassContext ctx)
    {
        var sorted = TopologicalSort(_passes);

        foreach (var pass in sorted)
        {
            if (!pass.IsEnabled) continue;

            // 切换 RenderTarget
            if (pass.Output is { } rt)
                rt.SetAsTarget();
            else
            {
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                _gl.Viewport(0, 0, (uint)_screenWidth, (uint)_screenHeight);
            }

            // 清屏 (Color + Depth + Stencil)
            _gl.Clear((uint)(ClearBufferMask.ColorBufferBit |
                             ClearBufferMask.DepthBufferBit |
                             ClearBufferMask.StencilBufferBit));

            pass.Execute(ctx);
        }
    }

    /// <summary>简化版拓扑排序：依据 Output/Inputs 引用相等构建依赖图</summary>
    private static List<RenderPass> TopologicalSort(List<RenderPass> passes)
    {
        var sorted = new List<RenderPass>(passes.Count);
        var visited = new HashSet<RenderTarget2D>();
        var remaining = new List<RenderPass>(passes);

        while (remaining.Count > 0)
        {
            int beforeCount = remaining.Count;
            for (int i = remaining.Count - 1; i >= 0; i--)
            {
                var p = remaining[i];
                bool ready = true;
                foreach (var input in p.Inputs)
                {
                    if (!visited.Contains(input)) { ready = false; break; }
                }
                if (ready)
                {
                    sorted.Add(p);
                    if (p.Output is not null) visited.Add(p.Output);
                    remaining.RemoveAt(i);
                }
            }
            if (remaining.Count == beforeCount)
                throw new InvalidOperationException(
                    "[RenderPipeline] cyclic dependency detected between RenderPasses");
        }
        return sorted;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var pass in _passes)
            pass.Dispose();
        _passes.Clear();
    }
}

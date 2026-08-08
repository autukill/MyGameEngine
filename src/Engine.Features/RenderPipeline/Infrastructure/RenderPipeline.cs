namespace GameEngine.Features.RenderPipeline.Infrastructure;

using Silk.NET.OpenGL;

/// <summary>Pass 调度器：支持在帧边界精确挂接/卸载，再按 RenderTarget 依赖执行。</summary>
public sealed class RenderPipeline : IDisposable
{
    private readonly List<RenderPass> _passes = new();
    private readonly Dictionary<RenderPassHandle, RenderPass> _passHandles = new();
    private readonly GL _gl;
    private int _screenWidth;
    private int _screenHeight;
    private long _nextPassHandle;
    private bool _disposed;
    private bool _isExecuting;

    public RenderPipeline(GL gl, int screenWidth, int screenHeight)
    {
        _gl = gl;
        _screenWidth = screenWidth;
        _screenHeight = screenHeight;
    }

    public IReadOnlyList<RenderPass> Passes => _passes;

    public RenderPassHandle AddPass(RenderPass pass)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(pass);
        EnsureMutable();
        if (_passes.Contains(pass))
            throw new InvalidOperationException($"RenderPass '{pass.Name}' is already attached.");

        var handle = new RenderPassHandle(++_nextPassHandle);
        _passes.Add(pass);
        _passHandles.Add(handle, pass);
        return handle;
    }

    /// <summary>按稳定 Handle 移除并释放 Pass。</summary>
    public bool RemovePass(RenderPassHandle handle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureMutable();
        if (!_passHandles.Remove(handle, out var pass)) return false;
        _passes.Remove(pass);
        pass.Dispose();
        return true;
    }

    /// <summary>兼容名称移除；所有同名 Pass 都会被释放。</summary>
    public int RemovePass(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureMutable();
        var handles = _passHandles
            .Where(pair => pair.Value.Name == name)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var handle in handles) RemovePass(handle);
        return handles.Length;
    }

    public void Resize(int screenWidth, int screenHeight)
    {
        if (screenWidth <= 0 || screenHeight <= 0) return;
        _screenWidth = screenWidth;
        _screenHeight = screenHeight;
    }

    public void Execute(in RenderPassContext ctx)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isExecuting)
            throw new InvalidOperationException("RenderPipeline cannot execute recursively.");

        _isExecuting = true;
        try
        {
            var sorted = TopologicalSort(_passes);
            foreach (var pass in sorted)
            {
                if (!pass.IsEnabled) continue;

                if (pass.Output is { } rt)
                    rt.SetAsTarget();
                else
                {
                    _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                    _gl.Viewport(0, 0, (uint)_screenWidth, (uint)_screenHeight);
                }

                _gl.Clear((uint)(ClearBufferMask.ColorBufferBit |
                                 ClearBufferMask.DepthBufferBit |
                                 ClearBufferMask.StencilBufferBit));
                pass.Execute(ctx);
            }
        }
        finally
        {
            _isExecuting = false;
        }
    }

    private void EnsureMutable()
    {
        if (_isExecuting)
            throw new InvalidOperationException("RenderPipeline cannot be mutated while executing.");
    }

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
                var pass = remaining[i];
                bool ready = true;
                foreach (var input in pass.Inputs)
                {
                    if (!visited.Contains(input))
                    {
                        ready = false;
                        break;
                    }
                }

                if (!ready) continue;
                sorted.Add(pass);
                if (pass.Output is not null) visited.Add(pass.Output);
                remaining.RemoveAt(i);
            }

            if (remaining.Count == beforeCount)
                throw new InvalidOperationException(
                    "[RenderPipeline] cyclic or missing dependency detected between RenderPasses");
        }

        return sorted;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var pass in _passes) pass.Dispose();
        _passes.Clear();
        _passHandles.Clear();
    }
}

public readonly record struct RenderPassHandle(long Value);

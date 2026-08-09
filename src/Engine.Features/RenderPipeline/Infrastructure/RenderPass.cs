namespace GameEngine.Features.RenderPipeline.Infrastructure;

using Silk.NET.OpenGL;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Core.Infrastructure.Diagnostics;
using GameEngine.Features.RenderPipeline.Domain;

/// <summary>Pass 执行上下文：避免每个 Pass 重新获取 GL/Shader/Batch 引用</summary>
public readonly record struct RenderPassContext(
    GL Gl,
    IShader DefaultShader,
    SpriteBatch Batch,
    int ScreenWidth,
    int ScreenHeight,
    IFrameStatisticsSink? Statistics = null)
{
    /// <summary>自定义 Pass 直接提交 GL 绘制后调用；SpriteBatch 会自动计数。</summary>
    public void RecordDrawCall() => Statistics?.RecordDrawCall();
}

/// <summary>抽象管道节点</summary>
public abstract class RenderPass : IDisposable
{
    public string Name { get; init; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>该 Pass 写入的目标 (null = 直接写入屏幕)</summary>
    public abstract RenderTarget2D? Output { get; }

    /// <summary>该 Pass 读取的所有 RenderTarget（用于依赖排序）</summary>
    public abstract IEnumerable<RenderTarget2D> Inputs { get; }

    protected RenderPass(string name) => Name = name;

    public abstract void Execute(in RenderPassContext ctx);

    /// <summary>释放 Pass 自身拥有的 GPU 资源；外部注入的 Shader/RenderTarget 不在此释放。</summary>
    public virtual void Dispose() { }
}

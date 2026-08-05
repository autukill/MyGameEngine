namespace GameEngine.Features.RenderPipeline.Infrastructure;

using Silk.NET.OpenGL;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.RenderPipeline.Domain;

/// <summary>
/// 场景渲染 Pass：把 SceneAggregate 中所有活跃 GameInstance 的 OnDraw 提交到 SpriteBatch。
/// 可指定 RenderTarget 与 Camera。
///
/// GMS 对照：相当于 GMS 默认的 Draw 事件调度器——遍历 Room 中所有 Instance 的 Draw 事件。
/// </summary>
public sealed class SceneRenderPass : RenderPass
{
    private readonly SceneAggregate _scene;
    private readonly Camera2D _camera;
    private readonly RenderTarget2D? _target;

    public override RenderTarget2D? Output => _target;
    public override IEnumerable<RenderTarget2D> Inputs => Array.Empty<RenderTarget2D>();

    public SceneRenderPass(
        string name,
        SceneAggregate scene,
        Camera2D camera,
        RenderTarget2D? target = null)
        : base(name)
    {
        _scene = scene;
        _camera = camera;
        _target = target;
    }

    public override void Execute(in RenderPassContext ctx)
    {
        // 1. 应用 Camera 矩阵到默认 Shader
        ctx.DefaultShader.Use();
        ctx.DefaultShader.SetProjection(_camera.GetViewProjectionMatrix());

        // 2. 调用 SceneAggregate.DrawActive，由聚合根遍历实例调用 OnDraw
        ctx.Batch.Begin();
        _scene.DrawActive(ctx.Batch);
        ctx.Batch.End();
    }
}

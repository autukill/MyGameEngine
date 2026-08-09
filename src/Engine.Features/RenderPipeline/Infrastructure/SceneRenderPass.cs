namespace GameEngine.Features.RenderPipeline.Infrastructure;

using System.Numerics;
using Silk.NET.OpenGL;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.RenderPipeline.Domain;

/// <summary>
/// 场景渲染 Pass（Layer 感知版）。
///
/// 把 SceneAggregate 中所有活跃 GameInstance 按 Layer 分组提交到 SpriteBatch。
/// 支持 SceneAggregate.Background 清屏色 + 背景精灵。
/// 可指定 RenderTarget 与 Camera。
///
/// GMS 对照：相当于 GMS 默认的 Draw 事件调度器——遍历 Room 中所有 Instance 的 Draw 事件。
/// </summary>
public sealed class SceneRenderPass : RenderPass
{
    private readonly GL _gl;
    private readonly SceneAggregate _scene;
    private readonly Camera2D _camera;
    private readonly RenderTarget2D? _target;
    private readonly SceneLayerFilter _layerFilter;

    public override RenderTarget2D? Output => _target;
    public override IEnumerable<RenderTarget2D> Inputs => Array.Empty<RenderTarget2D>();

    public SceneRenderPass(
        string name,
        GL gl,
        SceneAggregate scene,
        Camera2D camera,
        RenderTarget2D? target = null,
        SceneLayerFilter? layerFilter = null)
        : base(name)
    {
        _gl = gl;
        _scene = scene;
        _camera = camera;
        _target = target;
        _layerFilter = layerFilter ?? SceneLayerFilter.All;
    }

    public override void Execute(in RenderPassContext ctx)
    {
        // 1. 应用 Scene 背景清屏色（覆盖 Pipeline 的默认 clear）
        var bg = _scene.Background;
        _gl.ClearColor(bg.ClearColor.X, bg.ClearColor.Y, bg.ClearColor.Z, bg.ClearColor.W);
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);

        // 2. 应用 Camera 矩阵
        Matrix4x4 projection = _camera.GetViewProjectionMatrix();
        ctx.Batch.ShaderResolver?.SetProjection(projection);
        ctx.DefaultShader.Use();
        ctx.DefaultShader.SetProjection(projection);

        // 3. 如果配置了背景精灵，先绘制（在 "Background" Layer 之前）
        if (bg.HasSprite)
        {
            ctx.Batch.Begin();
            DrawBackground(ctx, bg);
            ctx.Batch.End();
        }

        // 4. 调用 SceneAggregate.DrawActive（Layer 感知版——按层分组遍历实例）
        ctx.Batch.Begin();
        _scene.DrawActive(ctx.Batch, _layerFilter);
        ctx.Batch.End();
    }

    /// <summary>绘制背景精灵（Stretch 或 Tile）。</summary>
    private void DrawBackground(in RenderPassContext ctx, BackgroundConfig bg)
    {
        if (bg.TileMode == BackgroundTileMode.Stretch)
        {
            ctx.Batch.DrawSpriteStretched(
                bg.BackgroundSprite,
                subImage: 0,
                position: Vector2.Zero,
                size: new Vector2(_scene.ViewportWidth, _scene.ViewportHeight));
        }
        else if (bg.TileMode == BackgroundTileMode.Tile)
        {
            if (!ctx.Batch.TryGetSpriteMetadata(bg.BackgroundSprite, out var metadata)) return;
            float w = metadata.Size.X;
            float h = metadata.Size.Y;
            if (w <= 0f || h <= 0f) return;
            for (float y = 0; y < _scene.ViewportHeight; y += h)
            {
                for (float x = 0; x < _scene.ViewportWidth; x += w)
                {
                    ctx.Batch.DrawSprite(
                        bg.BackgroundSprite,
                        subImage: 0,
                        position: new Vector2(x, y) + metadata.Origin);
                }
            }
        }
    }
}

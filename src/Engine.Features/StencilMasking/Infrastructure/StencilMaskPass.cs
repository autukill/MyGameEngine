namespace GameEngine.Features.StencilMasking.Infrastructure;

using Silk.NET.OpenGL;
using System.Numerics;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Features.StencilMasking.Domain;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;
using GameEngine.Features.Camera.Domain;

/// <summary>
/// Stencil 遮罩 Pass。两阶段：
///   Phase A: ColorMask 关 + StencilWrite 绘制遮罩几何 (只改 Stencil Buffer)
///   Phase B: 恢复 ColorMask + 按 Mode 选择 EQUAL/NOT_EQUAL 重绘被遮罩内容
/// </summary>
public sealed class StencilMaskPass : RenderPass
{
    private readonly SceneAggregate _scene;
    private readonly Camera2D _camera;
    private readonly RenderTarget2D _output;
    private readonly SpriteBatch _batch;
    private readonly IShader _shader;
    private readonly WhiteTexture _white;

    /// <summary>当前 Stencil 配置（Mode + Ref + Mask）。外部可每帧修改。</summary>
    public StencilMaskState State { get; set; } = StencilMaskState.Default;

    private Vector2 _maskCenter;
    private float _maskRadius;
    private bool _hasDirectMask;

    private StencilMaskPassRequestedEvent? _pendingEvent;

    public override RenderTarget2D? Output => _output;
    public override IEnumerable<RenderTarget2D> Inputs => Array.Empty<RenderTarget2D>();

    public StencilMaskPass(
        string name, GL gl, SceneAggregate scene, Camera2D camera,
        RenderTarget2D output, IShader shader, WhiteTexture white) : base(name)
    {
        _scene = scene;
        _camera = camera;
        _output = output;
        _batch = new SpriteBatch(gl);
        _shader = shader;
        _white = white;
    }

    // ---------- 直接 API（Phase 1.3 Demo 兼容） ----------

    public void SetMaskCircle(Vector2 centerWorld, float radiusWorld)
    {
        _maskCenter = centerWorld;
        _maskRadius = radiusWorld;
        _hasDirectMask = true;
        _pendingEvent = null;
    }

    // ---------- 事件驱动 API（DDD + VSA 路径） ----------

    /// <summary>消费领域事件，把事件的两个 Action 回调用于本帧 Execute</summary>
    public void ApplyStencilEvent(StencilMaskPassRequestedEvent ev)
    {
        _pendingEvent = ev;
        _hasDirectMask = false;
    }

    public override void Execute(in RenderPassContext ctx)
    {
        var gl = ctx.Gl;

        // ============= Phase A: Stencil Write =============
        BlendState.ColorMaskDisabled.Apply(gl);
        DepthStencilState.StencilWrite(
            refValue: (int)State.StencilRef, mask: State.MaskBits).Apply(gl);

        _shader.Use();
        _shader.SetProjection(_camera.GetViewProjectionMatrix());

        _batch.Begin();
        if (_pendingEvent is { } ev && ev.RenderMaskShape is not null)
        {
            ev.RenderMaskShape();
        }
        else if (_hasDirectMask)
        {
            _batch.Draw(
                textureHandle: _white.Handle,
                position: _maskCenter - new Vector2(_maskRadius, _maskRadius),
                size: new Vector2(_maskRadius * 2, _maskRadius * 2),
                color: new Vector4(1, 1, 1, 1));
        }
        _batch.End();

        // ============= Phase B: Stencil Test + 重绘被遮罩内容 =============
        BlendState.AlphaBlend.Apply(gl);
        GetTestState(State).Apply(gl);

        _shader.Use();
        _shader.SetProjection(_camera.GetViewProjectionMatrix());

        if (_pendingEvent is { } ev2 && ev2.RenderMaskedContent is not null)
        {
            _batch.Begin();
            ev2.RenderMaskedContent();
            _batch.End();
        }
        else
        {
            // 直接 API 路径：用 SceneAggregate.DrawActive 重绘所有活跃实例
            // 这次只会写到 Stencil 遮罩覆盖的像素上（由 Mode 决定 Inside/Outside）
            _batch.Begin();
            _scene.DrawActive(_batch);
            _batch.End();
        }

        // 复位
        DepthStencilState.None.Apply(gl);
        BlendState.AlphaBlend.Apply(gl);

        // 事件为一次性，每帧后清理
        _pendingEvent = null;
    }

    /// <summary>根据 State.Mode 选择对应的 Stencil Test 状态</summary>
    private static DepthStencilState GetTestState(StencilMaskState state) =>
        state.Mode == StencilMaskMode.ShowOutside
            ? DepthStencilState.StencilTestNotEqual((int)state.StencilRef, state.MaskBits)
            : DepthStencilState.StencilTest((int)state.StencilRef, state.MaskBits);

    public override string ToString() =>
        $"StencilMaskPass[Mode={State.Mode} Ref={State.StencilRef} Mask=0x{State.MaskBits:X}]";

    public override void Dispose() => _batch.Dispose();
}

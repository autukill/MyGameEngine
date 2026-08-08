namespace GameEngine.Features.StencilMasking.Infrastructure;

using System.Numerics;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;
using GameEngine.Features.StencilMasking.Domain;

/// <summary>两阶段 Stencil Pass；动态路径可同时聚合多个 owner 的遮罩区域。</summary>
public sealed class StencilMaskPass : RenderPass
{
    private readonly SceneAggregate _scene;
    private readonly Camera2D _camera;
    private readonly RenderTarget2D _output;
    private readonly SpriteBatch _batch;
    private readonly IShader _shader;
    private readonly uint _whiteTextureHandle;
    private StencilMaskEffectDescriptor[] _masks = Array.Empty<StencilMaskEffectDescriptor>();
    private Vector2 _directCenter;
    private float _directRadius;
    private bool _hasDirectMask;

    public StencilMaskState State { get; set; } = StencilMaskState.Default;
    public int MaskCount => _masks.Length + (_hasDirectMask ? 1 : 0);
    public override RenderTarget2D? Output => _output;
    public override IEnumerable<RenderTarget2D> Inputs => Array.Empty<RenderTarget2D>();

    public StencilMaskPass(
        string name,
        Silk.NET.OpenGL.GL gl,
        SceneAggregate scene,
        Camera2D camera,
        RenderTarget2D output,
        IShader shader,
        WhiteTexture white,
        ISpriteResolver? spriteResolver = null) : base(name)
    {
        _scene = scene;
        _camera = camera;
        _output = output;
        _batch = new SpriteBatch(gl) { DefaultShader = shader, SpriteResolver = spriteResolver };
        _shader = shader;
        _whiteTextureHandle = white.Handle;
    }

    public StencilMaskPass(
        string name,
        Silk.NET.OpenGL.GL gl,
        SceneAggregate scene,
        Camera2D camera,
        RenderTarget2D output,
        IShader shader,
        TextureRef whiteTexture,
        ITextureResolver textureResolver,
        ISpriteResolver? spriteResolver = null) : base(name)
    {
        ArgumentNullException.ThrowIfNull(textureResolver);
        if (!textureResolver.TryResolve(whiteTexture, out var resolved))
            throw new ArgumentException($"Texture '{whiteTexture}' is not registered.", nameof(whiteTexture));
        _scene = scene;
        _camera = camera;
        _output = output;
        _batch = new SpriteBatch(gl) { DefaultShader = shader, SpriteResolver = spriteResolver };
        _shader = shader;
        _whiteTextureHandle = resolved.Handle;
    }

    /// <summary>保留给现有 VisualTests 的直接 API。</summary>
    public void SetMaskCircle(Vector2 centerWorld, float radiusWorld)
    {
        if (!float.IsFinite(radiusWorld) || radiusWorld <= 0f)
            throw new ArgumentOutOfRangeException(nameof(radiusWorld));
        _directCenter = centerWorld;
        _directRadius = radiusWorld;
        _hasDirectMask = true;
        _masks = Array.Empty<StencilMaskEffectDescriptor>();
    }

    public void UpdateMasks(IReadOnlyList<StencilMaskEffectDescriptor> masks)
    {
        ArgumentNullException.ThrowIfNull(masks);
        if (masks.Count == 0)
            throw new ArgumentException("At least one stencil mask is required.", nameof(masks));
        var state = masks[0].State;
        if (masks.Any(mask => mask.State != state))
            throw new ArgumentException("Shared stencil masks must use the same state.", nameof(masks));
        _masks = masks.ToArray();
        State = state;
        _hasDirectMask = false;
    }

    public override void Execute(in RenderPassContext context)
    {
        var gl = context.Gl;
        BlendState.ColorMaskDisabled.Apply(gl);
        DepthStencilState.StencilWrite((int)State.StencilRef, State.MaskBits).Apply(gl);
        _shader.Use();
        _shader.SetProjection(_camera.GetViewProjectionMatrix());

        _batch.Begin();
        if (_masks.Length > 0)
        {
            foreach (var mask in _masks)
                DrawMask(new Vector2(mask.Center.X, mask.Center.Y), mask.Radius);
        }
        else if (_hasDirectMask)
            DrawMask(_directCenter, _directRadius);
        _batch.End();

        BlendState.AlphaBlend.Apply(gl);
        GetTestState(State).Apply(gl);
        _shader.Use();
        _shader.SetProjection(_camera.GetViewProjectionMatrix());
        _batch.Begin();
        _scene.DrawActive(_batch);
        _batch.End();

        DepthStencilState.None.Apply(gl);
        BlendState.AlphaBlend.Apply(gl);
    }

    private void DrawMask(Vector2 center, float radius) =>
        _batch.Draw(
            _whiteTextureHandle,
            center - new Vector2(radius, radius),
            new Vector2(radius * 2f, radius * 2f),
            Vector4.One);

    private static DepthStencilState GetTestState(StencilMaskState state) =>
        state.Mode == StencilMaskMode.ShowOutside
            ? DepthStencilState.StencilTestNotEqual((int)state.StencilRef, state.MaskBits)
            : DepthStencilState.StencilTest((int)state.StencilRef, state.MaskBits);

    public override void Dispose() => _batch.Dispose();
}

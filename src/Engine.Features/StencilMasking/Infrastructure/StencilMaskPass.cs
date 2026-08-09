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
    private readonly IShader _sceneShader;
    private readonly StencilMaskShader _maskShader;
    private readonly uint _whiteTextureHandle;
    private readonly SceneLayerFilter _layerFilter;
    private StencilMaskEffectDescriptor[] _masks = Array.Empty<StencilMaskEffectDescriptor>();
    private Vector2 _directCenter;
    private float _directRadius;
    private bool _hasDirectMask;
    private bool _hasSpriteMasks;
    private int _maskCount;

    public StencilMaskState State { get; set; } = StencilMaskState.Default;
    public int MaskCount => _maskCount + (_hasDirectMask ? 1 : 0);
    public override RenderTarget2D? Output => _output;
    public override IEnumerable<RenderTarget2D> Inputs => Array.Empty<RenderTarget2D>();

    public StencilMaskPass(
        string name,
        Silk.NET.OpenGL.GL gl,
        SceneAggregate scene,
        Camera2D camera,
        RenderTarget2D output,
        IShader sceneShader,
        StencilMaskShader maskShader,
        WhiteTexture white,
        ISpriteResolver? spriteResolver = null,
        IShaderResolver? shaderResolver = null,
        SceneLayerFilter? layerFilter = null) : base(name)
    {
        _scene = scene;
        _camera = camera;
        _output = output;
        _batch = new SpriteBatch(gl)
        {
            DefaultShader = sceneShader,
            SpriteResolver = spriteResolver,
            ShaderResolver = shaderResolver
        };
        _sceneShader = sceneShader;
        _maskShader = maskShader;
        _whiteTextureHandle = white.Handle;
        _layerFilter = layerFilter ?? SceneLayerFilter.All;
    }

    public StencilMaskPass(
        string name,
        Silk.NET.OpenGL.GL gl,
        SceneAggregate scene,
        Camera2D camera,
        RenderTarget2D output,
        IShader sceneShader,
        StencilMaskShader maskShader,
        TextureRef whiteTexture,
        ITextureResolver textureResolver,
        ISpriteResolver? spriteResolver = null,
        IShaderResolver? shaderResolver = null,
        SceneLayerFilter? layerFilter = null) : base(name)
    {
        ArgumentNullException.ThrowIfNull(textureResolver);
        if (!textureResolver.TryResolve(whiteTexture, out var resolved))
            throw new ArgumentException($"Texture '{whiteTexture}' is not registered.", nameof(whiteTexture));
        _scene = scene;
        _camera = camera;
        _output = output;
        _batch = new SpriteBatch(gl)
        {
            DefaultShader = sceneShader,
            SpriteResolver = spriteResolver,
            ShaderResolver = shaderResolver
        };
        _sceneShader = sceneShader;
        _maskShader = maskShader;
        _whiteTextureHandle = resolved.Handle;
        _layerFilter = layerFilter ?? SceneLayerFilter.All;
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
        _hasSpriteMasks = false;
        _maskCount = 0;
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
        _maskCount = 0;
        _hasSpriteMasks = false;
        foreach (StencilMaskEffectDescriptor mask in _masks)
        {
            _maskCount += mask.GeometryCount;
            for (int i = 0; i < mask.GeometryCount; i++)
            {
                if (mask.GetGeometry(i).Kind == StencilMaskGeometryKind.SpriteAlpha)
                    _hasSpriteMasks = true;
            }
        }
        State = state;
        _hasDirectMask = false;
    }

    public override void Execute(in RenderPassContext context)
    {
        _batch.Statistics = context.Statistics;
        var gl = context.Gl;
        // Dynamic effect targets must remain transparent outside the stencil area.
        // Do not inherit a clear color left behind by a preceding scene pass.
        gl.ClearColor(0f, 0f, 0f, 0f);
        gl.Clear((uint)(
            Silk.NET.OpenGL.ClearBufferMask.ColorBufferBit |
            Silk.NET.OpenGL.ClearBufferMask.StencilBufferBit));
        BlendState.ColorMaskDisabled.Apply(gl);
        DepthStencilState.StencilWrite((int)State.StencilRef, State.MaskBits).Apply(gl);
        DrawMaskGeometry();

        BlendState.AlphaBlend.Apply(gl);
        GetTestState(State).Apply(gl);
        Matrix4x4 projection = _camera.GetViewProjectionMatrix();
        _batch.ShaderResolver?.SetProjection(projection);
        _sceneShader.Use();
        _sceneShader.SetProjection(projection);
        _batch.DefaultShader = _sceneShader;
        _batch.Begin();
        if (_camera.TryGetVisibleWorldBounds(out var viewBounds))
            _scene.DrawActive(_batch, _layerFilter, viewBounds);
        else
            _scene.DrawActive(_batch, _layerFilter);
        _batch.End();

        DepthStencilState.None.Apply(gl);
        BlendState.AlphaBlend.Apply(gl);
    }

    private void DrawMaskGeometry()
    {
        var projection = _camera.GetViewProjectionMatrix();
        _batch.DefaultShader = _maskShader;
        _maskShader.SetProjection(projection);
        _maskShader.SetGeometry(StencilMaskGeometryKind.Circle);
        _batch.Begin();
        if (_masks.Length > 0)
        {
            foreach (var mask in _masks)
            {
                for (int i = 0; i < mask.GeometryCount; i++)
                {
                    StencilMaskGeometry geometry = mask.GetGeometry(i);
                    if (geometry.Kind == StencilMaskGeometryKind.Circle)
                    {
                        DrawCircle(
                            new Vector2(geometry.Center.X, geometry.Center.Y),
                            geometry.Radius);
                    }
                }
            }
        }
        else if (_hasDirectMask)
            DrawCircle(_directCenter, _directRadius);
        _batch.End();

        if (!_hasSpriteMasks)
            return;

        _maskShader.SetGeometry(StencilMaskGeometryKind.SpriteAlpha);
        _batch.Begin();
        float currentCutoff = float.NaN;
        foreach (var mask in _masks)
        {
            for (int i = 0; i < mask.GeometryCount; i++)
            {
                StencilMaskGeometry geometry = mask.GetGeometry(i);
                if (geometry.Kind != StencilMaskGeometryKind.SpriteAlpha) continue;
                if (geometry.AlphaCutoff != currentCutoff)
                {
                    _batch.Flush();
                    _maskShader.SetGeometry(StencilMaskGeometryKind.SpriteAlpha, geometry.AlphaCutoff);
                    currentCutoff = geometry.AlphaCutoff;
                }
                _batch.DrawSpriteCommand(new SpriteDrawCommand(
                    geometry.Sprite,
                    geometry.SubImage,
                    new Vector2(geometry.Transform.Position.X, geometry.Transform.Position.Y),
                    new Vector2(geometry.Transform.Scale.X, geometry.Transform.Scale.Y),
                    geometry.Transform.Rotation,
                    Vector4.One));
            }
        }
        _batch.End();
    }

    private void DrawCircle(Vector2 center, float radius) =>
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

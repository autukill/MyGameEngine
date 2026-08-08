namespace GameEngine.Features.StencilMasking.Infrastructure;

using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;
using GameEngine.Features.StencilMasking.Domain;

/// <summary>把类型化 Stencil owner 集合装配为共享 Stencil Pass 和可选 Bloom Pass。</summary>
public sealed class StencilMaskEffectFactory : IRenderEffectFactory
{
    private readonly Silk.NET.OpenGL.GL _gl;
    private readonly SceneAggregate _scene;
    private readonly Camera2D _camera;
    private readonly IShader _spriteShader;
    private readonly TextureRef _whiteTexture;
    private readonly ITextureResolver _textures;
    private readonly ISpriteResolver? _sprites;
    private readonly PostProcessShader? _bloomShader;

    public string Kind => StencilMaskEffectDescriptor.EffectKind;

    public StencilMaskEffectFactory(
        Silk.NET.OpenGL.GL gl,
        SceneAggregate scene,
        Camera2D camera,
        IShader spriteShader,
        TextureRef whiteTexture,
        ITextureResolver textures,
        ISpriteResolver? sprites = null,
        PostProcessShader? bloomShader = null)
    {
        _gl = gl;
        _scene = scene;
        _camera = camera;
        _spriteShader = spriteShader;
        _whiteTexture = whiteTexture;
        _textures = textures;
        _sprites = sprites;
        _bloomShader = bloomShader;
    }

    public void Validate(
        RenderEffectKey key,
        IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners) =>
        StencilMaskEffectPolicy.ValidateAndOrder(key, owners);

    public IRenderEffectRuntime Create(
        in RenderEffectBuildContext context,
        RenderEffectKey key,
        IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners)
    {
        var descriptors = StencilMaskEffectPolicy.ValidateAndOrder(key, owners);
        RenderTargetLease? maskLease = null;
        RenderTargetLease? bloomLease = null;
        StencilMaskPass? stencilPass = null;
        PostProcessPass? bloomPass = null;
        try
        {
            maskLease = context.Targets.Rent(new RenderTargetDescriptor(
                context.Width,
                context.Height,
                depthStencilFormat: RenderTargetDepthStencilFormat.Depth24Stencil8));
            stencilPass = new StencilMaskPass(
                $"StencilMask:{key.Slot}",
                _gl,
                _scene,
                _camera,
                maskLease.Target,
                _spriteShader,
                _whiteTexture,
                _textures,
                _sprites);
            stencilPass.UpdateMasks(descriptors);

            var passes = new List<RenderPass> { stencilPass };
            RenderTarget2D compositeTarget = maskLease.Target;
            BlendState compositeBlend = BlendState.AlphaBlend;

            if (_bloomShader is not null)
            {
                bloomLease = context.Targets.Rent(new RenderTargetDescriptor(
                    context.Width,
                    context.Height));
                _bloomShader.SetTextureSize(context.Width, context.Height);
                bloomPass = new PostProcessPass(
                    $"StencilBloom:{key.Slot}",
                    _gl,
                    _bloomShader,
                    maskLease.Target,
                    bloomLease.Target);
                passes.Add(bloomPass);
                compositeTarget = bloomLease.Target;
                compositeBlend = BlendState.Additive;
            }

            return new StencilMaskEffectRuntime(
                key,
                stencilPass,
                passes,
                new[]
                {
                    new RenderEffectCompositeSource(
                        compositeTarget,
                        ViewportRect.FullScreen,
                        compositeBlend)
                },
                maskLease,
                bloomLease);
        }
        catch
        {
            bloomPass?.Dispose();
            stencilPass?.Dispose();
            bloomLease?.Dispose();
            maskLease?.Dispose();
            throw;
        }
    }

    private sealed class StencilMaskEffectRuntime : IRenderEffectRuntime
    {
        private readonly StencilMaskPass _stencilPass;
        private RenderTargetLease? _maskLease;
        private RenderTargetLease? _bloomLease;

        public RenderEffectKey Key { get; }
        public IReadOnlyList<RenderPass> Passes { get; }
        public IReadOnlyList<RenderEffectCompositeSource> CompositeSources { get; }

        public StencilMaskEffectRuntime(
            RenderEffectKey key,
            StencilMaskPass stencilPass,
            IReadOnlyList<RenderPass> passes,
            IReadOnlyList<RenderEffectCompositeSource> compositeSources,
            RenderTargetLease maskLease,
            RenderTargetLease? bloomLease)
        {
            Key = key;
            _stencilPass = stencilPass;
            Passes = passes;
            CompositeSources = compositeSources;
            _maskLease = maskLease;
            _bloomLease = bloomLease;
        }

        public void UpdateOwners(
            IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners) =>
            _stencilPass.UpdateMasks(StencilMaskEffectPolicy.ValidateAndOrder(Key, owners));

        public void Dispose()
        {
            Interlocked.Exchange(ref _bloomLease, null)?.Dispose();
            Interlocked.Exchange(ref _maskLease, null)?.Dispose();
        }
    }
}

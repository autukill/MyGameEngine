namespace GameEngine.Features.StencilMasking.Infrastructure;

using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;
using GameEngine.Features.StencilMasking.Domain;

/// <summary>把类型化 Stencil owner 集合装配为共享 Stencil Pass。</summary>
public sealed class StencilMaskEffectFactory : IRenderEffectFactory
{
    private readonly Silk.NET.OpenGL.GL _gl;
    private readonly SceneAggregate _scene;
    private readonly Camera2D _camera;
    private readonly IShader _spriteShader;
    private readonly StencilMaskShader _maskShader;
    private readonly TextureRef _whiteTexture;
    private readonly ITextureResolver _textures;
    private readonly ISpriteResolver _sprites;
    private readonly IShaderResolver? _shaders;

    public string Kind => StencilMaskEffectDescriptor.EffectKind;

    public StencilMaskEffectFactory(
        Silk.NET.OpenGL.GL gl,
        SceneAggregate scene,
        Camera2D camera,
        IShader spriteShader,
        StencilMaskShader maskShader,
        TextureRef whiteTexture,
        ITextureResolver textures,
        ISpriteResolver sprites,
        IShaderResolver? shaders = null)
    {
        _gl = gl;
        _scene = scene;
        _camera = camera;
        _spriteShader = spriteShader;
        _maskShader = maskShader ?? throw new ArgumentNullException(nameof(maskShader));
        _whiteTexture = whiteTexture;
        _textures = textures;
        _sprites = sprites ?? throw new ArgumentNullException(nameof(sprites));
        _shaders = shaders;
    }

    public RenderEffectPlan Plan(
        RenderEffectKey key,
        IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners)
    {
        var descriptors = StencilMaskEffectPolicy.ValidateAndOrder(key, owners);
        ValidateSpriteMasks(descriptors);
        return new RenderEffectPlan(
            key,
            inputSurfaces: null,
            outputSurfaces: new[]
            {
                RenderSurfaceSpec.Ldr(StencilMaskEffectDescriptor.MaskOutput(key))
            });
    }

    public IRenderEffectRuntime Create(
        in RenderEffectBuildContext context,
        RenderEffectKey key,
        IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners)
    {
        var descriptors = StencilMaskEffectPolicy.ValidateAndOrder(key, owners);
        ValidateSpriteMasks(descriptors);
        RenderTargetLease? maskLease = null;
        StencilMaskPass? stencilPass = null;
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
                _maskShader,
                _whiteTexture,
                _textures,
                _sprites,
                _shaders);
            stencilPass.UpdateMasks(descriptors);

            var passes = new List<RenderPass> { stencilPass };

            return new StencilMaskEffectRuntime(
                key,
                stencilPass,
                passes,
                new[]
                {
                    new RenderEffectOutput(
                        StencilMaskEffectDescriptor.MaskOutput(key),
                        maskLease.Target)
                },
                maskLease);
        }
        catch
        {
            stencilPass?.Dispose();
            maskLease?.Dispose();
            throw;
        }
    }

    private void ValidateSpriteMasks(IEnumerable<StencilMaskEffectDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            for (int i = 0; i < descriptor.GeometryCount; i++)
            {
                StencilMaskGeometry geometry = descriptor.GetGeometry(i);
                if (geometry.Kind == StencilMaskGeometryKind.SpriteAlpha &&
                    !_sprites.TryGetMetadata(geometry.Sprite, out _))
                {
                    throw new InvalidOperationException(
                        $"Stencil mask Sprite '{geometry.Sprite}' is not registered.");
                }
            }
        }
    }

    private sealed class StencilMaskEffectRuntime : IRenderEffectRuntime
    {
        private readonly StencilMaskPass _stencilPass;
        private RenderTargetLease? _maskLease;

        public RenderEffectKey Key { get; }
        public IReadOnlyList<RenderPass> Passes { get; }
        public IReadOnlyList<RenderEffectOutput> Outputs { get; }

        public StencilMaskEffectRuntime(
            RenderEffectKey key,
            StencilMaskPass stencilPass,
            IReadOnlyList<RenderPass> passes,
            IReadOnlyList<RenderEffectOutput> outputs,
            RenderTargetLease maskLease)
        {
            Key = key;
            _stencilPass = stencilPass;
            Passes = passes;
            Outputs = outputs;
            _maskLease = maskLease;
        }

        public void UpdateOwners(
            IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners) =>
            _stencilPass.UpdateMasks(StencilMaskEffectPolicy.ValidateAndOrder(Key, owners));

        public void Dispose()
        {
            Interlocked.Exchange(ref _maskLease, null)?.Dispose();
        }
    }
}

namespace GameEngine.Features.ToneMapping.Infrastructure;

using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;
using GameEngine.Features.ToneMapping.Domain;
using Silk.NET.OpenGL;

public sealed class ToneMappingEffectFactory : IRenderEffectFactory
{
    private readonly GL _gl;
    private readonly ToneMappingShader _shader;

    public string Kind => ToneMappingEffectDescriptor.EffectKind;

    public ToneMappingEffectFactory(GL gl, ToneMappingShader shader)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _shader = shader ?? throw new ArgumentNullException(nameof(shader));
    }

    public RenderEffectPlan Plan(
        RenderEffectKey key,
        IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners)
    {
        var configuration = ToneMappingEffectPolicy.ValidateAndGetConfiguration(key, owners);
        var inputs = new List<RenderSurfaceSpec>
        {
            RenderSurfaceSpec.Hdr(configuration.Source)
        };
        if (configuration.BloomSource is { } bloom)
            inputs.Add(RenderSurfaceSpec.Hdr(bloom));
        return new RenderEffectPlan(
            key,
            inputs,
            new[] { RenderSurfaceSpec.Ldr(ToneMappingEffectDescriptor.ColorOutput(key)) });
    }

    public IRenderEffectRuntime Create(
        in RenderEffectBuildContext context,
        RenderEffectKey key,
        IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners)
    {
        var configuration = ToneMappingEffectPolicy.ValidateAndGetConfiguration(key, owners);
        RenderTarget2D scene = context.Surfaces.Resolve(configuration.Source);
        RenderTarget2D? bloom = configuration.BloomSource is { } bloomKey
            ? context.Surfaces.Resolve(bloomKey)
            : null;
        RenderTargetLease? output = null;
        ToneMappingPass? pass = null;
        try
        {
            output = context.Targets.Rent(new RenderTargetDescriptor(
                context.Width,
                context.Height,
                RenderTargetColorFormat.Rgba8));
            pass = new ToneMappingPass(
                $"ToneMapping:{key.Slot}",
                _gl,
                scene,
                bloom,
                output.Target,
                _shader,
                configuration.Settings);
            return new ToneMappingEffectRuntime(key, configuration, pass, output);
        }
        catch
        {
            pass?.Dispose();
            output?.Dispose();
            throw;
        }
    }

    private sealed class ToneMappingEffectRuntime : IRenderEffectRuntime
    {
        private readonly ToneMappingPass _pass;
        private ToneMappingEffectPolicy.Configuration _configuration;
        private RenderTargetLease? _output;

        public RenderEffectKey Key { get; }
        public IReadOnlyList<RenderPass> Passes { get; }
        public IReadOnlyList<RenderEffectCompositeSource> CompositeSources { get; }
        public IReadOnlyList<RenderEffectOutput> Outputs { get; }

        public ToneMappingEffectRuntime(
            RenderEffectKey key,
            ToneMappingEffectPolicy.Configuration configuration,
            ToneMappingPass pass,
            RenderTargetLease output)
        {
            Key = key;
            _configuration = configuration;
            _pass = pass;
            _output = output;
            Passes = new[] { pass };
            CompositeSources = new[]
            {
                new RenderEffectCompositeSource(
                    output.Target,
                    ViewportRect.FullScreen,
                    BlendState.Opaque,
                    Order: 0)
            };
            Outputs = new[]
            {
                new RenderEffectOutput(
                    ToneMappingEffectDescriptor.ColorOutput(key),
                    output.Target)
            };
        }

        public void UpdateOwners(
            IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners)
        {
            _configuration = ToneMappingEffectPolicy.ValidateAndGetConfiguration(Key, owners);
            _pass.UpdateSettings(_configuration.Settings);
        }

        public void Dispose() => Interlocked.Exchange(ref _output, null)?.Dispose();
    }
}

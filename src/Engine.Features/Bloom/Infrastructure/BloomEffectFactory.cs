namespace GameEngine.Features.Bloom.Infrastructure;

using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.Bloom.Domain;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;
using Silk.NET.OpenGL;

public sealed class BloomEffectFactory : IRenderEffectFactory
{
    private readonly GL _gl;
    private readonly BloomExtractShader _extractShader;
    private readonly GaussianBlurShader _blurShader;

    public string Kind => BloomEffectDescriptor.EffectKind;

    public BloomEffectFactory(
        GL gl,
        BloomExtractShader extractShader,
        GaussianBlurShader blurShader)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _extractShader = extractShader ?? throw new ArgumentNullException(nameof(extractShader));
        _blurShader = blurShader ?? throw new ArgumentNullException(nameof(blurShader));
    }

    public RenderEffectPlan Plan(
        RenderEffectKey key,
        IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners)
    {
        var configuration = BloomEffectPolicy.ValidateAndGetConfiguration(key, owners);
        return new RenderEffectPlan(
            key,
            new[] { configuration.Source },
            new[] { BloomEffectDescriptor.GlowOutput(key) });
    }

    public IRenderEffectRuntime Create(
        in RenderEffectBuildContext context,
        RenderEffectKey key,
        IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners)
    {
        var configuration = BloomEffectPolicy.ValidateAndGetConfiguration(key, owners);
        BloomSettings settings = configuration.Settings;
        RenderTarget2D source = context.Surfaces.Resolve(configuration.Source);
        var size = BloomPass.CalculateTargetSize(context.Width, context.Height, settings.Resolution);
        var descriptor = new RenderTargetDescriptor(size.Width, size.Height);
        RenderTargetLease? bright = null;
        RenderTargetLease? ping = null;
        RenderTargetLease? pong = null;
        BloomPass? pass = null;
        try
        {
            bright = context.Targets.Rent(descriptor);
            ping = context.Targets.Rent(descriptor);
            pong = context.Targets.Rent(descriptor);
            pass = new BloomPass(
                $"Bloom:{key.Slot}", _gl, source,
                bright.Target, ping.Target, pong.Target,
                _extractShader, _blurShader, settings);
            return new BloomEffectRuntime(
                key, configuration, pass, bright, ping, pong);
        }
        catch
        {
            pass?.Dispose();
            pong?.Dispose();
            ping?.Dispose();
            bright?.Dispose();
            throw;
        }
    }

    private sealed class BloomEffectRuntime : IRenderEffectRuntime
    {
        private readonly BloomPass _pass;
        private BloomEffectPolicy.Configuration _configuration;
        private RenderTargetLease? _bright;
        private RenderTargetLease? _ping;
        private RenderTargetLease? _pong;

        public RenderEffectKey Key { get; }
        public IReadOnlyList<RenderPass> Passes { get; }
        public IReadOnlyList<RenderEffectCompositeSource> CompositeSources { get; }
        public IReadOnlyList<RenderEffectOutput> Outputs { get; }

        public BloomEffectRuntime(
            RenderEffectKey key,
            BloomEffectPolicy.Configuration configuration,
            BloomPass pass,
            RenderTargetLease bright,
            RenderTargetLease ping,
            RenderTargetLease pong)
        {
            Key = key;
            _configuration = configuration;
            _pass = pass;
            _bright = bright;
            _ping = ping;
            _pong = pong;
            Passes = new[] { pass };
            CompositeSources = new[]
            {
                new RenderEffectCompositeSource(
                    pong.Target, ViewportRect.FullScreen, BlendState.Additive)
            };
            Outputs = new[]
            {
                new RenderEffectOutput(BloomEffectDescriptor.GlowOutput(key), pong.Target)
            };
        }

        public bool RequiresRebuild(
            IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners) =>
            BloomEffectPolicy.ValidateAndGetConfiguration(Key, owners).Settings.Resolution !=
            _configuration.Settings.Resolution;

        public void UpdateOwners(
            IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners)
        {
            _configuration = BloomEffectPolicy.ValidateAndGetConfiguration(Key, owners);
            _pass.UpdateSettings(_configuration.Settings);
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _pong, null)?.Dispose();
            Interlocked.Exchange(ref _ping, null)?.Dispose();
            Interlocked.Exchange(ref _bright, null)?.Dispose();
        }
    }
}

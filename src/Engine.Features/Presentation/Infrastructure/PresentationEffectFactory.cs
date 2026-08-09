namespace GameEngine.Features.Presentation.Infrastructure;

using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.Presentation.Domain;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;
using Silk.NET.OpenGL;

public sealed class PresentationEffectFactory : IRenderEffectFactory
{
    private readonly GL _gl;
    private readonly IShader _blitShader;
    private readonly SpriteBatch _batch;

    public string Kind => PresentSurfaceDescriptor.EffectKind;

    public PresentationEffectFactory(GL gl, IShader blitShader, SpriteBatch batch)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _blitShader = blitShader ?? throw new ArgumentNullException(nameof(blitShader));
        _batch = batch ?? throw new ArgumentNullException(nameof(batch));
    }

    public RenderEffectPlan Plan(
        RenderEffectKey key,
        IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners)
    {
        var entries = PresentSurfacePolicy.ValidateOrderAndDeduplicate(key, owners);
        return new RenderEffectPlan(
            key,
            entries.Select(entry => entry.Source)
                .Distinct()
                .Select(RenderSurfaceSpec.Ldr),
            outputSurfaces: null);
    }

    public IRenderEffectRuntime Create(
        in RenderEffectBuildContext context,
        RenderEffectKey key,
        IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners)
    {
        var entries = PresentSurfacePolicy.ValidateOrderAndDeduplicate(key, owners);
        var resolved = entries
            .Select(entry => entry.Source)
            .Distinct()
            .ToDictionary(source => source, context.Surfaces.Resolve);
        var pass = new ViewportCompositorPass(
            $"Presentation:{key.Slot}", _gl, _blitShader, _batch)
        {
            ClearBeforeDraw = true
        };
        var runtime = new PresentationRuntime(key, pass, resolved);
        runtime.UpdateOwners(owners);
        return runtime;
    }

    private sealed class PresentationRuntime : IRenderEffectRuntime
    {
        private readonly ViewportCompositorPass _pass;
        private readonly IReadOnlyDictionary<RenderSurfaceKey, RenderTarget2D> _resolved;

        public RenderEffectKey Key { get; }
        public IReadOnlyList<RenderPass> Passes { get; }
        public IReadOnlyList<RenderEffectOutput> Outputs { get; } =
            Array.Empty<RenderEffectOutput>();

        public PresentationRuntime(
            RenderEffectKey key,
            ViewportCompositorPass pass,
            IReadOnlyDictionary<RenderSurfaceKey, RenderTarget2D> resolved)
        {
            Key = key;
            _pass = pass;
            _resolved = resolved;
            Passes = new[] { pass };
        }

        public void UpdateOwners(
            IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners)
        {
            var entries = PresentSurfacePolicy.ValidateOrderAndDeduplicate(Key, owners);
            _pass.ClearSources();
            foreach (var entry in entries)
                _pass.AddSource(
                    _resolved[entry.Source],
                    entry.Viewport,
                    ToBlendState(entry.Blend),
                    entry.Layer);
        }

        public void Dispose() { }

        private static BlendState ToBlendState(PresentationBlendMode blend) => blend switch
        {
            PresentationBlendMode.Opaque => BlendState.Opaque,
            PresentationBlendMode.AlphaBlend => BlendState.AlphaBlend,
            PresentationBlendMode.Additive => BlendState.Additive,
            _ => throw new ArgumentOutOfRangeException(nameof(blend))
        };
    }
}

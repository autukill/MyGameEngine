namespace GameEngine.VisualRegressionTests;

using System.Numerics;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Features.Bloom.Application;
using GameEngine.Features.Bloom.Domain;
using GameEngine.Features.Bloom.Infrastructure;
using GameEngine.Features.Presentation.Application;
using GameEngine.Features.Presentation.Domain;
using GameEngine.Features.Presentation.Infrastructure;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;
using GameEngine.Features.ToneMapping.Application;
using GameEngine.Features.ToneMapping.Domain;
using GameEngine.Features.ToneMapping.Infrastructure;
using GameEngine.Testing.Visual;
using Silk.NET.OpenGL;

internal sealed class MultiRenderViewLifecycleScenario : IVisualRegressionScenario
{
    private static readonly PixelComparisonOptions HdrTolerance = new(
        SoftChannelDelta: 3,
        HardChannelDelta: 12,
        MaximumDifferentPixelRatio: 0.005);
    private MultiRenderViewLifecycleFixture? _fixture;

    public string Name => "multi-render-view-lifecycle";
    public int Width => 400;
    public int Height => 300;
    public int FrameCount => 4;
    public IReadOnlyList<VisualCheckpoint> Checkpoints { get; } = new[]
    {
        new VisualCheckpoint(0, "active", HdrTolerance),
        new VisualCheckpoint(1, "resized-active", HdrTolerance),
        new VisualCheckpoint(2, "observer-released", HdrTolerance),
        new VisualCheckpoint(3, "all-views-released")
    };

    public void Initialize(EngineWindow window) =>
        _fixture = new MultiRenderViewLifecycleFixture(window.Graphics.Gl, 320, 240);

    public void AdvanceAndDraw(int frameIndex, double fixedDeltaTime)
    {
        if (frameIndex == 1) _fixture!.Resize(Width, Height);
        if (frameIndex == 2) _fixture!.ReleaseObserver();
        if (frameIndex == 3) _fixture!.ReleaseMain();
        _fixture!.StepAndDraw(fixedDeltaTime);

        (int effects, int leases) = frameIndex switch
        {
            0 or 1 => (4, 5),
            2 => (3, 4),
            _ => (1, 0)
        };
        if (_fixture.ActiveEffectCount != effects || _fixture.LeasedTargetCount != leases)
            throw new InvalidOperationException(
                $"Multi-View frame {frameIndex} expected {effects} effects and " +
                $"{leases} leases, found {_fixture.ActiveEffectCount} and " +
                $"{_fixture.LeasedTargetCount}.");

        if (frameIndex == 0)
            _fixture.AssertActiveDescriptors(
                mainWidth: 160,
                mainHeight: 240,
                observerWidth: 120,
                observerHeight: 180);
        if (frameIndex == 1)
        {
            _fixture.AssertActiveDescriptors(
                mainWidth: 200,
                mainHeight: 300,
                observerWidth: 150,
                observerHeight: 225);
            if (_fixture.TotalTargetCount != 5)
                throw new InvalidOperationException(
                    "Multi-View resize must replace all five old effect targets.");
        }
        if (frameIndex == 3 &&
            _fixture.AvailableTargetCount != _fixture.TotalTargetCount)
            throw new InvalidOperationException(
                "Releasing both Views must leave no cached target active; found " +
                $"total={_fixture.TotalTargetCount}, " +
                $"leased={_fixture.LeasedTargetCount}, " +
                $"available={_fixture.AvailableTargetCount}.");
    }

    public void Dispose() => _fixture?.Dispose();
}

internal sealed class MultiRenderViewLifecycleFixture : IDisposable
{
    private static readonly RenderSurfaceKey BackgroundSurface =
        new("regression", "multi-view", "background");
    private static readonly RenderSurfaceKey ObserverScene =
        new("scene-view", "observer", "color");
    private static readonly RenderEffectKey MainBloomKey = BloomEffectDescriptor.DefaultKey;
    private static readonly RenderEffectKey MainToneKey = ToneMappingEffectDescriptor.DefaultKey;
    private static readonly RenderEffectKey ObserverToneKey =
        new(ToneMappingEffectDescriptor.EffectKind, "observer");

    private readonly GL _gl;
    private readonly SpriteShader _spriteShader;
    private readonly BlitShader _blitShader;
    private readonly BloomExtractShader _extractShader;
    private readonly GaussianBlurShader _blurShader;
    private readonly ToneMappingShader _toneShader;
    private readonly SpriteBatch _batch;
    private readonly SceneAggregate _scene;
    private readonly RenderTarget2D _backgroundTarget;
    private readonly RenderTarget2D _mainTarget;
    private readonly RenderTarget2D _observerTarget;
    private readonly RenderTargetPool _pool;
    private readonly RenderPipeline _pipeline;
    private readonly ScenePipelineBuilder _builder;
    private readonly ViewEffectsOwner _mainOwner;
    private readonly ViewEffectsOwner _observerOwner;
    private int _screenWidth;
    private int _screenHeight;
    private bool _mainReleased;
    private bool _observerReleased;
    private bool _disposed;

    public int ActiveEffectCount => _builder.ActiveEffectCount;
    public int TotalTargetCount => _pool.CaptureDiagnostics().TotalCount;
    public int LeasedTargetCount => _pool.CaptureDiagnostics().LeasedCount;
    public int AvailableTargetCount => _pool.CaptureDiagnostics().AvailableCount;

    public MultiRenderViewLifecycleFixture(GL gl, int width, int height)
    {
        _gl = gl;
        _screenWidth = width;
        _screenHeight = height;
        _spriteShader = new SpriteShader(gl);
        _blitShader = new BlitShader(gl);
        _extractShader = new BloomExtractShader(gl);
        _blurShader = new GaussianBlurShader(gl);
        _toneShader = new ToneMappingShader(gl);
        _batch = new SpriteBatch(gl) { DefaultShader = _spriteShader };

        var (mainWidth, mainHeight, observerWidth, observerHeight) =
            ResolveViewSizes(width, height);
        _backgroundTarget = new RenderTarget2D(gl, new RenderTargetDescriptor(
            width, height, RenderTargetColorFormat.Rgba8));
        _mainTarget = CreateHdrTarget(gl, mainWidth, mainHeight);
        _observerTarget = CreateHdrTarget(gl, observerWidth, observerHeight);

        _scene = new SceneAggregate("multi-render-view-lifecycle");
        _scene.Add(new SurfacePresentationOwner(
            _scene.RaiseEvent,
            BackgroundSurface,
            layer: -100,
            blend: PresentationBlendMode.Opaque));
        _mainOwner = _scene.Add(new ViewEffectsOwner(
            _scene.RaiseEvent,
            RenderSurfaceKey.SceneColor,
            MainToneKey,
            ViewportRect.LeftHalf,
            bloomKey: MainBloomKey));
        _observerOwner = _scene.Add(new ViewEffectsOwner(
            _scene.RaiseEvent,
            ObserverScene,
            ObserverToneKey,
            ViewportRect.RightHalf));

        _pool = new RenderTargetPool(gl);
        _pipeline = new RenderPipeline(gl, width, height);
        _pipeline.AddPass(new SolidColorPass(
            "Regression.Background", gl, _backgroundTarget, new Vector4(0.008f, 0.012f, 0.02f, 1f)));
        _pipeline.AddPass(new SolidColorPass(
            "Regression.MainView", gl, _mainTarget, new Vector4(2.8f, 0.08f, 0.03f, 1f)));
        _pipeline.AddPass(new SolidColorPass(
            "Regression.ObserverView", gl, _observerTarget, new Vector4(0.03f, 0.7f, 2.4f, 1f)));

        _builder = new ScenePipelineBuilder(
            _pipeline, _pool, mainWidth, mainHeight);
        _builder.RegisterRootSurface(BackgroundSurface, _backgroundTarget);
        _builder.RegisterRootSurface(
            RenderSurfaceKey.SceneColor,
            _mainTarget,
            RenderSurfaceEncoding.Linear);
        _builder.RegisterRootSurface(
            ObserverScene,
            _observerTarget,
            RenderSurfaceEncoding.Linear);
        _builder.RegisterFactory(new BloomEffectFactory(gl, _extractShader, _blurShader));
        _builder.RegisterFactory(new ToneMappingEffectFactory(gl, _toneShader));
        _builder.RegisterFactory(new PresentationEffectFactory(gl, _blitShader, _batch));
        _builder.ApplyEvents(_scene.DrainUncommittedEvents());
    }

    public void Resize(int width, int height)
    {
        _screenWidth = width;
        _screenHeight = height;
        var (mainWidth, mainHeight, observerWidth, observerHeight) =
            ResolveViewSizes(width, height);
        _backgroundTarget.Resize(width, height);
        _mainTarget.Resize(mainWidth, mainHeight);
        _observerTarget.Resize(observerWidth, observerHeight);
        _pipeline.Resize(width, height);
        _builder.Resize(mainWidth, mainHeight);
    }

    public void ReleaseObserver()
    {
        if (_observerReleased) return;
        _observerReleased = true;
        _scene.Destroy(_observerOwner.Id);
    }

    public void ReleaseMain()
    {
        if (_mainReleased) return;
        _mainReleased = true;
        _scene.Destroy(_mainOwner.Id);
    }

    public void StepAndDraw(double fixedDeltaTime)
    {
        _scene.PerformStep(fixedDeltaTime);
        _builder.ApplyEvents(_scene.DrainUncommittedEvents());
        _pipeline.Execute(new RenderPassContext(
            _gl, _spriteShader, _batch, _screenWidth, _screenHeight));
    }

    public void AssertActiveDescriptors(
        int mainWidth,
        int mainHeight,
        int observerWidth,
        int observerHeight)
    {
        RenderTargetDescriptor[] descriptors = _pool.CaptureDiagnostics()
            .ActiveLeases
            .Select(lease => lease.Descriptor)
            .ToArray();
        int bloomWidth = (mainWidth + 1) / 2;
        int bloomHeight = (mainHeight + 1) / 2;
        RequireDescriptorCount(
            descriptors,
            new RenderTargetDescriptor(
                bloomWidth,
                bloomHeight,
                RenderTargetColorFormat.Rgba16Float),
            expected: 3);
        RequireDescriptorCount(
            descriptors,
            new RenderTargetDescriptor(
                mainWidth,
                mainHeight,
                RenderTargetColorFormat.Rgba8),
            expected: 1);
        RequireDescriptorCount(
            descriptors,
            new RenderTargetDescriptor(
                observerWidth,
                observerHeight,
                RenderTargetColorFormat.Rgba8),
            expected: 1);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scene.End();
        _builder.Dispose();
        _pipeline.Dispose();
        _pool.Dispose();
        _observerTarget.Dispose();
        _mainTarget.Dispose();
        _backgroundTarget.Dispose();
        _batch.Dispose();
        _toneShader.Dispose();
        _blurShader.Dispose();
        _extractShader.Dispose();
        _blitShader.Dispose();
        _spriteShader.Dispose();
    }

    private static RenderTarget2D CreateHdrTarget(GL gl, int width, int height) =>
        new(gl, new RenderTargetDescriptor(
            width,
            height,
            RenderTargetColorFormat.Rgba16Float,
            RenderTargetDepthStencilFormat.Depth24Stencil8));

    private static (int MainWidth, int MainHeight, int ObserverWidth, int ObserverHeight)
        ResolveViewSizes(int width, int height)
    {
        var (_, _, halfWidth, fullHeight) = ViewportRect.LeftHalf.ToPixels(width, height);
        return (
            halfWidth,
            fullHeight,
            Math.Max(1, (int)MathF.Round(
                halfWidth * 0.75f,
                MidpointRounding.AwayFromZero)),
            Math.Max(1, (int)MathF.Round(
                fullHeight * 0.75f,
                MidpointRounding.AwayFromZero)));
    }

    private static void RequireDescriptorCount(
        IEnumerable<RenderTargetDescriptor> descriptors,
        RenderTargetDescriptor expectedDescriptor,
        int expected)
    {
        int actual = descriptors.Count(descriptor => descriptor == expectedDescriptor);
        if (actual != expected)
            throw new InvalidOperationException(
                $"Expected {expected} active leases for {expectedDescriptor}, found {actual}.");
    }

    private sealed class ViewEffectsOwner : GameInstance
    {
        private readonly Action<IDomainEvent> _raiseEvent;
        private readonly RenderSurfaceKey _source;
        private readonly RenderEffectKey _toneKey;
        private readonly RenderEffectKey? _bloomKey;
        private readonly ViewportRect _viewport;

        public ViewEffectsOwner(
            Action<IDomainEvent> raiseEvent,
            RenderSurfaceKey source,
            RenderEffectKey toneKey,
            ViewportRect viewport,
            RenderEffectKey? bloomKey = null)
        {
            _raiseEvent = raiseEvent;
            _source = source;
            _toneKey = toneKey;
            _viewport = viewport;
            _bloomKey = bloomKey;
        }

        public override void OnCreate()
        {
            RenderSurfaceKey? bloomSource = null;
            if (_bloomKey is { } bloomKey)
            {
                this.RequestBloom(
                    new BloomSettings(0.35f, 1.1f, 1f, 2, BloomResolution.Half),
                    _raiseEvent,
                    bloomKey,
                    _source,
                    RenderTargetColorFormat.Rgba16Float,
                    RenderSurfaceEncoding.Linear);
                bloomSource = BloomEffectDescriptor.GlowOutput(bloomKey);
            }
            this.RequestToneMapping(
                ToneMappingSettings.Default,
                _raiseEvent,
                _toneKey,
                _source,
                bloomSource);
            this.RequestPresentSurface(
                ToneMappingEffectDescriptor.ColorOutput(_toneKey),
                _raiseEvent,
                layer: 0,
                blend: PresentationBlendMode.Opaque,
                viewport: _viewport);
        }

        public override void OnDestroy()
        {
            this.ReleasePresentSurface(_raiseEvent);
            this.ReleaseToneMapping(_raiseEvent, _toneKey);
            if (_bloomKey is { } bloomKey)
                this.ReleaseBloom(_raiseEvent, bloomKey);
        }
    }

    private sealed class SolidColorPass : RenderPass
    {
        private readonly GL _gl;
        private readonly Vector4 _color;

        public override RenderTarget2D Output { get; }
        public override IEnumerable<RenderTarget2D> Inputs => Array.Empty<RenderTarget2D>();

        public SolidColorPass(
            string name,
            GL gl,
            RenderTarget2D output,
            Vector4 color) : base(name)
        {
            _gl = gl;
            Output = output;
            _color = color;
        }

        public override void Execute(in RenderPassContext context)
        {
            _gl.ClearColor(_color.X, _color.Y, _color.Z, _color.W);
            _gl.Clear((uint)ClearBufferMask.ColorBufferBit);
        }
    }
}

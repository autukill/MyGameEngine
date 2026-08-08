namespace GameEngine.VisualRegressionTests;

using System.Numerics;
using Silk.NET.OpenGL;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.Bloom.Application;
using GameEngine.Features.Bloom.Domain;
using GameEngine.Features.Bloom.Infrastructure;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;
using GameEngine.Features.Sprites.Infrastructure;
using GameEngine.Features.StencilMasking.Application;
using GameEngine.Features.StencilMasking.Domain;
using GameEngine.Features.StencilMasking.Infrastructure;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;
using GameEngine.Testing.Visual;

internal sealed class SpriteOriginTransformScenario : IVisualRegressionScenario
{
    private SpriteShader? _shader;
    private SpriteBatch? _batch;
    private TextureLibrary? _textures;
    private SpriteLibrary? _sprites;
    private SceneAggregate? _scene;

    public string Name => "sprites-origin-transform";
    public int Width => 320;
    public int Height => 240;
    public int FrameCount => 2;
    public IReadOnlyList<VisualCheckpoint> Checkpoints { get; } =
        new[] { new VisualCheckpoint(1, "final") };

    public void Initialize(EngineWindow window)
    {
        GL gl = window.Graphics.Gl;
        _shader = new SpriteShader(gl);
        _batch = new SpriteBatch(gl) { DefaultShader = _shader };
        _textures = new TextureLibrary(gl);
        _sprites = new SpriteLibrary(_textures);
        _batch.SpriteResolver = _sprites;

        TextureRef texture = _textures.RegisterRgba(
            "regression.checker",
            24,
            16,
            CreateCheckerPixels(24, 16),
            TextureSampler.PixelArt);
        TextureRef markerTexture = _textures.RegisterRgba(
            "regression.marker",
            3,
            3,
            CreateSolidPixels(3, 3, 255, 255, 255, 255),
            TextureSampler.PixelArt);

        SpriteRef centered = _sprites.RegisterSingle(
            "regression.centered", texture, new Vector2(12, 8));
        SpriteRef topLeft = _sprites.RegisterSingle(
            "regression.top-left", texture, Vector2.Zero);
        SpriteRef custom = _sprites.RegisterSingle(
            "regression.custom", texture, new Vector2(5, 13));
        SpriteRef marker = _sprites.RegisterSingle(
            "regression.marker", markerTexture, new Vector2(1, 1));

        _scene = new SceneAggregate(Name)
        {
            ViewportWidth = Width,
            ViewportHeight = Height
        };
        _scene.SetSprites(_sprites);
        AddSprite(_scene, centered, new Vector2D(70, 65), 0, new Vector2D(2, 2), Vector4.One);
        AddSprite(_scene, topLeft, new Vector2D(160, 45), MathF.PI / 4f,
            new Vector2D(2.2f, 1.4f), new Vector4(0.75f, 1f, 0.75f, 0.9f));
        AddSprite(_scene, custom, new Vector2D(255, 75), -MathF.PI / 6f,
            new Vector2D(-2f, 2.5f), new Vector4(0.75f, 0.85f, 1f, 0.8f));
        AddSprite(_scene, centered, new Vector2D(115, 170), MathF.PI / 2f,
            new Vector2D(3f, 1.25f), new Vector4(1f, 0.65f, 0.65f, 0.75f));
        AddSprite(_scene, custom, new Vector2D(235, 175), MathF.PI,
            new Vector2D(2.5f, -1.5f), new Vector4(1f, 0.9f, 0.4f, 1f));

        foreach (Vector2D anchor in new[]
        {
            new Vector2D(70, 65), new Vector2D(160, 45), new Vector2D(255, 75),
            new Vector2D(115, 170), new Vector2D(235, 175)
        })
            AddSprite(_scene, marker, anchor, 0, new Vector2D(1, 1), Vector4.One);

        gl.Viewport(0, 0, (uint)Width, (uint)Height);
        gl.ClearColor(0.035f, 0.045f, 0.07f, 1f);
    }

    public void AdvanceAndDraw(int frameIndex, double fixedDeltaTime)
    {
        _scene!.PerformStep(fixedDeltaTime);
        _shader!.SetProjection(Matrix4x4.CreateOrthographicOffCenter(
            0, Width, Height, 0, -1, 1));
        _batch!.Begin();
        _scene.DrawActive(_batch);
        _batch.End();
    }

    public void Dispose()
    {
        _scene?.End();
        _textures?.Dispose();
        _batch?.Dispose();
        _shader?.Dispose();
    }

    private static void AddSprite(
        SceneAggregate scene,
        SpriteRef sprite,
        Vector2D position,
        float rotation,
        Vector2D scale,
        Vector4 color) =>
        scene.Add(new SnapshotSprite(sprite, position, rotation, scale, color));

    private sealed class SnapshotSprite : GameInstance
    {
        public SnapshotSprite(
            SpriteRef sprite,
            Vector2D position,
            float rotation,
            Vector2D scale,
            Vector4 color)
        {
            Sprite = sprite;
            Transform = new Transform2D(position, rotation, scale);
            Color = color;
        }
    }

    private static byte[] CreateCheckerPixels(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            bool alternate = ((x / 4) + (y / 4)) % 2 == 0;
            int offset = (y * width + x) * 4;
            pixels[offset] = alternate ? (byte)245 : (byte)35;
            pixels[offset + 1] = alternate ? (byte)115 : (byte)190;
            pixels[offset + 2] = alternate ? (byte)40 : (byte)235;
            pixels[offset + 3] = 255;
        }
        return pixels;
    }

    internal static byte[] CreateSolidPixels(
        int width, int height, byte red, byte green, byte blue, byte alpha)
    {
        var pixels = new byte[width * height * 4];
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = red;
            pixels[offset + 1] = green;
            pixels[offset + 2] = blue;
            pixels[offset + 3] = alpha;
        }
        return pixels;
    }
}

internal sealed class StencilOwnerLifecycleScenario : IVisualRegressionScenario
{
    private DynamicStencilFixture? _fixture;

    public string Name => "stencil-owner-lifecycle";
    public int Width => 320;
    public int Height => 240;
    public int FrameCount => 3;
    public IReadOnlyList<VisualCheckpoint> Checkpoints { get; } = new[]
    {
        new VisualCheckpoint(0, "two-owners"),
        new VisualCheckpoint(1, "one-owner"),
        new VisualCheckpoint(2, "no-owners")
    };

    public void Initialize(EngineWindow window) =>
        _fixture = new DynamicStencilFixture(window.Graphics.Gl, Width, Height, twoOwners: true);

    public void AdvanceAndDraw(int frameIndex, double fixedDeltaTime)
    {
        if (frameIndex == 1) _fixture!.DeactivateFirstOwner();
        if (frameIndex == 2) _fixture!.DestroySecondOwner();
        _fixture!.StepAndDraw(fixedDeltaTime);

        int expectedOwners = 2 - frameIndex;
        if (_fixture.OwnerCount != expectedOwners)
            throw new InvalidOperationException(
                $"Expected {expectedOwners} stencil owners, found {_fixture.OwnerCount}.");
        if (frameIndex == 2 && (_fixture.ActiveEffectCount != 0 || _fixture.LeasedTargetCount != 0))
            throw new InvalidOperationException("Last owner must release the effect and render target.");
    }

    public void Dispose() => _fixture?.Dispose();
}

internal sealed class DynamicEffectResizeScenario : IVisualRegressionScenario
{
    private DynamicStencilFixture? _fixture;

    public string Name => "dynamic-effect-resize";
    public int Width => 400;
    public int Height => 300;
    public int FrameCount => 2;
    public IReadOnlyList<VisualCheckpoint> Checkpoints { get; } =
        new[] { new VisualCheckpoint(1, "resized") };

    public void Initialize(EngineWindow window) =>
        _fixture = new DynamicStencilFixture(window.Graphics.Gl, 320, 240, twoOwners: false);

    public void AdvanceAndDraw(int frameIndex, double fixedDeltaTime)
    {
        if (frameIndex == 1) _fixture!.Resize(Width, Height);
        _fixture!.StepAndDraw(fixedDeltaTime);
        if (frameIndex == 1 &&
            (_fixture.OwnerCount != 1 || _fixture.ActiveEffectCount != 1 ||
             _fixture.TotalTargetCount != 1 || _fixture.LeasedTargetCount != 1))
            throw new InvalidOperationException("Resize must rebuild one active effect without leaking targets.");
    }

    public void Dispose() => _fixture?.Dispose();
}

internal sealed class BloomPingPongScenario : IVisualRegressionScenario
{
    private static readonly PixelComparisonOptions BloomTolerance = new(
        SoftChannelDelta: 3,
        HardChannelDelta: 12,
        MaximumDifferentPixelRatio: 0.005);
    private BloomPingPongFixture? _fixture;

    public string Name => "bloom-ping-pong";
    public int Width => 400;
    public int Height => 300;
    public int FrameCount => 3;
    public IReadOnlyList<VisualCheckpoint> Checkpoints { get; } = new[]
    {
        new VisualCheckpoint(0, "active", BloomTolerance),
        new VisualCheckpoint(1, "resized-active", BloomTolerance),
        new VisualCheckpoint(2, "released")
    };

    public void Initialize(EngineWindow window) =>
        _fixture = new BloomPingPongFixture(window.Graphics.Gl, 320, 240);

    public void AdvanceAndDraw(int frameIndex, double fixedDeltaTime)
    {
        if (frameIndex == 1) _fixture!.Resize(Width, Height);
        if (frameIndex == 2) _fixture!.ReleaseBloom();
        _fixture!.StepAndDraw(fixedDeltaTime);

        int expectedEffects = frameIndex < 2 ? 1 : 0;
        int expectedLeases = frameIndex < 2 ? 3 : 0;
        if (_fixture.ActiveEffectCount != expectedEffects ||
            _fixture.OwnerCount != expectedEffects ||
            _fixture.LeasedTargetCount != expectedLeases)
            throw new InvalidOperationException(
                $"Bloom frame {frameIndex} expected {expectedEffects} effect and " +
                $"{expectedLeases} leases, found {_fixture.ActiveEffectCount} and " +
                $"{_fixture.LeasedTargetCount}.");
        if (frameIndex == 1 && _fixture.TotalTargetCount != 3)
            throw new InvalidOperationException(
                "Bloom resize must replace exactly three intermediate targets.");
    }

    public void Dispose() => _fixture?.Dispose();
}

internal sealed class BloomPingPongFixture : IDisposable
{
    private readonly GL _gl;
    private readonly SpriteShader _spriteShader;
    private readonly BlitShader _blitShader;
    private readonly BloomExtractShader _extractShader;
    private readonly GaussianBlurShader _blurShader;
    private readonly SpriteBatch _batch;
    private readonly TextureLibrary _textures;
    private readonly SpriteLibrary _sprites;
    private readonly SceneAggregate _scene;
    private readonly Camera2D _camera;
    private readonly RenderTarget2D _sceneTarget;
    private readonly RenderTargetPool _pool;
    private readonly RenderPipeline _pipeline;
    private readonly ScenePipelineBuilder _builder;
    private readonly BloomOwner _owner;
    private int _width;
    private int _height;
    private bool _released;
    private bool _disposed;

    public int OwnerCount => _builder.GetOwnerCount(BloomEffectDescriptor.DefaultKey);
    public int ActiveEffectCount => _builder.ActiveEffectCount;
    public int TotalTargetCount => _pool.TotalCount;
    public int LeasedTargetCount => _pool.LeasedCount;

    public BloomPingPongFixture(GL gl, int width, int height)
    {
        _gl = gl;
        _width = width;
        _height = height;
        _spriteShader = new SpriteShader(gl);
        _blitShader = new BlitShader(gl);
        _extractShader = new BloomExtractShader(gl);
        _blurShader = new GaussianBlurShader(gl);
        _batch = new SpriteBatch(gl) { DefaultShader = _spriteShader };
        _textures = new TextureLibrary(gl);
        _sprites = new SpriteLibrary(_textures);
        _batch.SpriteResolver = _sprites;

        TextureRef white = _textures.RegisterRgba(
            "regression.bloom.white", 1, 1,
            SpriteOriginTransformScenario.CreateSolidPixels(1, 1, 255, 255, 255, 255),
            TextureSampler.PixelArt);
        SpriteRef tile = _sprites.RegisterSingle(
            "regression.bloom.tile", white, new Vector2(20, 20), new Vector2(10, 10));

        _scene = new SceneAggregate("bloom-ping-pong")
        {
            ViewportWidth = width,
            ViewportHeight = height,
            Background = BackgroundConfig.FromColor(new Vector4(0.018f, 0.025f, 0.045f, 1f))
        };
        _scene.SetSprites(_sprites);
        AddTile(tile, new Vector2D(70, 65), new Vector2D(4, 4),
            new Vector4(1f, 0.12f, 0.08f, 1f));
        AddTile(tile, new Vector2D(165, 120), new Vector2D(6, 2),
            new Vector4(0.05f, 0.9f, 1f, 1f));
        AddTile(tile, new Vector2D(255, 175), new Vector2D(3, 6),
            new Vector4(0.85f, 0.2f, 1f, 1f));
        AddTile(tile, new Vector2D(105, 195), new Vector2D(2, 2), Vector4.One);
        _owner = _scene.Add(new BloomOwner(_scene.RaiseEvent));

        _camera = new Camera2D(new Vector2(width, height));
        _sceneTarget = new RenderTarget2D(gl, width, height, withDepthStencil: true);
        _pool = new RenderTargetPool(gl);
        var scenePass = new SceneRenderPass("Scene", gl, _scene, _camera, _sceneTarget);
        var compositor = new ViewportCompositorPass("Compositor", gl, _blitShader, _batch);
        compositor.AddSource(_sceneTarget, ViewportRect.FullScreen, BlendState.Opaque);
        _pipeline = new RenderPipeline(gl, width, height);
        _pipeline.AddPass(scenePass);
        _pipeline.AddPass(compositor);
        _builder = new ScenePipelineBuilder(_pipeline, compositor, _pool, width, height);
        _builder.RegisterFactory(new BloomEffectFactory(
            gl, _sceneTarget, _extractShader, _blurShader));
        _builder.ApplyEvents(_scene.DrainUncommittedEvents());
    }

    public void Resize(int width, int height)
    {
        _width = width;
        _height = height;
        _scene.ViewportWidth = width;
        _scene.ViewportHeight = height;
        _camera.ResizeViewport(width, height);
        _sceneTarget.Resize(width, height);
        _pipeline.Resize(width, height);
        _builder.Resize(width, height);
    }

    public void ReleaseBloom()
    {
        if (_released) return;
        _released = true;
        _scene.Destroy(_owner.Id);
    }

    public void StepAndDraw(double fixedDeltaTime)
    {
        _scene.PerformStep(fixedDeltaTime);
        _builder.ApplyEvents(_scene.DrainUncommittedEvents());
        var context = new RenderPassContext(
            _gl, _spriteShader, _batch, _width, _height);
        _pipeline.Execute(context);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scene.End();
        _builder.Dispose();
        _pipeline.Dispose();
        _pool.Dispose();
        _sceneTarget.Dispose();
        _textures.Dispose();
        _batch.Dispose();
        _blurShader.Dispose();
        _extractShader.Dispose();
        _blitShader.Dispose();
        _spriteShader.Dispose();
    }

    private void AddTile(SpriteRef sprite, Vector2D position, Vector2D scale, Vector4 color) =>
        _scene.Add(new BloomTile(sprite, position, scale, color));

    private sealed class BloomTile : GameInstance
    {
        public BloomTile(SpriteRef sprite, Vector2D position, Vector2D scale, Vector4 color)
        {
            Sprite = sprite;
            Transform = new Transform2D(position, 0, scale);
            Color = color;
        }
    }

    private sealed class BloomOwner : GameInstance
    {
        private readonly Action<IDomainEvent> _raiseEvent;

        public BloomOwner(Action<IDomainEvent> raiseEvent) => _raiseEvent = raiseEvent;

        public override void OnCreate() => this.RequestBloom(
            new BloomSettings(0.35f, 1.35f, 1f, 2, BloomResolution.Half),
            _raiseEvent);

        public override void OnDestroy() => this.ReleaseBloom(_raiseEvent);
    }
}

internal sealed class DynamicStencilFixture : IDisposable
{
    private static readonly RenderEffectKey EffectKey = StencilMaskEffectDescriptor.DefaultKey;

    private readonly GL _gl;
    private readonly SpriteShader _spriteShader;
    private readonly BlitShader _blitShader;
    private readonly SpriteBatch _batch;
    private readonly TextureLibrary _textures;
    private readonly SpriteLibrary _sprites;
    private readonly SceneAggregate _scene;
    private readonly Camera2D _camera;
    private readonly RenderTarget2D _sceneTarget;
    private readonly RenderTargetPool _pool;
    private readonly RenderPipeline _pipeline;
    private readonly ScenePipelineBuilder _builder;
    private readonly MaskOwner _firstOwner;
    private readonly MaskOwner? _secondOwner;
    private int _width;
    private int _height;
    private bool _disposed;

    public int OwnerCount => _builder.GetOwnerCount(EffectKey);
    public int ActiveEffectCount => _builder.ActiveEffectCount;
    public int TotalTargetCount => _pool.TotalCount;
    public int LeasedTargetCount => _pool.LeasedCount;

    public DynamicStencilFixture(GL gl, int width, int height, bool twoOwners)
    {
        _gl = gl;
        _width = width;
        _height = height;
        _spriteShader = new SpriteShader(gl);
        _blitShader = new BlitShader(gl);
        _batch = new SpriteBatch(gl) { DefaultShader = _spriteShader };
        _textures = new TextureLibrary(gl);
        _sprites = new SpriteLibrary(_textures);
        _batch.SpriteResolver = _sprites;

        TextureRef white = _textures.RegisterRgba(
            "regression.white", 1, 1,
            SpriteOriginTransformScenario.CreateSolidPixels(1, 1, 255, 255, 255, 255),
            TextureSampler.PixelArt);
        SpriteRef tile = _sprites.RegisterSingle(
            "regression.tile", white, new Vector2(20, 20), new Vector2(10, 10));

        _scene = new SceneAggregate("dynamic-stencil")
        {
            ViewportWidth = width,
            ViewportHeight = height,
            Background = BackgroundConfig.FromColor(new Vector4(0.025f, 0.035f, 0.055f, 1f))
        };
        _scene.SetSprites(_sprites);
        AddTile(tile, new Vector2D(70, 65), new Vector2D(6, 4), new Vector4(1f, 0.18f, 0.12f, 0.78f));
        AddTile(tile, new Vector2D(170, 105), new Vector2D(7, 5), new Vector4(0.1f, 0.8f, 1f, 0.72f));
        AddTile(tile, new Vector2D(255, 175), new Vector2D(5, 7), new Vector4(0.8f, 0.25f, 1f, 0.72f));
        AddTile(tile, new Vector2D(110, 195), new Vector2D(4, 3), new Vector4(0.4f, 1f, 0.3f, 0.85f));

        _firstOwner = _scene.Add(new MaskOwner(
            _scene.RaiseEvent, new Vector2D(90, 85), 52f));
        if (twoOwners)
            _secondOwner = _scene.Add(new MaskOwner(
                _scene.RaiseEvent, new Vector2D(235, 155), 58f));

        _camera = new Camera2D(new Vector2(width, height));
        _sceneTarget = new RenderTarget2D(gl, width, height, withDepthStencil: true);
        _pool = new RenderTargetPool(gl);
        var scenePass = new SceneRenderPass("Scene", gl, _scene, _camera, _sceneTarget);
        var compositor = new ViewportCompositorPass("Compositor", gl, _blitShader, _batch);
        compositor.AddSource(_sceneTarget, ViewportRect.FullScreen, BlendState.Opaque);
        _pipeline = new RenderPipeline(gl, width, height);
        _pipeline.AddPass(scenePass);
        _pipeline.AddPass(compositor);
        _builder = new ScenePipelineBuilder(_pipeline, compositor, _pool, width, height);
        _builder.RegisterFactory(new StencilMaskEffectFactory(
            gl, _scene, _camera, _spriteShader, white, _textures, _sprites));
        _builder.ApplyEvents(_scene.DrainUncommittedEvents());
    }

    public void DeactivateFirstOwner()
    {
        _firstOwner.SetActive(false, _scene.RaiseEvent);
    }

    public void DestroySecondOwner()
    {
        if (_secondOwner is null)
            throw new InvalidOperationException("The fixture has no second owner.");
        _scene.Destroy(_secondOwner.Id);
    }

    public void Resize(int width, int height)
    {
        _width = width;
        _height = height;
        _scene.ViewportWidth = width;
        _scene.ViewportHeight = height;
        _camera.ResizeViewport(width, height);
        _sceneTarget.Resize(width, height);
        _pipeline.Resize(width, height);
        _builder.Resize(width, height);
    }

    public void StepAndDraw(double fixedDeltaTime)
    {
        _scene.PerformStep(fixedDeltaTime);
        _builder.ApplyEvents(_scene.DrainUncommittedEvents());
        var context = new RenderPassContext(
            _gl, _spriteShader, _batch, _width, _height);
        _pipeline.Execute(context);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scene.End();
        _builder.Dispose();
        _pipeline.Dispose();
        _pool.Dispose();
        _sceneTarget.Dispose();
        _textures.Dispose();
        _batch.Dispose();
        _blitShader.Dispose();
        _spriteShader.Dispose();
    }

    private void AddTile(SpriteRef sprite, Vector2D position, Vector2D scale, Vector4 color) =>
        _scene.Add(new StaticSprite(sprite, position, scale, color));

    private sealed class StaticSprite : GameInstance
    {
        public StaticSprite(SpriteRef sprite, Vector2D position, Vector2D scale, Vector4 color)
        {
            Sprite = sprite;
            Transform = new Transform2D(position, 0, scale);
            Color = color;
        }
    }

    private sealed class MaskOwner : GameInstance
    {
        private readonly Action<IDomainEvent> _raiseEvent;
        private readonly Vector2D _center;
        private readonly float _radius;

        public MaskOwner(Action<IDomainEvent> raiseEvent, Vector2D center, float radius)
        {
            _raiseEvent = raiseEvent;
            _center = center;
            _radius = radius;
        }

        public override void OnCreate() =>
            this.RequestStencilMask(_center, _radius, StencilMaskState.Spotlight, _raiseEvent);

        public override void OnDestroy() => this.ReleaseStencilMask(_raiseEvent);
    }
}

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
using GameEngine.Features.Presentation.Application;
using GameEngine.Features.Presentation.Domain;
using GameEngine.Features.Presentation.Infrastructure;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;
using GameEngine.Features.Sprites.Infrastructure;
using GameEngine.Features.StencilMasking.Application;
using GameEngine.Features.StencilMasking.Domain;
using GameEngine.Features.StencilMasking.Infrastructure;
using GameEngine.Features.ToneMapping.Application;
using GameEngine.Features.ToneMapping.Domain;
using GameEngine.Features.ToneMapping.Infrastructure;
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

internal sealed class ShaderProgramReloadScenario : IVisualRegressionScenario
{
    private const string ShaderName = "regression.reload";
    private const string PeerShaderName = "regression.reload.peer";
    private SpriteShader? _defaultShader;
    private ShaderLibrary? _shaders;
    private ShaderMaterial? _material;
    private SpriteBatch? _batch;
    private TextureLibrary? _textures;
    private TextureRef _white;
    private uint _initialHandle;
    private uint _peerInitialHandle;

    public string Name => "shader-program-reload";
    public int Width => 192;
    public int Height => 128;
    public int FrameCount => 3;
    public IReadOnlyList<VisualCheckpoint> Checkpoints { get; } = new[]
    {
        new VisualCheckpoint(0, "initial"),
        new VisualCheckpoint(1, "compile-failure-retains-old"),
        new VisualCheckpoint(2, "replacement-applied")
    };

    public void Initialize(EngineWindow window)
    {
        GL gl = window.Graphics.Gl;
        _gl = gl;
        _defaultShader = new SpriteShader(gl);
        _shaders = new ShaderLibrary(gl);
        _shaders.Create(ShaderName, VertexSource, RedFragmentSource);
        _shaders.Create(PeerShaderName, VertexSource, RedFragmentSource);
        _material = _shaders.CreateMaterial(
                "regression.reload.material",
                new ShaderRef(ShaderName),
                ShaderUniformDefinition.Float("uGain"))
            .SetFloat("uGain", 1f);
        try
        {
            _material.SetInt("uGain", 1);
            throw new InvalidOperationException("Material accepted a mismatched uniform type.");
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains("not Int", StringComparison.Ordinal))
        {
        }
        _initialHandle = _shaders.Resolve(new ShaderRef(ShaderName));
        _peerInitialHandle = _shaders.Resolve(new ShaderRef(PeerShaderName));
        _batch = new SpriteBatch(gl)
        {
            DefaultShader = _defaultShader,
            ShaderResolver = _shaders
        };
        _textures = new TextureLibrary(gl);
        _white = _textures.RegisterRgba(
            "regression.shader-white",
            1,
            1,
            new byte[] { 255, 255, 255, 255 },
            TextureSampler.PixelArt);
        gl.Viewport(0, 0, (uint)Width, (uint)Height);
    }

    public void AdvanceAndDraw(int frameIndex, double fixedDeltaTime)
    {
        if (frameIndex == 1)
        {
            try
            {
                _shaders!.ReplaceAll(new[]
                {
                    new ShaderProgramSource(ShaderName, VertexSource, GreenFragmentSource),
                    new ShaderProgramSource(PeerShaderName, VertexSource, InvalidFragmentSource)
                });
                throw new InvalidOperationException("Invalid GLSL unexpectedly compiled.");
            }
            catch (ShaderBuildException)
            {
            }
            if (_shaders!.Resolve(new ShaderRef(ShaderName)) != _initialHandle)
                throw new InvalidOperationException("Failed replacement changed the live Program handle.");
            if (_shaders.Resolve(new ShaderRef(PeerShaderName)) != _peerInitialHandle)
                throw new InvalidOperationException("Failed batch replacement changed a peer Program handle.");
        }
        else if (frameIndex == 2)
        {
            _shaders!.ReplaceAll(new[]
            {
                new ShaderProgramSource(ShaderName, VertexSource, GreenFragmentSource)
            });
            if (_shaders.Resolve(new ShaderRef(ShaderName)) == _initialHandle)
                throw new InvalidOperationException("Successful replacement retained the old Program handle.");
        }

        GL gl = _gl ?? throw new InvalidOperationException("Scenario is not initialized.");
        gl.ClearColor(0.03f, 0.04f, 0.06f, 1f);
        gl.Clear((uint)ClearBufferMask.ColorBufferBit);
        Matrix4x4 projection = Matrix4x4.CreateOrthographicOffCenter(0, Width, Height, 0, -1, 1);
        _shaders!.SetProjection(projection);
        _defaultShader!.Use();
        _defaultShader.SetProjection(projection);
        _batch!.Begin();
        _batch.SetMaterial(_material!.Ref);
        _batch.Draw(
            ResolveWhiteHandle(),
            new Vector2(32, 24),
            new Vector2(128, 80),
            new Vector4(1f, 0.1f, 0.1f, 1f));
        _batch.End();
    }

    private GL? _gl;

    private uint ResolveWhiteHandle()
    {
        if (!_textures!.TryResolve(_white, out ResolvedTexture texture))
            throw new InvalidOperationException("White texture is unavailable.");
        return texture.Handle;
    }

    public void Dispose()
    {
        _textures?.Dispose();
        _batch?.Dispose();
        _shaders?.Dispose();
        _defaultShader?.Dispose();
    }

    private const string VertexSource = """
        #version 330 core
        layout (location = 0) in vec2 aPos;
        layout (location = 1) in vec2 aTexCoord;
        layout (location = 2) in vec4 aColor;
        out vec2 vUv;
        out vec4 vColor;
        uniform mat4 uProjection;
        void main() {
            gl_Position = uProjection * vec4(aPos, 0.0, 1.0);
            vUv = aTexCoord;
            vColor = aColor;
        }
        """;

    private const string RedFragmentSource = """
        #version 330 core
        in vec2 vUv;
        in vec4 vColor;
        out vec4 FragColor;
        uniform sampler2D uTexture;
        uniform float uGain;
        void main() { FragColor = texture(uTexture, vUv) * vColor * uGain; }
        """;

    private const string GreenFragmentSource = """
        #version 330 core
        in vec2 vUv;
        in vec4 vColor;
        out vec4 FragColor;
        uniform sampler2D uTexture;
        uniform float uGain;
        void main() {
            float value = texture(uTexture, vUv).r * vColor.r * uGain;
            FragColor = vec4(0.1, value, 0.1, 1.0);
        }
        """;

    private const string InvalidFragmentSource = """
        #version 330 core
        this is not valid GLSL
        """;
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
        if (frameIndex == 2 && (_fixture.ActiveEffectCount != 1 || _fixture.LeasedTargetCount != 0))
            throw new InvalidOperationException(
                "Last mask owner must release Stencil while the screen terminal remains active.");
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
            (_fixture.OwnerCount != 1 || _fixture.ActiveEffectCount != 2 ||
             _fixture.TotalTargetCount != 1 || _fixture.LeasedTargetCount != 1))
            throw new InvalidOperationException("Resize must rebuild one active effect without leaking targets.");
    }

    public void Dispose() => _fixture?.Dispose();
}

internal sealed class StencilSpriteAlphaScenario : IVisualRegressionScenario
{
    private DynamicStencilFixture? _fixture;

    public string Name => "stencil-sprite-alpha";
    public int Width => 320;
    public int Height => 240;
    public int FrameCount => 1;
    public IReadOnlyList<VisualCheckpoint> Checkpoints { get; } =
        new[] { new VisualCheckpoint(0, "transformed-alpha-mask") };

    public void Initialize(EngineWindow window) =>
        _fixture = new DynamicStencilFixture(
            window.Graphics.Gl,
            Width,
            Height,
            twoOwners: false,
            useSpriteMask: true);

    public void AdvanceAndDraw(int frameIndex, double fixedDeltaTime)
    {
        _fixture!.StepAndDraw(fixedDeltaTime);
        if (_fixture.OwnerCount != 1 ||
            _fixture.ActiveEffectCount != 2 ||
            _fixture.LeasedTargetCount != 1)
            throw new InvalidOperationException(
                "Sprite Alpha mask must share the Stencil runtime and explicit terminal.");
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

        int expectedEffects = frameIndex < 2 ? 2 : 1;
        int expectedBloomOwners = frameIndex < 2 ? 1 : 0;
        int expectedLeases = frameIndex < 2 ? 3 : 0;
        if (_fixture.ActiveEffectCount != expectedEffects ||
            _fixture.OwnerCount != expectedBloomOwners ||
            _fixture.LeasedTargetCount != expectedLeases)
            throw new InvalidOperationException(
                $"Bloom frame {frameIndex} expected {expectedEffects} effects and " +
                $"{expectedLeases} leases, found {_fixture.ActiveEffectCount} and " +
                $"{_fixture.LeasedTargetCount}.");
        if (frameIndex == 1 && _fixture.TotalTargetCount != 3)
            throw new InvalidOperationException(
                "Bloom resize must replace exactly three intermediate targets.");
    }

    public void Dispose() => _fixture?.Dispose();
}

internal sealed class RenderSurfaceChainScenario : IVisualRegressionScenario
{
    private static readonly PixelComparisonOptions BloomTolerance = new(
        SoftChannelDelta: 3,
        HardChannelDelta: 12,
        MaximumDifferentPixelRatio: 0.005);
    private BloomPingPongFixture? _fixture;

    public string Name => "render-surface-chain";
    public int Width => 400;
    public int Height => 300;
    public int FrameCount => 2;
    public IReadOnlyList<VisualCheckpoint> Checkpoints { get; } = new[]
    {
        new VisualCheckpoint(0, "scene-bloom-bloom", BloomTolerance)
    };

    public void Initialize(EngineWindow window) =>
        _fixture = new BloomPingPongFixture(
            window.Graphics.Gl, Width, Height, chained: true);

    public void AdvanceAndDraw(int frameIndex, double fixedDeltaTime)
    {
        if (frameIndex == 1) _fixture!.ReleaseBloom();
        _fixture!.StepAndDraw(fixedDeltaTime);

        int expectedEffects = frameIndex == 0 ? 3 : 1;
        int expectedBloomOwners = frameIndex == 0 ? 2 : 0;
        int expectedLeases = frameIndex == 0 ? 6 : 0;
        if (_fixture.ActiveEffectCount != expectedEffects ||
            _fixture.TotalOwnerCount != expectedBloomOwners ||
            _fixture.LeasedTargetCount != expectedLeases)
            throw new InvalidOperationException(
                $"Chained Bloom frame {frameIndex} expected {expectedEffects} effects and " +
                $"{expectedLeases} leases, found {_fixture.ActiveEffectCount} and " +
                $"{_fixture.LeasedTargetCount}.");
    }

    public void Dispose() => _fixture?.Dispose();
}

internal sealed class BloomPingPongFixture : IDisposable
{
    private static readonly RenderEffectKey UpstreamKey = BloomEffectDescriptor.DefaultKey;
    private static readonly RenderEffectKey DownstreamKey =
        new(BloomEffectDescriptor.EffectKind, "secondary");
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
    private readonly BloomOwner? _downstreamOwner;
    private int _width;
    private int _height;
    private bool _released;
    private bool _disposed;

    public int OwnerCount => _builder.GetOwnerCount(BloomEffectDescriptor.DefaultKey);
    public int TotalOwnerCount =>
        _builder.GetOwnerCount(UpstreamKey) + _builder.GetOwnerCount(DownstreamKey);
    public int ActiveEffectCount => _builder.ActiveEffectCount;
    public int TotalTargetCount => _pool.CaptureDiagnostics().TotalCount;
    public int LeasedTargetCount => _pool.CaptureDiagnostics().LeasedCount;

    public BloomPingPongFixture(GL gl, int width, int height, bool chained = false)
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
        _scene.Add(new SurfacePresentationOwner(
            _scene.RaiseEvent,
            RenderSurfaceKey.SceneColor,
            layer: 0,
            blend: PresentationBlendMode.Opaque));
        _owner = _scene.Add(new BloomOwner(
            _scene.RaiseEvent,
            UpstreamKey,
            RenderSurfaceKey.SceneColor,
            new BloomSettings(0.35f, 1.35f, 1f, 2, BloomResolution.Half),
            presentationLayer: 100));
        if (chained)
            _downstreamOwner = _scene.Add(new BloomOwner(
                _scene.RaiseEvent,
                DownstreamKey,
                BloomEffectDescriptor.GlowOutput(UpstreamKey),
                new BloomSettings(0.12f, 0.8f, 1.25f, 2, BloomResolution.Half),
                presentationLayer: 200));

        _camera = new Camera2D(new Vector2(width, height));
        _sceneTarget = new RenderTarget2D(gl, width, height, withDepthStencil: true);
        _pool = new RenderTargetPool(gl);
        var scenePass = new SceneRenderPass("Scene", gl, _scene, _camera, _sceneTarget);
        _pipeline = new RenderPipeline(gl, width, height);
        _pipeline.AddPass(scenePass);
        _builder = new ScenePipelineBuilder(_pipeline, _pool, width, height);
        _builder.RegisterRootSurface(RenderSurfaceKey.SceneColor, _sceneTarget);
        _builder.RegisterFactory(new BloomEffectFactory(
            gl, _extractShader, _blurShader));
        _builder.RegisterFactory(new PresentationEffectFactory(gl, _blitShader, _batch));
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
        if (_downstreamOwner is not null) _scene.Destroy(_downstreamOwner.Id);
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
        private readonly RenderEffectKey _key;
        private readonly RenderSurfaceKey _source;
        private readonly BloomSettings _settings;
        private readonly int _presentationLayer;

        public BloomOwner(
            Action<IDomainEvent> raiseEvent,
            RenderEffectKey key,
            RenderSurfaceKey source,
            BloomSettings settings,
            int presentationLayer)
        {
            _raiseEvent = raiseEvent;
            _key = key;
            _source = source;
            _settings = settings;
            _presentationLayer = presentationLayer;
        }

        public override void OnCreate()
        {
            this.RequestBloom(_settings, _raiseEvent, _key, _source);
            this.RequestPresentSurface(
                BloomEffectDescriptor.GlowOutput(_key),
                _raiseEvent,
                _presentationLayer,
                PresentationBlendMode.Additive);
        }

        public override void OnDestroy()
        {
            this.ReleasePresentSurface(_raiseEvent);
            this.ReleaseBloom(_raiseEvent, _key);
        }
    }
}

internal sealed class HdrToneMappingScenario : IVisualRegressionScenario
{
    private static readonly PixelComparisonOptions HdrTolerance = new(
        SoftChannelDelta: 3,
        HardChannelDelta: 12,
        MaximumDifferentPixelRatio: 0.005);
    private HdrToneMappingFixture? _fixture;

    public string Name => "hdr-tone-mapping";
    public int Width => 400;
    public int Height => 300;
    public int FrameCount => 4;
    public IReadOnlyList<VisualCheckpoint> Checkpoints { get; } = new[]
    {
        new VisualCheckpoint(0, "aces", HdrTolerance),
        new VisualCheckpoint(1, "reinhard-low-exposure", HdrTolerance),
        new VisualCheckpoint(2, "resized", HdrTolerance),
        new VisualCheckpoint(3, "released")
    };

    public void Initialize(EngineWindow window) =>
        _fixture = new HdrToneMappingFixture(window.Graphics.Gl, 320, 240);

    public void AdvanceAndDraw(int frameIndex, double fixedDeltaTime)
    {
        if (frameIndex == 1)
            _fixture!.UpdateToneMapping(new ToneMappingSettings(
                ToneMappingOperator.Reinhard, exposure: -0.75f, gamma: 2.2f));
        if (frameIndex == 2) _fixture!.Resize(Width, Height);
        if (frameIndex == 3) _fixture!.ReleaseEffects();
        _fixture!.StepAndDraw(fixedDeltaTime);

        int expectedEffects = frameIndex < 3 ? 3 : 1;
        int expectedLeases = frameIndex < 3 ? 4 : 0;
        if (_fixture.ActiveEffectCount != expectedEffects ||
            _fixture.LeasedTargetCount != expectedLeases)
            throw new InvalidOperationException(
                $"HDR frame {frameIndex} expected {expectedEffects} effects and " +
                $"{expectedLeases} leases, found {_fixture.ActiveEffectCount} and " +
                $"{_fixture.LeasedTargetCount}.");
        if (frameIndex == 2 && _fixture.TotalTargetCount != 4)
            throw new InvalidOperationException(
                "HDR resize must replace exactly three Bloom targets and one Tone Mapping target.");
    }

    public void Dispose() => _fixture?.Dispose();
}

internal sealed class HdrToneMappingFixture : IDisposable
{
    private readonly GL _gl;
    private readonly SpriteShader _spriteShader;
    private readonly BlitShader _blitShader;
    private readonly BloomExtractShader _extractShader;
    private readonly GaussianBlurShader _blurShader;
    private readonly ToneMappingShader _toneShader;
    private readonly SpriteBatch _batch;
    private readonly TextureLibrary _textures;
    private readonly SpriteLibrary _sprites;
    private readonly SceneAggregate _scene;
    private readonly Camera2D _camera;
    private readonly RenderTarget2D _sceneTarget;
    private readonly RenderTarget2D _guiTarget;
    private readonly RenderTargetPool _pool;
    private readonly RenderPipeline _pipeline;
    private readonly ScenePipelineBuilder _builder;
    private readonly HdrOwner _owner;
    private int _width;
    private int _height;
    private bool _released;
    private bool _disposed;

    public int ActiveEffectCount => _builder.ActiveEffectCount;
    public int TotalTargetCount => _pool.CaptureDiagnostics().TotalCount;
    public int LeasedTargetCount => _pool.CaptureDiagnostics().LeasedCount;

    public HdrToneMappingFixture(GL gl, int width, int height)
    {
        _gl = gl;
        _width = width;
        _height = height;
        _spriteShader = new SpriteShader(gl);
        _blitShader = new BlitShader(gl);
        _extractShader = new BloomExtractShader(gl);
        _blurShader = new GaussianBlurShader(gl);
        _toneShader = new ToneMappingShader(gl);
        _batch = new SpriteBatch(gl) { DefaultShader = _spriteShader };
        _textures = new TextureLibrary(gl);
        _sprites = new SpriteLibrary(_textures);
        _batch.SpriteResolver = _sprites;

        TextureRef white = _textures.RegisterRgba(
            "regression.hdr.white",
            1,
            1,
            SpriteOriginTransformScenario.CreateSolidPixels(1, 1, 255, 255, 255, 255),
            TextureSampler.PixelArt);
        SpriteRef tile = _sprites.RegisterSingle(
            "regression.hdr.tile", white, new Vector2(20, 20), new Vector2(10, 10));

        _scene = new SceneAggregate("hdr-tone-mapping")
        {
            ViewportWidth = width,
            ViewportHeight = height,
            Background = BackgroundConfig.FromColor(new Vector4(0.01f, 0.018f, 0.035f, 1f))
        };
        _scene.SetSprites(_sprites);
        AddTile(tile, new Vector2D(75, 70), new Vector2D(5, 4),
            new Vector4(4f, 0.18f, 0.08f, 1f));
        AddTile(tile, new Vector2D(175, 115), new Vector2D(6, 3),
            new Vector4(0.05f, 2.8f, 4f, 1f));
        AddTile(tile, new Vector2D(255, 180), new Vector2D(4, 6),
            new Vector4(2.5f, 0.2f, 4f, 1f));
        AddTile(tile, new Vector2D(115, 195), new Vector2D(2, 2),
            new Vector4(1f, 1f, 1f, 1f));
        _scene.Add(new GuiMarker(tile));
        _scene.Add(new SurfacePresentationOwner(
            _scene.RaiseEvent,
            RenderSurfaceKey.SceneGui,
            layer: 1000,
            blend: PresentationBlendMode.AlphaBlend));
        _owner = _scene.Add(new HdrOwner(_scene.RaiseEvent, ToneMappingSettings.Default));

        _camera = new Camera2D(new Vector2(width, height));
        _sceneTarget = new RenderTarget2D(gl, new RenderTargetDescriptor(
            width,
            height,
            RenderTargetColorFormat.Rgba16Float,
            RenderTargetDepthStencilFormat.Depth24Stencil8));
        _guiTarget = new RenderTarget2D(gl, new RenderTargetDescriptor(
            width,
            height,
            RenderTargetColorFormat.Rgba8,
            RenderTargetDepthStencilFormat.None));
        _pool = new RenderTargetPool(gl);
        var scenePass = new SceneRenderPass("HDR Scene", gl, _scene, _camera, _sceneTarget);
        var guiPass = new SceneGuiRenderPass("LDR GUI", gl, _scene, _guiTarget);
        _pipeline = new RenderPipeline(gl, width, height);
        _pipeline.AddPass(scenePass);
        _pipeline.AddPass(guiPass);
        _builder = new ScenePipelineBuilder(_pipeline, _pool, width, height);
        _builder.RegisterRootSurface(
            RenderSurfaceKey.SceneColor,
            _sceneTarget,
            RenderSurfaceEncoding.Linear);
        _builder.RegisterRootSurface(RenderSurfaceKey.SceneGui, _guiTarget);
        _builder.RegisterFactory(new BloomEffectFactory(gl, _extractShader, _blurShader));
        _builder.RegisterFactory(new ToneMappingEffectFactory(gl, _toneShader));
        _builder.RegisterFactory(new PresentationEffectFactory(gl, _blitShader, _batch));
        _builder.ApplyEvents(_scene.DrainUncommittedEvents());
    }

    public void UpdateToneMapping(ToneMappingSettings settings) =>
        _owner.UpdateToneMapping(settings);

    public void Resize(int width, int height)
    {
        _width = width;
        _height = height;
        _scene.ViewportWidth = width;
        _scene.ViewportHeight = height;
        _camera.ResizeViewport(width, height);
        _sceneTarget.Resize(width, height);
        _guiTarget.Resize(width, height);
        _pipeline.Resize(width, height);
        _builder.Resize(width, height);
    }

    public void ReleaseEffects()
    {
        if (_released) return;
        _released = true;
        _scene.Destroy(_owner.Id);
    }

    public void StepAndDraw(double fixedDeltaTime)
    {
        _scene.PerformStep(fixedDeltaTime);
        _builder.ApplyEvents(_scene.DrainUncommittedEvents());
        _pipeline.Execute(new RenderPassContext(
            _gl, _spriteShader, _batch, _width, _height));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scene.End();
        _builder.Dispose();
        _pipeline.Dispose();
        _pool.Dispose();
        _guiTarget.Dispose();
        _sceneTarget.Dispose();
        _textures.Dispose();
        _batch.Dispose();
        _toneShader.Dispose();
        _blurShader.Dispose();
        _extractShader.Dispose();
        _blitShader.Dispose();
        _spriteShader.Dispose();
    }

    private void AddTile(SpriteRef sprite, Vector2D position, Vector2D scale, Vector4 color) =>
        _scene.Add(new HdrTile(sprite, position, scale, color));

    private sealed class HdrTile : GameInstance
    {
        public HdrTile(SpriteRef sprite, Vector2D position, Vector2D scale, Vector4 color)
        {
            Sprite = sprite;
            Transform = new Transform2D(position, 0, scale);
            Color = color;
        }
    }

    private sealed class GuiMarker : GameInstance
    {
        public GuiMarker(SpriteRef sprite)
        {
            Sprite = sprite;
        }

        public override void OnDrawGUI(ISpriteBatch batch) => batch.DrawSpriteCommand(
            new SpriteDrawCommand(
                Sprite,
                0f,
                new Vector2(34f, 28f),
                new Vector2(2.2f, 1.2f),
                0f,
                new Vector4(0.2f, 1f, 0.35f, 0.9f)));
    }

    private sealed class HdrOwner : GameInstance
    {
        private readonly Action<IDomainEvent> _raiseEvent;
        private ToneMappingSettings _toneSettings;

        public HdrOwner(Action<IDomainEvent> raiseEvent, ToneMappingSettings toneSettings)
        {
            _raiseEvent = raiseEvent;
            _toneSettings = toneSettings;
        }

        public override void OnCreate()
        {
            this.RequestBloom(
                new BloomSettings(0.7f, 1.1f, 1f, 2, BloomResolution.Half),
                _raiseEvent,
                colorFormat: RenderTargetColorFormat.Rgba16Float,
                encoding: RenderSurfaceEncoding.Linear);
            RequestToneMapping();
            this.RequestPresentSurface(
                ToneMappingEffectDescriptor.ColorOutput(ToneMappingEffectDescriptor.DefaultKey),
                _raiseEvent,
                layer: 0,
                blend: PresentationBlendMode.Opaque);
        }

        public void UpdateToneMapping(ToneMappingSettings settings)
        {
            _toneSettings = settings;
            RequestToneMapping();
        }

        public override void OnDestroy()
        {
            this.ReleasePresentSurface(_raiseEvent);
            this.ReleaseToneMapping(_raiseEvent);
            this.ReleaseBloom(_raiseEvent);
        }

        private void RequestToneMapping() => this.RequestToneMapping(
            _toneSettings,
            _raiseEvent,
            bloomSource: BloomEffectDescriptor.GlowOutput(BloomEffectDescriptor.DefaultKey));
    }
}

internal sealed class DynamicStencilFixture : IDisposable
{
    private static readonly RenderEffectKey EffectKey = StencilMaskEffectDescriptor.DefaultKey;

    private readonly GL _gl;
    private readonly SpriteShader _spriteShader;
    private readonly StencilMaskShader _maskShader;
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
    private readonly GameInstance _firstOwner;
    private readonly MaskOwner? _secondOwner;
    private int _width;
    private int _height;
    private bool _disposed;

    public int OwnerCount => _builder.GetOwnerCount(EffectKey);
    public int ActiveEffectCount => _builder.ActiveEffectCount;
    public int TotalTargetCount => _pool.CaptureDiagnostics().TotalCount;
    public int LeasedTargetCount => _pool.CaptureDiagnostics().LeasedCount;

    public DynamicStencilFixture(
        GL gl,
        int width,
        int height,
        bool twoOwners,
        bool useSpriteMask = false)
    {
        _gl = gl;
        _width = width;
        _height = height;
        _spriteShader = new SpriteShader(gl);
        _maskShader = new StencilMaskShader(gl);
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
        TextureRef alphaMaskTexture = _textures.RegisterRgba(
            "regression.stencil-alpha-mask",
            64,
            64,
            CreateAlphaMaskPixels(64, 64),
            TextureSampler.PixelArt);
        SpriteRef alphaMask = _sprites.RegisterSingle(
            "regression.stencil-alpha-mask",
            alphaMaskTexture,
            new Vector2(32, 32));

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

        _scene.Add(new SurfacePresentationOwner(
            _scene.RaiseEvent,
            RenderSurfaceKey.SceneColor,
            layer: 0,
            blend: PresentationBlendMode.Opaque));

        var secondMask = StencilMaskGeometry.Circle(new Vector2D(235, 155), 58f);
        _firstOwner = useSpriteMask
            ? _scene.Add(new SpriteMaskOwner(
                _scene.RaiseEvent,
                alphaMask,
                new Transform2D(
                    new Vector2D(width * 0.5f, height * 0.5f),
                    MathF.PI / 7f,
                    new Vector2D(2.3f, 1.7f))))
            : _scene.Add(new MaskOwner(
                _scene.RaiseEvent,
                new Vector2D(90, 85),
                52f,
                twoOwners ? secondMask : null));
        if (twoOwners)
            _secondOwner = _scene.Add(new MaskOwner(
                _scene.RaiseEvent, secondMask.Center, secondMask.Radius));

        _camera = new Camera2D(new Vector2(width, height));
        _sceneTarget = new RenderTarget2D(gl, width, height, withDepthStencil: true);
        _pool = new RenderTargetPool(gl);
        var scenePass = new SceneRenderPass("Scene", gl, _scene, _camera, _sceneTarget);
        _pipeline = new RenderPipeline(gl, width, height);
        _pipeline.AddPass(scenePass);
        _builder = new ScenePipelineBuilder(_pipeline, _pool, width, height);
        _builder.RegisterRootSurface(RenderSurfaceKey.SceneColor, _sceneTarget);
        _builder.RegisterFactory(new StencilMaskEffectFactory(
            gl, _scene, _camera, _spriteShader, _maskShader, white, _textures, _sprites));
        _builder.RegisterFactory(new PresentationEffectFactory(gl, _blitShader, _batch));
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
        _maskShader.Dispose();
        _spriteShader.Dispose();
    }

    private void AddTile(SpriteRef sprite, Vector2D position, Vector2D scale, Vector4 color) =>
        _scene.Add(new StaticSprite(sprite, position, scale, color));

    private static byte[] CreateAlphaMaskPixels(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float nx = (x + 0.5f) / width * 2f - 1f;
            float ny = (y + 0.5f) / height * 2f - 1f;
            bool insideDiamond = MathF.Abs(nx) + MathF.Abs(ny) <= 0.92f;
            bool centerHole = nx * nx + ny * ny < 0.13f;
            int offset = (y * width + x) * 4;
            pixels[offset] = 255;
            pixels[offset + 1] = 255;
            pixels[offset + 2] = 255;
            pixels[offset + 3] = insideDiamond && !centerHole ? (byte)255 : (byte)0;
        }
        return pixels;
    }

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
        private readonly StencilMaskGeometry? _additionalGeometry;

        public MaskOwner(
            Action<IDomainEvent> raiseEvent,
            Vector2D center,
            float radius,
            StencilMaskGeometry? additionalGeometry = null)
        {
            _raiseEvent = raiseEvent;
            _center = center;
            _radius = radius;
            _additionalGeometry = additionalGeometry;
        }

        public override void OnCreate()
        {
            if (_additionalGeometry is { } additional)
            {
                StencilMaskGeometry[] masks =
                [
                    StencilMaskGeometry.Circle(_center, _radius),
                    additional
                ];
                this.RequestStencilMasks(
                    StencilMaskEffectDescriptor.DefaultGroup,
                    masks,
                    StencilMaskState.Spotlight,
                    _raiseEvent);
            }
            else
            {
                this.RequestStencilMask(
                    StencilMaskEffectDescriptor.DefaultGroup,
                    _center,
                    _radius,
                    StencilMaskState.Spotlight,
                    _raiseEvent);
            }
            this.RequestPresentSurface(
                StencilMaskEffectDescriptor.MaskOutput(EffectKey),
                _raiseEvent,
                layer: 100,
                blend: PresentationBlendMode.AlphaBlend);
        }

        public override void OnDestroy()
        {
            this.ReleasePresentSurface(_raiseEvent);
            this.ReleaseStencilMask(StencilMaskEffectDescriptor.DefaultGroup, _raiseEvent);
        }
    }

    private sealed class SpriteMaskOwner : GameInstance
    {
        private readonly Action<IDomainEvent> _raiseEvent;
        private readonly SpriteRef _sprite;
        private readonly Transform2D _transform;

        public SpriteMaskOwner(
            Action<IDomainEvent> raiseEvent,
            SpriteRef sprite,
            Transform2D transform)
        {
            _raiseEvent = raiseEvent;
            _sprite = sprite;
            _transform = transform;
        }

        public override void OnCreate()
        {
            this.RequestStencilSpriteMask(
                _sprite,
                0f,
                _transform,
                0.5f,
                StencilMaskState.Spotlight,
                _raiseEvent);
            this.RequestPresentSurface(
                StencilMaskEffectDescriptor.MaskOutput(EffectKey),
                _raiseEvent,
                layer: 100,
                blend: PresentationBlendMode.AlphaBlend);
        }

        public override void OnDestroy()
        {
            this.ReleasePresentSurface(_raiseEvent);
            this.ReleaseStencilMask(_raiseEvent);
        }
    }
}

internal sealed class SurfacePresentationOwner(
    Action<IDomainEvent> raiseEvent,
    RenderSurfaceKey source,
    int layer,
    PresentationBlendMode blend) : GameInstance
{
    public override void OnCreate() => this.RequestPresentSurface(
        source,
        raiseEvent,
        layer,
        blend);

    public override void OnDestroy() => this.ReleasePresentSurface(raiseEvent);
}

namespace TileWorldStreaming.VisualTests;

using System.Diagnostics;
using System.Numerics;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;
using GameEngine.Features.Tilemaps.Domain;
using GameEngine.Features.TileWorldStreaming;
using GameEngine.Features.ViewportNavigation;
using GameEngine.Features.WorldStreaming;
using GameEngine.Hosting;

internal static class Program
{
    private static readonly SceneRef DemoScene = new("TileWorldStreaming.Visual");
    private static readonly Bounds2D WorldBounds = new(
        0f,
        0f,
        VisualWorldFixture.TileWorldSize,
        VisualWorldFixture.TileWorldSize);

    private static void Main(string[] args)
    {
        bool smoke = args.Contains("--smoke", StringComparer.Ordinal);
        Console.WriteLine("=== TileWorld Streaming Visual Test ===");
        Console.WriteLine("拖拽/滚轮：移动缩放 | Q/W/E：LOD2/LOD1/LOD0 | Space：自动巡游 | R：重放 Preview | ESC：退出");
        Console.WriteLine("紫色状态 = Preview；蓝色状态 = Raster LOD；绿色状态 = LOD0 Tile。");

        EngineWindowOptions options = (EngineWindowOptions.Default with
        {
            Title = "MyGameEngine - TileWorld Preview / LOD Visual Test",
            IsVisible = !smoke,
            VSync = !smoke
        }).WithFixedUpdateRate(60d);

        using GameApplication game = GameApplication
            .Create(options)
            .UseDefault2DRenderer(renderer => renderer.UseInteractiveViewport(navigation => navigation
                .Drag()
                .Pinch()
                .Wheel(new ViewportWheelOptions(smoothFrames: 5))
                .Decelerate()
                .ClampZoom(new ViewportClampZoomOptions(minScale: .45f, maxScale: 3.2f))
                .Clamp(new ViewportClampOptions(WorldBounds, underflow: ViewportUnderflow.Center))))
            .AddScene(DemoScene, context =>
            {
                context.Scene.Background = BackgroundConfig.FromColor(
                    new Vector4(.012f, .018f, .032f, 1f));
                context.Scene.Add(new TileWorldVisualController(context, smoke));
            })
            .StartScene(DemoScene)
            .Build();

        game.Run();
    }

    private sealed class TileWorldVisualController : GameInstance
    {
        private static readonly float[] TourZooms = [.7f, 1.4f, 2.6f];
        private readonly Default2DGameContext _context;
        private readonly bool _smoke;
        private readonly VisualWorldFixture _fixture;
        private readonly ViewportController _viewport;
        private readonly List<TextureRef> _ownedTextures = [];
        private readonly List<SpriteRef> _ownedSprites = [];
        private TileWorldStreamingSession _stream = null!;
        private TileSetRef _tileSet;
        private uint _whiteHandle;
        private TileWorldDrawStatistics _lastDraw;
        private TileWorldStreamingUpdateResult _lastUpdate;
        private TileWorldStreamingDiagnostics _diagnostics;
        private string _lastTitle = string.Empty;
        private double _tourElapsed;
        private int _tourPhase = -1;
        private int _smokeSteps;
        private int _smokeReadyStep = -1;
        private int _smokeStressPhase;
        private int _smokeStressStableFrames;
        private bool _smokeSawPreview;
        private bool _smokeRequestedLod0;
        private bool _autoTour = true;
        private bool _disposed;

        public TileWorldVisualController(Default2DGameContext context, bool smoke)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _smoke = smoke;
            _fixture = new VisualWorldFixture();
            ViewCulling = InstanceViewCullingMode.AlwaysVisible;
            TimeMode = InstanceTimeMode.Unscaled;
            try
            {
                RegisterDrawAssets();
                _tileSet = context.TileSets.Register(_fixture.TileSet);
                _viewport = context.GetViewportNavigation(context.RenderViews[0].Ref);
                _viewport.SetZoom(TourZooms[0]);
                _viewport.MoveCenter(new Vector2(
                    VisualWorldFixture.TileWorldSize * .5f,
                    VisualWorldFixture.TileWorldSize * .5f));
                _stream = CreateStream();
            }
            catch
            {
                DisposeOwnedResources();
                throw;
            }
        }

        public override void OnStep(double deltaTime)
        {
            if (_autoTour && !_smoke)
            {
                _tourElapsed += deltaTime;
                int phase = (int)(_tourElapsed / 4d) % TourZooms.Length;
                ApplyTourPhase(phase);
            }
            else if (_smoke)
            {
                _smokeSteps++;
                if (_smokeStressPhase == 1)
                {
                    _viewport.SetZoom(TourZooms[1]);
                    _smokeStressPhase = 2;
                }
                else if (_smokeStressPhase == 2 && _stream.PendingLevel == 1)
                {
                    _viewport.SetZoom(TourZooms[2]);
                    _smokeStressPhase = 3;
                }
                else if (!_smokeSawPreview && _lastDraw.FallbackSurfaceQuads > 0)
                {
                    _smokeSawPreview = true;
                    SetManualZoom(TourZooms[1]);
                }
                else if (_smokeSawPreview && !_smokeRequestedLod0 &&
                         _stream.ActiveLevel == 1 && _stream.PendingLevel is null)
                {
                    _smokeRequestedLod0 = true;
                    SetManualZoom(TourZooms[2]);
                }
            }

            bool retiringBlockedLod = _smoke && _smokeStressPhase == 3 &&
                                      _stream.PendingLevel == 1;
            long updateStarted = Stopwatch.GetTimestamp();
            _lastUpdate = _stream.Update(_viewport.CaptureSnapshot());
            TimeSpan updateElapsed = Stopwatch.GetElapsedTime(updateStarted);
            if (retiringBlockedLod && updateElapsed >= TimeSpan.FromMilliseconds(300))
                throw new InvalidOperationException(
                    $"Retiring a blocked LOD stalled Scene Step for {updateElapsed.TotalMilliseconds:0.###} ms.");
            _diagnostics = _stream.CaptureDiagnostics();
            UpdateTitle();

            if (!_smoke) return;
            bool ready = _smokeSawPreview && _diagnostics.FallbackSurfacesReady &&
                         _stream.ActiveLevel == 0 && _stream.PendingLevel is null;
            if (ready && _smokeReadyStep < 0) _smokeReadyStep = _smokeSteps;
            if (_smokeReadyStep >= 0 && _smokeStressPhase == 0)
                _smokeStressPhase = 1;
            else if (_smokeStressPhase == 3 && ready)
            {
                _smokeStressStableFrames++;
                if (_smokeStressStableFrames >= 3) _context.Close();
            }
            else if (_smokeSteps >= 180)
                throw new InvalidOperationException(
                    "TileWorld visual smoke did not reach committed LOD0 within 180 fixed steps.");
        }

        public override void OnDraw(ISpriteBatch batch)
        {
            _lastDraw = _stream.Draw(batch);
            DrawWorldBorder(batch);
        }

        public override void OnDrawGUI(ISpriteBatch batch)
        {
            Vector4 panel = new(.01f, .015f, .025f, .88f);
            batch.Draw(_whiteHandle, new Vector2(16f, 16f), new Vector2(352f, 58f), panel, UnitUv);

            int active = !_diagnostics.FallbackSurfacesReady || _lastDraw.FallbackSurfaceQuads > 0
                ? 0
                : _stream.ActiveLevel == 0 ? 2 : 1;
            Vector4[] colors =
            [
                new Vector4(.72f, .28f, .95f, 1f),
                new Vector4(.18f, .62f, 1f, 1f),
                new Vector4(.24f, .9f, .42f, 1f)
            ];
            for (int index = 0; index < colors.Length; index++)
            {
                Vector4 color = colors[index];
                if (index != active) color.W = .25f;
                batch.Draw(
                    _whiteHandle,
                    new Vector2(28f + index * 104f, 28f),
                    new Vector2(92f, index == active ? 18f : 8f),
                    color,
                    UnitUv);
            }

            float progress = ResolveLoadProgress();
            batch.Draw(_whiteHandle, new Vector2(28f, 56f), new Vector2(320f, 6f),
                new Vector4(.16f, .19f, .25f, 1f), UnitUv);
            batch.Draw(_whiteHandle, new Vector2(28f, 56f), new Vector2(320f * progress, 6f),
                colors[active], UnitUv);
        }

        public override void OnKeyDown(InputKey key)
        {
            switch (key)
            {
                case InputKey.Q:
                    SetManualZoom(TourZooms[0]);
                    break;
                case InputKey.W:
                    SetManualZoom(TourZooms[1]);
                    break;
                case InputKey.E:
                    SetManualZoom(TourZooms[2]);
                    break;
                case InputKey.Space:
                    _autoTour = !_autoTour;
                    _tourElapsed = 0d;
                    _tourPhase = -1;
                    break;
                case InputKey.R:
                    RestartStream();
                    break;
                case InputKey.Escape:
                    _context.Close();
                    break;
            }
        }

        public override void OnDestroy() => DisposeOwnedResources();

        private TileWorldStreamingSession CreateStream() => new(
            _fixture.Descriptor,
            _context.TileSets,
            _context.Textures,
            new VisualDelayDecoder(_smoke),
            new TileWorldStreamingOptions(
                new TileWorldLodSelectionOptions(
                    targetPixelsPerTexel: 4f,
                    hysteresisRatio: .08f),
                new WorldChunkStreamingOptions(
                    preloadMarginChunks: 0,
                    retainMarginChunks: 1,
                    maximumConcurrentLoads: 2,
                    maximumTrackedChunks: 64,
                    retryFailedOnViewportChange: true,
                    maximumLoadsStartedPerUpdate: 1),
                TileWorldChunkLoadMode.Background,
                new TileWorldTextureUploadBudget(
                    maximumTexturesPerUpdate: 1,
                    maximumBytesPerUpdate: 512 * 1_024)));

        private void RegisterDrawAssets()
        {
            string[] names = ["grass", "sand", "stone", "water"];
            for (int index = 0; index < names.Length; index++)
            {
                string textureName = $"visual.world.{names[index]}.texture";
                TextureRef texture = _context.Textures.RegisterRgba(
                    textureName,
                    VisualWorldFixture.TilePixelSize,
                    VisualWorldFixture.TilePixelSize,
                    VisualWorldFixture.CreateTilePixels(index),
                    TextureSampler.PixelArt);
                _ownedTextures.Add(texture);
                _ownedSprites.Add(_context.Sprites.RegisterSingle(
                    $"visual.world.{names[index]}",
                    texture,
                    new Vector2(256f),
                    new Vector2(128f)));
            }

            TextureRef road = _context.Textures.RegisterRgba(
                "visual.world.road.texture",
                VisualWorldFixture.TilePixelSize,
                VisualWorldFixture.TilePixelSize,
                VisualWorldFixture.CreateRoadPixels(),
                TextureSampler.PixelArt);
            _ownedTextures.Add(road);
            _ownedSprites.Add(_context.Sprites.RegisterSingle(
                "visual.world.road",
                road,
                new Vector2(256f),
                new Vector2(128f)));

            TextureRef white = _context.Textures.RegisterRgba(
                "visual.world.ui.white",
                1,
                1,
                [255, 255, 255, 255],
                TextureSampler.PixelArt);
            _ownedTextures.Add(white);
            if (!_context.Textures.TryResolve(white, out ResolvedTexture resolved))
                throw new InvalidOperationException("Visual-test white Texture did not resolve.");
            _whiteHandle = resolved.Handle;
        }

        private void RestartStream()
        {
            _stream.Dispose();
            _viewport.SetZoom(TourZooms[0]);
            _viewport.MoveCenter(new Vector2(
                VisualWorldFixture.TileWorldSize * .5f,
                VisualWorldFixture.TileWorldSize * .5f));
            _tourElapsed = 0d;
            _tourPhase = 0;
            _stream = CreateStream();
            _lastDraw = default;
            _diagnostics = default;
            Console.WriteLine("  [restart] Preview/Fallback Session 已重新创建");
        }

        private void ApplyTourPhase(int phase)
        {
            if (_tourPhase == phase) return;
            _tourPhase = phase;
            AnimateZoom(TourZooms[phase]);
        }

        private void SetManualZoom(float zoom)
        {
            _autoTour = false;
            if (_smoke) _viewport.SetZoom(zoom);
            else AnimateZoom(zoom);
        }

        private void AnimateZoom(float zoom) =>
            _viewport.Plugins.Add(new ViewportAnimatePlugin(new ViewportAnimateOptions(
                zoom: zoom,
                durationSeconds: .65d,
                easing: EasingKind.SmootherStep,
                interruptMode: ViewportMotionInterruptMode.Cancel)));

        private void UpdateTitle()
        {
            string source = !_diagnostics.FallbackSurfacesReady
                ? "DECODING PREVIEW"
                : _lastDraw.FallbackSurfaceQuads > 0
                    ? "PREVIEW FALLBACK"
                    : _stream.ActiveLevel == 0
                        ? "LOD0 TILES"
                        : $"LOD{_stream.ActiveLevel} RASTER";
            string pending = _stream.PendingLevel is { } level ? $" → LOD{level}" : string.Empty;
            string title = $"TileWorld | {source}{pending} | Zoom {_viewport.Zoom:0.00} | " +
                           $"Preview {_diagnostics.ResidentFallbackSurfaces} | " +
                           $"Upload {_lastUpdate.TexturesUploaded}/{_lastUpdate.TextureBytesUploaded / 1_024f:0.#} KiB | " +
                           $"Retiring {_diagnostics.RetiringLevels} | " +
                           $"Fallback draws {_lastDraw.FallbackQuads}/{_lastDraw.FallbackSurfaceQuads}";
            if (StringComparer.Ordinal.Equals(title, _lastTitle)) return;
            _lastTitle = title;
            _context.Window.NativeWindow.Title = title;
            Console.WriteLine("  " + title);
        }

        private float ResolveLoadProgress()
        {
            WorldChunkStreamingDiagnostics source = _diagnostics.Pending ??
                (_stream.ActiveLevel == _stream.FallbackLevel
                    ? _diagnostics.Fallback
                    : _diagnostics.Active);
            if (source.VisibleCount <= 0) return _diagnostics.FallbackSurfacesReady ? 1f : 0f;
            return Math.Clamp((float)source.LoadedCount / source.VisibleCount, 0f, 1f);
        }

        private void DrawWorldBorder(ISpriteBatch batch)
        {
            const float size = VisualWorldFixture.TileWorldSize;
            const float thickness = 7f;
            Vector4 color = new(.78f, .9f, 1f, .9f);
            batch.Draw(_whiteHandle, Vector2.Zero, new Vector2(size, thickness), color, UnitUv);
            batch.Draw(_whiteHandle, new Vector2(0f, size - thickness),
                new Vector2(size, thickness), color, UnitUv);
            batch.Draw(_whiteHandle, Vector2.Zero, new Vector2(thickness, size), color, UnitUv);
            batch.Draw(_whiteHandle, new Vector2(size - thickness, 0f),
                new Vector2(thickness, size), color, UnitUv);
        }

        private void DisposeOwnedResources()
        {
            if (_disposed) return;
            _disposed = true;
            _stream?.Dispose();
            if (!_tileSet.IsEmpty) _context.TileSets.Remove(_tileSet);
            for (int index = _ownedSprites.Count - 1; index >= 0; index--)
                _context.Sprites.Remove(_ownedSprites[index]);
            for (int index = _ownedTextures.Count - 1; index >= 0; index--)
            {
                try { _context.Textures.Remove(_ownedTextures[index]); }
                catch (ObjectDisposedException) { }
            }
            _fixture.Dispose();
        }

        private static Vector4 UnitUv => new(0f, 0f, 1f, 1f);
    }

    private sealed class VisualDelayDecoder : IImageDecoder
    {
        private readonly SkiaImageDecoder _decoder = new();
        private readonly bool _smoke;

        public VisualDelayDecoder(bool smoke) => _smoke = smoke;

        public DecodedImage Decode(Stream stream)
        {
            DecodedImage image = _decoder.Decode(stream);
            int delayMilliseconds = image.Width <= 128
                ? (_smoke ? 0 : 90)
                : (_smoke ? 80 : 850);
            if (delayMilliseconds > 0) Thread.Sleep(delayMilliseconds);
            return image;
        }
    }
}

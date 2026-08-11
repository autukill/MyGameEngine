namespace TileWorldStreaming.Tests;

using System.Diagnostics;
using System.Numerics;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;
using GameEngine.Features.Tilemaps.Domain;
using GameEngine.Features.Tilemaps.Infrastructure;
using GameEngine.Features.TileWorlds.Domain;
using GameEngine.Features.TileWorlds.Infrastructure;
using GameEngine.Features.TileWorldStreaming;
using GameEngine.Features.ViewportNavigation;
using GameEngine.Features.WorldStreaming;

internal static class Program
{
    private static int _failures;

    private static async Task Main()
    {
        Console.WriteLine("=== TileWorldStreaming Feature Smoke Tests ===");
        using var fixture = new WorldFixture();
        Run("Zoom thresholds use stable multiplicative hysteresis", () => VerifyLodSelector(fixture));
        await RunAsync("Archive decode and Texture commit keep ownership explicit", () => VerifyLoader(fixture));
        await RunAsync("Fallback Surface decode and Texture ownership remain explicit", () =>
            VerifyFallbackSurfaceLoader(fixture));
        await RunAsync("Fallback Surface covers a visible region while coarse LOD decodes", () =>
            VerifySessionFallbackSurface(fixture));
        Run("GPU uploads obey deterministic per-update budgets", () => VerifyUploadBudget(fixture));
        await RunAsync("LOD replacement retires blocked work without joining", () =>
            VerifyNonBlockingLodRetirement(fixture));
        Run("Session retains coarse fallback until detailed coverage is complete", () => VerifySession(fixture));
        Console.WriteLine(_failures == 0
            ? "=== All TileWorldStreaming smoke tests passed ==="
            : $"=== {_failures} TileWorldStreaming test(s) FAILED ===");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }

    private static void VerifyLodSelector(WorldFixture fixture)
    {
        var selector = new TileWorldLodSelector(
            fixture.Descriptor.Metadata,
            new TileWorldLodSelectionOptions(1f, 0.1f));
        Check(MathF.Abs(selector.GetBoundaryZoom(0) - 0.5f) < 0.0001f,
            "LOD0/1 boundary should be derived from raster density.");
        Check(selector.Select(1f) == 0, "Zoom 1 should select authoritative LOD0.");
        Check(selector.Select(0.46f) == 0, "Zoom inside the dead band should retain LOD0.");
        Check(selector.Select(0.44f) == 1, "Crossing the coarse threshold should select LOD1.");
        Check(selector.Select(0.52f) == 1, "Reverse jitter inside the dead band should retain LOD1.");
        Check(selector.Select(0.56f) == 0, "Crossing the fine threshold should restore LOD0.");
        Check(selector.Select(0.1f) == 2, "Large zoom changes may skip directly to the coarsest level.");
        selector.Reset();
        Check(selector.CurrentLevel is null, "Reset removes selector history.");
        Throws<ArgumentOutOfRangeException>(() => new TileWorldLodSelectionOptions(0f, 0.1f));
    }

    private static async Task VerifyLoader(WorldFixture fixture)
    {
        var decoder = new FakeDecoder();
        var backend = new FakeTextureBackend();
        using var textures = new TextureLibrary(backend, decoder);
        using (var loader = new TileWorldChunkLoader(
                   fixture.Descriptor,
                   2,
                   "loader-test",
                   decoder,
                   TileWorldChunkLoadMode.Background))
        {
            TileWorldChunkLease lease = await loader.LoadAsync(
                new WorldChunkCoordinate(0, 0), CancellationToken.None);
            Check(lease.HasPayload && !lease.IsCommitted && decoder.DecodeCount == 2,
                "Raster WebP layers should decode without touching the GPU.");
            Check(textures.Count == 0, "Background preparation must not mutate TextureLibrary.");
            lease.CommitTextures(textures);
            Check(lease.IsCommitted && lease.RasterLayers.Count == 2 && textures.Count == 2,
                "Main-thread commit should register every prepared Layer atomically.");
            Check(backend.Samplers.All(value => value == TextureSampler.PixelArt),
                "Runtime upload should inherit the TileWorld raster sampler.");
            Vector4 uv = lease.RasterLayers[0].InnerUvBounds;
            Check(Vector4.Distance(uv, new Vector4(1f / 6f, 1f / 6f, 5f / 6f, 5f / 6f)) < 0.0001f,
                "Runtime UV should exclude the one-pixel Gutter.");
            lease.Dispose();
            lease.Dispose();
            Check(textures.Count == 0 && backend.DeleteCount == 2,
                "Idempotent lease disposal should release all owned GPU textures.");
            await ThrowsAsync<ArgumentOutOfRangeException>(() => loader.LoadAsync(
                new WorldChunkCoordinate(2, 0), CancellationToken.None).AsTask());
        }

        using (var missingLoader = new TileWorldChunkLoader(
                   fixture.Descriptor,
                   1,
                   "missing-test",
                   decoder,
                   TileWorldChunkLoadMode.Inline))
        using (TileWorldChunkLease missing = await missingLoader.LoadAsync(
                   new WorldChunkCoordinate(1, 0), CancellationToken.None))
        {
            missing.CommitTextures(textures);
            Check(!missing.HasPayload && missing.IsCommitted,
                "Sparse in-bounds archive holes should become ready transparent leases.");
        }

        using (var lod0Loader = new TileWorldChunkLoader(
                   fixture.Descriptor,
                   0,
                   "lod0-test",
                   decoder,
                   TileWorldChunkLoadMode.Inline))
        using (TileWorldChunkLease lod0 = await lod0Loader.LoadAsync(
                   new WorldChunkCoordinate(0, 0), CancellationToken.None))
        {
            lod0.CommitTextures(textures);
            Check(lod0.AuthoritativeData is not null && lod0.RasterLayers.Count == 0,
                "LOD0 should retain authoritative Tile and collision data without GPU upload.");
        }

        var failingBackend = new FakeTextureBackend { FailCreateAttempt = 2 };
        using var failingTextures = new TextureLibrary(failingBackend, decoder);
        using var rollbackLoader = new TileWorldChunkLoader(
            fixture.Descriptor,
            2,
            "rollback-test",
            decoder,
            TileWorldChunkLoadMode.Inline);
        using TileWorldChunkLease rollback = await rollbackLoader.LoadAsync(
            new WorldChunkCoordinate(0, 0), CancellationToken.None);
        Throws<InvalidOperationException>(() => rollback.CommitTextures(failingTextures));
        Check(failingTextures.Count == 0 && failingBackend.DeleteCount == 1,
            "A later upload failure should roll back textures registered by the same commit.");
    }

    private static void VerifyUploadBudget(WorldFixture fixture)
    {
        Throws<ArgumentOutOfRangeException>(() => new TileWorldTextureUploadBudget(0, 1));
        Throws<ArgumentOutOfRangeException>(() => new TileWorldTextureUploadBudget(1, 0));

        var decoder = new FakeDecoder();
        var backend = new FakeTextureBackend();
        using var textures = new TextureLibrary(backend, decoder);
        var options = new TileWorldStreamingOptions(
            new TileWorldLodSelectionOptions(1f, 0.1f),
            new WorldChunkStreamingOptions(0, 0, 1, 8, true, 1),
            TileWorldChunkLoadMode.Inline,
            new TileWorldTextureUploadBudget(2, 1));
        using var session = new TileWorldStreamingSession(
            fixture.Descriptor,
            fixture.TileSets,
            textures,
            decoder,
            options);
        ViewportSnapshot view = Snapshot(0f, 0f, 4f, 4f, 0.1f, 200);

        TileWorldStreamingUpdateResult first = session.Update(view);
        Check(first.TexturesUploaded == 1 && first.TextureBytesUploaded == 144 &&
              textures.Count == 1 && session.FallbackSurfacesReady,
            "The first oversized Preview upload may exceed the byte budget to guarantee progress");
        TileWorldStreamingUpdateResult second = session.Update(view);
        Check(second.TexturesUploaded == 1 && textures.Count == 2,
            "Only one prepared Raster Layer should upload in the next update");
        var batch = new RecordingBatch();
        TileWorldDrawStatistics staged = session.Draw(batch);
        Check(staged.FallbackSurfaceQuads == 1 && staged.RasterQuads == 1,
            "A partially uploaded Chunk must remain invisible while Preview covers its missing layer");

        TileWorldStreamingUpdateResult third = session.Update(view);
        batch.Reset();
        TileWorldDrawStatistics committed = session.Draw(batch);
        Check(third.TexturesUploaded == 1 && textures.Count == 3 &&
              committed.FallbackSurfaceQuads == 0 && committed.RasterQuads == 2,
            "The Chunk should publish atomically after its final Layer upload");
        session.Dispose();
        Check(textures.Count == 0,
            "Session disposal should remove committed and staged budgeted uploads");

        var countBackend = new FakeTextureBackend();
        using var countTextures = new TextureLibrary(countBackend, decoder);
        var countOptions = new TileWorldStreamingOptions(
            new TileWorldLodSelectionOptions(1f, 0.1f),
            new WorldChunkStreamingOptions(0, 0, 1, 8, true, 1),
            TileWorldChunkLoadMode.Inline,
            new TileWorldTextureUploadBudget(1, 1_024 * 1_024));
        using var countSession = new TileWorldStreamingSession(
            fixture.Descriptor,
            fixture.TileSets,
            countTextures,
            decoder,
            countOptions);
        TileWorldStreamingUpdateResult countLimited = countSession.Update(view);
        Check(countLimited.TexturesUploaded == 1 && countTextures.Count == 1,
            "Texture-count budget should stop additional uploads even when byte capacity remains");
    }

    private static async Task VerifyNonBlockingLodRetirement(WorldFixture fixture)
    {
        var decoder = new BlockingSelectedRasterDecoder();
        var backend = new FakeTextureBackend();
        using var textures = new TextureLibrary(backend, decoder);
        var options = new TileWorldStreamingOptions(
            new TileWorldLodSelectionOptions(1f, 0.1f),
            new WorldChunkStreamingOptions(0, 0, 1, 16, true, 1),
            TileWorldChunkLoadMode.Background,
            new TileWorldTextureUploadBudget(8, 8 * 1_024 * 1_024));
        TileWorldStreamingSession? session = null;
        try
        {
            session = new TileWorldStreamingSession(
                fixture.Descriptor,
                fixture.TileSets,
                textures,
                decoder,
                options);
            ViewportSnapshot lod1 = Snapshot(0f, 0f, 8f, 4f, 0.3f, 300);
            session.Update(lod1);
            Check(decoder.WaitUntilBlocked(TimeSpan.FromSeconds(5)),
                "LOD1 decode should be blocked before replacement");

            long started = Stopwatch.GetTimestamp();
            TileWorldStreamingUpdateResult changed = session.Update(
                Snapshot(0f, 0f, 8f, 4f, 1f, 301));
            TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
            Check(elapsed < TimeSpan.FromMilliseconds(250) && changed.RetiringLevels == 1,
                $"LOD replacement must retire without joining blocked decode ({elapsed.TotalMilliseconds:0.###} ms)");

            decoder.Release();
            for (int attempt = 0; attempt < 500 &&
                 session.CaptureDiagnostics().RetiringLevels > 0; attempt++)
            {
                await Task.Delay(1);
                session.Update(Snapshot(0f, 0f, 8f, 4f, 1f, 301));
            }
            Check(session.CaptureDiagnostics().RetiringLevels == 0,
                "Retired LOD work should be drained and release its prepared lease");
        }
        finally
        {
            decoder.Release();
            session?.Dispose();
        }
    }

    private static async Task VerifyFallbackSurfaceLoader(WorldFixture fixture)
    {
        var decoder = new FakeDecoder();
        var backend = new FakeTextureBackend();
        using var textures = new TextureLibrary(backend, decoder);
        var loader = new TileWorldFallbackSurfaceLoader(
            fixture.Descriptor,
            "fallback-loader-test",
            decoder,
            TileWorldChunkLoadMode.Inline);
        using TileWorldFallbackSurfaceLease lease = await loader.LoadAsync(
            CancellationToken.None);
        Check(!lease.IsCommitted && lease.Surfaces.Count == 0 && decoder.DecodeCount == 1,
            "Fallback WebP should decode without touching the GPU.");
        lease.CommitTextures(textures);
        Check(lease.IsCommitted && lease.Surfaces.Count == 1 && textures.Count == 1 &&
              backend.Samplers.Single() == TextureSampler.Smooth,
            "Main-thread commit should upload the declared per-Layer fallback sampler.");
        Check(lease.TryGet(0, out _) && !lease.TryGet(1, out _),
            "Fallback surfaces retain their explicit TileWorld Layer binding.");
        lease.Dispose();
        Check(textures.Count == 0 && backend.DeleteCount == 1,
            "Fallback lease disposal releases its owned Texture exactly once.");
    }

    private static async Task VerifySessionFallbackSurface(WorldFixture fixture)
    {
        var decoder = new BlockingRasterDecoder();
        var backend = new FakeTextureBackend();
        using var textures = new TextureLibrary(backend, decoder);
        var options = new TileWorldStreamingOptions(
            new TileWorldLodSelectionOptions(1f, 0.1f),
            new WorldChunkStreamingOptions(
                preloadMarginChunks: 0,
                retainMarginChunks: 0,
                maximumConcurrentLoads: 1,
                maximumTrackedChunks: 8,
                retryFailedOnViewportChange: true,
                maximumLoadsStartedPerUpdate: 1),
            TileWorldChunkLoadMode.Background);
        TileWorldStreamingSession? session = null;
        try
        {
            session = new TileWorldStreamingSession(
                fixture.Descriptor,
                fixture.TileSets,
                textures,
                decoder,
                options);
            ViewportSnapshot view = Snapshot(0f, 0f, 4f, 4f, 0.1f, 100);
            session.Update(view);
            for (int attempt = 0; attempt < 200 && textures.Count == 0; attempt++)
            {
                await Task.Delay(1);
                session.Update(view);
            }
            Check(textures.Count == 1 && decoder.FallbackDecoded,
                "The small fallback surface should become resident independently of blocked Chunk decode.");
            TileWorldStreamingDiagnostics diagnostics = session.CaptureDiagnostics();
            Check(diagnostics.HasFallbackSurfaces && diagnostics.FallbackSurfacesReady &&
                  diagnostics.ResidentFallbackSurfaces == 1,
                "Diagnostics expose declared and resident fallback surface state without GPU handles.");
            var batch = new RecordingBatch();
            TileWorldDrawStatistics draw = session.Draw(batch);
            Check(draw.MissingActiveChunks == 1 && draw.FallbackSurfaceQuads == 1 &&
                  draw.FallbackQuads == 0 && batch.Draws.Count == 1,
                "A missing coarse Chunk should crop and draw the matching full-world fallback region.");
            Check(Vector4.Distance(batch.Draws[0].Uv, new Vector4(0f, 0f, 1f, 1f)) < 0.0001f,
                "A full-world visible region should use the complete fallback UV range.");
        }
        finally
        {
            decoder.ReleaseRaster();
            session?.Dispose();
        }
    }

    private static void VerifySession(WorldFixture fixture)
    {
        var decoder = new FakeDecoder();
        var backend = new FakeTextureBackend();
        using var textures = new TextureLibrary(backend, decoder);
        var options = new TileWorldStreamingOptions(
            new TileWorldLodSelectionOptions(1f, 0.1f),
            new WorldChunkStreamingOptions(
                preloadMarginChunks: 0,
                retainMarginChunks: 0,
                maximumConcurrentLoads: 4,
                maximumTrackedChunks: 16,
                retryFailedOnViewportChange: true,
                maximumLoadsStartedPerUpdate: 1),
            TileWorldChunkLoadMode.Inline,
            new TileWorldTextureUploadBudget(8, 8 * 1_024 * 1_024));
        using (var session = new TileWorldStreamingSession(
                   fixture.Descriptor,
                   fixture.TileSets,
                   textures,
                   decoder,
                   options))
        {
            ViewportSnapshot firstView = Snapshot(0f, 0f, 8f, 4f, 1f, 1);
            TileWorldStreamingUpdateResult first = session.Update(firstView);
            Check(first.ActiveLevel == 2 && first.PendingLevel == 0 && !first.LevelChanged,
                "The coarse fallback should remain active while one detailed Chunk is missing.");
            var batch = new RecordingBatch();
            TileWorldDrawStatistics fallbackDraw = session.Draw(batch);
            Check(fallbackDraw.RasterQuads == 2 && fallbackDraw.TileSprites == 0,
                "The coarsest resident Layer set should draw while detail prepares.");

            TileWorldStreamingUpdateResult second = session.Update(firstView);
            Check(second.ActiveLevel == 0 && second.PendingLevel is null && second.LevelChanged,
                "Detailed LOD should replace fallback only after complete visible coverage.");
            batch.Reset();
            TileWorldDrawStatistics detailedDraw = session.Draw(batch);
            Check(detailedDraw.TileSprites == 4 && detailedDraw.RasterQuads == 0,
                "Authoritative LOD0 should render both visible Chunks in Layer order.");
            batch.Reset();
            TileWorldDrawStatistics oneLayer = session.DrawLayer(batch, 0);
            Check(oneLayer.TileSprites == 2 && batch.SpriteCommands == 2,
                "One Layer can be drawn independently for gameplay depth interleaving.");
            Throws<ArgumentOutOfRangeException>(() => session.DrawLayer(batch, 2));

            ViewportSnapshot moved = Snapshot(8f, 0f, 16f, 4f, 1f, 2);
            session.Update(moved);
            batch.Reset();
            TileWorldDrawStatistics mixed = session.Draw(batch);
            Check(mixed.MissingActiveChunks == 1 && mixed.TileSprites == 2 &&
                  mixed.FallbackQuads == 2,
                "Missing detailed coverage should sample matching regions from coarse fallback.");
            Check(batch.Draws.All(draw => draw.Uv.Z > draw.Uv.X && draw.Uv.W > draw.Uv.Y),
                "Fallback crops should retain positive inner UV regions.");

            session.Update(moved);
            for (int index = 0; index < 4_096; index++) session.Update(moved);
            long firstProbe = MeasureStableUpdates(session, moved);
            long secondProbe = MeasureStableUpdates(session, moved);
            Check(Math.Min(firstProbe, secondProbe) == 0,
                "A stable fully loaded Session should allocate 0 B " +
                $"(Tiered JIT probes: {firstProbe:N0} B, {secondProbe:N0} B).");

            TileWorldStreamingUpdateResult coarse = session.Update(
                Snapshot(8f, 0f, 16f, 4f, 0.1f, 3));
            Check(coarse.ActiveLevel == 2 && coarse.LevelChanged,
                "Zooming far out should return directly to the persistent fallback state.");
        }
        Check(textures.Count == 0,
            "Session disposal should release fallback and active Texture leases.");
    }

    private static ViewportSnapshot Snapshot(
        float left,
        float top,
        float right,
        float bottom,
        float zoom,
        ulong revision) => new(
            new Bounds2D(left, top, right, bottom),
            new Vector2((left + right) * 0.5f, (top + bottom) * 0.5f),
            zoom,
            new Vector2((right - left) * zoom, (bottom - top) * zoom),
            revision);

    private static long MeasureStableUpdates(
        TileWorldStreamingSession session,
        in ViewportSnapshot viewport)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 10_000; index++) session.Update(viewport);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"  [PASS] {name}");
        }
        catch (Exception exception)
        {
            _failures++;
            Console.WriteLine($"  [FAIL] {name}: {exception}");
        }
    }

    private static async Task RunAsync(string name, Func<Task> test)
    {
        try
        {
            await test();
            Console.WriteLine($"  [PASS] {name}");
        }
        catch (Exception exception)
        {
            _failures++;
            Console.WriteLine($"  [FAIL] {name}: {exception}");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class WorldFixture : IDisposable
    {
        private readonly string _directory;

        public WorldFixture()
        {
            _directory = Path.Combine(Path.GetTempPath(), "mygame-tileworld-streaming-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            TileSets = new TileSetLibrary();
            TileSets.Register(new TileSet(
                "stream.tiles",
                new Vector2(4f, 4f),
                [new TileDefinition(new TileId(1), new SpriteRef("stream.tile"))]));
            var map = new TileMap("stream.world", 1, 1);
            TileLayer ground = map.AddLayer("ground", new TileSetRef("stream.tiles"), -1);
            TileLayer overlay = map.AddLayer("overlay", new TileSetRef("stream.tiles"), 1);
            for (int x = 0; x < 4; x++)
            {
                ground.SetCell(x, 0, new TileCell(new TileId(1)));
                overlay.SetCell(x, 0, new TileCell(new TileId(1)));
            }
            TileWorldArchiveBuild lod0 = TileWorldArchiveBuilder.BuildLod0(
                map,
                TileSets,
                new TileWorldChunkBounds(0, 0, 3, 0),
                3,
                new TileWorldRasterSettings(4, 4, 1, TileWorldRasterSampling.PixelArt));
            var rasterLod1 = new TileWorldRasterChunkData(
                new TileWorldChunkKey(1, 0, 0),
                [
                    new TileWorldRasterLayerData(0, 4, 4, 1,
                        TileWorldRasterEncoding.WebpLossless, FakeWebp(53)),
                    new TileWorldRasterLayerData(1, 4, 4, 1,
                        TileWorldRasterEncoding.WebpLossless, FakeWebp(59))
                ]);
            var rasterLod2 = new TileWorldRasterChunkData(
                new TileWorldChunkKey(2, 0, 0),
                [
                    new TileWorldRasterLayerData(0, 4, 4, 1,
                        TileWorldRasterEncoding.WebpLossless, FakeWebp(17)),
                    new TileWorldRasterLayerData(1, 4, 4, 1,
                        TileWorldRasterEncoding.WebpLossless, FakeWebp(29))
                ]);
            var fallback = new TileWorldFallbackSurfaceData(
                0,
                6,
                6,
                TileWorldRasterEncoding.WebpLossless,
                TileWorldRasterSampling.Smooth,
                FakeWebp(41));
            var metadata = new TileWorldMetadata(
                lod0.Metadata.Name,
                lod0.Metadata.ChunkWidth,
                lod0.Metadata.ChunkHeight,
                lod0.Metadata.TileSize,
                lod0.Metadata.Bounds,
                lod0.Metadata.DeclaredLodCount,
                lod0.Metadata.RasterSettings,
                lod0.Metadata.Layers,
                [fallback.Metadata]);
            string archivePath = Path.Combine(_directory, "stream.mgworld");
            using (FileStream stream = File.Create(archivePath))
                TileWorldArchiveWriter.Write(
                    stream,
                    new TileWorldArchiveBuild(
                        metadata,
                        lod0.Chunks,
                        [rasterLod1, rasterLod2],
                        [fallback]));
            Descriptor = new TileWorldDescriptor(metadata.Ref, archivePath, metadata);
        }

        public TileSetLibrary TileSets { get; }
        public TileWorldDescriptor Descriptor { get; }

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }

        private static byte[] FakeWebp(byte marker)
        {
            byte[] bytes = new byte[13];
            "RIFF"u8.CopyTo(bytes);
            "WEBP"u8.CopyTo(bytes.AsSpan(8));
            bytes[12] = marker;
            return bytes;
        }
    }

    private sealed class FakeDecoder : IImageDecoder
    {
        public int DecodeCount { get; private set; }

        public DecodedImage Decode(Stream stream)
        {
            DecodeCount++;
            int marker = stream.ReadByte();
            var pixels = new byte[6 * 6 * 4];
            for (int index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = (byte)(marker < 0 ? 127 : marker);
                pixels[index + 1] = 80;
                pixels[index + 2] = 40;
                pixels[index + 3] = 255;
            }
            return new DecodedImage(6, 6, pixels);
        }
    }

    private sealed class BlockingRasterDecoder : IImageDecoder
    {
        private readonly ManualResetEventSlim _rasterGate = new(false);
        public bool FallbackDecoded { get; private set; }

        public DecodedImage Decode(Stream stream)
        {
            stream.Position = stream.Length - 1;
            int marker = stream.ReadByte();
            if (marker == 41)
                FallbackDecoded = true;
            else
                _rasterGate.Wait(TimeSpan.FromSeconds(10));
            var pixels = new byte[6 * 6 * 4];
            for (int index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = (byte)marker;
                pixels[index + 3] = 255;
            }
            return new DecodedImage(6, 6, pixels);
        }

        public void ReleaseRaster() => _rasterGate.Set();
    }

    private sealed class BlockingSelectedRasterDecoder : IImageDecoder
    {
        private readonly ManualResetEventSlim _blocked = new(false);
        private readonly ManualResetEventSlim _release = new(false);

        public DecodedImage Decode(Stream stream)
        {
            stream.Position = stream.Length - 1;
            int marker = stream.ReadByte();
            if (marker is 53 or 59)
            {
                _blocked.Set();
                _release.Wait(TimeSpan.FromSeconds(10));
            }
            var pixels = new byte[6 * 6 * 4];
            for (int index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = (byte)marker;
                pixels[index + 3] = 255;
            }
            return new DecodedImage(6, 6, pixels);
        }

        public bool WaitUntilBlocked(TimeSpan timeout) => _blocked.Wait(timeout);
        public void Release() => _release.Set();
    }

    private sealed class FakeTextureBackend : ITextureBackend
    {
        private uint _nextHandle = 1;
        public int CreateAttempt { get; private set; }
        public int DeleteCount { get; private set; }
        public int? FailCreateAttempt { get; init; }
        public List<TextureSampler> Samplers { get; } = [];

        public uint CreateTexture(
            int width,
            int height,
            ReadOnlySpan<byte> rgbaPixels,
            TextureSampler sampler)
        {
            CreateAttempt++;
            if (CreateAttempt == FailCreateAttempt)
                throw new InvalidOperationException("simulated upload failure");
            Samplers.Add(sampler);
            return _nextHandle++;
        }

        public void DeleteTexture(uint handle) => DeleteCount++;
    }

    private sealed class RecordingBatch : ISpriteBatch
    {
        public readonly List<(uint Handle, Vector4 Uv)> Draws = [];
        public int SpriteCommands { get; private set; }

        public void Reset()
        {
            Draws.Clear();
            SpriteCommands = 0;
        }

        public void Begin() { }
        public void End() { }
        public void Draw(uint textureHandle, Vector2 position, Vector2 size, Vector4 color, Vector4 uvBounds) =>
            Draws.Add((textureHandle, uvBounds));
        public void DrawSpriteCommand(in SpriteDrawCommand command) => SpriteCommands++;
        public bool TryGetSpriteMetadata(SpriteRef sprite, out SpriteMetadata metadata)
        {
            metadata = default;
            return false;
        }
        public void Flush() { }
        public void SetBlendMode(BlendMode mode) { }
        public void SetDepthState(bool depthTest, bool depthWrite) { }
        public void SetShader(ShaderRef? shader) { }
        public void SetMaterial(MaterialRef? material) { }
    }
}

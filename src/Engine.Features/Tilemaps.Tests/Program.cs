namespace Tilemaps.Tests;

using System.Numerics;
using System.Text;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.Tilemaps.Domain;
using GameEngine.Features.Tilemaps.Infrastructure;

internal static class Program
{
    private static int _failures;

    private static void Main()
    {
        Console.WriteLine("=== Tilemaps Feature Smoke Tests ===");
        VerifyTileSetAndLibraries();
        VerifySparseChunksAndNegativeCoordinates();
        VerifyVisibleRendering();
        VerifyCollisionBaking();
        VerifyManifest();
        VerifySteadyStateAllocation();
        Console.WriteLine(_failures == 0
            ? "=== All Tilemaps smoke tests passed ==="
            : $"=== {_failures} Tilemaps test(s) FAILED ===");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }

    private static void VerifyTileSetAndLibraries()
    {
        Console.WriteLine("1. Logical TileSet and library ownership");
        TileSet tileSet = CreateTileSet();
        var sets = new TileSetLibrary();
        TileSetRef reference = sets.Register(tileSet);
        Check(reference == new TileSetRef("world.tiles") && sets.Get(reference) == tileSet,
            "TileSet registers as a logical reference");
        Check(tileSet.TryGet(new TileId(2), out TileDefinition solid) &&
              solid.Collision == TileCollisionKind.Solid,
            "Tile definitions retain Sprite frame and collision metadata");
        CheckThrows<ArgumentException>(() => sets.Register(tileSet),
            "Duplicate TileSet name is rejected");
        CheckThrows<ArgumentException>(() => new TileSet("bad", Vector2.One,
            [new TileDefinition(TileId.Empty, new SpriteRef("sprite"))]),
            "Tile id zero remains reserved");

        var maps = new TileMapLibrary();
        var map = new TileMap("level.one", 4, 4);
        maps.Register(map);
        Check(maps.Get(map.Ref) == map && maps.Remove(map.Ref) && maps.Count == 0,
            "TileMap library has explicit register/remove ownership");
    }

    private static void VerifySparseChunksAndNegativeCoordinates()
    {
        Console.WriteLine("2. Sparse Chunk storage and negative coordinates");
        var map = new TileMap("sparse", 4, 4);
        TileLayer layer = map.AddLayer("ground", new TileSetRef("world.tiles"));
        TileCell value = new(new TileId(1));
        layer.SetCell(-1, -1, value);
        layer.SetCell(-4, -4, new TileCell(new TileId(2)));
        layer.SetCell(-5, -5, new TileCell(new TileId(2)));
        layer.SetCell(int.MinValue, int.MinValue, value);
        Check(layer.GetCell(-1, -1) == value && layer.GetCell(int.MinValue, int.MinValue) == value &&
              layer.AllocatedChunkCount == 3,
            "Floor division maps negative cells into stable Chunks");
        Check(layer.TryGetChunk(new TileChunkCoordinate(-1, -1), out TileChunk chunk) &&
              chunk.Get(3, 3) == value,
            "Negative cell -1 resolves to local cell 3");
        long revision = layer.Revision;
        layer.SetCell(-1, -1, value);
        Check(layer.Revision == revision, "Writing an unchanged cell does not advance revision");
        layer.ClearCell(-1, -1);
        layer.ClearCell(-4, -4);
        Check(layer.AllocatedChunkCount == 2,
            "An empty Chunk is pruned immediately");

        map.AddLayer("background", new TileSetRef("world.tiles"), depth: -10);
        map.AddLayer("foreground", new TileSetRef("world.tiles"), depth: 10);
        map.AddLayer("same-depth-a", new TileSetRef("world.tiles"), depth: 20);
        map.AddLayer("same-depth-b", new TileSetRef("world.tiles"), depth: 20);
        Check(map.Layers[0].Name == "background" && map.Layers[2].Name == "foreground",
            "Layers draw in deterministic depth order");
        Check(map.Layers[^2].Name == "same-depth-a" && map.Layers[^1].Name == "same-depth-b",
            "Equal-depth layers retain declaration order");
        CheckThrows<ArgumentOutOfRangeException>(
            () => layer.Offset = new Vector2(float.NaN, 0),
            "Runtime layer offsets must remain finite");
    }

    private static void VerifyVisibleRendering()
    {
        Console.WriteLine("3. Camera-visible, allocation-free command generation");
        var sets = new TileSetLibrary();
        sets.Register(CreateTileSet());
        var map = new TileMap("render", 4, 4);
        TileLayer layer = map.AddLayer("ground", new TileSetRef("world.tiles"));
        layer.SetCell(0, 0, new TileCell(new TileId(1)));
        layer.SetCell(1, 0, new TileCell(new TileId(2), TileTransform.FlipX));
        layer.SetCell(2, 0, new TileCell(new TileId(1), TileTransform.Rotate90));
        layer.SetCell(20, 20, new TileCell(new TileId(1)));
        layer.SetCell(0, 1, new TileCell(new TileId(99)));
        var batch = new RecordingBatch();
        var renderer = new TileMapRenderer(sets);

        TileMapDrawStatistics stats = renderer.Draw(
            batch,
            map,
            new Bounds2D(100, 50, 148, 82),
            new Vector2(100, 50));
        Check(stats.DrawnTiles == 3 && stats.UnknownTiles == 1 && batch.Count == 3,
            "Only visible, registered non-empty Tiles draw");
        Check(batch.Commands[0].Position == new Vector2(108, 58) &&
              batch.Commands[0].OriginOverride == new Vector2(8, 8),
            "Grid coordinates use cell top-left semantics independent of Sprite origin");
        Check(batch.Commands[1].Scale == new Vector2(-1, 1) &&
              Near(batch.Commands[2].RotationRadians, MathF.PI * 0.5f),
            "Flip and quarter-turn transforms become Sprite geometry commands");
        Check(stats.VisitedChunks == 1,
            "Far populated Chunks are rejected before cell traversal");

        batch.Clear();
        stats = renderer.Draw(batch, map, new Bounds2D(320, 320, 336, 336));
        Check(stats.DrawnTiles == 1 && batch.Commands[0].Position == new Vector2(328, 328),
            "A second Camera view can draw a distinct visible region of the same map");
    }

    private static void VerifyCollisionBaking()
    {
        Console.WriteLine("4. Chunk-local static collision baking");
        var sets = new TileSetLibrary();
        sets.Register(CreateTileSet());
        var map = new TileMap("collision", 4, 4);
        TileLayer layer = map.AddLayer(
            "walls", new TileSetRef("world.tiles"), offset: new Vector2(4, 8));
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 3; x++)
                layer.SetCell(x, y, new TileCell(new TileId(2)));
        layer.SetCell(3, 0, new TileCell(new TileId(1)));

        var buffer = new TileCollisionBakeBuffer();
        var baker = new TileCollisionBaker(sets);
        int count = baker.BakeLayer(map, "walls", buffer, new Vector2(100, 50));
        Check(count == 1 && buffer[0].Bounds == new Bounds2D(104, 58, 152, 90),
            "A solid 3x2 region is greedily merged into one world-space AABB");

        layer.SetCell(4, 0, new TileCell(new TileId(2)));
        count = baker.BakeLayer(map, "walls", buffer);
        Check(count == 2,
            "Collision rectangles stay Chunk-local for bounded incremental rebuilds");
    }

    private static void VerifyManifest()
    {
        Console.WriteLine("5. Strict declarative Tilemap manifest");
        const string json = """
        {
          "schemaVersion": 1,
          "name": "levels.demo",
          "chunkSize": { "width": 2, "height": 2 },
          "layers": [
            {
              "name": "ground",
              "tileSet": "world.tiles",
              "depth": -1,
              "offset": { "x": 4, "y": 8 },
              "chunks": [
                { "x": -1, "y": 0, "tiles": [1, 65538, 0, 0] }
              ]
            }
          ]
        }
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        TileMap map = TileMapManifestParser.Parse(stream);
        TileLayer layer = map.GetLayer("ground");
        Check(map.ChunkWidth == 2 && layer.GetCell(-2, 0).Tile == new TileId(1) &&
              layer.GetCell(-1, 0) == new TileCell(new TileId(2), TileTransform.FlipX),
            "Dense Chunk data loads row-major with packed transform flags");

        CheckInvalidManifest(json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2"),
            "Unknown schema version is rejected");
        CheckInvalidManifest(json.Replace("\"depth\": -1,", "\"mystery\": 3,"),
            "Unknown JSON fields are rejected");
        CheckInvalidManifest(json.Replace("[1, 65538, 0, 0]", "[1, 2]"),
            "Malformed dense Chunk length is rejected");
    }

    private static void VerifySteadyStateAllocation()
    {
        Console.WriteLine("6. Steady-state draw and collision bake allocation");
        var sets = new TileSetLibrary();
        sets.Register(CreateTileSet());
        var map = new TileMap("allocation", 8, 8);
        TileLayer layer = map.AddLayer("ground", new TileSetRef("world.tiles"));
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                layer.SetCell(x, y, new TileCell(new TileId((ushort)(x % 2 + 1))));
        var batch = new CountingBatch();
        var renderer = new TileMapRenderer(sets);
        var baker = new TileCollisionBaker(sets);
        var buffer = new TileCollisionBakeBuffer(64);
        var bounds = new Bounds2D(0, 0, 128, 128);
        renderer.Draw(batch, map, bounds);
        baker.BakeLayer(map, "ground", buffer);
        // Let tiered JIT promote the hot paths before measuring application allocations.
        for (int i = 0; i < 4_096; i++)
        {
            renderer.Draw(batch, map, bounds);
            baker.BakeLayer(map, "ground", buffer);
        }

        long beforeDraw = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 64; i++)
            renderer.Draw(batch, map, bounds);
        long drawAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeDraw;
        long beforeBake = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 64; i++)
            baker.BakeLayer(map, "ground", buffer);
        long bakeAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeBake;
        const long tieredProbeBudget = 64 * 24;
        Check(drawAllocated <= tieredProbeBudget && bakeAllocated <= tieredProbeBudget,
            "Hot paths contain no payload-proportional allocation " +
            $"(Tiered JIT probes: draw {drawAllocated} B, bake {bakeAllocated} B)");
    }

    private static TileSet CreateTileSet() => new(
        "world.tiles",
        new Vector2(16, 16),
        [
            new TileDefinition(new TileId(1), new SpriteRef("world.ground"), 0),
            new TileDefinition(new TileId(2), new SpriteRef("world.ground"), 3, TileCollisionKind.Solid)
        ]);

    private static void CheckInvalidManifest(string json, string name)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        CheckThrows<InvalidDataException>(() => TileMapManifestParser.Parse(stream), name);
    }

    private static bool Near(float a, float b) => MathF.Abs(a - b) < 0.0001f;

    private static void Check(bool condition, string name)
    {
        Console.WriteLine($"  [{(condition ? "PASS" : "FAIL")}] {name}");
        if (!condition) _failures++;
    }

    private static void CheckThrows<T>(Action action, string name) where T : Exception
    {
        try { action(); Check(false, name); }
        catch (T) { Check(true, name); }
        catch (Exception exception)
        {
            Console.WriteLine($"  [FAIL] {name}: expected {typeof(T).Name}, got {exception.GetType().Name}");
            _failures++;
        }
    }

    private sealed class RecordingBatch : ISpriteBatch
    {
        public List<SpriteDrawCommand> Commands { get; } = [];
        public int Count => Commands.Count;
        public void Clear() => Commands.Clear();
        public void DrawSpriteCommand(in SpriteDrawCommand command) => Commands.Add(command);
        public void Begin() { }
        public void End() { }
        public void Draw(uint textureHandle, Vector2 position, Vector2 size, Vector4 color, Vector4 uvBounds) { }
        public bool TryGetSpriteMetadata(SpriteRef sprite, out SpriteMetadata metadata) { metadata = default; return false; }
        public void Flush() { }
        public void SetBlendMode(BlendMode mode) { }
        public void SetDepthState(bool depthTest, bool depthWrite) { }
        public void SetShader(ShaderRef? shader) { }
        public void SetMaterial(MaterialRef? material) { }
    }

    private sealed class CountingBatch : ISpriteBatch
    {
        public int Count { get; private set; }
        public void DrawSpriteCommand(in SpriteDrawCommand command) => Count++;
        public void Begin() { }
        public void End() { }
        public void Draw(uint textureHandle, Vector2 position, Vector2 size, Vector4 color, Vector4 uvBounds) { }
        public bool TryGetSpriteMetadata(SpriteRef sprite, out SpriteMetadata metadata) { metadata = default; return false; }
        public void Flush() { }
        public void SetBlendMode(BlendMode mode) { }
        public void SetDepthState(bool depthTest, bool depthWrite) { }
        public void SetShader(ShaderRef? shader) { }
        public void SetMaterial(MaterialRef? material) { }
    }
}

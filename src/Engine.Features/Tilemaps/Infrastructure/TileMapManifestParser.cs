namespace GameEngine.Features.Tilemaps.Infrastructure;

using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameEngine.Features.Tilemaps.Domain;

/// <summary>Strict parser for human-authored, chunked Tilemap JSON.</summary>
public static class TileMapManifestParser
{
    public const int CurrentSchemaVersion = 1;

    public static TileMap Parse(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (!json.CanRead) throw new ArgumentException("Tilemap stream must be readable.", nameof(json));
        DocumentDto document;
        try
        {
            document = JsonSerializer.Deserialize(json, TileMapJsonContext.Default.DocumentDto)
                ?? throw new InvalidDataException("Tilemap document is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Tilemap document is invalid JSON.", exception);
        }

        if (document.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException(
                $"Unsupported Tilemap schemaVersion '{document.SchemaVersion}'. Expected {CurrentSchemaVersion}.");
        if (string.IsNullOrWhiteSpace(document.Name))
            throw new InvalidDataException("Tilemap name cannot be empty.");
        int chunkWidth = document.ChunkSize?.Width ?? 32;
        int chunkHeight = document.ChunkSize?.Height ?? 32;
        TileMap map;
        try { map = new TileMap(document.Name, chunkWidth, chunkHeight); }
        catch (ArgumentException exception) { throw new InvalidDataException("Tilemap chunkSize is invalid.", exception); }

        if (document.Layers is not { Count: > 0 })
            throw new InvalidDataException("Tilemap requires at least one layer.");
        for (int layerIndex = 0; layerIndex < document.Layers.Count; layerIndex++)
        {
            LayerDto source = document.Layers[layerIndex]
                ?? throw new InvalidDataException($"Tilemap layer {layerIndex} is null.");
            if (string.IsNullOrWhiteSpace(source.Name))
                throw new InvalidDataException($"Tilemap layer {layerIndex} has no name.");
            if (string.IsNullOrWhiteSpace(source.TileSet))
                throw new InvalidDataException($"Tilemap layer '{source.Name}' has no tileSet.");
            Vector2 offset = source.Offset is null
                ? Vector2.Zero
                : new Vector2(source.Offset.X, source.Offset.Y);
            TileLayer layer;
            try
            {
                layer = map.AddLayer(
                    source.Name,
                    new TileSetRef(source.TileSet),
                    source.Depth ?? 0,
                    offset,
                    source.Visible ?? true);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"Tilemap layer '{source.Name}' is invalid.", exception);
            }

            List<ChunkDto?> chunks = source.Chunks ?? [];
            var coordinates = new HashSet<TileChunkCoordinate>();
            for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                ChunkDto chunk = chunks[chunkIndex]
                    ?? throw new InvalidDataException(
                        $"Chunk {chunkIndex} of layer '{source.Name}' is null.");
                var coordinate = new TileChunkCoordinate(chunk.X, chunk.Y);
                if (!coordinates.Add(coordinate))
                    throw new InvalidDataException(
                        $"Layer '{source.Name}' repeats Chunk ({chunk.X},{chunk.Y}).");
                int expected = checked(chunkWidth * chunkHeight);
                if (chunk.Tiles is null || chunk.Tiles.Count != expected)
                    throw new InvalidDataException(
                        $"Chunk ({chunk.X},{chunk.Y}) of layer '{source.Name}' requires exactly {expected} cells.");
                for (int index = 0; index < expected; index++)
                {
                    uint encoded = chunk.Tiles[index];
                    ushort id = (ushort)(encoded & 0xffffu);
                    uint flags = encoded >> 16;
                    if ((flags & ~0x0fu) != 0)
                        throw new InvalidDataException(
                            $"Cell {index} of Chunk ({chunk.X},{chunk.Y}) contains unknown transform flags.");
                    if (id == 0 && flags != 0)
                        throw new InvalidDataException("Empty Tile cells cannot carry transform flags.");
                    if (id == 0) continue;
                    int localX = index % chunkWidth;
                    int localY = index / chunkWidth;
                    try
                    {
                        layer.SetCell(
                            checked(chunk.X * chunkWidth + localX),
                            checked(chunk.Y * chunkHeight + localY),
                            new TileCell(new TileId(id), (TileTransform)flags));
                    }
                    catch (OverflowException exception)
                    {
                        throw new InvalidDataException(
                            $"Chunk ({chunk.X},{chunk.Y}) exceeds the supported Tile coordinate range.",
                            exception);
                    }
                }
            }
        }
        return map;
    }

    public sealed class DocumentDto
    {
        public int SchemaVersion { get; set; }
        public string? Name { get; set; }
        public SizeDto? ChunkSize { get; set; }
        public List<LayerDto?>? Layers { get; set; }
    }

    public sealed class SizeDto { public int Width { get; set; } public int Height { get; set; } }
    public sealed class OffsetDto { public float X { get; set; } public float Y { get; set; } }
    public sealed class LayerDto
    {
        public string? Name { get; set; }
        public string? TileSet { get; set; }
        public int? Depth { get; set; }
        public bool? Visible { get; set; }
        public OffsetDto? Offset { get; set; }
        public List<ChunkDto?>? Chunks { get; set; }
    }
    public sealed class ChunkDto
    {
        public int X { get; set; }
        public int Y { get; set; }
        public List<uint>? Tiles { get; set; }
    }
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    PropertyNameCaseInsensitive = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(TileMapManifestParser.DocumentDto))]
internal sealed partial class TileMapJsonContext : JsonSerializerContext;

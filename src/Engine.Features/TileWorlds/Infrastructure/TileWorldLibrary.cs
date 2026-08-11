namespace GameEngine.Features.TileWorlds.Infrastructure;

using GameEngine.Features.TileWorlds.Domain;

public sealed record TileWorldDescriptor(
    TileWorldRef Ref,
    string ArchivePath,
    TileWorldMetadata Metadata);

/// <summary>
/// Logical catalog for compiled TileWorld archives. Archive files are borrowed package assets;
/// each opened reader owns only its own file stream.
/// </summary>
public sealed class TileWorldLibrary
{
    private readonly Dictionary<TileWorldRef, TileWorldDescriptor> _entries = [];

    public int Count => _entries.Count;

    public TileWorldRef Register(string name, string archivePath)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("TileWorld name cannot be empty.", nameof(name));
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        string path = Path.GetFullPath(archivePath);
        using var reader = new TileWorldArchiveReader(File.OpenRead(path));
        if (!StringComparer.Ordinal.Equals(name, reader.Metadata.Name))
            throw new InvalidDataException(
                $"TileWorld declaration '{name}' does not match archive name '{reader.Metadata.Name}'.");
        var reference = new TileWorldRef(name);
        if (!_entries.TryAdd(reference, new TileWorldDescriptor(reference, path, reader.Metadata)))
            throw new ArgumentException($"TileWorld '{name}' is already registered.", nameof(name));
        return reference;
    }

    public bool TryGet(TileWorldRef reference, out TileWorldDescriptor descriptor) =>
        _entries.TryGetValue(reference, out descriptor!);

    public TileWorldDescriptor Get(TileWorldRef reference) => TryGet(reference, out var descriptor)
        ? descriptor
        : throw new KeyNotFoundException($"TileWorld '{reference}' is not registered.");

    public TileWorldArchiveReader Open(TileWorldRef reference) =>
        new(File.OpenRead(Get(reference).ArchivePath));

    public bool Remove(TileWorldRef reference) => _entries.Remove(reference);
}

namespace GameEngine.Features.ContentAssets.Domain;

/// <summary>
/// A stable logical reference to a content package. It identifies the package expected at a
/// manifest path without owning any loaded textures or sprites.
/// </summary>
public readonly record struct ContentPackageRef
{
    public ContentPackageRef(string id, string manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest);
        Id = id;
        Manifest = manifest;
    }

    public string Id { get; }
    public string Manifest { get; }

    public override string ToString() => Id ?? string.Empty;
}

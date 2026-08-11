namespace GameEngine.Features.ContentAssets.Infrastructure;

using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.Animation;
using GameEngine.Features.Audio;
using GameEngine.Features.Tilemaps.Domain;
using GameEngine.Features.TileWorlds.Domain;

/// <summary>An external lease over a loaded package and its transitive dependencies.</summary>
public sealed class LoadedContentPackage : IDisposable
{
    private ContentPackageManager? _manager;

    internal LoadedContentPackage(ContentPackageManager manager, string id)
    {
        _manager = manager;
        Id = id;
    }

    public string Id { get; }

    public TextureRef GetTexture(string name) =>
        (_manager ?? throw new ObjectDisposedException(nameof(LoadedContentPackage)))
            .GetTexture(Id, name);

    public SpriteRef GetSprite(string name) =>
        (_manager ?? throw new ObjectDisposedException(nameof(LoadedContentPackage)))
            .GetSprite(Id, name);

    public AnimationClipRef GetAnimation(string name) =>
        (_manager ?? throw new ObjectDisposedException(nameof(LoadedContentPackage)))
            .GetAnimation(Id, name);

    public AudioClipRef GetAudioClip(string name) =>
        (_manager ?? throw new ObjectDisposedException(nameof(LoadedContentPackage)))
            .GetAudioClip(Id, name);

    public TileSetRef GetTileSet(string name) =>
        (_manager ?? throw new ObjectDisposedException(nameof(LoadedContentPackage)))
            .GetTileSet(Id, name);

    public TileMapRef GetTileMap(string name) =>
        (_manager ?? throw new ObjectDisposedException(nameof(LoadedContentPackage)))
            .GetTileMap(Id, name);

    public TileWorldRef GetTileWorld(string name) =>
        (_manager ?? throw new ObjectDisposedException(nameof(LoadedContentPackage)))
            .GetTileWorld(Id, name);

    public void Dispose()
    {
        var manager = Interlocked.Exchange(ref _manager, null);
        manager?.Release(Id);
    }
}

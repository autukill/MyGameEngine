namespace GameEngine.Features.ContentAssets.Infrastructure;

using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.Animation;

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

    public void Dispose()
    {
        var manager = Interlocked.Exchange(ref _manager, null);
        manager?.Release(Id);
    }
}

namespace GameEngine.Core.Domain.Graphics;

using GameEngine.Core.Domain.ValueObjects;

/// <summary>Resolves logical texture references without exposing resource ownership.</summary>
public interface ITextureResolver
{
    bool TryGetMetadata(TextureRef texture, out TextureMetadata metadata);

    bool TryResolve(TextureRef texture, out ResolvedTexture resolved);
}

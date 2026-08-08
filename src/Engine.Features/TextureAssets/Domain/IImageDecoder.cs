namespace GameEngine.Features.TextureAssets.Domain;

/// <summary>Decodes an encoded image stream without taking ownership of the stream.</summary>
public interface IImageDecoder
{
    DecodedImage Decode(Stream stream);
}

namespace GameEngine.Features.TextureAssets.Infrastructure;

using GameEngine.Features.TextureAssets.Domain;
using Silk.NET.OpenGL;

/// <summary>Uploads RGBA8 pixels to the current OpenGL context.</summary>
public sealed class OpenGlTextureBackend(GL gl) : ITextureBackend
{
    private readonly GL _gl = gl ?? throw new ArgumentNullException(nameof(gl));

    public unsafe uint CreateTexture(
        int width,
        int height,
        ReadOnlySpan<byte> rgbaPixels,
        TextureSampler sampler)
    {
        uint handle = _gl.GenTexture();
        if (handle == 0)
            throw new InvalidOperationException("OpenGL did not create a texture handle.");

        try
        {
            _gl.BindTexture(TextureTarget.Texture2D, handle);
            fixed (byte* pixels = rgbaPixels)
            {
                _gl.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    (int)InternalFormat.Rgba8,
                    (uint)width,
                    (uint)height,
                    0,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    pixels);
            }

            uint minFilter = Map(sampler.MinFilter);
            uint magFilter = Map(sampler.MagFilter);
            uint wrapU = Map(sampler.WrapU);
            uint wrapV = Map(sampler.WrapV);
            _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, in minFilter);
            _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, in magFilter);
            _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, in wrapU);
            _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, in wrapV);
            return handle;
        }
        catch
        {
            _gl.DeleteTexture(handle);
            throw;
        }
    }

    public void DeleteTexture(uint handle)
    {
        if (handle != 0)
            _gl.DeleteTexture(handle);
    }

    private static uint Map(TextureFilter filter) => filter switch
    {
        TextureFilter.Nearest => (uint)GLEnum.Nearest,
        TextureFilter.Linear => (uint)GLEnum.Linear,
        _ => throw new ArgumentOutOfRangeException(nameof(filter))
    };

    private static uint Map(TextureWrap wrap) => wrap switch
    {
        TextureWrap.ClampToEdge => (uint)GLEnum.ClampToEdge,
        TextureWrap.Repeat => (uint)GLEnum.Repeat,
        _ => throw new ArgumentOutOfRangeException(nameof(wrap))
    };
}

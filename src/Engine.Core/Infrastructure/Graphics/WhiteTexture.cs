namespace GameEngine.Core.Infrastructure.Graphics;

using Silk.NET.OpenGL;
using System.Numerics;

/// <summary>
/// 工具类：在 GPU 上生成一张 1x1 像素白色纹理（用于无纹理彩色矩形渲染）。
/// </summary>
public sealed class WhiteTexture : IDisposable
{
    private readonly GL _gl;
    private bool _disposed;
    public uint Handle { get; }

    public WhiteTexture(GL gl)
    {
        _gl = gl;
        Handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, Handle);

        // 1x1 白色 RGBA 像素
        byte[] pixel = { 255, 255, 255, 255 };
        unsafe
        {
            fixed (byte* p = pixel)
            {
                gl.TexImage2D(TextureTarget.Texture2D, 0,
                    (int)InternalFormat.Rgba8, 1, 1, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, p);
            }
        }
        uint linear = (uint)GLEnum.Linear;
        uint clampToEdge = (uint)GLEnum.ClampToEdge;
        gl.TexParameterI(TextureTarget.Texture2D,
            TextureParameterName.TextureMinFilter, in linear);
        gl.TexParameterI(TextureTarget.Texture2D,
            TextureParameterName.TextureMagFilter, in linear);
        gl.TexParameterI(TextureTarget.Texture2D,
            TextureParameterName.TextureWrapS, in clampToEdge);
        gl.TexParameterI(TextureTarget.Texture2D,
            TextureParameterName.TextureWrapT, in clampToEdge);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gl.DeleteTexture(Handle);
    }
}

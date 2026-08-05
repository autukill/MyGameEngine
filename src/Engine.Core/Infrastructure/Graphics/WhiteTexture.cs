namespace GameEngine.Core.Infrastructure.Graphics;

using Silk.NET.OpenGL;
using System.Numerics;

/// <summary>
/// 工具类：在 GPU 上生成一张 1x1 像素白色纹理（用于无纹理彩色矩形渲染）。
/// </summary>
public sealed class WhiteTexture : IDisposable
{
    public uint Handle { get; }

    public WhiteTexture(GL gl)
    {
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
        gl.TexParameterI(TextureTarget.Texture2D,
            TextureParameterName.TextureMinFilter, (uint)GLEnum.Linear);
        gl.TexParameterI(TextureTarget.Texture2D,
            TextureParameterName.TextureMagFilter, (uint)GLEnum.Linear);
        gl.TexParameterI(TextureTarget.Texture2D,
            TextureParameterName.TextureWrapS, (uint)GLEnum.ClampToEdge);
        gl.TexParameterI(TextureTarget.Texture2D,
            TextureParameterName.TextureWrapT, (uint)GLEnum.ClampToEdge);
    }

    public void Dispose()
    {
        // GL handle cleanup 在引擎退出时由 GL.Dispose 统一处理
    }
}

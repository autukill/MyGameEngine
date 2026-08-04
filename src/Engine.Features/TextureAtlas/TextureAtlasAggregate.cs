namespace GameEngine.Features.TextureAtlas;

using Silk.NET.OpenGL;
using System.Numerics;

public class TextureAtlasAggregate : IDisposable {
    private readonly GL _gl;
    public uint TextureHandle { get; }
    public int Width { get; }
    public int Height { get; }

    private readonly Dictionary<string, SpriteRegion> _regions;

    public TextureAtlasAggregate( GL gl, int width, int height, Dictionary<string, SpriteRegion> regions, Span<byte> pixelData ) {
        _gl = gl;
        Width = width;
        Height = height;
        _regions = regions;

        // 向 GPU 申请大图纹理内存
        TextureHandle = _gl.GenTexture();
        _gl.BindTexture( TextureTarget.Texture2D, TextureHandle );

        // 设置纹理过滤（2D 像素风选用 Nearest，现代插画风选用 Linear）
        _gl.TextureParameter( TextureHandle, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest );
        _gl.TextureParameter( TextureHandle, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest );
        _gl.TextureParameter( TextureHandle, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge );
        _gl.TextureParameter( TextureHandle, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge );

        unsafe {
            fixed (byte* ptr = pixelData) {
                _gl.TexImage2D(
                    TextureTarget.Texture2D, 0,
                    (int)InternalFormat.Rgba,
                    (uint)width, (uint)height, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte,
                    ptr
                );
            }
        }

        _gl.BindTexture( TextureTarget.Texture2D, 0 );
    }

    /// <summary>
    /// 根据名称高效获取子图 UV
    /// </summary>
    public bool TryGetRegion( string name, out SpriteRegion region ) {
        return _regions.TryGetValue( name, out region );
    }

    public SpriteRegion GetRegion( string name ) {
        if ( _regions.TryGetValue( name, out var region ) ) return region;

        throw new KeyNotFoundException( $"Sprite '{name}' not found in Texture Atlas." );
    }

    public void Dispose() {
        _gl.DeleteTexture( TextureHandle );
    }
}
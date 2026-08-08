namespace GameEngine.Features.RenderPipeline.Infrastructure;

using Silk.NET.OpenGL;
using System.Numerics;

/// <summary>
/// FBO 封装：Color Texture2D + 可选 D24S8 Depth/Stencil RenderBuffer。
/// </summary>
public sealed class RenderTarget2D : IDisposable
{
    private readonly GL _gl;

    public uint FboHandle { get; }
    public uint ColorTexture { get; }
    public uint DepthStencilRbo { get; private set; }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool HasDepthStencil { get; }
    public Vector2 Size => new(Width, Height);

    public RenderTarget2D(GL gl, int width, int height, bool withDepthStencil = true)
    {
        _gl = gl;
        Width = width;
        Height = height;
        HasDepthStencil = withDepthStencil;

        // 1. 生成并绑定 Framebuffer
        FboHandle = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, FboHandle);

        // 2. Color Attachment (Texture2D)
        ColorTexture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, ColorTexture);
        unsafe
        {
            gl.TexImage2D(TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba8,
                (uint)width, (uint)height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, null);
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

        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, ColorTexture, 0);

        // 3. Depth/Stencil RenderBuffer (可选)
        if (withDepthStencil)
        {
            DepthStencilRbo = gl.GenRenderbuffer();
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, DepthStencilRbo);
            gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer,
                InternalFormat.Depth24Stencil8, (uint)width, (uint)height);
            gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                FramebufferAttachment.DepthStencilAttachment,
                RenderbufferTarget.Renderbuffer, DepthStencilRbo);
        }

        // 4. 完整性校验
        var status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            throw new InvalidOperationException($"[RenderTarget2D] FBO incomplete: {status}");

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void SetAsTarget()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, FboHandle);
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
    }

    public void Unbind(int screenWidth, int screenHeight)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)screenWidth, (uint)screenHeight);
    }

    public void Resize(int newWidth, int newHeight)
    {
        if (Width == newWidth && Height == newHeight) return;
        Width = newWidth;
        Height = newHeight;

        _gl.BindTexture(TextureTarget.Texture2D, ColorTexture);
        unsafe
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba8,
                (uint)Width, (uint)Height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, null);
        }

        if (HasDepthStencil)
        {
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, DepthStencilRbo);
            _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer,
                InternalFormat.Depth24Stencil8, (uint)Width, (uint)Height);
        }
    }

    public void Dispose()
    {
        _gl.DeleteTexture(ColorTexture);
        if (HasDepthStencil) _gl.DeleteRenderbuffer(DepthStencilRbo);
        _gl.DeleteFramebuffer(FboHandle);
    }
}

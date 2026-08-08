namespace GameEngine.Features.RenderPipeline.Infrastructure;

using GameEngine.Features.RenderPipeline.Domain;
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
    public RenderTargetColorFormat ColorFormat { get; }
    public Vector2 Size => new(Width, Height);

    public RenderTarget2D(GL gl, int width, int height, bool withDepthStencil = true)
        : this(gl, new RenderTargetDescriptor(
            width,
            height,
            RenderTargetColorFormat.Rgba8,
            withDepthStencil
                ? RenderTargetDepthStencilFormat.Depth24Stencil8
                : RenderTargetDepthStencilFormat.None))
    {
    }

    public RenderTarget2D(GL gl, in RenderTargetDescriptor descriptor)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        Width = descriptor.Width;
        Height = descriptor.Height;
        HasDepthStencil = descriptor.HasDepthStencil;
        ColorFormat = descriptor.ColorFormat;

        // 1. 生成并绑定 Framebuffer
        FboHandle = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, FboHandle);

        // 2. Color Attachment (Texture2D)
        ColorTexture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, ColorTexture);
        unsafe
        {
            gl.TexImage2D(TextureTarget.Texture2D, 0, (int)GetInternalFormat(ColorFormat),
                (uint)Width, (uint)Height, 0,
                PixelFormat.Rgba, GetPixelType(ColorFormat), null);
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
        if (HasDepthStencil)
        {
            DepthStencilRbo = gl.GenRenderbuffer();
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, DepthStencilRbo);
            gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer,
                InternalFormat.Depth24Stencil8, (uint)Width, (uint)Height);
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
            _gl.TexImage2D(TextureTarget.Texture2D, 0, (int)GetInternalFormat(ColorFormat),
                (uint)Width, (uint)Height, 0,
                PixelFormat.Rgba, GetPixelType(ColorFormat), null);
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

    private static InternalFormat GetInternalFormat(RenderTargetColorFormat format) => format switch
    {
        RenderTargetColorFormat.Rgba8 => InternalFormat.Rgba8,
        RenderTargetColorFormat.Rgba16Float => InternalFormat.Rgba16f,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    private static PixelType GetPixelType(RenderTargetColorFormat format) => format switch
    {
        RenderTargetColorFormat.Rgba8 => PixelType.UnsignedByte,
        RenderTargetColorFormat.Rgba16Float => PixelType.HalfFloat,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
}

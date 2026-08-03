namespace GameEngine.Core.Infrastructure.Windowing;

using Silk.NET.Maths;
using Silk.NET.Windowing;

public record EngineWindowOptions(
    string Title = "Custom C# 2D Engine",
    Vector2D<int> Size = default,
    bool VSync = true,
    int StencilBits = 8,  // 显式申请 8-bit Stencil 缓冲 (0-255 值域)
    int DepthBits = 24    // 24-bit 深度缓冲
)
{
    public static EngineWindowOptions Default => new(
        Size: new Vector2D<int>(1280, 720)
    );

    public WindowOptions ToSilkWindowOptions()
    {
        var opts = WindowOptions.Default;
        opts.Title = Title;
        opts.Size = Size;
        opts.VSync = VSync;

        // 配置 OpenGL 3.3 Core Profile
        opts.API = new GraphicsAPI(
            ContextAPI.OpenGL,
            ContextProfile.Core,
            ContextFlags.ForwardCompatible,
            new APIVersion(3, 3)
        );

        // 显式向驱动申请 Depth 与 Stencil Buffer 位深
        opts.PreferredDepthBufferBits = DepthBits;
        opts.PreferredStencilBufferBits = StencilBits;

        return opts;
    }
}

namespace GameEngine.Core.Infrastructure.Graphics;

using Silk.NET.OpenGL;
using Silk.NET.Windowing;

public sealed class GraphicsDevice : IDisposable
{
    public GL Gl { get; }
    public int ViewportWidth { get; private set; }
    public int ViewportHeight { get; private set; }

    public GraphicsDevice(IWindow window)
    {
        // 绑定 OpenGL 函数指针
        Gl = GL.GetApi(window);

        ViewportWidth = window.Size.X;
        ViewportHeight = window.Size.Y;

        InitializeDefaultStates();
    }

    private void InitializeDefaultStates()
    {
        // 设置默认清屏颜色 (暗灰色背景)
        Gl.ClearColor(0.1f, 0.12f, 0.15f, 1.0f);

        // 设置默认 Stencil Buffer 清空基准值
        Gl.ClearStencil(0);

        // 开启 2D 精灵渲染必须的 Alpha 混合
        Gl.Enable(EnableCap.Blend);
        Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }

    public void OnResize(int width, int height)
    {
        ViewportWidth = width;
        ViewportHeight = height;
        Gl.Viewport(0, 0, (uint)width, (uint)height);
    }

    /// <summary>
    /// 每帧开始前，同时清空 Color Buffer 和 Stencil Buffer
    /// </summary>
    public void ClearBuffers()
    {
        Gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit));
    }

    public void Dispose()
    {
        Gl.Dispose();
    }
}

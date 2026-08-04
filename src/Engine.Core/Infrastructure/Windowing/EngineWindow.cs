namespace GameEngine.Core.Infrastructure.Windowing;

using Silk.NET.Windowing;
using GameEngine.Core.Infrastructure.Graphics;

public class EngineWindow {
    private readonly IWindow _nativeWindow;
    public GraphicsDevice Graphics { get; private set; } = null!;

    // 暴露类似 GameMaker 生命周期管道的回调委托
    public event Action? OnLoad;

    // 完全保留你原版契合 GameMaker 的精妙生命周期委托
    public event Action<double>? OnPreStep;
    public event Action<double>? OnStep; // 对应 GMS 的 Step Event
    public event Action<double>? OnPostStep;

    public event Action? OnDrawBegin;
    public event Action? OnDraw; // 对应 GMS 的 Draw Event
    public event Action? OnDrawGUI; // 对应 GMS 的 Draw GUI Event

    public EngineWindow( EngineWindowOptions options ) {
        _nativeWindow = Window.Create( options.ToSilkWindowOptions() );

        _nativeWindow.Load += HandleLoad;
        _nativeWindow.Update += HandleUpdate;
        _nativeWindow.Render += HandleRender;
        _nativeWindow.Resize += HandleResize;
        _nativeWindow.Closing += HandleClosing;
    }

    public void Run() {
        _nativeWindow.Run();
    }

    private void HandleLoad() {
        // 1. 初始化 Graphics Device
        Graphics = new GraphicsDevice( _nativeWindow );
        Console.WriteLine( $"[Engine EngineWindow] OpenGL Initialized. Version: {Graphics.Gl.GetStringS( Silk.NET.OpenGL.GLEnum.Version )}" );

        // 2. 通知外部：GPU 上下文与设备已准备就绪，可以安全初始化 Shader、Scene 和 PhysicsGrid
        OnLoad?.Invoke();
    }

    private void HandleUpdate( double deltaTime ) {
        // Step 阶段：只跑游戏逻辑、物理移动、碰撞检测，绝对不包含任何 Render 操作
        OnPreStep?.Invoke( deltaTime );
        OnStep?.Invoke( deltaTime );
        OnPostStep?.Invoke( deltaTime );
    }

    private void HandleRender( double deltaTime ) {
        // 1. 重置 Framebuffer (清空 Color 和 Stencil Buffer) -> 保持你的封装，不裸露给外部
        Graphics.ClearBuffers();

        // 2. 游戏场景世界渲染 (受 Camera & Layer 影响)
        OnDrawBegin?.Invoke();
        OnDraw?.Invoke();

        // 3. UI 界面渲染 (不受 Camera 影响的屏幕坐标系)
        OnDrawGUI?.Invoke();
    }

    private void HandleResize( Silk.NET.Maths.Vector2D<int> size ) {
        Graphics?.OnResize( size.X, size.Y );
    }

    private void HandleClosing() {
        Graphics?.Dispose();
    }
}
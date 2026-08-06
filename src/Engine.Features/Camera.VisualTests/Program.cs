namespace Camera.VisualTests;

using System.Numerics;
using Silk.NET.Input;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Features.Camera.Domain;

/// <summary>
/// Camera 切片 · 可运行看效果 Demo。
///
/// 展示内容：
///   - 世界网格 + 彩色方块（世界固定坐标系）
///   - WASD    平移相机
///   - Q/E     缩放 (Zoom 0.5x~3x)
///   - R       震屏
///   - ESC     退出
///
/// 关键点：世界坐标固定，改变相机属性即可观察视图变化——验证
/// Camera2D.GetViewProjectionMatrix() 的正交视图投影映射。
/// </summary>
internal static class Program
{
    private static EngineWindow? _window;
    private static SpriteShader? _shader;
    private static SpriteBatch? _batch;
    private static WhiteTexture? _white;
    private static Camera2D? _camera;
    private static IKeyboard? _keyboard;
    private static IMouse? _mouse;

    private static void Main()
    {
        Console.WriteLine("=== Camera Visual Test ===");
        Console.WriteLine("  WASD 平移 | Q/E 缩放 | R 震屏 | ESC 退出");

        _window = new EngineWindow(EngineWindowOptions.Default);
        _window.OnLoad += HandleLoad;
        _window.OnStep += HandleStep;
        _window.OnDraw += HandleDraw;
        _window.Run();
    }

    private static void HandleLoad()
    {
        var gl = _window!.Graphics.Gl;
        _shader = new SpriteShader(gl);
        _batch = new SpriteBatch(gl);
        _white = new WhiteTexture(gl);
        _camera = new Camera2D(new Vector2(_window.Width, _window.Height));

        try
        {
            // 缓存 IKeyboard/IMouse，避免每帧重复 CreateInput() 导致输入状态丢失
            var input = _window.NativeWindow.CreateInput();
            _keyboard = input.Keyboards.Count > 0 ? input.Keyboards[0] : null;
            _mouse = input.Mice.Count > 0 ? input.Mice[0] : null;

            if (_keyboard is not null)
            {
                _keyboard.KeyDown += (_, key, _) =>
                {
                    switch (key)
                    {
                        case Key.Escape: _window.NativeWindow.Close(); break;
                        case Key.R: _camera!.Shake(30f, 0.3f); break;
                        case Key.Q: _camera!.Zoom = Math.Clamp(_camera.Zoom * 0.9f, 0.2f, 5f); break;
                        case Key.E: _camera!.Zoom = Math.Clamp(_camera.Zoom * 1.1f, 0.2f, 5f); break;
                    }
                };
            }

            if (_mouse is not null)
            {
                _mouse.Scroll += (_, scroll) =>
                    _camera!.Zoom = Math.Clamp(_camera.Zoom * (scroll.Y > 0 ? 1.1f : 0.9f), 0.2f, 5f);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Input] WARN: {ex.Message}");
        }
    }

    private static void HandleStep(double dt)
    {
        // 用缓存的 IKeyboard 轮询平移键（WASD 是持续按住，需每帧轮询 IsKeyPressed）
        var speed = (float)(300.0 * dt);
        if (_keyboard is not null)
        {
            if (_keyboard.IsKeyPressed(Key.W)) _camera!.Position += new Vector2(0, -speed);
            if (_keyboard.IsKeyPressed(Key.S)) _camera!.Position += new Vector2(0, speed);
            if (_keyboard.IsKeyPressed(Key.A)) _camera!.Position += new Vector2(-speed, 0);
            if (_keyboard.IsKeyPressed(Key.D)) _camera!.Position += new Vector2(speed, 0);
        }
        _camera!.Update(dt);
    }

    private static void HandleDraw()
    {
        var gl = _window!.Graphics.Gl;
        _shader!.Use();
        _shader.SetProjection(_camera!.GetViewProjectionMatrix());

        // 网格背景（世界坐标 40px 间隔）
        _batch!.Begin();
        DrawGrid();
        DrawWorldSquares();
        _batch.End();
    }

    private static void DrawGrid()
    {
        float size = 4000f;
        for (float x = -size; x <= size; x += 40f)
        {
            var color = MathF.Abs(x) < 1f ? new Vector4(0.3f, 0.3f, 0.3f, 1f)
                : new Vector4(0.18f, 0.18f, 0.18f, 1f);
            _batch!.Draw(_white!.Handle,
                new Vector2(x, -size), new Vector2(1, size * 2), color);
        }
        for (float y = -size; y <= size; y += 40f)
        {
            var color = MathF.Abs(y) < 1f ? new Vector4(0.3f, 0.3f, 0.3f, 1f)
                : new Vector4(0.18f, 0.18f, 0.18f, 1f);
            _batch!.Draw(_white!.Handle,
                new Vector2(-size, y), new Vector2(size * 2, 1), color);
        }
    }

    private static void DrawWorldSquares()
    {
        // 世界固定方块（原点为中心 4 个彩色方块）
        var colors = new[]
        {
            new Vector4(1f, 0.3f, 0.3f, 1f),
            new Vector4(0.3f, 1f, 0.3f, 1f),
            new Vector4(0.3f, 0.5f, 1f, 1f),
            new Vector4(1f, 1f, 0.3f, 1f),
        };
        var offsets = new[]
        {
            new Vector2(-160, -160),
            new Vector2(40, -160),
            new Vector2(-160, 40),
            new Vector2(40, 40),
        };
        for (int i = 0; i < 4; i++)
        {
            _batch!.Draw(_white!.Handle, offsets[i], new Vector2(120, 120), colors[i]);
        }

        // 原点标记
        _batch!.Draw(_white!.Handle, new Vector2(-6, -6), new Vector2(12, 12), Vector4.One);
    }
}

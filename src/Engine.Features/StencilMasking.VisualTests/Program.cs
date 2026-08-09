namespace StencilMasking.VisualTests;

using System.Numerics;
using Silk.NET.OpenGL;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;
using GameEngine.Features.StencilMasking.Domain;
using GameEngine.Features.StencilMasking.Infrastructure;

/// <summary>
/// StencilMasking 切片 · 可运行看效果 Demo（GameInstance 事件驱动版）。
///
/// 展示内容：圆形聚光灯 Stencil 遮罩（ShowInside 模式）
///   - 整个场景先渲染到 RT_Scene
///   - StencilMaskPass：画圆形遮罩 → 只重绘遮罩内部的内容到 RT_Masked
///   - 合成到屏幕：RT_Scene + RT_Masked 叠加
///
/// 操作（全部由 SpotlightSource 实例处理）：
///   - 移动鼠标：聚光灯圆心跟随
///   - 滚轮：调整遮罩半径
///   - M：切换 ShowInside / ShowOutside（聚光灯 ↔ 透视洞）
///   - ESC：退出
/// </summary>
internal static class Program
{
    private static EngineWindow? _window;
    private static SpriteShader? _spriteShader;
    private static StencilMaskShader? _maskShader;
    private static BlitShader? _blitShader;
    private static SpriteBatch? _batch;
    private static WhiteTexture? _white;
    private static Camera2D? _camera;
    private static SceneAggregate? _scene;
    private static RenderPipeline? _pipeline;
    private static StencilMaskPass? _stencilPass;

    private static void Main()
    {
        Console.WriteLine("=== StencilMasking Visual Test (Spotlight) ===");
        Console.WriteLine("  移动鼠标: 光源 | 滚轮: 半径 | M: 切换内/外 | ESC: 退出");

        _window = new EngineWindow(EngineWindowOptions.Default);
        _window.OnLoad += HandleLoad;
        _window.OnStep += HandleStep;
        _window.OnDraw += HandleDraw;
        _window.Run();
    }

    private static void HandleLoad()
    {
        var gl = _window!.Graphics.Gl;
        var (vw, vh) = (_window.Width, _window.Height);

        _spriteShader = new SpriteShader(gl);
        _maskShader = new StencilMaskShader(gl);
        _blitShader = new BlitShader(gl);
        _batch = new SpriteBatch(gl);
        _batch.DefaultShader = _spriteShader;
        _white = new WhiteTexture(gl);
        _camera = new Camera2D(new Vector2(vw, vh));

        // 场景：填充背景方块 + 若干彩色方块
        _scene = new SceneAggregate("StencilDemo");
        _scene.SetInput(_window.Input);
        _scene.ViewportWidth = vw;
        _scene.ViewportHeight = vh;
        _scene.Background = BackgroundConfig.FromColor(new Vector4(0.1f, 0.1f, 0.14f, 1f));
        for (int r = 0; r < 5; r++)
            for (int c = 0; c < 8; c++)
                _scene.Add(new ColorBox(new Vector2(30 + c * 80, 30 + r * 80),
                    new Vector4(0.3f, 0.4f, 0.8f, 1f), _white.Handle));

        var rtScene = new RenderTarget2D(gl, vw, vh, withDepthStencil: true);
        var rtMasked = new RenderTarget2D(gl, vw, vh, withDepthStencil: true);

        var scenePass = new SceneRenderPass("ScenePass", gl, _scene, _camera, rtScene);
        _stencilPass = new StencilMaskPass("StencilPass", gl, _scene, _camera,
            rtMasked, _spriteShader, _maskShader, _white);
        _stencilPass.State = StencilMaskState.Spotlight;

        var compositor = new ViewportCompositorPass("CompositorPass", gl, _blitShader, _batch);
        compositor.AddSource(rtScene, ViewportRect.FullScreen, BlendState.Opaque);
        compositor.AddSource(rtMasked, ViewportRect.FullScreen, BlendState.AlphaBlend);

        _pipeline = new RenderPipeline(gl, vw, vh);
        _pipeline.AddPass(scenePass);
        _pipeline.AddPass(_stencilPass);
        _pipeline.AddPass(compositor);

        // 聚光灯控制器：输入 + 状态全部进 GameInstance
        _scene.Add(new SpotlightSource(_stencilPass, _window));
    }

    private static void HandleStep(double dt)
    {
        _scene!.PerformInput(_window!.Input.KeysPressed, _window.Input.KeysReleased);
        _scene.PerformStep(dt);
    }

    private static void HandleDraw()
    {
        var ctx = new RenderPassContext(
            _window!.Graphics.Gl, _spriteShader!, _batch!,
            _window.Width, _window.Height);
        _pipeline!.Execute(ctx);
    }

    /// <summary>
    /// 聚光灯控制器：OnStep 读鼠标/滚轮并每帧应用遮罩位置半径；
    /// OnKeyDown 切换 ShowInside/ShowOutside。
    /// 注：直接持 StencilMaskPass 引用是演示捷径；通用化路径为实例发
    /// RenderEffectRequested 事件由 PipelineBuilder 装配（第二阶段）。
    /// </summary>
    private sealed class SpotlightSource : GameInstance
    {
        private readonly StencilMaskPass _stencilPass;
        private readonly EngineWindow _window;
        private float _maskRadius = 120f;
        private bool _showOutside;

        public SpotlightSource(StencilMaskPass stencilPass, EngineWindow window)
            : base(nameof(SpotlightSource), Vector2D.Zero, LayerDepth.Instances)
        {
            _stencilPass = stencilPass;
            _window = window;
            LayerName = SceneAggregate.LayerNameInstances;
        }

        public override void OnStep(double dt)
        {
            if (Input is null) return;

            // 滚轮调整遮罩半径
            var scroll = Input.MouseScrollDelta;
            if (scroll != 0f)
                _maskRadius = Math.Clamp(_maskRadius + scroll * 15f, 20f, 400f);

            // 聚光灯圆心跟随鼠标（默认单位相机：屏幕坐标 == 世界坐标）
            var mouse = Input.MousePosition;
            _stencilPass.SetMaskCircle(new Vector2(mouse.X, mouse.Y), _maskRadius);
        }

        public override void OnKeyDown(InputKey key)
        {
            switch (key)
            {
                case InputKey.Escape:
                    _window.NativeWindow.Close();
                    break;
                case InputKey.M:
                    _showOutside = !_showOutside;
                    _stencilPass.State = _showOutside
                        ? StencilMaskState.FogOfWarHole
                        : StencilMaskState.Spotlight;
                    Console.WriteLine($"[Stencil] Mode={( _showOutside ? "ShowOutside" : "ShowInside" )}");
                    break;
            }
        }
    }

    /// <summary>彩色方块实例（Instances 层）</summary>
    private sealed class ColorBox : GameInstance
    {
        private readonly uint _tex;
        private readonly Vector4 _color;

        public ColorBox(Vector2 pos, Vector4 color, uint tex)
            : base(nameof(ColorBox), new Vector2D(pos.X, pos.Y), LayerDepth.Instances)
        {
            _tex = tex;
            _color = color;
        }

        public override void OnDraw(ISpriteBatch batch)
        {
            var p = Transform.Position;
            batch.Draw(_tex, new Vector2(p.X - 25, p.Y - 25), new Vector2(50, 50), _color,
                new Vector4(0, 0, 1, 1));
        }
    }
}

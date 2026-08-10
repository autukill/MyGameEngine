namespace TextRendering.VisualTests;

using System.Numerics;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.TextRendering.Domain;
using GameEngine.Features.TextRendering.Infrastructure;
using GameEngine.Hosting;

internal static class Program
{
    private static readonly SceneRef DemoScene = new("TextRendering.Visual");

    private static void Main(string[] args)
    {
        bool smoke = args.Contains("--smoke", StringComparer.Ordinal);
        EngineWindowOptions options = (EngineWindowOptions.Default with
        {
            Title = "Text Rendering - 中文 / World / SceneGui",
            IsVisible = !smoke,
            VSync = !smoke
        }).WithFixedUpdateRate(60d);

        using GameApplication game = GameApplication
            .Create(options)
            .UseDefault2DRenderer()
            .AddScene(DemoScene, context =>
            {
                context.Scene.Background = BackgroundConfig.FromColor(
                    new Vector4(.018f, .025f, .055f, 1f));

                (string latinPath, string cjkPath) = FindFonts();
                FontRef latin = context.Text.LoadFont("visual.latin", latinPath);
                FontRef cjk = context.Text.LoadFont("visual.cjk", cjkPath);
                FontFamily family = context.Text.CreateFamily(latin, cjk);

                context.Camera.Position = new Vector2(80, 30);
                context.Camera.Zoom = 1.1f;
                context.Scene.Add(new TextDemo(context.Text, family));
                context.Scene.Add(new CameraAndExitController(
                    context.Camera,
                    context.Close,
                    smoke));
            })
            .StartScene(DemoScene)
            .Build();

        game.Run();
    }

    private static (string Latin, string Cjk) FindFonts()
    {
        string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        string? latin = FirstExisting(
            Path.Combine(fonts, "arial.ttf"),
            Path.Combine(fonts, "segoeui.ttf"),
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/liberation2/LiberationSans-Regular.ttf",
            "/System/Library/Fonts/Supplemental/Arial.ttf");
        string? cjk = FirstExisting(
            Path.Combine(fonts, "msyh.ttc"),
            Path.Combine(fonts, "simhei.ttf"),
            "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
            "/System/Library/Fonts/PingFang.ttc");
        if (latin is null || cjk is null)
            throw new FileNotFoundException(
                "Text visual test requires an installed Latin font and a CJK font.");
        return (latin, cjk);
    }

    private static string? FirstExisting(params string[] candidates) =>
        candidates.FirstOrDefault(File.Exists);

    private sealed class TextDemo : GameInstance
    {
        private readonly TextRuntime _text;
        private readonly PreparedTextLayout _worldTitle;
        private readonly PreparedTextLayout _worldDetails;
        private readonly PreparedTextLayout _worldParagraph;
        private readonly PreparedTextLayout _gui;

        public TextDemo(TextRuntime text, FontFamily family)
        {
            _text = text;
            _worldTitle = text.Prepare(family, "你好，MyGameEngine!", 42f);
            _worldDetails = text.Prepare(family, "World Text · ABC 123 · 中文回退", 24f);
            _worldParagraph = text.Prepare(
                family,
                "多行中文会在字素边界稳定换行，标点不会轻易出现在行首。\n" +
                "Latin words prefer whitespace boundaries; emoji clusters stay together.",
                22f,
                new TextLayoutOptions(
                    520f,
                    TextWrapMode.Word,
                    LineSpacing: 6f));
            _gui = text.Prepare(
                family,
                "SceneGui：居中多行文本\n不受 Camera 移动、缩放和旋转影响",
                22f,
                new TextLayoutOptions(
                    620f,
                    TextWrapMode.Word,
                    TextAlignment.Center,
                    LineSpacing: 3f));
        }

        public override void OnDraw(ISpriteBatch batch)
        {
            _text.Draw(batch, _worldTitle, new Vector2(180, 240), new Vector4(.35f, .9f, 1f, 1f));
            _text.Draw(batch, _worldDetails, new Vector2(180, 300), new Vector4(1f, .82f, .35f, 1f));
            _text.Draw(batch, _worldParagraph, new Vector2(180, 342), new Vector4(.85f, .9f, 1f, 1f));
        }

        public override void OnDrawGUI(ISpriteBatch batch)
        {
            _text.Draw(batch, _gui, new Vector2(24, 24), Vector4.One);
        }
    }

    private sealed class CameraAndExitController : GameInstance
    {
        private readonly Camera2D _camera;
        private readonly Action _close;
        private readonly bool _smoke;
        private double _elapsed;
        private int _steps;

        public CameraAndExitController(Camera2D camera, Action close, bool smoke)
        {
            _camera = camera;
            _close = close;
            _smoke = smoke;
            TimeMode = InstanceTimeMode.Unscaled;
        }

        public override void OnStep(double deltaTime)
        {
            _elapsed += deltaTime;
            _steps++;
            _camera.Rotation = MathF.Sin((float)_elapsed * .6f) * .04f;
            if (_smoke && _steps >= 4) _close();
        }

        public override void OnKeyDown(InputKey key)
        {
            if (key == InputKey.Escape) _close();
        }
    }
}

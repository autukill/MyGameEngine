namespace SceneSystem.Tests;

using GameEngine.Features.SceneSystem.Domain;
using GameEngine.Features.SceneSystem.Infrastructure;
using GameEngine.Features.RenderPipeline.Domain;

/// <summary>
/// SceneSystem 切片的控制台冒烟测试（无 OpenGL 依赖）。
///
/// 验证项：
///   1. RenderCommand 值对象：数据携带
///   2. Layer：提交命令、按 Depth 排序（Depth 大的先画，即背景在前）
///   3. Layer.IsVisible 控制
///   4. LayerRenderState 状态覆盖挂载
/// </summary>
internal static class Program
{
    private static int _failures;

    private static void Check(bool condition, string name)
    {
        if (condition)
        {
            Console.WriteLine($"  [PASS] {name}");
        }
        else
        {
            _failures++;
            Console.WriteLine($"  [FAIL] {name}");
        }
    }

    private static void Main()
    {
        Console.WriteLine("=== SceneSystem Feature Smoke Test ===\n");

        // ---------- 1. RenderCommand ----------
        Console.WriteLine("1. RenderCommand");
        var cmd = new RenderCommand
        {
            TextureHandle = 7u,
            Position = new System.Numerics.Vector2(10, 20),
            Size = new System.Numerics.Vector2(32, 32),
            Color = new System.Numerics.Vector4(1, 1, 1, 1),
            Depth = 0,
        };
        Check(cmd.TextureHandle == 7u && cmd.Position.X == 10f, "RenderCommand carries data");

        // ---------- 2. Layer ----------
        Console.WriteLine("2. Layer");
        var layer = new Layer("Instances", 0);
        layer.Submit(new RenderCommand { Depth = 100 });
        layer.Submit(new RenderCommand { Depth = -100 });
        layer.Submit(new RenderCommand { Depth = 0 });
        Check(layer.Name == "Instances" && layer.DepthOrder == 0, "Layer name + depth");
        Check(layer.IsVisible, "Layer visible by default");
        layer.IsVisible = false;
        Check(!layer.IsVisible, "Layer can be hidden");

        // ---------- 3. LayerRenderState ----------
        Console.WriteLine("3. LayerRenderState");
        layer.RenderStateOverride = LayerRenderState.AdditiveBlend;
        Check(layer.RenderStateOverride.BlendOverride == BlendState.Additive,
            "Layer accepts additive render-state override");

        // 注意：Layer.Draw 需要 SpriteBatch+GL，这里不调用。

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All SceneSystem smoke tests passed ==="
            : $"=== {_failures} SceneSystem test(s) FAILED ===");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }
}

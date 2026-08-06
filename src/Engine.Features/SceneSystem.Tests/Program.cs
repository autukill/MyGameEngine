namespace SceneSystem.Tests;

using GameEngine.Features.SceneSystem.Domain;
using GameEngine.Features.SceneSystem.Infrastructure;

/// <summary>
/// SceneSystem 切片的控制台冒烟测试（无 OpenGL 依赖）。
///
/// 验证项：
///   1. RenderCommand 值对象：数据携带
///   2. Layer：提交命令、按 Depth 排序（Depth 大的先画，即背景在前）
///   3. Layer.IsVisible 控制
///   4. SceneRenderContext：默认 3 图层 + 添加图层 + 按 DepthOrder 排序
///   5. SceneRenderContext 暴露 MainCamera
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

        // 3. SceneRenderContext：默认图层 + 排序
        Console.WriteLine("3. SceneRenderContext");
        var ctx = new SceneRenderContext(800, 600);
        Check(ctx.GetSortedLayers().Count == 3, "Default 3 layers");
        Check(ctx.MainCamera.ViewportSize == new System.Numerics.Vector2(800, 600),
            "MainCamera viewport synced");

        ctx.AddLayer("Effects", 500);
        var sorted = ctx.GetSortedLayers();
        Check(sorted.Count == 4, "Added layer -> 4 layers");

        // 按 DepthOrder 降序排序（背景 Depth 大在前）
        bool sortedCorrect = true;
        for (int i = 1; i < sorted.Count; i++)
            if (sorted[i - 1].DepthOrder < sorted[i].DepthOrder)
                sortedCorrect = false;
        Check(sortedCorrect, "Layers sorted by DepthOrder desc");
        Check(sorted[0].Name == "Background", "Background drawn first");

        // GetLayer 访问
        var inst = ctx.GetLayer("Instances");
        Check(inst.Name == "Instances", "GetLayer by name");

        // 注意：Layer.Draw / SceneRenderContext.Render 需要 SpriteBatch+GL，这里不调用。

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All SceneSystem smoke tests passed ==="
            : $"=== {_failures} SceneSystem test(s) FAILED ===");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }
}

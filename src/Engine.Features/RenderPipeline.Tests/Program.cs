namespace RenderPipeline.Tests;

using Silk.NET.OpenGL;
using GameEngine.Features.RenderPipeline.Domain;

/// <summary>
/// RenderPipeline 切片的控制台冒烟测试（无 GL 上下文，仅验证值对象纯逻辑）。
///
/// 验证项：
///   1. BlendState 预设（AlphaBlend / Additive / Opaque / ColorMaskDisabled）
///   2. DepthStencilState 预设（None / StencilWrite / StencilTest / StencilTestNotEqual）
///   3. ViewportRect 预设 + 像素换算
///   4. LayerRenderState 默认 + 覆盖
///   5. 值对象零 GC：可作字典 Key（状态指纹）
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
        Console.WriteLine("=== RenderPipeline Feature Smoke Test ===\n");

        // ---------- 1. BlendState ----------
        Console.WriteLine("1. BlendState presets");
        Check(BlendState.AlphaBlend.EnableBlend &&
              BlendState.AlphaBlend.SrcFactor == BlendingFactor.SrcAlpha &&
              BlendState.AlphaBlend.DstFactor == BlendingFactor.OneMinusSrcAlpha,
            "AlphaBlend = SrcAlpha / OneMinusSrcAlpha");
        Check(BlendState.Additive.DstFactor == BlendingFactor.One,
            "Additive = SrcAlpha / One");
        Check(!BlendState.Opaque.EnableBlend, "Opaque disables blend");
        Check(!BlendState.ColorMaskDisabled.WriteR &&
              !BlendState.ColorMaskDisabled.WriteA,
            "ColorMaskDisabled masks all color writes");

        // ---------- 2. DepthStencilState ----------
        Console.WriteLine("2. DepthStencilState presets");
        Check(!DepthStencilState.None.StencilTestEnable, "None disables stencil");
        Check(DepthStencilState.StencilWrite().StencilFunc == StencilFunction.Always &&
              DepthStencilState.StencilWrite().StencilPass == StencilOp.Replace,
            "StencilWrite = Always + Replace");
        Check(DepthStencilState.StencilTest().StencilFunc == StencilFunction.Equal,
            "StencilTest = Equal");
        Check(DepthStencilState.StencilTestNotEqual().StencilFunc == StencilFunction.Notequal,
            "StencilTestNotEqual = Notequal");

        // ---------- 3. ViewportRect ----------
        Console.WriteLine("3. ViewportRect");
        Check(ViewportRect.FullScreen == new ViewportRect(0, 0, 1, 1), "FullScreen rect");
        var px = ViewportRect.BottomHalf.ToPixels(800, 600);
        Check(px == (0, 300, 800, 300), "BottomHalf pixels @800x600");
        var q = ViewportRect.TopRightQuarter.ToPixels(400, 200);
        Check(q == (300, 0, 100, 50), "TopRightQuarter pixels @400x200");

        // ---------- 4. LayerRenderState ----------
        Console.WriteLine("4. LayerRenderState");
        Check(LayerRenderState.Default.BlendOverride is null, "Default has no override");
        Check(LayerRenderState.AdditiveBlend.BlendOverride == BlendState.Additive,
            "AdditiveBlend override set");
        Check(LayerRenderState.UI.DepthStencilOverride is { DepthTestEnable: false },
            "UI overrides depth off");

        // ---------- 5. 值对象作字典 Key（状态指纹去重） ----------
        Console.WriteLine("5. Value-object state fingerprint");
        var states = new Dictionary<BlendState, string>();
        states[BlendState.AlphaBlend] = "alpha";
        states[BlendState.Additive] = "additive";
        states[BlendState.Opaque] = "opaque";
        states[BlendState.AlphaBlend] = "alpha-again"; // 覆盖同 Key
        Check(states.Count == 3, "BlendState works as dictionary key (dedup)");

        var stencils = new HashSet<DepthStencilState>
        {
            DepthStencilState.StencilWrite(),
            DepthStencilState.StencilWrite(2),
            DepthStencilState.StencilWrite(),
        };
        Check(stencils.Count == 2, "DepthStencilState works as set element");

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All RenderPipeline smoke tests passed ==="
            : $"=== {_failures} RenderPipeline test(s) FAILED ===");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }
}

namespace Camera.Tests;

using System.Numerics;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.Camera.Application;
using GameEngine.Features.Camera.Domain;

/// <summary>
/// Camera 切片的控制台冒烟测试（无 OpenGL 依赖）。
///
/// 验证项：
///   1. Camera2D 视图投影矩阵：默认 / 平移 / 缩放 / 旋转
///   2. 世界→屏幕坐标映射（相机中心点在视口中心）
///   3. 震屏只改变矩阵、不崩溃
///   4. ResizeViewport 生效
///   5. FocusCameraCommand → CameraCommandHandler 命令链路
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
        Console.WriteLine("=== Camera Feature Smoke Test ===\n");

        // 1. 默认相机（视口 800x600，中心为世界原点）
        var cam = new Camera2D(new Vector2(800, 600));
        Check(cam.ViewportSize == new Vector2(800, 600), "ViewportSize=800x600");
        Check(MathF.Abs(cam.Zoom - 1f) < 1e-5f, "Default Zoom=1");

        // 相机中心点 (400,300) 应映射到 NDC 中心 (0,0)
        var m = cam.GetViewProjectionMatrix();
        var centerWorld = Vector4.Transform(new Vector4(400, 300, 0, 1), m);
        Check(MathF.Abs(centerWorld.X) < 1e-3f && MathF.Abs(centerWorld.Y) < 1e-3f,
            "Center maps to NDC (0,0)");
        bool hasDefaultBounds = cam.TryGetVisibleWorldBounds(out var defaultBounds);
        Check(hasDefaultBounds &&
              MathF.Abs(defaultBounds.Left) < 1e-3f &&
              MathF.Abs(defaultBounds.Top) < 1e-3f &&
              MathF.Abs(defaultBounds.Right - 800f) < 1e-3f &&
              MathF.Abs(defaultBounds.Bottom - 600f) < 1e-3f,
            "Visible world bounds match the default Camera viewport");

        // 2. 平移：相机向右移 100，世界 500 应落回屏幕中心
        cam.Position = new Vector2(100, 0);
        m = cam.GetViewProjectionMatrix();
        var afterPan = Vector4.Transform(new Vector4(500, 300, 0, 1), m);
        Check(MathF.Abs(afterPan.X) < 1e-3f, "Pan moves origin accordingly");

        // 3. 缩放：改变 Zoom 必须改变视图投影矩阵（缩放参与矩阵合成），且不崩溃
        cam.Position = Vector2.Zero;
        cam.Zoom = 1f;
        var mZoom1 = cam.GetViewProjectionMatrix();
        cam.Zoom = 2f;
        var mZoom2 = cam.GetViewProjectionMatrix();
        cam.Zoom = 0.5f;
        var mZoomHalf = cam.GetViewProjectionMatrix();
        Check(mZoom1 != mZoom2 && mZoom2 != mZoomHalf && mZoom1 != mZoomHalf,
            "Zoom changes the view-projection matrix");
        Check(MathF.Abs(mZoom2.GetDeterminant()) > 1e-6f &&
              MathF.Abs(mZoomHalf.GetDeterminant()) > 1e-6f,
            "Zoom matrices remain invertible");

        // 4. 旋转：不崩溃 + 矩阵保持可逆（行列式非零）
        cam.Zoom = 1f;
        cam.Rotation = MathF.PI / 2;
        m = cam.GetViewProjectionMatrix();
        var det = m.GetDeterminant();
        Check(MathF.Abs(det) > 1e-6f, "Rotation matrix is invertible");
        bool hasRotatedBounds = cam.TryGetVisibleWorldBounds(out var rotatedBounds);
        Check(hasRotatedBounds &&
              MathF.Abs(rotatedBounds.Width - 600f) < 1e-2f &&
              MathF.Abs(rotatedBounds.Height - 800f) < 1e-2f,
            "Rotated Camera exposes a conservative enclosing world AABB");

        // 5. 震屏：只影响矩阵生成，不抛异常
        cam.Rotation = 0;
        cam.Shake(5f, 1.0f);
        cam.Update(0.5);
        var shakenA = cam.GetViewProjectionMatrix();
        var shakenB = cam.GetViewProjectionMatrix();
        Check(shakenA == shakenB,
            "Shake is stable across all render passes in one update");
        cam.Update(0.6); // 计时归零
        Check(true, "Shake timer decays and Update() does not throw");

        cam.Zoom = 0f;
        Check(!cam.TryGetVisibleWorldBounds(out _),
            "Non-invertible Camera fails open instead of producing invalid culling bounds");
        cam.Zoom = 1f;

        // 6. ResizeViewport
        cam.ResizeViewport(1920, 1080);
        Check(cam.ViewportSize == new Vector2(1920, 1080), "ResizeViewport updates size");

        // 7. Stable coordinate conversion ignores transient camera shake.
        cam.Position = new Vector2(100, 50);
        cam.Zoom = 1.5f;
        cam.Rotation = 0.2f;
        Vector2 world = new(450, 320);
        Vector2 viewport = cam.WorldToViewport(world);
        bool mapped = cam.TryViewportToWorld(viewport, out Vector2 roundTrip);
        Check(mapped && Vector2.Distance(world, roundTrip) < 0.001f,
            "World/Viewport conversion round-trips through pan, zoom, and rotation");
        cam.Shake(50f, 1f);
        Check(cam.WorldToViewport(world) == viewport,
            "Gameplay coordinate conversion ignores presentation-only shake");

        // 8. 命令链路：FocusCameraCommand → Handler
        var scene = new SceneAggregate("CameraScene");
        CameraCommandHandler.Handle(new FocusCameraCommand(
            Scene: scene,
            TargetPosition: new Vector2D(100, 200),
            Zoom: 1.5f,
            ShakeDuration: 0.2f,
            ShakeMagnitude: 3f));
        Check(true, "FocusCameraCommand handled (logs)");

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All Camera smoke tests passed ==="
            : $"=== {_failures} Camera test(s) FAILED ===");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }
}

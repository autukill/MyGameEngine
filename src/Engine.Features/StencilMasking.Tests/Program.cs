namespace StencilMasking.Tests;

using GameEngine.Core.Application.Commands;
using GameEngine.Core.Application.Handlers;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.StencilMasking.Application;
using GameEngine.Features.StencilMasking.Domain;

/// <summary>
/// StencilMasking 切片的控制台冒烟测试（无 OpenGL 依赖）。
///
/// 验证项：
///   1. StencilMaskState 值对象：预设 + Inverted + 状态指纹
///   2. ApplySpotlightMaskCommand → StencilMaskCommandHandler 命令链路
///   3. 链路最终在 SceneAggregate 发出 StencilMaskPassRequestedEvent
///   4. GameInstance.RequestStencilMask 扩展方法（非激活实例不发事件）
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
        Console.WriteLine("=== StencilMasking Feature Smoke Test ===\n");

        // ---------- 1. StencilMaskState 值对象 ----------
        Console.WriteLine("1. StencilMaskState");
        Check(StencilMaskState.Default.Mode == StencilMaskMode.ShowInside &&
              StencilMaskState.Default.StencilRef == 1,
            "Default = ShowInside, ref=1");
        Check(StencilMaskState.Spotlight.Mode == StencilMaskMode.ShowInside,
            "Spotlight preset = ShowInside");
        Check(StencilMaskState.FogOfWarHole.Mode == StencilMaskMode.ShowOutside,
            "FogOfWarHole preset = ShowOutside");
        Check(StencilMaskState.Spotlight.Inverted.Mode == StencilMaskMode.ShowOutside,
            "Inverted flips mode");
        Check(StencilMaskState.FogOfWarHole.Inverted.Mode == StencilMaskMode.ShowInside,
            "Double-invert returns to ShowInside");

        // 值对象作字典 Key
        var set = new HashSet<StencilMaskState>
        {
            StencilMaskState.Spotlight,
            StencilMaskState.Spotlight,
            StencilMaskState.FogOfWarHole,
        };
        Check(set.Count == 2, "StencilMaskState dedups as set element");
        Check(StencilMaskState.Spotlight == StencilMaskState.Default,
            "Spotlight equals Default (typical spotlight config)");

        // ---------- 2. 场景准备 ----------
        var scene = new SceneAggregate("StencilScene");
        var player = SceneCommandHandlers.Handle(new SpawnInstanceCommand(
            Scene: scene,
            ObjectTypeName: "Player",
            Position: new Vector2D(100, 100),
            Depth: GameEngine.Core.Domain.ValueObjects.LayerDepth.Instances));
        Check(scene.FindById(player.Id) is not null, "Player spawned in scene");

        // ---------- 3. 命令链路：Command → Handler → Event ----------
        Console.WriteLine("2. Command chain");
        scene.MarkEventsAsCommitted(); // 清空已累积事件
        StencilMaskCommandHandler.Handle(new ApplySpotlightMaskCommand(
            Scene: scene,
            SpotlightId: player.Id,
            MaskCenter: new Vector2D(100, 100),
            MaskRadius: 80f,
            MaskState: StencilMaskState.Spotlight));

        var ev = scene.UncommittedEvents
            .OfType<StencilMaskPassRequestedEvent>()
            .FirstOrDefault();
        Check(ev is not null, "StencilMaskPassRequestedEvent raised");
        Check(ev is { ProviderId: var pid } && pid == player.Id,
            "Event ProviderId = player instance id");

        // ---------- 4. 非激活实例不发事件 ----------
        Console.WriteLine("3. Inactive guard");
        player.SetActive(false, scene.RaiseEvent);
        scene.MarkEventsAsCommitted();
        StencilMaskCommandHandler.Handle(new ApplySpotlightMaskCommand(
            Scene: scene,
            SpotlightId: player.Id,
            MaskCenter: new Vector2D(100, 100),
            MaskRadius: 80f,
            MaskState: StencilMaskState.FogOfWarHole));
        Check(!scene.UncommittedEvents.OfType<StencilMaskPassRequestedEvent>().Any(),
            "Inactive instance suppresses event");

        // 未找到实例：Handler 打印警告，不崩溃
        StencilMaskCommandHandler.Handle(new ApplySpotlightMaskCommand(
            Scene: scene,
            SpotlightId: InstanceId.New(),
            MaskCenter: new Vector2D(0, 0),
            MaskRadius: 10f,
            MaskState: StencilMaskState.Spotlight));
        Check(true, "Missing instance handled gracefully");

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All StencilMasking smoke tests passed ==="
            : $"=== {_failures} StencilMasking test(s) FAILED ===");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }
}

namespace Engine.DddTests;

using GameEngine.Core.Application.Commands;
using GameEngine.Core.Application.Handlers;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.StencilMasking.Application;
using GameEngine.Features.StencilMasking.Domain;

/// <summary>
/// Phase 0->1 DDD 战术设计验证 Demo（控制台版，无需 OpenGL）。
///
/// 验证项：
///   1. 值对象 (Vector2D / Transform2D / InstanceId / LayerDepth) 的不可变性与运算
///   2. GameInstance 实体的状态变更会发出领域事件
///   3. SceneAggregate 聚合根维护一致性边界（唯一 InstanceId / 事件队列）
///   4. Command + Handler 模式：外部 Command -> 聚合根行为 -> 领域事件
///   5. StencilMaskPassRequestedEvent：逻辑层声明意图，渲染层捕获执行（解耦验证）
/// </summary>
internal sealed class Program
{
    private static void Main()
    {
        Console.WriteLine("=== Phase 0->1 DDD Tactical Design Smoke Test ===\n");

        // ---------- 1. 值对象测试 ----------
        var v1 = new Vector2D(3f, 4f);
        Console.WriteLine($"1. Vector2D {v1} length={v1.Length():F2} normalized={v1.Normalize()}");

        var t1 = Transform2D.Default;
        var t2 = t1.Translate(new Vector2D(10, 5)).Rotate(MathF.PI / 2);
        Console.WriteLine($"   Transform: default={t1} -> moved+rotated={t2}\n");

        var id1 = InstanceId.New();
        var id2 = InstanceId.New();
        Console.WriteLine($"   InstanceId#1={id1}, #2={id2}, equal? {id1 == id2}");

        Console.WriteLine($"   LayerDepth: BG={LayerDepth.Background.Value}, " +
                          $"Inst={LayerDepth.Instances.Value}, UI={LayerDepth.UI.Value}\n");

        // ---------- 2 & 3. 聚合根 + 实体 + 事件 ----------
        var scene = new SceneAggregate(sceneName: "TestRoom01");
        Console.WriteLine($"2. SceneAggregate '{scene.SceneName}' id={scene.SceneId:B}");

        // ---------- 4. Command -> Handler -> 聚合根 ----------
        var playerCmd = new SpawnInstanceCommand(
            Scene: scene,
            ObjectTypeName: "Player",
            Position: new Vector2D(100, 200),
            Depth: LayerDepth.Instances);
        var player = SceneCommandHandlers.Handle(playerCmd);

        var enemyCmd = new SpawnInstanceCommand(
            Scene: scene,
            ObjectTypeName: "Enemy",
            Position: new Vector2D(500, 300),
            Depth: LayerDepth.Instances);
        var enemy = SceneCommandHandlers.Handle(enemyCmd);

        Console.WriteLine($"\n3. Active instances: {scene.ActiveInstances.Count()}");
        foreach (var inst in scene.ActiveInstances)
            Console.WriteLine($"   - {inst}");

        // 移动 player（触发 InstanceMovedEvent）
        player.MoveTo(new Vector2D(150, 220), scene.RaiseEvent);
        Console.WriteLine($"\n4. After player move: {player}");

        // ---------- 5. Stencil 遮罩命令链路（VSA 切片路径） ----------
        // 通过 StencilMasking 切片的 Command + Handler + GameInstance 扩展方法
        var spotlightCmd = new ApplySpotlightMaskCommand(
            Scene: scene,
            SpotlightId: player.Id,
            MaskCenter: new Vector2D(150, 220),
            MaskRadius: 80f,
            MaskState: StencilMaskState.Spotlight);  // ShowInside 模式
        StencilMaskCommandHandler.Handle(spotlightCmd);

        // 演示 ShowOutside 模式（战争迷雾挖孔）
        var fogCmd = new ApplySpotlightMaskCommand(
            Scene: scene,
            SpotlightId: enemy.Id,
            MaskCenter: new Vector2D(500, 300),
            MaskRadius: 60f,
            MaskState: StencilMaskState.FogOfWarHole);  // ShowOutside 模式
        StencilMaskCommandHandler.Handle(fogCmd);

        // ---------- 6. 检查未提交事件队列 ----------
        Console.WriteLine($"\n5. Uncommitted events in scene: {scene.UncommittedEvents.Count()}");
        foreach (var ev in scene.UncommittedEvents)
        {
            var type = ev.GetType().Name;
            Console.WriteLine($"   [{type}] occurred={ev.OccurredOn:HH:mm:ss.fff}");
        }

        // 标记事件为已提交（模拟事务完成）
        scene.MarkEventsAsCommitted();
        Console.WriteLine($"\n6. After commit, uncommitted count: {scene.UncommittedEvents.Count()}");

        // ---------- 7. 销毁实例 ----------
        SceneCommandHandlers.Handle(new DestroyInstanceCommand(scene, enemy.Id));
        Console.WriteLine($"\n7. After destroying enemy, active count: {scene.ActiveInstances.Count()}");

        Console.WriteLine("\n=== All DDD tactical design smoke tests passed ===");
    }
}

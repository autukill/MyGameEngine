# Spawn/Wave Authoring 使用指南

`SpawnSequence` 把 Spawner 中重复的浮点倒计时、波次游标、循环和并发上限收敛为一个确定性、owner-driven 时间线。它只决定“何时产生一次 emission”，具体生成哪个 Prefab、参数如何随机、出生位置在哪里仍由游戏代码决定。

## 创建时间线

```csharp
private static readonly SpawnSequence Timeline = new SpawnSequenceBuilder()
    .Delay(1.0)
    .Wave(count: 5, intervalSeconds: 0.4)
    .Delay(2.0)
    .Wave(count: 10, intervalSeconds: 0.2)
    .Build(SpawnSequenceRepeat.Loop, maximumConcurrent: 24);
```

- `Delay` 必须为有限正数。
- `Wave` 的数量必须大于零；第一项进入 Wave 时立即就绪，`intervalSeconds` 是后续项间隔。
- `Once` 在最后一波完成后停止。
- `Loop` 从第一段重新开始；循环必须包含正时间间隔，避免一次 Update 无限发射。
- `maximumConcurrent` 是本时间线允许的最大存活数量。

## 在 GameInstance 中使用

```csharp
private readonly SpawnSequencePlayer _spawns = new(Timeline);
private readonly SpawnEmissionHandler _emit;

public EnemySpawner()
{
    _emit = EmitEnemy;
}

public override void OnStep(double deltaTime)
{
    _spawns.Update(
        deltaTime,
        CountInstances<Enemy>(),
        _emit);
}

private void EmitEnemy(in SpawnEmission emission)
{
    Spawn(EnemyPrefabs.Basic, new EnemySpawnArgs(
        position: ChoosePosition(),
        wave: emission.WaveIndex));
}
```

Player 不拥有 Scene 或全局 Manager。把 `Update` 放在 `OnStep` 中后，它自然继承 GameInstance 的 active、Gameplay Pause、TimeScale 和 `InstanceTimeMode` 调度；`SpawnSequencePlayer` 不重复实现第二套时间系统。

回调应该在构造时缓存。每帧重新创建 Lambda/Delegate 会产生调用方分配，不属于 Player 的 0 B 保证。

## 并发门控

`activeCount` 应传入当前已提交的存活实例数。一次 `Update` 内已经发出的 emission 会被 Player 本地计入，所以即使 Scene 要到安全帧边界才提交 Spawn，也不会在同一帧突破上限。

当容量已满：

- Timeline 停在当前 emission，不丢弃它。
- `IsWaitingForCapacity` 为 `true`。
- 后续 Update 在容量释放后继续。
- emission 回调无论最终是否生成实例，都消费一个本次调用的容量槽；回调不要静默忽略计划事件。

## 状态与确定性

```csharp
SpawnSequencePlayerState state = _spawns.CaptureState();
_spawns.RestoreState(state);
```

Snapshot 包含 Segment、Wave、Item、Loop Iteration、总 emission、剩余时间和完成状态，可由游戏自己的 Save/Checkpoint 协议持久化。当前 Replay Bundle 仍只记录输入与 Hash，不会自动反射抓取 Player 状态。

`SpawnEmission` 提供：

- `SequenceIteration`
- `WaveIndex`
- `ItemIndex`
- `TotalEmissionIndex`

同一时间线、delta、容量和回调结果会产生相同 emission 顺序。大 delta 可以跨越多个段和波次。

## 控制

- `Complete()`：显式终止 Once 或 Loop。
- `Restart()`：回到第一段并清空计数器。
- `IsCompleted`：Once 完成或显式停止。
- `RemainingSeconds`：当前 Delay/Interval 的剩余时间。

## 当前边界

- 不提供全局 Wave Manager、协程 DSL 或关卡脚本语言。
- 不自动统计“属于某一 Spawner”的实例；调用方决定 `activeCount` 的查询范围。
- 不内置掉落、难度曲线、路径、Formation、Boss Phase 或胜利条件。
- 不把 Callback、Prefab 或随机数写进 Core Snapshot。
- 每个 Player 由一个 owner 更新，不支持多线程并发调用。

真实用例见 `playgrounds/Asteroids/AsteroidSpawner.cs`：循环 Timeline 管理 0.45 秒 cadence 和 24 个 Asteroid 上限，游戏回调继续负责随机边缘、速度、半径、目标方向和 Prefab Spawn。

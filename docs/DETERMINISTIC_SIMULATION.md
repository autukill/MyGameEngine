# 确定性 Simulation Clock 与 Gameplay Random

确定性模拟的基础不是读取现实时间，而是让玩法只依赖明确的 Step 序列、固定 delta、逻辑输入和受控随机流。当前已提供 `SimulationClock`、固定 PCG32 `GameplayRandom`、一键固定 UPS 配置、按 Tick 的逻辑输入录制/回放，以及状态 Hash 和首次分叉诊断。

## 固定更新配置

```csharp
EngineWindowOptions options = EngineWindowOptions.Default
    .WithFixedUpdateRate(60d);
```

`WithFixedUpdateRate(60)` 同时设置：

- 原生 `UpdatesPerSecond = 60`。
- 逻辑 `FixedDeltaTime = 1 / 60`。

渲染仍由 VSync 与 `FramesPerSecond` 独立控制。只设置更新频率而继续使用测量 delta，或只覆盖 delta 却不限更新频率，都不构成完整的固定步长配置。

## Simulation Clock

`SceneAggregate.Clock` 暴露当前 `SimulationClockSnapshot`；`GameInstance` 子类通过 protected `SimulationTime` 读取同一快照：

```csharp
public override void OnStep(double deltaTime)
{
    ulong tick = SimulationTime.StepIndex;
    double elapsed = SimulationTime.GameplayElapsedSeconds;
}
```

快照包含：

- `StepIndex`：第一次 `PerformStep` 为 1，每次逻辑更新严格加一。
- `UnscaledDeltaSeconds` / `GameplayDeltaSeconds`。
- `UnscaledElapsedSeconds` / `GameplayElapsedSeconds`。
- `TimeScale` / `IsPaused`。

Clock 在 Step 生命周期开始前推进，所以同一 Step 的 Alarm、Begin Step、Step 和 End Step 看到相同 Tick。暂停时 StepIndex 与 Unscaled 时间继续推进，Gameplay delta 为零且 Gameplay 累计时间冻结；普通 Gameplay 实例不被调度，Unscaled 实例可以观察暂停 Tick。Scene 切换保留 Clock，创建新的 `SceneAggregate` 才建立新时间轴。

Clock 不读取 `DateTime` 或 `Stopwatch`。现有 Domain Event 的 `OccurredOn` 只属于外部诊断元数据，未来状态 Hash 和回放数据必须排除它。

## Owner-local Gameplay Random

```csharp
private readonly GameplayRandom _random = new(0xA57E201DUL);

bool leftEdge = _random.Chance(0.5f);
float speed = _random.Range(55f, 130f);
Vector2D offset = _random.InsideCircle(24f);
```

`GameplayRandom` 使用引擎固定的 PCG32 version 1：

- `NextUInt` 提供跨受支持 .NET/OS 的固定 bit sequence。
- `NextInt` 与整数 `Range` 使用 rejection sampling，不引入取模偏差。
- `NextFloat`、float `Range`、`Chance`。
- `Direction2D`、`InsideCircle`。
- `Choose(ReadOnlySpan<T>)`、`Shuffle(Span<T>)`。
- `Reset(seed)`、`CaptureState()`、`RestoreState(state)`。

每个 Spawner、AI 或掉落系统持有自己的随机流。不要共享一个 Scene 全局流，否则增加一个无关随机调用就会改变其他系统的结果。对象构造后所有随机调用保持零分配。

`Direction2D` 和 `InsideCircle` 使用 `MathF` 三角函数与平方根；随机 bit sequence 固定，但这些派生浮点结果当前只承诺同一受支持平台/运行时配置可复现，不承诺不同 CPU 与数学库之间逐位一致。

## 可复现条件

当前可以保证：给定相同引擎版本、固定更新配置、相同 pause/time-scale 操作、相同逻辑输入 Tick 流、相同随机 seed 与状态，Clock、输入和 RNG 序列可复现。录制与回放用法见[逻辑输入 Tick 录制与回放](LOGICAL_INPUT_REPLAY.md)。

当前尚不能保证跨所有环境的完整游戏回放，因为仍需约束：

1. 所有会影响未来模拟的自定义字段都必须显式贡献到状态 Hash。
2. 外部 IO、线程完成顺序和平台浮点差异。
3. 引擎/游戏版本和 Hash schema 的匹配。

状态录制与验证见[Gameplay 状态 Hash 与首次分叉诊断](GAMEPLAY_STATE_HASHING.md)。后续可增加单一磁盘回放容器与 Checkpoint；不在基础 Clock 或输入流中隐藏存档、网络同步或时间回溯系统。

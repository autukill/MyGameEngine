# 逻辑输入 Tick 录制与回放

输入回放记录“玩家想做什么”，而不是某个设备按下了什么键。MyGameEngine 在每个固定模拟 Tick 捕获已配置的 `ActionDown/Pressed/Released` 和 `Axis2D` 值；回放时不读取物理玩法输入，普通 `GameInstance` 仍使用原来的逻辑查询 API。

## 录制

```csharp
var recorder = new LogicalInputRecorder(initialFrameCapacity: 60 * 60);

using var game = GameApplication
    .Create(EngineWindowOptions.Default.WithFixedUpdateRate(60d))
    .ConfigureInput(input => input
        .BindAxis2D(GameInputs.Move,
            InputKey.A, InputKey.D, InputKey.W, InputKey.S)
        .BindAction(GameInputs.Fire, InputKey.Space))
    .RecordLogicalInput(recorder)
    .UseDefault2DRenderer()
    .ConfigureScene("Game", ConfigureGame)
    .Build();

game.Run();
LogicalInputRecording recording = recorder.Snapshot();
```

`LogicalInputRecorder` 每个 Tick 创建一个不可变 `LogicalInputFrame`。这是开发工具路径，录制期间按帧增长内存是显式成本；未启用录制的普通游戏不创建输入帧。Recorder 必须是尚未捕获任何帧的新对象。

## 回放

```csharp
using var replay = GameApplication
    .Create(EngineWindowOptions.Default.WithFixedUpdateRate(60d))
    .ConfigureInput(input => input
        // 可以换成另一套物理键位；逻辑名称和种类必须一致。
        .BindAxis2D(GameInputs.Move,
            InputKey.Left, InputKey.Right, InputKey.Up, InputKey.Down)
        .BindAction(GameInputs.Fire, InputKey.Enter))
    .ReplayLogicalInput(recording)
    .UseDefault2DRenderer()
    .ConfigureScene("Game", ConfigureGame)
    .Build();

replay.Run();
```

Hosting 的完整会话回放要求：

- `WithFixedUpdateRate(...)` 提供有限正数的固定 delta。
- 当前 fixed delta 与录制流保存的值逐位一致。
- 已配置至少一个逻辑 Action/Axis。
- 录制流非空，并从模拟 Tick 1 开始。
- 当前 `InputMap` 的逻辑名称和控制种类与录制协议完全一致。
- 每个 Tick 都有且只有一帧；缺帧、跳 Tick 或流耗尽会快速失败。

录制协议按名称序数排序，因此 `ConfigureInput` 的声明顺序和物理键位不属于兼容性判断。增加、删除或把同名 Action 改成 Axis 会使旧录制明确失效。

## Gameplay 代码不需要分支

```csharp
public override void OnStep(double deltaTime)
{
    Vector2D direction = InputAxis2D(GameInputs.Move);
    if (ActionPressed(GameInputs.Fire))
        Fire();
}
```

Live、Record 和 Replay 都经过同一个 `InputMap` 查询入口。当前帧的回放查询保持 0 B；暂停 Tick 仍会被记录，因为 `SimulationClock.StepIndex` 和 Unscaled 时间在暂停期间继续前进。

## 明确不在 v1 中的内容

- 原始 `KeyDown/KeyPressed/KeyReleased`、Mouse 查询和 `OnKeyDown/OnKeyUp` 不属于确定性录制。Record/Replay 模式会拒绝直接物理查询，而不是静默返回可能导致分叉的值。
- `LogicalInputRecording` 当前是版本化的内存模型，尚未提供磁盘 JSON/二进制格式、压缩或跨版本迁移。
- 回放流保存并校验 fixed delta，但不保存随机状态、Scene/实例状态或外部 IO；调用方仍须使用相同确定性 `GameplayRandom` seed。
- v1 不自动在流结束时关闭窗口；继续请求不存在的 Tick 会抛出清晰异常。
- 手动 `LogicalInputRecorder.BeginStep` 可以从任意正 Tick 录制局部片段，但 Hosting 的整局回放目前只接收从 Tick 1 开始的流。局部回放要等状态 Checkpoint 边界落地。

下一切片将增加稳定 Gameplay 状态 Hash 和首次分叉 Tick 诊断。Hash 不应包含 GPU、墙钟时间、Domain Event 的 `OccurredOn` 或字典物理布局。

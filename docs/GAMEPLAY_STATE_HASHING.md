# Gameplay 状态 Hash 与首次分叉诊断

状态 Hash 用来回答回放调试中最重要的两个问题：“第一次从哪个 Tick 开始不同？”以及“最先不同的是 Scene 还是哪一个实例？”它不是存档，也不会尝试反射任意对象字段。

## 显式声明自定义状态

`GameInstance` 已自动写入常见的引擎状态。玩法类只需补充会影响未来模拟的私有字段：

```csharp
public sealed class PlayerShip : GameInstance
{
    private readonly GameplayRandom _random = new(0x5EEDUL);
    private readonly GameplayCooldown _fireCooldown = new(0.15d);
    private Vector2D _velocity;
    public GameplayHealth Health { get; } = new(5f);

    protected override void OnWriteGameplayState(ref GameplayStateWriter writer)
    {
        writer.Write("ship.velocity", _velocity);
        writer.Write("ship.health", Health);
        writer.Write("ship.fireCooldown", _fireCooldown);
        writer.Write("ship.random", _random.CaptureState());
    }
}
```

`GameplayBehavior` 提供相同的 protected 钩子。内置 `LifetimeBehavior` 已写入 duration、remaining 和 completed；自定义 Behavior 应写入自己的可变配置与运行状态。

Writer v1 使用固定 FNV-1a 64 编码，名称、类型、IEEE 数值 bit 和写入顺序都是协议的一部分。支持 bool、整数、float/double、string、`Vector2D`、`Vector4`、`Transform2D`、`GameplayRandomState`、`GameplayHealth`、`GameplayCooldown` 与 `InputActionBuffer`。Writer 自身保持 0 B；不要使用每 Tick 动态拼接的字段名。

## 录制基线

```csharp
var inputRecorder = new LogicalInputRecorder();
var stateRecorder = new GameplayStateRecorder();

using var game = GameApplication
    .Create(EngineWindowOptions.Default.WithFixedUpdateRate(60d))
    .ConfigureInput(ConfigureInputs)
    .RecordLogicalInput(inputRecorder)
    .RecordGameplayState(stateRecorder)
    .UseDefault2DRenderer()
    .ConfigureScene("Game", ConfigureGame)
    .Build();

game.Run();

LogicalInputRecording input = inputRecorder.Snapshot();
GameplayStateRecording state = stateRecorder.Snapshot();
```

Hosting 在每个 Step 的 Spawn/Destroy 提交、效果事件同步和待处理 Scene 切换完成后捕获一次状态，因此 Tick Hash 表示下一次 Step 将要看到的完整 Gameplay 边界。状态录制和逻辑输入录制都保存相同 fixed delta，并要求连续 Tick。

## 回放并定位首次分叉

```csharp
var verifier = new GameplayStateVerifier(state);

using var replay = GameApplication
    .Create(EngineWindowOptions.Default.WithFixedUpdateRate(60d))
    .ConfigureInput(ConfigureInputs)
    .ReplayLogicalInput(input)
    .VerifyGameplayState(verifier)
    .UseDefault2DRenderer()
    .ConfigureScene("Game", ConfigureGame)
    .Build();

replay.Run();
```

匹配时继续运行；第一次不匹配时 Hosting 抛出 `GameplayStateDivergenceException`。结构化 `Divergence` 与 `verifier.FirstDivergence` 包含：

- 实际发生分叉的 `StepIndex`。
- expected/actual 总 Hash。
- expected/actual 首个不同 contributor。
- contributor 的稳定加入序号、逻辑实例类型和局部 Hash。

Verifier 只保留第一次差异，不用后续连锁变化覆盖根因。基线耗尽、Tick 不一致和 fixed delta 不一致同样快速失败。

## 自动覆盖范围

Scene contributor 包含：Scene 名称、完整 `SimulationClockSnapshot`、暂停状态/请求数和实例数量。

每个实例 contributor 按稳定加入序号排列，自动包含：

- 逻辑类型、active/persistent、时间域。
- Transform、Sprite 名称、ImageIndex/ImageSpeed。
- Collider、按名称排序的 Gameplay Tags。
- 按名称排序的 Alarm 与剩余时间。
- Behavior 声明顺序和各 Behavior 自定义 Hash。

`InstanceId` 使用 Version 7 GUID，不适合作为跨运行状态协议，因此不会进入 Hash；同样的生成/销毁顺序会得到相同的稳定 contributor sequence。

## 明确排除与限制

- 不反射派生类字段。未在 `OnWriteGameplayState` 中声明的私有状态无法被检测。
- `Properties` 动态对象包、Input/GPU/Shader/RenderTarget、颜色和其他纯表现状态默认排除；如果某个通常属于表现的值会影响你的玩法，应在自定义钩子显式写入。
- Hash 是分叉检测，不是状态快照，不能恢复或回溯对象。
- 浮点按 bit 比较；`+0/-0`、NaN payload 或跨平台数学库差异会被视为不同。
- `GameplayStateSnapshot` 和每 Tick contributor 数组是显式诊断成本；未启用 Record/Verify 时不执行 Hash 捕获。
- v1 状态轨迹仍是内存模型，没有磁盘容器、压缩、版本迁移或 Checkpoint。
- 当前 Scene 自动写入暂停结果与请求数量，但不把 owner GUID 写入协议；若自定义暂停身份会影响未来分支，应由拥有该规则的玩法对象贡献相应逻辑状态。

下一阶段可把输入流与状态轨迹封装为单一回放文件，并在较长会话中加入周期 Checkpoint；时间回溯仍需要真正可恢复的状态模型，不能只依靠 Hash。

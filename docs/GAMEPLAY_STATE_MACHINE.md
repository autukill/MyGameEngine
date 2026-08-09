# Gameplay 强类型状态机

`GameplayStateMachine<TState>` 用于普通玩法对象中的有限状态切换，替代反复出现的 `enum + switch + timer + enter/exit` 样板。它是 `Engine.Core` 中的实例级辅助对象，不依赖 UI、Scene 全局管理器或渲染基础设施。

## 基本用法

```csharp
private enum EnemyState
{
    Spawning,
    Active
}

private readonly GameplayStateMachine<EnemyState> _states;

public Enemy()
{
    _states = new GameplayStateMachine<EnemyState>(EnemyState.Spawning)
        .State(
            EnemyState.Spawning,
            enter: BeginSpawning,
            step: UpdateSpawning,
            exit: EndSpawning)
        .State(
            EnemyState.Active,
            enter: BecomeActive,
            step: UpdateActive);
}

public override void OnCreate() => _states.Start();
public override void OnStep(double deltaTime) => _states.Update(deltaTime);

private void UpdateSpawning(double deltaTime)
{
    Scale = Tween.Lerp(
        new Vector2D(.25f, .25f),
        Vector2D.One,
        _states.Elapsed,
        .4d,
        EasingKind.BackOut);

    if (_states.Elapsed >= .4d)
        _states.ChangeTo(EnemyState.Active);
}
```

每个状态可选提供 `enter`、`step` 和 `exit`。没有对应行为时传空或省略即可。状态必须在 `Start()` 前全部注册，启动后配置被冻结。

## 切换语义

- `Start()` 只允许调用一次，并立即执行初始状态的 Enter。
- `Update(deltaTime)` 先累加当前状态的 `Elapsed`，再调用当前 Step。
- Step 中调用 `ChangeTo(next)` 时，当前 Step 会先完整结束，随后依次执行旧状态 Exit 和新状态 Enter。
- 新状态不会在这次 `Update` 中再执行 Step；它从下一次 `Update` 开始更新。
- `ChangeTo(CurrentState)` 是幂等 no-op，不会重置计时或重复回调。
- 需要重新进入当前状态时显式调用 `Restart()`。
- `PreviousState` 表示最近一次成功退出的状态；首次启动前后尚未切换时为 `null`。

Enter 和 Exit 中也可以请求后续切换。一次公开操作最多连续提交 32 次切换，超过后视为进入回调形成循环并快速失败。同一个回调不能请求两个不同目标；这通常意味着状态职责或条件存在歧义。

## 与 GameInstance 时间域的关系

状态机不读取全局时钟，只消费调用方传入的 `deltaTime`：

```csharp
public override void OnStep(double deltaTime) => _states.Update(deltaTime);
```

因此它自然继承所属 `GameInstance.TimeMode`：

- Gameplay 实例在暂停时不会进入 `OnStep`，状态和 `Elapsed` 同时冻结。
- Unscaled 实例继续收到真实时间并推进状态。
- `TimeScale` 已由 Scene 在调用 `OnStep` 前应用，状态机不进行第二次缩放。

状态机不会自动注册到 Scene。显式调用保留了开发者对“先更新状态还是先进行移动、输入、碰撞”的控制权。

## 分配与失败边界

状态注册、字典扩容、委托以及捕获实例的闭包发生在配置阶段。完成 `Start` 和运行时预热后，`Update`、`ChangeTo` 与 `Restart` 不产生稳态托管分配。

以下情况会快速失败：

- 初始状态未注册。
- 重复注册同一个状态。
- `Start` 后继续修改配置或再次启动。
- 启动前 Update/Change/Restart。
- 切换到未注册状态。
- 非有限或负 `deltaTime`。
- 同一回调请求冲突目标，或 Enter/Exit 形成不收敛的切换循环。

回调本身产生的分配和副作用由游戏代码负责；回调异常不会被吞掉。状态机不是事务系统，已经执行的游戏副作用不会回滚。

## 当前边界

v1 有意不提供分层状态、并行状态、Any State 条件图、行为树、异步协程、反射注册或编辑器图。复杂 AI 可以在后续独立切片中建立在这套显式状态生命周期之上，而不把常见对象的简单状态切换复杂化。

真实示例见 `playgrounds/AirplaneShooter/Target.cs`：Target 先以 Tween/Easing 完成 Spawning 状态，随后进入 Active 并启用碰撞。

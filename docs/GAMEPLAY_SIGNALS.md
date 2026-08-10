# Scene 作用域 Gameplay Signals

Gameplay Signal 用来表达“本 Tick 已经发生了一件事”，并让同一 Scene 中零个、一个或多个玩法对象作出反应。它适合击杀、拾取、波次完成、目标达成等一对多通知；不需要发布者持有接收者，也不引入全局事件总线。

## 定义与使用

Signal 是值类型，建议使用 `readonly record struct`：

```csharp
public readonly record struct AsteroidDestroyedSignal(
    Vector2D Position,
    int Score);
```

接收者实现强类型接口，并在构造函数中声明监听：

```csharp
public sealed class ScoreTracker :
    GameInstance,
    IGameplaySignalHandler<AsteroidDestroyedSignal>
{
    public int Score { get; private set; }

    public ScoreTracker() => ListenSignal<AsteroidDestroyedSignal>();

    public void OnGameplaySignal(in AsteroidDestroyedSignal signal) =>
        Score += signal.Score;
}
```

发布者无需知道有哪些接收者：

```csharp
var destroyed = new AsteroidDestroyedSignal(enemy.Position, Score: 100);
PublishSignal(in destroyed);
```

`ListenSignal<T>()` 和 `PublishSignal<T>()` 都是 `GameInstance` 的 protected API。监听声明必须发生在实例进入 Scene 之前；同一实例不能重复监听同一种 Signal。类型关联通过泛型接口完成，不扫描程序集、不使用反射，也不依赖 Service Locator。

## 确定性时序

一个逻辑 Tick 的相关顺序为：

```text
Alarm
Begin Step
Step                       <- PublishSignal
End Step
Sprite animation
Gameplay Signal dispatch  <- 按发布顺序、再按实例加入 Scene 的顺序
Spawn / Destroy commit
Scene OnAfterStep
```

- 发布只把值复制进 Scene 本地队列，不会同步递归调用接收者。
- 不同 Signal 类型也保留全局发布顺序；同一条 Signal 的接收者按订阅/实例加入 Scene 的稳定顺序执行。
- 接收者在处理过程中发布的新 Signal 延迟到下一个 Tick，避免递归、顺序歧义和不可控调用栈。
- Signal handler 可以 `Spawn` 或 `Destroy`；这些请求会进入当前 Tick 随后的安全变更提交。
- 实例销毁时自动退订。Scene Reset 会清除尚未发送的通知，避免通知泄漏到下一 Scene；persistent 实例继续保留其订阅。

## 暂停与失活

- inactive 接收者不处理 Signal。
- Scene 暂停时，默认 `InstanceTimeMode.Gameplay` 接收者不处理 Signal。
- `InstanceTimeMode.Unscaled` 接收者在暂停期间仍会处理，适合不依赖 UI 的暂停控制器或系统协调对象。

这里采用“跳过”而不是“为接收者积压”。一条 Signal 描述的是当前边界的瞬时事实；如果某项状态必须在恢复后仍可读取，应把它写入明确的 Gameplay 状态，而不是依赖历史通知。

## 失败、性能与确定性边界

Handler 抛出的异常会包装为 `GameplaySignalDispatchException`，其中包含 Signal 类型、Publisher `InstanceId` 和 Handler `InstanceId`。错误不会被静默吞掉。

每种 Signal 使用独立泛型通道保存结构体载荷；跨类型队列只保存通道和索引，因此载荷不装箱。订阅列表和队列热身后复用，正常发布/分发路径保持零托管分配，并兼容 Native AOT。

Signal 队列是瞬时调度状态，不自动写入 Gameplay State Hash、Replay Bundle 或存档。若 Signal 的结果会影响确定性玩法，接收者必须像普通玩法字段一样在 `OnWriteGameplayState` 中贡献最终状态。Asteroids 示例中的玩家分数与 Spawner 已击毁计数都遵循这一规则。

## 何时不该使用

- 一对一、需要持续身份的目标关系：使用 `InstanceRef<T>`。
- 每帧查询当前世界事实：使用 Tag、Collision、Area/Radius Query。
- Owner 内部可复用生命周期能力：使用 `GameplayBehavior<T>`。
- 必须持久化或回滚的状态：使用明确字段、状态机或后续专用系统。
- 渲染效果、UI 或跨 Scene/跨进程消息：不要借用 Gameplay Signal；使用各自的显式边界。

当前真实样例位于 `playgrounds/Asteroids`：Laser 在陨石首次耗尽时发布一条 `AsteroidDestroyedSignal`，PlayerShip 计分，AsteroidSpawner 同时统计击毁数量。发布者与两个消费者彼此不持有引用。

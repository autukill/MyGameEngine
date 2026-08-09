# Gameplay 暂停、时间缩放与回溯方向

本文说明已经实现的不依赖 UI 的 Gameplay 时间策略，并评估“时间回溯”作为未来游戏机制的合理边界。暂停菜单、按钮和遮罩只是可能的调用方，不属于本切片；即使游戏没有任何 UI，也能通过输入、剧情、失焦或调试控制器暂停和恢复模拟。

## 设计结论

- **暂停不是停止窗口循环**：输入采样、渲染、resize、资源热重载和低频诊断仍然运行。
- **暂停不是向所有对象传 `deltaTime = 0`**：默认 Gameplay 实例应完全跳过 Step，避免仍然生成对象、消费输入或触发零时间副作用。
- **时间缩放与暂停分离**：`TimeScale` 表达慢动作/加速，Pause owner 表达一个或多个系统要求冻结 Gameplay；不能用“最后一次写入 bool”管理多个暂停来源。
- **少量控制器使用 Unscaled 时间**：负责解除暂停、镜头表现或调试的实例继续 Step；普通玩法对象默认使用 Gameplay 时间。
- **Draw 默认继续**：被冻结的世界保持可见，渲染效果和 Presentation 继续执行，但由 Gameplay owner 驱动的参数不会自行变化。
- **时间回溯是状态恢复，不是负时间缩放**：它需要历史快照、结构变更日志和副作用隔离，应作为独立、可选机制推进。

## 公共模型

```csharp
public readonly record struct GameplayPauseKey(string Name);

public enum InstanceTimeMode
{
    Gameplay, // 默认；受暂停和 TimeScale 影响
    Unscaled  // 始终使用真实 update delta
}

public readonly record struct GameplayTimeSnapshot(
    double UnscaledDeltaTime,
    double DeltaTime,
    double TimeScale,
    bool IsPaused);
```

组合根与实例分别使用同一状态源：

```csharp
context.Time.TimeScale = 0.25; // 慢动作，不改变 UPS 调度频率
context.Time.Pause(PauseKeys.WindowFocus);
context.Time.Resume(PauseKeys.WindowFocus);

public sealed class PauseController : GameInstance
{
    private static readonly GameplayPauseKey PlayerPause = new("player.pause");

    public PauseController() => TimeMode = InstanceTimeMode.Unscaled;

    public override void OnStep(double deltaTime)
    {
        if (KeyPressed(InputKey.P))
            ToggleGameplayPause(PlayerPause);
    }
}
```

普通 Gameplay 开发者不需要接触 Window 或 Hosting。`PauseGameplay/ResumeGameplay/ToggleGameplayPause` 应通过实例级 Context 携带 owner ID；同一 owner/key 的重复请求幂等，多个 owner 可以同时持有暂停，只有最后一个 owner 释放后才恢复。

`TimeScale` 要求有限且位于 `(0, 8]`；零值不作为暂停的别名。第一版只提供一个明确的 Scene 时间缩放值，不引入多个 slow-motion modifier 的优先级或乘法栈。`context.Time.Current` 提供最近一次 update 的只读 `GameplayTimeSnapshot`。

## 每帧调度语义

Hosting 仍按真实 update delta 驱动 Scene，但 Scene 根据时间模式选择实例：

```text
采样 Input                      始终执行
计算 unscaled/scaled delta      paused 时 scaled delta = 0
分发输入边沿                    运行中：全部；暂停：仅 Unscaled
推进 Alarm                     各实例使用自己的时间域
Begin/Step/End Step             Gameplay 暂停时完全跳过；Unscaled 继续
推进 Sprite 动画               使用实例时间域
提交 Spawn/Destroy              始终在安全帧边界执行
同步 RenderEffect/Scene 请求    始终执行
Content/Shader Hot Reload       使用真实时间，继续执行
Draw/DrawGUI/Presentation       始终执行
```

具体规则：

- Gameplay 实例暂停时不接收 `OnBeginStep/OnStep/OnEndStep`，Alarm 与 Sprite 动画也不推进。
- Unscaled 实例继续收到真实 `deltaTime`，可解除暂停或请求 Scene 切换；它生成/销毁的实例仍在当前安全边界提交。
- Scene 的 `OnBeforeStep/OnAfterStep` 视为 Gameplay hook，暂停时不调用。后续如确有 Scene 级常驻更新需求，应增加名字明确的 unscaled hook，不复用现有 hook。
- 输入系统仍更新当前 Down 状态，但暂停期间只有 Unscaled 实例消费 Pressed/Released 边沿；恢复后不会补发暂停期间已经错过的边沿，避免意外射击或跳跃。
- inactive 实例仍优先于时间模式，不 Step、不 Draw、不推进 Alarm/动画。
- 帧统计中的 UPS 表示真实 update 调度率；时间快照额外暴露 `IsPaused/TimeScale`，不伪造一个“暂停后 UPS = 0”的指标。
- Pause/Resume 在调用时更新控制器状态，但当前已经开始的 Scene update 使用帧首快照完成；调度变化从下一次 update 生效，避免遍历顺序改变同帧结果。

## Pause owner 生命周期

仅用全局 `bool IsPaused` 会出现“窗口失焦和剧情同时暂停，其中一个恢复却误解锁另一个”的问题。建议存储 `(owner, PauseKey)` 集合：

- 实例请求使用 `InstanceId` 作为 owner；同 owner/key 重复请求不增加计数。
- 组合根、窗口失焦或调试器使用稳定的外部 owner key。
- 实例销毁或失活时自动释放其 Scene-scoped pause 请求，防止永久死锁。
- Scene 切换清空全部 Scene-scoped owner，即使 owner 是 persistent；窗口失焦等 Host-scoped owner 保留。
- Scene 切换把单一 `TimeScale` 重置为 `1`，避免慢动作意外泄漏到新 Scene；需要跨 Scene 的外部系统应在新 Scene 配置后显式重设。
- 恢复一个不存在的 owner/key 是安全 no-op，便于 `OnDestroy` 做防御性清理。

当前实现不返回必须手工 `Dispose` 的 Pause lease。Gameplay 实例可能在暂停期间不再获得 Step，遗漏 Dispose 会让 Scene 永久冻结；owner/key 与生命周期自动清理更安全。

## 与现有系统的边界

- `FrameRateSettings` 控制实际 FPS/UPS 调度，Gameplay `TimeScale` 只缩放模拟 delta，两者不能互相替代。
- `Motion.Damp` 等辅助直接使用传入 delta；Gameplay 实例自然获得 scaled delta，Unscaled 控制器获得真实 delta。
- RenderPipeline、Bloom、Stencil 和 Presentation 不读取 Gameplay 时间；它们继续绘制当前已提交状态。
- Content/Shader 热重载、GPU 资源回收和诊断不能因 Gameplay 暂停而停止。
- Scene 切换仍是安全帧边界操作。暂停状态下只有 Unscaled 或外部调用方能够发起它。
- 暂停不创建 Scene Stack，也不等同于打开菜单 Scene；两者可在未来组合，但所有权不同。

## 时间回溯是否合理

合理，尤其适合解谜、动作纠错、赛车幽灵、死亡回退或局部时间能力；但它不是所有游戏都需要的基础开发体验。若目标 Playground 没有真实玩法需求，过早把通用对象序列化塞进 Core 会显著增加复杂度。

绝不能通过 `TimeScale = -1` 或向 `OnStep` 传负 delta 实现回溯。普通 Step 包含 Spawn、Destroy、随机数、碰撞、声音和外部副作用，这些操作通常不可逆。推荐的原型路线是**显式、可选的固定 Tick 快照环形缓冲区**：

```csharp
public interface IRewindable<TState> where TState : struct
{
    TState CaptureRewindState();
    void RestoreRewindState(in TState state);
}
```

运行策略：

1. 正常固定逻辑 Tick 后，为 opt-in 实例捕获纯值状态。
2. 同时记录 Spawn/Destroy、Prefab 参数和稳定 Rewind ID，不能只保存当前存活对象。
3. 回溯时停止正常 Gameplay Step，Unscaled 回溯控制器继续读取输入。
4. 每个 Tick 恢复上一快照并正常 Draw，不反向调用业务代码。
5. 松开回溯后从恢复点继续，v1 丢弃“未来分支”。

至少需要纳入快照或明确重建的状态：Transform、速度、Alarm 剩余时间、Sprite `ImageIndex`、Gameplay 自定义状态、随机数生成器状态，以及实例的出现/消失。GPU Texture、Shader、RenderTarget 和 Atlas 不进入快照；渲染效果应由恢复后的逻辑 owner 重新派生。

### 回溯的难点与限制

- **副作用**：音频、成就、存档、网络发送和遥测不能随着快照重复执行。必须区分可逆模拟事件与只提交一次的外部事件。
- **标识**：当前 `InstanceId` 代表一次运行时实体；回溯 Spawn/Destroy 需要额外稳定 `RewindId` 或结构日志。
- **内存**：`每实体状态字节 × 实体数 × Tick 数`。例如 1,000 个实体、每个 64 B、60 Hz、10 秒约 36.6 MiB，尚未包含结构日志。
- **Scene 边界**：v1 应限制在当前 Scene 内，遇到 Scene 切换快照即停止；跨 Scene 回溯需要连同 Content 租约和 Scene activation 参数一起管理。
- **确定性**：快照恢复比“记录输入后重新模拟”更容易先落地。Checkpoint + 输入重演内存更低，但要求随机数、浮点、迭代顺序和所有系统严格确定，适合作为后续优化。

## 推荐推进顺序

1. Gameplay/Unscaled 两个时间域、owner/key 暂停和单一 `TimeScale` 已经实现。
2. Asteroids 已增加无 UI 的 `P` 键暂停控制器，Core 测试覆盖世界冻结、输入不穿透、Draw 继续、owner 清理及 Scene scope。
3. 下一步收敛 Scene 生命周期遍历分配，不改变已提交的时间域帧语义。
4. 只有出现以回溯为核心的 Playground 后，先在 Playground 做 3–5 秒、少量实例的显式快照实验。
5. 实验确认状态契约和内存预算后，再提炼可选 Rewind Feature；不让普通 `GameInstance` 为未启用回溯承担每帧序列化成本。

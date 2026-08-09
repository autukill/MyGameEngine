# Gameplay Cooldown

`GameplayCooldown` 是一个预创建、实例局部、零稳态分配的冷却计时器，适合射击、冲刺、技能和受击无敌间隔。它只负责“何时可以再次使用”，不读取输入、不执行回调，也不依赖全局管理器。

## 基本用法

```csharp
private readonly GameplayCooldown _fire = new(0.12d);

public override void OnStep(double deltaTime)
{
    _fire.Update(deltaTime);
    if (ActionDown(GameInputs.Fire) && _fire.TryUse())
        Spawn(BulletPrefab, Position);
}
```

实例刚创建时冷却为可用状态。`TryUse()` 只在 `IsReady` 时返回 `true` 并开始完整冷却；冷却期间再次调用返回 `false`，且不会重置剩余时间。

## 状态与控制

- `DurationSeconds`：构造时确定的固定时长，必须是有限非负数。
- `RemainingSeconds`：当前剩余时间，始终保持在 `0..DurationSeconds`。
- `IsReady`：是否可以使用。
- `Progress`：恢复进度；刚使用为 `0`，完全恢复为 `1`。
- `Update(deltaTime)`：按传入时间推进，拒绝负数和非有限值。
- `Restart()`：无条件恢复到完整冷却，适合外部规则强制延后使用。
- `Reset()`：立即变为可用，适合重生、换关或调试重置。

时长为 `0` 明确表示“不施加冷却”：`Progress` 恒为 `1`，每次 `TryUse()` 都成功。这个语义便于通过配置关闭冷却，但不会限制同一 Step 内的重复调用；调用方仍应保持每个动作路径只尝试一次。

## 与输入缓冲组合

冷却判断和输入意图是两件事。需要保留冷却结束前的短按时，把 `InputActionBuffer` 与冷却组合：

```csharp
private readonly InputActionBuffer _fireBuffer = new(GameInputs.Fire, 0.12d);
private readonly GameplayCooldown _fire = new(0.14d);

public override void OnStep(double deltaTime)
{
    UpdateActionBuffer(_fireBuffer, deltaTime);
    _fire.Update(deltaTime);

    if ((ActionDown(GameInputs.Fire) || _fireBuffer.IsBuffered) && _fire.TryUse())
    {
        Spawn(LaserPrefab, CreateLaserSpawn());
        _fireBuffer.TryConsume();
    }
}
```

持续按住由 `ActionDown` 驱动，短按意图由 Buffer 保留，是否真正执行由 `TryUse` 原子决定。这样输入系统不需要知道射速规则，冷却也不需要知道物理键位。

## 时间域与暂停

`GameplayCooldown` 不自行读取 Scene 时钟。把 `OnStep` 收到的 `deltaTime` 传给 `Update` 后，它自然继承 Owner 的时间语义：

- 普通 Gameplay 实例在暂停时不会执行 Step，因此冷却冻结。
- `TimeMode = InstanceTimeMode.Unscaled` 的控制器继续按真实时间推进。
- inactive 实例不执行 Step，因此冷却保持不变。
- Scene 时间缩放会通过 `deltaTime` 同比例影响冷却。

如果某个冷却必须无视 Owner 的时间域，应把它放在明确的 Unscaled Owner 中更新，而不是让计时器访问全局时钟。

## 当前边界

第一版刻意不包含动态修改时长、charges、分组共享冷却、回调、自动输入消费或序列化。技能与 Buff 系统可以在未来组合这个原语，但不应反向把技能规则塞进 `GameplayCooldown`。

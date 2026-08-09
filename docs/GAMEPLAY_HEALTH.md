# Gameplay Health 与 Damage

`GameplayHealth` 是一个实例局部、零稳态分配的数值容器，用于生命、护盾耐久、可破坏物体或需要“耗尽”语义的玩法状态。它负责合法值、上下界钳制和状态转换结果，不负责护甲、阵营、伤害来源、击杀奖励、动画、音效或 UI。

## 基本用法

```csharp
public sealed class Enemy : GameInstance, IHasGameplayHealth
{
    public GameplayHealth Health { get; } = new(3f);
}
```

默认构造从满生命开始；也可以显式指定初始值：

```csharp
var dormantShield = new GameplayHealth(maximumHealth: 100f, initialHealth: 0f);
```

最大生命必须是有限正数，初始生命必须位于 `0..MaximumHealth`。

## 造成伤害和处理耗尽

```csharp
if (FirstCollision(GameTags.Enemy) is not { } enemy)
    return;

DestroySelf();
if (enemy is IHasGameplayHealth damageable &&
    damageable.Health.ApplyDamage(1f).BecameDepleted)
{
    Destroy(enemy);
    AwardScore();
}
```

`GameplayTag` 表达横切身份，`IHasGameplayHealth` 表达可调用能力。两者组合后，Projectile 不依赖某个具体 Enemy 类，同时也不会假设每个带敌人标签的实例一定有生命值。

`ApplyDamage` 接收有限非负数并在零处钳制。返回的 `GameplayHealthChange` 是值类型快照：

- `PreviousHealth`、`CurrentHealth`、`MaximumHealth`：本次变化前后状态。
- `Delta`：当前值减去旧值；伤害为负，治疗为正。
- `AppliedAmount`：钳制后实际生效的绝对值。
- `Changed`、`IsDamage`、`IsHealing`：变化分类。
- `BecameDepleted`：仅在本次从存活变为耗尽时为 `true`。
- `BecameAlive`：仅在本次从耗尽恢复为存活时为 `true`。
- `ReachedFull`：仅在本次恢复到满值时为 `true`。

因此多个 Projectile 在同一 Step 命中同一对象时，只有造成首次耗尽的调用会得到 `BecameDepleted = true`。重复 Destroy 本身虽然安全，但计分、掉落等非幂等副作用应绑定这个转换结果。

## 治疗、复活和重置

```csharp
GameplayHealthChange healed = Health.Heal(5f);
if (healed.BecameAlive)
    ResumeGameplay();

Health.Reset(); // 恢复到 MaximumHealth
```

`Heal` 在最大值处钳制；第一版允许从零治疗，因此复活是显式、可观察的普通变化。若游戏不允许复活，应由调用方在 `Health.IsDepleted` 时拒绝治疗，而不是让基础容器猜测游戏规则。

## 读取状态

- `CurrentHealth` / `MaximumHealth`
- `Normalized`：稳定的 `0..1` 比例，可供玩法逻辑或未来表现层读取。
- `IsAlive` / `IsDepleted` / `IsFull`

Health 不依赖时间，因此不需要 `Update`，也不受暂停影响。受击无敌、持续伤害间隔等时间规则可以在 Owner 中组合 `GameplayCooldown`；不要把计时职责塞入 Health。

## 当前边界

第一版不包含动态最大值、伤害类型、抗性、护甲、暴击、来源 Owner、团队关系、事件总线、自动销毁或回调。后续 Skill/Buff 切片可以在进入 Health 前计算最终伤害，但 `GameplayHealth` 继续只接收已经决定的非负数值。这让基础 API 既适合简单动作游戏，也不会预设 RPG 战斗模型。

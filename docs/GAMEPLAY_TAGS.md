# Gameplay Tags

Gameplay Tag 表达跨继承树的玩法身份，例如敌人、可受伤对象、拾取物或玩家子弹。它适合回答“这个实例在玩法中扮演什么角色”，而不是“这个实例由哪个具体类实现”。

## 定义与附加

把稳定 Tag 集中定义，不要在玩法代码中散落字符串：

```csharp
public static class GameTags
{
    public static readonly GameplayTag Enemy = new("actor.enemy");
    public static readonly GameplayTag Damageable = new("combat.damageable");
    public static readonly GameplayTag Invulnerable = new("combat.invulnerable");
    public static readonly GameplayTag PlayerProjectile = new("projectile.player");
}
```

实例通常在构造函数中声明固有身份，也可以在状态变化时动态增删：

```csharp
public Enemy(...)
{
    AddTag(GameTags.Enemy);
    AddTag(GameTags.Damageable);
}

AddTag(GameTags.Invulnerable);
RemoveTag(GameTags.Invulnerable);
bool canTakeDamage = HasTag(GameTags.Damageable) &&
                     !HasTag(GameTags.Invulnerable);
```

Tag 名称大小写敏感，空名称和 `default(GameplayTag)` 会被拒绝。`AddTag`、`RemoveTag` 返回成员关系是否实际变化，重复添加或移除是安全 no-op。`ClearTags` 用于明确重置全部身份；引擎不公开内部可变集合。

标签容器延迟到首次 `AddTag` 才创建，完全不使用 Tag 的实例没有额外集合成本。Tag 修改立即生效：同一 Step 中稍后执行的查询会看到新成员关系。Spawn/Destroy 是否可见仍遵循原有帧边界。

## 类型与 Tag 组合查询

所有查询仍保留泛型类型约束，并增加一个必需 Tag：

```csharp
Enemy? target = FindFirst<Enemy>(GameTags.Damageable);
int count = CountInstances<Enemy>(GameTags.Enemy);

private readonly GameplayQueryBuffer<Enemy> nearby = new(32);
QueryRadius(Position, 160f, GameTags.Damageable, nearby);
```

只关心 Tag、不关心具体类型时，使用不带泛型的实例便利 API：

```csharp
if (FirstCollision(GameTags.Enemy) is { } enemy)
    Destroy(enemy);
```

`FindFirst/FindAll/CountInstances` 保持既有 Find 语义，会查询所有已提交实例，包括 inactive 实例。`FirstCollision/Collisions/QueryArea/QueryRadius` 保持空间查询语义，只考虑 active 且具有 Collider 的实例；自身碰撞查询仍排除自身。

数组便利重载适合低频逻辑；高频路径使用 `GameplayQueryBuffer<T>`，查询前自动清空内容并保留容量。Tag 过滤继续计入原有 Find、Collision、Area、Radius 遥测，预热后不会产生托管分配。

## 选择 Tag、类型还是其他机制

- 用类型表达实现能力和强类型 API，例如 `Boss`、`Pickup`。
- 用 Tag 表达横切身份，例如 `Enemy`、`Damageable`、`Flying`。
- 用 Collider 表达空间形状；Tag 不参与几何计算。
- 用 Scene Layer 表达绘制组织；Tag 不改变渲染顺序或可见性。
- 用状态机表达互斥阶段；临时 Tag 只应在其他对象确实需要查询该状态时使用。

v1 每次查询支持一个必需 Tag，可再与具体类型组合。暂不加入多 Tag 布尔表达式、层级 Tag、自动索引、编辑器配置或网络复制。当前查询沿用已有线性扫描；只有真实负载数据证明 Tag 查询成为瓶颈后，才增加按 Tag 索引并承担运行时变更同步成本。

可运行迁移见 [Asteroids Playground](../playgrounds/Asteroids/README.md)。

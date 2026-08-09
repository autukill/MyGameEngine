# 强类型 Instance 引用

`InstanceRef<T>` 是 Scene 内实例的弱、强类型句柄。它只保存 `InstanceId`，不会持有实例对象或延长生命周期，适合追踪目标、投射物 Owner、召唤者、门与开关等跨 Step 关系。

## 创建与解析

```csharp
InstanceRef<PlayerShip> target = player.ToInstanceRef();
```

在 `GameInstance` 子类中解析：

```csharp
PlayerShip? player = Resolve(target);
if (player is not null)
    MoveToward(player.Position);
```

组合根和高级工具可通过 `SceneAggregate.Resolve(target)` 使用同一语义。解析直接复用 Scene 的 Instance 字典，平均 O(1)，创建与解析均不分配。

使用引用比长期保存 `GameInstance` 对象更明确：目标销毁或离开 Scene 后，`Resolve` 返回 `null`，调用方不会继续操作一个已脱离 Scene Context 的旧对象。

## 安全销毁

```csharp
Destroy(target);
```

实例内部的 `Destroy(InstanceRef<T>)` 与现有 Gameplay 销毁请求一致，在 End Step 后提交。公开 `SceneAggregate.Destroy(reference)` 是组合根的立即操作。两条路径都会先验证引用当前确实解析为 `T`；即使有人用同一个 ID 构造了错误泛型类型，也不能误删目标。

## 帧边界语义

- `Spawn` 返回的实例可以立即转换为 Ref，但在 End Step 提交前 `Resolve` 返回 `null`。
- `Destroy(ref)` 请求发出后，当前 Step 内仍能解析；提交后返回 `null`。
- inactive 实例仍属于 Scene，因此可以解析。
- Scene 切换后，非 persistent 实例的 Ref 失效；persistent 实例继续解析。
- 后续创建的同类型实例拥有新的 Version 7 `InstanceId`，旧 Ref 不会错误指向替代对象。
- `default(InstanceRef<T>)` 与 `InstanceRef<T>.Empty` 都安全解析为 `null`。

这些规则与现有 Find/Spawn/Destroy 可见性完全一致，没有额外的“引用专属生命周期”。

## Asteroids 示例

`AsteroidSpawner` 构造时接收玩家引用：

```csharp
var player = new PlayerShip(...);
scene.Add(player);
scene.Add(new AsteroidSpawner(
    player.ToInstanceRef(),
    worldWidth,
    worldHeight));
```

每次生成时再解析位置：

```csharp
Vector2D targetPosition = Resolve(_target)?.Position ?? worldCenter;
```

Spawner 不需要每次线性搜索玩家，也不保留可能已经销毁的对象；目标失效策略仍由游戏明确决定，这里选择回退到世界中心。

## 当前边界

Ref 只在当前运行时的 Scene 身份空间内有意义，不是 Content 引用、存档 ID 或网络 Entity ID。第一版不自动跨进程恢复、不在目标销毁时回调、不提供所有权或级联销毁，也不隐藏 `null` 分支。未来 Damage Source、Skill/Buff 来源可以组合 Ref，但不应把业务关系塞回基础句柄。

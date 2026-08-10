# Asteroids Playground

第二个 Gameplay Authoring 样例，用来验证不同于飞机直线射击的旋转推进玩法。

## 操作

- 方向键左右或 `A/D`：旋转。
- 方向键上或 `W`：推进。
- `Space`：射击。
- `P`：暂停/恢复 Gameplay；窗口循环和 Draw 继续运行。
- `Enter`：在 Game Over 后重新开始。

## 展示的引擎能力

- `GameInputs` 与 `ConfigureInput` 集中装配逻辑输入，不让玩法类依赖具体键位。
- 射击组合 `InputActionBuffer` 与 `GameplayCooldown`，兼顾短按预输入和按住连续发射。
- `GameplayTag` 表达 Player、Enemy、Damageable 和 PlayerProjectile；Laser 依赖 Enemy 身份和 `IHasGameplayHealth` 能力，不依赖具体 Asteroid 类。
- Laser 使用内置 `LifetimeBehavior`，Asteroid 使用项目自定义强类型 `SpinBehavior`。
- `PrefabRef<T, TArgs>` 传递 Laser/Asteroid 的位置、速度和半径，不使用无类型参数字典。
- `AsteroidSpawner` 保存弱、强类型 `InstanceRef<PlayerShip>`；玩家消失后安全回退到世界中心。
- 固定 60 Tick 与 owner-local `GameplayRandom` 让生成边缘、速度和半径可复现。
- PlayerShip、Spawner、Asteroid、Laser 和 SpinBehavior 显式贡献状态 Hash，可直接配合 Replay 分叉诊断。
- Box/Circle Collider、Tag 与 Capability 组合完成射击和受击。
- Ship 命中后通过 `SceneRef<GameOverArgs>` 把生存时间、射击次数和分数传给 Game Over Scene。

## 一对多 Gameplay Signal

Laser 在 Asteroid 生命首次耗尽时只发布一条 `AsteroidDestroyedSignal`：

```csharp
var destroyed = new AsteroidDestroyedSignal(enemy.Position, Score: 100);
PublishSignal(in destroyed);
```

PlayerShip 监听它并累加分数，AsteroidSpawner 同时监听并统计已击毁数量。Laser 不持有这两个消费者；新增音效、波次或成就系统时也无需修改发布者。通知在 End Step 后确定性分发，两个消费者都把影响未来玩法的结果写入 `OnWriteGameplayState`。完整语义见 [`docs/GAMEPLAY_SIGNALS.md`](../../docs/GAMEPLAY_SIGNALS.md)。

## 运行

```powershell
dotnet run --project playgrounds/Asteroids/Asteroids.csproj
```

输出真实 Find/Collision/Area/Radius 查询统计：

```powershell
dotnet run --project playgrounds/Asteroids/Asteroids.csproj -- --diagnostics
```

自动经历 pause → resume，并跨越 Main → GameOver 的隐藏窗口冒烟：

```powershell
dotnet run --project playgrounds/Asteroids/Asteroids.csproj -- --smoke
```

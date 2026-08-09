# Asteroids Playground

第二个 Gameplay Authoring 样例，用来验证 API 能否支持不同于飞机直线射击的玩法：

- 方向键或 `A/D` 旋转，方向键上或 `W` 推进。
- `GameInputs` 与 `ConfigureInput` 集中装配旋转、推进、射击、暂停和重开，不让玩法类依赖具体键位。
- 射击使用 `InputActionBuffer` 保留冷却结束前的短按，同时继续支持按住连续发射。
- `GameTags` 集中定义 Player、Enemy、Damageable 和 PlayerProjectile；Laser 与 PlayerShip 按 Enemy 标签碰撞，不依赖具体 `Asteroid` 类。
- Laser 使用内置 `LifetimeBehavior`，Asteroid 使用项目自定义的强类型 `SpinBehavior`，无需复制生命周期或旋转代码。
- 按住空格连续发射带方向和速度参数的 Laser。
- Alarm 周期生成带半径、位置和速度参数的 Asteroid。
- Circle Collider 驱动 Laser/Asteroid 与 Ship/Asteroid 碰撞。
- Ship 命中后通过 `SceneRef<GameOverArgs>` 把生存时长和发射次数交给 GameOver Scene，按 `Enter` 无参切回 Main。
- `P` 键通过 Unscaled `PauseController` 冻结/恢复 Gameplay；窗口循环和 Draw 保持运行，不依赖暂停菜单。
- 所有运行时生成和 Scene 参数都使用编译期关联的泛型引用，不使用无类型参数字典或全局过渡状态。

运行：

```powershell
dotnet run --project playgrounds/Asteroids/Asteroids.csproj
```

每秒输出真实 Find/Collision/Area/Radius 查询次数、候选、命中和平均每 Step 耗时：

```powershell
dotnet run --project playgrounds/Asteroids/Asteroids.csproj -- --diagnostics
```

自动经历 Gameplay pause → resume，并跨越 Main → GameOver 的隐藏窗口冒烟：

```powershell
dotnet run --project playgrounds/Asteroids/Asteroids.csproj -- --smoke
```

# Airplane Shooter Playground

一个刻意保持简单的 MyGameEngine 可运行示例：

- 方向键或 `WASD` 移动飞机。
- `GameInputs` 与 `ConfigureInput` 把移动、射击和重开意图从具体键位中分离。
- 按住空格通过内置 `GameplayCooldown` 连续发射子弹，无需手写递减计时字段。
- 使用 `WithFixedUpdateRate(60)` 绑定更新频率和逻辑 delta，移动、冷却与动画运行在稳定 Tick 上。
- PlayerPlane 与 Target 显式贡献 Cooldown、Health 和状态机状态，可直接接入 Gameplay 状态 Hash 与首次分叉诊断。
- 飞机被限制在窗口范围内。
- 子弹通过类型安全的 `PrefabRef<PlayerBullet>` 创建，并通过 `LifetimeBehavior` 自动销毁，展示可复用的 Owner 局部生命周期组合。
- 子弹与目标使用轻量 Box Collider；命中后在安全帧边界切换到 Victory Scene。
- Target 持有三点 `GameplayHealth`；每颗子弹造成一点伤害，只在 `BecameDepleted` 时销毁目标并切换 Victory，避免重复死亡副作用。
- Victory Scene 中按 `Enter` 返回 Main Scene。
- Sprite 使用声明式 `assets.json` 和构建时生成的强类型 `GameAssets` 引用。

在仓库根目录运行：

```powershell
dotnet run --project playgrounds/AirplaneShooter/AirplaneShooter.csproj
```

无窗口冒烟验证：

```powershell
dotnet run --project playgrounds/AirplaneShooter/AirplaneShooter.csproj -- --smoke
```

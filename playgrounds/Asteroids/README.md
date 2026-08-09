# Asteroids Playground

第二个 Gameplay Authoring 样例，用来验证 API 能否支持不同于飞机直线射击的玩法：

- 方向键或 `A/D` 旋转，方向键上或 `W` 推进。
- 按住空格连续发射带方向和速度参数的 Laser。
- Alarm 周期生成带半径、位置和速度参数的 Asteroid。
- Circle Collider 驱动 Laser/Asteroid 与 Ship/Asteroid 碰撞。
- Ship 命中后安全切换到 GameOver Scene，按 `Enter` 重新开始。
- 所有运行时生成都使用 `PrefabRef<T, TArgs>`，不使用无类型参数字典。

运行：

```powershell
dotnet run --project playgrounds/Asteroids/Asteroids.csproj
```

自动跨越 Main → GameOver 的隐藏窗口冒烟：

```powershell
dotnet run --project playgrounds/Asteroids/Asteroids.csproj -- --smoke
```

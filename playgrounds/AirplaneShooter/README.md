# Airplane Shooter Playground

一个刻意保持简单的 MyGameEngine 可运行示例：

- 方向键或 `WASD` 移动飞机。
- 按住空格连续发射子弹。
- 飞机被限制在窗口范围内。
- 子弹通过实例 Alarm 自动销毁。
- Sprite 使用声明式 `assets.json` 和构建时生成的强类型 `GameAssets` 引用。

在仓库根目录运行：

```powershell
dotnet run --project playgrounds/AirplaneShooter/AirplaneShooter.csproj
```

无窗口冒烟验证：

```powershell
dotnet run --project playgrounds/AirplaneShooter/AirplaneShooter.csproj -- --smoke
```

# Flappy Bird Playground

一个只使用单张白色 WebP、Sprite 缩放和颜色组合的完整小游戏，展示游戏开发者如何用现有引擎 API 快速拼出可玩的闭环。

```powershell
dotnet run --project playgrounds/FlappyBird/FlappyBird.csproj
```

- `Space` / `W` / `↑`：开始游戏或拍动翅膀。
- `Enter` / `Space`：Game Over 后重新开始。
- `Esc`：退出。
- `--smoke`：隐藏窗口，自动切换到 Game Over 并关闭。

示例覆盖逻辑 Input Action、参数化 Prefab、确定性 `SpawnSequence`、碰撞与计分触发器、类型化 Scene 参数、强类型 Content、程序化短音效和无窗口 Smoke。管道、鸟、数字与背景都由 `flappy.shape` 这个逻辑 Sprite 组合完成，因此没有外部美术授权依赖。

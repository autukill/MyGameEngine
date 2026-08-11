# BubbleTa Tests

无窗口测试按被测项目建立独立控制台工程，并放入解决方案的 `Games/BubbleTa/Tests` 虚拟目录。

计划中的首批测试项目：

- `BubbleTa.Game.Tests`（已建立，覆盖 HomeScene 动画状态、确定性装饰和入口交互）
- `BubbleTa.BubbleGrid.Tests`
- `BubbleTa.LevelFormat.Tests`
- `BubbleTa.Simulation.Tests`

泡泡网格、关卡格式与 Simulation 仍没有玩法公共 API，因此暂不创建只验证占位符的空测试项目。第一个模块行为与对应测试必须在同一功能切片落地。

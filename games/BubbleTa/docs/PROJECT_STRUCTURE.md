# BubbleTa 项目结构与模块约定

## 目标

本目录同时容纳完整游戏产品和由真实玩法需求驱动的单功能模块。模块可以独立构建、测试和演化，但默认仍属于 `BubbleTa`，不会因为“看起来可能复用”就进入通用引擎。

## 物理目录与解决方案虚拟目录

| 物理目录 | 解决方案虚拟目录 | 用途 |
|---|---|---|
| `src/BubbleTa.Game` | `Games/BubbleTa/Game` | Hosting、场景、输入、表现和发布入口 |
| `src/BubbleTa.Simulation` | `Games/BubbleTa/Simulation` | 完整确定性玩法流程 |
| `modules/*` | `Games/BubbleTa/Modules/*` | 可独立验证的单一职责游戏模块 |
| `tools/*` | `Games/BubbleTa/Tools` | 只在构建或迁移阶段运行的离线工具 |
| `tests/*` | `Games/BubbleTa/Tests` | 无窗口测试；名称与被测项目对应 |

解决方案目录只是 IDE 中的逻辑分组，不代替磁盘目录，也不改变项目引用。

## 依赖方向

```text
BubbleTa.BubbleGrid ─────┐
                        ├─> BubbleTa.Simulation ─> BubbleTa.Game ─> MyGameEngine Hosting
BubbleTa.LevelFormat ────┘
          ▲
          └──────── BubbleTa.LegacyImporter
```

固定规则：

1. `BubbleGrid` 和 `LevelFormat` 不引用 MyGameEngine、Game 项目或旧 GameMaker 文件。
2. `Simulation` 可以引用游戏局部模块，但不引用 GPU、Window、Input 设备或资产句柄。
3. `Game` 是组合根，负责把 MyGameEngine 的输入、渲染、音频和内容服务接到 Simulation。
4. `LegacyImporter` 只在离线阶段读取旧数据，输出的新格式必须能在没有旧工程的环境中使用。
5. 项目测试引用被测项目；正式项目不得反向引用 Tests 或 Tools。

## 单功能模块准入规则

只有同时满足以下条件时才新增 `modules/<Name>` 项目：

- 有一个清晰且可以单独描述的职责。
- 有独立数据契约和失败语义。
- 可以无窗口验证。
- 拆分能够阻止不合理依赖，或确实存在独立演化价值。

不要把每个类、每种泡泡或每个 Gameplay Behavior 拆成项目。首批两个模块的边界是：

- `BubbleTa.BubbleGrid`：坐标、六邻接拓扑、占用和吸附候选；不负责动画、计分或关卡 IO。
- `BubbleTa.LevelFormat`：新关卡 DTO、Schema 版本和静态验证；不负责旧数据解析或运行时玩法。

## 提升为引擎 Feature 的门槛

游戏局部模块只有在出现第二个真实消费者后才评估提升。评估时要求：

- 公共语义不再包含 BubbleTa 名称或规则假设。
- 第二个消费者不是为证明复用而制造的测试项目。
- 生命周期、所有权、性能和错误契约已经由真实使用稳定下来。
- 移动后不会迫使引擎依赖游戏项目。

在此之前，模块即使设计良好，也继续保留在 `games/BubbleTa/modules`。

## 旧项目边界

旧项目当前位于 MyGameEngine 仓库之外。Gate 0 只把它视为只读参考：

- 不把旧 GML、GameMaker Object、第三方 UI 源码或旧 SDK 复制进本仓库。
- `LegacyImporter` 未来通过显式命令行路径读取用户提供的旧关卡文件。
- 转换产物使用新的版本化格式，并记录源关卡 ID 与转换器版本。
- 图片和音频只有完成来源与再发布权限审计后，才进入正式 Content Package。

## 后续项目落点

预计按实际需要渐进增加，而不是一次性创建空工程：

- `tests/BubbleTa.BubbleGrid.Tests`
- `tests/BubbleTa.LevelFormat.Tests`
- `tests/BubbleTa.Simulation.Tests`
- 可选的 `modules/BubbleTa.ShotTrajectory`
- 可选的 `modules/BubbleTa.LevelRules`

是否拆出 `ShotTrajectory` 或 `LevelRules`，由首个确定性玩法切片中的复杂度决定。

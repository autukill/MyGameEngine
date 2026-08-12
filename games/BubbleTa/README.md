# 《天天泡泡TA / BubbleTa》

> 状态：`BubbleTa.HomeScene` 与 WorldMap 底部第一岛屿已可运行；核心泡泡玩法尚未开始，仍处于 Gate 0 行为重建阶段。

`BubbleTa` 是对 2015 年 GameMaker Studio 1 泡泡龙项目的现代重建候选。旧源码与 HTML5 构建仅作为行为参考、关卡来源和资产审计输入，不会直接复制旧运行时、第三方 UI 框架、支付 SDK 或商业化代码。

第一项产品目标是使用 MyGameEngine 完成一个 PC 纵向、鼠标友好的十关核心切片：发射、墙面反弹、交错泡泡网格吸附、同色三消、悬空掉落、得分与胜负闭环。

## 目录

```text
games/BubbleTa/
├── src/
│   ├── BubbleTa.Game/              # 可运行游戏与 MyGameEngine 组合根
│   └── BubbleTa.Simulation/        # 无窗口、确定性的完整玩法编排
├── modules/
│   ├── BubbleTa.BubbleGrid/        # 单一职责：泡泡格坐标与拓扑
│   ├── BubbleTa.LevelFormat/       # 单一职责：新关卡格式与静态验证
│   └── README.md                   # 游戏局部模块的准入与晋升规则
├── tools/
│   └── BubbleTa.LegacyImporter/    # 旧 INI/Base64/JSON 的离线迁移入口
├── tests/                          # HomeScene 等游戏行为的无窗口测试
└── docs/
    ├── HOME_SCENE.md               # 首页实现、坐标与验证说明
    └── PROJECT_STRUCTURE.md        # 依赖方向、解决方案分组与迁移边界
```

## 当前项目

| 项目 | 类型 | 当前职责 |
|---|---|---|
| `BubbleTa.Game` | Exe | 可运行的 HomeScene、WorldMap 第一岛屿及各自的声明式内容与音频包 |
| `BubbleTa.Game.Tests` | Exe Tests | 首页动画、第一岛屿布局、确定性装饰、节点表现、按钮与 ESC 行为 |
| `BubbleTa.Simulation` | Library | 未来组合射击、消除、掉落、计分和胜负流程 |
| `BubbleTa.BubbleGrid` | Library | 独立泡泡网格模块，不依赖引擎或游戏表现 |
| `BubbleTa.LevelFormat` | Library | 独立的新关卡数据契约，不理解 GameMaker 文件 |
| `BubbleTa.LegacyImporter` | Exe Tool | 未来把旧关卡离线转换为新格式；运行时不依赖它 |

## 当前非目标

- 不复制旧 GML、第三方 UI 源码、SDK 或旧打包产物。首页使用的 32 张旧图片以及 Home BGM/点击音是内部重建原型的受控例外，发布前必须完成逐项来源与再分发权限审计。
- 不实现核心泡泡玩法、关卡转换、正式设置界面、商店、签到、支付或 Android SDK。
- 不把泡泡龙专属规则加入 `Engine.Core`。
- 不把尚未被第二个消费者验证的模块提升为通用 Engine Feature。

详细说明：

- [项目结构与模块约定](docs/PROJECT_STRUCTURE.md)：物理目录、解决方案分组、依赖方向和模块晋升门槛。
- [旧工程 Room 与场景迁移](docs/LEGACY_SCENES.md)：六个旧 Room、主要流程及 MyGameEngine Scene 边界建议。
- [HomeScene 实现说明](docs/HOME_SCENE.md)：运行方式、坐标系统、动画迁移、交互与资产发布 Gate。
- [WorldMapScene 考古与渐进规格](docs/WORLD_MAP_SCENE.md)：旧纵向地图结构、相机交互、岛屿虚拟化和现代职责边界。

## 运行与验证

```powershell
dotnet run -c Release --project games/BubbleTa/src/BubbleTa.Game/BubbleTa.Game.csproj
dotnet run -c Release --project games/BubbleTa/tests/BubbleTa.Game.Tests/BubbleTa.Game.Tests.csproj
dotnet run -c Release --project games/BubbleTa/src/BubbleTa.Game/BubbleTa.Game.csproj -- --smoke
```

Home 进入时循环播放自己的流式 OGG BGM。鼠标在世界按钮内完成按下与释放后播放一次 WAV 点击音，并进入 WorldMap 底部第一岛屿；取消点击不会发声。WorldMap 展示两张无缝拼接的岛屿主体、前后云层和 20 个只读关卡节点，并播放独立的流式 OGG BGM。Home 拥有固定 Camera；WorldMap 每次拖动会锁定主方向，纵向手势浏览完整地图，明确的横向手势可以暂时拉离中央 View 并在松开后橡皮筋回弹。两个 Scene 不共享 Navigation 状态。Home 中按 `ESC` 关闭，WorldMap 中按 `ESC` 返回 Home。

两首音乐均由各自 Scene 的 `SceneAudio.PlayMusic` 持有，离开时自动停止；点击音使用显式跨 Scene 的一次性 Voice，确保切换发生后仍能自然播放完。图片和声音均来自旧工程的内部原型迁移，不代表已获得公开分发许可，详见 [资产来源说明](src/BubbleTa.Game/Assets/ASSET_PROVENANCE.md)。

# 旧工程 Room 与场景迁移

本文记录 2015 年 GameMaker Studio 1 工程中的 Room、主要职责和跳转关系，为 BubbleTa 的 Scene 设计提供行为参考。旧工程位于 MyGameEngine 仓库之外，仅作为只读考古材料；当前仅 `rm_ini` 所需的 32 张旧图片作为内部原型资产受控迁入，旧源码、音频、第三方 UI 与 SDK 均未复制。

## 总览

旧工程共定义六个 Room：

| Room | 尺寸 | 速度 | 核心职责 |
|---|---:|---:|---|
| `rm_scale` | 760×1280 | 30 | 启动与画面比例初始化 |
| `rm_loding` | 960×1280 | 30 | 资源预加载与加载进度；名称沿用旧工程拼写 |
| `rm_ini` | 960×1280 | 46 | 首页、标题表现与主菜单入口 |
| `rm_world` | 1048×16100 | 46 | 纵向世界地图、关卡入口和外围系统 |
| `rm_game` | 960×1280 | 46 | 所有关卡共用的泡泡龙玩法 Room |
| `rm_logo_2` | 480×854 | 24 | 独立公司或渠道 Logo，疑似遗留可选流程 |

`rm_loding`、`rm_ini`、`rm_world` 和 `rm_game` 的活动 View 均为 720×1280。Room 的 46 FPS 是旧项目行为的一部分，但新 Simulation 不应把规则绑定到这一帧率；迁移时通过固定时间步和行为样本重新校准手感。

## `rm_scale`：启动与画面适配

Room 只预放置 `obj_scale`，负责初始化各 Room 的逻辑宽度、窗口和 View 缩放。完成后调用 `room_goto_next()` 进入加载 Room。

它更接近 MyGameEngine 的 Hosting/Presentation 启动配置，不必为了还原旧结构而保留为长期正式 Scene。

## `rm_loding`：资源加载

Room 只预放置 `obj_loading`。对象使用队列逐项预加载资源，绘制加载条、百分比和提示，完成后进入 `rm_ini`。

新版本只有在内容加载确实跨越可感知帧数时才需要独立 Loading Scene；首个十关切片可先由 Boot 流程同步加载，避免提前建立复杂后台加载系统。

## `rm_ini`：首页与主菜单

Room 预放置 34 个实例，包括：

- `obj_ini`：全局状态初始化。
- `obj_worldEnter`：世界地图入口。
- `obj_setEnter`：设置入口。
- `obj_drawer`：通用状态与界面绘制。
- `obj_logo_1` 到 `obj_logo_12`：标题和 Logo 表现。
- 云、星星、流星、泡泡与角色等首页装饰对象。

这个 Room 同时承担首页、标题动画、角色展示和若干面板入口。新版本应把 Home Scene 与面板状态分开，不延续全局对象相互探测的组织方式。

### 当前迁移状态

`BubbleTa.HomeScene` 已完成第一版行为重建：保留 960×1280 Room 坐标，以位于 `(120, 0)` 的 720×1280 相机做中央裁切；旧 46 FPS Alarm 换算为秒，由 60 Hz 固定更新推进。12 片 Logo 入场、云/泡泡/星光周期 Tween、三名角色入场与浮动、五个确定性闪点和五条确定性流星均已落地。

世界按钮已接到 `WorldMapScene` 底部第一岛屿只读景观，设置按钮仍只展示。Home 已接入流式 OGG BGM 和 WAV 点击音；Home 固定 Camera 与 WorldMap 纵向导航分别由各自 Scene View 拥有。旧 `obj_ini`/`obj_drawer` 全局逻辑仍不复制。实现与验证细节见 [HomeScene 实现说明](HOME_SCENE.md)。

## `rm_world`：纵向世界地图

完整的对象、相机、五段岛屿、可见性与渐进重建分析见 [WorldMapScene 考古与渐进规格](WORLD_MAP_SCENE.md)。

Room 高达 16100 像素，只预放置 `obj_world_control`，再由它动态创建：

- 0 到 4 组岛屿容器及其云层。
- 关卡节点、解锁状态和当前关卡入口动画。
- 体力、货币和星级信息。
- 商店、星级奖励、每日任务、签到、抽奖和设置入口。

点击关卡后会打开关卡开始面板，再由 `ui_levelStart` 进入 `rm_game`。首个重制切片不需要复原这些运营入口；世界地图可以等十关核心玩法成立后再加入。

## `rm_game`：实际泡泡关卡

Room 初始只有三个实例：

- `obj_bubble_builder`
- `obj_gameStore`
- `obj_limit`

关卡运行时动态创建泡泡布局、炮台、发射泡泡、主角、暂停按钮、道具、目标提示、得分，以及胜利、失败和复活面板。

最重要的结构事实是：旧工程的 100 个关卡不是 100 个 Room。它们全部复用 `rm_game`，由关卡数据决定布局、特殊泡泡、目标、球数和星级阈值。

新版本应保留这种“一个玩法 Scene + 参数化关卡”的思想，但使用强类型参数：

```csharp
SceneRef<BubbleLevelArgs>
```

参数只携带关卡 ID 或编译后的关卡引用；Scene 从新的 `BubbleTa.LevelFormat` 数据契约构造 Simulation，不读取旧 INI。

## `rm_logo_2`：独立 Logo

Room 只预放置 `obj_logo_2_show`，用于绘制公司 Logo。它在工程 Room 顺序中位于最后，也没有发现标准启动流程显式进入它的路径，因此更像特定渠道构建或早期流程遗留。

在确认旧发布包的真实调用条件前，不把它视为新版本必需场景。

## 旧版主要流程

```text
rm_scale
   ↓
rm_loding
   ↓
rm_ini
   ↓
rm_world
   ↓
rm_game
   ↓
rm_world
```

胜利结算、失败退出、取消重开和下一关等流程通常从 `rm_game` 返回 `rm_world`。设置、暂停、胜负、关卡开始和复活主要由 Room 内动态对象和面板表达，不是独立 Room。

## MyGameEngine 场景建议

不机械地为六个旧 Room 创建六个新 Scene。建议的现代边界为：

| MyGameEngine Scene | 旧 Room 来源 | 说明 |
|---|---|---|
| `BubbleTa.BootScene` | `rm_scale` + `rm_loding` | Presentation 初始化和必要内容加载 |
| `BubbleTa.HomeScene` | `rm_ini` | 已完成首个可运行重建；首页与进入游戏的产品入口 |
| `BubbleTa.WorldMapScene` | `rm_world` | 已重建底部第一岛屿的主体、云层、景观装饰与只读节点；进度和关卡进入未实现 |
| `BubbleTa.LevelScene` | `rm_game` | 参数化的实际泡泡玩法场景 |
| 可选 `BubbleTa.SplashScene` | `rm_logo_2` | 仅在确认产品需要后加入 |

设置、暂停、关卡开始、胜负和复活应采用 Scene 内强类型状态或覆盖层，而不是为每个面板创建 Scene。Home 与 WorldMap 第一岛屿已经成为真实场景切片；是否进入最小 `LevelScene` 核心射击循环，应由产品验证节奏决定，不要求先复原 World Map 的运营入口。

## 迁移约束

- Room 名称和对象关系只用于理解旧行为，不作为新公共 API。
- 不迁移旧全局变量、Alarm、`with` 或动态对象探测模式。
- 不把 46 FPS 当作玩法常量；用固定 Tick Simulation 和显式秒数表达规则。
- 不把世界地图的商店、签到、抽奖和支付入口纳入首个核心切片。
- 新场景只消费声明式 Content Package；未来 LevelScene 只消费新 LevelFormat，不在运行时访问旧工程。
- 当前首页旧图片只能用于内部重建原型；正式分发前必须通过资产来源与再分发权限审计 Gate。

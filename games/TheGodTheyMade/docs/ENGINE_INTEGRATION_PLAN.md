# MyGameEngine 集成计划

## 原则

《神意难测》用于验证 MyGameEngine 的真实游戏开发体验，但游戏需求不自动等于引擎公共能力。先在游戏侧证明规则，再把确实通用、具有稳定不变量的部分提升为 Feature。

## 可以直接复用

| 游戏需求 | 现有能力 |
|---|---|
| 村民、神兽和世界对象 | GameInstance、Prefab、InstanceRef、Gameplay Tag |
| 作息和行为阶段 | Gameplay State Machine、Behavior、Signal |
| 固定模拟与调试 | 确定性 Time/Random、Pause、Replay、State Hash |
| 地图与视野 | Tilemap、Camera2D、Viewport |
| 选择和邻域感知 | Box/Circle Collision、Area/Radius Query、复用 Query Buffer |
| 角色、环境与文字 | Sprite、Animation、Text Rendering、Content Assets |
| 短声音反馈 | Audio/OpenAL 声明式 WAV |
| 神迹视觉 | RenderPipeline、Stencil、Bloom、Shader Material |

## 首个原型前需要的通用切片

### Pointer Interaction

当前键鼠输入足以读取设备状态，但游戏需要稳定的指针语义：

- 屏幕、Viewport 和世界坐标转换。
- Hover、Press、Drag、Release、Cancel。
- 指针捕获，避免拖拽越过对象时丢失交互。
- 相机变化后仍能明确命中哪个世界。
- 为未来触控保留 Pointer Id，但 PC 首版只装配主鼠标。

逻辑输入记录应优先记录最终游戏命令，而不是每个鼠标像素移动。

### Grid Navigation

Tilemap 已负责世界表达和静态碰撞，但村民需要独立导航语义：

- 固定邻居顺序的确定性 A*。
- 可复用 Path Buffer，稳定更新不分配。
- 阻挡 Cell 与地图 Revision。
- 路径缓存和动态障碍失效。
- 首版只支持方格正交地图，不做 NavMesh、群体避障或分层寻路。

## 先保留在游戏侧

- `VillageDirector`：分配工作和组织日程。
- `BeliefSimulation`：观察、假说、传播和教义行为。
- `FamiliarLearning`：上下文特征、动作权重和教学。
- `GodHandController`：神迹交互和玩家表达。
- `IslandScenario`：谜题与章节进度。
- `MythChronicle`：壁画事实选择与文本模板。

这些名称不进入 `Engine.Core`，也不成为 SDK 的默认概念。

## 渐进实施顺序

### Gate 0：设计契约（首版已完成）

- 首岛事件词汇、玩家动词、地图、村民、强化学习参数和验证问题已冻结首版，入口见[首个 10 分钟可玩流程](FIRST_PLAYABLE_SCRIPT.md)与[模拟数据契约](SIMULATION_DATA_CONTRACT.md)。
- 不创建正式资产，不实现第二个神迹。

### Gate 1A：交互、导航与村庄灰盒（已完成）

- 已建立 Simulation、Game、Simulation Tests 和声明式 Content Package。
- 复用 Hosting 现有 Screen→World API，在游戏侧实现 Pointer Hover/Press/Drag/Release 与捕获，不重复新增 Engine Pointer Feature。
- 已实现确定性四方向 Grid Navigation、Navigation Revision、复用 Path Buffer 和 0 B 稳态查询。
- 12 名占位村民已按 600 秒日程在工作锚点、广场和家庭间移动；Camera、缩放、Cell 调试与巨石阻挡切换可用。
- 当前未实现降雨、信仰或强化学习，保持 Gate 间的验收边界。

### Gate 1B：可观察世界状态（已完成）

- 已实现神意、局部降雨、蓄水池、水闸、水渠和三块田地湿度，并把状态纳入确定性 Hash。
- 钟声、降雨、枯萎、恢复和水闸打开均发布为游戏侧 `WorldObservation`，事件 ID 单调递增。
- 每名村民只按 Visual/Auditory/Direct 范围与 Bresenham 视线记录实际观察，最近关键记忆固定为 32 条；本 Gate 不生成信仰。
- Pointer Release 只在 Game 层转换成带固定 Tick 和目标 Cell 的 `MingzhongCommand`；Simulation Tests 回放最终命令而非原始鼠标像素轨迹。

### Gate 2：信仰误解（已完成）

- 已注册八条首岛因果白名单；只有实际进入村民记忆的事件能够成为个人证据。
- 已实现整数证据、窗口成功/超时反证、Prior、每人八条有界假说、稳定淘汰、证言传播和最小公共教义。
- 钟声召雨的分数阈值会改变敲钟、钟塔维护和教义集会三类 `VillageDirector` 任务。
- `--scripted-belief` 已通过真实 Replay Bundle 录制/回放和逐 Tick Gameplay State Hash 验证；录制逻辑时序而非原始鼠标输入。

### Gate 3：神兽学习（已完成）

- 已只实现猿形身体、七种离散态势和六个候选动作；身体能力、Affordance 与失败冷却先于 Q 值执行硬过滤。
- 游戏侧使用 `GameplayRandom`、`-8000..8000` 整数 Q、冻结的 Alpha/Gamma/Epsilon 与有界奖励，没有新增通用 Engine AI Feature。
- 已接入 `+800` 示范先验、Q/E 嘉许/制止、环境结果、5 秒信用窗口、信赖倍率和最近 16 条梦境/诊断解释环。
- 无窗口测试覆盖受控探索、非法动作、错误泛化后的渐进纠正、Snapshot 恢复下一选择；真实 Replay 固定脚本还覆盖示范、搬开巨石和环境奖励。

### Gate 4：完整《鸣钟谷》（工程完成，待外部盲测）

- 已用 `MingzhongIslandScenario` 组合水闸搬运、湿遗迹、钟与葬礼三个谜题、无操作恢复路径、葬礼抉择与有限模板三联壁画。
- 可运行 Game 已呈现水位/田地/雨区/遗迹/葬礼/壁画灰盒视觉，并为钟、雨、水闸与葬礼注册程序生成短音；这些是最低反馈，不冒充正式资产。
- 40 项无窗口检查覆盖三条 108,000 Tick 章节分支、不同壁画、终态零分配、确定性、Gameplay Command Journal 与 Gate 阈值判定；真实 Replay 继续逐 Tick 验证固定脚本的窗口集成。
- 真实盲测可用版本化命令流复现被接受的降雨落点、水闸与神兽反馈，并输出结构化 Playtest Report；它不冒充鼠标轨迹或玩家理解证据。
- 无窗口 `--audit-playtests` 会严格读取独立测试员报告、拒绝重复身份/缺失问卷，并输出机器可读的 Gate Audit；“测试员此前不知规则”仍由招募记录证明。
- win-x64 自包含盲测包会固定源码 commit、内容包与 SHA-256，发布前执行隐藏窗口烟测；包内启动器以唯一测试员 ID 自动写出命令流和报告，并禁止覆盖已有证据。
- 正式关闭 Gate 仍需按 [Gate 4 外部盲测协议](GATE4_PLAYTEST_PROTOCOL.md) 完成至少 5 名陌生玩家验证，再决定是否扩产。

## 性能策略

- 世界与移动维持 60 Hz 固定更新；角色高层决策分散到 2～5 Hz。
- 信仰按观察事件更新，不每帧扫描所有事件组合。
- 路径搜索错峰执行，按 Tilemap/Navigation Revision 缓存。
- 首版 12 名村民无需 ECS、Job System 或 Spatial Hash。
- Y-Sort 先按脚底位置更新 Depth；只有规模和性能数据证明需要时才进入引擎。

## 存档边界

Replay 和 Gameplay State Hash 是确定性诊断能力，不是正式存档。垂直切片后期需要单独设计：

- 游戏定义的版本化 Snapshot。
- 岛屿状态、村民、信仰、神兽学习、确定性随机和谜题状态。
- 原子写入与损坏恢复。
- 明确迁移策略，而不是直接序列化运行时对象图。

只有原型状态模型稳定后才实现，避免保存尚未稳定的内部结构。

## 明确延后

- 完整 2D 动态光照与阴影材质。
- Streaming Music 和复杂环境混音。
- FairyGUI、HTML/CSS GUI 或大型编辑器。
- 多岛无缝加载、开放世界和后台 Scene Stack。
- 完整地下城房间系统、占有模式和数百自治单位。
- 多身体神兽、生成式对话、深度强化学习和复杂社会模拟。

# Developer Experience Roadmap

本路线聚焦“让游戏开发者更容易正确使用引擎”，暂不扩展 UI 系统。优先消除组合根样板、字符串资产名和难以诊断的装配错误，同时保留底层 RenderPass、Factory 与资源 API 作为高级逃生口。

## 阶段 1：Engine Hosting 与默认 2D 渲染预设（已实现）

目标是把普通游戏入口从手工创建 Window、Shader、Batch、Library、RenderTarget、Pipeline 和 Factory，收敛为声明式启动代码。

计划能力：

- `GameApplicationBuilder`：配置窗口、内容包、初始 Scene 和默认渲染预设。
- `GameApplication`：统一接管 Load、Step、Draw、Resize、Closing 与异常清理。
- `Default2DRendererOptions`：按需启用 HDR/Tone Mapping、Bloom、Stencil 和 SceneGui。
- `Default2DGameContext`：向 Scene 装配回调提供强类型 Scene、Content、Texture、Sprite、Animation、Camera 和渲染扩展入口。
- 资源所有权：固定 Builder → Pipeline → Pool → RenderTarget → Content/Library → Batch/Shader 的释放顺序，初始化失败时逆序回滚。
- 高级逃生口：仍允许注册自定义 `IRenderEffectFactory`、根 Surface 和 RenderPass。

验收结果：Runner 已使用 Hosting API，不再维护静态 GPU 字段或窗口回调；默认预设保留 Spotlight、HDR Bloom、Tone Mapping、resize、ESC 和关闭释放行为。配置验证、默认 owner 事件顺序与逆序资源清理已有无窗口测试。

## 阶段 2：强类型 Content 访问（已实现）

从 `assets.json` 或编译产物生成稳定的 C# 标识：

```csharp
GameAssets.Sprites.PlayerIdle
GameAssets.Textures.WorldTiles
GameAssets.Animations.PlayerRun
GameAssets.AnimationEvents.PlayerRunFootstep
GameAssets.Packages.SharedPrimitives
```

目标是把资源拼写错误从运行时提前到编译期。生成器只产生逻辑 `SpriteRef`、`TextureRef`、`AnimationClipRef`、`AnimationEventRef` 和 `ContentPackageRef`，不包含 GPU 句柄，也不改变 ContentPackageManager 的生命周期。

验收结果：AssetCompiler 从编译后的 Manifest 依赖图生成确定性 `.g.cs`；已打入 Atlas 的源 Texture 与内部 Atlas 页不会泄漏为公开引用，标识符冲突在构建期失败。Runner 已使用 `GameAssets.Packages.Root` 和 `GameAssets.Sprites.RunnerOrbiting`，Hosting 会校验包 ID。

## 阶段 3：项目模板与命令行体验（已实现）

- `MyGameEngine.GameSdk` 聚合正式运行时程序集并声明第三方运行依赖；源码 Feature 仍保持垂直切片。
- `MyGameEngine.Templates` 提供 `dotnet new mygameengine-game` 最小项目模板。
- 模板默认包含 `Assets/assets.json`、真实 WebP、首个 Scene、实例示例和内容构建配置。
- 隔离式分发测试执行 Pack → 安装模板 → 仓库外创建 → Restore/Build/Run/Publish，并拒绝仓库路径与 `ProjectReference` 泄漏。
- `gameengine doctor` 检查 SDK、包版本、Restore、内容清单与 Build 输出；`--probe-opengl` 显式验证隐藏 OpenGL 3.3 Context。
- 给出 Debug、Release、Publish 的可复制命令。

验收结果：四个分发包共享版本；模板项目只引用 GameSdk 与 ContentPipeline，并通过本地 Tool Manifest 固定 CLI。Build 自动生成强类型 Content，`--smoke` 可隐藏窗口运行三帧并正常释放，Doctor 普通检查与 OpenGL Probe 均在仓库外生成项目通过，Publish 只携带运行时程序集与编译后资产。

## 阶段 4：诊断与可观察性

- 输出 RenderSurface 依赖图、Effect owner、Pass 顺序和 RenderTarget 租约快照。（已实现）
- 为未知资源、缺失 Factory、格式不匹配和依赖循环提供带上下文的诊断。
- 可选帧统计：FPS/UPS、Draw Call、有效 Batch Flush、纹理切换和活跃 Pass。（已实现）
- Texture/Atlas、根目标与 Pool 缓存显存估算，支持高级资源显式补充。（已实现）
- 结构化性能预算、低频 Sink 与 Runner 控制台/JSON Lines 导出。（已实现）
- 诊断 API 默认只读，不改变运行时状态。

当前验收：`Default2DGameContext.CaptureRenderDiagnostics()` 聚合 Pipeline、Builder、Pool 与可选最近帧统计；`FrameRateSettings` 支持启动及运行时 FPS/UPS/VSync 控制。统计默认关闭，开启后以零帧分配值快照记录内置 SpriteBatch 与后处理 Draw Call。`CapturePerformanceSnapshot()` 进一步聚合 Texture/Atlas、根目标、活动及缓存 RT 和自定义资源，预算超限以结构化值交给低频 Sink；Runner 支持控制台与 JSON Lines。

## 阶段 5：开发期热重载

- 优先支持内容包和 Shader 热重载，再考虑代码热重载。
- 失败时继续使用上一份有效资源，不破坏当前 Scene。
- 与 Content 指纹、Atlas 原子替换和 Hosting 生命周期共享同一所有权边界。

当前验收：Content 包已使用编译元数据轮询和去抖，后台完成 Manifest 图校验、图片解码、Sprite 规范化与 Animation Clip 验证；Texture/Sprite/Animation/包索引在 Step 与 Draw 之间事务切换，失败保留旧修订。自定义 Sprite Shader 支持安全根文件注册、稳定源码快照、整批 Program 原子替换和驱动错误诊断；投影同步覆盖主 Scene 与 Stencil 重绘。类型化材质参数块以逻辑 Shader 引用保存 CPU 参数，支持多材质共享 Program、按 Revision 批处理和热替换后自动重放。材质装配与热重载候选使用 GL 反射验证 Uniform 名称、类型和数组边界；编译诊断保留源码路径、行号、阶段和原始驱动日志。`shaders.json` 已把 Program 文件、Material Schema 和默认值暴露给 Hosting 与 MSBuild；AssetCompiler 在 CoreCompile 前静态校验，并生成 `GameShaders` 下的强类型 Shader、Material 与 Uniform 参数键，运行时继续由真实驱动复核。Runner 提供 `--content-hot-reload` 与 `--shader-hot-reload`。

离线 GLSL 编译仍保留为显式可选方向，但不再占用当前开发体验主线；适配器、诊断、缓存与恢复条件记录在[可选离线 Shader 编译方向](OFFLINE_SHADER_COMPILATION.md)。

## 阶段 6：Gameplay Authoring Experience（当前主线）

目标从“继续完善引擎装配基础设施”转向“减少普通玩法类每天重复编写的样板”。游戏对象应能在不接触 `SceneAggregate`、领域事件回调、可空输入或渲染基础设施的情况下完成常见行为。

- `Position/Rotation/Scale` 与 `MoveBy/RotateBy/ScaleBy` 提供直接变换入口。
- 非空 `Controls`、`KeyDown/KeyPressed/KeyReleased` 和 WASD `InputAxis2D` 收敛输入查询。
- 不可变 `InputMap`、`InputActionRef` 与 `InputAxis2DRef` 已把玩法意图从物理键位解耦；Hosting 集中绑定，Scene 注入现有和后续实例，稳态查询保持 0 B。
- `InputActionBuffer` 与 `GameplayGracePeriod` 提供显式捕获/消费、暂停感知和零分配的预输入与条件宽限，不把跳跃、冷却等玩法规则塞入输入系统。
- `GameplayCooldown` 提供 ready/use/progress/restart/reset 的 owner-local 冷却语义；AirplaneShooter 与 Asteroids 已移除重复的手写浮点计时，并继续继承暂停、时间缩放和 inactive 调度。
- `GameplayHealth`、`GameplayHealthChange` 与 `IHasGameplayHealth` 提供钳制生命值、一次性耗尽/复活转换和 Tag + capability 的伤害调用方式；不把护甲、来源、死亡表现或 RPG 规则固化进 Core。
- `InstanceRef<T>` 提供只保存 ID 的强类型弱引用、O(1) Resolve 与类型校验销毁；Asteroids Spawner 通过它跨帧追踪玩家，不保留已脱离 Scene 的对象。
- `SimulationClock` 在每个 Step 提供稳定 Tick、缩放/非缩放 delta 与累计时间；`WithFixedUpdateRate` 绑定 UPS 和固定 delta，owner-local `GameplayRandom` 以 versioned PCG32 提供可恢复、零分配随机流。
- 强类型 `GameplayTag` 与 Find/Collision/Area/Radius 对称重载已让横切玩法身份脱离继承树；类型和单 Tag 可组合，Buffer 路径保持 0 B，不提前维护 Tag 索引。
- 轻量 `GameplayBehavior<TInstance>` 提供强类型 Owner、冻结装配、确定性生命周期和暂停感知调度；`LifetimeBehavior` 已替代两个 Playground 的重复子弹 Alarm，稳态分发保持 0 B。
- 技能与 Buff 已完成需求分析：推荐独立 Abilities 切片，以固定 BuffContainer/SkillBook 管理动态 Runtime，先验证 Buff 叠层、来源和安全修改，再实现 Skill 提交与游戏专属 Executor；不提前引入万能 Effect DSL 或通用 RPG 属性系统。详见[技能与 Buff 功能设计思考](SKILLS_AND_BUFFS_DESIGN.md)。
- Scene 注入实例级 `IGameplayContext`，提供 `Spawn/DestroySelf/Destroy/Find`，不引入全局 Service Locator。
- Gameplay Spawn/Destroy 在 End Step 后按请求顺序确定性提交；新实例下一帧 Step，待销毁实例完成当前 End Step。
- `AlarmId`、`SetAlarm/CancelAlarm/OnAlarm` 提供无协程依赖的轻量计时。
- 项目模板使用 WASD 移动、Space 生成 Bullet 和 Alarm 自动销毁展示黄金路径。
- 声明式 Scene 目录、安全帧边界切换和持久实例语义。
- `SceneRef<TArgs>` 把结算/关卡入口参数与目标 Scene 编译期绑定，在请求时复制并由配置函数直接消费。
- 类型安全、构建后冻结的 Instance Factory / Prefab。
- `PrefabRef<T, TArgs>` 与 `in TArgs` 提供不装箱的强类型构造参数。
- Box/Circle Collider，以及按类型的相交、区域和半径查询。
- AirplaneShooter 与 Asteroids 分别验证直线射击和旋转推进/声明式 Spawn/Wave/重启流程。
- Gameplay Cookbook 收敛常见配方；Release 基准记录 100/1,000/10,000 Collider 线性查询成本。
- `Easing`、`Tween` 与 `Motion` 提供归一化曲线、值/最短弧度角插值、限速追踪和半衰期平滑，不引入全局 Manager。
- SceneAggregate 使用可复用阶段快照和原地稳定排序；Input、Step、Draw、DrawGUI 在实例规模预热后保持 0 B/帧，同时保留阶段间直接变更与 Gameplay 帧边界提交语义。
- `GameplayStateMachine<TState>` 提供强类型 Enter/Step/Exit、状态计时、确定性回调后切换和冲突/循环保护；配置后 Update/Change/Restart 保持零稳态分配。
- `GameplayQueryBuffer<T>`、`CountInstances<T>()` 与 Buffer 查询重载保留便利数组 API，同时给高频路径提供 0 B 结果复用；可选遥测按真实 Step 汇总调用、候选、命中和耗时。
- `ReplaySession` 已把逻辑输入与状态 Hash 收敛为版本化 `.mgreplay`：Hosting 一次装配 Record/Playback、Build 身份和 fixed delta 启动前校验、首次分叉诊断、受限读取与最后 Tick 自动退出；Asteroids 提供录制/回放入口。
- Scene 作用域强类型 `Gameplay Signal` 已以 Asteroids 击毁事件验证真实一对多协作：值类型载荷、构造期显式监听、End Step 后确定性投递、暂停/失活/销毁语义、嵌套通知延迟和热身后 0 B；不引入全局总线或反射。
- `SpawnSequenceBuilder/SpawnSequencePlayer` 已把 Delay、有限 Wave、Once/Loop、并发门控、状态快照和大步长确定性推进收敛为 owner-driven 时间线；Asteroids 已移除生成 Alarm，游戏仍掌握随机参数和 Prefab 回调。
- Animation 黄金路径已接通：声明式 Content、强类型 Clip/Event、Hosting Catalog、GameInstance Behavior、Sprite/ImageIndex 驱动、状态快照和原子 Hot Reload；Asteroids 提供真实消费样例。
- 独立 TransformHierarchy 阶段 0 已提供 generation Handle、Local/World、KeepLocal/KeepWorld、循环/Shear/不可逆拒绝、深树迭代传播和 0 B 稳态；尚未接入 Scene/GameInstance/Prefab。
- TextRendering 黄金路径已接通：真实 Skia TTF/OTF、Font Fallback、Rune + Grapheme 单行 Layout、TextureLibrary 局部上传、动态 Glyph Atlas、Hosting TextRuntime 与 World/SceneGui DrawText；隐藏 OpenGL smoke 已验证释放链。
- Audio 黄金路径已接通：声明式 PCM WAV 与流式 OGG Vorbis、强类型 Clip、OpenAL Soft/Silent、四 Buffer 队列、确定性 Voice 抢占，以及随 Scene 自动停止的 `SceneAudio` 所有权。

当前验收：无窗口顺序测试覆盖输入边沿、变换、生成可见性、Create/Step/Destroy 顺序、实例查询、DestroySelf、inactive Alarm、Prefab 冻结及参数传递、Collider 组合和 Scene 请求；两个 Playground 冒烟均真实跨 Scene。完整语义见 [Gameplay Authoring Experience](GAMEPLAY_AUTHORING.md)、[Scene、Prefab 与碰撞查询](SCENE_PREFABS_COLLISION.md)和 [Gameplay Cookbook](GAMEPLAY_COOKBOOK.md)。

下一步优先级：Animation、TransformHierarchy、Text 多行 Layout、Audio（短音效 + Streaming Music）和 Tilemap/World Authoring 第一条黄金路径已经闭环。主线回到“由真实游戏切片暴露需求，再补 Gameplay Authoring 缺口”，不再按基础设施清单连续扩张。Lighting 0/1 保持高价值候选，但应由实际场景的画面目标触发；完整 Skill/Buff、GUI 控件、协程、物理系统与逐帧异形碰撞继续保持需求记录。

Transform Hierarchy 已完成数学/Handle、Scene/GameInstance 接入和第一版强类型组合 Authoring：`context.Transforms`、opt-in Binding、纯挂点、Local/World、`KeepLocal/KeepWorld`、`TransformPrefab<TParts>`、具名类型节点与帧边界同步均已落地；AirplaneShooter 使用 `root → weapon → muzzle` 真实样例。仍保留 Scene 扁平 Step、Layer/Depth、Collider 索引，不让空间父子关系接管生命周期、渲染排序或 UI 布局。下一步先由玩法验证是否需要原子多 GameInstance Composite Prefab。使用见 [Transform Hierarchy 创作指南](TRANSFORM_HIERARCHY_AUTHORING.md)。

## 阶段 7：Interactive Viewport 与大世界观察边界（可用基线已完成，转入维护）

目标是把成熟的 pixi-viewport 地图浏览体验重建为 MyGameEngine 原生 Camera 能力，并为 Chunk Streaming/LOD 提供稳定但不耦合资源系统的观察边界。

第一阶段已经完成：

- 官方 pixi-viewport 6.0.3 仅下载到 Git 忽略的参考区，用于核对公共功能形状；仓库不纳入或逐行翻译其 TypeScript。
- `ViewportController`、可替换/暂停/恢复的固定顺序插件管理与 `ViewportSnapshot/Revision` 已落地。
- Drag、鼠标锚点 Wheel、可选平滑、帧率无关 Decelerate、ClampZoom、Clamp/Underflow 已实现。
- `UseInteractiveViewport` 为单主 Camera 提供黄金入口；`UseRenderViews` 可让每个 View 独立声明，CameraFollow 所有权冲突在装配期拒绝。
- Hosting 在 Scene Step 前按最上层 View 路由输入，Resize 后重新约束；核心稳定更新 0 B，真实 OpenGL 隐藏 smoke 通过。

统一多 Pointer/Pinch、Bounce/Animate/Snap/SnapZoom/MouseEdges 和独立 `WorldChunkStreamer` 已完成。TileWorld 已贯通声明式清单、权威 LOD0、逐 Layer LOD1+ exact 无损 WebP、既有 WebP 切片离线导入、运行时 Zoom/滞回、有界后台解码、非阻塞 LOD 退休、主线程逐帧 Texture 上传预算、单 LOD Raster 稳态驻留预算、完整替换、最粗可用 LOD 与逐 Layer Preview 回退图（Fallback Surface）；所有权诊断可区分 CPU Payload、逻辑 GPU、Lease 与在途任务。小型临时合成世界覆盖自动回归，仓库外 ZL Editor `12000×12000`、400 张详细切片和 Preview 回退图已完成真实 SDK 集成、五轮加载/卸载与内存趋势验证，不作为公共 API 特例或仓库资产。

这条路线现在满足有限大地图的常用生产路径，转入需求驱动维护。跨 Session/多 View 共享缓存、LOD 淡化、逐 Chunk 热重载、交接期总驻留预算和 LRU 仅在真实游戏证明现有边界造成卡顿、显存超限或重复资源时恢复；不再为假设规模提前实现。Chunk、异步 IO、Texture lease 和显存预算仍不进入 Viewport 项目。完整用法见 [Interactive Viewport](INTERACTIVE_VIEWPORT.md)、[World Chunk Streaming](WORLD_CHUNK_STREAMING.md)、[TileWorld 离线切片编译器](TILE_WORLD_OFFLINE_COMPILER.md) 与 [TileWorld 运行时流式加载](TILE_WORLD_RUNTIME_STREAMING.md)。

## 候选视觉主线：2D Lighting（已规划，尚未实施）

2D Lighting 已收敛为独立渐进路线，但目前不冒充已完成能力，也不覆盖 Gameplay Authoring 当前优先级。未来切换到该主线时，按以下顺序推进：

1. 先固定 sRGB 颜色纹理、Linear 数据纹理、Linear Scene Surface 与最终显示编码，建立 GPU 基线。
2. 实现每 View 显式启用的 Environment + 无阴影 Point Light，共享一个 Lighting Runtime，默认 Half Light Buffer、256 可见灯预算。
3. 增加 Point/Spot 的 Box、Circle、Convex Polygon 可见性硬阴影，默认 32 个阴影灯预算。
4. 增加 Directional、Contact Shadow 与 Projected Shadow，覆盖平台游戏、户外与角色落地感。
5. 在多纹理材质、配对 Atlas 与 Multi-Attachment/G-Buffer 边界稳定后，再实现 Normal/Emission Lighting Material。
6. Cookie、Line Light、Fog、Soft Shadow、SDF/极坐标 Shadow Map 和空间索引均由真实画面与性能数据触发。

完整阶段、API 候选、验收和非目标见 [2D 光照、阴影与受光材质渐进路线图](LIGHTING_2D_ROADMAP.md)。

## 候选内容与 GUI 主线：Text Rendering + GUI Integrations（基础/Spike 已完成）

原生 Text Rendering 与 FairyGUI 保持两条边界：

- 原生 Text 是引擎基础能力，服务 World Text、SceneGui、字幕、对话、伤害数字和无 FairyGUI 游戏。优先实现中文 Font、Fallback、Unicode Layout、动态 Glyph Atlas 与基础 Draw，再渐进加入彩色富文本、Grapheme-aware 打字机、Sprite Emoji、内联动画和 IME。
- FairyGUI 是可选集成，不进入 Core 或默认 SDK。官方 MonoGame Runtime 与当前 Silk.NET/OpenGL 后端不同，因此先做受限 Compatibility Spike；只有 Render/Input/Loader Adapter 成本可控、NativeAOT 和真实 Package 通过后才产品化。
- FairyGUI 初期保留自己的 Text/RichText 语义，原生 Text 不强行替换其 Layout。两者可以复用字体文件、Texture 上传和诊断，但必须保持 Editor Preview 一致性。
- GIF/Animated WebP 直接解码不是文本第一阶段；内联动图优先使用现有 `SpriteRef`/未来 Animation Clip 或 FairyGUI MovieClip。
- GUI 第一轮兼容性 Spike 已完成：Yoga 只作为 Flexbox Layout 内核进入 C ABI/NativeAOT 后续实验；RmlUi 是开发者优先完整 Runtime 首选候选；FairyGUI 继续作为设计器优先可选路线。三者不同时产品化。

完整路线见[中文字体、文本绘制与富文本渐进路线图](TEXT_RENDERING_ROADMAP.md)、[HTML/CSS、Yoga 与游戏 GUI 兼容性 Spike](HTML_CSS_YOGA_GUI_ROADMAP.md)和[FairyGUI 可选集成渐进路线图](FAIRYGUI_INTEGRATION_ROADMAP.md)。

## 跨路线当前优先级

| 优先级 | 路线 | 当前决策 |
|---|---|---|
| 已完成基础 | Gameplay Signals、Spawn/Wave Authoring | Asteroids 已验证一对多通知与确定性生成时间线 |
| 已完成接入 | Animation Authoring | Content、强类型生成、Hosting、GameInstance 与 Hot Reload 已闭环 |
| 已完成接入 | Scene Graph / Transform Hierarchy | Scene、GameInstance、纯挂点与强类型 Transform Prefab 已接入；多实例 Composite Prefab 等真实需求验证 |
| 已完成可用基线 | Tilemap/World Authoring | 声明式编译、预切片导入、Preview、LOD、后台加载、上传/驻留预算和真实外部地图验收已闭环；后续按证据维护 |
| 已完成接入 | 原生中文 Text Rendering | 真实 Font、中文/单词多行、对齐、Ellipsis、复用 Buffer、Hosting 与 World/SceneGui 已闭环；Shaping 后续 Spike |
| 已完成接入 | Audio 短音效与 Streaming Music | 声明式 WAV/OGG、OpenAL 四 Buffer 队列、强类型 Clip、SceneAudio 所有权与诊断已闭环 |
| 当前主线 | 真实游戏驱动的 Gameplay Authoring | 继续推进可运行场景和游戏切片；只有重复样板、正确性风险或性能数据出现时才增加通用引擎 API |
| 维护模式 | 大世界 Authoring | 常用单 Session 路径与真实 12000×12000 地图验收已完成；共享缓存、LRU 和淡化由真实瓶颈触发 |
| 已完成调研 | Yoga/RmlUi/FairyGUI Compatibility Spike | 已建立候选顺序、适配面和 Go/No-Go 门槛 |
| P2 | RichText、彩色文字、Typewriter、Sprite Emoji | 建立在原生 Text Layout 上 |
| P2 | Gamepad/Rebinding、Save Game | Logical Input 与显式状态协议已有基础 |
| P2 | Lighting 阶段 0/1 | 先做颜色空间与无阴影 Point Light，不直接跳到 G-Buffer |
| P2 | FairyGUI 最小可用集成 | 只有 Spike 通过且 Text/Input/SceneGui 边界稳定后进入 |
| P3 | 彩色 Font Emoji、AnimatedImage、FairyGUI 高级组件 | 由真实产品需求和资产驱动 |
| P3 | Lighting 软阴影/高级材质、完整物理/导航 | 由性能数据和真实玩法驱动 |

Transform Scene/Prefab、Text 多行/Layout Buffer、Audio 短音效/流式音乐与 Tilemap/World Authoring 可用基线均已完成。大地图开发体验已经推进到 `Interactive Viewport → World Chunk Streaming → TileWorld LOD0 → LOD1+ 分层 WebP → 既有切片导入 → 非阻塞运行时 LOD/Loader → 逐帧 GPU 上传预算 → 稳态驻留预算 → Preview 回退图（Fallback Surface）→ 所有权与内存趋势验证`，并经过仓库外真实地图验收。当前回到真实游戏切片和 Gameplay Authoring；Lighting、Save Game、Gamepad/Rebinding 或更复杂碰撞等候选，由游戏遇到的第一个明确阻塞决定顺序。HarfBuzz、Yoga C ABI 与 RmlUi Render Spike 可以独立调研，但完整 GUI 集成不能越过输入路由、IME、资源租约和 SceneGui 状态恢复；Yoga Layout Tree 不替代世界 Transform Hierarchy。

## 设计约束

- 不引入全局 Service Locator。
- 默认预设负责常用路径，但不隐藏逻辑 RenderSurface 和资源所有权。
- 可选 Feature 未启用时不创建对应 Shader、Factory 或 RenderTarget。
- Host 不把 GPU 对象注入领域描述符或 GameInstance 状态。
- 所有便捷 API 必须能映射回现有底层 API，避免形成第二套渲染实现。

# 项目现状

更新日期：2026-08-11

项目处于 Phase 1.x：最小引擎闭环已经可运行，并开始由真实游戏灰盒反向验证开发体验。当前解决方案共 68 个 .NET 项目、仓库共 69 个项目；19 个正式/基础 Feature 模块保持垂直切片，`Engine.Hosting` 作为开发者入口组合根。

## 已完成

- Silk.NET 窗口、OpenGL 3.3 Core、统一 InputSystem 和 GMS 风格 GameInstance 生命周期。
- SceneAggregate、Layer、Background、Camera2D、SpriteBatch 状态机和 RenderPass DAG。
- SpriteRef/SpriteLibrary、多帧动画、原点、缩放旋转和 DrawSprite 便利 API。
- TextureRef/TextureLibrary、PNG/静态 WebP、声明式 Content Assets、包依赖和引用计数卸载。
- 确定性离线 TextureAtlas、跨页动画、大帧旁路和增量 AssetCompiler。
- Build/Run/Publish MSBuild 资产集成、内容指纹、check/rebuild、所有权和原子替换。
- 类型化 `RenderEffectRequestedEvent`/`RenderEffectReleasedEvent`，描述符不携带 GPU 对象或绘制回调。
- `ScenePipelineBuilder` 按 EffectKey 与 owner 集合差量创建、更新和回收动态 Pass。
- `RenderTargetPool` 按完整 Descriptor 复用临时 RT，并在 resize 时安全重建活跃效果。
- Runner 的 SpotlightController 作为组锚点声明 Stencil 几何与对应 Presentation；Hosting 默认 owner 声明 World、HDR Bloom、Tone Mapping、GUI 与 Presentation。
- 独立 Bloom 描述符、三目标 Bright/Ping/Pong 效果链、五采样分离高斯与分辨率重建协议。
- 纯逻辑 `RenderSurfaceKey`、Factory Plan、根 Surface 注册、效果输出解析与稳定拓扑依赖图。
- 动态效果结构变化采用全图原子重建；缺失输入、重复输出、循环、创建或挂接失败均保留旧图。
- RenderTarget 支持 RGBA8 与 RGBA16F；Pool 按完整格式隔离复用，逻辑 Surface 同时校验存储格式与 Linear/Display 编码。
- 独立 Tone Mapping 描述符、ACES/Reinhard、Exposure/Gamma 原地更新，以及 Scene + Bloom HDR 合并后输出 RGBA8。
- Runner 已迁移为 RGBA16F Scene → HDR Bloom → ACES Tone Mapping，并保持 Stencil、resize 与释放行为。
- 独立 Presentation 垂直切片把 RGBA8/Display Surface 作为显式终端输入；动态 Runtime 不再携带屏幕合成副作用。
- `SceneGui` 根 Surface 与 `SceneGuiRenderPass` 把 Draw GUI 固定在 Tone Mapping 之后，UI 不参与 HDR 曝光和 Bloom。
- Stencil Mask 支持专用 Shader 的真实圆形裁剪与 Sprite Alpha 裁剪，并继承 Sprite 帧、原点、旋转和正负缩放。
- `StencilMaskGroupRef` 支持多 owner 共享组；`RequestStencilMasks` 允许单 owner 批量提交几何，同时保持一套 Pass/RenderTarget。
- `GameApplicationBuilder`、默认 2D 渲染预设与强类型 `Default2DGameContext` 已统一接管窗口事件、内容、Scene 帧循环、resize 和逆序资源清理。
- Runner 已迁移到 Hosting API，不再手工持有 Shader、Batch、Library、RenderTarget、Pipeline 或窗口回调。
- 23 个无窗口冒烟/集成项目覆盖领域值、生命周期、渲染状态、资源资产、Atlas、Shader/Material 清单、编译产物、动态 owner、Hosting、Bloom/Tone Mapping/Presentation 设置、池所有权、CLI 诊断、外部分发以及 Animation/Audio/Text/Transform 基础。
- `Engine.Testing.Visual` 提供隐藏固定步长窗口、RGBA8 framebuffer 捕获、PNG 编解码和像素容差比较。
- 自动 GPU 回归覆盖 Sprite、Shader Program 成功/失败替换、真实圆形/Sprite Alpha Stencil、动态 resize、Bloom、双 Bloom Surface 串联，以及 HDR、LDR GUI、ACES/Reinhard、曝光、resize 和释放，共 17 个 checkpoint。
- AssetCompiler 可打包为 `gameengine-assets` .NET Tool；ContentPipeline NuGet 包通过 `buildTransitive` 为外部项目提供内置编译器、增量 Build 与 Publish 接入。
- 内容构建从编译后 Manifest 图生成 `GameAssets.Packages/Sprites/Textures`；Atlas 内部页和已吞并源 Texture 不进入公开 API，标识符冲突在构建期失败。
- 包集成测试使用临时本地 Feed，真实验证 Tool 安装、带空格路径、Debug/Release、缓存命中与 Publish 边界。
- `MyGameEngine.GameSdk` 已聚合 15 个正式运行时程序集（包含 Replay）并声明 Silk.NET/SkiaSharp 依赖；包内不包含源码项目依赖、符号或仓库绝对路径。
- `MyGameEngine.Templates` 已提供 `dotnet new mygameengine-game`；生成项目包含 Hosting、GameInstance、声明式 WebP 内容与强类型引用，不包含 `ProjectReference`。
- 分发集成测试使用隔离 CLI Home、NuGet 包目录和临时本地 Feed，真实验证 Pack、模板安装、仓库外 Restore/Build、三帧 smoke run 与 Publish。
- `MyGameEngine.Cli` 提供 `gameengine doctor`：默认执行无图形副作用的项目与内容诊断，`--probe-opengl` 显式创建隐藏 OpenGL 3.3 Context。
- 模板包含固定版本的本地 Tool Manifest；分发集成测试真实安装 CLI，并在生成项目上验证零警告 Doctor 与 OpenGL Vendor/Renderer Probe。
- Pipeline、ScenePipelineBuilder 与 RenderTargetPool 提供显式只读诊断快照，覆盖 Pass 挂接/执行顺序、逻辑 Surface、Effect owner、Descriptor 分组与活动租约 ID。
- Hosting 聚合入口 `CaptureRenderDiagnostics()` 不暴露 GPU 对象；Runner smoke 在真实 HDR/Stencil/Bloom/Presentation 图上验证快照，GPU 回归通过快照读取 resize/release 后的租约计数。
- `FrameRateSettings` 支持启动及运行时 FPS/UPS/VSync 控制；`0` 表示不限速。`WithFixedUpdateRate` 同时绑定 UPS 与固定 delta，减少确定性配置不一致。
- 可选帧统计默认关闭；启用后零帧分配记录实际 FPS/UPS、引擎 Draw Call、有效 Batch Flush、纹理切换和活跃 Pass，并聚合到 Hosting 诊断快照。
- TextureLibrary、根 RenderTarget 与 Pool 提供稳定显存估算；活动租约、可用缓存和高级自定义资源分项统计，不暴露 GPU Handle。
- `PerformanceBudget` 与低频 Sink 只在采样点创建结构化快照；Runner 支持 `--diagnostics` 控制台摘要和 `--diagnostics-json` JSON Lines。
- Content 包开发期热重载以 `.mygame-assets.json` 指纹为提交标记，后台解码完整修订，并在 Step 与 Draw 之间事务替换 Texture、Sprite、Animation 与包索引；失败保留旧修订。
- Hosting 提供 `EnableContentHotReload`，Runner 提供 `--content-hot-reload`；同修订失败去重，依赖包内容可更新，v1 明确拒绝运行中改变包依赖拓扑。
- Hosting 通过 `UseShaders` 装配自定义 Sprite Shader，并把 `ShaderLibrary` 注入主 Scene 与 Stencil Batch；`uProjection` 自动同步，Context 提供动态 uniform 入口。
- Shader 热重载后台读取双重 SHA-256 稳定快照，在 GL 线程整批编译和原子切换 Program；任一编译/链接失败时全部旧 Handle 保持有效，Runner 提供 `--shader-hot-reload`。
- `MaterialRef`、`ShaderMaterial` 与类型化 `MaterialParameterBlock` 支持同一 Shader 的多套参数；Batch 按材质 Revision 精确 Flush，Program 热替换后自动在新 Handle 上重放 CPU 参数。
- 材质创建与 Shader 热替换候选通过 GL Active Uniform 反射验证名称、类型和数组边界；失败保持旧 Handle，并输出包含文件路径、行号、阶段与逐 Uniform issue 的结构化诊断。
- 独立 ShaderAssets 切片严格解析版本化 `shaders.json`，统一 Program 文件、Material Schema/default 与安全路径；Hosting 自动装配，AssetCompiler/MSBuild 在 C# 编译前复用同一静态校验。
- AssetCompiler 从 `shaders.json` 确定性生成 `GameShaders.ManifestPath/Shaders/Materials/Parameters`；`MaterialParameterRef<T>` 在编译期固定值类型并携带所属 Material，Runner 与外部 NuGet 消费项目均不再手写 Shader 资产名称。
- Gameplay Authoring 提供 `Position/Rotation/Scale`、输入轴/按键边沿、实例级 `Spawn/Destroy/Find`、End Step 后确定性变更提交和暂停感知的轻量 `AlarmId`；可选 `SceneTransformRuntime` 进一步提供 GameInstance Local/World 父子组合、纯挂点、`KeepLocal/KeepWorld` 与安全帧边界同步，`TransformPrefab<TParts>` 支持可复用 `root → weapon → muzzle` 拓扑和强类型具名节点，原有世界坐标 Draw/Collider/查询保持兼容。
- Text Rendering 支持真实 TTF/OTF/TTC、Font Fallback、Grapheme 安全的中文/单词多行换行、Left/Center/Right、MaxLines、Clip/Ellipsis、可复用 `TextLayoutBuffer/PreparedTextLayoutBuffer`、Glyph Atlas 与基础缓存/缺字诊断；World 和 SceneGui 共享同一布局与 Atlas。
- `playgrounds/AirplaneShooter` 提供第一个面向游戏开发者的独立可运行样例：方向键/WASD 移动、按住空格发射、强类型 Sprite 资产和 Alarm 子弹回收。
- Hosting 提供强类型 `SceneRef` 目录、帧边界切换和 persistent 实例保留；Prefab 目录按 `PrefabRef<T>` 注册并在 Build 后冻结。
- `SceneRef<TArgs>`、泛型 `AddScene/StartScene/SwitchScene` 把值类型参数与目标 Scene 编译期绑定；请求快照、注册类型校验和同帧冲突均发生在切换前，Asteroids 已传递 GameOver 生存数据。
- GameInstance 支持可选 Box/Circle Collider、类型化首次/全部碰撞，以及 Scene 区域和半径查询；AirplaneShooter 已迁移为 Prefab 子弹、目标碰撞和 Main/Victory Scene 往返。
- `PrefabRef<T, TArgs>` 通过类型化 `in TArgs` 路径传递方向、速度、半径等构造数据，不装箱且不引入属性字典。
- `playgrounds/Asteroids` 验证旋转推进、参数化 Laser/Asteroid、声明式 Spawn/Wave 时间线、Circle 碰撞和 Main/GameOver 重启；Gameplay Cookbook 已提炼两套 Playground 的常见配方。
- `playgrounds/FlappyBird` 提供一个最小但完整的游戏闭环：拍动/重力、确定性管道生成、Prefab 障碍与计分门、碰撞失败、七段数字 HUD、最佳分数、类型化 GameOver/重开和程序化短音效；全部画面只依赖一个逻辑白色 Sprite。
- 可选 Release 空间查询基准覆盖 100/1,000/10,000 Collider；本机 1,000 Collider 约 0.0201 ms/查询，暂不引入 Spatial Hash。
- `Easing` 提供 21 种归一化曲线；`Tween` 支持标量、位置、颜色和最短弧度角；`Motion` 提供限速追踪与帧率无关的半衰期平滑，全部为无状态零分配 API。
- `GameplayTimeController` 提供 Gameplay/Unscaled 时间域、owner/key 暂停、`(0,8]` TimeScale 和帧快照；暂停冻结默认实例的 Step/Alarm/动画/输入但继续 Draw，Asteroids 以 `P` 键无 UI 验证。
- SceneAggregate 的 Input、Step、Draw 与 DrawGUI 使用可复用快照；预热后 128 实例回归为 0 B/帧，Layer/Depth 有序索引让 Draw 无需重复排序并保持相同 Depth 的加入顺序。
- `GameplayStateMachine<TState>` 提供强类型 Enter/Step/Exit、`Elapsed`、显式 Restart 和回调后确定性切换；冲突与循环快速失败，稳态 Update/Change 为 0 B，AirplaneShooter Target 已用于验证 Spawning → Active。
- `GameplayQueryBuffer<T>` 与 `CountInstances<T>()` 为高频 Find/Collision/Area/Radius 提供 0 B 结果复用；便利数组 API 保持不变，Hosting 遥测按采样 Step 汇总查询次数、候选、命中和耗时，Asteroids 提供 `--diagnostics` 出口。
- `GameplayTag` 为敌人、可受伤对象、拾取物等横切身份提供大小写敏感的强类型引用；Find/Collision/Area/Radius 支持类型与单 Tag 组合，Asteroids 的射击和受击已解除对具体敌人类的依赖。
- `GameplayBehavior<TInstance>` 为 Owner 局部能力提供构造期冻结组合、确定性顺序、创建失败回滚、暂停/时间域继承和 0 B 稳态调度；内置 `LifetimeBehavior`，Asteroids 展示项目自定义 `SpinBehavior`。
- `GameplayHealth` 以有限 float、上下界钳制和零分配 `GameplayHealthChange` 提供通用生命值；`IHasGameplayHealth` 与 Tag 组合区分身份和能力，AirplaneShooter 与 Asteroids 均以 `BecameDepleted` 驱动一次性死亡结果。
- `InstanceRef<T>` 以 Version 7 `InstanceId` 提供强类型弱引用；Scene 与实例 Context 对称支持 O(1) Resolve 和类型安全 Destroy，遵循 Spawn/Destroy 提交、inactive 与 persistent 的现有语义。
- `SimulationClock` 为同一 Step 提供共享 Tick、缩放/非缩放 delta 与累计时间，暂停时只冻结 Gameplay 轴且跨 Scene 保留；`GameplayRandom` 固定 PCG32 v1 bit sequence，支持无偏范围、概率、几何、Choose/Shuffle 和状态恢复，稳态 0 B。
- `LogicalInputRecorder/Recording/Playback` 按模拟 Tick 冻结 Action held/edge、Axis2D 与 fixed delta；Hosting 以 `RecordLogicalInput/ReplayLogicalInput` 装配，要求 delta 逐位一致、协议匹配和完整 Tick 1 流，回放查询保持 0 B。
- `GameplayStateWriter` 提供版本化 FNV-1a 64 显式状态协议；Scene 按稳定加入序号聚合时间、实例内建状态与自定义 contributor，`RecordGameplayState/VerifyGameplayState` 在首次分叉 Tick 抛出结构化诊断。
- `ReplaySession` 将逻辑输入与状态轨迹保存为确定性 `.mgreplay` 二进制文件；Game/Build 身份、fixed delta、组件版本、SHA-256、受限读取和首次分叉验证形成完整开发期回放边界，Asteroids 提供 `--record-replay/--replay` 示例。
- Scene-local `Gameplay Signal` 以结构体载荷和泛型 Handler 提供一对多玩法通知；End Step 后按发布/订阅顺序投递，暂停、失活、销毁和嵌套发布语义明确，稳态 0 B 且不使用反射。Asteroids 用一条击毁通知同时驱动玩家计分与 Spawner 统计。
- `SpawnSequenceBuilder/SpawnSequencePlayer` 提供 Delay、有限 Wave、Once/Loop、并发门控、状态快照和大步长确定性推进；Asteroids 生成器已迁移并保持随机 Prefab 参数由游戏掌握。
- Animation 已贯通 `assets.json`、依赖闭包验证、强类型 Clip/Event 引用、Hosting Catalog、GameInstance Behavior、状态快照和原子 Content Hot Reload；Once/Loop/PingPong、正反向播放和帧事件热身后 Update 为 0 B。
- TextRendering 已贯通真实 Skia TTF/OTF、逻辑 Font/Fallback、Unicode Rune + Grapheme 单行 Layout、TextureLibrary 局部 RGBA 上传、确定性动态 Glyph Atlas、Hosting `TextRuntime` 与 World/SceneGui DrawText；真实字体、Fake GPU 与隐藏 OpenGL smoke 均有覆盖。
- Audio 短音效黄金路径提供声明式 PCM WAV、OpenAL Soft/Silent 后端、Hosting、逻辑 Clip/Bus、代际 Voice、确定性 Priority 抢占、Buffer 共享释放和运行时诊断。
- 独立 TransformHierarchy 阶段 0 提供 generation Handle、Local/World 矩阵、KeepLocal/KeepWorld、循环/Shear/不可逆拒绝、2048 深树迭代传播和 0 B 稳态。
- HTML/CSS/Yoga GUI Compatibility Spike 已比较 Yoga、RmlUi、FairyGUI 与浏览器内核，并固定 NativeAOT、中文、输入、渲染和维护成本的 Go/No-Go 条件。
- Hosting 第一阶段多 Viewport 已落地：一份 Camera/Scene/后处理结果可声明式呈现到多个稳定槽位，支持 Stretch/Contain/Cover、奇数尺寸无缝取整、布局感知 Screen→View→World 转换和 Viewport 诊断；Runner `--mirrored-viewports` 在 HDR 链上验证不重复 Pass。
- Hosting 第二阶段多 Camera 已落地：`RenderViewRef/RenderView`、`UseRenderViews`、独立 Camera/SceneColor/SceneRenderPass、RenderScale、resize、按 View 输入反算与根目标诊断；Runner `--split-cameras` 验证两台真实 Camera。
- 多 View 效果策略已显式化：主 View 由 `UseHdr` 配置；次级 View 默认 Direct，也可独立选择 HDR + Tone Mapping 与可选 Bloom。配置报告额外 Pass/租约，工厂按输入 Surface 尺寸创建目标，次级 View 不承担未声明成本。
- 每 View `SceneLayerFilter.Include/Exclude/All` 已落地，Scene 与主 Stencil 重绘共享过滤语义；名单装配期校验、逐帧 0 B。Runner observer 排除 `MainOnly` 验证小地图式黄金路径。
- 每 View `SceneDrawStatistics` 已接入 Render 诊断：候选访问、选中/绘制、排序比较始终零分配计数，启用 Frame Statistics 后增加遍历/排序/绘制耗时。Scene 已用运行时同步的 Layer 索引消除“层数 × 全场景”重复扫描，且保留同帧切层和稳定 Depth 排序语义；10,000 实例双 View 本机样本由约 1.536 ms 降至 1.185 ms。
- 每个 Render View 已在绘制前执行保守 Camera 可见性剔除：默认从 Sprite Size/Origin 推导，支持自定义 `LocalDrawBounds` 与 `AlwaysVisible` 退出，未知边界 fail-open；旋转、缩放、负缩放与震屏均按实际绘制边界处理。10,000 实例、每 View 可见 20% 的样本把 Draw 回调由 20,000 次降至 4,000 次；无 GPU 假 Batch 的边界检查成本约 0.10 ms，保持 0 B/frame。
- Layer 索引现已在实例加入、切层和改 Depth 时维护稳定有序关系，普通 View Draw 不再重复排序；相同 Depth 保持 Scene 加入顺序，同帧后续 Layer 仍能看到变更。10,000 实例双 View 本机调度由 Layer 索引阶段约 1.185 ms 进一步降至 0.470 ms，排序比较为 0/0。
- Camera 开发体验切片提供 `CameraFollowController`：归一化 Anchor、视口像素 Dead Zone、半衰期平滑、旋转/缩放兼容的世界边界约束、GameInstance 便利重载和零分配叠加震屏；`UseRenderViews` 可声明每个 View 的静态跟随策略，Hosting 惰性创建控制器，Gameplay 仍显式提供和切换运行时目标。
- 独立 `Engine.PerformanceBenchmarks` 已把多 View 性能实验与 DDD 烟测分离：100/1,000/10,000 实例场景同时报告无剔除/剔除耗时、每 View 候选/Draw/拒绝数与分配量，并以确定性计数、零排序和 `0 B/frame` 作为回归守卫。
- GPU 回归新增 `multi-render-view-lifecycle`：真实组合主 View HDR Bloom + Tone Mapping 与 0.75 RenderScale observer Tone Mapping，resize 后验证五个活动租约的精确尺寸，逐 View 释放后验证活动效果和租约归零、缓存全部回到 Pool。
- `games/TheGodTheyMade` 的 Gate 4 工程切片已完成：30 分钟场景状态机组合水闸、湿遗迹、葬礼价值选择、无操作恢复和有限三联壁画；Game 呈现相应灰盒视觉并用程序短音反馈钟/雨/闸/葬礼。33 项无窗口检查覆盖三条完整 108,000 Tick 历史和确定性，Gate 仍等待 5 人外部盲测证据后正式关闭。

## 仍在演进

- MSBuild 已能静态校验声明式 Material Schema，但不会猜测 GLSL；可选离线编译方向已记录并暂缓，精确 Program 编译和 active Uniform 类型继续由运行时 GL Context 复核。
- Stencil 暂时只支持 Circle 与 SpriteAlpha，不支持软边、任意矢量路径或布尔几何运算。
- Atlas 暂不支持旋转、trim 或相同像素内容哈希去重。
- 内容工具包尚未签名或发布到远程 Feed，也没有跨仓库/远程构建缓存。
- 各交互式 VisualTests 仍需人工观察；自动基线已覆盖九条高价值确定性路径，但无显示器 CI 环境尚未固化。
- Hosting v1 仅支持单窗口；已有声明式及强类型参数 Scene 切换和无 UI 暂停策略，但尚无 Scene 栈或后台加载。
- 多 Render View 可选择不同 Scene Layer 与 HDR/Bloom/Tone Mapping Profile；Layer 索引与 Camera 粗剔除已减少每 View 工作量，但仍独立检查、排序与重绘可见实例，Stencil 暂只属于主 View。
- 2D Lighting 当前只有规划，没有运行时代码。路线明确先解决颜色空间，再渐进实现每 View Point Light、几何硬阴影、投射阴影和 Normal/Emission 材质；当前不能把 Spotlight Stencil 或现有 ShaderMaterial 描述成完整光照系统。
- Text 已支持中文/单词多行、对齐、Ellipsis 与动态 Layout Buffer；仍无 HarfBuzz shaping、Font Content/Hot Reload、RichText、IME 或控件树。FairyGUI MonoGame Runtime 仍不能直接视为 Silk.NET/OpenGL 后端；Yoga/RmlUi/FairyGUI 只完成第一轮兼容性决策。
- TransformHierarchy 已接入 Scene/GameInstance、纯挂点与强类型 Transform Prefab；仍不接管扁平实例生命周期、Layer/Depth、Collider 索引或 UI 布局。
- Audio 已能真实播放预加载 PCM WAV；尚无 Streaming Music、OGG/Opus、异步解码、Audio Hot Reload、设备切换或 DSP。
- Animation 尚无过渡状态图、交叉淡化、Blend Tree、骨骼动画、Root Motion 或 Timeline Editor；当前一个 Clip 绑定一个可跨多纹理/Atlas 页的 Sprite。

## Tilemap / World Authoring 第一阶段（已完成）

1. 已建立逻辑 TileSet/TileLayer/TileMap、稀疏 Chunk、负坐标和相机可见 Chunk 渲染，不依赖编辑器 UI。
2. `assets.json`、ContentPackageManager、AssetCompiler 和强类型生成器已贯通 TileSet/TileMap，复用现有 Texture/Sprite/Atlas 生命周期。
3. 已提供 Chunk 内静态碰撞贪心烘焙、复用 Buffer、多 Camera 显式可见边界和无窗口回归。
4. 当前 TileMap 编译产物仍为严格 JSON；Tiled 导入、版本化二进制 Chunk、地图热重载和流式驻留留待后续真实规模驱动。

## 当前真实游戏里程碑：《神意难测》Gate 4 外部盲测

1. 按游戏目录中的 `GATE4_PLAYTEST_PROTOCOL.md` 邀请至少 5 名不了解内部规则的玩家完成流程。
2. 保存 Replay、理解度与卡点记录，验证信仰证据、等待选择、神兽学习和至少两种可完成历史。
3. 未达到 4/5、3/5、4/5 三项理解阈值时优先调整反馈、时间窗与地图，不扩张新系统。

## 后续引擎候选里程碑：Streaming Music

1. 在现有短音效 Voice/Bus/Backend 边界之外增加独立 Music Stream，不把长音频完整解码进内存。
2. 优先完成 OGG/Vorbis、后台解码、环形 PCM Buffer、暂停/恢复/循环与确定性资源释放。
3. 完成后进入 Lighting 0/1，并复用 Tilemap Chunk Revision 和静态碰撞几何作为未来遮挡数据边界。

## 已知限制

- RenderTarget 当前支持 RGBA8/RGBA16F 与可选 Depth24Stencil8，但不支持 MSAA、多 Attachment、sRGB framebuffer 或自动曝光。
- ContentAssets 仍是同步全量解码上传，没有流式驻留、显存预算或 LRU。
- 暂无物理/Spatial Hash、真实音频 Backend、完整编辑器和 AI Bridge 运行时代码。
- Replay v1 全程保留输入帧与状态 contributor，不包含压缩、Checkpoint、状态恢复或跨版本迁移；长会话需由游戏限制录制时长。
- NuGet 漏洞数据源不可访问时可能出现 `NU1900`，不影响使用本地缓存包构建。

相关说明：[Developer Experience Roadmap](DEVELOPER_EXPERIENCE_ROADMAP.md)、[Gameplay Authoring](GAMEPLAY_AUTHORING.md)、[Transform Hierarchy 创作](TRANSFORM_HIERARCHY_AUTHORING.md)、[Spawn/Wave Authoring](SPAWN_WAVE_AUTHORING.md)、[Animation Authoring](ANIMATION_AUTHORING.md)、[Audio Runtime](AUDIO_RUNTIME.md)、[Scene Graph / Transform Hierarchy](SCENE_GRAPH_TRANSFORM_HIERARCHY.md)、[HTML/CSS/Yoga GUI 决策](HTML_CSS_YOGA_GUI_ROADMAP.md)、[Gameplay Signals](GAMEPLAY_SIGNALS.md)、[Gameplay Cooldown](GAMEPLAY_COOLDOWN.md)、[Gameplay Health 与 Damage](GAMEPLAY_HEALTH.md)、[强类型 Instance 引用](INSTANCE_REFERENCES.md)、[确定性 Simulation](DETERMINISTIC_SIMULATION.md)、[逻辑输入回放](LOGICAL_INPUT_REPLAY.md)、[Gameplay 状态 Hash](GAMEPLAY_STATE_HASHING.md)、[可持久化 Replay Bundle](REPLAY_BUNDLES.md)、[Gameplay 状态机](GAMEPLAY_STATE_MACHINE.md)、[Gameplay 查询性能](GAMEPLAY_QUERY_PERFORMANCE.md)、[Camera/Viewport 边界](CAMERA_VIEWPORT_STATUS.md)、[多 View 性能基准](MULTI_VIEW_PERFORMANCE.md)、[Scene 生命周期性能](SCENE_LIFECYCLE_PERFORMANCE.md)、[2D Lighting 路线图](LIGHTING_2D_ROADMAP.md)、[中文 Text Rendering 路线图](TEXT_RENDERING_ROADMAP.md)、[FairyGUI 集成路线图](FAIRYGUI_INTEGRATION_ROADMAP.md)、[可选离线 Shader 编译](OFFLINE_SHADER_COMPILATION.md)、[Game SDK 与项目模板](GAME_SDK_AND_TEMPLATES.md)、[`gameengine doctor`](GAMEENGINE_DOCTOR.md)、[运行时渲染诊断](RUNTIME_RENDER_DIAGNOSTICS.md)、[性能预算与低频遥测](PERFORMANCE_TELEMETRY.md)、[Content 热重载](CONTENT_HOT_RELOAD.md)、[Shader 热重载](SHADER_HOT_RELOAD.md)、[Shader 材质参数块](SHADER_MATERIALS.md)、[声明式 Shader/Material Assets](SHADER_ASSETS.md)、[Engine Hosting](ENGINE_HOSTING.md)、[强类型 Content](STRONGLY_TYPED_CONTENT.md)、[Content Assets](CONTENT_ASSETS.md)、[Texture Atlas](TEXTURE_ATLAS.md)、[可分发内容工具链](CONTENT_PIPELINE_PACKAGES.md)、[`GameEngine.Content.targets`](GAMEENGINE_CONTENT_TARGETS.md)、[动态渲染效果](DYNAMIC_RENDER_EFFECTS.md)、[逻辑 RenderSurface](RENDER_SURFACES.md)、[Presentation](PRESENTATION.md)、[Bloom](BLOOM_EFFECT.md)、[Tone Mapping](TONE_MAPPING.md)、[Stencil 几何](STENCIL_MASK_GEOMETRY.md)、[GPU 像素回归](VISUAL_REGRESSION.md)。

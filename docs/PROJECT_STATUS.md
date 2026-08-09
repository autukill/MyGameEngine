# 项目现状

更新日期：2026-08-09

项目处于 Phase 1.x：最小引擎闭环已经可运行，正在从技术 Demo 向可扩展运行时收口。当前共 45 个 .NET 项目、205 个 C# 文件；除十二个 Feature 模块外，`Engine.Hosting` 作为开发者入口组合根。

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
- 18 个无窗口冒烟/集成项目覆盖领域值、生命周期、渲染状态、资源资产、Atlas、Shader/Material 清单、编译产物、动态 owner、Hosting、Bloom/Tone Mapping/Presentation 设置、池所有权、CLI 诊断和外部分发闭环。
- `Engine.Testing.Visual` 提供隐藏固定步长窗口、RGBA8 framebuffer 捕获、PNG 编解码和像素容差比较。
- 自动 GPU 回归覆盖 Sprite、Shader Program 成功/失败替换、真实圆形/Sprite Alpha Stencil、动态 resize、Bloom、双 Bloom Surface 串联，以及 HDR、LDR GUI、ACES/Reinhard、曝光、resize 和释放，共 17 个 checkpoint。
- AssetCompiler 可打包为 `gameengine-assets` .NET Tool；ContentPipeline NuGet 包通过 `buildTransitive` 为外部项目提供内置编译器、增量 Build 与 Publish 接入。
- 内容构建从编译后 Manifest 图生成 `GameAssets.Packages/Sprites/Textures`；Atlas 内部页和已吞并源 Texture 不进入公开 API，标识符冲突在构建期失败。
- 包集成测试使用临时本地 Feed，真实验证 Tool 安装、带空格路径、Debug/Release、缓存命中与 Publish 边界。
- `MyGameEngine.GameSdk` 已聚合 14 个正式运行时程序集并声明 Silk.NET/SkiaSharp 依赖；包内不包含源码项目依赖、符号或仓库绝对路径。
- `MyGameEngine.Templates` 已提供 `dotnet new mygameengine-game`；生成项目包含 Hosting、GameInstance、声明式 WebP 内容与强类型引用，不包含 `ProjectReference`。
- 分发集成测试使用隔离 CLI Home、NuGet 包目录和临时本地 Feed，真实验证 Pack、模板安装、仓库外 Restore/Build、三帧 smoke run 与 Publish。
- `MyGameEngine.Cli` 提供 `gameengine doctor`：默认执行无图形副作用的项目与内容诊断，`--probe-opengl` 显式创建隐藏 OpenGL 3.3 Context。
- 模板包含固定版本的本地 Tool Manifest；分发集成测试真实安装 CLI，并在生成项目上验证零警告 Doctor 与 OpenGL Vendor/Renderer Probe。
- Pipeline、ScenePipelineBuilder 与 RenderTargetPool 提供显式只读诊断快照，覆盖 Pass 挂接/执行顺序、逻辑 Surface、Effect owner、Descriptor 分组与活动租约 ID。
- Hosting 聚合入口 `CaptureRenderDiagnostics()` 不暴露 GPU 对象；Runner smoke 在真实 HDR/Stencil/Bloom/Presentation 图上验证快照，GPU 回归通过快照读取 resize/release 后的租约计数。
- `FrameRateSettings` 支持启动及运行时 FPS/UPS/VSync 控制；`0` 表示不限速，固定模拟 delta 与真实调度频率保持独立。
- 可选帧统计默认关闭；启用后零帧分配记录实际 FPS/UPS、引擎 Draw Call、有效 Batch Flush、纹理切换和活跃 Pass，并聚合到 Hosting 诊断快照。
- TextureLibrary、根 RenderTarget 与 Pool 提供稳定显存估算；活动租约、可用缓存和高级自定义资源分项统计，不暴露 GPU Handle。
- `PerformanceBudget` 与低频 Sink 只在采样点创建结构化快照；Runner 支持 `--diagnostics` 控制台摘要和 `--diagnostics-json` JSON Lines。
- Content 包开发期热重载以 `.mygame-assets.json` 指纹为提交标记，后台解码完整修订，并在 Step 与 Draw 之间事务替换 Texture、Sprite 与包索引；失败保留旧修订。
- Hosting 提供 `EnableContentHotReload`，Runner 提供 `--content-hot-reload`；同修订失败去重，依赖包内容可更新，v1 明确拒绝运行中改变包依赖拓扑。
- Hosting 通过 `UseShaders` 装配自定义 Sprite Shader，并把 `ShaderLibrary` 注入主 Scene 与 Stencil Batch；`uProjection` 自动同步，Context 提供动态 uniform 入口。
- Shader 热重载后台读取双重 SHA-256 稳定快照，在 GL 线程整批编译和原子切换 Program；任一编译/链接失败时全部旧 Handle 保持有效，Runner 提供 `--shader-hot-reload`。
- `MaterialRef`、`ShaderMaterial` 与类型化 `MaterialParameterBlock` 支持同一 Shader 的多套参数；Batch 按材质 Revision 精确 Flush，Program 热替换后自动在新 Handle 上重放 CPU 参数。
- 材质创建与 Shader 热替换候选通过 GL Active Uniform 反射验证名称、类型和数组边界；失败保持旧 Handle，并输出包含文件路径、行号、阶段与逐 Uniform issue 的结构化诊断。
- 独立 ShaderAssets 切片严格解析版本化 `shaders.json`，统一 Program 文件、Material Schema/default 与安全路径；Hosting 自动装配，AssetCompiler/MSBuild 在 C# 编译前复用同一静态校验。
- AssetCompiler 从 `shaders.json` 确定性生成 `GameShaders.ManifestPath/Shaders/Materials/Parameters`；`MaterialParameterRef<T>` 在编译期固定值类型并携带所属 Material，Runner 与外部 NuGet 消费项目均不再手写 Shader 资产名称。
- Gameplay Authoring 第一切片提供 `Position/Rotation/Scale`、输入轴/按键边沿、实例级 `Spawn/Destroy/Find`、End Step 后确定性变更提交和暂停感知的轻量 `AlarmId`；模板展示 WASD、Space 发射与生命周期自动销毁。

## 仍在演进

- MSBuild 已能静态校验声明式 Material Schema，但不会猜测 GLSL；可选离线编译方向已记录并暂缓，精确 Program 编译和 active Uniform 类型继续由运行时 GL Context 复核。
- Stencil 暂时只支持 Circle 与 SpriteAlpha，不支持软边、任意矢量路径或布尔几何运算。
- Atlas 暂不支持旋转、trim 或相同像素内容哈希去重。
- 内容工具包尚未签名或发布到远程 Feed，也没有跨仓库/远程构建缓存。
- 各交互式 VisualTests 仍需人工观察；自动基线已覆盖八条高价值确定性路径，但无显示器 CI 环境尚未固化。
- SceneAggregate 的 ToList、LINQ 过滤和排序仍会产生每帧分配。
- Hosting v1 仅支持单窗口与单初始 Scene，尚无 Scene 切换栈、暂停策略或后台加载。

## 下一里程碑：Gameplay Scene 与实例复用体验

1. 把 Scene 定义、注册与切换从单个 `Program.cs` 回调收敛为强类型目录和明确生命周期。
2. 为常用实例建立可复用 Factory/Prefab 边界，避免把 GPU 资源或全局容器放进玩法对象。
3. 评估基础碰撞形状、空间查询与 Gameplay Cookbook；继续降低 SceneAggregate 每帧列表和排序分配。

## 已知限制

- RenderTarget 当前支持 RGBA8/RGBA16F 与可选 Depth24Stencil8，但不支持 MSAA、多 Attachment、sRGB framebuffer 或自动曝光。
- ContentAssets 仍是同步全量解码上传，没有流式驻留、显存预算或 LRU。
- 暂无物理/Spatial Hash、音频、完整编辑器和 AI Bridge 运行时代码。
- NuGet 漏洞数据源不可访问时可能出现 `NU1900`，不影响使用本地缓存包构建。

相关说明：[Developer Experience Roadmap](DEVELOPER_EXPERIENCE_ROADMAP.md)、[Gameplay Authoring](GAMEPLAY_AUTHORING.md)、[可选离线 Shader 编译](OFFLINE_SHADER_COMPILATION.md)、[Game SDK 与项目模板](GAME_SDK_AND_TEMPLATES.md)、[`gameengine doctor`](GAMEENGINE_DOCTOR.md)、[运行时渲染诊断](RUNTIME_RENDER_DIAGNOSTICS.md)、[性能预算与低频遥测](PERFORMANCE_TELEMETRY.md)、[Content 热重载](CONTENT_HOT_RELOAD.md)、[Shader 热重载](SHADER_HOT_RELOAD.md)、[Shader 材质参数块](SHADER_MATERIALS.md)、[声明式 Shader/Material Assets](SHADER_ASSETS.md)、[Engine Hosting](ENGINE_HOSTING.md)、[强类型 Content](STRONGLY_TYPED_CONTENT.md)、[Content Assets](CONTENT_ASSETS.md)、[Texture Atlas](TEXTURE_ATLAS.md)、[可分发内容工具链](CONTENT_PIPELINE_PACKAGES.md)、[`GameEngine.Content.targets`](GAMEENGINE_CONTENT_TARGETS.md)、[动态渲染效果](DYNAMIC_RENDER_EFFECTS.md)、[逻辑 RenderSurface](RENDER_SURFACES.md)、[Presentation](PRESENTATION.md)、[Bloom](BLOOM_EFFECT.md)、[Tone Mapping](TONE_MAPPING.md)、[Stencil 几何](STENCIL_MASK_GEOMETRY.md)、[GPU 像素回归](VISUAL_REGRESSION.md)。

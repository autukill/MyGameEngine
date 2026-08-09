# 项目现状

更新日期：2026-08-09

项目处于 Phase 1.x：最小引擎闭环已经可运行，正在从技术 Demo 向可扩展运行时收口。当前共 38 个 .NET 项目、160 个 C# 文件；除十一个 Feature 模块外，`Engine.Hosting` 作为开发者入口组合根。

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
- 15 个无窗口冒烟/集成项目覆盖领域值、生命周期、渲染状态、资源资产、Atlas、编译产物、动态 owner、Hosting、Bloom/Tone Mapping/Presentation 设置和池所有权。
- `Engine.Testing.Visual` 提供隐藏固定步长窗口、RGBA8 framebuffer 捕获、PNG 编解码和像素容差比较。
- 自动 GPU 回归覆盖 Sprite、真实圆形/Sprite Alpha Stencil、动态 resize、Bloom、双 Bloom Surface 串联，以及 HDR、LDR GUI、ACES/Reinhard、曝光、resize 和释放，共 14 个 checkpoint。
- AssetCompiler 可打包为 `gameengine-assets` .NET Tool；ContentPipeline NuGet 包通过 `buildTransitive` 为外部项目提供内置编译器、增量 Build 与 Publish 接入。
- 内容构建从编译后 Manifest 图生成 `GameAssets.Packages/Sprites/Textures`；Atlas 内部页和已吞并源 Texture 不进入公开 API，标识符冲突在构建期失败。
- 包集成测试使用临时本地 Feed，真实验证 Tool 安装、带空格路径、Debug/Release、缓存命中与 Publish 边界。

## 仍在演进

- `ShaderRef` 与 ShaderLibrary 已具备，但缺少统一动态 uniform API。
- Stencil 暂时只支持 Circle 与 SpriteAlpha，不支持软边、任意矢量路径或布尔几何运算。
- Atlas 暂不支持旋转、trim 或相同像素内容哈希去重。
- 内容工具包尚未签名或发布到远程 Feed，也没有跨仓库/远程构建缓存。
- 各交互式 VisualTests 仍需人工观察；自动基线已覆盖五条高价值确定性路径，但无显示器 CI 环境尚未固化。
- SceneAggregate 的 ToList、LINQ 过滤和排序仍会产生每帧分配。
- Hosting v1 仅支持单窗口与单初始 Scene，尚无 Scene 切换栈、暂停策略或后台加载。

## 下一里程碑：项目模板与开发环境诊断

1. 提供 `dotnet new mygameengine-game`，默认接入 Hosting、强类型 Content 与内容构建。
2. 增加 `gameengine doctor`，检查 SDK、OpenGL、内容编译器与输出路径。
3. 增加 RenderSurface、Effect owner、Pass 与 RenderTarget 租约只读诊断快照。
4. 固化无显示器 CI 的软件 OpenGL 环境。

## 已知限制

- RenderTarget 当前支持 RGBA8/RGBA16F 与可选 Depth24Stencil8，但不支持 MSAA、多 Attachment、sRGB framebuffer 或自动曝光。
- ContentAssets 仍是同步全量解码上传，没有流式驻留、显存预算或 LRU。
- 暂无物理/Spatial Hash、音频、完整编辑器和 AI Bridge 运行时代码。
- NuGet 漏洞数据源不可访问时可能出现 `NU1900`，不影响使用本地缓存包构建。

相关说明：[Developer Experience Roadmap](DEVELOPER_EXPERIENCE_ROADMAP.md)、[Engine Hosting](ENGINE_HOSTING.md)、[强类型 Content](STRONGLY_TYPED_CONTENT.md)、[Content Assets](CONTENT_ASSETS.md)、[Texture Atlas](TEXTURE_ATLAS.md)、[可分发内容工具链](CONTENT_PIPELINE_PACKAGES.md)、[`GameEngine.Content.targets`](GAMEENGINE_CONTENT_TARGETS.md)、[动态渲染效果](DYNAMIC_RENDER_EFFECTS.md)、[逻辑 RenderSurface](RENDER_SURFACES.md)、[Presentation](PRESENTATION.md)、[Bloom](BLOOM_EFFECT.md)、[Tone Mapping](TONE_MAPPING.md)、[Stencil 几何](STENCIL_MASK_GEOMETRY.md)、[GPU 像素回归](VISUAL_REGRESSION.md)。

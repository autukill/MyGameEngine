# 项目现状

更新日期：2026-08-09

项目处于 Phase 1.x：最小引擎闭环已经可运行，正在从技术 Demo 向可扩展运行时收口。当前共 34 个 .NET 项目、142 个 C# 文件，Feature 拆分为 Bloom、Camera、ContentAssets、RenderPipeline、SceneSystem、Sprites、StencilMasking、TextureAssets、TextureAtlas、ToneMapping 十个独立模块。

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
- Runner 的 SpotlightController 只声明 Stencil；SceneBloomController 独立声明完整 Scene Bloom。
- 独立 Bloom 描述符、三目标 Bright/Ping/Pong 效果链、五采样分离高斯与分辨率重建协议。
- 纯逻辑 `RenderSurfaceKey`、Factory Plan、根 Surface 注册、效果输出解析与稳定拓扑依赖图。
- 动态效果结构变化采用全图原子重建；缺失输入、重复输出、循环、创建或挂接失败均保留旧图。
- RenderTarget 支持 RGBA8 与 RGBA16F；Pool 按完整格式隔离复用，逻辑 Surface 同时校验存储格式与 Linear/Display 编码。
- 独立 Tone Mapping 描述符、ACES/Reinhard、Exposure/Gamma 原地更新，以及 Scene + Bloom HDR 合并后输出 RGBA8。
- Runner 已迁移为 RGBA16F Scene → HDR Bloom → ACES Tone Mapping，并保持 Stencil、resize 与释放行为。
- 12 个无窗口冒烟项目覆盖领域值、生命周期、渲染状态、资源资产、Atlas、编译产物、动态 owner、Bloom/Tone Mapping 设置和池所有权。
- `Engine.Testing.Visual` 提供隐藏固定步长窗口、RGBA8 framebuffer 捕获、PNG 编解码和像素容差比较。
- 自动 GPU 回归覆盖 Sprite、Stencil、动态 resize、Bloom、双 Bloom Surface 串联，以及 HDR 的 ACES/Reinhard、曝光、resize 和释放，共 13 个 checkpoint。
- AssetCompiler 可打包为 `gameengine-assets` .NET Tool；ContentPipeline NuGet 包通过 `buildTransitive` 为外部项目提供内置编译器、增量 Build 与 Publish 接入。
- 包集成测试使用临时本地 Feed，真实验证 Tool 安装、带空格路径、Debug/Release、缓存命中与 Publish 边界。

## 仍在演进

- `ShaderRef` 与 ShaderLibrary 已具备，但缺少统一动态 uniform API。
- Stencil 几何仍使用白纹理 Quad；尚未增加任意矢量路径或专用圆形网格。
- Atlas 暂不支持旋转、trim 或相同像素内容哈希去重。
- 内容工具包尚未签名或发布到远程 Feed，也没有跨仓库/远程构建缓存。
- 各交互式 VisualTests 仍需人工观察；自动基线已覆盖五条高价值确定性路径，但无显示器 CI 环境尚未固化。
- SceneAggregate 的 ToList、LINQ 过滤和排序仍会产生每帧分配。

## 下一里程碑：显式 Present 节点与 HDR/LDR UI 边界

1. 把当前 Runtime 的 `CompositeSources` 副作用收口为显式 Present 描述符/节点，使最终屏幕依赖也进入逻辑图。
2. 定义 HDR World → Tone Mapping → LDR UI 的固定组合顺序，避免 UI 参与曝光与 Bloom。
3. 为 Present 顺序、空输出、UI resize 和效果释放建立 GPU 基线。
4. 固化无显示器 CI 的软件 OpenGL 环境，再评估自动曝光与亮度直方图。

## 已知限制

- RenderTarget 当前支持 RGBA8/RGBA16F 与可选 Depth24Stencil8，但不支持 MSAA、多 Attachment、sRGB framebuffer 或自动曝光。
- ContentAssets 仍是同步全量解码上传，没有流式驻留、显存预算或 LRU。
- 暂无物理/Spatial Hash、音频、完整编辑器和 AI Bridge 运行时代码。
- NuGet 漏洞数据源不可访问时可能出现 `NU1900`，不影响使用本地缓存包构建。

相关说明：[Content Assets](CONTENT_ASSETS.md)、[Texture Atlas](TEXTURE_ATLAS.md)、[可分发内容工具链](CONTENT_PIPELINE_PACKAGES.md)、[`GameEngine.Content.targets`](GAMEENGINE_CONTENT_TARGETS.md)、[动态渲染效果](DYNAMIC_RENDER_EFFECTS.md)、[逻辑 RenderSurface](RENDER_SURFACES.md)、[Bloom](BLOOM_EFFECT.md)、[Tone Mapping](TONE_MAPPING.md)、[GPU 像素回归](VISUAL_REGRESSION.md)。

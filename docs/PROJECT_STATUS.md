# 项目现状

更新日期：2026-08-08

项目处于 Phase 1.x：最小引擎闭环已经可运行，正在从技术 Demo 向可扩展运行时收口。当前共 28 个 .NET 项目、120 个 C# 文件，Feature 拆分为 Camera、ContentAssets、RenderPipeline、SceneSystem、Sprites、StencilMasking、TextureAssets、TextureAtlas 八个独立模块。

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
- Runner 的 SpotlightController 已移除直接 Stencil Pass 引用，Stencil + Bloom 由效果工厂装配。
- 10 个无窗口冒烟项目覆盖领域值、生命周期、渲染状态、资源资产、Atlas、编译产物、动态 owner 和池所有权。
- `Engine.Testing.Visual` 提供隐藏固定步长窗口、RGBA8 framebuffer 捕获、PNG 编解码和像素容差比较。
- 自动 GPU 回归覆盖 Sprite 原点/变换、Stencil 两/一/零 owner 生命周期与活跃效果 resize，并生成可诊断差异产物。

## 仍在演进

- `ShaderRef` 与 ShaderLibrary 已具备，但缺少统一动态 uniform API。
- Stencil 几何仍使用白纹理 Quad；尚未增加任意矢量路径或专用圆形网格。
- Bloom 是单 Pass 9-tap 近似，尚未拆为独立效果描述符和 ping-pong 高斯链。
- Atlas 暂不支持旋转、trim 或相同像素内容哈希去重。
- AssetCompiler 尚未打包为独立 .NET Tool/NuGet build package，也没有跨仓库缓存。
- Bloom 与各交互式 VisualTests 仍需人工观察；自动基线目前只覆盖三条高价值确定性路径。
- SceneAggregate 的 ToList、LINQ 过滤和排序仍会产生每帧分配。

## 下一里程碑：可分发内容工具链

1. 将 AssetCompiler 发布为可复用的 .NET Tool 与 NuGet build package。
2. 为跨仓库内容构建增加稳定的工具版本锁定和缓存键。
3. 增加独立 Bloom 描述符与水平/垂直 ping-pong 链，再建立专属 GPU 基线。
4. 为无显示器 CI 选择并验证软件 OpenGL 执行环境。

## 已知限制

- v1 RenderTarget 只支持 RGBA8 与可选 Depth24Stencil8，不支持 HDR、MSAA 或多 Attachment。
- ContentAssets 仍是同步全量解码上传，没有流式驻留、显存预算或 LRU。
- 暂无物理/Spatial Hash、音频、完整编辑器和 AI Bridge 运行时代码。
- NuGet 漏洞数据源不可访问时可能出现 `NU1900`，不影响使用本地缓存包构建。

相关说明：[Content Assets](CONTENT_ASSETS.md)、[Texture Atlas](TEXTURE_ATLAS.md)、[`GameEngine.Content.targets`](GAMEENGINE_CONTENT_TARGETS.md)、[动态渲染效果](DYNAMIC_RENDER_EFFECTS.md)、[GPU 像素回归](VISUAL_REGRESSION.md)。

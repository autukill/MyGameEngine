# 项目现状

更新日期：2026-08-08

使用文档按功能切片渐进维护，入口见 [文档索引](README.md)；声明式资产包与离线构建详见 [Content Assets 使用指南](CONTENT_ASSETS.md) 和 [Texture Atlas 使用指南](TEXTURE_ATLAS.md)。

## 当前阶段

项目处于 Phase 1.x：最小引擎闭环已经可运行，核心模块边界基本稳定，正在从技术 Demo 向可扩展运行时收口。

当前代码规模为 26 个 .NET 项目、102 个 C# 文件。Feature 已拆为 Camera、ContentAssets、RenderPipeline、SceneSystem、Sprites、StencilMasking、TextureAssets、TextureAtlas 八个独立 module，并新增离线 AssetCompiler 及其测试项目。

## 已完成

- Silk.NET 窗口、OpenGL 3.3 Core、Depth24/Stencil8 framebuffer。
- SpriteBatch 动态 VBO/静态 EBO，纹理与 Blend/Depth/Shader 状态打断。
- SceneAggregate、GameInstance、Layer、Background 与领域事件。
- GameMaker 风格实例事件和统一 InputSystem。
- 逻辑 SpriteRef、SpriteLibrary、多帧 UV、原点、动画推进和 DrawSprite/DrawSpriteExt/Stretched API。
- 逻辑 TextureRef、TextureLibrary、PNG/静态 WebP 解码、采样预设、content-root 清单加载与 GPU 句柄所有权。
- 版本化 `assets.json`、依赖拓扑、共享包引用计数、事务回滚，以及 Single/Grid/多图片 Frames Sprite 装配。
- Sprite 逐帧保存 `TextureRef + PixelRectI`，同一动画可跨独立纹理，为 Atlas 重映射建立稳定边界。
- 确定性离线 TextureAtlas：Shelf 多页、padding、edge extrude、采样分组、页面裁切和超大帧旁路。
- AssetCompiler 将源清单编译为标准运行时包；依赖复制、跨页动画和 ContentPackageManager 直接加载均已验证。
- Camera2D 与窗口 resize 传播。
- RenderTarget、RenderPass DAG、Stencil、Bloom、Viewport 合成。
- Runner 业务逻辑实例化：聚光灯输入由 `SpotlightController` 管理，Program 仅负责组合根装配。
- GPU 资源关闭链路：Window Closing -> Pipeline/Pass/RT/Shader/Batch -> GraphicsDevice。
- 10 个无窗口冒烟项目覆盖值对象、聚合、生命周期、输入沿事件、渲染状态、Sprite、PNG/WebP、内容包、Atlas 像素与编译产物。

## 仍在演进

- `ShaderRef`、`ShaderLibrary` 和通用 `ShaderProgram` 已具备，但缺少 Runner 中的自定义实例 Shader 示例与统一动态 uniform API。
- Atlas 暂不支持旋转、trim、内容哈希去重、增量缓存或自动 MSBuild/发布集成。
- `SceneRenderContext` 已标记过时；生产路径已使用 `SceneAggregate + SceneRenderPass`，后续应删除兼容类型。
- VisualTests 已实例事件化，但仍依赖人工观察，没有自动 GPU 回归基线。
- SceneAggregate 的多次 `ToList`、LINQ 过滤与排序仍会产生每帧分配，“零 GC”尚未覆盖整个引擎循环。

## 下一里程碑：动态效果装配

下一阶段以一个垂直切片完成：

1. 在 Domain 定义 `RenderEffectRequested`/释放事件和稳定的效果描述符，不携带 GL 对象。
2. 增加 `ScenePipelineBuilder`，在 Step 边界消费事件并对 Pass 图做差量更新。
3. 增加 `RenderTargetPool`，按尺寸/格式复用 RT。
4. 用 `effect kind -> owner InstanceId set` 管理共享效果；owner 集合为空时才移除 Pass 并归还 RT。
5. 先将 StencilMasking 的直接 Pass 引用迁移为该事件路径，再扩展 Bloom 等效果。

## 已知限制

- Bloom 是单 Pass 9-tap 模糊，不是水平/垂直 ping-pong 高斯链。
- Pipeline 拓扑与 RT 生命周期仍由组合根手动装配。
- 暂无物理/Spatial Hash、音频、完整编辑器与 AI Bridge 的运行时代码。
- ContentAssets v1 全量同步解码上传，不保留 CPU 像素，也没有流式驻留、显存预算或 LRU。
- NuGet 漏洞数据源不可访问时会出现 `NU1900`，不影响使用本地缓存包构建。

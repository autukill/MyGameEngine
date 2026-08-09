# Developer Experience Roadmap

本路线聚焦“让游戏开发者更容易正确使用引擎”，暂不扩展 UI 系统。优先消除组合根样板、字符串资产名和难以诊断的装配错误，同时保留底层 RenderPass、Factory 与资源 API 作为高级逃生口。

## 阶段 1：Engine Hosting 与默认 2D 渲染预设（已实现）

目标是把普通游戏入口从手工创建 Window、Shader、Batch、Library、RenderTarget、Pipeline 和 Factory，收敛为声明式启动代码。

计划能力：

- `GameApplicationBuilder`：配置窗口、内容包、初始 Scene 和默认渲染预设。
- `GameApplication`：统一接管 Load、Step、Draw、Resize、Closing 与异常清理。
- `Default2DRendererOptions`：按需启用 HDR/Tone Mapping、Bloom、Stencil 和 SceneGui。
- `Default2DGameContext`：向 Scene 装配回调提供强类型 Scene、Content、Texture、Sprite、Camera 和渲染扩展入口。
- 资源所有权：固定 Builder → Pipeline → Pool → RenderTarget → Content/Library → Batch/Shader 的释放顺序，初始化失败时逆序回滚。
- 高级逃生口：仍允许注册自定义 `IRenderEffectFactory`、根 Surface 和 RenderPass。

验收结果：Runner 已使用 Hosting API，不再维护静态 GPU 字段或窗口回调；默认预设保留 Spotlight、HDR Bloom、Tone Mapping、resize、ESC 和关闭释放行为。配置验证、默认 owner 事件顺序与逆序资源清理已有无窗口测试。

## 阶段 2：强类型 Content 访问

从 `assets.json` 或编译产物生成稳定的 C# 标识：

```csharp
Sprites.PlayerIdle
Textures.WorldTiles
Packages.SharedPrimitives
```

目标是把资源拼写错误从运行时提前到编译期。生成器只产生逻辑 `SpriteRef`、`TextureRef` 和包 ID，不包含 GPU 句柄，也不改变 ContentPackageManager 的生命周期。

## 阶段 3：项目模板与命令行体验

- 提供 `dotnet new mygameengine-game` 最小项目模板。
- 默认包含 `Assets/assets.json`、首个 Scene、实例示例和内容构建配置。
- 增加 `gameengine doctor`，检查 SDK、OpenGL、内容工具链和输出目录。
- 给出 Debug、Release、Publish 的可复制命令。

## 阶段 4：诊断与可观察性

- 输出 RenderSurface 依赖图、Effect owner、Pass 顺序和 RenderTarget 租约快照。
- 为未知资源、缺失 Factory、格式不匹配和依赖循环提供带上下文的诊断。
- 可选帧统计：Draw Call、Flush、纹理切换、活跃 Pass 和显存估算。
- 诊断 API 默认只读，不改变运行时状态。

## 阶段 5：开发期热重载

- 优先支持内容包和 Shader 热重载，再考虑代码热重载。
- 失败时继续使用上一份有效资源，不破坏当前 Scene。
- 与 Content 指纹、Atlas 原子替换和 Hosting 生命周期共享同一所有权边界。

## 设计约束

- 不引入全局 Service Locator。
- 默认预设负责常用路径，但不隐藏逻辑 RenderSurface 和资源所有权。
- 可选 Feature 未启用时不创建对应 Shader、Factory 或 RenderTarget。
- Host 不把 GPU 对象注入领域描述符或 GameInstance 状态。
- 所有便捷 API 必须能映射回现有底层 API，避免形成第二套渲染实现。

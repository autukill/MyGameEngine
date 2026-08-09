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

## 阶段 2：强类型 Content 访问（已实现）

从 `assets.json` 或编译产物生成稳定的 C# 标识：

```csharp
GameAssets.Sprites.PlayerIdle
GameAssets.Textures.WorldTiles
GameAssets.Packages.SharedPrimitives
```

目标是把资源拼写错误从运行时提前到编译期。生成器只产生逻辑 `SpriteRef`、`TextureRef` 和 `ContentPackageRef`，不包含 GPU 句柄，也不改变 ContentPackageManager 的生命周期。

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

当前验收：Content 包已使用编译元数据轮询和去抖，后台完成 Manifest 图校验、图片解码与 Sprite 规范化；Texture/Sprite/包索引在 Step 与 Draw 之间事务切换，失败保留旧修订。自定义 Sprite Shader 支持安全根文件注册、稳定源码快照、整批 Program 原子替换和驱动错误诊断；投影同步覆盖主 Scene 与 Stencil 重绘。Runner 提供 `--content-hot-reload` 与 `--shader-hot-reload`。阶段 5 下一步评估材质参数块与代码热重载边界。

## 设计约束

- 不引入全局 Service Locator。
- 默认预设负责常用路径，但不隐藏逻辑 RenderSurface 和资源所有权。
- 可选 Feature 未启用时不创建对应 Shader、Factory 或 RenderTarget。
- Host 不把 GPU 对象注入领域描述符或 GameInstance 状态。
- 所有便捷 API 必须能映射回现有底层 API，避免形成第二套渲染实现。

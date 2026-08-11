# Engine Hosting 与默认 2D 启动套件

`Engine.Hosting` 为普通游戏提供组合根，不替代底层 Feature API。它统一管理窗口事件、默认 2D 渲染器、内容包、Scene 帧循环、resize 和资源释放，让入口代码只保留游戏配置。

## 最小 LDR 游戏

```csharp
using MyGame.Content;

using var game = GameApplication
    .Create(EngineWindowOptions.Default)
    .UseDefault2DRenderer(renderer => renderer
        .UseContent(GameAssets.Packages.Root))
    .ConfigureScene("MainScene", context =>
    {
        context.Scene.Add(new Player(GameAssets.Sprites.PlayerIdle));
    })
    .Build();

game.Run();
```

默认路径创建 RGBA8 SceneColor、透明 SceneGui、Presentation 终端、Sprite/Texture/Content Library、Camera、RenderTargetPool 和帧循环。未启用 HDR、Bloom 或 Stencil 时，不创建对应 Shader、Factory 或临时目标。

## HDR、Bloom 与 Stencil

```csharp
.UseDefault2DRenderer(renderer => renderer
    .UseContent(GameAssets.Packages.Root)
    .UseHdr(
        ToneMappingSettings.Default,
        new BloomSettings(
            threshold: 0.35f,
            intensity: 1.25f,
            blurRadius: 1f,
            iterations: 2,
            resolution: BloomResolution.Half))
    .EnableStencilMasking())
```

`UseHdr` 把 SceneColor 改为 RGBA16F/Linear，并由内置 owner 声明 Tone Mapping 与最终 Presentation。传入 Bloom 设置时，同一 owner 先声明 HDR Bloom，再让 Tone Mapping 消费 Glow。`EnableStencilMasking` 注册专用 Shader 与 Factory，但具体遮罩仍由游戏中的 GameInstance 请求。

`UseContent(ContentPackageRef)` 默认从 `AssetsCompiled` 加载，并校验生成引用中的包 ID。需要动态路径时仍可使用 `UseContent(packagesRoot, manifestPath)`；强类型生成规则见[强类型 Content 引用](STRONGLY_TYPED_CONTENT.md)。

## `UseContentCatalog` 与 Scene 级内容

当游戏按首页、世界地图、关卡等边界拆包时，使用 `UseContentCatalog` 只配置编译包目录，再在 Scene 声明上绑定需要的包：

```csharp
using var game = GameApplication
    .Create(options)
    .UseDefault2DRenderer(renderer => renderer
        .UseContentCatalog())
    .AddScene(
        GameScenes.Home,
        GameAssets.Packages.GameHome,
        context => HomeScene.Configure(context))
    .AddScene(
        GameScenes.World,
        GameAssets.Packages.GameWorld,
        context => WorldScene.Configure(context))
    .AddScene(
        GameScenes.EmptyDebug,
        context => DebugScene.Configure(context))
    .StartScene(GameScenes.Home)
    .Build();
```

`UseContentCatalog(packagesRoot = "AssetsCompiled")` 只建立 `ContentPackageManager` 的安全根目录。它不会扫描目录、不会加载聚合根，也不会预先上传 Texture。Hosting 在初始 Scene 启动或 Scene 切换时读取其 `ContentPackageRef`，加载目标包及传递依赖，并把本次租约放入 `context.Content`。无包 Scene 的 `context.Content` 为 `null`。

| 模式 | 何时加载 | 持有时间 | 适用场景 |
| --- | --- | --- | --- |
| `UseContent(package)` | Runtime 初始化 | 直到应用关闭 | 小型游戏、共享资产占主体、VisualTests |
| `UseContentCatalog()` + `AddScene(scene, package, ...)` | Scene 初次进入或切换前 | 当前 Scene 使用期间 | 多 Scene 游戏、较大的独立场景资产 |

两种模式互斥；声明了 Scene 包却没有调用 `UseContentCatalog` 会在构建 Hosting Plan 时直接失败。当前 Content Hot Reload 只支持 `UseContent` 常驻模式。完整术语、切换顺序与失败边界见 [Scene 级 Content Package 生命周期](SCENE_CONTENT_LIFECYCLE.md)。

开发期可以继续调用 `EnableContentHotReload(options)`。Host 轮询编译指纹、在后台解码新修订，并固定在 Step 与 Draw 之间提交；失败时保留旧资源。完整协议见 [Content 包开发期热重载](CONTENT_HOT_RELOAD.md)。

游戏自定义 Sprite Shader 可通过 `UseShaders(root, definitions)` 注册，随后由 `ShaderRef` 选择。`EnableShaderHotReload(options)` 会在后台读取稳定源码快照，并在相同 Step/Draw 边界整批编译替换；失败保留旧 Program。Context 暴露 `Shaders` 供高级 uniform 设置。详见 [自定义 Sprite Shader 与开发期热重载](SHADER_HOT_RELOAD.md)。

需要由构建系统统一检查 Shader、Material Schema 与默认值时，推荐改用 `UseShaderAssets(GameShaders.ManifestPath)`。Host 会按清单装配 Program 和 Material，游戏代码直接使用生成的 `GameShaders.Materials` 与 `GameShaders.Parameters`；`context.GetMaterial(name)` 仍保留为动态名称逃生口。该模式与命令式 `UseShaders` 互斥。格式与构建集成见 [声明式 Shader/Material Assets](SHADER_ASSETS.md)。

SceneGui 默认开启；不需要 Draw GUI 路径时可调用 `DisableSceneGui()`，避免创建对应 RenderTarget 和 Pass。

## 单 Camera 多 Viewport

同一份最终世界画面可以零重复 Scene/后处理地呈现到多个槽位：

```csharp
.UseDefault2DRenderer(renderer => renderer
    .UseSingleCameraViewports(views => views
        .Add("left", ViewportRect.LeftHalf, ViewportFitMode.Cover)
        .Add("right", ViewportRect.RightHalf, ViewportFitMode.Contain)))
```

`Stretch` 拉伸、`Contain` 留边、`Cover` 居中裁剪。每个额外槽位只增加一次最终 blit；SceneGui 仍只在全屏绘制一次，不随世界槽位复制。该入口是多 Camera 的第一阶段基础，本身不会产生不同视角。完整语义见 [Camera 与 Viewport 渐进式路线](CAMERA_VIEWPORT_STATUS.md)。

自定义的世界空间 LDR Surface（例如 Stencil 输出）应在 Scene 配置期使用 `context.PresentWorldSurface(surface, layer, blend)`；Host 会为每个槽位建立对应 owner。直接调用单个 GameInstance 的 `RequestPresentSurface` 仍只声明一个显式 Viewport，适合 GUI、调试覆盖层或完全自定义布局。

## 多 Camera Render View

真正需要不同视角时使用独立 Render View：

```csharp
.UseDefault2DRenderer(renderer => renderer
    .UseRenderViews(views => views
        .ConfigureMain(
            ViewportRect.LeftHalf,
            cameraFollow: CameraFollowSettings.Default)
        .Add(
            "observer",
            ViewportRect.RightHalf,
            renderScale: 0.75f,
            sceneLayers: SceneLayerFilter.Exclude("MainOnly"),
            effects: RenderViewEffects.Hdr(ToneMappingSettings.Default),
            cameraFollow: new CameraFollowSettings(
                anchor: new Vector2(0.5f),
                deadZoneSize: new Vector2(320, 180),
                halfLifeSeconds: 0.25f))))
```

每个 View 重绘 Scene 并拥有独立 Camera、SceneColor 与不可变 `SceneLayerFilter`。`Include(...)` 只绘制具名层，`Exclude(...)` 绘制除此之外的可见层，省略时为 `SceneLayerFilter.All`。过滤只作用于 `GameInstance` Layer；Scene 的清屏色和 Background Sprite 仍是每个 View 的共同背景。名单在装配时复制并拒绝空名称或重复项，逐帧查询不会分配。

`context.Camera` 仍是 `RenderViewRef.Main`；其他 Camera 通过 `context.GetRenderView(...)` 获取。`PresentViewSurface` 只把自定义 Surface 送入指定 View 的槽位。主 View 的 Stencil 场景重绘使用同一份 Layer 过滤，避免遮罩输出重新带回已排除实例。

`cameraFollow` 是可选静态策略。声明后通过 `context.GetCameraFollow(viewRef)` 或 `view.RequireCameraFollow()` 取得该 View 唯一的 `CameraFollowController`；Gameplay 在自己的 Step 中传入当前目标。目标不进入不可变 Hosting Plan，因此 Scene 切换、锁定目标切换和暂停策略都不会被渲染配置反向控制。未声明时不创建控制器，也没有额外运行时成本。

`UseHdr(...)` 继续配置 `main`；次级 View 默认使用 `RenderViewEffects.Direct`，只有在 `.Add(..., effects: ...)` 中显式选择时才创建独立后处理链。`RenderViewEffects.Hdr(toneMapping)` 创建 HDR SceneColor 与 Tone Mapping；再传入 Bloom 设置才增加 Bloom。每条效果链用 View 名称作为稳定 Effect Slot，并按该 View 自己的 RenderScale 尺寸租赁目标。`RenderView.DisplayColor` 始终指向可安全呈现的 Display Surface。

| Profile | 根 SceneColor | 额外 Pass | 额外租赁目标 |
| --- | --- | ---: | ---: |
| `RenderViewEffects.Direct` | RGBA8 / Display | 0 | 0 |
| `RenderViewEffects.Hdr(toneMapping)` | RGBA16F / Linear | 1 | 1 |
| `RenderViewEffects.Hdr(toneMapping, bloom)` | RGBA16F / Linear | 2 | 4 |

这组成本也可从 `AdditionalPassCount/AdditionalRenderTargetCount` 直接读取，并随 Viewport 诊断返回。Stencil 目前仍由 `EnableStencilMasking` 配置且只属于主 View；自定义主 View Stencil 输出使用 `PresentViewSurface(RenderViewRef.Main, ...)`。`UseRenderViews` 与 `UseSingleCameraViewports` 互斥，前者表示重绘，后者表示复用同一次渲染。

## Default2DGameContext

Scene 配置回调只在窗口 GL Context 就绪、默认资源装配完成后执行。Context 提供：

- `Scene`、主 `Camera`、`RenderViews/GetRenderView/GetCameraFollow` 和当前 `Window`。
- `Viewports`、`TryScreenToView/TryScreenToWorld` 与 `CaptureViewportDiagnostics()`，用于布局感知的输入和诊断。
- `Textures`、`Sprites`、`Animations`、共享 `TextRuntime` 与可选 `Content` 包租约。
- `GetTexture/GetSprite/GetAnimation` 便利方法；未配置 Content 时给出明确异常。
- `Text.LoadFont/CreateFamily/Prepare/Draw` 贯通真实字体、Glyph Atlas 与 World/SceneGui 文本；正式游戏仍显式拥有并分发字体文件。
- `GetMaterial` 取得声明式清单中已装配的逻辑 Material 引用；未声明时给出明确异常。
- `Pipeline`、`Effects` 和 `RenderTargets` 高级逃生口。
- `RegisterRenderEffectFactory` 与 `AddRenderPass` 扩展入口。
- `SetFrameRate()`，运行时统一更新 VSync、渲染 FPS 与更新 UPS 目标。
- `EngineWindowOptions.WithFixedUpdateRate(ups)`，启动时同时绑定 UPS 与固定逻辑 delta，作为确定性模拟入口。
- `RecordLogicalInput(recorder)` / `ReplayLogicalInput(recording)`，在固定 Tick 边界捕获或注入逻辑 Action/Axis；完整协议见[逻辑输入 Tick 录制与回放](LOGICAL_INPUT_REPLAY.md)。
- `RecordGameplayState(recorder)` / `VerifyGameplayState(verifier)`，在提交后的 Tick 边界记录状态 Hash，并在首次分叉时抛出结构化诊断；见[Gameplay 状态 Hash](GAMEPLAY_STATE_HASHING.md)。
- `UseReplayRecording(session)` / `UseReplayPlayback(session)`，把逻辑输入、状态验证、磁盘 Bundle 与结束策略收敛为一个开发期会话；见[可持久化 Replay Bundle](REPLAY_BUNDLES.md)。
- `TryCaptureFrameStatistics()`，读取显式启用后的最近完成帧统计。
- `CapturePerformanceSnapshot()`，低频聚合帧计数、Texture/Atlas、根目标、Pool 缓存和预算超限项。
- `RegisterGpuMemoryUsage()`，为绕过引擎资源库的高级 GPU 资源补充动态估算。
- `CaptureRenderDiagnostics()`，显式获取 Pass、逻辑 Surface、Effect owner、临时 RenderTarget 租约与可选帧统计的纯值快照。
- `Close()`，供 ESC 等实例行为请求关闭；Host 会等当前 Step/Draw 回调返回后再触发原生 Closing，避免回调中途释放 Pipeline。

Context 是装配期强类型对象，不是全局 Service Locator。GameInstance 仍应保存逻辑 Sprite、设置和领域事件回调，不应保存 GL、Shader 或 RenderTarget。

Render Graph 捕获只用于低频调试和测试，不在 Host 每帧自动执行，也不会延长 GPU 资源生命周期。帧统计默认关闭，可通过 `EngineWindowOptions.WithFrameStatistics()` 启用；限帧和 Draw/Flush 口径见[运行时渲染诊断快照](RUNTIME_RENDER_DIAGNOSTICS.md)。显存估算、预算与 Sink 配置见[性能预算与低频遥测](PERFORMANCE_TELEMETRY.md)。

## Host 接管的帧生命周期

```text
Window.Load
  -> 创建默认 Runtime
  -> 加载全局 Content，或按初始 Scene 加载其 Content Package
  -> ConfigureScene(context)
  -> 添加默认 World/GUI Presentation owner

Window.Step
  -> Scene.PerformInput
  -> Scene.PerformStep
  -> 准备并提交待切换 Scene 的 Content Package（仅有切换请求时）
  -> ScenePipelineBuilder.ApplyEvents
  -> ContentHotReload.Commit（仅有已准备修订时）
  -> ShaderHotReload.Commit（仅有稳定源码修订时）

Window.Draw
  -> RenderPipeline.Execute

Window.Resize
  -> Scene / Camera / 根 RT / Pipeline / Builder resize
```

使用者不再订阅这些窗口事件，也不需要手工构造 `RenderPassContext`。

## 所有权与失败回滚

GPU 对象按创建顺序进入内部 `OwnedResourceStack`，正常关闭或初始化失败时按逆序释放：

```text
Scene.End
ScenePipelineBuilder
RenderPipeline
RenderTargetPool
根 RenderTarget
ContentHotReloadCoordinator
ShaderHotReloadCoordinator
LoadedContentPackage / ContentPackageManager
TextRuntime（Glyph Atlas / Font）
TextureLibrary / SpriteBatch
Shader
```

释放幂等；某个资源释放失败时仍继续清理其余资源，最后汇总异常。内容加载或 Scene 配置回调失败时，不会遗留本次已创建的 Host 资源。

## 当前边界

- v1 只提供单窗口和 OpenGL 默认 2D Runtime；已支持 Scene 目录/切换，但没有 Scene 栈或后台加载。
- 支持单 Camera 多呈现槽位，以及带独立 Camera、RenderScale、Scene Layer 过滤和显式 HDR/Bloom/Tone Mapping 策略的多 Render View；次级 Stencil 尚未开放。
- Host 不自动注册未启用的可选 Feature；请求缺失 Factory 会沿用 Builder 的明确诊断。
- 内容路径相对 `AppContext.BaseDirectory` 解析；绝对路径视为开发者显式选择。
- 高级用户可以继续不使用 Hosting，直接组合现有底层模块。

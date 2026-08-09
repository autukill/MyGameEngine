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

SceneGui 默认开启；不需要 Draw GUI 路径时可调用 `DisableSceneGui()`，避免创建对应 RenderTarget 和 Pass。

## Default2DGameContext

Scene 配置回调只在窗口 GL Context 就绪、默认资源装配完成后执行。Context 提供：

- `Scene`、`Camera` 和当前 `Window`。
- `Textures`、`Sprites` 与可选 `Content` 包租约。
- `GetTexture/GetSprite` 便利方法；未配置 Content 时给出明确异常。
- `Pipeline`、`Effects` 和 `RenderTargets` 高级逃生口。
- `RegisterRenderEffectFactory` 与 `AddRenderPass` 扩展入口。
- `Close()`，供 ESC 等实例行为请求关闭；Host 会等当前 Step/Draw 回调返回后再触发原生 Closing，避免回调中途释放 Pipeline。

Context 是装配期强类型对象，不是全局 Service Locator。GameInstance 仍应保存逻辑 Sprite、设置和领域事件回调，不应保存 GL、Shader 或 RenderTarget。

## Host 接管的帧生命周期

```text
Window.Load
  -> 创建默认 Runtime
  -> 加载 Content
  -> ConfigureScene(context)
  -> 添加默认 World/GUI Presentation owner

Window.Step
  -> Scene.PerformInput
  -> Scene.PerformStep
  -> ScenePipelineBuilder.ApplyEvents

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
LoadedContentPackage / ContentPackageManager
TextureLibrary / SpriteBatch
Shader
```

释放幂等；某个资源释放失败时仍继续清理其余资源，最后汇总异常。内容加载或 Scene 配置回调失败时，不会遗留本次已创建的 Host 资源。

## 当前边界

- v1 只提供单窗口、单初始 Scene 和 OpenGL 默认 2D Runtime。
- 尚未提供 Scene 切换栈、暂停策略、后台加载或多窗口。
- Host 不自动注册未启用的可选 Feature；请求缺失 Factory 会沿用 Builder 的明确诊断。
- 内容路径相对 `AppContext.BaseDirectory` 解析；绝对路径视为开发者显式选择。
- 高级用户可以继续不使用 Hosting，直接组合现有底层模块。

# MyGameEngine — C# 2D 游戏引擎原型

MyGameEngine 是一个基于 .NET 10、Silk.NET 与 OpenGL 3.3 构建的 2D 游戏引擎原型。项目采用 DDD + 垂直切片架构，并提供接近 GameMaker 的 Room、Object Instance 与事件生命周期。

当前版本已经打通窗口、输入、场景、相机、SpriteBatch、HDR RenderTarget、Stencil Masking、Bloom、Tone Mapping 和多 Pass 合成，可运行完整图形 Demo。

## 当前能力

- GMS 风格实例生命周期：Create、Begin/Step/End Step、Begin/Draw/End Draw、Draw GUI、Key Down/Up、Destroy。
- Gameplay Authoring：直接变换、逻辑输入、时间原语、Health/Damage、Instance Ref、确定性 Clock/Random、可持久化 Replay、状态 Hash/首次分叉诊断、实例级生命周期与轻量 Behavior。
- Gameplay Motion：21 种无状态 Easing、标量/位置/颜色/最短角 Tween，以及限速和半衰期平滑追踪。
- Gameplay Time：Gameplay/Unscaled 时间域、owner/key 暂停、单一 TimeScale；暂停冻结逻辑但继续渲染和安全帧边界。
- Gameplay Runtime：Scene Input/Step/Draw/DrawGUI 使用可复用快照，实例规模预热后保持零托管分配，并保留确定性阶段边界。
- Gameplay State：强类型 Enter/Step/Exit、状态持续时间、确定性切换和零稳态分配，直接继承实例暂停与时间域。
- Gameplay Query：保留便利结果数组，同时提供可复用 Buffer、强类型 Gameplay Tag 过滤、无集合计数和按真实 Step 聚合的可选查询统计。
- Gameplay 组合：声明式 Scene 目录、`SceneRef<TArgs>` 安全参数快照、类型安全 Prefab，以及 Box/Circle 碰撞和区域/半径查询。
- `SceneAggregate`：实例、Layer、Background、Viewport、领域事件和场景生命周期。
- 统一输入系统：键盘/鼠标轮询以及每帧按下、释放沿事件；不可变逻辑 Action/Axis 把玩法意图与物理绑定分离，并支持固定 Tick 内存录制与无设备回放。
- 零额外依赖的 SpriteBatch：纹理、Blend、Depth、Shader 状态变化自动 Flush。
- 类型化 Shader 材质：逻辑 `MaterialRef`、多材质共享 Program、参数 Revision 批处理与热替换自动重放。
- Shader 契约诊断：GL 反射校验材质 Uniform 名称/类型/数组，编译错误携带源码路径、行号与原始驱动日志。
- 声明式 Shader Assets：版本化 `shaders.json`、Material 默认参数、Hosting 自动装配，以及 MSBuild 生成强类型 Shader/Material/Uniform 参数引用。
- 动画就绪 Sprite：逻辑资源名、原点、多帧 UV、自动帧推进、旋转/缩放/颜色以及 `batch.DrawSprite*` 便利 API。
- Texture Assets：逻辑 `TextureRef`、PNG/静态 WebP 解码、采样预设、资产清单与 GPU 句柄统一回收。
- 声明式 Content Assets：单一版本化 `assets.json`、包依赖、单图/Grid/多图片 Sprite、事务回滚与引用计数卸载。
- 离线 Texture Atlas：确定性多页打包、padding/extrude、采样分组、大帧旁路与标准运行时包输出。
- 正交 `Camera2D`：平移、缩放、旋转、震屏和 Viewport resize。
- RenderPass DAG：场景渲染、Stencil 遮罩、后处理和 Viewport 合成。
- 动态效果装配：实例领域事件、共享 owner 集合、`ScenePipelineBuilder` 与 `RenderTargetPool`。
- 逻辑 RenderSurface：纯值输入输出、根表面注册、稳定拓扑排序与失败原子重建。
- HDR/LDR 呈现链：RGBA16F Scene/Bloom、ACES/Reinhard Tone Mapping、显式 Presentation 终端与独立 SceneGui。
- Stencil 几何：显式 Mask 组、单 owner 批量快照、真实 Circle 与 Sprite Alpha，支持帧、原点、旋转和正负缩放。
- 自动 GPU 像素回归：固定时间步、PNG 基线、容差比较以及 expected/actual/diff 诊断产物。
- 可分发内容工具链：`gameengine-assets` .NET Tool 与内置编译器的 `buildTransitive` NuGet 包。
- Engine Hosting：声明式启动、默认 2D 渲染预设、强类型 Scene Context、帧循环与资源清理。
- 强类型 Content：Build 自动生成 Package、Sprite 与 Texture 逻辑引用，并在编译期诊断重名。
- 可分发 Game SDK 与模板：`MyGameEngine.GameSdk` 聚合运行时程序集，`dotnet new mygameengine-game` 可在仓库外创建完整项目。
- 开发环境诊断：`gameengine doctor` 检查 SDK、包版本、内容产物，并可显式探测隐藏 OpenGL 3.3 Context。
- 运行时渲染快照：显式读取 Pass 顺序、逻辑 Surface、Effect owner 与 RenderTarget 活动租约，不暴露 GPU 句柄。
- 独立 Feature module、控制台冒烟测试和图形 VisualTests。

文档从 [docs/README.md](docs/README.md) 进入；详细进度与已知限制见 [docs/PROJECT_STATUS.md](docs/PROJECT_STATUS.md)。

## 工程结构

```text
src/
├── Engine.Core/                         # 共享 Domain 与底层窗口/输入/图形基础设施
├── Engine.Hosting/                      # GameApplication、默认 2D Runtime 与开发者入口
├── Engine.Hosting.Tests/                # 配置、默认 owner 和资源所有权验证
├── Engine.Features/
│   ├── Camera/                          # Camera2D
│   ├── Bloom/                           # 独立阈值提取与水平/垂直 ping-pong 效果链
│   ├── ContentAssets/                   # 声明式包、依赖图、Texture/Sprite 装配与租约
│   ├── Presentation/                    # 显式 RGBA8/Display 屏幕终端与稳定合成层级
│   ├── RenderPipeline/                  # RenderTarget、RenderPass DAG、后处理与合成
│   ├── Replay/                          # 版本化输入 + 状态轨迹 Bundle 与会话 API
│   ├── SceneSystem/                     # Layer、RenderCommand（旧 Context 正在退役）
│   ├── ShaderAssets/                    # shaders.json、Material Schema/default 与安全路径校验
│   ├── Sprites/                         # SpriteLibrary、帧解析与动画资源元数据
│   ├── StencilMasking/                  # Stencil 状态、命令、事件与 Pass
│   ├── TextureAssets/                   # TextureLibrary、Skia 解码与资产清单
│   ├── TextureAtlas/                    # 纯 CPU Atlas 排布与像素页面生成
│   ├── ToneMapping/                     # HDR 曝光、ACES/Reinhard 与 RGBA8 显示输出
│   ├── *.Tests/                         # 13 个 Feature 无窗口控制台冒烟项目
│   └── *.VisualTests/                   # 5 个图形验证项目
├── Engine.Tools.AssetCompiler/          # 离线 assets.json → Atlas 运行时包编译器
├── Engine.Tools.AssetCompiler.Tests/    # 编译产物与运行时兼容验证
├── Engine.Tools.Cli/                    # gameengine doctor 可分发 .NET Tool
├── Engine.Tools.Cli.Tests/              # 项目配置、内容产物与 GPU Probe 无窗口测试
├── Engine.Build.ContentPipeline/        # 可跨仓库引用的 NuGet buildTransitive 包
├── Engine.Build.ContentPipeline.Tests/  # 本地 Feed、Tool 安装与外部消费项目集成验证
├── Engine.Distribution.GameSdk/         # 聚合正式运行时程序集的 NuGet 包
├── Engine.Distribution.Tests/           # Pack/模板安装/仓库外 Build/Run/Publish 验证
├── Engine.Templates/                    # dotnet new mygameengine-game 项目模板包
├── build/GameEngine.Content.targets     # Build/Run/Publish 增量资产集成
├── Engine.DddTests/                     # 聚合、生命周期、输入与状态调度验证
└── MyGame.Runner/                       # Stencil + Bloom 综合 Demo

playgrounds/
├── AirplaneShooter/                     # 方向键移动、空格发射与 Alarm 回收示例
└── Asteroids/                           # 参数化 Prefab、Circle 碰撞和 Scene 重启示例
```

Feature 依赖保持单向：

```text
Engine.Core
  ├─ Sprites
  ├─ ShaderAssets
  ├─ TextureAssets
  │    └─ ContentAssets（同时依赖 Sprites）
  ├─ TextureAtlas
  ├─ Replay
└─ Camera
       └─ RenderPipeline
            ├─ Bloom
            ├─ Presentation
            ├─ SceneSystem
            ├─ StencilMasking
            └─ ToneMapping

Engine.Hosting -> Core + Replay + Camera/Content/ShaderAssets/RenderPipeline/Presentation/Bloom/Stencil/Tone
```

解决方案当前共 50 个项目，入口文件为 `MyGameEngine.slnx`。

## 环境要求

- .NET 10 SDK（当前验证版本：10.0.302）
- 支持 OpenGL 3.3 Core 的显卡与驱动
- Windows 或安装了 GLFW/OpenGL 运行库的 Linux

## 构建与运行

```bash
dotnet restore MyGameEngine.slnx
dotnet build MyGameEngine.slnx
dotnet run --project src/MyGame.Runner/MyGame.Runner.csproj
```

面向游戏开发者的最小示例可以直接运行：

```bash
dotnet run --project playgrounds/AirplaneShooter/AirplaneShooter.csproj
```

使用方向键或 `WASD` 移动飞机，按住空格连续发射子弹。示例完整展示 Hosting、强类型 Content、输入、`Spawn` 和 `Alarm`，详见 [Airplane Shooter Playground](playgrounds/AirplaneShooter/README.md)。

第二个 Gameplay 样例使用旋转推进、`PrefabRef<T, TArgs>`、Alarm 敌人生成、Circle 碰撞、强类型 GameOver 参数和 `P` 键无 UI 暂停：

```bash
dotnet run --project playgrounds/Asteroids/Asteroids.csproj
```

完整配方见 [Gameplay Cookbook](docs/GAMEPLAY_COOKBOOK.md)，输入装配见 [逻辑 Input Actions](docs/INPUT_ACTIONS.md)，跨类型身份与过滤见 [Gameplay Tags](docs/GAMEPLAY_TAGS.md)，局部能力复用见 [Gameplay Behavior 组合](docs/GAMEPLAY_BEHAVIORS.md)，空间查询的实测基线与 Spatial Hash 决策见 [Gameplay 空间查询基准](docs/GAMEPLAY_QUERY_PERFORMANCE.md)。

Runner 内容：4 个彩色方块绕场景中心运动，鼠标控制圆形 Stencil 聚光灯；世界颜色先进入 RGBA16F Scene，随后经过 HDR Bloom 与 ACES Tone Mapping 输出到屏幕。

- 移动鼠标：移动聚光灯
- `Esc`：退出
- 调整窗口：同步 resize Camera、RenderTarget 和 Pipeline Viewport
- `--mirrored-viewports`：同一 Camera/后处理结果以 Cover 模式呈现到左右两个 Viewport
- `--split-cameras`：主 HDR/Stencil Camera 与 0.75 RenderScale 的轻量 LDR Camera 分别渲染左右 Viewport

## 验证

无窗口冒烟测试：

```bash
dotnet run --project src/Engine.DddTests/Engine.DddTests.csproj
dotnet run --project src/Engine.Hosting.Tests/Engine.Hosting.Tests.csproj
dotnet run --project src/Engine.Features/Bloom.Tests/Bloom.Tests.csproj
dotnet run --project src/Engine.Features/Camera.Tests/Camera.Tests.csproj
dotnet run --project src/Engine.Features/Presentation.Tests/Presentation.Tests.csproj
dotnet run --project src/Engine.Features/RenderPipeline.Tests/RenderPipeline.Tests.csproj
dotnet run --project src/Engine.Features/SceneSystem.Tests/SceneSystem.Tests.csproj
dotnet run --project src/Engine.Features/Sprites.Tests/Sprites.Tests.csproj
dotnet run --project src/Engine.Features/StencilMasking.Tests/StencilMasking.Tests.csproj
dotnet run --project src/Engine.Features/TextureAssets.Tests/TextureAssets.Tests.csproj
dotnet run --project src/Engine.Features/ContentAssets.Tests/ContentAssets.Tests.csproj
dotnet run --project src/Engine.Features/TextureAtlas.Tests/TextureAtlas.Tests.csproj
dotnet run --project src/Engine.Features/ToneMapping.Tests/ToneMapping.Tests.csproj
dotnet run --project src/Engine.Tools.AssetCompiler.Tests/Engine.Tools.AssetCompiler.Tests.csproj
dotnet run --project src/Engine.Build.ContentPipeline.Tests/Engine.Build.ContentPipeline.Tests.csproj
dotnet run --project src/Engine.Distribution.Tests/Engine.Distribution.Tests.csproj
dotnet run --project src/Engine.Tools.Cli.Tests/Engine.Tools.Cli.Tests.csproj

# 隐藏窗口运行三帧，验证 Hosting + Runner 的真实 GL 启动与安全关闭
dotnet run --project src/MyGame.Runner/MyGame.Runner.csproj -- --smoke
```

图形验证入口位于五个 `Engine.Features/*.VisualTests` 项目。`Sprites.VisualTests` 的源包包含双帧 WebP 图集和两张独立 WebP 帧，Build 自动在 `obj` 生成单页 Atlas 并复制到 `AssetsCompiled`；这些项目需要本地图形窗口人工确认。

九个确定性真实 OpenGL 场景可自动执行 PNG 像素回归：

```bash
dotnet run --project src/Engine.VisualRegressionTests/Engine.VisualRegressionTests.csproj -- --verify
```

基线更新、单场景过滤、退出码和差异产物说明见 [GPU 像素回归测试](docs/VISUAL_REGRESSION.md)。

## Engine Hosting 快速开始

```csharp
using MyGame.Content;

using var game = GameApplication
    .Create(EngineWindowOptions.Default)
    .UseDefault2DRenderer(renderer => renderer
        .UseContent(GameAssets.Packages.Root)
        .UseHdr(ToneMappingSettings.Default, BloomSettings.Default)
        .EnableStencilMasking())
    .ConfigureScene("MainScene", context =>
    {
        context.Scene.Add(new Player(GameAssets.Sprites.PlayerIdle));
    })
    .Build();

game.Run();
```

Host 统一接管 Load、Step、Draw、resize、内容包和 GPU 资源释放；高级用户仍可通过 Context 添加自定义 Factory 与 RenderPass。完整说明见 [Engine Hosting 与默认 2D 启动套件](docs/ENGINE_HOSTING.md)。

## Game SDK 与项目模板

```powershell
dotnet pack src/Engine.Distribution.GameSdk/Engine.Distribution.GameSdk.csproj -c Release -o artifacts/packages
dotnet pack src/Engine.Build.ContentPipeline/Engine.Build.ContentPipeline.csproj -c Release -o artifacts/packages
dotnet pack src/Engine.Templates/Engine.Templates.csproj -c Release -o artifacts/packages
dotnet new install artifacts/packages/MyGameEngine.Templates.0.1.0-alpha.1.nupkg
dotnet new mygameengine-game -n MyFirstGame
dotnet tool restore --tool-manifest MyFirstGame/.config/dotnet-tools.json
dotnet gameengine doctor MyFirstGame
```

生成项目只引用 `MyGameEngine.GameSdk` 与 `MyGameEngine.ContentPipeline`，默认带有 Hosting 启动代码、GameInstance 示例、真实 WebP 资产和强类型 `GameAssets`。完整打包、本地 Feed 与模板说明见 [Game SDK 与项目模板](docs/GAME_SDK_AND_TEMPLATES.md)。

`gameengine doctor` 默认只读检查项目；增加 `--probe-opengl` 后会创建短生命周期隐藏窗口，真实验证 OpenGL 3.3 Core。诊断代码、退出码和 CI 用法见 [`gameengine doctor` 开发环境诊断](docs/GAMEENGINE_DOCTOR.md)。

运行时可通过 `context.CaptureRenderDiagnostics()` 低频捕获渲染图快照，检查 Pass 执行顺序、Effect owner、Surface 关系和临时 RenderTarget 租约。窗口支持启动及运行时 FPS/UPS/VSync 控制；显式调用 `WithFrameStatistics()` 后还可读取 Draw Call、Batch Flush、纹理切换和活跃 Pass。完整 API、统计口径与性能边界见 [运行时渲染诊断快照](docs/RUNTIME_RENDER_DIAGNOSTICS.md)。

开发期还可启用 `PerformanceTelemetryOptions`，按预算低频输出 Texture/Atlas、根目标、动态与缓存 RenderTarget 的显存估算。Runner 的 `--diagnostics` 输出控制台摘要，`--diagnostics-json <path>` 写入 JSON Lines；详见[性能预算与低频遥测](docs/PERFORMANCE_TELEMETRY.md)。

## Sprite 便利 API

```csharp
batch.DrawSprite(sprite, imageIndex, x, y);
batch.DrawSpriteExt(sprite, imageIndex, position, scale, rotationRadians, color);
batch.DrawSpriteStretched(sprite, 0, position, size);
```

`SpriteRef` 只保存逻辑名称；`SpriteLibrary` 通过 `TextureRef` 向 `ITextureResolver` 解析 GPU 纹理，不再借用原始句柄。GameInstance 的默认 `DrawSelf` 自动使用 `Transform`、`Color`、`ImageIndex` 与 `ImageSpeed`。

## Texture Assets

```csharp
using var textures = new TextureLibrary(gl);
var playerTexture = textures.Load("player", "Content/player.webp", TextureSampler.PixelArt);
var sprites = new SpriteLibrary(textures);
var playerSprite = sprites.RegisterGrid("player", playerTexture,
    frameSize: new Vector2(32, 32), origin: new Vector2(16, 24), frameCount: 8, framesPerSecond: 12);
```

`TextureLibrary` 拥有已上传句柄的生命周期，支持文件、流、RGBA8 内存数据和 JSON 资产清单。默认 Skia 解码器支持 PNG 与静态 WebP；动态 WebP 应显式拆为 Sprite 帧。清单路径必须位于指定 content root 内，批量加载失败时会回滚本次已创建的纹理。

## 声明式 Content Assets

```csharp
using var manager = new ContentPackageManager(textures, sprites, packagesRoot);
using var package = manager.Load(GameAssets.Packages.Root);
var idle = package.GetSprite("boss.idle");
```

`assets.json` 可声明包依赖、Texture，以及 `single`、`grid`、`frames` 三种 Sprite 布局。`frames` 的每一帧都可引用不同 `TextureRef` 并指定像素裁剪区域，因此大尺寸单帧可以保留为独立图片；运行时 Sprite 引用和绘制 API 不受未来 Atlas 重映射影响。Manager 会先验证完整依赖图，再按拓扑顺序同步加载；失败只回滚本次新增资源，共享依赖在最后一个租约释放后才卸载。

完整清单字段、多纹理长动画、包依赖和生命周期说明见 [Content Assets 使用指南](docs/CONTENT_ASSETS.md)；生成引用、Atlas 过滤和命名规则见[强类型 Content 引用](docs/STRONGLY_TYPED_CONTENT.md)。开发运行可选择启用[编译指纹驱动的 Content 热重载](docs/CONTENT_HOT_RELOAD.md)，新修订后台准备并在 Step 与 Draw 之间原子切换，失败继续使用旧资源。

## 离线 Texture Atlas

```powershell
dotnet run --project src/Engine.Tools.AssetCompiler/Engine.Tools.AssetCompiler.csproj -- `
  --incremental `
  src/Engine.Features/Sprites.VisualTests/Assets `
  assets.json `
  artifacts/sprites-visual
```

编译器按包维护内容指纹和输出 SHA-256，只重建受影响包及上游；构建使用临时目录原子替换，失败保留旧产物。Runner 与 Sprites.VisualTests 已通过共享 MSBuild Target 自动接入 Build、Run 和 Publish。完整说明见 [离线 Texture Atlas 使用指南](docs/TEXTURE_ATLAS.md)和 [`GameEngine.Content.targets` 解读](docs/GAMEENGINE_CONTENT_TARGETS.md)。

外部项目可以安装 `MyGameEngine.AssetCompiler` Tool，或通过 `MyGameEngine.ContentPipeline` PackageReference 自动接入同一套 targets。包不依赖仓库绝对路径，Debug/Release 缓存相互隔离，Publish 只携带运行时资产。完整说明见 [可分发内容工具链](docs/CONTENT_PIPELINE_PACKAGES.md)。

## 渲染流程

```text
SceneRenderPass      -> RT_Scene RGBA16F Linear
StencilMaskPass      -> StencilMask.mask RGBA8 Display
BloomPass            -> Bright -> Ping(H) -> Pong(V) RGBA16F
ToneMappingPass      -> Scene + Glow -> RGBA8 Display
SceneGuiRenderPass   -> SceneGui RGBA8 Display
Presentation         -> Screen (Tone opaque + Mask alpha + SceneGui alpha)
```

Factory 先用 `RenderEffectPlan` 声明带存储格式和颜色编码的逻辑 Surface 输入/输出，Builder 验证唯一生产者、缺失输入、格式匹配和循环后稳定拓扑创建 Runtime；底层 Pass 再通过实际 RenderTarget 声明执行依赖。完整说明见 [动态渲染效果使用指南](docs/DYNAMIC_RENDER_EFFECTS.md)、[逻辑 RenderSurface](docs/RENDER_SURFACES.md)、[Presentation](docs/PRESENTATION.md)、[Bloom](docs/BLOOM_EFFECT.md)和 [Tone Mapping](docs/TONE_MAPPING.md)。

自定义 Sprite Shader 可由 Hosting 从安全根目录注册，通过逻辑 `ShaderRef` 或类型化 `MaterialRef` 应用到实例；开发期可选整批原子热替换，编译或链接失败继续使用旧 Program，材质参数会自动应用到新 Program。详见[自定义 Sprite Shader 与热重载](docs/SHADER_HOT_RELOAD.md)和[Shader 材质参数块](docs/SHADER_MATERIALS.md)。

## 下一阶段

1. 多 Render View 的 Release 基线、声明式 Camera 跟随和 resize/release GPU 回归已经闭环；当前数据不支持引入跨 View 缓存。
2. 确定性 Gameplay Authoring 的 Clock/Random、逻辑输入、状态 Hash 和版本化磁盘 Replay 已闭环；下一项先用真实回放评估长会话跳转成本，再决定是否引入周期 Checkpoint，Gameplay Signal 继续等待真实一对多用例。

可选离线 Shader 编译已记录在[独立方向文档](docs/OFFLINE_SHADER_COMPILATION.md)，当前暂缓以优先改善日常玩法编写体验。

设计推演原稿保存在 [docs/C# 2D 游戏引擎从零构建.md](docs/C%23%202D%20游戏引擎从零构建.md)，它是路线参考，不代表所有示例都已实现。

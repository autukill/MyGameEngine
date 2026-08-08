# MyGameEngine — C# 2D 游戏引擎原型

MyGameEngine 是一个基于 .NET 10、Silk.NET 与 OpenGL 3.3 构建的 2D 游戏引擎原型。项目采用 DDD + 垂直切片架构，并提供接近 GameMaker 的 Room、Object Instance 与事件生命周期。

当前版本已经打通窗口、输入、场景、相机、SpriteBatch、RenderTarget、Stencil Masking、Bloom 后处理和多 Pass 合成，可运行完整图形 Demo。

## 当前能力

- GMS 风格实例生命周期：Create、Begin/Step/End Step、Begin/Draw/End Draw、Draw GUI、Key Down/Up、Destroy。
- `SceneAggregate`：实例、Layer、Background、Viewport、领域事件和场景生命周期。
- 统一输入系统：键盘/鼠标轮询以及每帧按下、释放沿事件。
- 零额外依赖的 SpriteBatch：纹理、Blend、Depth、Shader 状态变化自动 Flush。
- 动画就绪 Sprite：逻辑资源名、原点、多帧 UV、自动帧推进、旋转/缩放/颜色以及 `batch.DrawSprite*` 便利 API。
- Texture Assets：逻辑 `TextureRef`、PNG/静态 WebP 解码、采样预设、资产清单与 GPU 句柄统一回收。
- 声明式 Content Assets：单一版本化 `assets.json`、包依赖、单图/Grid/多图片 Sprite、事务回滚与引用计数卸载。
- 离线 Texture Atlas：确定性多页打包、padding/extrude、采样分组、大帧旁路与标准运行时包输出。
- 正交 `Camera2D`：平移、缩放、旋转、震屏和 Viewport resize。
- RenderPass DAG：场景渲染、Stencil 遮罩、后处理和 Viewport 合成。
- 动态效果装配：实例领域事件、共享 owner 集合、`ScenePipelineBuilder` 与 `RenderTargetPool`。
- 自动 GPU 像素回归：固定时间步、PNG 基线、容差比较以及 expected/actual/diff 诊断产物。
- 可分发内容工具链：`gameengine-assets` .NET Tool 与内置编译器的 `buildTransitive` NuGet 包。
- 独立 Feature module、控制台冒烟测试和图形 VisualTests。

文档从 [docs/README.md](docs/README.md) 进入；详细进度与已知限制见 [docs/PROJECT_STATUS.md](docs/PROJECT_STATUS.md)。

## 工程结构

```text
src/
├── Engine.Core/                         # 共享 Domain 与底层窗口/输入/图形基础设施
├── Engine.Features/
│   ├── Camera/                          # Camera2D
│   ├── Bloom/                           # 独立阈值提取与水平/垂直 ping-pong 效果链
│   ├── ContentAssets/                   # 声明式包、依赖图、Texture/Sprite 装配与租约
│   ├── RenderPipeline/                  # RenderTarget、RenderPass DAG、后处理与合成
│   ├── SceneSystem/                     # Layer、RenderCommand（旧 Context 正在退役）
│   ├── Sprites/                         # SpriteLibrary、帧解析与动画资源元数据
│   ├── StencilMasking/                  # Stencil 状态、命令、事件与 Pass
│   ├── TextureAssets/                   # TextureLibrary、Skia 解码与资产清单
│   ├── TextureAtlas/                    # 纯 CPU Atlas 排布与像素页面生成
│   ├── *.Tests/                         # 9 个 Feature 无窗口控制台冒烟项目
│   └── *.VisualTests/                   # 5 个图形验证项目
├── Engine.Tools.AssetCompiler/          # 离线 assets.json → Atlas 运行时包编译器
├── Engine.Tools.AssetCompiler.Tests/    # 编译产物与运行时兼容验证
├── Engine.Build.ContentPipeline/        # 可跨仓库引用的 NuGet buildTransitive 包
├── Engine.Build.ContentPipeline.Tests/  # 本地 Feed、Tool 安装与外部消费项目集成验证
├── build/GameEngine.Content.targets     # Build/Run/Publish 增量资产集成
├── Engine.DddTests/                     # 聚合、生命周期、输入与状态调度验证
└── MyGame.Runner/                       # Stencil + Bloom 综合 Demo
```

Feature 依赖保持单向：

```text
Engine.Core
  ├─ Sprites
  ├─ TextureAssets
  │    └─ ContentAssets（同时依赖 Sprites）
  ├─ TextureAtlas
└─ Camera
       └─ RenderPipeline
            ├─ Bloom
            ├─ SceneSystem
            └─ StencilMasking
```

解决方案当前共 32 个项目，入口文件为 `MyGameEngine.slnx`。

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

Runner 内容：4 个彩色方块绕场景中心运动，鼠标控制圆形 Stencil 聚光灯，聚光区域经过 Bloom 后与原场景合成。

- 移动鼠标：移动聚光灯
- `Esc`：退出
- 调整窗口：同步 resize Camera、RenderTarget 和 Pipeline Viewport

## 验证

无窗口冒烟测试：

```bash
dotnet run --project src/Engine.DddTests/Engine.DddTests.csproj
dotnet run --project src/Engine.Features/Bloom.Tests/Bloom.Tests.csproj
dotnet run --project src/Engine.Features/Camera.Tests/Camera.Tests.csproj
dotnet run --project src/Engine.Features/RenderPipeline.Tests/RenderPipeline.Tests.csproj
dotnet run --project src/Engine.Features/SceneSystem.Tests/SceneSystem.Tests.csproj
dotnet run --project src/Engine.Features/Sprites.Tests/Sprites.Tests.csproj
dotnet run --project src/Engine.Features/StencilMasking.Tests/StencilMasking.Tests.csproj
dotnet run --project src/Engine.Features/TextureAssets.Tests/TextureAssets.Tests.csproj
dotnet run --project src/Engine.Features/ContentAssets.Tests/ContentAssets.Tests.csproj
dotnet run --project src/Engine.Features/TextureAtlas.Tests/TextureAtlas.Tests.csproj
dotnet run --project src/Engine.Tools.AssetCompiler.Tests/Engine.Tools.AssetCompiler.Tests.csproj
dotnet run --project src/Engine.Build.ContentPipeline.Tests/Engine.Build.ContentPipeline.Tests.csproj
```

图形验证入口位于五个 `Engine.Features/*.VisualTests` 项目。`Sprites.VisualTests` 的源包包含双帧 WebP 图集和两张独立 WebP 帧，Build 自动在 `obj` 生成单页 Atlas 并复制到 `AssetsCompiled`；这些项目需要本地图形窗口人工确认。

四个确定性真实 OpenGL 场景可自动执行 PNG 像素回归：

```bash
dotnet run --project src/Engine.VisualRegressionTests/Engine.VisualRegressionTests.csproj -- --verify
```

基线更新、单场景过滤、退出码和差异产物说明见 [GPU 像素回归测试](docs/VISUAL_REGRESSION.md)。

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
using var package = manager.Load("characters/boss/assets.json");
var idle = package.GetSprite("boss.idle");
```

`assets.json` 可声明包依赖、Texture，以及 `single`、`grid`、`frames` 三种 Sprite 布局。`frames` 的每一帧都可引用不同 `TextureRef` 并指定像素裁剪区域，因此大尺寸单帧可以保留为独立图片；运行时 Sprite 引用和绘制 API 不受未来 Atlas 重映射影响。Manager 会先验证完整依赖图，再按拓扑顺序同步加载；失败只回滚本次新增资源，共享依赖在最后一个租约释放后才卸载。

完整清单字段、多纹理长动画、包依赖和生命周期说明见 [Content Assets 使用指南](docs/CONTENT_ASSETS.md)。

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
SceneRenderPass      -> RT_Scene
StencilMaskPass      -> RT_Masked (AlphaBlend)
BloomPass            -> Bright -> Ping(H) -> Pong(V)
ViewportCompositor   -> Screen (Scene opaque + Mask alpha + Bloom additive)
```

Pass 通过输入/输出 RenderTarget 声明依赖，Pipeline 在每帧执行前进行拓扑排序。实例通过 `RenderEffectRequestedEvent` 分别声明 Spotlight 与 Bloom；Builder 在 Step/Draw 边界差量维护动态 Pass，最后一个 owner 离开后归还临时 RT。完整说明见 [动态渲染效果使用指南](docs/DYNAMIC_RENDER_EFFECTS.md)和 [Bloom 效果使用指南](docs/BLOOM_EFFECT.md)。

## 下一阶段

1. 设计动态效果间的显式依赖与可选 HDR/tone mapping 边界。
2. 为内容工具包增加签名、远程 Feed 发布与跨仓库缓存。
3. 为无显示器 CI 固化软件 OpenGL 执行环境。
4. 持续减少场景调度中的 LINQ/快照分配，再推进 Spatial Hash。

设计推演原稿保存在 [docs/C# 2D 游戏引擎从零构建.md](docs/C%23%202D%20游戏引擎从零构建.md)，它是路线参考，不代表所有示例都已实现。

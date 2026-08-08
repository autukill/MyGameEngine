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
- 正交 `Camera2D`：平移、缩放、旋转、震屏和 Viewport resize。
- RenderPass DAG：场景渲染、Stencil 遮罩、后处理和 Viewport 合成。
- 独立 Feature module、控制台冒烟测试和图形 VisualTests。

详细进度与已知限制见 [docs/PROJECT_STATUS.md](docs/PROJECT_STATUS.md)。

## 工程结构

```text
src/
├── Engine.Core/                         # 共享 Domain 与底层窗口/输入/图形基础设施
├── Engine.Features/
│   ├── Camera/                          # Camera2D
│   ├── RenderPipeline/                  # RenderTarget、RenderPass DAG、后处理与合成
│   ├── SceneSystem/                     # Layer、RenderCommand（旧 Context 正在退役）
│   ├── Sprites/                         # SpriteLibrary、帧解析与动画资源元数据
│   ├── StencilMasking/                  # Stencil 状态、命令、事件与 Pass
│   ├── TextureAssets/                   # TextureLibrary、Skia 解码与资产清单
│   ├── *.Tests/                         # 6 个无窗口控制台冒烟项目
│   └── *.VisualTests/                   # 5 个图形验证项目
├── Engine.DddTests/                     # 聚合、生命周期、输入与状态调度验证
└── MyGame.Runner/                       # Stencil + Bloom 综合 Demo
```

Feature 依赖保持单向：

```text
Engine.Core
  ├─ Sprites
├─ TextureAssets
└─ Camera
       └─ RenderPipeline
            ├─ SceneSystem
            └─ StencilMasking
```

解决方案当前共 20 个项目，入口文件为 `MyGameEngine.slnx`。

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
dotnet run --project src/Engine.Features/Camera.Tests/Camera.Tests.csproj
dotnet run --project src/Engine.Features/RenderPipeline.Tests/RenderPipeline.Tests.csproj
dotnet run --project src/Engine.Features/SceneSystem.Tests/SceneSystem.Tests.csproj
dotnet run --project src/Engine.Features/Sprites.Tests/Sprites.Tests.csproj
dotnet run --project src/Engine.Features/StencilMasking.Tests/StencilMasking.Tests.csproj
dotnet run --project src/Engine.Features/TextureAssets.Tests/TextureAssets.Tests.csproj
```

图形验证入口位于五个 `Engine.Features/*.VisualTests` 项目。`Sprites.VisualTests` 会从输出目录真实加载 `Assets/orbiting-drone-2frame.webp`，展示动画、中心/非中心原点、旋转、非均匀缩放与翻转；这些项目需要本地图形窗口人工确认。

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

## 渲染流程

```text
SceneRenderPass      -> RT_Scene
StencilMaskPass     -> RT_Masked
PostProcessPass     -> RT_Bloom
ViewportCompositor  -> Screen (RT_Scene opaque + RT_Bloom additive)
```

Pass 通过输入/输出 RenderTarget 声明依赖，Pipeline 在每帧执行前进行拓扑排序。实例只声明 `RenderStyle` 和 `ShaderRef`；实际 OpenGL 状态与资源由基础设施层管理。

## 下一阶段

1. `RenderEffectRequested`：实例通过领域事件声明特需渲染效果。
2. `RenderTargetPool`：复用临时 RT，并按效果 owner 集合回收动态 Pass。
3. 将 VisualTests 纳入可重复的 GPU 快照或像素回归验证。
4. 在已有 TextureLibrary 上增加 Sprite 清单和自动 TextureAtlas，保持解码、打包与动画定义分层。
5. 持续减少场景调度中的 LINQ/快照分配，再推进 Spatial Hash。

设计推演原稿保存在 [docs/C# 2D 游戏引擎从零构建.md](docs/C%23%202D%20游戏引擎从零构建.md)，它是路线参考，不代表所有示例都已实现。

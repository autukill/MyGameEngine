# MyGameEngine — C# 2D 游戏引擎原型

MyGameEngine 是一个基于 .NET 10、Silk.NET 与 OpenGL 3.3 构建的 2D 游戏引擎原型。项目采用 DDD + 垂直切片架构，并提供接近 GameMaker 的 Room、Object Instance 与事件生命周期。

当前版本已经打通窗口、输入、场景、相机、SpriteBatch、RenderTarget、Stencil Masking、Bloom 后处理和多 Pass 合成，可运行完整图形 Demo。

## 当前能力

- GMS 风格实例生命周期：Create、Begin/Step/End Step、Begin/Draw/End Draw、Draw GUI、Key Down/Up、Destroy。
- `SceneAggregate`：实例、Layer、Background、Viewport、领域事件和场景生命周期。
- 统一输入系统：键盘/鼠标轮询以及每帧按下、释放沿事件。
- 零额外依赖的 SpriteBatch：纹理、Blend、Depth、Shader 状态变化自动 Flush。
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
│   ├── StencilMasking/                  # Stencil 状态、命令、事件与 Pass
│   ├── *.Tests/                         # 4 个无窗口控制台冒烟项目
│   └── *.VisualTests/                   # 4 个图形验证项目
├── Engine.DddTests/                     # 聚合、生命周期、输入与状态调度验证
└── MyGame.Runner/                       # Stencil + Bloom 综合 Demo
```

Feature 依赖保持单向：

```text
Engine.Core
  └─ Camera
       └─ RenderPipeline
            ├─ SceneSystem
            └─ StencilMasking
```

解决方案当前共 15 个项目，入口文件为 `MyGameEngine.slnx`。

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
dotnet run --project src/Engine.Features/StencilMasking.Tests/StencilMasking.Tests.csproj
```

图形验证入口位于四个 `Engine.Features/*.VisualTests` 项目，需要本地图形窗口人工确认画面与交互。

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
4. 持续减少场景调度中的 LINQ/快照分配，再推进 TextureAtlas 与 Spatial Hash。

设计推演原稿保存在 [docs/C# 2D 游戏引擎从零构建.md](docs/C%23%202D%20游戏引擎从零构建.md)，它是路线参考，不代表所有示例都已实现。

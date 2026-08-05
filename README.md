# MyGameEngine — Phase 1.3 Runnable Demo

> **目标**：把 Phase 1.3 设计文档（自定义渲染管道 + 多 Viewport/Camera）落地为**可编译、可运行的 C# .NET 10 项目**，附 **Stencil 遮罩 + Bloom 后处理** 的端到端 Demo。

---

## 一、 项目结构

```
MyGameEngine/
├── MyGameEngine.sln
├── .gitignore
└── src/
    ├── Engine.Core/                          # 基础设施层
    │   ├── Engine.Core.csproj
    │   ├── Domain/ValueObjects/
    │   │   └── Vector2D.cs
    │   └── Infrastructure/
    │       ├── Windowing/
    │       │   ├── EngineWindow.cs           # Silk.NET 窗口 + 生命周期管道
    │       │   └── EngineWindowOptions.cs    # OpenGL 3.3 Core + Depth24/Stencil8 申请
    │       └── Graphics/
    │           ├── GraphicsDevice.cs         # GL 句柄 + 默认状态
    │           ├── IShader.cs                # 通用 Shader 接口（让 Pass 不绑定具体类型）
    │           ├── SpriteShader.cs           # 默认 2D Sprite Shader
    │           ├── PostProcessShader.cs      # Bloom: Bright Pass + 9-tap Gaussian
    │           ├── BlitShader.cs             # 简单纹理 Blit
    │           ├── SpriteBatch.cs            # 零 GC 动态批处理（MaxQuads=2048）
    │           ├── Vertex2D.cs               # 32 字节对齐顶点
    │           └── WhiteTexture.cs           # 1x1 白色纹理（用于无图彩色矩形）
    ├── Engine.Features/                      # 功能切片层
    │   ├── Engine.Features.csproj
    │   ├── Camera/
    │   │   └── Camera2D.cs                   # 正交相机 + 震屏
    │   ├── SceneSystem/
    │   │   ├── Layer.cs                     # Layer + RenderCommand 缓冲
    │   │   └── SceneAggregate.cs            # 聚合根 + Layer 排序
    │   ├── RenderPipeline/
    │   │   ├── BlendState.cs                # AlphaBlend / Additive / ColorMaskDisabled
    │   │   ├── DepthStencilState.cs        # StencilWrite / StencilTest 预设
    │   │   ├── LayerRenderState.cs         # Per-Layer 状态覆盖
    │   │   ├── ViewportRect.cs              # NDC 视口矩形
    │   │   ├── RenderTarget2D.cs            # FBO + Color + D24S8
    │   │   ├── RenderPass.cs                # 抽象节点 + RenderPassContext
    │   │   ├── RenderPipeline.cs            # 拓扑排序调度器
    │   │   ├── SceneRenderPass.cs           # 场景渲染（应用 Per-Layer 状态）
    │   │   ├── PostProcessPass.cs           # 全屏 Quad 后处理
    │   │   └── ViewportCompositorPass.cs   # 多 Camera Viewport 合成
    │   └── StencilMasking/
    │       └── StencilMaskPass.cs          # 圆形遮罩：Stencil Write → Stencil Test
    └── MyGame.Runner/                        # Demo 入口
        ├── MyGame.Runner.csproj
        └── Program.cs                       # 闪光灯圆圈 + Bloom + 鼠标控制
```

---

## 二、 构建 & 运行

### 前置依赖

- **.NET 10 SDK**（10.0.100+）— [下载](https://dotnet.microsoft.com/download/dotnet/10.0)
- **支持 OpenGL 3.3 的显卡 + 驱动**
- **操作系统**：Windows / Linux（X11 或 Wayland）/ macOS

### Linux 额外依赖

```bash
# Debian / Ubuntu
sudo apt install libglfw3 libgl1

# Fedora
sudo dnf install glfw-devel mesa-libGL
```

### 构建命令

```bash
cd MyGameEngine
dotnet restore
dotnet build
```

> 本仓库已在 .NET 10 SDK 10.0.302 上验证编译成功（0 错误 0 警告）。

### 运行命令

```bash
cd MyGameEngine
dotnet run --project src/MyGame.Runner
```

---

## 三、 Demo 玩法

启动后会看到一个 1280x720 的暗色窗口：

- **4 个彩色矩形** 在屏幕上做圆周运动（红、绿、蓝、黄）
- **一个白色圆圈**（闪光灯）跟随鼠标移动
- **圆圈内**：彩色矩形以高亮版本重绘 → 触发 Bloom（亮色光晕）
- **圆圈外**：保持原样的暗色场景
- **ESC**：退出

### 渲染流程（4 个 Pass 的 DAG）

```
   ┌─────────────────┐
   │  SceneRenderPass │──▶ RT_Scene  (Background + Instances, 默认状态)
   └─────────────────┘
   ┌─────────────────┐
   │  StencilMaskPass │──▶ RT_Masked (圆圈 Stencil Write → 重绘 Instances, Stencil Test)
   └─────────────────┘            │
                                    ▼
   ┌─────────────────┐         ┌─────────────┐
   │ PostProcessPass │──▶ RT_Bloom (Bright + 9-tap Gaussian)
   └─────────────────┘
   ┌────────────────────┐
   │ ViewportCompositor  │──▶ 屏幕  (RT_Scene 不透明底 + RT_Bloom Additive 叠加)
   └────────────────────┘
```

- **Pass 1 (SceneRenderPass → RT_Scene)**：背景 + 4 个彩色矩形按默认 Alpha 混合绘制
- **Pass 2 (StencilMaskPass → RT_Masked)**：
  - Phase A: `ColorMaskDisabled + StencilWrite(ref=1)` → 在 Stencil Buffer 标记圆圈区域
  - Phase B: `StencilTest(Equal, ref=1)` + AlphaBlend → 重绘 Instances（只在圆圈内可见）
- **Pass 3 (PostProcessPass → RT_Bloom)**：对 RT_Masked 做 Bright Pass（亮度 > 0.3）+ 9 邻域高斯加权 → 输出模糊亮区
- **Pass 4 (ViewportCompositorPass → 屏幕)**：
  - 先以 `BlendState.Opaque` 画 RT_Scene（满屏底图）
  - 再以 `BlendState.Additive` 画 RT_Bloom（仅在原圆圈位置有光晕，与底图叠加发光）

> 鼠标移动闪光灯 → 圆圈实时跟随 → Bloom 光晕实时变化。

---

## 四、 关键设计决策

### 4.1 IShader 接口（解耦 Pass 与具体 Shader）

```csharp
public interface IShader : IDisposable
{
    uint Handle { get; }
    void Use();
    void SetProjection(Matrix4x4 matrix);
}
```

让 `PostProcessPass` / `ViewportCompositorPass` 不绑死 `SpriteShader` —— 后续接入 Compute Shader / Raymarching Shader 也不用改 Pass 签名。

### 4.2 BlendState / DepthStencilState 为 readonly record struct

- **值对象语义**：可作为字典 Key 做"状态指纹"缓存，避免重复 Apply
- **零 GC**：栈上分配
- **预设常量**：`BlendState.AlphaBlend` / `BlendState.Additive` / `DepthStencilState.StencilWrite()` / `StencilTest()` —— 一行表达完整状态

### 4.3 RenderPipeline 拓扑排序

```csharp
public void Execute(in RenderPassContext ctx)
{
    var sorted = TopologicalSort(_passes);
    foreach (var pass in sorted) { ... }
}
```

Pass 之间通过 `Inputs` / `Output` 声明依赖关系，Pipeline 自动拓扑排序 —— **AI Agent 写新 Pass 只需声明输入输出，不用关心执行顺序**。

### 4.4 Per-Layer 状态隔离

```csharp
var spotlightLayer = scene.GetLayer("Instances");
spotlightLayer.RenderStateOverride = new LayerRenderState
{
    BlendOverride = BlendState.Additive,
    DepthStencilOverride = DepthStencilState.StencilTest(refValue: 1)
};
```

Layer 级 Shader/Blend/Stencil 覆盖，**SceneRenderPass 在每个 Layer 绘制前/后自动 Apply/Reset**。彻底解决 GMS "全局 GPU 状态 push/pop 易出 bug" 的痛点。

### 4.5 FBO 申请 Stencil Bit 的硬性约束

`EngineWindowOptions` 显式申请 `PreferredStencilBufferBits = 8`：

```csharp
opts.PreferredStencilBufferBits = StencilBits;  // 8
opts.PreferredDepthBufferBits = DepthBits;       // 24
```

避免 GameMaker 的"平台默认 0-bit Stencil → Stencil 测试静默失效"问题。

---

## 五、 端到端调用代码片段

`Program.cs` 主入口（核心 40 行）：

```csharp
// 1. 创建所有 RenderTarget
_rtScene    = new RenderTarget2D(gl, width, height);
_rtMasked   = new RenderTarget2D(gl, width, height);
_rtBloom    = new RenderTarget2D(gl, width, height);

// 2. 注册 4 个 Pass 到 Pipeline
_pipeline.AddPass(new SceneRenderPass("Scene", _scene, _scene.MainCamera, _rtScene));
_pipeline.AddPass(new StencilMaskPass("Stencil", gl, _scene, _scene.MainCamera, _rtMasked, _spriteShader, _white));
_pipeline.AddPass(new PostProcessPass("Bloom", gl, _bloomShader, _rtMasked, _rtBloom));
_pipeline.AddPass(_compositor = new ViewportCompositorPass("Composite", gl, _blitShader, _batch));

// 3. 配置合成 Pass 的两个 Source
_compositor.AddSource(_rtScene, ViewportRect.FullScreen, BlendState.Opaque);
_compositor.AddSource(_rtBloom, ViewportRect.FullScreen, BlendState.Additive);

// 4. 每帧 SetMaskCircle + Execute
_stencilPass.SetMaskCircle(_mouseScreen, radius: 120);
_pipeline.Execute(ctx);
```

---

## 六、 验证清单

- [x] `dotnet restore` 成功
- [x] `dotnet build` 成功（0 错 0 警）
- [x] 项目结构匹配 Phase 1.3 文档的目录设计
- [x] RenderTarget2D 同时支持 Color + Depth/Stencil
- [x] RenderPipeline 实现拓扑排序
- [x] Per-Layer 状态覆盖路径打通
- [x] Demo 场景包含 Stencil + Bloom + 多 Pass 合成
- [ ] **本地带显示环境运行**（用户在自己机器上验证）

---

## 七、 已知限制

1. **Bloom 单 Pass 实现**：真实生产应使用 2-pass 分离高斯（Horizontal → Vertical ping-pong RT 链）。当前 9-tap 单 Pass 在大尺寸下模糊不足。
2. **多 Camera 未完整演示**：Demo 只用 1 个 Camera，但 `ViewportCompositorPass.AddSource` 支持多 RT 合成，可一行接入副相机/小地图。
3. **窗口 resize 时 RT 未自动 resize**：RenderTarget2D.Resize() 已实现，需在 EngineWindow 的 Resize 事件中手动调用。

---

## 八、 下一步：Phase 1.4

Phase 1.3 已落地后，下一步进入 **C# 10 Source Generator 静态代码生成**：

- 用 `[RenderPass("Bloom")]` 标注 `PostProcessPass`，Source Generator 自动生成 `RenderPipelineRegistry.Register("Bloom", ...)` —— **零反射注册**
- 同理 `[GameEvent]` / `[System]` 标注自动接入 Event Bus / ECS System 调度

让 AI Agent 写一个标注即可接入引擎，不再写样板注册代码。

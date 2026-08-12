# Interactive Viewport 使用与实现边界

`Engine.Features.ViewportNavigation` 为每台 `Camera2D` 提供可组合的地图浏览行为。当前以 pixi-viewport 的成熟功能形状为参考，完成拖拽、双指 Pinch/平移、鼠标锚点滚轮缩放、惯性、缩放限制和世界边界限制。

它不加载地图、不创建 Sprite，也不拥有 Texture。已经实现的独立 `WorldChunkStreamer` 读取 `ViewportSnapshot` 管理空间驻留；LOD 与具体资源生命周期继续由后续消费者负责。

## 推荐的 Scene 作用域用法

Window 中的输出矩形、RenderTarget 与 Pipeline 由 Hosting 长期持有；Camera 初始状态和 Navigation 插件链由 Scene 声明。固定画面的 Home 不声明 Navigation，地图 Scene 才声明：

```csharp
using System.Numerics;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Features.ViewportNavigation;
using GameEngine.Hosting;

var world = new Bounds2D(0, 0, 12_000, 12_000);

GameApplication.Create(windowOptions)
    .UseDefault2DRenderer()
    .AddScene(
        new SceneRef("Home"),
        views => views.ConfigureMain(
            new SceneCameraState(new Vector2(120, 0))),
        ConfigureHome)
    .AddScene(
        new SceneRef("World"),
        views => views.ConfigureMain(
            new SceneCameraState(new Vector2(5_640, 5_360)),
            navigation: viewport => viewport
                .Drag()
                .Pinch()
                .Wheel(new ViewportWheelOptions(smoothFrames: 6))
                .Decelerate()
                .ClampZoom(new ViewportClampZoomOptions(
                    maxWidth: 12_000,
                    maxHeight: 12_000,
                    maxScale: 4))
                .Clamp(new ViewportClampOptions(
                    world,
                    underflow: ViewportUnderflow.Center))),
        ConfigureWorld)
    .StartScene(new SceneRef("Home"));
```

Scene 激活时 Hosting 把长期 Render View 的 Camera 重置为 `SceneCameraState`，清除旧 Pointer 捕获，并为新 Scene 创建全新的 Navigation/CameraFollow Controller。离开 World 后，惯性、拖拽、平滑滚轮和插件状态不会泄漏到 Home；切换期间仍按下的 Pointer 也不会在新 Scene 被解释为一次新按下。

Hosting 在 Scene Step 前采样鼠标与滚轮，按最上层命中的 Render View 路由输入。拖拽不会意外从一个 View 转移到另一个 View；滚轮只进入命中的 View。Resize 会先更新 Camera 尺寸，再重新执行当前 Scene 的约束。

Viewport 导航属于表现层交互，直接采样窗口指针，不进入 Gameplay Replay 的逻辑输入流。确定性玩法不应以 Camera 当前位置作为规则判定；回放、联机或权威模拟需要记录的是玩家的逻辑命令，地图剔除与 Chunk 驻留则可以只消费 `ViewportSnapshot`。

同一 Scene 的多 Camera 可分别声明。Renderer 先固定稳定输出槽位与渲染成本：

```csharp
renderer.UseRenderViews(views => views
    .ConfigureMain(ViewportRect.LeftHalf)
    .Add("editor.preview", ViewportRect.RightHalf));

// Scene 注册期再声明每个 View 的 Camera 与交互所有权。
sceneViews
    .ConfigureMain(
        SceneCameraState.Default,
        navigation: viewport => viewport.Drag().Pinch().Wheel().Decelerate())
    .Configure(
        new RenderViewRef("editor.preview"),
        new SceneCameraState(new Vector2(4_000, 4_000)),
        navigation: viewport => viewport
            .Drag()
            .Pinch()
            .Wheel()
            .Clamp(new ViewportClampOptions(world)));
```

同一 Render View 不能同时声明 `CameraFollowController` 和 Interactive Viewport，因为两者都会拥有 Camera 位置。组合根会在创建窗口前拒绝这种冲突。

旧的 `Default2DRendererOptions.UseInteractiveViewport(...)` 以及 `UseRenderViews(... navigation:)` 继续作为应用级兼容默认值，适合只有一个 Scene 或所有 Scene 确实共享同一策略的项目；Controller 仍会在 Scene 激活时重新创建。新游戏应优先使用 `SceneViewLayoutBuilder`，使没有导航的 Scene 无需通过 Pause/Enabled 开关抵消全局配置。

## 统一 Pointer 输入边界

`IInputProvider` 的 `PointerCount/GetPointer(index)` 是平台无关边界。`PointerContact` 包含稳定 `PointerId`、`PointerKind.Mouse/Touch/Pen`、屏幕位置、按下状态和 Primary 标记。已有只实现 Mouse API 的 Provider 不需要修改；默认接口实现会把左键映射为 `PointerId.Mouse`。当前 Silk.NET 桌面后端显式提供这一 Mouse Pointer，未来 Android/触控窗口后端直接返回多个 Touch Contact，Viewport 与 Pinch 不需要再修改。

Provider 可以在释放帧保留 `IsDown=false` 的 Contact，也可以立即移除 Contact；Hosting 会比较稳定 ID 并把两种形状都解释为释放。Pointer 按下时捕获到最上层命中的 Viewport 槽位，离开槽位或与其他 View 重叠后仍使用原槽位坐标，直到释放。不同 Pointer 可以同时被不同 Render View 捕获，不会误组成跨 View Pinch。

## 核心语义

- `ViewportController` 只拥有当前 Scene 的导航状态和插件，不拥有 RenderTarget 或地图内容。
- `Center`、`MoveCenter`、`MoveCorner`、`FitWidth`、`FitHeight`、`FitWorld` 和 `SetZoomAt` 使用世界/Render View 像素坐标。
- `SetZoomAt` 保证缩放前后锚点下的世界位置不变；滚轮默认使用鼠标位置。
- `IInputProvider.PointerCount/GetPointer` 统一 Mouse、Touch 与 Pen；旧鼠标 Provider 通过默认接口实现自动暴露一个稳定 `PointerId.Mouse`，Hosting 当前为平台后端路由最多 16 个并发 Pointer。
- Pinch 使用同一 Render View 捕获的两个 Pointer；双指中心平移与距离缩放可以组合，任一 Pointer 消失时结束，剩余 Pointer 可平滑接回 Drag。
- Pointer 按下后会持续捕获到原 Render View；拖入 Letterbox 或窗口外时，输入位置钉在 fitted Viewport 的最近边缘。未捕获 Pointer 位于 Letterbox 时仍判定为未命中，不会滚动、拖拽或抛出坐标映射异常。
- `Revision` 只在 Camera 空间发生变化时递增；稳定帧不会递增。
- `CaptureSnapshot()` 返回可见世界 AABB、中心、Zoom、Render View 尺寸和 Revision，是剔除、LOD 与已实现 `WorldChunkStreamer` 的稳定边界。
- Camera 旋转继续可用；Snapshot 返回旋转视图的保守世界 AABB。
- 插件以固定顺序运行；同 Key 再次添加表示替换，支持 `Pause/Resume/Remove/Reset`。
- 惯性使用按时间积分的指数衰减，同样总时间在 30 Hz 与 60 Hz 得到一致结果。
- 核心更新预热后保持零托管分配。

## ClampZoom 术语

`MinWidth/MaxWidth/MinHeight/MaxHeight` 描述“屏幕当前可见的世界跨度”，与 pixi-viewport 的命名一致：

- `maxWidth: 12_000`：不允许缩小到一次看见超过 12,000 个世界单位，因此建立最小 Zoom。
- `minWidth: 240`：不允许放大到一次只看见少于 240 个世界单位，因此建立最大 Zoom。
- `MinScale/MaxScale`：直接限制 Camera Zoom。

宽高与 Scale 可以共同声明，运行时取全部约束的交集；窗口尺寸改变后如果约束互相矛盾，会明确失败。

`ViewportUnderflow` 决定世界比视野小时贴在哪一侧。`Center` 是大地图常用默认值，也支持八方向和 `None`。

## 运动与约束插件

- `MouseEdges` 支持四边统一/独立 Insets 或中心 Radius 两种热区，速度按秒表达。安全默认 `PointerDown` 要求主鼠标键按下，因此无按键鼠标从窗外进入不会移动 Viewport；RTS 式无按键边缘滚动需显式选择 `ViewportMouseEdgesActivation.Hover`，`Always` 则接受两种状态。在 View 内离开热区时可把世界速度交给 `Decelerate`；离开 View/Window 不注入退出方向，默认也不抢占已经运行的惯性，只有 `interruptDeceleration: true` 才允许覆盖。
- `Animate` 是一次性 Center/Zoom/可见宽高过渡，完成后保持静止；可选择 Pointer 交互时 Pause、Cancel 或 Ignore。
- `Snap` 是持续位置目标，可锁定 Center 或左上角；目标被其他逻辑移开后会重新收敛。
- `SnapZoom` 是持续 Zoom/可见宽高目标；Resize 后重新解析目标 Zoom，并可指定屏幕锚点。
- `Bounce` 允许 Drag/Pinch 暂时越界，释放后以指定 Easing 回弹；它与硬 `Clamp` 是两种互斥的边界策略，Builder 会拒绝同时声明。

```csharp
viewport
    .Drag()
    .Pinch()
    .Wheel()
    .MouseEdges(new ViewportMouseEdgesOptions(
        insets: ViewportEdgeInsets.Uniform(36),
        speedPixelsPerSecond: 720,
        activation: ViewportMouseEdgesActivation.PointerDown))
    .Decelerate()
    .Bounce(new ViewportBounceOptions(world))
    .SnapZoom(new ViewportSnapZoomOptions(
        visibleWidth: 1_600,
        durationSeconds: 0.35));
```

固定执行顺序为 Drag → Pinch → Wheel → MouseEdges → Decelerate → Animate → Bounce → SnapZoom → ClampZoom → Snap → Clamp。后置约束可以校正前置运动；同一 Key 仍只有一个插件实例。`Animate` 与对应的持续 Snap 所有权冲突、Bounce 与硬 Clamp 冲突会在组合期失败。

## 与 Chunk Streaming 的关系

正确依赖方向是：

```text
Input → ViewportController → Camera2D
                    │
                    └─ ViewportSnapshot
                              ↓
                    WorldChunkStreamer
                              ↓
                    Content / Texture leases
```

`WorldChunkStreamer` 已根据可见范围和 Revision 计算 Visible、Preloaded、Retained 三层 Chunk，并提供并发、单帧启动量、最大跟踪数、取消和租约释放边界；Interactive Viewport 不知道 Chunk 是否存在，也不干预异步 IO 或显存预算。完整用法见 [World Chunk Streaming](WORLD_CHUNK_STREAMING.md)。

## 当前覆盖与后续兼容面

| pixi-viewport 功能形状 | 当前状态 |
|---|---|
| View 几何、中心、角点、Fit、可见范围 | 已实现 |
| 插件添加、替换、暂停、恢复、移除、Reset | 已实现 |
| Drag | 已实现 |
| Wheel 与可选逐帧平滑 | 已实现 |
| Decelerate | 已实现 |
| ClampZoom | 已实现 |
| Clamp 与 Underflow | 已实现 |
| 统一 Mouse/Touch/Pen Pointer 与 Pinch | 已实现；桌面 Silk 后端当前提供 Mouse，Android/触控后端可直接提供多 Pointer |
| Bounce、Animate、Snap、SnapZoom、MouseEdges | 已实现；共享交互中断与固定执行顺序 |
| Follow | 现有 `CameraFollowController` 已覆盖玩法跟随；后续统一中断语义 |
| Chunk Streaming | 已由独立 `Engine.Features.WorldStreaming` 实现，不属于 Viewport |
| LOD | 后续离线编译与运行时选择切片，不属于 Viewport |

## 行为参考与版权边界

开发期间将官方 `pixi-viewport` 6.0.3、提交 `19265f8` 下载到 `.codex-tmp/pixi-viewport-6.0.3`。整个 `.codex-tmp` 被 Git 忽略，仅用于核对公共 API、插件顺序、事件名称和可观察行为。

仓库没有纳入或翻译上游 TypeScript 源文件。C# 实现基于 MyGameEngine 的 `Camera2D` 数学、值类型输入帧、确定性 delta 和独立无窗口测试重新编写。pixi-viewport 本身使用 MIT License；如果未来直接采用其具体源码片段，必须另行保留对应版权和许可证声明。

## 验证

```powershell
dotnet run --project src/Engine.Features/ViewportNavigation.Tests/ViewportNavigation.Tests.csproj -c Release
dotnet run --project src/Engine.Features/ViewportNavigation.VisualTests/ViewportNavigation.VisualTests.csproj -c Release
dotnet run --project src/Engine.Features/ViewportNavigation.VisualTests/ViewportNavigation.VisualTests.csproj -c Release -- --smoke
```

VisualTests 使用 12,000 × 12,000、20 × 20 分块网格验证拖拽、锚点缩放、MouseEdges、惯性、最大可见范围、世界边界、Resize 和隐藏窗口释放；网格仍只验证 Viewport 本身，Chunk 驻留由独立 WorldStreaming 无窗口测试覆盖。

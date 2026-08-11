# Interactive Viewport 使用与实现边界

`Engine.Features.ViewportNavigation` 为每台 `Camera2D` 提供可组合的地图浏览行为。当前以 pixi-viewport 的成熟功能形状为参考，完成拖拽、双指 Pinch/平移、鼠标锚点滚轮缩放、惯性、缩放限制和世界边界限制。

它不加载地图、不创建 Sprite，也不拥有 Texture。Chunk Streaming、LOD 和资源生命周期是读取 `ViewportSnapshot` 的后续独立消费者。

## 最小 Hosting 用法

一台主 Camera 不需要伪造第二个 Render View：

```csharp
using System.Numerics;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Features.ViewportNavigation;
using GameEngine.Hosting;

var world = new Bounds2D(0, 0, 12_000, 12_000);

GameApplication.Create(windowOptions)
    .UseDefault2DRenderer(renderer => renderer
        .UseInteractiveViewport(viewport => viewport
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
                underflow: ViewportUnderflow.Center))))
    .ConfigureScene("World", context =>
    {
        ViewportController viewport =
            context.GetViewportNavigation(RenderViewRef.Main);
        viewport.MoveCenter(new Vector2(6_000, 6_000));
    });
```

Hosting 在 Scene Step 前采样鼠标与滚轮，按最上层命中的 Render View 路由输入。拖拽不会意外从一个 View 转移到另一个 View；滚轮只进入命中的 View。Resize 会先更新 Camera 尺寸，再重新执行 `ClampZoom` 和 `Clamp`，不会保留右侧或底部白边。

Viewport 导航属于表现层交互，直接采样窗口指针，不进入 Gameplay Replay 的逻辑输入流。确定性玩法不应以 Camera 当前位置作为规则判定；回放、联机或权威模拟需要记录的是玩家的逻辑命令，地图剔除与 Chunk 驻留则可以只消费 `ViewportSnapshot`。

多 Camera 可分别声明：

```csharp
renderer.UseRenderViews(views => views
    .ConfigureMain(
        ViewportRect.LeftHalf,
        navigation: viewport => viewport.Drag().Pinch().Wheel().Decelerate())
    .Add(
        "editor.preview",
        ViewportRect.RightHalf,
        navigation: viewport => viewport
            .Drag()
            .Pinch()
            .Wheel()
            .Clamp(new ViewportClampOptions(world))));
```

同一 Render View 不能同时声明 `CameraFollowController` 和 Interactive Viewport，因为两者都会拥有 Camera 位置。组合根会在创建窗口前拒绝这种冲突。

## 统一 Pointer 输入边界

`IInputProvider` 的 `PointerCount/GetPointer(index)` 是平台无关边界。`PointerContact` 包含稳定 `PointerId`、`PointerKind.Mouse/Touch/Pen`、屏幕位置、按下状态和 Primary 标记。已有只实现 Mouse API 的 Provider 不需要修改；默认接口实现会把左键映射为 `PointerId.Mouse`。当前 Silk.NET 桌面后端显式提供这一 Mouse Pointer，未来 Android/触控窗口后端直接返回多个 Touch Contact，Viewport 与 Pinch 不需要再修改。

Provider 可以在释放帧保留 `IsDown=false` 的 Contact，也可以立即移除 Contact；Hosting 会比较稳定 ID 并把两种形状都解释为释放。Pointer 按下时捕获到最上层命中的 Viewport 槽位，离开槽位或与其他 View 重叠后仍使用原槽位坐标，直到释放。不同 Pointer 可以同时被不同 Render View 捕获，不会误组成跨 View Pinch。

## 核心语义

- `ViewportController` 只拥有导航状态和插件，不拥有 Scene、RenderTarget 或地图内容。
- `Center`、`MoveCenter`、`MoveCorner`、`FitWidth`、`FitHeight`、`FitWorld` 和 `SetZoomAt` 使用世界/Render View 像素坐标。
- `SetZoomAt` 保证缩放前后锚点下的世界位置不变；滚轮默认使用鼠标位置。
- `IInputProvider.PointerCount/GetPointer` 统一 Mouse、Touch 与 Pen；旧鼠标 Provider 通过默认接口实现自动暴露一个稳定 `PointerId.Mouse`，Hosting 当前为平台后端路由最多 16 个并发 Pointer。
- Pinch 使用同一 Render View 捕获的两个 Pointer；双指中心平移与距离缩放可以组合，任一 Pointer 消失时结束，剩余 Pointer 可平滑接回 Drag。
- `Revision` 只在 Camera 空间发生变化时递增；稳定帧不会递增。
- `CaptureSnapshot()` 返回可见世界 AABB、中心、Zoom、Render View 尺寸和 Revision，是未来剔除、LOD 与 Chunk Streaming 的稳定边界。
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

## 与 Chunk Streaming 的关系

正确依赖方向是：

```text
Input → ViewportController → Camera2D
                    │
                    └─ ViewportSnapshot
                              ↓
                    WorldChunkStreamer（后续）
                              ↓
                    Content / Texture leases
```

未来 `WorldChunkStreamer` 根据可见范围和 Revision 计算 Visible、Preload、Retained 三层 Chunk；Interactive Viewport 不知道 Chunk 是否存在，也不干预异步 IO 或显存预算。

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
| Bounce、Animate、Snap、SnapZoom、MouseEdges | 下一 Viewport 阶段 |
| Follow | 现有 `CameraFollowController` 已覆盖玩法跟随；后续统一中断语义 |
| Chunk Streaming、LOD | 独立后续切片，不属于 Viewport |

## 行为参考与版权边界

开发期间将官方 `pixi-viewport` 6.0.3、提交 `19265f8` 下载到 `.codex-tmp/pixi-viewport-6.0.3`。整个 `.codex-tmp` 被 Git 忽略，仅用于核对公共 API、插件顺序、事件名称和可观察行为。

仓库没有纳入或翻译上游 TypeScript 源文件。C# 实现基于 MyGameEngine 的 `Camera2D` 数学、值类型输入帧、确定性 delta 和独立无窗口测试重新编写。pixi-viewport 本身使用 MIT License；如果未来直接采用其具体源码片段，必须另行保留对应版权和许可证声明。

## 验证

```powershell
dotnet run --project src/Engine.Features/ViewportNavigation.Tests/ViewportNavigation.Tests.csproj -c Release
dotnet run --project src/Engine.Features/ViewportNavigation.VisualTests/ViewportNavigation.VisualTests.csproj -c Release
dotnet run --project src/Engine.Features/ViewportNavigation.VisualTests/ViewportNavigation.VisualTests.csproj -c Release -- --smoke
```

VisualTests 使用 12,000 × 12,000、20 × 20 分块网格验证拖拽、锚点缩放、惯性、最大可见范围、世界边界、Resize 和隐藏窗口释放；它只绘制网格，不冒充已经实现 Chunk Streaming。

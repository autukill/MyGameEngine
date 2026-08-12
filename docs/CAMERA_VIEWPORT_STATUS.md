# Camera 与 Viewport 渐进式路线

## 当前能力：单 Camera、多个呈现 Viewport

第一阶段已经完成。默认 Hosting 仍只渲染一份 Scene、一台 `Camera2D` 和一条 Stencil/Bloom/Tone Mapping 链，但最终的 Display Surface 可以声明式呈现到多个屏幕槽位：

```csharp
.UseDefault2DRenderer(renderer => renderer
    .UseSingleCameraViewports(views => views
        .Add("left", ViewportRect.LeftHalf, ViewportFitMode.Cover)
        .Add("right", ViewportRect.RightHalf, ViewportFitMode.Cover)))
```

每个槽位拥有稳定 `ViewportSlotRef`、标准化 `ViewportRect`、`ViewportFitMode` 和合成 Layer。省略配置时仍使用 `main + FullScreen + Stretch`，现有游戏行为不变。

这层能力适合：同一世界画面的镜像展示、直播/观战布局、调试对照和不需要第二次世界渲染的画中画。它不等同于双人分屏或小地图，因为所有槽位看到的是同一 Camera 的同一次渲染结果。

## Fit 语义

- `Stretch`：填满槽位，允许宽高比变形；默认值用于兼容旧行为。
- `Contain`：完整显示源画面，剩余区域保持 Presentation 清屏色。黑边不参与输入命中。
- `Cover`：填满槽位并从源画面中心裁剪；输入坐标会经过相同裁剪反算。

`ViewportRect.ToPixels` 通过共享边界取整。即使窗口宽高为奇数，相邻的 `LeftHalf/RightHalf` 也不会出现一像素裂缝或重叠。

## 坐标映射

`Default2DGameContext` 提供布局感知的转换：

```csharp
if (context.TryScreenToView(context.Window.Input.MousePosition, out ViewportHit hit))
{
    Console.WriteLine($"slot={hit.Slot}, world={hit.WorldPosition}");
}

if (context.TryScreenToView(mouse, new ViewportSlotRef("right"), out ViewportHit right))
{
    // 显式限制在一个槽位内。
}
```

重叠槽位按 Layer、再按声明顺序从上到下命中。转换使用 Camera 的稳定变换，视觉震屏不会让玩法拾取位置抖动。`CaptureViewportDiagnostics()` 或聚合的 `CaptureRenderDiagnostics().Viewports` 可读取当前像素矩形、Fit 和 Layer。

默认 World Surface 自动复制到全部槽位。Stencil 等自定义世界空间输出应在 Scene 配置时调用 `context.PresentWorldSurface(surface, layer, blend)`，它会复用同一布局；SceneGui 和直接 `RequestPresentSurface` 的条目保持显式，不会被隐式复制。

## 性能边界

这一阶段只增加最终合成 Draw：

```text
Scene + Stencil + Bloom + Tone Mapping  （一次）
                         |
                  Display Surface
                   /           \
              Viewport A    Viewport B  （各一次 blit）
```

不会增加 Scene Draw Call、根 RenderTarget、Bloom 租约或效果 Pass。Runner 可用 `--mirrored-viewports` 在真实 HDR 链上验证双槽位；隐藏 smoke 当前仍为 6 个活跃 Pass。

## 当前能力：逻辑 RenderView 与真正多 Camera

第二阶段已经完成。需要不同视角时改用 `UseRenderViews`：

```csharp
.UseDefault2DRenderer(renderer => renderer
    .UseRenderViews(views => views
        .ConfigureMain(ViewportRect.LeftHalf)
        .Add(
            "player.two",
            ViewportRect.RightHalf,
            renderScale: 0.75f,
            sceneLayers: SceneLayerFilter.Include("Instances", "Effects"),
            effects: RenderViewEffects.Hdr(ToneMappingSettings.Default))))
```

`main` 保持为 `context.Camera`；额外 View 通过 `context.GetRenderView(new RenderViewRef("player.two"))` 取得。每个 `RenderView` 拥有独立 Camera、SceneColor 根 Surface、SceneRenderPass、RenderScale 和 Viewport，但不公开其 RenderTarget。resize 会按槽位像素尺寸与 RenderScale 同步 Camera 和目标。

每个 View 可在 `ConfigureMain/Add(..., cameraFollow: settings)` 中声明独立跟随策略，获得 Anchor、Dead Zone、平滑、世界边界和叠加震屏；Hosting 只创建控制器，Gameplay 通过 `context.GetCameraFollow(viewRef)` 显式传入或切换目标。控制器不拥有 Scene/RenderTarget，也不需要继承 GameInstance。完整用法见[Camera 跟随指南](CAMERA_FOLLOWING.md)。

```csharp
RenderView second = context.GetRenderView(new RenderViewRef("player.two"));
second.Camera.Position = new Vector2(800, 0);
second.Camera.Zoom = 0.75f;
```

`SceneLayerFilter.Include(...)` 适合只显示世界或小地图层，`Exclude(...)` 适合隐藏主视图专属装饰；默认 `All` 保持兼容行为。它组合现有 `SceneLayerConfig.IsVisible`：层必须同时全局可见且被 View 允许才会绘制。Background 不属于 Instance Layer，因此不会被过滤。Scene 内部按 Layer 索引实例，View 只访问允许层中的候选；过滤检查与绘制保持零稳态分配，但每个 View 仍会独立排序并绘制其可见实例。

每个 View 还会用自己的 Camera 进行排序前粗剔除。默认 Sprite 从 Size/Origin 推导视觉边界；自定义绘制可设置 `LocalDrawBounds`，跨屏幕/跨世界绘制可选择 `InstanceViewCullingMode.AlwaysVisible`。未知边界始终保留，Collider 不会被误作视觉边界。旋转 Camera 使用可见四角的世界 AABB，保证保守而非精确多边形剔除。

`ViewportHit.View` 会标识命中的 Render View，坐标通过该 View 自己的 Camera 与源分辨率反算。诊断同时报告呈现像素矩形、`RenderWidth/RenderHeight` 和 `SceneLayers`。

当前多 Camera 策略：

- 每个 View 重绘同一 Scene，但使用独立 Camera。
- 主 View 继续由 `UseHdr` 和 `EnableStencilMasking` 配置。
- 次级 View 默认 `Direct`，可显式选择独立 HDR + Tone Mapping，以及可选 Bloom；不会从主 View 隐式继承。
- Bloom 和 Tone Mapping 的租赁目标从实际输入 Surface 解析尺寸，保证每条链跟随所属 View 的 RenderScale。
- resize 按 Viewport 与 RenderScale 重建目标。
- 诊断明确列出每个 View 的 Pass、RT 显存和 Draw Call 成本。
- `UseRenderViews` 与 `UseSingleCameraViewports` 互斥，避免“重绘”和“镜像呈现”语义混淆。
- Runner `--split-cameras` 验证“主 HDR + Bloom + Stencil + 全部层 / 次级 HDR + Tone Mapping + 排除 MainOnly”；当前为 8 Pass、3 个根目标和 6 个动态租约，次级输出租约使用自己的 0.75 RenderScale 尺寸。

## 后续阶段

### Interactive Viewport 第一阶段（已完成）

`Engine.Features.ViewportNavigation` 已把地图浏览行为从游戏手写 Camera 逻辑中拆出。`SceneViewLayoutBuilder` 让每个 Scene 为主 Camera 或多个 Render View 分别声明 Camera 初态、Drag、双指 Pinch、鼠标锚点 Wheel、帧率无关 Decelerate、ClampZoom 和世界边界；Renderer 级 `UseInteractiveViewport` 保留为兼容默认值。Hosting 在 Scene Step 前按最上层命中 View 路由 Mouse/Touch/Pen Pointer 与滚轮；切换 Scene 时清理 Pointer 捕获、重置 Camera 状态并重建 Controller，Resize 后重新执行当前 Scene 的缩放和边界约束。

Scene View 还可以声明 `SceneCameraViewportPolicy`。它与 Presentation 的 `Stretch/Contain/Cover` 不同：后者决定已经渲染好的 Surface 如何放入屏幕槽位，前者决定窗口 Resize 后 Camera 实际能看见多少世界。`FixedVisibleHeight/FixedVisibleWidth` 保护指定轴；`Expand` 取宽高缩放的较小值，保证完整参考画面并在另一轴显示更多世界；`Cover` 取较大值，填满输出并裁切另一轴。`WithAnchor` 决定 resize 时保持稳定的世界点；`FixedVisibleHeight/Expand` 可用 `WithMaximumVisibleSize` 限制 Overscan，超限后自动缩小内容 RenderTarget、以 Contain 留边并从输入命中中排除黑边。`SceneCameraFramingResult` 是可离线测试的纯结果，公开 Scale、VisibleWorldSize、Content 尺寸与 ContentRect。默认 `MatchRenderTarget` 继续适合编辑器、大地图及希望像素尺寸直接决定可见范围的场景。

`Camera.VisualTests` 已加入可交互构图标尺：蓝色外框为 `1280×720` Overscan，黄色框为 `960×540` Reference View，绿色框为 `800×450` Design Safe Frame。拖动窗口可直接观察扩展、裁切或超限留边；`TAB` 依次切换 Bounded FixedHeight、Bounded Expand、四种基础策略与 MatchRenderTarget，`SPACE` 重置当前策略，窗口标题报告实际世界可见尺寸和 Content 尺寸。隐藏窗口 smoke 与纯数学测试覆盖极端 `3440×900` 超宽输出、窄屏、锚点和留边输入边界。

`ViewportSnapshot` 固定可见世界 AABB、中心、Zoom、Render Size 与 Revision，作为 Chunk Streaming/LOD 的只读消费边界。独立 `Engine.Features.WorldStreaming` 已消费该边界，提供 Visible/Preloaded/Retained 驻留、加载预算、取消和租约释放；Viewport 本身仍不加载 Chunk、不拥有 Texture。完整用法见 [Interactive Viewport](INTERACTIVE_VIEWPORT.md) 与 [World Chunk Streaming](WORLD_CHUNK_STREAMING.md)。

### 阶段 3：显式效果策略（已完成）

每 View Layer 过滤与显式效果策略已经完成。`Direct`、HDR + Tone Mapping、HDR + Bloom + Tone Mapping 三档配置直接暴露额外 Pass/RT 成本，次级 View 不会隐式继承主链。每个 View 的诊断现在还报告 Scene 候选访问、剔除、选择/绘制实例和可选 CPU 分项耗时。Layer/Depth 有序索引已修正 `Layer × Scene` 放大并消除 Draw 阶段重复排序；10,000 实例双 View 调度约从最初 `1.536 ms` 降至 `0.470 ms`，保持 0 B/frame。

### 阶段 4：高级终端

最后再考虑多窗口、离屏导出、动态 Viewport 动画与编辑器预览。这些能力不会改变 `RenderView -> RenderSurface -> Presentation slot` 的主边界。

## 当前明确不支持

- 次级 View 的 Stencil；HDR、Bloom 和 Tone Mapping 已可独立声明。
- 跨 View 可见性缓存或通用空间索引；当前 Layer 候选已索引并逐 View 粗剔除，但各 View 仍独立检查、排序并绘制可见实例。
- 多窗口与多个默认 framebuffer 终端。
- 独立 `WorldChunkStreamer` 与 LOD 消费者；统一 Pointer/Pinch 和 Bounce/Animate/Snap/SnapZoom/MouseEdges 已完成。
- 跨窗口鼠标捕获与编辑器 Dock。
- Chunk Streaming、异步 IO、LOD 和显存预算；它们将作为 ViewportSnapshot 的独立消费者。

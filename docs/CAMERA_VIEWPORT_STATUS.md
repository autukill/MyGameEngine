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

## 后续阶段

### 阶段 2：逻辑 RenderView 与真正多 Camera

引入稳定 `RenderViewRef` 和声明式 View 注册。每个 View 明确 Camera、输出 Surface、RenderScale 与目标 Viewport，由 Hosting 为其拥有独立 Scene RenderTarget/Pass。Presentation 继续作为唯一屏幕终端，并复用本阶段的槽位、Fit 和坐标映射。

第一版真正多 Camera 先限定：

- 每个 View 重绘同一 Scene，但使用独立 Camera。
- 每个 View 独立 SceneColor；默认不复制 Bloom/Tone Mapping 等后处理。
- resize 按 Viewport 与 RenderScale 重建目标。
- 诊断明确列出每个 View 的 Pass、RT 显存和 Draw Call 成本。
- 用双人分屏和小地图验证，不先引入通用可见性剔除。

### 阶段 3：每 View 渲染策略

在真实多 Camera 稳定后，再增加 Layer 过滤、每 View 后处理选择、重叠输入优先级和可选不同分辨率。此时才讨论共享阴影/后处理、可见性缓存等性能优化。

### 阶段 4：高级终端

最后再考虑多窗口、离屏导出、动态 Viewport 动画与编辑器预览。这些能力不会改变 `RenderView -> RenderSurface -> Presentation slot` 的主边界。

## 当前明确不支持

- Hosting 中的第二台 Camera、双人分屏和真正小地图。
- 每槽位独立后处理或 Layer 过滤。
- 多窗口与多个默认 framebuffer 终端。
- Viewport 动画、鼠标捕获策略和编辑器 Dock。

高级用户仍可手工创建多个 `Camera2D + SceneRenderPass + RenderTarget2D`，但需要自行管理 resize、效果依赖和释放；这不是当前推荐的开发者路径。

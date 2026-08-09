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

```csharp
RenderView second = context.GetRenderView(new RenderViewRef("player.two"));
second.Camera.Position = new Vector2(800, 0);
second.Camera.Zoom = 0.75f;
```

`SceneLayerFilter.Include(...)` 适合只显示世界或小地图层，`Exclude(...)` 适合隐藏主视图专属装饰；默认 `All` 保持兼容行为。它组合现有 `SceneLayerConfig.IsVisible`：层必须同时全局可见且被 View 允许才会绘制。Background 不属于 Instance Layer，因此不会被过滤。过滤检查与绘制保持零稳态分配，但每个 View 仍会独立遍历并绘制其可见实例。

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

### 阶段 3：显式效果策略（已完成）

每 View Layer 过滤与显式效果策略已经完成。`Direct`、HDR + Tone Mapping、HDR + Bloom + Tone Mapping 三档配置直接暴露额外 Pass/RT 成本，次级 View 不会隐式继承主链。每个 View 的诊断现在还报告 Scene 候选访问、选择/绘制实例、排序比较和可选 CPU 分项耗时。Runner 小样本为主/observer `48/36` 次候选访问和 `12/11` 次 Draw 回调，证实 Layer 数会线性放大当前全 Scene 扫描；下一步需要更大实例量的稳定基准再决定缓存边界。

### 阶段 4：高级终端

最后再考虑多窗口、离屏导出、动态 Viewport 动画与编辑器预览。这些能力不会改变 `RenderView -> RenderSurface -> Presentation slot` 的主边界。

## 当前明确不支持

- 次级 View 的 Stencil；HDR、Bloom 和 Tone Mapping 已可独立声明。
- 跨 View 可见性缓存或通用空间剔除；当前每个 View 独立遍历并绘制被 Layer 过滤后的实例。
- 多窗口与多个默认 framebuffer 终端。
- Viewport 动画、鼠标捕获策略和编辑器 Dock。

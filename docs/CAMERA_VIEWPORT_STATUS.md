# Camera 与 Viewport 当前边界

## 当前结论

引擎底层已经具备组合多个 Camera 和 Viewport 所需的主要零件，但默认 Hosting API 目前只正式支持单主 Camera、单 Scene View 和全窗口输出。

已支持：

- `Camera2D` 是独立对象，可手工创建多个实例，并分别设置 Position、Zoom、Rotation、ViewportSize 和 Shake。
- `SceneRenderPass` 接受显式 `Camera2D` 与 `RenderTarget2D`，因此高级装配可以用不同 Camera 重绘同一 Scene。
- `ViewportRect` 使用标准化屏幕坐标描述输出区域。
- `ViewportCompositorPass` 与 Presentation 能将多个 RenderTarget 按顺序、Viewport 和 Blend 状态合成到屏幕，可表达分屏或画中画的最终合成。

默认 Hosting 尚未支持：

- `Default2DGameContext` 只公开一个 `Camera`。
- `Default2DGameRuntime` 只创建一个 Scene RenderTarget、一个 `SceneRenderPass` 和一个 `SceneColor` 根 Surface。
- resize 只更新这一台 Camera 和这一组根目标。
- Stencil、Bloom、Tone Mapping 与 Presentation 默认链都绑定单一 Scene View。
- 没有声明式 View 注册、每 View 渲染比例、Layer 过滤、Camera 选择或输入坐标转换 API。

所以目前不能仅通过 Hosting 配置直接实现双人分屏、小地图或安全的多视口后处理。高级用户可以手工添加 Pass 和 RenderTarget，但需要自行承担 resize、资源所有权、效果链和释放顺序；这还不是推荐的游戏开发者路径。

## 推荐的后续垂直切片

后续应围绕逻辑 View，而不是简单地把 `List<Camera2D>` 暴露出来：

```csharp
views.Add("player.one", view => view
    .UseCamera(playerOneCamera)
    .PresentTo(new ViewportRect(0f, 0f, .5f, 1f)));

views.Add("minimap", view => view
    .UseCamera(minimapCamera)
    .RenderScale(.5f)
    .PresentTo(ViewportRect.TopRightQuarter));
```

每个 View 应明确拥有：

- 稳定 `ViewRef` 和 Camera。
- 标准化屏幕 Viewport 与内部渲染尺寸/比例。
- 独立逻辑 RenderSurface 和 RenderTarget 生命周期。
- 可选的 Layer 过滤与每 View 后处理链。
- resize 重建规则。
- `ScreenToWorld` / `WorldToScreen`，并在重叠 View 时给出明确的输入命中顺序。

Presentation 继续作为唯一屏幕终端；多个 View 只生产和提交 Surface，不直接写默认 framebuffer。这样分屏、小地图和画中画可以复用现有 RenderSurface DAG、RenderTargetPool 与原子重建边界。

## 性能边界

每增加一个 Scene View，通常意味着再绘制一次可见世界，并拥有独立 Scene RenderTarget；Draw Call、批处理、像素填充和后处理成本都会增加。第一版应提供每 View RenderScale，并把 View 数量、目标显存和 Pass 统计纳入现有诊断，但暂不提前实现通用可见性剔除或共享后处理结果。

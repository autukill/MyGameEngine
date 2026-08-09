# 显式 Presentation 与 HDR/LDR UI 边界

Presentation 是渲染依赖图的唯一屏幕终端。Bloom、Tone Mapping 和 Stencil Runtime 只发布逻辑 Surface，不再通过隐藏的合成源直接修改屏幕；`PresentSurfaceDescriptor` 明确声明哪个 RGBA8/Display Surface、以什么层级和混合状态进入 framebuffer。

## 固定颜色边界

Runner 的主链为：

```text
SceneRenderPass  -> SceneColor RGBA16F/Linear
                  -> Bloom.glow RGBA16F/Linear
                  -> ToneMapping.color RGBA8/Display ─┐

StencilMaskPass  -> StencilMask.mask RGBA8/Display ───┼-> present:main -> Screen
SceneGuiRenderPass -> SceneGui RGBA8/Display ──────────┘
```

推荐层级为：

- `0`：Tone Mapping 主画面，`Opaque`。
- `100`：Stencil 或其他 LDR 世界叠加层，`AlphaBlend`。
- `1000`：`SceneGui`，`AlphaBlend`。

GUI 在独立 RGBA8 根 Surface 中绘制，并且只在 Tone Mapping 完成后呈现，因此不会参与曝光、Bloom 阈值提取或 HDR 钳制。

## GameInstance 声明终端输入

```csharp
// TonePresentationController.OnCreate
this.RequestPresentSurface(
        ToneMappingEffectDescriptor.ColorOutput(
            ToneMappingEffectDescriptor.DefaultKey),
        scene.RaiseEvent,
        layer: 0,
        blend: PresentationBlendMode.Opaque);

// GuiPresentationController.OnCreate（另一个 GameInstance）
this.RequestPresentSurface(
        RenderSurfaceKey.SceneGui,
        scene.RaiseEvent,
        layer: 1000,
        blend: PresentationBlendMode.AlphaBlend);
```

一个 owner 在同一 EffectKey 下只能持有一个描述符；需要同时呈现多个 Surface 时，应使用多个职责单一的 GameInstance。所有请求共享唯一 `present:main` Runtime。

多个 owner 声明完全相同的 Source、Viewport、Layer 和 Blend 时会合并为一个绘制条目；不同条目按 Layer、再按 owner ID 稳定排序。Presentation 仅接受 RGBA8/Display 输入，HDR Surface 必须先经过 Tone Mapping。

## 组合根装配

```csharp
var guiTarget = new RenderTarget2D(gl, new RenderTargetDescriptor(
    width,
    height,
    RenderTargetColorFormat.Rgba8,
    RenderTargetDepthStencilFormat.None));

pipeline.AddPass(new SceneGuiRenderPass(
    "SceneGui",
    gl,
    scene,
    guiTarget));

var builder = new ScenePipelineBuilder(
    pipeline,
    targetPool,
    width,
    height);

builder.RegisterRootSurface(RenderSurfaceKey.SceneGui, guiTarget);
builder.RegisterFactory(new PresentationEffectFactory(gl, blitShader, batch));
```

`SceneGuiRenderPass` 每帧把目标清为透明色、建立屏幕正交投影，再调用 `SceneAggregate.DrawGUI`。GUI RenderTarget 由组合根拥有，resize 时与 Scene RT 一起调整尺寸。

Presentation Runtime 自己拥有动态 `ViewportCompositorPass`，执行前清空默认 framebuffer，并按描述符顺序绘制所有输入。它不租用 RenderTarget，也不发布输出；其 Pass 的 `Output` 为屏幕终端 `null`。

## 生命周期与原子性

- Source 集合改变会改变 `RenderEffectPlan` 输入并触发全动态子图原子重建。
- 只改变 Viewport、Layer 或 Blend 时，Runtime 可原地更新条目。
- 删除一个仍被 Present 消费的上游效果会在修改当前图前失败；应在同一事件批次释放 Present owner 与生产者 owner。
- 最后一个 Present owner 离开后，终端 Pass 被移除；下一帧由宿主默认 framebuffer 行为决定，因此正常 Scene 应保留至少一个基础呈现 owner。
- 关闭顺序为 Builder → Pipeline → Pool → 根 RenderTarget → Shader/Batch。

## 当前边界

- 仅支持一个 `present:main` 屏幕终端。
- 输入固定为 RGBA8/Display；没有隐式 Tone Mapping、色域转换或 sRGB framebuffer。
- Viewport 使用 `[0,1]` 归一化矩形，必须完全位于屏幕内。
- 暂无 letterbox 策略对象、离屏最终输出、跨 Scene 呈现或多窗口终端。

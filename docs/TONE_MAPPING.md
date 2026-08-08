# HDR 与 Tone Mapping 使用指南

Tone Mapping 是独立的动态效果切片，负责把线性 HDR Scene 与可选 HDR Bloom Glow 合并，应用曝光和色调映射，再输出可显示的 RGBA8 Surface。当前 Runner 使用：

```text
Scene RGBA16F/Linear
  -> Bloom RGBA16F/Linear (SurfaceOnly)
  -> Tone Mapping RGBA8/Display
  -> Viewport Compositor
```

## 创建 HDR Scene

```csharp
var sceneTarget = new RenderTarget2D(gl, new RenderTargetDescriptor(
    width,
    height,
    RenderTargetColorFormat.Rgba16Float,
    RenderTargetDepthStencilFormat.Depth24Stencil8));

builder.RegisterRootSurface(
    RenderSurfaceKey.SceneColor,
    sceneTarget,
    RenderSurfaceEncoding.Linear);
```

`RenderTargetPool` 把颜色格式作为完整复用键的一部分。相同尺寸的 RGBA8 与 RGBA16F 目标不会相互复用；resize 会清理不再匹配的空闲目标。

## 实例声明

```csharp
public override void OnCreate()
{
    this.RequestBloom(
        BloomSettings.Default,
        _raiseEvent,
        colorFormat: RenderTargetColorFormat.Rgba16Float,
        encoding: RenderSurfaceEncoding.Linear,
        presentation: BloomPresentation.SurfaceOnly);

    this.RequestToneMapping(
        ToneMappingSettings.Default,
        _raiseEvent,
        bloomSource: BloomEffectDescriptor.GlowOutput(
            BloomEffectDescriptor.DefaultKey));
}

public override void OnDestroy()
{
    this.ReleaseToneMapping(_raiseEvent);
    this.ReleaseBloom(_raiseEvent);
}
```

同一个事件批次可以同时请求 Bloom 与 Tone Mapping。逻辑依赖图会先创建 Bloom，再把 Glow 解析给 Tone Mapping；删除时应在同一批次释放消费者和生产者。

## 设置

| 设置 | 默认值 | 有效范围 | 作用 |
|---|---:|---:|---|
| `Operator` | `Aces` | Aces/Reinhard | 色调映射曲线 |
| `Exposure` | `0` EV | `[-10, 10]` | 映射前乘以 `2^Exposure` |
| `Gamma` | `2.2` | `(0, 4]` | 映射后转换到显示输出 |

Exposure、Gamma 与 Operator 都是 Shader uniform，更新时不重建 RenderTarget。Source 或 BloomSource 改变会改变逻辑 Plan，并触发动态效果图的原子重建。

## 工厂和所有权

```csharp
var shader = new ToneMappingShader(gl);
builder.RegisterFactory(new ToneMappingEffectFactory(gl, shader));
```

每个 Tone Mapping Key 租用一个全尺寸 RGBA8 输出目标，以 Opaque、Order `0` 加入 Viewport Compositor。Stencil 使用 Order `100`，直接显示的 LDR Bloom 使用 Order `200`，因此挂接顺序不会改变最终叠加关系。

关闭顺序为 Builder → Pipeline → Pool → Scene RT → Tone Mapping/Bloom Shader。Runtime 只拥有租约和 Pass；Shader 与 HDR Scene 始终由组合根拥有。

## 失败与限制

- Tone Mapping 的 Scene 与 Bloom 输入必须是 RGBA16F/Linear，输出固定为 RGBA8/Display。
- 格式或编码不匹配会在 GPU 分配前由 Surface Planner 拒绝。
- 当前没有自动曝光、亮度直方图、白平衡、LUT、色域转换或 sRGB framebuffer。
- LDR UI 尚未被建模为 Tone Mapping 之后的独立逻辑节点；下一阶段会显式收口 Present 与 UI 顺序。

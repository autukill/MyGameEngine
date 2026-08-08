# Bloom 效果使用指南

Bloom 是独立于 Stencil 的动态渲染效果。它默认读取组合根注册的 SceneColor，也可以读取另一个动态效果的逻辑输出；随后提取高亮区域，执行水平/垂直分离高斯模糊，最后以 Additive 混合回屏幕。

## 实例声明

```csharp
public sealed class SceneBloomController : GameInstance
{
    private readonly Action<IDomainEvent> _raiseEvent;

    public override void OnCreate() => this.RequestBloom(
        new BloomSettings(
            threshold: 0.35f,
            intensity: 1.25f,
            blurRadius: 1f,
            iterations: 2,
            resolution: BloomResolution.Half),
        _raiseEvent);

    public override void OnDestroy() =>
        this.ReleaseBloom(_raiseEvent);
}
```

`RequestBloom` 在实例失活时为 no-op。相同 `RenderEffectKey` 的多个 owner 共享一条效果链，并且必须提供完全一致的设置；需要两套配置时应使用不同 Slot。

## 设置边界

| 设置 | 默认值 | 有效范围 | 作用 |
|---|---:|---:|---|
| `Threshold` | `0.35` | `[0, 1]` | Rec.709 亮度硬阈值 |
| `Intensity` | `1.25` | `(0, 8]` | 最后一次垂直模糊的输出倍率 |
| `BlurRadius` | `1.0` | `(0, 4]` | 按中间纹理尺寸归一化的采样跨度 |
| `Iterations` | `2` | `1..8` | 水平/垂直模糊轮数 |
| `Resolution` | `Half` | Full/Half/Quarter | Bright、Ping、Pong 中间目标尺寸 |

降采样尺寸使用向上取整并且最小为 `1×1`。改变阈值、强度、半径或轮数会原地更新运行时；改变分辨率会请求 `ScenePipelineBuilder` 原子重建活跃效果。

## 组合根装配

```csharp
var extractShader = new BloomExtractShader(gl);
var blurShader = new GaussianBlurShader(gl);

builder.RegisterFactory(new BloomEffectFactory(
    gl,
    extractShader,
    blurShader));
```

组合根需先注册 SceneColor：

```csharp
builder.RegisterRootSurface(RenderSurfaceKey.SceneColor, sceneRenderTarget);
```

每个 Bloom Key 会从 `RenderTargetPool` 租用三个 RGBA8 目标：Bright、Ping 和 Pong。Factory 的逻辑 Plan 声明 Source 输入与 `BloomEffectDescriptor.GlowOutput(key)` 输出；单个 `BloomPass` 内部执行：

```text
RT_Scene -> Bright (Rec.709 threshold)
Bright / previous Pong -> Ping (horizontal Gaussian)
Ping -> Pong (vertical Gaussian; final iteration applies intensity)
Pong -> ViewportCompositor (Additive)
```

高斯模糊每个方向固定五次采样：中心权重 `0.227027`，`±1.384615` 权重 `0.316216`，`±3.230769` 权重 `0.070270`。BlurRadius 会乘到按目标纹理宽高计算的采样方向上。

`RequestBloom` 的可选 `source` 参数可以指向另一个效果输出。共享同一 Bloom Key 的 owner 必须同时使用相同设置和相同 Source。改变 Source 或 Resolution 会原子重建效果图；其他设置原地更新。

## Resize、释放与所有权

- resize 会先创建并挂接三张新尺寸中间目标；成功后才移除旧链并归还旧租约。
- 创建或挂接失败时，旧 Pass 图和旧租约保持有效。
- 最后一个 owner 释放、失活或销毁后，Pass 与合成源被移除，三个租约全部归还。
- Shader 由组合根拥有；关闭顺序为 Builder → Pipeline → Pool → Scene RT → Bloom Shader。

## 当前边界

- 输入必须是当前 Scene 中已注册的根 Surface 或活跃效果输出。
- 中间目标固定为 RGBA8；高强度结果会被钳制，不提供 HDR。
- 暂不支持软阈值 Knee、Mip Pyramid、Temporal Bloom、Lens Dirt 或 Anamorphic Bloom。
- 不支持 MSAA 和多颜色 Attachment。

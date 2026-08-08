# 逻辑 RenderSurface 与动态效果依赖图

逻辑 RenderSurface 用纯值对象描述动态效果之间的纹理数据流。领域描述符只引用 Key 与格式契约，不会持有 `RenderTarget2D`、GL 句柄、Shader 或 Pass。

## Surface 标识

```csharp
RenderSurfaceKey scene = RenderSurfaceKey.SceneColor;

var bloomKey = new RenderEffectKey("bloom", "main");
RenderSurfaceKey glow = RenderSurfaceKey.FromEffect(bloomKey, "glow");
```

Surface 名称由 ProducerKind、ProducerSlot 和 Output 组成，采用大小写敏感比较。一个 Surface 只能有一个生产者；效果不能发布属于其他 EffectKey 的输出，也不能覆盖组合根注册的根 Surface。

`RenderSurfaceSpec` 进一步声明物理存储格式与颜色编码：

```csharp
RenderSurfaceSpec hdrScene = RenderSurfaceSpec.Hdr(RenderSurfaceKey.SceneColor);
RenderSurfaceSpec ldrOutput = RenderSurfaceSpec.Ldr(outputKey);
```

`Hdr` 等价于 RGBA16F/Linear，`Ldr` 等价于 RGBA8/Display。生产者与消费者必须完全匹配，否则依赖图会在创建 GPU 资源前拒绝装配。

## 注册根 Surface

```csharp
var builder = new ScenePipelineBuilder(
    pipeline,
    compositor,
    targetPool,
    width,
    height);

builder.RegisterRootSurface(
    RenderSurfaceKey.SceneColor,
    sceneRenderTarget,
    RenderSurfaceEncoding.Linear);
```

根 Surface 是组合根拥有的借用资源，Builder 不负责释放。未显式传 Encoding 时，RGBA16F 默认 Linear，RGBA8 默认 Display。必须在第一个动态效果创建前完成注册；效果活跃期间不能替换根 Surface。RenderTarget resize 保持对象身份与格式不变，因此不需要重新注册。

## Factory 规划与 Runtime 输出

Factory 在分配 GPU 资源前返回纯逻辑计划：

```csharp
public RenderEffectPlan Plan(
    RenderEffectKey key,
    IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners)
{
    ValidateOwners(owners);
    return new RenderEffectPlan(
        key,
        inputSurfaces: new[] { RenderSurfaceSpec.Hdr(RenderSurfaceKey.SceneColor) },
        outputSurfaces: new[] { RenderSurfaceSpec.Ldr(RenderSurfaceKey.FromEffect(key, "color")) });
}
```

创建 Runtime 时通过 resolver 获取已经可用的上游表面：

```csharp
RenderTarget2D source = context.Surfaces.Resolve(inputKey);
```

Runtime 必须按 Plan 的顺序暴露完全一致的输出：

```csharp
Outputs = new[]
{
    new RenderEffectOutput(outputKey, outputLease.Target)
};
```

Plan 与 Runtime 输出不一致会在挂接前失败，并释放本次新建资源。

## 依赖规划与原子重建

`ScenePipelineBuilder` 在修改当前图之前执行：

1. 收集并验证所有 Factory Plan。
2. 建立 Surface → Producer 唯一映射。
3. 拒绝缺失输入、格式/编码不匹配、重复生产者、外来输出与依赖循环。
4. 按依赖关系拓扑排序；互不依赖的效果按 EffectKey 稳定排序。
5. 在临时 Surface Registry 中按序创建 Runtime。
6. 全部创建并挂接成功后，才逆序移除旧图。

拓扑、输入、输出或结构参数改变时，v1 会原子重建整个动态效果子图。普通参数变化仍调用 `UpdateOwners` 原地更新。创建、输出校验或图挂接失败时，旧 Runtime、Pass、合成源和租约继续有效。

删除生产者但保留消费者会因缺失输入而被拒绝；在同一事件批次中同时删除消费者和生产者则可以成功。

## Bloom 串联示例

```csharp
var upstream = new RenderEffectKey("bloom", "main");
var downstream = new RenderEffectKey("bloom", "secondary");

first.RequestBloom(
    BloomSettings.Default,
    scene.RaiseEvent,
    upstream,
    RenderSurfaceKey.SceneColor);

second.RequestBloom(
    BloomSettings.Default,
    scene.RaiseEvent,
    downstream,
    BloomEffectDescriptor.GlowOutput(upstream));
```

对应数据流：

```text
SceneColor
   -> Bloom(main).glow
      -> Bloom(secondary).glow
```

两个 Bloom 仍分别拥有 Bright/Ping/Pong 三个中间目标，并按拓扑顺序添加 Additive 合成源。

HDR 呈现链使用类型化契约：

```text
SceneColor RGBA16F/Linear
   -> Bloom.glow RGBA16F/Linear (SurfaceOnly)
      -> ToneMapping.color RGBA8/Display
         -> ViewportCompositor
```

## 当前边界

- Surface 支持 RGBA8/Display 与 RGBA16F/Linear；暂不提供隐式格式或颜色编码转换。
- v1 在结构变化时重建整个动态子图，尚未计算最小受影响下游集合。
- 屏幕 framebuffer 不是可消费 Surface；最终输出仍通过 Compositor 完成。
- 不支持历史帧、跨 Scene Surface、MSAA resolve 或多颜色 Attachment。
- 屏幕呈现仍通过 Runtime 合成源完成，尚未建模为显式逻辑 Present 节点。

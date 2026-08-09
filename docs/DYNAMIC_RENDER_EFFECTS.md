# 动态渲染效果使用指南

动态效果切片把“实例需要什么效果”和“如何创建 OpenGL Pass/RenderTarget”分开。GameInstance 只发出领域事件，`ScenePipelineBuilder` 在 Step 与 Draw 之间把持久 owner 状态差量映射为 RenderPass 图。

## 数据流

```text
GameInstance.OnCreate / OnStep / OnDestroy
                │
                ▼
RenderEffectRequestedEvent / RenderEffectReleasedEvent
                │
                ▼
SceneAggregate.DrainUncommittedEvents()
                │
                ▼
ScenePipelineBuilder.ApplyEvents(...)
     ├─ owner 状态校验与合并
     ├─ IRenderEffectFactory.Plan(...)
     ├─ RenderSurface 依赖校验与稳定拓扑排序
     ├─ RenderTargetPool.Rent(...)
     └─ 原子添加/更新/移除 Pass 与逻辑输出
                │
                ▼
RenderPipeline.Execute(...)
```

领域描述符不能携带 GL、Shader、RenderPass、RenderTarget 或 `Action` 绘制回调。GPU 依赖只存在于 Factory 和 Runtime。

## 效果标识与共享

```csharp
var key = new RenderEffectKey(
    kind: "stencil-mask",
    slot: "main");
```

- `Kind` 用于选择 `IRenderEffectFactory`。
- `Slot` 区分同一种效果的多个逻辑实例。
- 相同 Key 的多个 owner 共享一份 Runtime、Pass 和 RenderTarget。
- 同一个 owner 重复请求相同 Key 表示更新描述符，不会重建 Runtime。
- 最后一个 owner 释放、失活或销毁后，Builder 才移除效果并归还 RenderTarget。

名称采用大小写敏感比较，Kind 和 Slot 都不能是空字符串。

## GameInstance 声明 Stencil Spotlight

```csharp
public sealed class SpotlightController : GameInstance
{
    private readonly Action<IDomainEvent> _raiseEvent;

    public override void OnCreate() => this.RequestPresentSurface(
        StencilMaskEffectDescriptor.MaskOutput(
            StencilMaskEffectDescriptor.DefaultKey),
        _raiseEvent,
        layer: 100,
        blend: PresentationBlendMode.AlphaBlend);

    public override void OnStep(double deltaTime)
    {
        if (Input is null) return;

        this.RequestStencilMask(
            center: Input.MousePosition,
            radius: 120f,
            state: StencilMaskState.Spotlight,
            raiseEvent: _raiseEvent);
    }

    public override void OnDestroy()
    {
        this.ReleasePresentSurface(_raiseEvent);
        this.ReleaseStencilMask(_raiseEvent);
    }
}
```

`StencilMaskEffectDescriptor` 保存 Key、`StencilMaskGeometry` 和 `StencilMaskState`。Circle 的中心和半径必须为有限值，半径必须大于零；SpriteAlpha 还可保存 Sprite 帧、Transform 与 AlphaCutoff。

共享同一个 Stencil Key 的 owner 可以拥有不同中心和半径，但 `Mode`、`StencilRef` 和 `MaskBits` 必须一致。状态冲突会在修改 Pass 图之前失败；需要不同状态时应使用不同 Slot。

## 组合根装配

```csharp
var targets = new RenderTargetPool(gl);
var builder = new ScenePipelineBuilder(
    pipeline,
    targets,
    window.Width,
    window.Height);

builder.RegisterRootSurface(
    RenderSurfaceKey.SceneColor,
    sceneRenderTarget);

builder.RegisterFactory(new StencilMaskEffectFactory(
    gl,
    scene,
    camera,
    spriteShader,
    stencilMaskShader,
    whiteTexture,
    textures,
    sprites));

builder.RegisterFactory(new BloomEffectFactory(
    gl,
    bloomExtractShader,
    gaussianBlurShader));

builder.RegisterFactory(new PresentationEffectFactory(
    gl,
    blitShader,
    batch));
```

Stencil 与 Bloom 是两个效果生产者；Presentation 是消费其 RGBA8/Display 输出的唯一屏幕终端：

```text
StencilMaskPass -> StencilMask.mask RGBA8/Display
StencilMask.mask -> Presentation (AlphaBlend)

RT_Scene -> Bright -> Ping(H) -> Pong(V)
RT_Pong  -> Bloom.glow
```

组合根不保存动态 Stencil/Bloom Pass 或对应 RenderTarget；它只保存 Builder、Pool 和借用的 SceneColor 根 Surface。Bloom Factory 通过构建上下文解析 SceneColor，不再直接持有 Scene RT。

效果间数据流由 `RenderEffectPlan` 的逻辑输入/输出声明。缺失输入、重复输出和循环会在分配 GPU 资源前失败；完整规则见 [逻辑 RenderSurface 与动态效果依赖图](RENDER_SURFACES.md)。

## 每帧同步边界

```csharp
scene.PerformInput(input.KeysPressed, input.KeysReleased);
scene.PerformStep(deltaTime);

var events = scene.DrainUncommittedEvents();
pipelineBuilder.ApplyEvents(events);

pipeline.Execute(context);
```

事件快照可以先交给其他消费者，再交给 Builder。`ApplyEvents` 必须位于 Step 之后、Draw 之前；Pipeline 在 `Execute` 期间禁止修改。

同一批事件按顺序应用：Request 后 Release 的最终状态是释放，Release 后 Request 的最终状态是重新获取。整批事件会先完成工厂、描述符、共享配置和 Surface 依赖图校验，再修改当前图。

## RenderTargetPool 所有权

```csharp
using var lease = targets.Rent(new RenderTargetDescriptor(
    width,
    height,
    depthStencilFormat: RenderTargetDepthStencilFormat.Depth24Stencil8));

RenderTarget2D target = lease.Target;
```

- Pool 按宽、高、颜色格式和 Depth/Stencil 格式复用。
- `RenderTargetLease.Dispose()` 幂等，只负责归还，不直接删除 GPU 资源。
- Pool 拒绝不属于自己的资源和重复归还。
- `RenderTargetPool.Dispose()` 会释放空闲和仍被租赁的全部资源一次。
- 颜色格式支持 RGBA8 与 RGBA16F，Depth/Stencil 为 None 或 Depth24Stencil8；Pool 按完整 Descriptor 隔离。

Factory 创建的 Runtime 持有 Lease；Pass 只借用 `RenderTarget2D`。挂接后 Pass 由 `RenderPipeline` 负责释放，Runtime 负责归还 Lease。

## Resize 与关闭

```csharp
pipeline.Resize(width, height);
pipelineBuilder.Resize(width, height);
```

Builder 会先为全部活跃效果创建新尺寸 Runtime，成功挂接后再移除旧附件；失败时保留旧图。旧 Lease 归还后，Pool 清理不匹配当前窗口尺寸的空闲资源。owner 不需要重新发送请求。

运行时还可以通过 `RequiresRebuild(nextOwners)` 声明“这次参数变化不能原地更新”。Bloom 在 Resolution 改变时使用该协议重建三张中间目标；Threshold、Intensity、BlurRadius 和 Iterations 则由 `UpdateOwners` 原地更新。该重建同样保持失败原子性。

关闭顺序固定为：

```text
Scene.End
ScenePipelineBuilder.Dispose
RenderPipeline.Dispose
RenderTargetPool.Dispose
Shader / Texture / Batch / Window Graphics
```

Builder 先移除动态 Pass，再归还 Lease；这样不会让 Pass 引用已经释放的 RenderTarget 或 Shader。

## 扩展新效果

1. 定义实现 `IRenderEffectDescriptor` 的纯领域描述符。
2. 实现 `IRenderEffectFactory.Plan`，完成共享配置检查并声明逻辑输入/输出。
3. Factory 在 `Create` 中通过 `context.Surfaces` 解析上游，再从 Pool 租赁目标。
4. Runtime 暴露与 Plan 完全一致的 `Outputs` 和 Pass；需要上屏时由 Presentation 描述符消费输出。
5. 在 `UpdateOwners` 中更新普通参数；结构参数变化时实现 `RequiresRebuild`。
6. 在组合根注册根 Surface 与 Factory；GameInstance 发 Request/Release 事件。

Factory 创建失败、未知 Kind 或描述符冲突不会破坏已经挂接的效果图。

## 当前边界

- Stencil 支持程序化 Circle 与 SpriteAlpha；尚未支持软边或任意矢量路径。
- Bloom 可以消费 SceneColor 或另一个动态效果输出，并支持 RGBA8/Display 与 RGBA16F/Linear 两条严格匹配路径。
- Tone Mapping 消费 RGBA16F/Linear Scene 与可选 Bloom，输出 RGBA8/Display。
- Presentation 只消费 RGBA8/Display，并用唯一 `present:main` Runtime 稳定组合 World、Stencil 与 SceneGui。
- v1 不支持 MSAA、多颜色 Attachment、隐式格式转换或跨场景共享 Pool。
- 没有全局事件总线；组合根负责在明确帧边界分发事件快照。

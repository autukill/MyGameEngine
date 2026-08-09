# 运行时渲染诊断快照

运行时诊断分为两类：低频显式捕获的渲染图快照，以及启动时按需启用的零帧分配统计。两者都只公开值数据，不暴露 GL、FBO、Texture Handle 或可变 Runtime 对象。

## Hosting 入口

```csharp
Default2DRenderDiagnostics snapshot = context.CaptureRenderDiagnostics();

foreach (RenderPassDiagnostics pass in snapshot.Pipeline.Passes)
    Console.WriteLine($"{pass.ExecutionIndex}: {pass.Name} enabled={pass.IsEnabled}");

foreach (RenderEffectDiagnostics effect in snapshot.Effects.Effects)
    Console.WriteLine($"{effect.Key} owners={effect.Owners.Count} passes={effect.Passes.Count}");

Console.WriteLine($"leases={snapshot.RenderTargets.ActiveLeases.Count}");

foreach (ViewportSlotDiagnostics viewport in snapshot.Viewports)
{
    SceneDrawStatistics draw = viewport.SceneDraw;
    Console.WriteLine(
        $"{viewport.Slot}: {viewport.Width}x{viewport.Height} " +
        $"candidates={draw.CandidateVisitCount} culled={draw.CulledInstanceCount} " +
        $"drawn={draw.DrawnInstanceCount} " +
        $"sort={draw.SortComparisonCount} sceneMs={draw.TotalTime.TotalMilliseconds:F3}");
}

if (snapshot.FrameStatistics is { } frame)
    Console.WriteLine($"fps={frame.FramesPerSecond:F1} draw={frame.DrawCalls}");
```

高级组合根也可分别调用 `RenderPipeline.CaptureDiagnostics()`、`ScenePipelineBuilder.CaptureDiagnostics()` 与 `RenderTargetPool.CaptureDiagnostics()`。

`Viewports` 保存 Render View、标准化布局、实际呈现像素矩形、内部 RenderWidth/RenderHeight、Fit、合成 Layer、不可变 `SceneLayers` 过滤器与 `Effects` Profile；也可单独调用 `context.CaptureViewportDiagnostics()`。`Effects.AdditionalPassCount/AdditionalRenderTargetCount` 可在不解析底层图的情况下展示配置成本。Contain 的像素矩形只包含真实画面，不包含 letterbox 黑边。RenderScale 只改变内部渲染尺寸，不改变呈现槽位。

## 每 View Scene Draw 分项

`ViewportSlotDiagnostics.SceneDraw` 是最近完成帧的 `SceneDrawStatistics` 值快照：

- `VisibleLayerCount`：同时满足全局可见与 View Layer Filter 的层数。
- `CandidateVisitCount`：收集阶段实际访问的实例候选总数。Scene 按 Layer 维护实例索引，因此该值是当前 View 允许层中的候选之和，不再乘以 Scene 的声明层数；inactive 实例仍是候选，但不会进入 Selected。
- `CulledInstanceCount`：活跃候选中，已知视觉边界完全位于当前 Camera 可见世界 AABB 外的实例数。
- `SelectedInstanceCount/DrawnInstanceCount`：进入排序列表和完成 Draw 回调的实例数。
- `SortComparisonCount`：Draw 阶段执行的 Depth 排序比较次数。当前有序 Layer 索引使普通 View 路径恒为 `0`；字段保留用于诊断兼容和未来其他排序策略。
- `TraversalTime/SortTime/DrawTime/TotalTime`：对应 CPU 分项耗时。

计数路径始终开启且逐帧零分配。只有启用 `FrameStatisticsOptions`（或通过 Hosting 性能遥测间接启用）时，`TimingEnabled` 才为 `true` 并采集高频时间戳；普通运行仍提供计数，但时间字段保持零。数据只覆盖每个 View 的基础 `SceneRenderPass`，Stencil 等效果内部的额外场景重绘仍通过总 Draw Call/Pass 统计观察，避免错误归属到某个普通 View。

`GameInstance.LayerName` 在实例属于 Scene 时会同步维护索引。切层不会改变公开 API；在较早 Layer 的 `OnDraw` 中把实例移入较晚 Layer，较晚 Layer 的同一次 View 绘制仍能看到它。索引只优化候选收集，各 View 仍独立排序和调用 Draw，避免缓存可能在回调中变化的 Depth、Active 或 Layer 状态。

早期 `Engine.DddTests` 单次基准结果如下（6 个声明层、4 个有实例层、主 View 全层、observer 排除四分之一实例所在层）：

| 实例数 | 优化前双 View | Layer 索引后 | 有序 Draw 索引后 | 候选访问（前 → 后） |
| ---: | ---: | ---: | ---: | ---: |
| 100 | 0.0649 ms | 0.0520 ms | 0.0156 ms | 600/500 → 100/75 |
| 1,000 | 0.5157 ms | 0.5625 ms | 0.1466 ms | 6,000/5,000 → 1,000/750 |
| 10,000 | 1.5356 ms | 1.1854 ms | 0.4703 ms | 60,000/50,000 → 10,000/7,500 |

微秒级结果会受 JIT 与机器噪声影响；候选计数是确定性的。Layer 索引先消除了重复收集，有序 Draw 索引再把每 View 排序比较从 `199,580/149,685` 降为 `0/0`。10,000 实例相对 Layer 索引阶段约再下降 60.3%。

当前多 View 基准已经从领域烟测中移出，统一通过独立 `Engine.PerformanceBenchmarks` 运行；命令、确定性守卫和最新基线见[多 View 性能基准](MULTI_VIEW_PERFORMANCE.md)。

Depth 顺序在实例加入、切 Layer 或 `ChangeDepth` 时维护。相同 Depth 继续按最初加入 Scene 的顺序稳定排列；从较早 Layer 的 Draw 回调修改较晚 Layer 的 Depth/Layer，仍会在同一个 View 的后续 Layer 捕获时生效。反复切层和改 Depth 预热后均为 0 B/frame。

## Camera 可见性剔除

每个 `SceneRenderPass` 会从自己的 Camera 计算当前渲染矩阵对应的世界 AABB，再在排序前执行保守剔除。旋转 Camera 使用四个视口角点的包围 AABB，因此可能多保留实例，但不会把旋转视口角落中的内容误剔除；震屏使用本帧实际渲染偏移。

普通 Sprite 实例无需配置：`Automatic` 会使用逻辑 Sprite 的 Size/Origin，并应用实例的正负缩放与旋转。自定义 `OnDraw` 可声明局部视觉边界，或完全退出：

```csharp
public sealed class LaserTrail : GameInstance
{
    public LaserTrail()
    {
        // 相对 Position 的局部视觉范围；它不复用 Collider。
        LocalDrawBounds = new Bounds2D(-8, -32, 256, 32);
    }
}

public sealed class WorldWeather : GameInstance
{
    public WorldWeather()
    {
        // 会跨越 Camera 绘制大范围内容，始终执行 Draw。
        ViewCulling = InstanceViewCullingMode.AlwaysVisible;
    }
}
```

没有 Sprite、没有 `LocalDrawBounds` 或 Sprite 元数据暂不可解析时，路径会 fail-open 并继续绘制。`Collider` 不作为视觉边界：命中区域经常小于特效/Sprite，把两者复用会造成画面边缘突然消失。

早期无 GPU 基准中，10,000 Sprite 实例、两个 Camera 各可见 20% 时，每个 View 绘制 2,000、剔除 8,000，保持 `0 B/frame`。Recording Batch 中的边界检查是为了避免真实 Draw 回调、顶点生成和 GPU 提交，不应把纯 CPU 空 Draw 数字解读为剔除本身必然加速。当前统一基准见[多 View 性能基准](MULTI_VIEW_PERFORMANCE.md)；该路径仍逐 View 检查候选，没有引入跨 View 缓存或通用空间树。

## Pipeline 快照

每个 `RenderPassDiagnostics` 包含稳定 `RenderPassHandle`、挂接顺序、拓扑执行顺序、名称、启用状态、输入数量，以及不含句柄的输出 Descriptor。快照会运行同一拓扑排序算法但不会执行 Pass；若存在缺失物理依赖或循环，`DependencyError` 保存错误文本，`ExecutionIndex` 为 `null`，挂接状态仍可用于诊断。

## Effect 与 Surface 快照

`ScenePipelineDiagnostics` 保存当前 viewport、稳定效果顺序和逻辑 Surface 图。Effect 项包含 Key、owner `InstanceId`、输入/输出契约与关联 Pass Handle；Surface 项包含格式、Linear/Display 编码、根标记、生产者和消费者。

多 Render View 模式下，`ScenePipelineDiagnostics.Width/Height` 表示 Builder 的主 View 分辨率，而不是窗口尺寸；它应与主 View 的 `RenderWidth/RenderHeight` 一致。每个效果输出实际按其输入 Surface 尺寸创建：Direct 次级 View 没有隐藏租约，显式 HDR Profile 的租约则匹配对应 View 的 `RenderWidth/RenderHeight`。

owner、输入、输出和消费者均在捕获时复制；后续帧更新、owner 释放或图重建不会修改旧快照。

## RenderTarget 租约快照

`RenderTargetPoolDiagnostics` 提供 Total、Leased、Available 总数、按完整 Descriptor 分组的数量，以及当前活动租约的单调递增 `LeaseId` 与 Descriptor。

租约 ID 只在所属 Pool 生命周期内有意义，不是 GPU Handle，也不能用于归还或访问资源。租约释放后不会继续出现在新快照中；旧快照只保留值数据，不阻止目标归还或释放。

## FPS/UPS 控制

启动时可一次设置渲染与更新循环目标：

```csharp
var windowOptions = EngineWindowOptions.Default.WithFrameRate(
    new FrameRateSettings(
        framesPerSecond: 120,
        updatesPerSecond: 60,
        vSync: false));
```

运行时在窗口线程调用同一值对象更新目标：

```csharp
context.SetFrameRate(new FrameRateSettings(60, 60, vSync: true));
```

- FPS 与 UPS 必须是有限非负数；`0` 表示窗口循环不主动限速。
- `VSync = true` 时实际渲染频率通常还受显示器和驱动交换间隔控制；`FramesPerSecond` 是窗口调度器上限，不承诺突破垂直同步。
- `FixedDeltaTime` 只替换传给游戏 Step 的模拟时间，不改变真实窗口调度频率，也不参与实际 UPS 统计。
- `FrameRateSettings.Uncapped` 同时关闭 VSync 与窗口 FPS/UPS 上限。

## 可选帧统计

统计默认关闭。只有显式启用后，窗口才创建采集器并在渲染热路径接入可空计数入口：

```csharp
var windowOptions = EngineWindowOptions.Default
    .WithFrameRate(new FrameRateSettings(120, 60, vSync: false))
    .WithFrameStatistics(new FrameStatisticsOptions(sampleWindowSeconds: 1));

if (context.TryCaptureFrameStatistics(out FrameStatisticsSnapshot frame))
{
    Console.WriteLine(
        $"FPS={frame.FramesPerSecond:F1} UPS={frame.UpdatesPerSecond:F1} " +
        $"Draw={frame.DrawCalls} Flush={frame.BatchFlushes} " +
        $"TextureSwitch={frame.TextureSwitches} Pass={frame.ActivePasses}");
}
```

字段口径：

- `DrawCalls`：引擎已知的真实 GL 绘制提交，包括 SpriteBatch 与内置全屏后处理。自定义 Pass 直接调用 GL 绘制后应调用 `RenderPassContext.RecordDrawCall()`。
- `BatchFlushes`：真正上传顶点并提交绘制的非空 SpriteBatch Flush；空 Flush 不计数。
- `TextureSwitches`：同一个 SpriteBatch Begin/End 区间内，从一个非零纹理切到另一个纹理的次数；首次选择纹理不计数。
- `ActivePasses`：该渲染帧中实际执行的启用 Pass 数；禁用 Pass 不计数。
- `FramesPerSecond/UpdatesPerSecond`：按各自真实 delta 独立采样，采样窗口只控制速率平滑，不影响单帧计数。

`CaptureRenderDiagnostics()` 会把最近完成帧的统计作为可空 `FrameStatistics` 一并返回。统计关闭或第一帧尚未完成时为 `null`。启用后的采集器按值覆盖结构体快照，不产生每帧托管分配；它面向单窗口线程，不是跨线程遥测队列。

## 性能与线程边界

- Render Graph 捕获会分配数组和只读包装；不要默认每帧调用。帧统计的 `TryCapture` 只复制值结构。
- 推荐在调试按键、低频遥测、测试 checkpoint 或明确的 Step/Draw 边界捕获。
- 当前 Runtime 采用单线程窗口循环；快照 API 不保证与后台图重建并发安全。
- 已 Dispose 的 Pipeline、Builder 或 Pool 会拒绝捕获。

Runner 的 `--smoke` 显式启用统计，并在效果图稳定后验证完整 HDR、Bloom、Stencil 与 Presentation 帧的 Draw/Flush/Texture/Pass 计数；GPU 回归测试继续通过 Pool 快照读取租约计数，验证创建、resize 与 release 生命周期。

需要按固定间隔聚合帧计数、Texture/Atlas 和 RenderTarget 显存估算，或检查开发期预算时，使用[性能预算与低频遥测](PERFORMANCE_TELEMETRY.md)。该路径只在采样点捕获集合，不把 Render Graph 快照改为每帧模型。

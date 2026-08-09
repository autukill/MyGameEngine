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

if (snapshot.FrameStatistics is { } frame)
    Console.WriteLine($"fps={frame.FramesPerSecond:F1} draw={frame.DrawCalls}");
```

高级组合根也可分别调用 `RenderPipeline.CaptureDiagnostics()`、`ScenePipelineBuilder.CaptureDiagnostics()` 与 `RenderTargetPool.CaptureDiagnostics()`。

## Pipeline 快照

每个 `RenderPassDiagnostics` 包含稳定 `RenderPassHandle`、挂接顺序、拓扑执行顺序、名称、启用状态、输入数量，以及不含句柄的输出 Descriptor。快照会运行同一拓扑排序算法但不会执行 Pass；若存在缺失物理依赖或循环，`DependencyError` 保存错误文本，`ExecutionIndex` 为 `null`，挂接状态仍可用于诊断。

## Effect 与 Surface 快照

`ScenePipelineDiagnostics` 保存当前 viewport、稳定效果顺序和逻辑 Surface 图。Effect 项包含 Key、owner `InstanceId`、输入/输出契约与关联 Pass Handle；Surface 项包含格式、Linear/Display 编码、根标记、生产者和消费者。

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

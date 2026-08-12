# 性能预算与低频遥测

性能遥测用于开发期日志、自动测试和外部工具，不提供游戏 UI。它复用可选帧统计，并在完成渲染帧后按固定间隔捕获 Gameplay 查询、Texture、Atlas 页和 RenderTarget 的统计。默认关闭；关闭时不创建采样器，也不订阅完成帧事件。

## Hosting 配置

```csharp
var budget = new PerformanceBudget(
    maxDrawCalls: 500,
    maxBatchFlushes: 250,
    maxTextureSwitches: 100,
    maxActivePasses: 32,
    maxEstimatedGpuMemoryBytes: 256L * 1024 * 1024);

using var sink = new MyTelemetrySink();

using var game = GameApplication
    .Create(EngineWindowOptions.Default)
    .UseDefault2DRenderer(renderer => renderer
        .UseContent(GameAssets.Packages.Root)
        .EnablePerformanceTelemetry(new PerformanceTelemetryOptions(
            sink,
            sampleInterval: TimeSpan.FromSeconds(1),
            budget)))
    .ConfigureScene("Main", context => { })
    .Build();
```

启用遥测时，如果窗口尚未配置 `FrameStatisticsOptions`，Hosting 会自动使用默认配置启用帧统计。调用方仍可先通过 `WithFrameStatistics(...)` 指定 FPS/UPS 平滑窗口。

`IPerformanceTelemetrySink` 生命周期由调用方拥有。采样发生在窗口线程；Sink 应快速完成或自行复制值快照后异步处理。Sink 抛出的异常不会被引擎吞掉。

## 显式捕获

无需启用自动遥测也可以在调试按键或测试 checkpoint 捕获：

```csharp
RuntimePerformanceSnapshot snapshot =
    context.CapturePerformanceSnapshot(budget);

Console.WriteLine(snapshot.GpuMemory.TotalBytes);
Console.WriteLine(snapshot.ProcessMemory.WorkingSetBytes);
Console.WriteLine(snapshot.ProcessMemory.PrivateBytes);
Console.WriteLine(snapshot.ProcessMemory.ManagedHeapEstimateBytes);
Console.WriteLine(snapshot.GameplayQueries.AverageMillisecondsPerStep);
foreach (PerformanceBudgetViolation violation in snapshot.BudgetViolations)
    Console.WriteLine($"{violation.Metric}: {violation.Actual} > {violation.Limit}");
```

预算采用严格“大于”比较，等于上限不算超限。帧统计未启用或尚无完成帧时，只评估显存预算。

`ProcessMemory` 默认不强制 GC，提供当前进程 Working Set、Peak Working Set、Private Bytes、Virtual
Bytes、Managed Heap 近似值、各代收集次数，以及最近一次 GC 后的 Heap、Committed、Fragmentation 和
系统内存压力。首次 GC 之前 `GcHeapSizeAfterLastCollectionBytes` 等字段为 `0` 是合法状态。若只需要
这一组低频值，可以调用 `context.CaptureProcessMemoryDiagnostics()`，不必启用自动性能遥测。

开发工具还可以显式调用 `CaptureProcessMemoryDiagnostics(forceFullCollection: true)`。它会阻塞并触发
完整 GC，返回值的 `WasFullCollectionForced` 为 `true`，适合手动 checkpoint、测试或泄漏调查；不得放在
每帧路径或正式游戏逻辑中。比较强制 GC 前后的 `ManagedHeapEstimateBytes`，可以区分“尚未收集的解码
缓冲”与“仍被引用的 Managed 对象”，但 Working Set/Private Bytes 仍可能因 GC、Runtime 和驱动缓存而
不立即回落。

`UnattributedPrivateBytes` 从 Private Bytes 中减去 Managed Heap 近似值与最近 GC Committed 的较大值，
只表示“GC 指标无法解释的 Private Bytes”。其中可能同时包含 CoreCLR、JIT 代码、程序集、线程栈、Skia/OpenAL、窗口系统、OpenGL 驱动
及其缓存；它不是精确的 Native Heap，也不能与估算 GPU 显存直接相减。判断泄漏应观察多次稳定采样的
趋势，而不是对单次任务管理器数值做归属推断。

## Working Set 与 Private Bytes

这两个指标回答的是不同问题：

| 指标 | 回答的问题 | 包含什么 | 不代表什么 |
|---|---|---|---|
| Working Set | 此刻有多少相关页面驻留在物理 RAM？ | 当前驻留的私有页，以及 DLL、Runtime、内存映射文件等共享页 | 进程拥有的全部内存，或不可共享内存总量 |
| Private Bytes | 操作系统为本进程提交了多少不可共享的私有内存？ | GC/Native Heap、线程栈、JIT、原生库和驱动的私有分配及缓存 | 当前全部位于 RAM，或全部仍是有用对象 |

Working Set 强调“现在驻留”。Windows 可以在进程没有释放对象时裁剪工作集：干净的文件映射页可以
丢弃后重读，私有页也可以离开 RAM 并在之后恢复。因此最小化窗口、系统内存压力或其他进程活动都可能
让 Working Set 上下波动。`ProcessMemoryDiagnostics.WorkingSetBytes` 使用的是进程总 Working Set；
Windows 任务管理器某些列显示的是 Private Working Set，比较时必须先确认工具口径。

Private Bytes 强调“私有提交”。提交意味着系统承诺在需要时由 RAM 或页面文件提供后备，但页面不必
当前驻留。它既可能是仍被引用的对象，也可能是 GC 已回收但保留复用的 Heap、Native allocator 空闲区、
线程栈、JIT 数据或驱动缓存。“只能由本进程使用”描述共享属性，不等于“当前有用”或“应立即归还”。

两者可以近似理解为：

```text
Private Bytes
├─ 当前驻留在 RAM 的私有页面
└─ 当前不驻留、但仍由进程拥有的私有页面

Working Set
├─ 当前驻留的私有页面
└─ 当前驻留的共享页面
```

因为 Working Set 还包含共享页，不能用 `Private Bytes - Working Set` 精确计算换出量。典型行为包括：

- 申请并触碰 100 MiB 私有内存时，两者通常一起增长；Windows 随后裁剪 60 MiB 时，Working Set 可以
  下降，而 Private Bytes 保持不变。
- 加载共享 DLL 的代码页会增加 Working Set，却不一定等量增加 Private Bytes。
- GC 回收 20 MiB 对象后，Managed Heap 近似值可能下降，但若 Runtime 保留提交区域供后续复用，
  Private Bytes 和 Working Set 都不保证立即下降。

调查泄漏时，Working Set 更适合观察实际 RAM 压力，Private Bytes 更适合观察进程私有提交是否在重复
场景循环后持续增长。推荐在相同 checkpoint 比较：等待后台工作收敛，确认资源 Lease/Texture 计数，
再做一次显式 Full GC 对照。若 Full GC 后 Managed Heap、Private Bytes 或明确登记的 CPU/GPU 所有权仍
随每轮加载/卸载单调增长，才值得继续定位引用、Native 分配或 GPU 生命周期；单次高水位不能证明泄漏。

`GameplayQueries` 将 Find、Collision、Area 和 Radius 分开统计调用数、扫描候选、命中及累计耗时。自动遥测每次发布后重置查询区间；`SampledSteps` 和 `AverageMillisecondsPerStep` 可用于判断查询是否进入更新帧预算。显式捕获默认不重置，如需区间采样可调用 `CapturePerformanceSnapshot(resetGameplayQueryStatistics: true)`。

## 显存估算口径

- `TextureLibrary` 中每个 Texture 和内部 Atlas 页按 RGBA8 `width × height × 4` 估算。
- RGBA8 RenderTarget Color Attachment 按每像素 4 字节估算。
- RGBA16F RenderTarget Color Attachment 按每像素 8 字节估算。
- Depth24Stencil8 Attachment 按每像素 4 字节估算。
- SceneColor/SceneGui 根目标单独统计。
- RenderTargetPool 的活动租约和可复用缓存分别统计；已归还但未 Trim 的目标仍占 GPU 内存。

这些数字不包含驱动对齐、压缩、mipmap、Shader、VAO/VBO/EBO、FBO 元数据或第三方库内部缓存，因此是稳定的逻辑估算，不是驱动级精确读数。

高级代码绕过 `TextureLibrary` 或 `RenderTargetPool` 创建资源时，可以显式补充：

```csharp
using IDisposable registration = context.RegisterGpuMemoryUsage(
    "particles.compute-buffer",
    () => particleCapacity * ParticleStride);
```

名称必须唯一，估算必须非负；释放 registration 后下一份快照不再包含该资源。通过 `TextureLibrary` 注册的自定义 Texture 已自动计入，不应重复登记。

自定义 RenderPass 直接调用 GL 绘制后，仍应调用 `RenderPassContext.RecordDrawCall()`；SpriteBatch 会自动记录 Draw、有效 Flush 和纹理切换。

## Runner 出口

控制台每秒摘要包含 FPS、Gameplay Query、估算 GPU 显存，以及 Working Set、Private Bytes、Managed
Heap 近似值和最近一次 GC 后的 Committed Heap：

```powershell
dotnet run --project src/MyGame.Runner -- --diagnostics
```

写入 JSON Lines，适合脚本或 CI 逐条读取：

```powershell
dotnet run --project src/MyGame.Runner -- --diagnostics-json artifacts/performance.jsonl
```

两个参数可以同时使用。Runner 默认预算为 500 Draw、250 Flush、100 Texture Switch、32 Pass 和 256 MiB 估算显存。

## 性能边界

- 每个已启用遥测的渲染帧读取单调时钟并做间隔判断；查询统计启用时，每次查询额外读取起止时间并更新值计数器。
- 到达间隔时才复制 Texture/Pool 诊断集合并评估预算。
- 第一份快照在第一帧完整结束后立即发布，后续按 `SampleInterval` 限频。
- 当前实现是单窗口线程模型，不提供后台并发注册或无界遥测队列。

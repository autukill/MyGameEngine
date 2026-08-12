# TileWorld 运行时 LOD 与流式加载

`Engine.Features.TileWorldStreaming` 把离线 `.mgworld` 产物接到 `ViewportSnapshot`、通用
`WorldChunkStreamer<T>` 和 `TextureLibrary`。它负责视觉 LOD 选择、归档随机读取、WebP 解码、
主线程 GPU 提交、跨层替换和分层绘制；Viewport 仍不知道地图格式，TileWorld Reader 也不知道
Camera 或 OpenGL。

```text
ViewportSnapshot
  → TileWorldLodSelector                 Zoom → DesiredLevel，带滞回
  → TileWorldStreamingSession
       ├─ 可选逐 Layer Preview 回退图    独立解码并常驻的最后保底
       ├─ 常驻最粗可用 LOD 状态          加载/失败/快速移动时的首选分块回退
       ├─ Active Level                   当前稳定绘制层
       └─ Pending Level                  可见 Chunk 全部就绪后原子替换 Active
            → WorldChunkStreamer<TileWorldChunkLease>
                 → TileWorldChunkLoader  有界后台读取 + WebP 解码
                 → staged upload queue   主线程按张数/字节预算提交
```

## 统一术语

World、Map、Cell、Tile、Chunk、LOD、Preview 回退图及驻留阶段的权威定义见
[MyGameEngine 统一术语](ENGINE_TERMINOLOGY.md)。特别注意：Chunk 是空间和生命周期边界，不等于图片、
文件或 Texture；Preview 回退图不是 LOD，也不计入 `lodCount`。

由于现有诊断 API 需要保持兼容，`FallbackLevel` 或单独的 `Fallback` 表示“最粗可用 LOD”；
带 `FallbackSurface` 的字段才表示“Preview 回退图”。

## Hosting 黄金入口

Content 包加载后，通过强类型 TileWorld 引用创建一个由 Scene 自己拥有的 Session：

```csharp
TileWorldRef world = context.Content!.GetTileWorld(
    GameAssets.TileWorlds.WorldOverworld.Name);

TileWorldStreamingSession stream = context.CreateTileWorldStream(
    world,
    new TileWorldStreamingOptions(
        new TileWorldLodSelectionOptions(
            targetPixelsPerTexel: 1f,
            hysteresisRatio: 0.1f),
        new WorldChunkStreamingOptions(
            preloadMarginChunks: 1,
            retainMarginChunks: 2,
            maximumConcurrentLoads: 4,
            maximumTrackedChunks: 4096,
            retryFailedOnViewportChange: true,
            maximumLoadsStartedPerUpdate: 8),
        TileWorldChunkLoadMode.Background,
        new TileWorldTextureUploadBudget(
            maximumTexturesPerUpdate: 2,
            maximumBytesPerUpdate: 2 * 1024 * 1024)));

ViewportController viewport = context.GetViewportNavigation(context.RenderViews[0].Ref);

// 每帧：Viewport 更新后、世界绘制前。
TileWorldStreamingUpdateResult update = stream.Update(viewport.CaptureSnapshot());

// 在已经 Begin 的 ISpriteBatch 中调用。
TileWorldDrawStatistics draw = stream.Draw(batch);

// 需要把 GameInstance 插在地图层之间时，按 Metadata LayerIndex 分别绘制：
stream.DrawLayer(batch, layerIndex: 0);
// draw gameplay instances
stream.DrawLayer(batch, layerIndex: 1);

// Scene 销毁时；必须早于 Content Package 和 TextureLibrary。
stream.Dispose();
```

`TileWorldStreamingSession` 不拥有 `TileSetLibrary`、`TextureLibrary` 或 Content Package。推荐由
GameInstance/Scene Controller 在 `OnCreate` 创建，在 `OnDestroy` 释放。运行中被替换的 Pending/Active
Level 先取消并进入退休队列，后续 `Update` 只轮询已经完成的任务，不在 Scene Step 中同步等待；
Session 终止释放仍遵循 Preview 回退图 → Pending/Retired LOD → Active LOD → 最粗可用 LOD，
每个 Lease 再移除自己注册的 Texture。

当 Zoom 已选择另一个 LOD 时，当前 Active Level 会冻结现有驻留集合，只作为新层级接管前的画面桥接；
它不会用新的全景范围扩张高细节 Chunk。缺失区域继续由最粗可用 LOD 或 Preview 回退图覆盖，因此快速从最大细节拉回
全景既不会突破 `MaximumTrackedChunks`，也不会短暂加载整张地图的 LOD0。

## LOD 选择与滞回

LOD 不是硬编码 Zoom 列表，而是从世界 Chunk 尺寸和离线 Raster 像素密度推导。默认目标为
一个 Raster texel 最多映射约一个屏幕像素：

```text
rasterDensityZoom = min(rasterWidth / baseChunkWorldWidth,
                        rasterHeight / baseChunkWorldHeight)

referenceZoom = targetPixelsPerTexel × rasterDensityZoom

LOD n → n+1 boundary = referenceZoom / 2^(n+1)
```

`targetPixelsPerTexel` 可整体调整清晰度/显存取舍。`hysteresisRatio = 0.1` 会在阈值两侧形成
10% 的乘法死区：缩小时越过下边界才切到更粗层，放大时越过上边界才切回更细层。因此 Wheel、
Pinch 或惯性 Zoom 在阈值附近不会反复创建和销毁 Chunk。

### 大白话：如何调整 LOD0 出现的 Zoom

先确认 `lodCount > 1`。只有一个 LOD 时没有层级可以切换，修改 `targetPixelsPerTexel` 不会产生效果。

`targetPixelsPerTexel` 可以理解为“允许一颗地图纹理像素在屏幕上被放大到多大”：

- 值越小，引擎越挑剔，会在更小的 Zoom 下提前使用高清 LOD。
- 值越大，引擎越愿意继续使用粗 LOD，要拉得更近才使用高清 LOD。

如果保持地图、窗口和 `hysteresisRatio` 不变，观察到旧配置需要 Zoom `1.5` 才进入 LOD0，
现在希望 Zoom `1.0` 就进入，可以按相同比例缩小配置：

```text
新 target = 旧 target × 希望的切换 Zoom ÷ 原来的切换 Zoom
           = 0.1 × 1.0 ÷ 1.5
           ≈ 0.0667
```

```csharp
new TileWorldLodSelectionOptions(
    targetPixelsPerTexel: 0.0667f,
    hysteresisRatio: .1f)
```

大白话就是：希望切换距离从 `1.5` 提前到 `1.0`，新门槛是旧门槛的三分之二，所以配置值也乘
三分之二。该方法会按相同比例移动全部 LOD 边界；滞回会让“拉近进入”和“拉远退出”的实际读数略有不同。

不要把“LOD0 已被选择”和“LOD0 Chunk 已经开始显示”混为一件事。选择器可能早已请求 LOD0，但如果
完整保留范围超过 Chunk 数量或 Texture 驻留预算，Session 会暂停该 LOD 并继续显示 Preview 回退图。
此时应该调整预算或增加粗 LOD，而不是调整 `targetPixelsPerTexel`。

TileMap 来源的 LOD0 始终读取权威 Tile/碰撞并通过 TileSet/Sprite 命令绘制；预切片来源的
Raster LOD0 解码导入的原始 WebP。生成式 LOD1+ 解码逐 Layer WebP。
`Draw()` 按 Metadata Depth 顺序绘制全部可见 Layer；`DrawLayer()` 允许 Scene 在地图层之间穿插
GameInstance，并保持 LOD0 与 Raster LOD 相同的层级边界。
Gameplay 查询不得使用 Raster LOD 替代 LOD0 权威数据。

## 后台准备与主线程提交

`TileWorldChunkLoader` 的默认 `Background` 模式只在线程池执行：

1. 从 `.mgworld` 随机读取 Chunk Payload。
2. 验证外层 SHA-256 与 Payload 结构。
3. 使用 `IImageDecoder` 把每个 Layer WebP 解码为未预乘 RGBA8。
4. 返回尚未触碰 GPU 的 `TileWorldChunkLease`。

`TileWorldStreamingSession.Update` 在调用线程收割完成项，但不会把所有 CPU-ready 图片在同一帧
全部上传。`TileWorldTextureUploadBudget` 默认限制每次 Update 最多 `2` 张 Texture / `2 MiB` RGBA；
张数和字节任一耗尽即延后到下一帧。若第一张 Texture 本身超过字节预算，仍允许这一张前进，避免
永久饥饿，因此离线构建仍应限制单 Chunk 图片尺寸。

每个 Chunk 可以跨多帧逐 Layer 上传，但只有最后一个 Layer 成功后才原子发布为 `IsCommitted`；
期间旧 Active、最粗可用 LOD 或 Preview 回退图继续遮盖缺口。后续 Layer 上传失败会删除该 Chunk 已暂存的
Texture，不影响旧 Active。`TileWorldStreamingUpdateResult.TexturesUploaded/TextureBytesUploaded`
提供本帧提交量，`RetiringLevels` 可观察尚未完成的取消任务。

OpenGL Texture 创建仍停留在拥有图形 Context 的线程。IO、SHA-256 与 WebP 解码通过
`MaximumConcurrentLoads` 有界并行；单纯把 `glTexImage2D` 放进线程池并不安全，若未来实测单张
Texture 上传仍超预算，再独立评估 PBO 或共享上传 Context/Fence。

`Inline` 模式主要用于无窗口测试、离线工具或确定知道 Chunk 很小的场景。它仍保持显式 Commit
边界，但归档读取和图片解码会占用调用线程，不建议作为大型地图默认值。

## 移动卡顿与预加载范围调优

本文统一使用以下术语：

| 中文术语 | API / 诊断 | 含义 |
|---|---|---|
| 可见范围 | `Visible` | 当前 Viewport 实际覆盖、必须优先就绪的 Chunk。 |
| 预加载范围 | `Preloaded` / `PreloadMarginChunks` | 在可见范围外提前读取和解码的 Chunk；用于降低移动时临时露出 Preview 回退图的概率。 |
| 保留范围 | `Retained` / `RetainMarginChunks` | Chunk 离开该范围后才取消或释放；用于避免来回移动时反复加载。 |
| 后台并发数 | `MaximumConcurrentLoads` | 同时进行 IO、校验和图片解码的最大任务数。 |
| 单次更新启动量 | `MaximumLoadsStartedPerUpdate` | 一次 `Update` 最多启动的新加载数，限制任务启动尖峰。 |
| 最大跟踪数 | `MaximumTrackedChunks` | 一个 Level 的完整保留范围最多允许跟踪多少 Chunk。 |
| 逐帧上传预算 | `TileWorldTextureUploadBudget` | 每次 `Update` 最多向 GPU 提交的 Texture 数和 RGBA 字节数。 |
| 稳态驻留预算 | `TileWorldTextureResidencyBudget` | 单个 Level 的 Raster Chunk Texture 保守驻留上限。 |

文档不使用“预载范围”指代上述概念；统一写作“预加载范围”。“驻留”表示资源仍由 Session/Streamer 持有，不等同于已经可见，也不等同于进程总内存。

Viewport 平移时容易短暂露出 Preview 回退图，通常说明移动方向上的 Chunk 未能在进入可见范围前完成“后台准备 → 主线程上传”。第一步应把预加载边距从 `1` 提高到 `2`，并让保留边距比它再大一圈：

```csharp
var options = new TileWorldStreamingOptions(
    new TileWorldLodSelectionOptions(
        targetPixelsPerTexel: 1f,
        hysteresisRatio: .1f),
    new WorldChunkStreamingOptions(
        preloadMarginChunks: 2,
        retainMarginChunks: 3,
        maximumConcurrentLoads: 6,
        maximumTrackedChunks: 192,
        retryFailedOnViewportChange: true,
        maximumLoadsStartedPerUpdate: 6),
    TileWorldChunkLoadMode.Background,
    new TileWorldTextureUploadBudget(
        maximumTexturesPerUpdate: 3,
        maximumBytesPerUpdate: 5L * 1024 * 1024),
    new TileWorldTextureResidencyBudget(
        maximumChunkTextureBytes: 128L * 1024 * 1024));
```

这是 `600×600`、单 Raster Layer 地图的建议起点，不是全局默认值。调优顺序如下：

1. 先增加 `PreloadMarginChunks`，直接扩大提前准备的范围。
2. 保持 `RetainMarginChunks >= PreloadMarginChunks`；通常设置为“预加载边距 + 1”，减少折返时的重复 IO 和解码。
3. 根据完整保留范围同步检查 `MaximumTrackedChunks` 和 `MaximumChunkTextureBytes`。只增加预加载范围而保留较小硬预算，可能让 Session 更早回退到 Preview 回退图。
4. 若后台准备跟不上，再小幅提高 `MaximumConcurrentLoads` 与 `MaximumLoadsStartedPerUpdate`；过高会加剧磁盘、CPU 和线程池竞争。
5. 若 Chunk 已完成后台准备但仍逐张出现，再提高逐帧 Texture 张数/字节预算；过高会把问题变成主线程 GPU 上传尖峰。

诊断时重点观察 `loaded/visible`、本帧 `TexturesUploaded/TextureBytesUploaded`、`BudgetFallbackReason` 和 `RequiredRetainedTextureBytes`：

- `loaded < visible` 且在途加载长期占满：优先检查后台并发、磁盘和解码成本。
- 已有大量待上传 RGBA，但每帧上传很少：调整逐帧上传预算。
- `MaximumTrackedChunks`：扩大保留范围后超过最大跟踪数。
- `MaximumChunkTextureBytes`：保守 Raster 驻留需求超过稳态预算；应增加 LOD、缩小范围或经过测量后提高预算。

更大的对称预加载范围会增加内存、显存、IO 与解码工作。如果游戏能得到稳定移动方向和速度，未来可以需求驱动实现方向性预取；当前 API 使用对称范围，优先保证行为简单且确定。

## `lodCount: 1`、Preview 回退图与 Chunk 预算

除 `MaximumTrackedChunks` 的数量硬上限外，可选的 `TileWorldTextureResidencyBudget` 在启动加载前约束单个 Level 的稳态 Raster Chunk Texture 成本：

```csharp
var options = new TileWorldStreamingOptions(
    lodSelection,
    chunkStreaming,
    TileWorldChunkLoadMode.Background,
    textureUploadBudget,
    new TileWorldTextureResidencyBudget(
        maximumChunkTextureBytes: 64L * 1024 * 1024));
```

估算只遍历 Retained 范围内归档中实际存在的 Raster Chunk；每个 Chunk 按 `EncodedWidth × EncodedHeight × 4 × 可见 Layer 数` 计算。权威 Tile Chunk 与稀疏空 Chunk 不占该预算；若某个 Raster Payload 省略了部分可见 Layer，估算会有意偏保守。默认值为 `Unlimited`，因此不会改变既有项目行为。

预算约束的是一个 LOD 的 Chunk Texture，不包含 Preview 回退图、根 RenderTarget、驱动缓存，也不承诺限制 LOD 交接期间多个 LOD 的瞬时总和。逐帧上传节流仍由 `TileWorldTextureUploadBudget` 独立负责：前者限制最终驻留规模，后者限制单帧提交尖峰。

有 Preview 回退图时，超限 LOD 会在创建 Chunk Lease 或上传 Texture 前暂停并由低清全图保底；没有 Preview 回退图时会在修改驻留状态前抛出明确异常。`BudgetFallbackReason` 区分 `MaximumTrackedChunks` 与 `MaximumChunkTextureBytes`，`RequiredRetainedTextureBytes` 给出本次保守需求，便于开发者调整 LOD、保留范围或预算，而不是盲目提高上限。

`lodCount` 只统计分块 LOD，Preview 回退图不计入其中。`lodCount: 1` 是合法配置，但其可选集合只有
LOD0：此时 `targetPixelsPerTexel` 和 Zoom 不会切出另一个层级，最粗可用 LOD 与最高细节 LOD 是同一层，
也没有生成式 LOD1+。如果启动后的全景视角只看到 Preview 回退图，不是“Zoom 尚未达到 LOD0 阈值”，
而是当前 LOD0 保留范围超过了 Chunk 数量或 Texture 驻留预算。

以 `20×20`、每片 `600×600` RGBA8、单 Raster Layer 的地图为例，全图共有 400 个 LOD0 Chunk，
全部解码后的逻辑 Texture 大约需要 `400 × 600 × 600 × 4 = 549.3 MiB`，还未包含进程、驱动和其他
渲染资源。若全景范围超过 `MaximumTrackedChunks` 或 `MaximumChunkTextureBytes`，运行时不会为了填满
画面而突破硬预算。优先离线生成 LOD1+ 并提高 `lodCount`，让全景使用少量粗 Chunk；只有经过测量后
才应提高预算。

当 `lodCount > 1` 时，降低 `targetPixelsPerTexel` 会让 LOD0 在更小的 Zoom 下被选中，提高它则更早
选择粗 LOD；该参数只影响“选择哪一层”，不会绕过 Chunk 数量、驻留和逐帧上传预算。

### 大白话：如何计算 Chunk 驻留预算

先计算每张 Chunk Texture 上传 GPU 后有多大。RGBA8 的每个像素固定使用 4 字节：

```text
单 Chunk MiB
= 宽 × 高 × 4 × 可见 Raster Layer 数 ÷ 1,048,576
```

`600×600`、单 Layer 的结果为：

```text
600 × 600 × 4 ÷ 1,048,576 ≈ 1.37 MiB
```

所以 `128 MiB` 理论上最多容纳约：

```text
128 ÷ 1.37 ≈ 93 个 Chunk
```

这里计算的是解码后的 RGBA，不是磁盘上的 WebP 文件大小。Viewport 越拉远，Visible 范围越大；再加上
`RetainMarginChunks` 的外围保留圈，实际所需 Chunk 可能超过 93 个，于是 Session 使用 Preview 回退图。
放大 Zoom 后屏幕覆盖的世界范围缩小，需要的 Chunk 变少，落回预算内后同一个 LOD0 才恢复显示。

最可靠的配置方式是在目标 Zoom 停住，读取诊断中的 `RequiredRetainedTextureBytes`，再预留约 20%：

```text
建议预算 ≈ RequiredRetainedTextureBytes × 1.2
```

例如诊断显示需要 `150 MiB`：

```text
150 × 1.2 = 180 MiB
```

可以向上取一个容易识别的配置值：

```csharp
new TileWorldTextureResidencyBudget(
    maximumChunkTextureBytes: 192L * 1024 * 1024)
```

若 `BudgetFallbackReason` 是 `MaximumTrackedChunks`，应比较 `RequiredRetainedChunks` 与
`MaximumTrackedChunks`；若原因是 `MaximumChunkTextureBytes`，才使用上面的 Texture 字节算法。
为了减少移动时露出 Preview 回退图而扩大预加载/保留范围后，也必须重新核对这两个预算。

当清单声明了 `fallbackSurfaces` 时，`TileWorldStreamingSession` 会暂停超预算 LOD 的 Chunk 流，取消
在途工作并释放已加载 Lease，画面由 Preview 回退图保底。放大到所需保留 Chunk 数重新落入预算后，
同一个 Session 会自动恢复 LOD0 流式加载。可通过以下字段观察这次降级：

- `TileWorldStreamingUpdateResult.IsUsingBudgetFallback`
- `TileWorldStreamingUpdateResult.RequiredRetainedChunks`
- `TileWorldStreamingDiagnostics.IsUsingBudgetFallback`
- `TileWorldStreamingDiagnostics.RequiredRetainedChunks`

若没有 Preview 回退图，超预算仍明确抛错，因为引擎没有能够保证画面完整的替代来源。此时应增加离线
LOD、声明 Preview 回退图、缩小可见范围，或经过内存/显存测量后提高 `MaximumTrackedChunks`；不建议把提高上限
作为默认解法。

## Preview 回退图、最粗可用 LOD 与无空洞替换

诊断中的 `FallbackLevel` 是 `.mgworld` 的最粗可用 LOD，即 `LOD(lodCount - 1)`；它不是 Preview 回退图。
该 LOD 的 Streamer 状态在 Session 生命周期内始终保留，
具体 Chunk 仍按 Visible/Preloaded/Retained 范围流式驻留。Pending Level 只有在
当前可见范围内每个坐标都得到“真实 Payload 或确认稀疏空 Chunk”的已提交 Lease 后，才会替换旧
Active Level。

当 Active 因快速移动尚缺少部分 Chunk 时，Renderer 不会把整张粗图再次叠加到详细图下面；它按
缺失的 Active 世界矩形裁取最粗可用 LOD 对应 Layer 的 UV 区域。这样透明 Layer 不会因为粗细两份内容重叠
而重复累加，Gutter 仍能在裁取边缘提供安全采样。

加载失败时旧 Active 保留；通用 Streamer 按 Viewport Revision 的既有策略重试。若旧 Active 的新
可见区域也尚未到达，则只在缺失区域显示最粗可用 LOD。`CaptureDiagnostics()` 可读取 Desired、
Active、Pending、Fallback Level 及各自 Pending/Loading/Loaded/Failed 计数。

若清单声明 `build.fallbackSurfaces[]`，Session 会独立后台读取并解码这些 Preview 回退图，再在
`Update` 调用线程让 `TileWorldFallbackSurfaceLease` 共享同一 GPU 上传预算并原子发布。它不占用 Chunk
Streamer 的后台并发预算，也不等待最粗可用 LOD；只有对应世界区域连最粗可用 LOD 的 Chunk 都尚未就绪时，Renderer 才按
Layer 和世界 `bounds` 裁取 Preview 回退图的 UV。`TileWorldDrawStatistics.FallbackSurfaceQuads` 可单独观察该路径。
Preview 回退图绑定明确 Layer，因此 `DrawLayer()` 仍能保持 Gameplay 深度穿插边界；扁平全图 Preview 回退图应绑定到
最底部地表 Layer。
`CaptureDiagnostics()` 还会报告是否声明、是否就绪以及当前常驻的 Preview 回退图（Fallback Surface）数量，但不暴露 GPU Handle。

## 底层组合入口

## 资源所有权与内存诊断

`TileWorldStreamingSession.CaptureMemoryDiagnostics()` 提供一次低频的所有权快照：

- `PreparedChunkDecodedBytes`：后台解码完成、尚未提交 GPU 的 Chunk RGBA 数组。
- `AuthoritativeChunkPayloadBytes`：LOD0 Tile Cell 与碰撞矩形的有效载荷估算。
- `EstimatedChunkGpuTextureBytes`：已提交 Raster Chunk Texture 的逻辑 RGBA8 字节数。
- `PreparedFallbackDecodedBytes` / `EstimatedFallbackGpuTextureBytes`：Preview 回退图在 CPU 准备态和 GPU 提交态的对应估算。
- `ResidentChunkLeaseCount`、`InFlightChunkLoadCount` 与 `LevelStateCount`：判断资源由哪个运行时状态继续持有。

这些字段回答的是“当前 Session 明确拥有多少资源”，不是进程总内存分析器：不包含压缩归档、解码器临时缓冲、CLR 数组头、驱动缓存、线程栈，已经失去引用但尚未 GC 的对象也不算作 Session 所有权。在途 Loader 的内部临时缓冲无法安全观察，因此只报告在途数量。

GPU 字段会与 Hosting 的 `TextureLibrary` 显存估算重叠，二者用于交叉核对，不得相加。若要把 CPU 所有权纳入统一遥测，可注册：

```csharp
using IDisposable registration = context.RegisterCpuMemoryUsage(
    "tileworld.streaming.payloads",
    CpuMemoryDomain.Managed,
    () => session.CaptureMemoryDiagnostics().OwnedCpuPayloadBytes);
```

注册本身不拥有 Session；应先释放注册，再释放 Session。

不使用 Hosting 时可直接组合：

```csharp
TileWorldDescriptor descriptor = tileWorldLibrary.Get(world);
using var session = new TileWorldStreamingSession(
    descriptor,
    tileSetLibrary,
    textureLibrary);

session.Update(viewportSnapshot);
session.Draw(batch);
```

若游戏需要自定义 ECS/Renderer，也可以单独使用固定 Level 的 `TileWorldChunkLoader` 与现有
`WorldChunkStreamer<TileWorldChunkLease>`。此时游戏必须在主线程显式 Commit，并让 Streamer 保持
Lease 的唯一所有权。

## 当前限制与下一步

### 可用性结论

当前实现已满足有限大地图的常用单 Session 路径：声明式离线编译或预切片导入、Preview 回退图首屏保底、交互 Viewport、LOD 滞回、有界后台读取/解码、逐帧 GPU 上传、单 LOD 稳态 Raster 驻留预算、非阻塞退休、确定性释放和低频内存归因。仓库外 `12000×12000`、400 张 WebP 细节切片样本已经通过本地 SDK 运行与五轮“全景 → 细节 → 全景”回归。

因此 TileWorld 不再作为当前开发主线。以下限制只在真实项目出现可复现卡顿、显存峰值、重复解码或内容迭代阻塞时恢复；普通游戏不需要等待它们即可使用现有能力。

- 独立 Preview 回退图（Fallback Surface）已支持；未声明时保持最粗可用 LOD 的原有行为。
- 不同 Session 尚不共享跨 Viewport 的解码结果或 Texture 引用计数。
- 已有逐帧上传预算与单 LOD Raster 稳态驻留预算，但没有 LOD 交接期 Session 总预算、LRU、PBO、逐 Chunk 热重载或层级淡化。
- `.mgworld` 可来自权威 TileMap，也可来自固定网格的 `*.pretiledworld.json`；后者的 LOD0 是纯视觉
  Raster，不提供 Tile/碰撞语义。
- LOD0 碰撞数据已经随 Lease 可用，但视觉 Session 不替 Gameplay 自动建立空间查询索引。

离线 `preTiledRaster` 适配器已经把既有多切片地图规范化进相同 `.mgworld` 索引；Session 仍不扫描目录
或猜测文件名。`12000×12000`、400 张详细 WebP 的仓库外样本已验证 Preview 回退图、6 层 LOD、后台解码和
逐帧上传；仓库内自动测试继续使用小型合成 Fixture。跨 View 共享、交接期总预算、逐 Chunk 构建缓存与
可选 LOD 淡化均转为需求驱动维护项，不再自动成为下一阶段。

## 验证

```powershell
dotnet run --project src/Engine.Features/TileWorldStreaming.Tests/TileWorldStreaming.Tests.csproj -c Release
dotnet run --project src/Engine.Features/TileWorldStreaming.VisualTests/TileWorldStreaming.VisualTests.csproj -c Release
```

无窗口测试覆盖密度阈值、双向滞回、稀疏空 Chunk、权威 LOD0、后台准备/GPU 提交边界、逐帧
张数/字节预算、分层暂存与原子发布、上传失败回滚、不可取消解码的非阻塞退休、最粗可用 LOD 常驻、
完整替换和按世界区域裁取最粗可用 LOD。

VisualTests 不提交真实地图资源，而是在临时目录生成一个 `4×4` 的小型 `.mgworld v3`、LOD1/LOD2
无损 WebP Chunk 和独立 Preview 回退图。运行后可通过拖拽/滚轮观察 Viewport，通过 `Q/W/E`
直接切换 LOD2/LOD1/LOD0，`Space` 暂停或恢复自动巡游，`R` 重建 Session 并重放
Preview 回退图 → Raster LOD → LOD0 的异步接管过程，`ESC` 退出。窗口标题与顶部状态条会显示当前来源、
待切换 LOD、每帧上传量、退休 LOD、Preview 回退图常驻数和 Fallback Surface Draw 数；交互 Zoom 使用平滑
Viewport Animate。`--smoke` 使用隐藏窗口故意在解码未结束时连续替换 LOD，并守卫 Scene Step
不得因同步 join 产生长帧。

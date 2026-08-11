# TileWorld 运行时 LOD 与流式加载

`Engine.Features.TileWorldStreaming` 把离线 `.mgworld` 产物接到 `ViewportSnapshot`、通用
`WorldChunkStreamer<T>` 和 `TextureLibrary`。它负责视觉 LOD 选择、归档随机读取、WebP 解码、
主线程 GPU 提交、跨层替换和分层绘制；Viewport 仍不知道地图格式，TileWorld Reader 也不知道
Camera 或 OpenGL。

```text
ViewportSnapshot
  → TileWorldLodSelector                 Zoom → DesiredLevel，带滞回
  → TileWorldStreamingSession
       ├─ 可选逐 Layer Preview Surface   独立解码并常驻的最后保底
       ├─ 常驻最粗生成 LOD 状态          加载/失败/快速移动时的首选回退
       ├─ Active Level                   当前稳定绘制层
       └─ Pending Level                  可见 Chunk 全部就绪后原子替换 Active
            → WorldChunkStreamer<TileWorldChunkLease>
                 → TileWorldChunkLoader  有界后台读取 + WebP 解码
                 → staged upload queue   主线程按张数/字节预算提交
```

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
Session 终止释放仍遵循 Preview → Pending/Retired → Active → Fallback，每个 Lease 再移除自己注册的 Texture。

当 Zoom 已选择另一个 LOD 时，当前 Active Level 会冻结现有驻留集合，只作为新层级接管前的画面桥接；
它不会用新的全景范围扩张高细节 Chunk。缺失区域继续由最粗层或 Preview 覆盖，因此快速从最大细节拉回
全景既不会突破 `MaximumTrackedChunks`，也不会短暂加载整张地图的 LOD0。

## LOD 选择与滞回

LOD 不是硬编码 Zoom 列表，而是从世界 Chunk 尺寸和离线 Raster 像素密度推导。默认目标为
一个 Raster texel 最多映射约一个屏幕像素：

```text
referenceZoom = min(rasterWidth / baseChunkWorldWidth,
                    rasterHeight / baseChunkWorldHeight)

LOD n → n+1 boundary = referenceZoom / 2^(n+1)
```

`targetPixelsPerTexel` 可整体调整清晰度/显存取舍。`hysteresisRatio = 0.1` 会在阈值两侧形成
10% 的乘法死区：缩小时越过下边界才切到更粗层，放大时越过上边界才切回更细层。因此 Wheel、
Pinch 或惯性 Zoom 在阈值附近不会反复创建和销毁 Chunk。

Level 0 始终读取权威 Tile/碰撞并通过 TileSet/Sprite 命令绘制；Level 1+ 解码逐 Layer WebP。
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
期间旧 Active、最粗 LOD 或 Preview 继续遮盖缺口。后续 Layer 上传失败会删除该 Chunk 已暂存的
Texture，不影响旧 Active。`TileWorldStreamingUpdateResult.TexturesUploaded/TextureBytesUploaded`
提供本帧提交量，`RetiringLevels` 可观察尚未完成的取消任务。

OpenGL Texture 创建仍停留在拥有图形 Context 的线程。IO、SHA-256 与 WebP 解码通过
`MaximumConcurrentLoads` 有界并行；单纯把 `glTexImage2D` 放进线程池并不安全，若未来实测单张
Texture 上传仍超预算，再独立评估 PBO 或共享上传 Context/Fence。

`Inline` 模式主要用于无窗口测试、离线工具或确定知道 Chunk 很小的场景。它仍保持显式 Commit
边界，但归档读取和图片解码会占用调用线程，不建议作为大型地图默认值。

## Preview、Fallback 与无空洞替换

当前 Fallback 是 `.mgworld` 中最粗的生成 LOD，其 Streamer 状态在 Session 生命周期内始终保留，
具体 Chunk 仍按 Visible/Preloaded/Retained 范围流式驻留。Pending Level 只有在
当前可见范围内每个坐标都得到“真实 Payload 或确认稀疏空 Chunk”的已提交 Lease 后，才会替换旧
Active Level。

当 Active 因快速移动尚缺少部分 Chunk 时，Renderer 不会把整张粗图再次叠加到详细图下面；它按
缺失的 Active 世界矩形裁取最粗 Layer 的对应 UV 区域。这样透明 Layer 不会因为粗细两份内容重叠
而重复累加，Gutter 仍能在裁取边缘提供安全采样。

加载失败时旧 Active 保留；通用 Streamer 按 Viewport Revision 的既有策略重试。若旧 Active 的新
可见区域也尚未到达，则只在缺失区域显示最粗 Fallback。`CaptureDiagnostics()` 可读取 Desired、
Active、Pending、Fallback Level 及各自 Pending/Loading/Loaded/Failed 计数。

若清单声明 `build.fallbackSurfaces[]`，Session 会独立后台读取并解码这些全世界低清图片，再在
`Update` 调用线程让 `TileWorldFallbackSurfaceLease` 共享同一 GPU 上传预算并原子发布。它不占用 Chunk
Streamer 的后台并发预算，也不等待最粗 LOD；只有对应世界区域连最粗 Chunk 都尚未就绪时，Renderer 才按
Layer 和世界 `bounds` 裁取 Preview UV。`TileWorldDrawStatistics.FallbackSurfaceQuads` 可单独观察该路径。
Preview 绑定明确 Layer，因此 `DrawLayer()` 仍能保持 Gameplay 深度穿插边界；扁平全图 Preview 应绑定到
最底部地表 Layer。
`CaptureDiagnostics()` 还会报告是否声明、是否就绪以及当前常驻的 Fallback Surface 数量，但不暴露 GPU Handle。

## 底层组合入口

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

- 独立 Preview/Fallback Surface 已支持；未声明时保持最粗生成 LOD 的原有行为。
- 不同 Session 尚不共享跨 Viewport 的解码结果或 Texture 引用计数。
- 已有逐帧上传预算，但没有总显存预算降级、LRU、PBO、逐 Chunk 热重载或层级淡化。
- `.mgworld` 可来自权威 TileMap，也可来自固定网格的 `*.pretiledworld.json`；后者的 LOD0 是纯视觉
  Raster，不提供 Tile/碰撞语义。
- LOD0 碰撞数据已经随 Lease 可用，但视觉 Session 不替 Gameplay 自动建立空间查询索引。

离线 `preTiledRaster` 适配器已经把既有多切片地图规范化进相同 `.mgworld` 索引；Session 仍不扫描目录
或猜测文件名。`12000×12000`、400 张详细 WebP 的仓库外样本已验证 Preview、6 层 LOD、后台解码和
逐帧上传；仓库内自动测试继续使用小型合成 Fixture。下一阶段可聚焦总显存预算/LRU、跨 View 共享、
逐 Chunk 构建缓存与可选 LOD 淡化。

## 验证

```powershell
dotnet run --project src/Engine.Features/TileWorldStreaming.Tests/TileWorldStreaming.Tests.csproj -c Release
dotnet run --project src/Engine.Features/TileWorldStreaming.VisualTests/TileWorldStreaming.VisualTests.csproj -c Release
```

无窗口测试覆盖密度阈值、双向滞回、稀疏空 Chunk、权威 LOD0、后台准备/GPU 提交边界、逐帧
张数/字节预算、分层暂存与原子发布、上传失败回滚、不可取消解码的非阻塞退休、最粗层常驻、
完整替换和按世界区域裁取 Fallback。

VisualTests 不提交真实地图资源，而是在临时目录生成一个 `4×4` 的小型 `.mgworld v3`、LOD1/LOD2
无损 WebP Chunk 和独立 Preview Surface。运行后可通过拖拽/滚轮观察 Viewport，通过 `Q/W/E`
直接切换 LOD2/LOD1/LOD0，`Space` 暂停或恢复自动巡游，`R` 重建 Session 并重放
Preview → Raster LOD → LOD0 的异步接管过程，`ESC` 退出。窗口标题与顶部状态条会显示当前来源、
待切换 Level、每帧上传量、退休 Level、Preview 常驻数和 Fallback Draw 数；交互 Zoom 使用平滑
Viewport Animate。`--smoke` 使用隐藏窗口故意在解码未结束时连续替换 LOD，并守卫 Scene Step
不得因同步 join 产生长帧。

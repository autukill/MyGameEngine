# TileWorld 运行时 LOD 与流式加载

`Engine.Features.TileWorldStreaming` 把离线 `.mgworld` 产物接到 `ViewportSnapshot`、通用
`WorldChunkStreamer<T>` 和 `TextureLibrary`。它负责视觉 LOD 选择、归档随机读取、WebP 解码、
主线程 GPU 提交、跨层替换和分层绘制；Viewport 仍不知道地图格式，TileWorld Reader 也不知道
Camera 或 OpenGL。

```text
ViewportSnapshot
  → TileWorldLodSelector                 Zoom → DesiredLevel，带滞回
  → TileWorldStreamingSession
       ├─ 常驻最粗生成 LOD 状态          加载/失败/快速移动时的回退
       ├─ Active Level                   当前稳定绘制层
       └─ Pending Level                  可见 Chunk 全部就绪后原子替换 Active
            → WorldChunkStreamer<TileWorldChunkLease>
                 → TileWorldChunkLoader  后台随机读取 + WebP 解码
                 → CommitTextures        主线程 TextureLibrary 提交
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
            maximumLoadsStartedPerUpdate: 8)));

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
GameInstance/Scene Controller 在 `OnCreate` 创建，在 `OnDestroy` 释放。Session 释放顺序固定为
Pending → Active → Fallback；每个 Chunk Lease 再移除自己注册的 Texture。

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

`TileWorldStreamingSession.Update` 在调用线程收割完成项，然后调用
`TileWorldChunkLease.CommitTextures(TextureLibrary)`。这让 OpenGL Texture 创建始终停留在图形上下文
线程。一个 Chunk 的后续 Layer 上传失败时，本次已经注册的 Texture 会全部回滚；旧 Active 和最粗
Fallback 不受影响。

`Inline` 模式主要用于无窗口测试、离线工具或确定知道 Chunk 很小的场景。它仍保持显式 Commit
边界，但归档读取和图片解码会占用调用线程，不建议作为大型地图默认值。

## Fallback 与无空洞替换

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

- Fallback 当前来自最粗生成 LOD；清单尚不能单独声明 `preview.webp` 全图 Surface。
- 不同 Session 尚不共享跨 Viewport 的解码结果或 Texture 引用计数。
- 没有按显存预算降级、LRU、逐 Chunk 热重载或层级淡化。
- `.mgworld` 仍来自权威 TileMap；历史 `tile_{row}_{column}.webp + preview.webp` 尚未接入
  `preTiledRaster` 导入器。
- LOD0 碰撞数据已经随 Lease 可用，但视觉 Session 不替 Gameplay 自动建立空间查询索引。

下一切片应先把独立 Preview/Fallback Surface 纳入声明式 TileWorld，再实现既有多切片地图导入；
GPU 预算和跨 View 共享应由真实 12000×12000 样本测量后决定。

## 验证

```powershell
dotnet run --project src/Engine.Features/TileWorldStreaming.Tests/TileWorldStreaming.Tests.csproj -c Release
```

无窗口测试覆盖密度阈值、双向滞回、稀疏空 Chunk、权威 LOD0、后台准备/GPU 提交边界、上传失败
回滚、最粗层常驻、完整替换和按世界区域裁取 Fallback。

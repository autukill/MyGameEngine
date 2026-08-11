# World Chunk Streaming 使用与实现边界

`Engine.Features.WorldStreaming` 把 `ViewportSnapshot` 转换为稳定的世界分块驻留请求。它解决“大地图当前该保留哪些 Chunk、先加载哪些 Chunk、何时取消和释放”的协调问题，不负责地图格式、图片解码、GPU 上传或具体绘制。

依赖方向保持单向：

```text
Input → ViewportController → Camera2D
                    └─ ViewportSnapshot
                              ↓
                    WorldChunkStreamer<TChunk>
                              ↓
                  游戏提供的 IWorldChunkLoader<TChunk>
                              ↓
                  Content Package / Tile Data / GPU Lease
```

Viewport 不知道 Chunk 是否存在；WorldStreaming 也不依赖 Hosting、ContentAssets 或 Tilemaps。游戏可以让一个 Chunk lease 包含声明式 Content 包、TileMap 数据、碰撞数据和绘制实例，也可以只包含其中一部分。

## 最小用法

```csharp
using System.Numerics;
using GameEngine.Features.ViewportNavigation;
using GameEngine.Features.WorldStreaming;

var layout = new WorldChunkLayout(
    chunkSize: new Vector2(512f, 512f),
    origin: Vector2.Zero);

using var chunks = new WorldChunkStreamer<MapChunkLease>(
    layout,
    new MapChunkLoader(),
    new WorldChunkStreamingOptions(
        preloadMarginChunks: 1,
        retainMarginChunks: 2,
        maximumConcurrentLoads: 4,
        maximumTrackedChunks: 4096,
        retryFailedOnViewportChange: true,
        maximumLoadsStartedPerUpdate: 8));

// 在主线程每帧更新一次；通常放在 Viewport 更新之后、世界绘制之前。
ViewportSnapshot snapshot = viewport.CaptureSnapshot();
WorldChunkUpdateResult result = chunks.Update(snapshot);

if (chunks.TryGetChunk(new WorldChunkCoordinate(3, 7), out MapChunkLease? chunk))
    chunk.Draw();
```

Loader 只需要返回一个可释放 lease：

```csharp
sealed class MapChunkLoader : IWorldChunkLoader<MapChunkLease>
{
    public async ValueTask<MapChunkLease> LoadAsync(
        WorldChunkCoordinate coordinate,
        CancellationToken cancellationToken)
    {
        // IO、解码或包加载由游戏/适配器决定。
        return await MapChunkLease.LoadAsync(coordinate, cancellationToken);
    }
}
```

异步 Loader 必须响应传入的 `CancellationToken`。Streamer 在 Chunk 离开 Retained 范围或自身释放时会请求取消；不响应取消的 Loader 会延迟资源回收，甚至令同步 `Dispose()` 等待。

## 三层驻留范围

同一 Snapshot 会产生三个同心范围：

- `Visible`：当前可见世界 AABB 覆盖的 Chunk，始终最先启动加载。
- `Preloaded`：Visible 外扩 `PreloadMarginChunks`，为即将进入视野的区域预热。
- `Retained`：Visible 外扩 `RetainMarginChunks`；Chunk 离开该范围才取消或释放，避免边界附近反复抖动。

`RetainMarginChunks` 必须大于等于 `PreloadMarginChunks`。范围按行优先、从左上到右下确定性遍历；负世界坐标使用数学 floor。可见边界恰好落在 Chunk 右/下边缘时，不会错误多算相邻 Chunk。

`WorldChunkLayout.Limits` 可约束有限地图。当前 Viewport 必须与 Limits 相交；完全移出有限世界会明确失败，通常应由 Viewport `Clamp` 或 `Bounce` 保证这个条件。

## 帧时间与并发预算

两个预算含义不同：

- `MaximumConcurrentLoads` 限制尚未完成的异步加载数量，保护 IO、解码线程和资源后端。
- `MaximumLoadsStartedPerUpdate` 限制一次 `Update` 新启动的数量。即使 Loader 同步命中缓存，也不会在单帧无上限地装配 Chunk。
- `MaximumTrackedChunks` 在修改驻留状态前检查完整 Retained 范围；超限时原子失败，不会留下半套新状态。

默认值分别为 4 个异步加载、每次 Update 启动 8 个、最多跟踪 4096 个 Chunk。实际项目应结合单 Chunk 解码成本、移动速度和平台内存预算测量，而不是简单增大这些值。

## 生命周期与失败语义

- 所有状态迁移和 `ChunkLoaded/ChunkUnloaded/ChunkFailed` 事件都只在调用 `Update` 的线程发生。
- Loader 可在任意线程完成；Streamer 下一次 `Update` 才收割结果。
- 已完成 lease 只由 Streamer 拥有，并在离开 Retained 或 `Dispose` 时恰好释放一次。
- 离开 Retained 的在途任务先收到取消；若任务仍成功完成，结果会立即释放，不重新进入世界。
- 失败默认只在 Viewport Revision 再次变化且 Chunk 仍被需要时重试，避免稳定画面每帧重试损坏资源。
- `Dispose` 幂等。Scene 通常拥有自己的 Streamer，并应在销毁 Scene 内容之前先释放它。

`CaptureDiagnostics()` 提供 Pending、Loading、Loaded、Failed 以及三层驻留计数，不暴露 Loader 或 GPU 句柄。相同且已完全加载的 Snapshot 重复更新保持 `0 B` 托管分配。

## 与 LOD、Content 和绘制的边界

当前切片只决定 Chunk 的空间驻留，不选择 LOD。离线编译器已经提供权威 LOD0 `.mgworld` Chunk、碰撞、索引校验和逻辑 Package 租约；下一阶段生成 LOD1+ WebP Layer，再由运行时策略根据 `ViewportSnapshot.Zoom` 选择具体 lease。该扩展不应把 Content 或 GPU 所有权塞回 Viewport。格式与使用见 [TileWorld 离线切片编译器](TILE_WORLD_OFFLINE_COMPILER.md)。

推荐每个 Chunk 的资源边界如下：

```text
WorldChunkCoordinate
  └─ MapChunkLease (游戏拥有的组合 lease)
       ├─ LoadedContentPackage lease
       ├─ Tile/Collision CPU data
       ├─ Scene instances or render proxies
       └─ optional GPU resources
```

不要在 `ChunkLoaded` 事件中永久复制 lease；通过 `TryGetChunk` 借用读取，并让 Streamer 保持唯一所有权。若多个 Viewport 观察同一世界，v1 建议由游戏提供共享 Loader/缓存并让每个 Streamer 持有独立 lease；跨 View 合并引用计数留给资源层，而不是合并 Camera 状态。

## 验证

```powershell
dotnet run --project src/Engine.Features/WorldStreaming.Tests/WorldStreaming.Tests.csproj -c Release
```

无窗口测试覆盖精确边界与负坐标、Visible/Preloaded/Retained、并发与取消、失败重试、原子跟踪预算、幂等释放和稳定 Snapshot `0 B` 分配。

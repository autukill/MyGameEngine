# TileWorld 离线切片编译器

`Engine.Features.TileWorlds` 为大型 Tile 世界提供独立于 GPU 和 Viewport 的编译产物边界。小地图继续使用整体驻留的 `TileMap` JSON；需要按 Chunk 加载、验证和释放的大地图声明为 `TileWorld`，由 AssetCompiler 转换为单个可随机读取的 `.mgworld` 归档。

当前阶段已经完成权威 `LOD0`。清单可以提前声明计划生成的 LOD 数量，但 `.mgworld` 中只有确实存在的 Chunk 才能通过 `Contains` 查询；远景 WebP、运行时 LOD 选择和跨层切换尚未实现。

## 为什么不直接缩小 Tile ID

Tile ID、碰撞类型和游戏规则是离散数据。把四个格子缩成一个 Tile 会丢失墙体、触发器和空间查询语义。因此边界固定为：

```text
LOD0：权威 Tile Cell + Tile Transform + 静态碰撞
LOD1+：未来的只读视觉栅格，不参与碰撞和 Gameplay 查询
```

未来 LOD1+ 会把每个可见 Layer 独立烘焙，保留 Depth；不会把多个可能与 GameInstance 穿插的 Layer 无条件合成一张图。

## 资产声明

```json
{
  "schemaVersion": 1,
  "id": "world.assets",
  "tileWorlds": [{
    "name": "world.overworld",
    "path": "maps/overworld.tilemap.json",
    "build": {
      "bounds": { "minX": -32, "minY": -16, "maxX": 127, "maxY": 95 },
      "lodCount": 4,
      "rasterChunkSize": { "width": 512, "height": 512 },
      "encoding": "webpLossless",
      "sampling": "smooth",
      "gutter": 2
    }
  }]
}
```

- `bounds` 使用 LOD0 Chunk 坐标，必须完整包含源地图的所有已分配 Chunk。
- `lodCount` 为 `1..8`。当前编译 LOD0；数值同时成为未来视觉层级的稳定声明。
- `rasterChunkSize`、`encoding`、`sampling` 和 `gutter` 已进入严格 Schema，供下一阶段视觉烘焙使用，不影响当前 LOD0 字节。
- v1 要求同一个 TileWorld 的所有 Layer 使用相同 TileSize。不同 Layer 仍可使用不同 TileSet。
- Manifest、源地图和输出路径都必须留在对应 Package 根目录内。

编译后清单被规范化为：

```json
{
  "tileWorlds": [{
    "name": "world.overworld",
    "path": "maps/overworld.mgworld"
  }]
}
```

运行时清单不保留 `build`，也不会复制原始 `.tilemap.json`。`GameAssets.TileWorlds.WorldOverworld` 只携带稳定逻辑名称，不携带文件路径或 Stream。

## `.mgworld` 格式

归档采用 little-endian、版本化、确定性布局：

```text
Magic + Version
World Metadata
  ├─ 名称、Chunk 尺寸、边界、声明 LOD 数
  └─ Layer 名称、TileSet、Depth、Offset、Visible
Chunk Index（Level → Y → X）
  └─ Key、PayloadKind、Offset、Length、SHA-256
Chunk Payloads
  ├─ 逐 Layer 的 RLE Tile Cell
  └─ 逐 Layer、逐 Chunk 的合并碰撞矩形
```

Tile Cell 的低 16 bit 保存 `TileId`，bit 16–19 保存 Flip/90° 旋转标记。RLE 对相邻相同的完整编码值进行确定性压缩，不依赖平台压缩库版本。碰撞沿用 `TileCollisionBaker` 的 Chunk 内贪心合并结果，矩形不会跨 Chunk，方便后续局部驻留和释放。

Reader 在分配 Payload 前检查 Magic、版本、字符串、Layer/Chunk 数量、Offset、Length、顺序和格式上限。读取具体 Chunk 时再次验证 SHA-256；截断或被修改的 Payload 不会进入 Gameplay。

索引从 v1 就显式区分 `AuthoritativeTiles` 和 `RasterLayers`。当前编译器只写入前者；这让下一阶段加入 LOD1+ 图片时不需要靠 Level 猜测数据类型，也不必改写现有 LOD0 语义。

## 运行时使用

包加载只读取小型归档索引并注册逻辑目录，归档文件仍是 Package 借用资源：

```csharp
using LoadedContentPackage package = content.Load(GameAssets.Packages.Root);
TileWorldRef world = package.GetTileWorld(GameAssets.TileWorlds.WorldOverworld.Name);

using TileWorldArchiveReader archive = content.TileWorlds.Open(world);
if (archive.Contains(new TileWorldChunkKey(0, 3, -1)))
{
    TileWorldChunkData chunk = archive.ReadChunk(new TileWorldChunkKey(0, 3, -1));
    // 当前由游戏或后续 Loader 把 Chunk 装配为 Tile/Collision lease。
}
```

`TileWorldLibrary` 不删除归档文件。最后一个 `LoadedContentPackage` 释放时先注销 TileWorld，再卸载 TileMap、TileSet、Sprite 和 Texture。Reader 自己拥有打开的文件流；需要在 Package 释放前关闭。

## 与 WorldStreaming 的边界

```text
ViewportSnapshot
  → WorldChunkStreamer<TLease>        决定驻留范围、并发和取消
  → 未来 TileWorldChunkLoader         把坐标映射到归档 Payload
  → TileWorldArchiveReader            验证并解码权威数据
```

Viewport 不知道 `.mgworld`、Tile 或 GPU；归档 Reader 也不知道 Camera。下一切片会实现 LOD1+ WebP Layer 烘焙，之后才增加 `TileWorldChunkLoader`、Zoom LOD 策略、滞回和旧层级保留。

## 当前限制与下一步

- 当前只有 LOD0；`rasterChunkSize/encoding/sampling/gutter` 尚未生成图片。
- 当前异步随机读取接口和 WorldStreaming Adapter 尚未实现。
- 当前增量缓存按 Package 指纹复用；Chunk 索引已经提供稳定 Payload Hash，逐 Chunk 编码复用留给视觉 LOD 阶段。
- 当前源 TileMap 仍整体解析；超大型导入格式和前向解析临时索引后续按真实地图规模补充。
- 尚无 Tiled 导入、无限程序化地图、视觉 LOD 淡化或 GPU 显存预算。

验证命令：

```powershell
dotnet run --project src/Engine.Features/TileWorlds.Tests/TileWorlds.Tests.csproj -c Release
dotnet run --project src/Engine.Tools.AssetCompiler.Tests/Engine.Tools.AssetCompiler.Tests.csproj -c Release
```

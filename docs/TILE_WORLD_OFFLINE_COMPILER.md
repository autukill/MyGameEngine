# TileWorld 离线切片编译器

`Engine.Features.TileWorlds` 为大型 Tile 世界提供独立于 GPU 和 Viewport 的编译产物边界。小地图继续使用整体驻留的 `TileMap` JSON；需要按 Chunk 加载、验证和释放的大地图声明为 `TileWorld`，由 AssetCompiler 转换为单个可随机读取的 `.mgworld` 归档。

当前已经完成权威 `LOD0`、预切片栅格 `LOD0`、生成式 `LOD1+` 与运行时流式适配。AssetCompiler 既可从 TileMap、TileSet 和原始 Sprite 帧确定性烘焙，也可导入 `tile_{row}_{column}.webp` 形式的既有地图；归档中只有确实存在的 Chunk 才能通过 `Contains` 查询。`Engine.Features.TileWorldStreaming` 进一步提供 Zoom LOD、滞回、后台 WebP 解码、主线程 GPU Lease 和最粗层回退。

## 为什么不直接缩小 Tile ID

Tile ID、碰撞类型和游戏规则是离散数据。把四个格子缩成一个 Tile 会丢失墙体、触发器和空间查询语义。因此边界固定为：

```text
LOD0：权威 Tile Cell + Tile Transform + 静态碰撞
LOD1+：逐 Layer 的只读视觉 WebP，不参与碰撞和 Gameplay 查询
```

LOD1+ 把每个可见 Layer 独立烘焙，保留 Metadata 中的 Depth；不会把多个可能与 GameInstance 穿插的 Layer 无条件合成一张图。不可见 Layer 不进入 Raster Payload，透明空 Chunk 不写入归档。

纯视觉历史地图可以使用 Raster LOD0。它不伪造 Tile Cell、TileSet 或碰撞语义；Gameplay 权威状态由游戏自己的空间数据提供，而 TileWorld 只负责地图画面流式驻留。

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
      "gutter": 2,
      "fallbackSurfaces": [{
        "layer": "ground",
        "path": "maps/preview.webp",
        "sampling": "smooth"
      }]
    }
  }]
}
```

- `bounds` 使用 LOD0 Chunk 坐标，必须完整包含源地图的所有已分配 Chunk。
- `lodCount` 为 `1..8`。`1` 只生成 LOD0；更大的值生成 Level `1..lodCount-1` 视觉层。
- Level `n` 的一个 Raster Chunk 覆盖 `2^n × 2^n` 个 LOD0 Chunk；负坐标使用数学 floor 分组。
- `rasterChunkSize` 是每个视觉 Chunk 的固定内区像素尺寸。Level 越高，固定像素覆盖越大的世界区域，因此像素密度按 2 的幂下降。
- `gutter` 在四周增加挤出的边缘像素；例如 `512×512`、Gutter `2` 实际编码为 `516×516`，运行时应使用内区 UV 映射原世界范围。
- `encoding` 在存在视觉 LOD 时必须为 `webpLossless`。编码使用 exact 无损模式，透明像素 RGBA 也不会被有损重写。
- `sampling` 为 `smooth` 或 `pixelArt`，决定离线缩放时的双线性或最近邻采样。
- 当前版本要求同一个 TileWorld 的所有 Layer 使用相同 TileSize。不同 Layer 仍可使用不同 TileSet。
- Manifest、源地图和输出路径都必须留在对应 Package 根目录内。
- `fallbackSurfaces` 可选；每项把一张低清全世界图片绑定到一个可见 Layer。同一 Layer 只能声明一次，路径相对所属 Package，`sampling` 独立于 Chunk Raster 设置。
- Fallback 源图会被重新编码为 exact 无损 WebP 并嵌入 `.mgworld`，运行时目录不保留松散 Preview 文件。
- Fallback Surface 覆盖声明的完整 `bounds`，只承担加载期视觉连续性，不生成 Tile、碰撞或 Gameplay 数据。扁平的历史 `preview.webp` 通常绑定到最底部地表 Layer；需要透明分层语义时应逐 Layer 提供 Preview。

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

## 导入既有 WebP 切片

对于已经离线切成固定网格的纯视觉地图，`path` 使用 `.pretiledworld.json` 描述符：

```json
{
  "schemaVersion": 1,
  "name": "world.legacy-map",
  "chunkWorldSize": { "width": 600, "height": 600 },
  "chunkPattern": "Map/tile_{row}_{column}.webp",
  "layer": { "name": "map", "depth": 0 }
}
```

对应 Content 清单仍使用标准 `tileWorlds` 入口：

```json
{
  "name": "world.legacy-map",
  "path": "legacy-map.pretiledworld.json",
  "build": {
    "bounds": { "minX": 0, "minY": 0, "maxX": 19, "maxY": 19 },
    "lodCount": 6,
    "rasterChunkSize": { "width": 600, "height": 600 },
    "encoding": "webpLossless",
    "sampling": "smooth",
    "gutter": 0,
    "fallbackSurfaces": [{
      "layer": "map",
      "path": "Map/preview.webp",
      "sampling": "smooth"
    }]
  }
}
```

- `{row}` / `{column}` 必须同时存在，所有路径仍受 Package 根目录安全边界约束。
- `bounds` 内每个 LOD0 文件都必须存在且尺寸等于 `rasterChunkSize`；缺片、非 WebP 或尺寸不一致在构建期失败。
- LOD0 保留原始 WebP 编码字节，不重复压缩；因此可以接纳已有的有损或无损 WebP。
- LOD1+ 每层把前一层的 2×2 Chunk 降采样为一个固定尺寸的无损 WebP。边界为奇数时，超出世界的象限保持透明。
- 原始 WebP Preview 同样原样嵌入归档，未就绪区域按世界坐标裁取对应 UV。
- Raster-only 归档不要求声明虚假的 TileSet，也不提供 Tile/碰撞查询。
- 当前预切片导入要求 `gutter: 0`；独立纹理使用 Clamp 采样，不存在 Atlas 跨帧串色。

## `.mgworld` 格式

归档采用 little-endian、版本化、确定性布局：

```text
Magic + Version
World Metadata
  ├─ 名称、Chunk 尺寸、边界、声明 LOD 数
  └─ Layer 名称、TileSet、Depth、Offset、Visible
Fallback Surface Index（LayerIndex）
  └─ LayerIndex、尺寸、Encoding、Sampling、Length、SHA-256
Chunk Index（Level → Y → X）
  └─ Key、PayloadKind、Offset、Length、SHA-256
Fallback Surface Payloads
  └─ 逐 Layer 的 exact 无损 WebP 全图
Chunk Payloads
  ├─ TileMap 来源 LOD0：逐 Layer 的 RLE Tile Cell 与合并碰撞矩形
  ├─ 预切片来源 LOD0：保留源编码的逐 Layer WebP Raster
  └─ LOD1+：逐 Layer 的 exact 无损 WebP Raster
```

Tile Cell 的低 16 bit 保存 `TileId`，bit 16–19 保存 Flip/90° 旋转标记。RLE 对相邻相同的完整编码值进行确定性压缩，不依赖平台压缩库版本。碰撞沿用 `TileCollisionBaker` 的 Chunk 内贪心合并结果，矩形不会跨 Chunk，方便后续局部驻留和释放。

Reader 在分配 Payload 前检查 Magic、版本、字符串、Layer/Chunk 数量、Offset、Length、顺序和格式上限。读取具体 Chunk 时再次验证 SHA-256；截断或被修改的 Payload 不会进入 Gameplay。

归档格式 v3 自描述 TileSize、Raster 尺寸、Gutter、采样方式和可选逐 Layer Fallback Surface。Fallback 描述符保存 LayerIndex、图片尺寸、编码、采样、长度和 SHA-256，Payload 位于 Chunk Payload 之前并按 LayerIndex 确定性排列。索引显式区分 `AuthoritativeTiles` 与 `RasterLayers`：权威 Tile 只允许出现在 Level 0；Raster 可以从 Level 0 开始并继续覆盖 LOD1+。每个 Raster Chunk Payload 按 LayerIndex 保存 Encoding、内区尺寸、Gutter 和 WebP 字节；Chunk 外层继续使用 SHA-256 做完整性验证。

离线栅格器与运行时 `TileMapRenderer` 共用 `TileTransformOperations`，因此 FlipX/FlipY 与 0/90/180/270° 旋转采用相同的 Y 向下、正弧度视觉逆时针约定。Tile Sprite 的固定 SubImage 会从 Single、Grid 或多图片 Frames 声明解析；Atlas 是否启用不改变烘焙像素来源。

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

var visualKey = new TileWorldChunkKey(2, 0, -1);
if (archive.Contains(visualKey) &&
    archive.GetPayloadKind(visualKey) == TileWorldChunkPayloadKind.RasterLayers)
{
    TileWorldRasterChunkData visual = archive.ReadRasterChunk(visualKey);
    foreach (TileWorldRasterLayerData layer in visual.Layers)
    {
        // layer.EncodedBytes 是 exact 无损 WebP；可由 TileWorldChunkLoader 异步准备。
    }
}
```

`TileWorldLibrary` 不删除归档文件。最后一个 `LoadedContentPackage` 释放时先注销 TileWorld，再卸载 TileMap、TileSet、Sprite 和 Texture。Reader 自己拥有打开的文件流；需要在 Package 释放前关闭。

游戏通常不需要手工读取 Payload，而是通过 `TileWorldStreamingSession` 连接 Viewport、LOD 与 GPU；完整用法见 [TileWorld 运行时 LOD 与流式加载](TILE_WORLD_RUNTIME_STREAMING.md)。

## 与 WorldStreaming 的边界

```text
ViewportSnapshot
  → WorldChunkStreamer<TLease>        决定驻留范围、并发和取消
  → TileWorldChunkLoader              把坐标映射到归档 Payload并后台解码
  → TileWorldArchiveReader            验证并解码权威数据
```

Viewport 不知道 `.mgworld`、Tile 或 GPU；归档 Reader 也不知道 Camera。`TileWorldStreamingSession` 在适配层组合这些模块，并保持最粗生成 LOD 作为回退。

## 当前限制与下一步

- 独立 Fallback Surface 已进入声明式清单和 `.mgworld v3`；没有声明时仍只使用最粗生成 LOD。
- 当前增量缓存按 Package 指纹复用，修改 TileMap、Sprite、Texture 或传递依赖会重建拥有它的 TileWorld；逐 Chunk 编码缓存尚未实现。
- 当前源 TileMap 仍整体解析；超大型导入格式和前向解析临时索引后续按真实地图规模补充。
- 当前 WebP 内嵌归档，不单独输出松散图片；运行时 Loader 从 Chunk Payload 按需解码。
- 已有固定网格 WebP 切片导入；尚无 Tiled TMX、稀疏缺片、无限程序化地图或视觉 LOD 淡化。

## 真实地图案例与固定推进顺序

历史 ZL Editor 的 `packages/shared-public/assets/map` 提供了一份很有价值的真实验收样本：世界为
`12000×12000`，包含 `20×20` 张 `600×600` 的 `tile_{row}_{column}.webp` 详细切片，以及一张
`2040×2040` 的 `preview.webp`。旧运行时让 Preview 铺满整个世界并始终位于底层；详细 Chunk
按 Viewport 可见范围加载后覆盖其对应区域，离开保留范围后再卸载。因此加载、取消、失败或快速
移动期间都不会出现空白世界。

这个结构应被解释为“常驻全图保底层 + 按需详细层”，而不是把 401 张图片声明成普通 Texture
并随 Content Package 一次性上传。`preview.webp` 的稳定语义是 `Fallback Surface`：只提供视觉
连续性，不参与 Tile、碰撞或 Gameplay 查询。详细 WebP 也只是视觉数据；权威世界状态仍应由
LOD0 Tile/Collision 或游戏自己的空间数据提供。

该目录现在已经通过独立 `.pretiledworld.json` 描述符成为真实验收输入；文件名约定只属于离线适配器，
不会泄漏到运行时 API。推进结果如下：

1. **生成式 LOD1+（已完成）**：从权威 TileMap 和 TileSet 确定性烘焙逐 Layer、逐层级的无损 WebP
   Chunk，保留 Layer Depth、透明边缘和 Gutter；构建阶段验证像素、索引和重复构建字节一致。
2. **运行时 LOD（已完成）**：实现 `TileWorldChunkLoader`、Zoom 选择、滞回、替换完整性和旧层级保留，
   接到现有 `WorldChunkStreamer<TLease>`；Viewport 仍只提供观察范围和 Zoom。
3. **Fallback Surface（已完成）**：最粗层级继续按需驻留；可选低清全图 Preview 按 Layer 独立声明并常驻，
   当最粗 Chunk 尚未解码时按缺失世界区域裁取对应 UV，不要求 Preview 来自 Tile 烘焙。
4. **既有切片导入（已完成）**：离线适配器验证行列、尺寸、缺片、路径安全与 Preview 世界范围，
   把 `tile_{row}_{column}.webp` 规范化进同一归档索引并生成 LOD1+。运行时不扫描目录，也不解析文件名。

这样既能让新项目得到由权威内容确定性生成的标准产物，也能在边界稳定后无损接纳已有大地图。
ZL 样本已在仓库外的独立 SDK 消费项目中通过集成验收：400 个原始 Chunk 生成 6 层共 539 个
Raster Chunk，隐藏窗口 smoke 在全图视角以 LOD3 驻留 9 个 Chunk 并保持一张 Preview。仓库内自动测试
仍只使用临时目录中程序生成的几像素图片，避免把完整真实地图引入引擎仓库。

验证命令：

```powershell
dotnet run --project src/Engine.Features/TileWorlds.Tests/TileWorlds.Tests.csproj -c Release
dotnet run --project src/Engine.Features/TileWorldStreaming.Tests/TileWorldStreaming.Tests.csproj -c Release
dotnet run --project src/Engine.Tools.AssetCompiler.Tests/Engine.Tools.AssetCompiler.Tests.csproj -c Release
```

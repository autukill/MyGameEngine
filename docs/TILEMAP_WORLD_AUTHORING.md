# Tilemap / World Authoring

这一切片提供不依赖编辑器 UI 的 Tilemap 创作与运行时边界。Tile 不是 `GameInstance`：地图以稀疏 Chunk 保存格子，绘制时直接生成 `SpriteDrawCommand`，避免为大地图中的每个格子承担实例生命周期成本。

## 核心模型

- `TileSetRef`、`TileMapRef` 和 `TileId` 都是稳定逻辑引用，不携带纹理或 GPU Handle。
- `TileDefinition` 把一个非零 Tile ID 映射到 `SpriteRef + subImage`，因此同一 TileSet 可以跨 Sprite、跨纹理和跨 Atlas 页。
- `TileCell` 额外保存 `FlipX`、`FlipY` 与 90° 倍数旋转；ID `0` 固定表示空格。
- `TileMap` 包含按 Depth 稳定排序的多个 `TileLayer`。每层绑定一个 TileSet，并可声明可见性和世界偏移。
- `TileLayer` 使用默认 `32×32` 的稀疏 Chunk。负格子坐标采用数学向下取整；最后一个非空格被清除时，对应 Chunk 自动释放。

TileSet 显式使用 Sprite 帧，而不是复制纹理 UV：未来 Atlas 重新映射 Sprite 帧时，TileMap 数据和绘制 API 不需要改变。

## 代码创建

```csharp
var tileSets = new TileSetLibrary();
TileSetRef worldTiles = tileSets.Register(new TileSet(
    "world.tiles",
    new Vector2(16, 16),
    [
        new TileDefinition(new TileId(1), GameAssets.Sprites.WorldTiles, 0),
        new TileDefinition(
            new TileId(2),
            GameAssets.Sprites.WorldTiles,
            1,
            TileCollisionKind.Solid)
    ]));

var map = new TileMap("levels.one");
TileLayer ground = map.AddLayer("ground", worldTiles);
ground.SetCell(0, 0, new TileCell(new TileId(1)));
ground.SetCell(-1, 0, new TileCell(new TileId(2), TileTransform.FlipX));
```

Hosting 已在 `Default2DGameContext` 暴露 `TileSets`、`TileMaps` 和共享 `TileMapRenderer`。声明式包加载后可以直接获取：

```csharp
TileMap map = context.TileMaps.Get(GameAssets.TileMaps.LevelsOne);
```

## 可见区域绘制与多 Camera

`TileMapRenderer.Draw` 显式接收世界空间 `Bounds2D`：

```csharp
if (camera.TryGetVisibleWorldBounds(out Bounds2D visible))
{
    TileMapDrawStatistics stats = context.TileMapRenderer.Draw(
        batch,
        map,
        visible,
        worldOrigin: Vector2.Zero);
}
```

可见格子范围先换算为 Chunk 范围，只访问落入 Camera AABB 的 Chunk。一个地图可被不同 Camera 分别调用，不依赖全局 Camera，也不会跨 View 复用错误的可见性结果。返回统计包含访问/缺失 Chunk、访问格子、实际绘制和未知 Tile 数。

Tile 的几何中心按格子尺寸计算，并覆盖 Sprite 的逻辑 Origin。因此用于角色的中心原点 Sprite 也可以安全复用为左上角对齐的地图格子。

## 声明式内容

`assets.json` 可以声明 TileSet 和外部 TileMap 文档：

```json
{
  "schemaVersion": 1,
  "id": "world.assets",
  "tileSets": [{
    "name": "world.tiles",
    "tileSize": { "width": 16, "height": 16 },
    "tiles": [
      { "id": 1, "sprite": "world.sprite", "subImage": 0 },
      { "id": 2, "sprite": "world.sprite", "subImage": 1, "collision": "solid" }
    ]
  }],
  "tileMaps": [
    { "name": "levels.one", "path": "maps/level-one.tilemap.json" }
  ]
}
```

TileMap 文档按 Chunk 保存固定长度的行优先数组：

```json
{
  "schemaVersion": 1,
  "name": "levels.one",
  "chunkSize": { "width": 2, "height": 2 },
  "layers": [{
    "name": "walls",
    "tileSet": "world.tiles",
    "depth": 0,
    "chunks": [
      { "x": 0, "y": 0, "tiles": [1, 2, 0, 2] }
    ]
  }]
}
```

格子编码的低 16 位是 Tile ID，高 16 位的低四个 bit 是变换标记。作者通常写普通 ID；导入器或构建工具可生成带变换的数值。未知字段、重复 Chunk、错误数组长度、路径逃逸、包依赖闭包外的 Sprite/TileSet 以及越界 sub-image 都会在装配阶段失败。

AssetCompiler 会把 TileMap 文档安全复制到编译产物，并保留 TileSet 声明；强类型生成器新增 `GameAssets.TileSets.*` 和 `GameAssets.TileMaps.*`。卸载顺序固定为 TileMap → TileSet → Sprite → Texture。第一版明确不对 Tilemap 热重载，修改后应重启运行时。

## 静态碰撞

`TileCollisionBaker` 根据 `TileCollisionKind.Solid` 生成世界空间 AABB。它在每个 Chunk 内贪心合并连续格子，并复用 `TileCollisionBakeBuffer`：

```csharp
var collisions = new TileCollisionBakeBuffer();
new TileCollisionBaker(context.TileSets)
    .BakeLayer(map, "walls", collisions);
```

矩形不会跨 Chunk 合并，这是有意的增量边界：局部 Tile 修改只需要重建受影响 Chunk。当前输出是静态几何数据，不会隐式创建数百个 `GameInstance` Collider。

## 当前边界

- 尚无地图编辑器、Tiled 导入、自动地形、动画 Tile、Tile Entity、等距地图或流式 Chunk 驻留。
- 当前编译产物仍保存严格 JSON TileMap；版本化二进制 Chunk 编译将在真实地图规模证明 JSON 启动成本后加入。
- 碰撞烘焙只处理实心 AABB，不替代完整物理系统。
- 多 Camera 由调用方分别传入可见边界；不引入隐藏全局绘制上下文。

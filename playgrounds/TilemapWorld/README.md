# Tilemap World Playground

这个无 UI Playground 展示声明式 `TileSet/TileMap`、稀疏 Chunk、Camera 可见区域绘制和静态碰撞烘焙。

```powershell
dotnet run --project playgrounds/TilemapWorld/TilemapWorld.csproj
```

- WASD / 方向键：移动 Camera，观察 Chunk 进入和离开可见范围。
- Esc：退出。
- `--smoke`：隐藏窗口运行四个固定更新后退出。

地图源文件位于 `Assets/world.tilemap.json`，没有编辑器或运行时生成步骤；Build 会生成强类型 `GameAssets.TileSets` 和 `GameAssets.TileMaps`。

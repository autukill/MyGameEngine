# 离线 Texture Atlas 使用指南

`Engine.Features.TextureAtlas` 提供不依赖 OpenGL 的确定性 CPU 打包器；`Engine.Tools.AssetCompiler` 读取声明式 `assets.json`，生成 Atlas PNG 页面和仍可由 `ContentPackageManager` 直接加载的运行时包。

Atlas 是离线构建步骤，不在游戏启动时重新打包。

## 为什么离线构建

- 运行时不需要长期保存解码后的 CPU 像素。
- 打包结果确定，同一输入可产生字节一致的产物。
- 构建失败发生在开发或发布阶段，而不是玩家进入场景时。
- 大帧可以自动绕过 Atlas，避免被页面尺寸强行限制。
- 一个动画可以跨多个 Atlas 页面，运行时 Sprite API 不变。

## 在源清单中启用

在包顶层增加 `atlas`：

```json
{
  "schemaVersion": 1,
  "id": "characters.player",
  "dependencies": [],
  "atlas": {
    "maxPageSize": { "width": 2048, "height": 2048 },
    "padding": 1,
    "extrude": 1,
    "textures": [
      "player.idle.source",
      "player.attack.0",
      "player.attack.1"
    ]
  },
  "textures": [],
  "sprites": []
}
```

- `maxPageSize` 默认 `2048 × 2048`。
- `padding` 默认 `1`，表示 extrude 外侧保留的透明间隔。
- `extrude` 默认 `1`，表示向外复制多少像素的边缘，用于避免线性采样渗色。
- `textures` 必须列出当前包内的 Texture 名称，名称不能重复。

未列入 `atlas.textures` 的 Texture 会原样复制到编译包。

列入列表的 Texture 被视为构建输入：当其中所有被引用帧都成功进入 Atlas 后，原图片不会出现在运行时包中。因此不要再通过 `LoadedContentPackage.GetTexture()` 将这些源 Texture 当作公开运行时资源。需要直接读取的图片应留在 Atlas 列表之外。

源包仍可直接交给 `ContentPackageManager` 加载；此时顶层 `atlas` 只作为构建元数据，运行时会加载原始图片。这提供了未编译的开发回退路径。

## 执行编译器

```powershell
dotnet run --project src/Engine.Tools.AssetCompiler/Engine.Tools.AssetCompiler.csproj -- `
  Content `
  characters/player/assets.json `
  artifacts/player
```

参数依次是：

1. packages root。
2. 相对 packages root 的根清单路径。
3. 输出目录；必须不存在或为空。

成功输出示例：

```text
Compiled package: characters.player
Manifest: .../artifacts/player/assets.json
Atlas pages: 2
Packed frames: 18
Passthrough frames: 1
```

## 编译产物

```text
artifacts/player/
├── assets.json
├── atlas/
│   ├── pixel-art-0.png
│   ├── pixel-art-1.png
│   └── smooth-0.png
├── boss-large.webp
└── shared/
    ├── assets.json
    └── white.webp
```

编译后的 `assets.json` 仍使用 Content Assets schema：

- Sprite 被规范化为显式 `frames`。
- 每一帧改写为 Atlas 页 Texture 与新像素矩形。
- PixelArt 与 Smooth Texture 分页打包，避免采样状态混用。
- 未选择的 Texture 原样保留。
- 超大帧保留原 Texture 和源矩形。
- 依赖包按 packages-root 相对路径复制到产物中；依赖自身可单独执行编译。
- 编译产物不再包含顶层 `atlas`，避免运行时重复构建。

运行方式与普通包完全相同：

```csharp
using var package = manager.Load("assets.json");
SpriteRef attack = package.GetSprite("player.attack");
```

## 多页和大帧

每个规范化帧的占用尺寸为：

```text
frame size + 2 × (padding + extrude)
```

如果单帧占用尺寸超过 `maxPageSize`，编译器将其标记为 passthrough，而不是缩放、裁剪或报错。其他帧继续正常打包。

同一个 Sprite 的不同帧可以位于：

- 同一 Atlas 页。
- 不同 Atlas 页。
- Atlas 页与独立 Texture 的混合组合。

这些情况都由现有逐帧 `TextureRef + PixelRectI` 边界表达，不改变 `SpriteRef`、`ImageIndex`、动画循环、GameInstance 或 `DrawSprite` API。

## 确定性与算法

第一版使用无旋转 Shelf Packing：

1. 按帧高度降序。
2. 再按宽度降序。
3. 最后按稳定帧键进行 Ordinal 排序。
4. 依次尝试已有页面与 Shelf，否则建立新页面。
5. 页面输出裁切到实际使用区域。

输入顺序不会改变布局或 PNG 内容，便于缓存、版本控制和回归测试。

## 当前限制

- 不支持旋转打包。
- 不支持 trim、逐帧逻辑偏移或透明边界裁切。
- 不进行相同像素内容的哈希去重；相同 Texture/矩形引用会复用，同内容但不同 Texture 名称仍视为不同来源。
- 产物页面使用无损 PNG；暂无自定义原始 RGBA 容器。
- 没有增量构建缓存，每次编译会重新解码参与包图的图片。
- 输出目录必须为空或不存在，避免覆盖未知文件。
- 依赖包会原样复制；若依赖也需要 Atlas 优化，应分别编译并按相同 packages-root 布局组织产物。

## 可运行验证

- `Engine.Features.TextureAtlas.Tests`：排布、像素复制、padding、extrude、多页、大帧旁路和确定性。
- `Engine.Tools.AssetCompiler.Tests`：真实 PNG、编译产物字节一致性、依赖复制、跨页 Sprite 与 ContentPackageManager 加载。
- `Sprites.VisualTests/AssetsCompiled`：由 WebP Grid 与两张独立 WebP 帧生成的一页 Atlas，图形测试直接加载编译产物。

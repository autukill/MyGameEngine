# Content Assets 使用指南

`Engine.Features.ContentAssets` 使用单一、版本化的 `assets.json` 声明 Texture、Sprite 和包依赖。它位于 `TextureAssets` 与 `Sprites` 之上，负责把图片同步加载到 GPU，再将这些 Texture 装配为逻辑 Sprite。

当前清单版本为 `schemaVersion: 1`。

## 快速开始

准备目录：

```text
Assets/
├── assets.json
├── player-atlas.webp
├── attack-0.webp
└── attack-1.webp
```

加载包：

```csharp
using var textures = new TextureLibrary(gl);
var sprites = new SpriteLibrary(textures);
using var packages = new ContentPackageManager(textures, sprites, assetsRoot);
using var gameAssets = packages.Load("assets.json");

SpriteRef idle = gameAssets.GetSprite("player.idle");
SpriteRef attack = gameAssets.GetSprite("player.attack");
TextureRef white = gameAssets.GetTexture("common.white");
```

将同一个 `SpriteLibrary` 注入绘制和场景：

```csharp
batch.SpriteResolver = sprites;
scene.SetSprites(sprites);
```

实例只保存逻辑 Sprite：

```csharp
var player = scene.Add(new PlayerInstance
{
    Sprite = attack,
    ImageSpeed = 1f
});
```

`GameInstance.DrawSelf()` 会使用 `ImageIndex`、Sprite 原点、`Transform.Scale`、`Transform.Rotation` 和 `Color`。

## 清单结构

```json
{
  "schemaVersion": 1,
  "id": "game.player",
  "dependencies": [],
  "textures": [],
  "sprites": []
}
```

- `schemaVersion`：必填，目前只能是 `1`。
- `id`：必填、区分大小写的包 ID。
- `dependencies`：依赖包列表，可为空。
- `textures`：本包拥有的 Texture 定义，可为空。
- `sprites`：本包拥有的 Sprite 定义，可为空。
- `atlas`：可选的离线构建配置；运行时加载源包时不会自动执行打包。
- 一个包至少需要声明一个 Texture 或 Sprite。
- 未知 JSON 字段会被拒绝，以便尽早发现拼写错误。

资源名称采用区分大小写的全局名称。建议使用包前缀，例如 `player.idle`、`boss.attack.0`。

## Texture 定义

```json
{
  "name": "player.atlas",
  "path": "player-atlas.webp",
  "sampling": "pixelArt"
}
```

- `name`、`path` 必填。
- `sampling` 可省略，默认为 `smooth`。
- 像素风可使用 `pixelArt`、`pixel-art` 或 `nearest`。
- 当前默认解码器支持 PNG 和静态 WebP。
- 图片路径相对当前清单所在目录解析，不能使用绝对路径，也不能通过 `..` 离开该目录。

## 单图片 Sprite

整张图片作为一帧：

```json
{
  "name": "player.portrait",
  "layout": "single",
  "texture": "player.portrait.texture",
  "origin": { "x": 64, "y": 128 }
}
```

只使用图片的一部分：

```json
{
  "name": "player.portrait",
  "layout": "single",
  "texture": "player.portrait.texture",
  "source": { "x": 32, "y": 16, "width": 128, "height": 256 },
  "origin": { "x": 64, "y": 224 }
}
```

省略 `source` 时使用对应 Texture 整图。

## 规则图集 Sprite

`grid` 从纹理左上角开始，按行优先连续切帧：

```json
{
  "name": "player.idle",
  "layout": "grid",
  "texture": "player.atlas",
  "frameSize": { "width": 64, "height": 64 },
  "frameCount": 8,
  "origin": { "x": 32, "y": 56 },
  "framesPerSecond": 10
}
```

v1 暂不支持 Grid margin、spacing 或旋转帧。

## 多图片、多纹理 Sprite

`frames` 布局允许每一帧引用不同 Texture，帧数和纹理数没有固定的小上限：

```json
{
  "schemaVersion": 1,
  "id": "boss.assets",
  "dependencies": [],
  "textures": [
    { "name": "boss.attack.0", "path": "attack-0.webp", "sampling": "pixelArt" },
    { "name": "boss.attack.1", "path": "attack-1.webp", "sampling": "pixelArt" },
    { "name": "boss.attack.2", "path": "attack-2.webp", "sampling": "pixelArt" }
  ],
  "sprites": [
    {
      "name": "boss.attack",
      "layout": "frames",
      "frames": [
        { "texture": "boss.attack.0" },
        { "texture": "boss.attack.1" },
        {
          "texture": "boss.attack.2",
          "source": { "x": 32, "y": 16, "width": 512, "height": 512 }
        }
      ],
      "size": { "width": 512, "height": 512 },
      "origin": { "x": 256, "y": 400 },
      "framesPerSecond": 8
    }
  ]
}
```

运行时每帧独立保存 `TextureRef` 和该纹理内的 UV。因此动画可以跨两张、三张或更多图片，`ImageIndex` 的正向循环、反向循环和绘制方式不会变化。

当多数帧使用同一个 Texture 时，可以在 Sprite 顶层声明默认值，再按帧覆盖：

```json
{
  "name": "boss.cast",
  "layout": "frames",
  "texture": "boss.cast.atlas",
  "frames": [
    { "source": { "x": 0, "y": 0, "width": 256, "height": 256 } },
    { "source": { "x": 256, "y": 0, "width": 256, "height": 256 } },
    { "texture": "boss.cast.large-final" }
  ],
  "origin": { "x": 128, "y": 220 },
  "framesPerSecond": 12
}
```

### 多图片帧固定规则

- 每一帧必须显式提供 `texture`，或继承 Sprite 顶层的默认 `texture`。
- 每帧 `source` 可省略；省略时使用对应 Texture 整图。
- 所有帧最终得到的源矩形宽高必须一致。
- 所有帧共享逻辑尺寸、原点和基础 FPS。
- `size` 可省略；默认采用第一帧源矩形尺寸。
- v1 不支持逐帧逻辑偏移、trim 补偿或不同源尺寸的隐式拉伸。

## 逻辑尺寸、源尺寸与原点

源矩形决定从 Texture 读取哪些像素；`size` 决定 Sprite 在未缩放时的逻辑绘制尺寸。两者可以不同：

```json
{
  "source": { "x": 0, "y": 0, "width": 1024, "height": 1024 },
  "size": { "width": 256, "height": 256 },
  "origin": { "x": 128, "y": 220 }
}
```

绘制位置对应 Sprite 原点。原点不要求位于中心，也可以位于 Sprite 范围之外。坐标、尺寸和 FPS 必须是有限数值；尺寸必须为正，FPS 必须非负。

## 包依赖

依赖路径相对 `ContentPackageManager` 的 `packagesRoot`：

```json
{
  "schemaVersion": 1,
  "id": "level.one",
  "dependencies": [
    {
      "id": "shared.primitives",
      "manifest": "shared/assets.json"
    }
  ],
  "textures": [],
  "sprites": [
    {
      "name": "level.one.marker",
      "layout": "single",
      "texture": "shared.white",
      "origin": { "x": 0, "y": 0 }
    }
  ]
}
```

Sprite 只能引用本包或传递依赖包中的 Texture。仅仅在 `TextureLibrary` 中存在同名 Texture 不会自动赋予包访问权限。

Manager 在修改 GPU 状态之前解析完整依赖图，并拒绝：

- 循环依赖。
- 依赖声明 ID 与目标清单 ID 不一致。
- 同一个包 ID 指向不同清单。
- 全局 Texture 或 Sprite 名称冲突。
- Manifest 或图片路径逃逸安全根目录。
- Sprite 引用依赖闭包以外的 Texture。

## 生命周期与所有权

`TextureLibrary` 拥有 GPU Texture；`SpriteLibrary` 只保存逻辑帧映射；`LoadedContentPackage` 是外部租约。

```text
Load root package
  → 依赖按拓扑顺序取得持有
  → Texture 同步解码并上传
  → Sprite 校验并注册

Dispose root lease
  → Sprite 卸载
  → Texture 卸载
  → 依赖持有释放
```

同一清单多次 `Load` 只装配一次。多个根包共享同一依赖时，只有最后一个引用释放后才卸载依赖。`LoadedContentPackage.Dispose()` 是幂等的。

推荐关闭顺序：

```csharp
scene.End();
package.Dispose();
packages.Dispose();
textures.Dispose();
batch.Dispose();
shader.Dispose();
```

`ContentPackageManager.Dispose()` 不会替调用者释放 `TextureLibrary`。

## 失败与回滚

图片解码、GPU 上传、帧范围或 Sprite 注册中的任一步失败，Manager 都会恢复调用前的包引用计数，并只移除本次新增的 Sprite 和 Texture。预先存在且不属于该包的资源不会被删除。

显存不足、图片超过解码器/GPU 尺寸上限或 WebP/PNG 数据损坏都会使整个包加载失败；v1 不提供部分成功状态。

## 性能边界

运行时直接加载源包时，会同步解码并上传清单内全部图片，不提供：

- 流式驻留或帧预取。
- 显存预算、LRU 或自动卸载单帧。
- 解码后 CPU 像素缓存。
- 运行时自动 Atlas 打包。

多图片长动画在功能上可以直接使用，但 SpriteBatch 遇到 Texture 变化时需要 Flush。大量实例同时播放跨纹理动画时，纹理切换可能成为主要成本。

离线 `Engine.Tools.AssetCompiler` 已能消费规范化的逐帧来源：小帧重映射到一个或多个 Atlas 页，放不下的大帧保持独立 Texture。这个过程不会改变 `SpriteRef`、`GameInstance`、`ImageIndex` 或 `DrawSprite` API。配置与命令行说明见 [离线 Texture Atlas 使用指南](TEXTURE_ATLAS.md)。

## 可运行示例

- `src/MyGame.Runner/Assets/assets.json`：单图片 Sprite、逻辑尺寸与中心原点。
- `src/Engine.Features/Sprites.VisualTests/Assets/assets.json`：规则 WebP 图集、两张独立 WebP 帧、中心与偏置原点。
- `src/Engine.Features/ContentAssets.Tests`：真实 WebP、多纹理帧、依赖引用计数、安全路径和失败回滚。

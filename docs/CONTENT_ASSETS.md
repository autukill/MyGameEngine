# Content Assets 使用指南

对于内容较大的多 Scene 游戏，可以只配置包目录，并把租约声明在 Scene 上：

```csharp
.UseDefault2DRenderer(renderer => renderer.UseContentCatalog())
.AddScene(GameScenes.Home, GameAssets.Packages.GameHome, ConfigureHome)
.AddScene(GameScenes.World, GameAssets.Packages.GameWorld, ConfigureWorld)
```

这与常驻的 `UseContent(GameAssets.Packages.Root)` 是两种显式模式。前者在安全 Scene 切换边界加载目标包并释放旧包；聚合根仍参与离线编译和强类型代码生成，但不会自动常驻。所有权、失败顺序和限制见 [Scene 级 Content 生命周期](SCENE_CONTENT_LIFECYCLE.md)。

`Engine.Features.ContentAssets` 使用单一、版本化的 `assets.json` 声明 Texture、Sprite、Animation、Audio Clip 和包依赖。它负责把图片同步加载到 GPU，把 Texture 装配为逻辑 Sprite，把 Sprite 装配为命名 Animation Clip，并把短 WAV 解码或把长 OGG 注册为流式逻辑 Audio Clip。

当前清单版本为 `schemaVersion: 1`。

## 编译期与运行时边界

Content 系统刻意复用同一份运行时 Manifest Schema，但把“准备内容”和“装配设备资源”分成两个阶段：

```text
Authoring Assets/
  assets.json + PNG/WebP/WAV/TileMap/TileWorld source
        ↓ Engine.Tools.AssetCompiler（Build/Publish）
CompiledAssets/
  标准 assets.json + Atlas 页/旁路图片/WAV/OGG/TileMap/.mgworld + 修订元数据
        ↓ ContentPackageManager（游戏启动/加载包）
Texture/Sprite/Animation/Audio/TileSet/TileMap/TileWorld Library
        ↓ 逻辑 Ref
Gameplay 与 Renderer
```

- AssetCompiler 负责安全路径、完整依赖图、增量指纹、离线 Atlas、输出事务和 `GameAssets` 强类型引用。
- `ContentPackageManager` 负责读取编译后的标准包、设备资源注册、依赖可见性、租约计数、失败回滚和卸载。
- `GameAssets` 只包含 `TextureRef`、`SpriteRef` 等逻辑名称，不包含像素、PCM、UV 或 GPU Handle。
- 运行时也可以直接读取源包，便于测试和工具；正式 Build 默认让 Runtime 只消费 `AssetsCompiled`，避免游戏进程执行离线 Atlas 或修改源目录。

编译器不会发明另一套私有二进制 Manifest。启用 Atlas 时，它把 `single/grid/frames` 统一规范化为显式逐帧来源，按包配置生成 PNG 或无损 WebP 页面，并重写为仍可由 `AssetPackageManifestParser` 读取的 `layout: "frames"`。因此运行时加载、热重载和测试共用相同 Schema 与验证模型。

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
var animations = new AnimationLibrary();
var audio = new AudioLibrary();
using var packages = new ContentPackageManager(textures, sprites, animations, audio, assetsRoot);
using var gameAssets = packages.Load("assets.json");

SpriteRef idle = gameAssets.GetSprite("player.idle");
SpriteRef attack = gameAssets.GetSprite("player.attack");
TextureRef white = gameAssets.GetTexture("common.white");
AnimationClipRef run = gameAssets.GetAnimation("player.run");
AudioClipRef shot = gameAssets.GetAudioClip("player.shot");
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
  "sprites": [],
  "animations": [],
  "audioClips": [],
  "tileSets": [],
  "tileMaps": []
}
```

- `schemaVersion`：必填，目前只能是 `1`。
- `id`：必填、区分大小写的包 ID。
- `dependencies`：依赖包列表，可为空。
- `textures`：本包拥有的 Texture 定义，可为空。
- `sprites`：本包拥有的 Sprite 定义，可为空。
- `animations`：本包拥有的 Animation Clip 定义，可为空。
- `audioClips`：本包拥有的预加载短 WAV 定义，可为空。
- `tileSets`：本包拥有的 Tile ID → Sprite/sub-image/collision 定义，可为空。
- `tileMaps`：本包拥有的外部稀疏 Chunk 地图定义，可为空。
- `atlas`：可选的离线构建配置；运行时加载源包时不会自动执行打包。
- 一个包至少需要声明一个 Texture、Sprite、Animation、Audio Clip、TileSet、TileMap 或 TileWorld。
- 未知 JSON 字段会被拒绝，以便尽早发现拼写错误。

资源名称采用区分大小写的全局名称。建议使用包前缀，例如 `player.idle`、`boss.attack.0`。

### Parser 实现原则

`AssetPackageManifestParser` 使用 `System.Text.Json` Source Generation 反序列化 DTO，并启用 `UnmappedMemberHandling.Disallow`。解析分两步：

1. JSON 形状、必填字段、枚举、有限数值、重复名称和局部范围被转换为不可变 Domain 定义。
2. 需要文件系统或跨包信息的规则留给 Compiler/PackageManager，例如路径是否存在、依赖闭包可见性、图片真实尺寸与 Sprite 帧范围。

这样既避免 Parser 依赖 GPU/设备，也保证编译期和运行时不会各自维护一套互相漂移的 JSON 规则。JSON 属性名按 Web 默认规则大小写不敏感；包 ID 与所有逻辑资源名称的比较使用 `StringComparer.Ordinal`，因此资源名称区分大小写。

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

## Audio Clip 定义

```json
{
  "name": "player.shot",
  "path": "audio/player-shot.wav",
  "streaming": false
}
```

- `name`、`path` 必填，名称与其他包中的 Audio Clip 全局区分大小写且不能冲突。
- `streaming: false` 只接受预加载的 PCM8/PCM16、Mono/Stereo WAV。
- `streaming: true` 只接受 Mono/Stereo OGG Vorbis；构建期验证完整 Header 与元数据，运行时包装配不预解码整首 PCM。
- Audio 路径与图片一样相对所属包目录解析，不能逃逸安全根。
- `LoadedContentPackage.GetAudioClip` 返回不含设备句柄或解码器的 `AudioClipRef`；播放由 Hosting 的 `AudioRuntime`/`SceneAudio` 完成。
- 构建管线会复制音频文件、纳入增量指纹并生成 `GameAssets.AudioClips` 强类型引用。
- 当前 Content Hot Reload 不替换 Audio Clip；修改音频文件后需要重启应用。

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

## Animation 定义

Animation 将一个逻辑 Sprite 的 sub-image 重组为可播放 Clip：

```json
{
  "name": "boss.attack.heavy",
  "sprite": "boss.attack",
  "frames": [0, 1, 2, 1],
  "framesPerSecond": 10,
  "loop": "pingPong",
  "markers": [
    { "frame": 2, "event": "boss.attack.heavy.hit" }
  ]
}
```

- `sprite` 必须来自本包或传递依赖包；仅存在于全局 `SpriteLibrary` 不会获得访问权。
- `frames` 必须非空，且每个 sub-image 都在目标 Sprite 范围内。
- `framesPerSecond` 必须有限且大于 `0`。
- `loop` 支持 `once`、`loop`、`pingPong`，默认 `loop`。
- Marker 的 `frame` 使用 Clip 内部帧索引，`event` 是区分大小写的稳定逻辑名称。

Animation 不直接保存 Texture 或 UV。多图片 Sprite、Atlas 跨页和大帧旁路都由 Sprite 层解析，因此不会改变 Clip API。

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

Sprite 只能引用本包或传递依赖包中的 Texture；Animation 同样只能引用依赖闭包中的 Sprite。仅仅在全局 Library 中存在同名资源不会自动赋予包访问权限。

只声明 `dependencies`、不包含本地资源的聚合包是合法的，适合把顶层 `assets.json` 保持为简短、显式的构建目录。完全没有依赖和本地资源的空包仍会被拒绝。

聚合根本身只定义包依赖图，不决定运行时驻留策略：

- `UseContent(GameAssets.Packages.Root)` 会取得聚合根租约，因此其完整依赖闭包常驻到应用关闭。
- `UseContentCatalog()` 不加载聚合根；只有绑定在当前 Scene 上的 `ContentPackageRef` 会取得运行时租约。

因此，把清单拆成子包只是建立可独立加载的边界；是否真正按 Scene 加载，取决于 Hosting 的装配方式。相关术语和所有权协议见 [Scene 级 Content Package 生命周期](SCENE_CONTENT_LIFECYCLE.md)。

Manager 在修改 GPU 状态之前解析完整依赖图，并拒绝：

- 循环依赖。
- 依赖声明 ID 与目标清单 ID 不一致。
- 同一个包 ID 指向不同清单。
- 全局 Texture、Sprite 或 Animation 名称冲突。
- Manifest 或图片路径逃逸安全根目录。
- Sprite 引用依赖闭包以外的 Texture，或 Animation 引用闭包以外的 Sprite。

## Runtime 装配算法

`ContentPackageManager.Load` 先执行只读 Preflight，再进入有副作用的 Acquire：

```text
Resolve manifest under packagesRoot
  → DFS 读取依赖图并校验 expected ID / 循环 / 同 ID 多路径
  → 全图检查文件、全局名称冲突和依赖闭包可见性
  → 递归 Acquire 依赖
  → Texture 解码并上传
  → Sprite 注册逐帧 TextureRef + PixelRect
  → Animation 注册 Sprite sub-image 序列
  → TileSet / TileMap / TileWorld 索引注册
  → WAV 同步解码，或为 OGG 注册流式 Factory
  → 写入 PackageState 并返回 LoadedContentPackage 租约
```

关键实现决策：

- 依赖 Manifest 始终相对 Manager 的 `packagesRoot`；Texture、WAV、TileMap 和 `.mgworld` 相对各自所属包目录。
- 路径经过 `GetFullPath + GetRelativePath` 再确认仍位于安全根内；绝对路径和 `..` 逃逸都会在打开文件前失败。
- `PackageState` 只记录该包自己注册的 Ref 和直接依赖状态；`GetTexture/GetSprite/...` 递归查询自身与依赖，不会因为某个名称恰好存在于全局 Library 就越权可见。
- Sprite/Animation/TileSet 的引用在注册时再次用真实 Texture/Sprite 元数据验证，因此编译器输出损坏或被手工修改也不能绕过运行时边界。
- 同一路径已经加载时只增加引用计数，不重复解码或上传；同一个包 ID 若指向另一 Manifest 会被拒绝。

## 生命周期与所有权

`TextureLibrary` 拥有 GPU Texture；`SpriteLibrary` 保存逻辑帧映射；`AnimationLibrary` 和 `AudioLibrary` 保存逻辑 Clip；`TileSetLibrary/TileMapLibrary` 保存整体驻留世界资源；`TileWorldLibrary` 保存借用归档的位置和小型索引元数据。`.mgworld` v3 同时容纳权威 LOD0 Tile/碰撞、逐 Layer LOD1+ 无损 WebP 和可选的逐 Layer 全图 Fallback Surface，但当前 Package Load 只打开归档，不会把这些视觉资源立即上传到 GPU；`LoadedContentPackage` 是外部租约。大型地图格式见 [TileWorld 离线切片编译器](TILE_WORLD_OFFLINE_COMPILER.md)。

```text
Load root package
  → 依赖按拓扑顺序取得持有
  → Texture 同步解码并上传
  → Sprite 校验并注册
  → Animation 校验并注册

Dispose root lease
  → TileWorld / TileMap / TileSet 卸载
  → Audio / Animation 卸载
  → Sprite 卸载
  → Texture 卸载并释放 GPU Handle
  → 依赖持有释放
```

同一清单多次 `Load` 只装配一次。当前 `PackageState.ReferenceCount` 同时统计外部 `LoadedContentPackage` 租约与上游包的依赖持有；多个根包共享同一依赖时，只有最后一个引用释放后才卸载依赖。正常 Release 按资源依赖的逆序移除，最后再递归释放依赖；`LoadedContentPackage.Dispose()` 是幂等的。

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

图片解码、GPU 上传、Sprite 帧范围、Animation sub-image、WAV/OGG 验证、Tile/TileWorld 资源或注册中的任一步失败，Manager 都会逆序移除本次新增的 TileWorld、TileMap、TileSet、Audio、Animation、Sprite 和 Texture，再释放本次取得的依赖持有。预先存在且不属于该包的资源不会被删除；已经缓存的依赖只恢复引用计数，不会被误卸载。

显存不足、图片超过解码器/GPU 尺寸上限或 WebP/PNG 数据损坏都会使整个包加载失败；v1 不提供部分成功状态。

开发运行可通过 Hosting 的 `EnableContentHotReload` 消费 AssetCompiler 完整修订。后台准备不会改变当前资源；Texture、Sprite、Animation 与包索引只在 Step 和 Draw 之间作为一个事务切换。使用方式、结构化失败诊断和依赖拓扑限制见 [Content 包开发期热重载](CONTENT_HOT_RELOAD.md)。

## 性能边界

运行时直接加载源包时，会同步解码并上传清单内全部图片，不提供：

- 流式驻留或帧预取。
- 显存预算、LRU 或自动卸载单帧。
- 解码后 CPU 像素缓存。
- 运行时自动 Atlas 打包。

多图片长动画在功能上可以直接使用，但 SpriteBatch 遇到 Texture 变化时需要 Flush。大量实例同时播放跨纹理动画时，纹理切换可能成为主要成本。

离线 `Engine.Tools.AssetCompiler` 已能消费规范化的逐帧来源：小帧重映射到一个或多个 Atlas 页，放不下的大帧保持独立 Texture。这个过程不会改变 `SpriteRef`、`GameInstance`、`ImageIndex` 或 `DrawSprite` API。配置与命令行说明见 [离线 Texture Atlas 使用指南](TEXTURE_ATLAS.md)。

编译器对 Atlas 的处理顺序是：按 Texture/源矩形去重帧 → 按采样模式分组 → 解码并裁剪 RGBA8 → 构建确定性多页 Atlas → 为成功放置的帧生成页 Texture 与新 PixelRect → 对过大帧保留原 Texture → 输出标准运行时 Manifest。Atlas 页使用内部 `__atlas.*` 逻辑名，强类型引用生成器不会暴露这些实现资源。被 Atlas 完全取代的源 Texture 也属于构建输入而非运行时 API；作者声明的 Sprite、Animation 等稳定逻辑资源仍保持原名。

Runner 与 VisualTests 已接入共享 MSBuild Target：Build、Run 和 Publish 会基于内容指纹自动生成 `obj/.../CompiledAssets`，运行时只读取复制到输出目录的标准包。完整构建阶段和属性说明见 [`GameEngine.Content.targets` 解读](GAMEENGINE_CONTENT_TARGETS.md)。

## 可运行示例

- `src/MyGame.Runner/Assets/assets.json`：单图片 Sprite、逻辑尺寸与中心原点。
- `src/Engine.Features/Sprites.VisualTests/Assets/assets.json`：规则 WebP 图集、两张独立 WebP 帧、中心与偏置原点。
- `src/Engine.Features/ContentAssets.Tests`：真实 WebP、多纹理帧、依赖引用计数、安全路径、失败回滚与编译修订替换。

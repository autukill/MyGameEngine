# 离线 Texture Atlas 使用指南

`Engine.Features.TextureAtlas` 提供不依赖 OpenGL 的确定性 CPU 打包器；`Engine.Tools.AssetCompiler` 读取声明式 `assets.json`，生成 PNG 或无损 WebP Atlas 页面和仍可由 `ContentPackageManager` 直接加载的运行时包。

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
    "pageEncoding": "webpLossless",
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
- `pageEncoding` 可为 `png` 或 `webpLossless`，省略时为 `png`，因此旧清单和产物格式保持兼容。
- `textures` 必须列出当前包内的 Texture 名称，名称不能重复。

`webpLossless` 使用 libwebp 的最高 lossless preset，并启用 exact 模式；完整 RGBA 会被保留，包括完全透明像素下的隐藏 RGB。WebP 只减少磁盘和分发体积，解码上传后的 RGBA8 显存与 PNG 相同。

未列入 `atlas.textures` 的 Texture 会原样复制到编译包。

列入列表的 Texture 被视为构建输入：当其中所有被引用帧都成功进入 Atlas 后，原图片不会出现在运行时包中。因此不要再通过 `LoadedContentPackage.GetTexture()` 将这些源 Texture 当作公开运行时资源。需要直接读取的图片应留在 Atlas 列表之外。

源包仍可直接交给 `ContentPackageManager` 加载；此时顶层 `atlas` 只作为构建元数据，运行时会加载原始图片。这提供了未编译的开发回退路径。

## 执行编译器

```powershell
dotnet run --project src/Engine.Tools.AssetCompiler/Engine.Tools.AssetCompiler.csproj -- `
  --incremental `
  Content `
  characters/player/assets.json `
  artifacts/player
```

参数依次是：

1. packages root。
2. 相对 packages root 的根清单路径。
3. 输出目录。首次构建必须不存在或为空；后续只允许更新带编译器所有权标记的目录。

模式：

- `--incremental`：默认模式；输入与输出均未变化时跳过，变化时只重建受影响包及其上游。
- `--rebuild`：忽略缓存，强制重新生成完整依赖图。
- `--check`：不写文件；产物有效时退出码为 `0`，缺失或过期时为 `3`。

成功输出示例：

```text
Build status: Built
Root package: characters.player
Manifest: .../artifacts/player/assets.json
Packages: 3
Built packages: 1
Reused packages: 2
Atlas pages: 2
Packed frames: 18
Passthrough frames: 1
```

## 编译产物

```text
artifacts/player/
├── .mygame-assets.json
├── assets.json
├── atlas/
│   ├── pixel-art-0.webp
│   ├── pixel-art-1.webp
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
- 完整依赖图按 packages-root 相对路径输出；含 Atlas 配置的依赖也会编译，无配置依赖执行确定性复制。
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

输入顺序不会改变布局或编码后的页面内容，便于缓存、版本控制和回归测试。

## 增量缓存与安全替换

`.mygame-assets.json` 保存编译器版本、根包指纹、每个包的输入指纹，以及全部输出文件 SHA-256。包指纹包含自身 Manifest、图片字节和传递依赖指纹，因此：

- 修改根包图片只重建根包。
- 修改共享依赖会重建该依赖及引用它的上游包。
- 无关依赖直接复用上一份已验证产物。
- 输出文件被手动修改或缺失时，相应包会重新生成。

构建始终先写入同卷临时目录，校验完成后再替换正式目录。解码、Atlas 或写入失败时，上一份有效产物保持不变。非空且没有正确所有权元数据的目录永远不会被覆盖。

## MSBuild、Run 与 Publish

仓库提供 [GameEngine.Content.targets](../build/GameEngine.Content.targets)。项目通过属性选择源包：

```xml
<PropertyGroup>
  <GameEngineContentPackagesRoot>$(MSBuildProjectDirectory)\Assets</GameEngineContentPackagesRoot>
  <GameEngineContentManifest>assets.json</GameEngineContentManifest>
</PropertyGroup>

<ProjectReference Include="..\Engine.Tools.AssetCompiler\Engine.Tools.AssetCompiler.csproj"
                  ReferenceOutputAssembly="false" />
<Import Project="..\..\build\GameEngine.Content.targets" />
```

Target 会在编译前增量生成到：

```text
obj/<Configuration>/<TargetFramework>/CompiledAssets/
```

随后把运行时文件复制到 `bin/.../AssetsCompiled`，并加入 Publish 文件列表。`.mygame-assets.json` 只留在 `obj`，不会进入游戏发布目录。

Target 的属性、执行顺序、输出搬运、Publish 接入和排障说明见 [`GameEngine.Content.targets` 解读](GAMEENGINE_CONTENT_TARGETS.md)。

## 当前限制

- 不支持旋转打包。
- 不支持 trim、逐帧逻辑偏移或透明边界裁切。
- 不进行相同像素内容的哈希去重；相同 Texture/矩形引用会复用，同内容但不同 Texture 名称仍视为不同来源。
- 产物页面支持默认 PNG 和显式 `webpLossless`；暂无自定义原始 RGBA 容器。
- 编译器版本目前是显式常量；改变输出算法时必须同步提升版本以使旧缓存失效。
- 尚未发布为独立 dotnet tool/NuGet 构建包，当前 MSBuild Target 使用仓库内项目引用。

## 可运行验证

- `Engine.Features.TextureAtlas.Tests`：排布、像素复制、padding、extrude、多页、大帧旁路和确定性。
- `Engine.Tools.AssetCompiler.Tests`：真实 PNG、exact WebP、包级增量、check 模式、失败保护、依赖聚合根、跨页 Sprite 与运行时加载。
- `Sprites.VisualTests`：Build 自动把 WebP Grid 与两张独立 WebP 帧编译到 `obj`，图形测试加载生成的一页 Atlas。

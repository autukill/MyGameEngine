# 强类型 Content 引用

内容构建会在编译游戏代码之前，从编译后的运行时 Manifest 依赖图生成 C# 逻辑引用。游戏代码不再重复书写 Sprite、Texture 或包路径字符串：

```csharp
using MyGame.Runner.Content;

renderer.UseContent(GameAssets.Packages.Root);
var sprite = GameAssets.Sprites.RunnerOrbiting;
var texture = GameAssets.Textures.RunnerWhite;
```

这些成员分别是 `ContentPackageRef`、`SpriteRef` 和 `TextureRef`，只保存稳定名称或包标识，不包含 GPU 句柄，也不拥有运行时资源。

## 生成时序

`GameEngine.Content.targets` 在 `CoreCompile` 前依次执行：

1. AssetCompiler 增量编译源内容包到 `obj/<Configuration>/<TargetFramework>/CompiledAssets`。
2. 引用生成器读取编译后的根 Manifest 和完整传递依赖图。
3. 生成 `obj/<Configuration>/<TargetFramework>/GameEngine.Content.g.cs`。
4. 将生成文件加入本次 C# 编译，再把运行时内容复制到 `bin/.../AssetsCompiled`。

生成器每次都会快速检查输出内容，但只有文本变化时才替换文件，因此缓存命中不会改变时间戳。`.g.cs` 不复制到 `AssetsCompiled`，也不作为独立文件进入 Publish。

生成目标必须使用 `.cs` 扩展名并位于编译包根目录之外，防止高级路径覆盖误伤 Manifest 或运行时图片。

## 为什么读取编译产物

源 Manifest 中被 Atlas 完全收纳的 Texture 在运行时已经不存在；Atlas 页的 `__atlas.*` Texture 又是编译器内部实现。如果直接从源清单生成，两类名称都会形成错误或不稳定的公开 API。

因此生成规则固定为：

- Sprite 使用编译后仍保持稳定的逻辑名称。
- 保留的大帧、Atlas 旁路帧和普通 Texture 生成 `TextureRef`。
- 已被 Atlas 完全吞并的源 Texture 不生成成员。
- `__atlas.*` 内部页不生成成员。
- 根包生成 `Packages.Root`；传递依赖包按 ID 生成具名成员。

Atlas 排布、跨页或未来重打包不会改变 Sprite 引用和绘制 API。

## 标识符规则

逻辑名称中的点、横线、空格等分隔符会转换为 PascalCase：

| 逻辑名称 | 生成成员 |
| --- | --- |
| `player.idle` | `PlayerIdle` |
| `world-tiles` | `WorldTiles` |
| `2d.background` | `_2dBackground` |

名称大小写敏感。若两个名称都映射到同一 C# 标识符，例如 `player-idle` 与 `player.idle`，构建会失败并同时报告两个原名称；生成器不会静默添加序号。无法表示为标识符的名称、非法命名空间和 C# 保留字配置同样会在 C# 编译前失败。

## MSBuild 配置

```xml
<PropertyGroup>
  <GameEngineContentPackagesRoot>$(MSBuildProjectDirectory)\Assets</GameEngineContentPackagesRoot>
  <GameEngineContentManifest>assets.json</GameEngineContentManifest>
  <GameEngineContentGeneratedNamespace>MyGame.Content</GameEngineContentGeneratedNamespace>
  <GameEngineContentGeneratedClass>GameAssets</GameEngineContentGeneratedClass>
</PropertyGroup>
```

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `GameEngineContentGenerateReferences` | `true` | 是否生成并编译强类型引用。纯内容构建项目可设为 `false`。 |
| `GameEngineContentGeneratedNamespace` | `$(RootNamespace).Content` | 生成类型所在命名空间。 |
| `GameEngineContentGeneratedClass` | `GameAssets` | 项目级根容器名称。 |
| `GameEngineContentGeneratedFile` | `obj/<Configuration>/<TargetFramework>/GameEngine.Content.g.cs` | 高级输出覆盖入口。 |

项目级 `GameAssets` 根容器避免引擎或第三方库中的通用 `Sprites`、`Textures` 类型发生全局冲突。

## 包 ID 校验

`ContentPackageManager.Load(ContentPackageRef)` 会先校验引用中的预期包 ID 与 Manifest 实际 ID。路径误配在任何 Texture 解码或 GPU 上传前失败；缓存包也会重复检查 ID。旧的 `Load(string)` 与 Hosting 的字符串 `UseContent` 仍保留，便于动态内容路径和渐进迁移。

## 当前边界

- 生成内容只包含逻辑引用，不生成资源元数据常量、动画枚举或实例类。
- 一个消费项目当前配置一个根 Manifest 和一个 `GameAssets` 根容器。
- 资源改名是编译期 API 变更；编译器不会保留旧名称别名。
- 运行时仍由 `LoadedContentPackage`、`TextureLibrary` 和 `SpriteLibrary` 管理加载、解析与释放。

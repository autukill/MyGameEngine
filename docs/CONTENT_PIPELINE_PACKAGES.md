# 可分发内容工具链

MyGameEngine 内容工具链提供两个同版本包：

- `MyGameEngine.AssetCompiler`：可独立安装的 .NET Tool，命令名为 `gameengine-assets`。
- `MyGameEngine.ContentPipeline`：供游戏项目引用的 `buildTransitive` NuGet 包，内置私有编译器载荷并自动接入 Build 与 Publish。

当前预览版本统一定义在仓库根目录 `Directory.Build.props` 的 `GameEnginePackageVersion`。两个包必须始终使用相同版本发布。

## Compiler 架构与职责

“Content Assets Compiler”不是单一 Atlas 函数，而是一条由多个组件组成的离线管线：

| 组件 | 实现职责 |
|---|---|
| `AssetPackageManifestParser` | Source-generated JSON 解析、Schema 与局部字段验证；编译期和运行时共用。 |
| `ContentBuildPipeline` | 读取完整依赖图、全局验证、计算增量指纹、复用 Package 输出、事务发布目录。 |
| `ContentAssetCompiler` | 对一个启用 `atlas` 的 Package 解码/裁帧/打包，重写标准运行时 Manifest。 |
| `ContentReferenceCodeGenerator` | 扫描编译后依赖图并生成 `GameAssets` 强类型逻辑 Ref。 |
| `Program` | 把上述能力暴露为 `--incremental/rebuild/check/generate-references` 等稳定 CLI。 |
| `GameEngine.Content.targets` | 把 CLI 排到 `CoreCompile` 之前，并接入 Build、Run、Publish。 |

Compiler 直接引用 ContentAssets、TextureAssets、TextureAtlas、Tilemaps、TileWorlds 等正式模块的 Domain/Parser，而不是复制一份 Schema。这保证开发工具与 Runtime 对包 ID、Sprite 帧、TileMap、`.mgworld` 和逻辑名称的理解一致。反向依赖不存在：Runtime Feature 不引用 Tool。

核心实现可从 [`ContentBuildPipeline.cs`](../src/Engine.Tools.AssetCompiler/ContentBuildPipeline.cs)、[`ContentAssetCompiler.cs`](../src/Engine.Tools.AssetCompiler/ContentAssetCompiler.cs)、[`ContentReferenceCodeGenerator.cs`](../src/Engine.Tools.AssetCompiler/ContentReferenceCodeGenerator.cs) 与 [`GameEngine.Content.targets`](../build/GameEngine.Content.targets) 顺序阅读。

## 构建本地包

```powershell
dotnet pack src/Engine.Tools.AssetCompiler/Engine.Tools.AssetCompiler.csproj `
  -c Release -o artifacts/packages

dotnet pack src/Engine.Build.ContentPipeline/Engine.Build.ContentPipeline.csproj `
  -c Release -o artifacts/packages
```

`artifacts/` 是本地生成目录，不应提交版本控制。正式发布前仍需补充许可证、仓库元数据、签名和远程 Feed 发布流程。

## 使用 AssetCompiler Tool

从本地 Feed 安装：

```powershell
dotnet tool install MyGameEngine.AssetCompiler `
  --tool-path .tools `
  --version 0.1.0-alpha.1 `
  --add-source artifacts/packages
```

执行编译：

```powershell
.tools/gameengine-assets --incremental Assets assets.json artifacts/compiled
.tools/gameengine-assets --rebuild Assets assets.json artifacts/compiled
.tools/gameengine-assets --check Assets assets.json artifacts/compiled
.tools/gameengine-assets --generate-references `
  artifacts/compiled assets.json obj/GameEngine.Content.g.cs MyGame.Content GameAssets
.tools/gameengine-assets --validate-shaders Shaders/shaders.json
.tools/gameengine-assets --generate-shader-references `
  . Shaders/shaders.json obj/GameEngine.Shaders.g.cs MyGame.Content GameShaders
```

Windows 的直接可执行文件名为 `gameengine-assets.exe`。Tool 退出码约定：

| 退出码 | 含义 |
| --- | --- |
| `0` | 编译成功，或 check 确认输出为最新。 |
| `1` | 清单、图片、路径、输出所有权或编译过程失败。 |
| `2` | 命令行参数无效。 |
| `3` | check 检测到输出已陈旧。 |

## 外部游戏项目接入

游戏项目不需要全局 Tool，也不需要引用 MyGameEngine 源仓库：

```xml
<PropertyGroup>
  <GameEngineContentPackagesRoot>$(MSBuildProjectDirectory)\Assets</GameEngineContentPackagesRoot>
  <GameEngineContentManifest>assets.json</GameEngineContentManifest>
  <GameEngineShaderManifest>$(MSBuildProjectDirectory)\Shaders\shaders.json</GameEngineShaderManifest>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="MyGameEngine.ContentPipeline"
                    Version="0.1.0-alpha.1"
                    PrivateAssets="all" />
</ItemGroup>
```

`PrivateAssets="all"` 防止构建工具包作为游戏运行时或库的传递依赖暴露。包使用约定命名的 `buildTransitive/MyGameEngine.ContentPipeline.props` 和 `.targets` 自动导入构建逻辑，并从包自身的 `tools/net10.0/any` 定位编译器，不依赖用户全局 Tool。

第一次 Build 会编译内容；后续 Build 仍会调用编译器，但内容指纹命中时不会重新解码图片或生成 Atlas：

```powershell
dotnet build Game.csproj
dotnet publish Game.csproj -c Release
```

编译缓存保存在 `obj/<Configuration>/<TargetFramework>/CompiledAssets`。同一 Target 默认生成并编译 `GameEngine.Content.g.cs`，提供 `GameAssets.Packages/Sprites/Textures`；设置 Shader 清单后还会生成 `GameEngine.Shaders.g.cs`，提供 `GameShaders.ManifestPath/Shaders/Materials/Parameters`。运行时内容包复制到 `bin/.../AssetsCompiled` 和 Publish 的 `AssetsCompiled`。生成源码不会进入运行目录；`.mygame-assets.json` 会最后复制，作为可选 Content 热重载与诊断使用的修订提交标记。

## 一次 Build 的实现顺序

`CompileGameEngineContent` 在 `ResolveProjectReferences` 之后、Roslyn `CoreCompile` 之前执行：

```text
构建/定位 GameEngineAssetCompiler.dll
  → --incremental 编译完整内容图到 obj/.../CompiledAssets
  → --generate-references 从“编译后 Manifest 图”生成 GameEngine.Content.g.cs
  → <Compile Include="...g.cs"> 注入本次 C# 编译
  → 普通资源复制到 bin/.../AssetsCompiled
  → .mygame-assets.json 最后复制，发布修订
```

强类型引用从编译后图生成非常重要：Atlas 内部页、旁路 Texture、规范化帧和 TileWorld 归档路径已经确定，生成器会隐藏 `__atlas.*` 内部 Texture，并只为编译后仍属于 Runtime Package 的 Package/Texture/Sprite/Animation/Audio/TileSet/TileMap/TileWorld 生成成员。被 Atlas 完全取代的源 Texture 是构建输入，不会错误地成为可加载 API。生成器按逻辑名称排序，把 `boss.attack-heavy` 转成 `BossAttackHeavy`；重复逻辑名或两个名称映射到同一 C# 标识符会让 Build 失败。

`GameEngine.Content.g.cs` 是普通的 MSBuild 生成源码，不是 Roslyn `ISourceGenerator`。Target 把它标记为 `AutoGen/DesignTime/Visible=false`，因此 IDE 可补全但文件保存在 `obj`；生成器使用 UTF-8 临时文件加覆盖移动，并在内容未变化时不重写，避免无意义时间戳和增量 C# 重编译。

## 依赖图与验证顺序

Compiler 首先在不写输出的阶段递归读取完整依赖图：

- 依赖 Manifest 相对 `packagesRoot`，资源文件相对所属 Package 目录。
- DFS 检测循环、expected ID 不匹配和同 ID 多 Manifest。
- 所有输入路径通过完整路径归一化，拒绝绝对路径和根目录逃逸。
- 全图拒绝 Texture/Sprite/Animation/Audio/TileSet/TileMap/TileWorld 重名与输出路径冲突。
- Sprite、Animation、TileSet、TileMap、TileWorld 只能引用当前 Package 的依赖闭包。
- Atlas 输入 Texture 属于依赖时有更严格边界：下游不能直接依赖另一个 Package 的 build-only Atlas Texture，应依赖其逻辑 Sprite。

只有图和所有引用规则通过后才创建 staging 输出；因此多数作者错误不会留下半成品目录。

## 增量指纹与 Package 复用

Fingerprint 使用确定性 SHA-256 输入流；字符串字段带长度前缀，文件内容以流式分块加入 Hash。输入包含：

- Compiler owner 与 `CompilerVersion`；
- Package ID、Manifest 相对路径和 Manifest 文件内容；
- 按逻辑名称排序的 Texture/WAV/TileMap/TileWorld source 路径与文件内容；
- 每个直接依赖的 Package ID 与递归 Fingerprint。

根 Fingerprint 表示完整依赖闭包，Package Fingerprint 则允许局部复用。默认 `incremental` 的判断分两层：

1. 根元数据、Fingerprint、输出文件集合和每个文件 SHA-256 全部匹配时，整图 `UpToDate`。
2. 根已变化时，新 staging 仍可从旧权威输出复制 Fingerprint 未变且文件 Hash 有效的 Package，只重编译变化 Package 及受其递归 Fingerprint 影响的上游 Package。

`rebuild` 忽略复用并重建全部 Package；`check` 不写任何文件，只返回 `UpToDate` 或 `Stale`，后者映射为退出码 `3`。修改 Compiler 算法时递增 `CompilerVersion`，旧缓存会自然失效。

## Atlas 编译与标准输出

没有 `atlas` 配置的 Package 原样复制普通 Manifest 资源；TileWorld source 始终由完整 `ContentBuildPipeline` 编译为 `.mgworld` 并重写清单，而不会复制进运行时输出。编译器从原始 Sprite 帧生成权威 LOD0 与逐 Layer LOD1+ exact 无损 WebP；这一步独立于源 Texture 最终是否被 Atlas 替换。CLI 分别输出 `TileWorld Chunks` 总数与 `TileWorld Raster Chunks` 数量。启用 Atlas 时，`ContentAssetCompiler`：

1. 把 `single/grid/frames` 全部规范化为 `(TextureName, PixelRectI)` 帧。
2. 延迟解码图片，只解码被选中并实际引用的来源；相同 Texture/Rect 只裁剪一次。
3. 按 `pixelArt/smooth` 采样状态分组，避免一个 Atlas 页混用互斥 Sampler。
4. 交给 `TextureAtlasBuilder` 生成多页 RGBA8，应用 padding/extrude。
5. 已放置帧重映射到内部 Atlas 页；放不下的帧保留原 Texture 旁路。
6. 按包配置输出默认 PNG 或 exact 无损 WebP 页面，并把 Sprite 统一重写为显式 `frames`，保留逻辑尺寸、原点、FPS、Animation、Audio、TileSet、TileMap 和待编译 TileWorld 声明。

输出仍是 Runtime 可以直接解析的标准 Package，而不是只能由 Compiler 理解的数据库。这个边界让一个动画可以跨 Atlas 页，过大帧也可以继续引用独立 Texture，而 `SpriteRef` 和 Gameplay API 不变。

## 事务输出与目录所有权

权威输出不能位于源 `packagesRoot` 内，也不能是文件系统根目录。若目标非空但没有兼容的 `.mygame-assets.json` owner/schema，Compiler 拒绝覆盖，防止把用户目录误当缓存删除。

真正构建发生在 `<output>.tmp-<guid>`；成功后写入所有文件 Hash 和 Package 元数据，再把旧目录移动到 backup、staging 移动为权威 output，最后删除 backup。任一步异常都会删除 staging，并在必要时恢复 backup。因此 Build 要么保留上一份完整修订，要么看到下一份完整修订，不暴露部分成功状态。

`.mygame-assets.json` 同时承担：

- 输出目录所有权证明；
- 增量缓存索引和输出完整性 Hash；
- 根包/Manifest/Compiler 身份；
- Hot Reload 使用的完整修订提交标记。

## MSBuild 配置

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `GameEngineContentPackagesRoot` | 无 | 源包根目录，同时也是启用内容构建的开关。 |
| `GameEngineContentManifest` | `assets.json` | 相对源包根目录的入口清单。 |
| `GameEngineContentBuildMode` | `incremental` | `incremental`、`rebuild` 或 `check`。 |
| `GameEngineContentOutput` | `obj/<Configuration>/<TargetFramework>/CompiledAssets` | 权威编译缓存目录。 |
| `GameEngineContentOutputSubdirectory` | `AssetsCompiled` | bin/Publish 内的目标子目录。 |
| `GameEngineContentGenerateReferences` | `true` | 生成并编译强类型逻辑资源引用。 |
| `GameEngineContentGeneratedNamespace` | `$(RootNamespace).Content` | 生成代码命名空间。 |
| `GameEngineContentGeneratedClass` | `GameAssets` | 生成的项目级根容器。 |
| `GameEngineContentGeneratedFile` | `obj/<Configuration>/<TargetFramework>/GameEngine.Content.g.cs` | 生成源码路径。 |
| `GameEngineShaderManifest` | 无 | 声明式 Shader 清单，同时也是 Shader 校验与生成开关。 |
| `GameEngineShaderGenerateReferences` | `true` | 生成并编译强类型 Shader/Material/参数引用。 |
| `GameEngineShaderGeneratedNamespace` | `$(RootNamespace).Content` | Shader 生成代码命名空间。 |
| `GameEngineShaderGeneratedClass` | `GameShaders` | Shader 生成根容器。 |
| `GameEngineShaderGeneratedFile` | `obj/<Configuration>/<TargetFramework>/GameEngine.Shaders.g.cs` | Shader 生成源码路径。 |
| `GameEngineAssetCompilerDll` | 包内编译器或源码仓库输出 | 高级覆盖入口。 |
| `GameEngineDotNetHost` | `$(DotNetHostPath)` 或 `dotnet` | 用于启动框架依赖编译器的 dotnet host。 |

`check` 模式检测到陈旧输出时，编译器返回 `3`，因此 MSBuild 的 `Exec` 会让 Build 失败。这适合 CI 验证“已生成内容必须保持最新”；普通开发构建应使用默认 `incremental`。

## 源码仓库模式

Runner 与 Sprites.VisualTests 继续使用：

- 对 `Engine.Tools.AssetCompiler` 的 `ProjectReference`，仅保证构建顺序。
- 显式导入 `build/GameEngine.Content.targets`。

这避免仓库构建依赖一个尚未发布的自身 NuGet 包。源码模式和 NuGet 模式消费同一份 targets，差异只在编译器路径来源。

## ContentPipeline NuGet 如何自包含

`MyGameEngine.ContentPipeline` 设置 `IncludeBuildOutput=false` 和 `SuppressDependenciesWhenPacking=true`：游戏不会在运行时引用一个“构建工具程序集”。打包阶段反而把 AssetCompiler 的完整框架依赖输出、托管依赖、SkiaSharp native assets 和 exact WebP 编码所需的 MIT libwebp 多平台 runtime 收集到包内 `tools/net10.0/any`。

`buildTransitive/*.props` 只计算当前 NuGet 包根目录；`*.targets` 再从该根目录定位私有 `GameEngineAssetCompiler.dll`。因此外部项目无需安装全局 Tool，也不会依赖源码仓库绝对路径。源码仓库模式用 `ProjectReference ReferenceOutputAssembly=false` 保证 Compiler 先构建，然后导入同一份 targets；两种模式最终执行的是同一个 DLL 和同一条命令行协议。

## 包边界

- 两个包当前要求 .NET 10。
- ContentPipeline 包携带完整框架依赖编译器、托管依赖、SkiaSharp 与 libwebp 多平台 native assets，不要求外部游戏项目引用这些程序集。
- 包不包含源资产、编译缓存或任何仓库绝对路径。
- 当前未实现包签名、远程 Feed 发布、跨仓库共享缓存或远程缓存。
- 内容格式、Atlas 与 Shader 生成边界仍以 [Content Assets](CONTENT_ASSETS.md)、[Texture Atlas](TEXTURE_ATLAS.md)、[强类型 Content 引用](STRONGLY_TYPED_CONTENT.md)和[声明式 Shader 与 Material Assets](SHADER_ASSETS.md)文档为准。

# 可分发内容工具链

MyGameEngine 内容工具链提供两个同版本包：

- `MyGameEngine.AssetCompiler`：可独立安装的 .NET Tool，命令名为 `gameengine-assets`。
- `MyGameEngine.ContentPipeline`：供游戏项目引用的 `buildTransitive` NuGet 包，内置私有编译器载荷并自动接入 Build 与 Publish。

当前预览版本统一定义在仓库根目录 `Directory.Build.props` 的 `GameEnginePackageVersion`。两个包必须始终使用相同版本发布。

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

## 包边界

- 两个包当前要求 .NET 10。
- ContentPipeline 包携带完整框架依赖编译器、托管依赖以及 SkiaSharp 多平台 native assets，不要求外部游戏项目引用这些程序集。
- 包不包含源资产、编译缓存或任何仓库绝对路径。
- 当前未实现包签名、远程 Feed 发布、跨仓库共享缓存或远程缓存。
- 内容格式、Atlas 与 Shader 生成边界仍以 [Content Assets](CONTENT_ASSETS.md)、[Texture Atlas](TEXTURE_ATLAS.md)、[强类型 Content 引用](STRONGLY_TYPED_CONTENT.md)和[声明式 Shader 与 Material Assets](SHADER_ASSETS.md)文档为准。

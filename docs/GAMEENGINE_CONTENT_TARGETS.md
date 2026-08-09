# `GameEngine.Content.targets` 解读

[`build/GameEngine.Content.targets`](../build/GameEngine.Content.targets) 是游戏项目与 `Engine.Tools.AssetCompiler` 之间的 MSBuild 适配层。它不实现图片解码、Atlas 排布或内容指纹，而是负责以下四件事：

1. 在 C# 编译前调用资产编译器。
2. 从编译后的 Manifest 图生成并编译强类型逻辑引用。
3. 将编译后的标准内容包复制到程序输出目录。
4. 将同一批运行时资产加入 `dotnet publish` 的发布文件列表。

内容是否需要重建由 AssetCompiler 的包级 SHA-256 指纹判断，而不是由 MSBuild 的文件时间戳判断。

## 推荐接入方式：NuGet 包

外部游戏项目应引用构建包，不需要知道 AssetCompiler 的物理位置：

```xml
<PropertyGroup>
  <GameEngineContentPackagesRoot>$(MSBuildProjectDirectory)\Assets</GameEngineContentPackagesRoot>
  <GameEngineContentManifest>assets.json</GameEngineContentManifest>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="MyGameEngine.ContentPipeline"
                    Version="0.1.0-alpha.1"
                    PrivateAssets="all" />
</ItemGroup>
```

包通过 `buildTransitive` 导入本文件，并从包内 `tools/net10.0/any` 解析编译器。`PrivateAssets="all"` 保证构建工具不会成为游戏运行时的传递依赖。

## 源码仓库接入方式

项目文件需要声明源包根目录、保证编译器项目先构建，然后导入 Target：

```xml
<PropertyGroup>
  <GameEngineContentPackagesRoot>$(MSBuildProjectDirectory)\Assets</GameEngineContentPackagesRoot>
  <GameEngineContentManifest>assets.json</GameEngineContentManifest>
</PropertyGroup>

<ItemGroup>
  <ProjectReference Include="..\Engine.Tools.AssetCompiler\Engine.Tools.AssetCompiler.csproj"
                    ReferenceOutputAssembly="false" />
</ItemGroup>

<Import Project="..\..\build\GameEngine.Content.targets" />
```

`ReferenceOutputAssembly="false"` 表示项目只需要 AssetCompiler 的构建顺序，不把编译器程序集作为游戏运行时依赖引用。

如果没有设置 `GameEngineContentPackagesRoot`，两个 Target 的 `Condition` 都不成立，整个内容构建集成会保持关闭。

## 可配置属性

| 属性 | 默认值 | 含义 |
| --- | --- | --- |
| `GameEngineContentPackagesRoot` | 无 | 源内容包根目录；也是启用 Target 的开关。 |
| `GameEngineContentManifest` | `assets.json` | 相对 Packages Root 的根包清单路径。可以指向子目录，例如 `characters/boss/assets.json`。 |
| `GameEngineContentBuildMode` | `incremental` | 编译模式：`incremental`、`rebuild` 或 `check`。非法值会在调用编译器前失败。 |
| `GameEngineContentOutput` | `obj/<Configuration>/<TargetFramework>/CompiledAssets` | 编译缓存与标准运行时包的生成目录。 |
| `GameEngineContentOutputSubdirectory` | `AssetsCompiled` | `bin` 和 Publish 目录中的运行时资产子目录。 |
| `GameEngineContentGenerateReferences` | `true` | 是否生成并编译强类型引用。 |
| `GameEngineContentGeneratedNamespace` | `$(RootNamespace).Content` | 生成代码命名空间。 |
| `GameEngineContentGeneratedClass` | `GameAssets` | 生成的根容器类型名。 |
| `GameEngineContentGeneratedFile` | `obj/<Configuration>/<TargetFramework>/GameEngine.Content.g.cs` | 生成文件位置。 |
| `GameEngineAssetCompilerDll` | NuGet 包内编译器；源码模式为仓库 `bin` 输出 | 要执行的编译器 DLL，可显式覆盖。 |
| `GameEngineDotNetHost` | `$(DotNetHostPath)` 或 `dotnet` | 启动框架依赖编译器的 dotnet host。 |

默认中间输出显式包含 `Configuration` 和 `TargetFramework`，因此 Debug/Release 或不同目标框架不会共享错误的资产缓存。

## `CompileGameEngineContent` 的执行顺序

```text
ResolveProjectReferences
        │
        ▼
确保 AssetCompiler 项目已完成构建
        │
        ▼
CompileGameEngineContent
  ├─ 检查编译器 DLL
  ├─ 执行 AssetCompiler --incremental
  ├─ 从编译产物执行 --generate-references
  ├─ 将 GameEngine.Content.g.cs 加入 Compile
  ├─ 枚举运行时产物
  └─ 刷新 bin/.../AssetsCompiled
        │
        ▼
CoreCompile
```

Target 同时声明：

```xml
AfterTargets="ResolveProjectReferences"
BeforeTargets="CoreCompile"
```

这保证项目引用已经解析、编译器 DLL 已经可用，同时游戏代码尚未进入核心编译阶段。资产失败会直接使 Build 失败，不会产生“代码构建成功但运行时缺少资产”的半成品。

实际执行命令等价于：

```powershell
dotnet <GameEngineAssetCompilerDll> --<GameEngineContentBuildMode> `
  <GameEngineContentPackagesRoot> `
  <GameEngineContentManifest> `
  <GameEngineContentOutput>
```

AssetCompiler 会解析完整依赖图，并根据 manifest、图片内容、依赖指纹和编译器版本决定哪些包需要重建。Target 每次都可以安全调用它；缓存命中时不会重写产物或元数据。

随后 Target 执行：

```powershell
dotnet <GameEngineAssetCompilerDll> --generate-references `
  <GameEngineContentOutput> `
  <GameEngineContentManifest> `
  <GameEngineContentGeneratedFile> `
  <GameEngineContentGeneratedNamespace> `
  <GameEngineContentGeneratedClass>
```

生成器读取编译后的运行时 Manifest，因此不会公开已被 Atlas 移除的源 Texture 或内部 Atlas 页。输出未变化时保留文件时间戳；详细规则见[强类型 Content 引用](STRONGLY_TYPED_CONTENT.md)。

## 从 `obj` 到 `bin`

编译器的权威输出保存在：

```text
obj/<Configuration>/<TargetFramework>/CompiledAssets/
```

随后 Target 会：

1. 暂时排除根目录的 `.mygame-assets.json`。
2. 删除旧的 `bin/.../AssetsCompiled`。
3. 重新创建目录并复制当前运行时文件。
4. 最后复制 `.mygame-assets.json`，把它作为完整修订的运行时提交标记。

先删除输出目录可以清除改名或删除资源留下的陈旧文件。相应代价是每次 Build 都会重新复制运行时资产；真正昂贵的图片解码和 Atlas 构建仍由 `obj` 中的增量缓存避免。

`.mygame-assets.json` 保存所有权、输入指纹和输出哈希。普通运行时只需 Manifest 和图片；启用 Content 热重载时，Hosting 读取其中的根包身份和输入指纹判断完整修订。因此 Target 会把它复制到 `bin`，但始终最后写入，避免运行时观察到尚未复制完整的新内容。

## Publish 接入

`IncludeGameEngineContentInPublish` 在 `ComputeFilesToPublish` 前运行，并声明：

```xml
DependsOnTargets="CompileGameEngineContent"
```

因此直接执行 `dotnet publish` 时，也会先得到经过验证的最新内容包。Target 把生成文件加入 `ResolvedFileToPublish`，发布后的结构为：

```text
publish/
├── MyGameRunner.dll
└── AssetsCompiled/
    ├── assets.json
    ├── .mygame-assets.json
    ├── atlas/
    └── ...
```

发布列表包含 `.mygame-assets.json`，使同一构建产物保留可诊断的修订身份；是否开启热重载仍由运行时代码显式决定。在一次 MSBuild 调用中，即使编译 Target 同时被 Build 和 Publish 依赖，MSBuild 也只执行该 Target 一次。

## Build、Run 和 Publish 的关系

- `dotnet build`：增量编译到 `obj`，再复制到 `bin/.../AssetsCompiled`。
- `dotnet run`：默认先触发 Build，因此使用相同流程；`--no-build` 会直接使用现有 `bin` 内容。
- `dotnet publish`：确保增量编译完成，并将运行时资产加入发布目录。
- IDE Build/Run：只要 IDE 使用标准 MSBuild，同样会触发这个 Target。

运行时代码应该读取输出目录下的 `AssetsCompiled`，不应直接读取项目源目录中的 `Assets`。

## 为什么没有使用 MSBuild `Inputs` / `Outputs`

一个根 manifest 可以通过包依赖间接引用多份 manifest 和图片。仅在 Target 上声明静态 `Inputs` 很难完整表达传递依赖，也无法可靠处理内容相同但时间戳变化、文件删除或编译算法升级。

因此边界划分为：

- MSBuild：每次在固定阶段调用编译器并搬运运行时文件。
- AssetCompiler：解析依赖图、计算内容指纹、验证输出哈希并决定包级重建范围。

这让命令行、IDE 和 CI 共享同一套增量判定。

## 常见问题

### `GameEngine AssetCompiler was not built`

源码模式下确认项目包含指向 AssetCompiler 的 `ProjectReference`，并且其 `ReferenceOutputAssembly` 为 `false`。NuGet 模式下确认包内存在 `tools/net10.0/any/GameEngineAssetCompiler.dll`，并检查是否错误覆盖了 `GameEngineAssetCompilerDll`。

### 修改资产后使用 `dotnet run --no-build` 没有变化

`--no-build` 不会触发 MSBuild Target。先执行一次 `dotnet build`，或者去掉 `--no-build`。

### 程序能构建，但运行时找不到包

确认运行时代码从 `AppContext.BaseDirectory/AssetsCompiled` 加载，并检查 `GameEngineContentOutputSubdirectory` 是否被项目和代码一致地覆盖。

### 希望查看缓存状态但不写文件

直接调用编译器的 `--check` 模式：

```powershell
dotnet <GameEngineAssetCompilerDll> --check `
  <packages-root> <manifest-relative-path> <compiled-output>
```

当前输出会返回退出码 `0`；检测到陈旧输入会返回退出码 `3`，适合 CI 验证生成状态。

## 当前边界

- Target 同时支持仓库源码输出和 `MyGameEngine.ContentPipeline` 包内编译器。
- `MyGameEngine.AssetCompiler` 提供独立 `gameengine-assets` .NET Tool 命令。
- 两个包当前均要求 .NET 10，尚未发布到远程 Feed 或签名。
- Target 负责项目级接入，不负责跨项目共享缓存或远程构建缓存。
- 源资产和 manifest 应提交版本控制；`obj/CompiledAssets`、`bin/AssetsCompiled` 和 Publish 产物不应提交。

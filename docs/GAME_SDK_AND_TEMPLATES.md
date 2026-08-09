# Game SDK 与项目模板

本切片把引擎从“只能通过仓库内 `ProjectReference` 使用”推进到标准 .NET 分发边界。

## 分发包

- `MyGameEngine.GameSdk`：运行时聚合包，包含 Core、Hosting 与全部正式 Feature 程序集，并声明 Silk.NET、SkiaSharp 等第三方运行依赖。
- `MyGameEngine.ContentPipeline`：构建期包，负责内容编译、强类型引用生成及 Publish 复制。
- `MyGameEngine.Templates`：`dotnet new` 模板包，生成只包含 `PackageReference` 的独立游戏项目。

三个包共享 `Directory.Build.props` 中的 `GameEnginePackageVersion`。模板项目中的两个引擎包版本也必须随发布版本同步；分发集成测试会阻止版本漂移。

## 本地打包与创建项目

```powershell
dotnet pack src/Engine.Distribution.GameSdk/Engine.Distribution.GameSdk.csproj -c Release -o artifacts/packages
dotnet pack src/Engine.Build.ContentPipeline/Engine.Build.ContentPipeline.csproj -c Release -o artifacts/packages
dotnet pack src/Engine.Templates/Engine.Templates.csproj -c Release -o artifacts/packages

dotnet new install artifacts/packages/MyGameEngine.Templates.0.1.0-alpha.1.nupkg
dotnet new mygameengine-game -n MyFirstGame
```

若包尚未发布到公共源，应在游戏项目使用的 `NuGet.Config` 中加入 `artifacts/packages` 本地源，然后执行：

```powershell
dotnet restore
dotnet run
```

## 模板生成内容

模板默认提供：

- `GameApplication` 与默认二维渲染预设的最小组合根。
- 一个继承 `GameInstance` 的旋转 Sprite 示例。
- `Assets/assets.json` 与真实 WebP 纹理。
- 自动生成的 `MyGame.Content.GameAssets` 强类型引用。
- `--smoke` 三帧自动退出模式，便于 CI 或环境自检。

生成项目不包含仓库绝对路径、`ProjectReference`、手写资产字符串或对 AssetCompiler 的直接调用。

## 包边界

`GameSdk` 暂时采用单包聚合所有正式运行时程序集，以降低原型阶段的版本协调与入门成本。各 Feature 在源码仓库中仍保持垂直切片；将来只有在需要独立发布节奏、裁剪下载体积或稳定公共 API 层级时，才拆成多个 NuGet 包。

模板包不依赖运行时包；它只携带源文件。真正的依赖关系由生成项目的两个 `PackageReference` 显式表达。

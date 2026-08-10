# Windows x64 Native AOT 发布

Phase 1.3 为游戏可执行项目提供显式的 `win-x64` Native AOT 发布路径。GameSdk 内的运行时程序集启用 AOT、裁剪和单文件分析器；游戏模板仍默认使用普通 JIT build/run，仅在发布命令中显式开启 AOT。

现有 `GameInstance` 生命周期通过虚方法直接派发，不依赖运行时反射、动态委托查找或动态代码生成，因此本阶段不引入自定义事件 Source Generator。

## 环境要求

- .NET 10 SDK。
- Visual Studio 2022 或 Build Tools 的 **Desktop development with C++** workload，包含 MSVC linker 和 Windows SDK。
- 支持 OpenGL 3.3 Core 的 Windows x64 机器与显卡驱动。

Native AOT 的平台工具链要求和限制以 [.NET Native AOT 官方文档](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)为准。AOT 产物绑定目标 RID；本阶段只验收 `win-x64`。

## 发布

Runner：

```powershell
dotnet publish src/MyGame.Runner/MyGame.Runner.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishAot=true `
  -p:TrimmerSingleWarn=false
```

模板生成的游戏使用相同参数：

```powershell
dotnet publish MyGame.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishAot=true `
  -p:TrimmerSingleWarn=false
```

`PublishAot` 不写入模板默认属性，因此普通 `dotnet build`、`dotnet run` 和非 AOT `dotnet publish` 的行为保持不变。

## 产物边界

发布目录是无需预装 .NET 的自包含应用目录，而不是物理单文件。典型内容包括：

- 游戏原生可执行文件。
- `glfw3.dll` 与 `libSkiaSharp.dll` 等随附原生库。
- `AssetsCompiled`、Shader 源文件和其他运行时内容。
- 可选 PDB 调试符号。

生成阶段的 `GameEngine.Content.g.cs` 和 `GameEngine.Shaders.g.cs` 只参与编译，不进入发布目录。当前不启用原生库自解压或资产嵌入，避免增加启动期临时文件和加载边界。

## 验收

普通分发验证保持快速、离线的 JIT 路径：

```powershell
dotnet run -c Release `
  --project src/Engine.Distribution.Tests/Engine.Distribution.Tests.csproj
```

安装 C++ workload 后运行显式 AOT 验收：

```powershell
dotnet run -c Release `
  --project src/Engine.Distribution.Tests/Engine.Distribution.Tests.csproj `
  -- --native-aot
```

AOT 模式会从本地打包结果创建仓库外游戏项目，执行 `win-x64` AOT publish，拒绝任何 `warning IL####`，检查原生产物和编译后资产，并直接运行发布程序的隐藏三帧 `--smoke`。

运行时 JSON Manifest 和 Runner 诊断使用 `System.Text.Json` source-generation contexts。AssetCompiler 是发布前执行的框架依赖构建工具，不进入游戏运行闭包，也不继承游戏项目的 AOT、RID 或 self-contained 属性。

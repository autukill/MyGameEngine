# `gameengine doctor` 开发环境诊断

`MyGameEngine.Cli` 是独立于运行时和内容编译器的 .NET Tool。首个命令 `gameengine doctor` 用于在提交代码或排查新项目环境时执行只读诊断。

## 安装与使用

```powershell
dotnet tool install MyGameEngine.Cli --global --version 0.1.0-alpha.1
gameengine doctor
gameengine doctor MyGame.csproj --configuration Release
gameengine doctor MyGame.csproj --probe-opengl
```

不传项目路径时检查当前目录；目录中必须恰好有一个 `.csproj`，否则应传入明确文件。

## 检查范围

- 当前 Tool 运行在 .NET 10 或更高版本。
- 项目直接声明 `net10.0`。
- `MyGameEngine.GameSdk` 存在；`MyGameEngine.ContentPipeline` 缺失时给出警告。
- GameSdk 与 ContentPipeline 的显式版本一致。
- `obj/project.assets.json` 已解析声明的引擎包。
- `GameEngineContentPackagesRoot`、Manifest 路径安全且存在。
- Manifest 包含 `schemaVersion: 1` 和非空 `id`。
- 所选配置下的强类型 `.g.cs` 与 `AssetsCompiled` 产物存在且不早于源 Manifest。
- 显式传入 `--probe-opengl` 时创建隐藏 OpenGL 3.3 Core Context，并报告 Vendor 与 Renderer。

普通诊断不会创建窗口或修改项目。OpenGL 探测也是隐藏、短生命周期窗口，但可能触发系统图形驱动初始化，因此必须显式启用。

## 输出与退出码

每条诊断都有稳定代码和级别：

- `[PASS]`：检查通过。
- `[WARN]`：项目仍可继续工作，但缺少 Restore/Build 产物或推荐配置。
- `[FAIL]`：配置无效或所需能力不可用。

退出码：

- `0`：没有错误；允许存在警告。
- `1`：至少一项诊断错误。
- `2`：命令或参数用法错误。

稳定诊断代码适合 CI 日志检索；当前 v1 保持人类可读文本输出，后续若接入 IDE/编辑器再增加 JSON 格式，不改变现有代码语义。

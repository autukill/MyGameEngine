# 声明式 Shader 与 Material Assets

shaders.json 把 Shader 文件、Material Schema 和默认参数放在同一个严格版本化清单中。它同时服务于四个阶段：

1. MSBuild/AssetCompiler 在 C# 编译前完成无 GL 的静态校验。
2. AssetCompiler 生成强类型 Shader、Material 与 Uniform 参数引用。
3. Hosting 在创建 Scene 前编译 Program、创建材质并应用默认参数。
4. Shader 热重载复用清单中的固定 Program 集合，并在候选切换前执行真实 GL Uniform 契约校验。

## 清单格式

~~~json
{
  "schemaVersion": 1,
  "shaders": [
    {
      "name": "game.player",
      "vertex": "sprite.vert.glsl",
      "fragment": "player.frag.glsl"
    }
  ],
  "materials": [
    {
      "name": "game.player.default",
      "shader": "game.player",
      "uniforms": [
        { "name": "uFlash", "type": "float", "default": 0.0 },
        {
          "name": "uDirection",
          "type": "vector2",
          "default": { "x": 1.0, "y": 0.0 }
        },
        {
          "name": "uOverlay",
          "type": "vector4",
          "default": { "x": 1.0, "y": 0.2, "z": 0.2, "w": 1.0 }
        }
      ]
    }
  ]
}
~~~

固定规则：

- schemaVersion 必须为 1，未知字段拒绝。
- shaders 至少包含一项；名称区分大小写且不能重复。
- vertex、fragment 相对清单目录解析，绝对路径和目录逃逸拒绝，文件必须存在。
- materials 数组必填但可以为空；Material 只能引用本清单 Shader。
- Uniform 名称在单个 Material 内不能重复，且不能占用 uProjection、uTexture。
- 类型支持 float、int、vector2、vector4；default 必填并且形状必须匹配。
- 浮点数必须有限。v1 不支持 Matrix、Texture/Sampler、Array 或逐 Material Shader 宏。

## Hosting

把整个目录复制到输出和发布目录：

~~~xml
<ItemGroup>
  <Content Include="Shaders\**\*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
  </Content>
</ItemGroup>
~~~

使用构建生成的清单路径代替逐个 ShaderFileDefinition：

~~~csharp
renderer
    .UseShaderAssets(GameShaders.ManifestPath)
    .EnableShaderHotReload(options);
~~~

Scene 装配时材质已经完成注册：

~~~csharp
var player = new Player(GameAssets.Sprites.Player)
{
    Material = GameShaders.Materials.GamePlayerDefault
};
context.Scene.Add(player);

context.Shaders.Set(
    GameShaders.Parameters.GamePlayerDefault.Flash,
    0.75f);
~~~

UseShaders(...) 仍适合不需要声明式材质的底层路径，但不能与 UseShaderAssets(...) 同时使用。

生成代码只包含逻辑 `ShaderRef`、`MaterialRef` 和 `MaterialParameterRef<T>`，不包含 Program Handle 或 Uniform Location。`uFlash` 这类惯用 uniform 名称在 C# 中生成 `Flash`，运行时仍使用原始名称；参数键同时携带所属 Material，传给错误材质时会立即失败。

## MSBuild 静态校验与引用生成

项目设置清单路径后，共享 Target 会在 CoreCompile 前调用 AssetCompiler，并默认生成 `GameEngine.Shaders.g.cs`：

~~~xml
<PropertyGroup>
  <GameEngineShaderManifest>$(MSBuildProjectDirectory)\Shaders\shaders.json</GameEngineShaderManifest>
</PropertyGroup>
~~~

等价命令：

~~~powershell
dotnet GameEngineAssetCompiler.dll --validate-shaders Shaders/shaders.json
dotnet GameEngineAssetCompiler.dll --generate-shader-references `
    . Shaders/shaders.json obj/GameEngine.Shaders.g.cs `
    MyGame.Content GameShaders
~~~

可配置属性：

| 属性 | 默认值 | 含义 |
| --- | --- | --- |
| `GameEngineShaderGenerateReferences` | `true` | 是否生成并编译强类型 Shader 引用。 |
| `GameEngineShaderGeneratedNamespace` | `$(RootNamespace).Content` | 生成代码命名空间。 |
| `GameEngineShaderGeneratedClass` | `GameShaders` | 生成的根容器名称。 |
| `GameEngineShaderGeneratedFile` | `obj/<Configuration>/<TargetFramework>/GameEngine.Shaders.g.cs` | 生成文件路径。 |

生成器要求清单位于项目根目录内，并把运行时路径规范化为 `/` 分隔的项目相对路径，避免把仓库绝对路径写进程序集。Shader、Material 或参数名称映射成相同 C# 标识符时构建失败。

静态阶段验证 JSON Schema、名称/引用、默认值和安全文件边界，不创建窗口或 GL Context。它不会用正则表达式推断 GLSL；精确的 active Uniform 名称、驱动类型、数组和编译/链接错误仍由 Hosting 的真实 Program 反射负责。

## 热重载边界

v1 热重载只监视清单已声明的顶点和片元文件。修改 shaders.json 中的 Program 集合、Material Schema 或默认值后需要重新 Build 并重启；这避免运行中同时改变 Program 图和材质对象生命周期。Shader 源码本身仍支持原子热替换与失败回退。

未来如果引入可选离线 GLSL 编译器，它应接入同一个清单和结构化诊断模型，并允许按平台/驱动保留运行时复核；当前不会把某个离线编译器结果当作所有 OpenGL 驱动的最终真相。

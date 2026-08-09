# 自定义 Sprite Shader 与开发期热重载

需要多 Shader 和 Material 默认参数时，优先使用[声明式 Shader Assets](SHADER_ASSETS.md)；热重载会复用清单解析出的固定 Program 集合。

Shader 热重载面向通过 `GameInstance.Shader` / `ShaderRef` 使用的游戏自定义 Sprite Shader。Hosting 负责文件装配、Program 所有权、投影同步和安全替换；领域实例仍只保存逻辑名称，不持有 OpenGL Handle。

## 注册文件 Shader

项目把 GLSL 文件复制到输出目录：

```xml
<ItemGroup>
  <Content Include="Shaders\**\*.glsl">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
  </Content>
</ItemGroup>
```

在默认渲染器中注册：

```csharp
renderer.UseShaders(
    "Shaders",
    new ShaderFileDefinition(
        "game.player-hit",
        "sprite.vert.glsl",
        "player-hit.frag.glsl"));
```

`ShaderFileDefinition` 的顶点和片元路径相对同一个 Shader 根目录，不能使用绝对路径或通过 `..` 逃逸。逻辑名称区分大小写且不能重复。相对根目录从 `AppContext.BaseDirectory` 解析；高级开发流程也可以显式传入绝对源码根目录。

实例只引用名称：

```csharp
public Player(SpriteRef sprite)
{
    Sprite = sprite;
    Shader = new ShaderRef("game.player-hit");
}
```

Hosting 会把共享 `ShaderLibrary` 注入主 Scene Batch 和 Stencil 重绘 Batch，因此自定义 Shader 在普通场景与 Spotlight/Stencil 结果中保持一致。`Default2DGameContext.Shaders` 提供高级访问入口。

## Shader 输入约定

自定义 Sprite Shader 应与 SpriteBatch 顶点布局一致：

```glsl
layout (location = 0) in vec2 aPos;
layout (location = 1) in vec2 aTexCoord;
layout (location = 2) in vec4 aColor;
uniform mat4 uProjection;
uniform sampler2D uTexture;
```

`uProjection` 在每次 Scene 绘制前统一同步，`uTexture` 在 Program 创建和替换时显式绑定到 texture unit 0。Shader 可以不声明其中某个 uniform；驱动返回 `-1` 时引擎安全跳过。

自定义 uniform 可通过以下方式设置：

```csharp
context.Shaders.TryGet("game.player-hit")?.SetFloat("uFlash", flashAmount);
```

直接调用 `ShaderProgram.Set*` 时，Program 热替换后驱动端 uniform 值会回到默认状态，location 缓存也会清空。游戏对象需要持久参数时应创建类型化材质；材质保留 CPU 参数和逻辑 Shader 引用，新 Program 首次提交时会自动重放。完整用法见 [Shader 材质参数块](SHADER_MATERIALS.md)。

## 开启热重载

```csharp
renderer.EnableShaderHotReload(new ShaderHotReloadOptions(
    sink,
    pollInterval: TimeSpan.FromMilliseconds(250),
    debounce: TimeSpan.FromMilliseconds(250)));
```

Runner 示例：

```powershell
dotnet run --project src/MyGame.Runner -- --shader-hot-reload
```

当 Runner 使用输出目录的 `Shaders` 时，修改源码后在另一终端执行一次 Build，让 `PreserveNewest` 复制新文件：

```powershell
dotnet build src/MyGame.Runner/MyGame.Runner.csproj
```

也可以同时启用 Content 与 Shader 热重载：

```powershell
dotnet run --project src/MyGame.Runner -- `
  --content-hot-reload --shader-hot-reload
```

## 稳定快照与原子替换

```text
后台读取全部 .vert/.frag
  → 再读一次并比较 SHA-256，确认稳定快照
  → 内容指纹变化后去抖
  → Step 完成
  → 在 GL Context 线程编译所有变化 Shader
  → 全部链接成功：一次性切换全部 Program Handle
  → 清空对应 uniform location 缓存
  → 释放旧 Program
  → Draw 使用新修订
```

同一轮变化是一个事务。例如 Shader A 编译成功但 Shader B 失败时，候选 A 会被删除，A/B 的旧 Program 都继续工作。编译和链接失败不会中断 Scene，也不会让 `ShaderRef` 失效。

`IShaderHotReloadSink` 接收 `Detected`、`Applied`、`Failed` 结构化诊断，包含变化名称、组合指纹和耗时。编译失败还提供 `BuildFailure`：Shader 名称、阶段、绝对源码路径、从常见驱动日志格式解析出的首个行号及原始日志；材质契约失败提供 `ContractFailure` 和逐 Uniform issue。同一失败指纹不会每帧重复编译；任一源码变化产生新指纹后才重试。

候选 Program 链接完成后、切换 Handle 前，引擎会用 OpenGL Active Uniform 反射验证所有关联材质。删除、改名、改变类型或把普通 Uniform 改为数组都会使本轮热重载原子失败，避免画面在运行中静默变黑。

## 当前边界

- v1 的注册集合固定；运行中可修改源码，但不能新增、删除或重命名 Shader 定义。配置变化需要重启。
- 当前只管理游戏自定义 Sprite Shader。Bloom、Stencil、Tone Mapping 和 Presentation Shader 仍由各 Feature 组合根拥有，不读取外部覆盖文件。
- GL 编译必须在窗口 Context 线程执行；后台阶段只做文件读取和哈希。
- 每个 Scene Pass 会为已注册自定义 Shader 同步投影。通常游戏 Shader 数量较少；大量 Shader 时可进一步引入按需投影版本。材质参数已按材质与 Revision 延迟绑定。
- 正式发布默认不启用监测。GLSL 文件是否随 Publish 输出由项目的 Content 配置决定。

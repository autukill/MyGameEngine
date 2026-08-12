# Scene 级 Content Package 生命周期

## 目标

大型游戏不应因为进入首页就把世界地图、关卡、结算和后续章节的所有纹理一次性上传到 GPU。MyGameEngine 现在提供两种明确且互斥的内容装配方式：

- `UseContent(package)`：加载一个全局包，并在整个应用运行期间持有租约。适合小型游戏、VisualTests 和共享内容很少的工具。
- `UseContentCatalog(packagesRoot)`：只创建包目录，不预先加载根包；每个 Scene 通过 `AddScene(scene, package, configure)` 声明自己的内容包。

原有 API 保持兼容。Scene 级模式是显式选择，不会悄悄改变现有游戏的资源生命周期。

## 术语速查

| 术语 | 含义 |
| --- | --- |
| 源清单（source manifest） | 游戏仓库中的 `assets.json`，供 AssetCompiler 校验、编译和生成强类型引用。 |
| 编译包（compiled package） | `AssetsCompiled` 下的运行时清单与纹理、音频等构建产物。Runtime 只加载编译包，不读取源素材。 |
| 包目录（content catalog） | `UseContentCatalog(packagesRoot)` 指定的编译包安全根目录。Catalog 不是资源注册表，不扫描目录，也不会自动加载任何包。 |
| 聚合根（aggregate root） | 只含 `dependencies` 的顶层源清单，主要用于离线构建遍历、增量指纹和 `GameAssets` 代码生成。 |
| `ContentPackageRef` | 由生成代码提供的逻辑包引用，包含稳定包 ID 与相对 Manifest 路径；它不拥有资源，也不代表包已经加载。 |
| Scene 内容声明 | `AddScene(scene, package, configure)` 建立的“Scene 需要哪个根包”关系。Hosting 在切换时据此取得租约。 |
| `LoadedContentPackage` | 一次外部加载租约。它参与引用计数，幂等 `Dispose` 后放弃本次持有；Scene 本身不应释放 `context.Content`。 |
| 依赖持有（dependency hold） | 根包存活期间对传递依赖建立的内部引用。多个根包共享依赖时，最后一个持有消失后才卸载依赖。 |
| 常驻内容（global/eager content） | `UseContent(...)` 在 Runtime 启动时加载，并持有到应用关闭的一整套内容。 |
| Scene 级内容（scene-scoped content） | `UseContentCatalog(...)` 模式下，由当前 Scene 租约决定驻留范围的内容。 |
| 无包 Scene（package-free Scene） | 未声明 `ContentPackageRef` 的 Scene；其 `context.Content` 为 `null`，适合纯色占位、诊断或不依赖内容资产的场景。 |
| Prepare / Commit | 先在旧 Scene 有效时准备目标包；准备成功后才提交 Scene 切换。v1 的 Prepare 仍在渲染线程同步执行；使用 Fade 转场时会延迟到画面完全遮住后执行。 |

最容易混淆的是 Catalog 与聚合根：前者是运行时允许解析包路径的目录边界，后者是离线构建图的入口。调用 `UseContentCatalog()` 不等于调用 `UseContent(GameAssets.Packages.Root)`。

## 基本用法

```csharp
using var game = GameApplication
    .Create(options)
    .UseDefault2DRenderer(renderer => renderer.UseContentCatalog())
    .AddScene(
        GameScenes.Home,
        GameAssets.Packages.GameHome,
        context => HomeScene.Configure(context))
    .AddScene(
        GameScenes.World,
        GameAssets.Packages.GameWorld,
        context => WorldScene.Configure(context))
    .AddScene(GameScenes.EmptyDebug, context => DebugScene.Configure(context))
    .StartScene(GameScenes.Home)
    .Build();
```

配置回调执行时，`context.Content` 指向当前 Scene 的 `LoadedContentPackage`。没有声明包的 Scene 得到 `null`；通过生成的 `GameAssets.Sprites.*` 使用 Sprite 时，必须保证它属于当前包或其传递依赖。

类型化 Scene 同样支持内容声明：

```csharp
builder.AddScene(
    GameScenes.Level,
    GameAssets.Packages.Gameplay,
    (context, args) => LevelScene.Configure(context, args));
```

## 切换顺序

Scene 请求仍然由 Hosting 在当前固定 Step 结束后的安全边界提交。Scene 级内容切换顺序为：

1. 查找目标 Scene 声明。
2. 在旧 Scene 和旧租约仍有效时，同步加载目标包。
3. 如果加载失败，不调用 `SceneAggregate.TransitionTo`，旧 Scene 和旧租约保持有效，异常向调用方报告。
4. 目标包加载成功后，结束旧 Scene，并清理它的动态渲染效果。
5. 将 `context.Content` 切换为目标租约，再释放旧 Scene 租约。
6. 配置并启动新 Scene。

传入 `SceneTransitionOptions` 时，Hosting 先完成 Fade Out，再在完全不透明的 `Switching` 阶段执行上述 1–6；成功后从新 Scene Fade In。目标包在第 2 步加载失败时，旧 Scene 与租约保持活动，失败记录到 `SceneNavigator.LastTransitionFailure`，随后淡入恢复旧画面。转场只隐藏同步装配的视觉跳变，不会把解码或 GPU 上传搬到后台。详见[声明式 Scene 转场](SCENE_TRANSITIONS.md)。

同一包在多个 Scene 之间切换时仍经过 `ContentPackageManager` 的引用计数；共享依赖只装配一次，最后一个租约释放后才卸载。卸载顺序继续保持 Sprite、Animation、TileMap 等逻辑资源先于 Texture。

## 聚合根与运行时租约的区别

`Assets/assets.json` 仍可以作为编译聚合根。它让 AssetCompiler：

- 递归编译所有子包；
- 生成一个完整的强类型 `GameAssets` 目录；
- 计算整个构建图的增量指纹。

`UseContentCatalog` 不会加载这个聚合根。因此“根清单依赖 Home”只表示构建和代码生成可发现 Home，不表示 Home 永久驻留。真正的运行时所有权来自 Scene 上声明的 `ContentPackageRef`。

## 失败与所有权边界

- 目标包的文件缺失、解码失败、GPU 上传失败或资源命名冲突发生在旧 Scene 结束之前。
- `LoadedContentPackage` 仍是幂等租约，Scene 不应自行保存并释放 `context.Content`。
- GameInstance 不应跨 Scene 保存来自旧包的 TextureHandle；逻辑 `SpriteRef` 可以保存在数据中，但只有其包已加载时才能解析并绘制。
- package-free Scene 不能继续绘制上一个 Scene 的 Sprite；这类隐藏依赖会在迁移时暴露出来。
- Runtime 关闭时先结束 Scene、释放活动 Scene 租约，再销毁 ContentPackageManager 和 GPU Library。

## 当前限制

- v1 在渲染线程同步加载目标包，不提供 Loading Scene、后台解码或渐进 GPU 上传；Fade 转场只把同步工作安排在全遮罩阶段。
- Content Hot Reload 当前只支持 `UseContent` 的单一常驻包；Scene Catalog 模式会在构建配置阶段拒绝 Hot Reload。
- Scene 配置回调失败仍被视为应用装配错误；目标包会随 Runtime 关闭释放，但不会尝试恢复已经结束的旧 Scene。
- 不支持同一 Scene 同时声明多个根包。共享资源应放入子包并通过包依赖表达。

这些限制刻意保持 v1 边界简单。后续只有当真实游戏出现可感知加载时间时，才扩展异步 Prepare/Commit、Loading Scene 或预取。

# Scene、Prefab 与碰撞查询

这一切片把常见玩法编写从 `Program.cs` 的单次装配推进到三个可组合边界：声明式 Scene 目录、类型安全 Prefab，以及不依赖 GPU 的轻量碰撞与空间查询。

## 声明式 Scene 目录

Scene 使用稳定的逻辑引用注册：

```csharp
public static class GameScenes
{
    public static readonly SceneRef Main = new("Main");
    public static readonly SceneRef Victory = new("Victory");
}

using var game = GameApplication.Create()
    .UseDefault2DRenderer()
    .AddScene(GameScenes.Main, context => context.Scene.Add(new Player()))
    .AddScene(GameScenes.Victory, context =>
        context.Scene.Background = BackgroundConfig.FromColor(victoryColor))
    .StartScene(GameScenes.Main)
    .Build();
```

`ConfigureScene(string, configure)` 仍作为单 Scene 项目的便利入口。多 Scene 项目使用 `AddScene` 与可选的 `StartScene`；未显式选择时，第一个注册的 Scene 是初始 Scene。

实例内部通过 `SwitchScene(scene)` 发出请求，组合根代码也可以调用 `context.Scenes.SwitchTo(scene)`。请求不会在 `OnStep` 中途修改实例集合，而是在当前 Step、实例 Spawn/Destroy 提交和渲染效果事件同步完成后切换。

切换语义：

- 当前 Scene 的 `OnEnd` 和非持久实例 `OnDestroy` 先完成。
- `IsPersistent = true` 的实例保留，不会再次执行 `OnCreate`。
- Background、Layer 配置和 Scene Hook 重置，再执行新 Scene 定义。
- 新 Scene 在同一安全边界执行 `Start`，随后可以直接 Draw；新实例从下一逻辑帧开始 Step。
- 未注册 Scene 立即失败；同一目标的重复请求幂等；同一帧请求不同目标会报错。

当前实现复用同一个 `SceneAggregate`、Camera、RenderPipeline 和 GPU 根资源，因此 Scene 切换不重建 OpenGL Runtime。`SceneId` 在一次应用运行期内保持稳定，`SceneName` 表示当前逻辑定义。

## 类型安全 Instance Factory / Prefab

Prefab 是组合根中的纯实例工厂，不拥有 Texture、Shader、GL Handle 或服务容器：

```csharp
public static readonly PrefabRef<PlayerBullet> Bullet = new("player.bullet");

builder.ConfigureInstances(instances => instances.Register(
    Bullet,
    spawn => new PlayerBullet(GameAssets.Sprites.PlayerBullet, spawn.Position)));
```

实例中只保留强类型逻辑引用：

```csharp
if (KeyDown(InputKey.Space))
    Spawn(Bullet, Position + new Vector2D(0, -40));
```

目录在 `Build()` 时冻结。重名、未知名称、类型不匹配和返回 `null` 都会显式失败；创建出的实例仍走 Scene 的确定性帧末 Spawn 队列。v1 的 `PrefabSpawnContext` 只包含 Position，复杂参数暂时使用显式玩法工厂表达，不引入无类型属性字典。

## Collider 与空间查询

实例可以选择声明 Box 或 Circle Collider：

```csharp
Collider = CollisionShape2D.Box(52, 64);
Collider = CollisionShape2D.Circle(8, new Vector2D(0, -4));
```

实例内部常用查询：

```csharp
if (FirstCollision<Enemy>() is { } enemy)
    Destroy(enemy);

IReadOnlyList<Enemy> nearby = QueryRadius<Enemy>(Position, 160);
IReadOnlyList<Pickup> visible = QueryArea<Pickup>(cameraBounds);
```

Scene 也公开 `FirstCollision`、`Collisions`、`QueryArea` 和 `QueryRadius`。查询只考虑活跃且带 Collider 的已提交实例；自身碰撞查询会排除自身。

v1 规则：

- 支持 Box/Box、Circle/Circle 和 Circle/Box 精确相交。
- Position、Collider Offset 和正负 Scale 会参与计算。
- Box 保持世界轴对齐；Transform Rotation 暂不旋转 Collider。
- 非均匀缩放 Circle 时采用最大绝对缩放，避免漏判。
- 查询当前采用线性扫描，结果正确且没有索引陈旧问题；Spatial Hash 将在性能数据证明必要后置于同一查询接口后方。

完整可运行示例见 [`playgrounds/AirplaneShooter`](../playgrounds/AirplaneShooter/README.md)。

# Gameplay Cookbook

示例项目通过 `GameInputs` 集中定义逻辑 Action/Axis，并在 `ConfigureInput` 中绑定物理键位。玩法类使用 `ActionDown/ActionPressed/ActionReleased` 和 `InputAxis2D(GameInputs.Move)`；详见[逻辑 Input Actions](INPUT_ACTIONS.md)。

这些配方来自可运行 Playground，目标是让常见玩法代码短、明确且保持帧边界语义。完整项目见 [`AirplaneShooter`](../playgrounds/AirplaneShooter/README.md) 与 [`Asteroids`](../playgrounds/Asteroids/README.md)。

## 固定 Tick 与确定性随机

```csharp
EngineWindowOptions options = EngineWindowOptions.Default
    .WithFixedUpdateRate(60d);

private readonly GameplayRandom random = new(1234UL);
float speed = random.Range(80f, 120f);
bool rareSpawn = random.Chance(0.05f);
```

固定更新率定义模拟节奏，Owner-local seed 定义随机序列。不要在玩法代码中读取 `DateTime.Now`、`Stopwatch` 或 `Random.Shared`。需要重放某段生成序列时保存 `GameplayRandomState`；完整边界见 [确定性 Simulation](DETERMINISTIC_SIMULATION.md)。

## 使用 deltaTime 移动

```csharp
public override void OnStep(double deltaTime)
{
    Vector2D direction = InputAxis2D().Normalize();
    MoveBy(direction * (Speed * (float)deltaTime));
}
```

数字输入轴在对角移动时长度大于一，因此需要等速移动时先 `Normalize()`。坐标系 Y 轴向下，向上移动使用负 Y。

## 面向旋转方向移动

项目约定旋转使用弧度，正值逆时针。Sprite 默认朝上时：

```csharp
Vector2D forward = new(MathF.Sin(Rotation), -MathF.Cos(Rotation));
if (KeyDown(InputKey.Up))
    velocity += forward * (acceleration * dt);
```

## 连续射击冷却

```csharp
private readonly GameplayCooldown fire = new(0.12d);

fire.Update(deltaTime);
if (ActionDown(GameInputs.Fire) && fire.TryUse())
    Spawn(BulletPrefab, spawnArgs);
```

`ActionPressed` 适合一次性动作；`ActionDown + GameplayCooldown` 适合按住连续触发。冷却刚创建时可用，`TryUse` 在可用时原子开始计时，冷却期间不会被重复调用意外重置。

如果玩家在冷却结束前短按并松开，使用预创建的 `InputActionBuffer` 保留这次意图：

```csharp
private readonly InputActionBuffer fire = new(GameInputs.Fire, 0.12d);
private readonly GameplayCooldown cooldown = new(0.12d);

UpdateActionBuffer(fire, deltaTime);
cooldown.Update(deltaTime);
if ((ActionDown(GameInputs.Fire) || fire.IsBuffered) && cooldown.TryUse())
{
    Spawn(BulletPrefab, spawnArgs);
    fire.TryConsume();
}
```

平台跳跃可再组合 `GameplayGracePeriod` 记录最近一次着地：输入缓冲解决“按早了”，着地宽限解决“晚按了一点”。这些 owner-local 时间原语都跟随实例时间域，暂停时不会偷偷过期。完整冷却语义见 [Gameplay Cooldown](GAMEPLAY_COOLDOWN.md)。

## 带强类型参数的 Prefab

参数应是表达一次创建所需信息的不可变值，而不是通用属性字典：

```csharp
public readonly record struct LaserSpawnArgs(Vector2D Position, Vector2D Velocity);
public static readonly PrefabRef<Laser, LaserSpawnArgs> LaserPrefab =
    new("asteroids.laser");

builder.ConfigureInstances(instances => instances.Register(
    LaserPrefab,
    (in LaserSpawnArgs spawn) =>
        new Laser(GameAssets.Sprites.AsteroidsLaser, spawn)));
```

实例中调用 `Spawn(LaserPrefab, spawnArgs)`。泛型注册和 `in TArgs` 调用路径不会把 struct 参数装箱；创建结果仍在当前 End Step 后提交。

只需要 Position 时继续使用较短的 `PrefabRef<T>`：

```csharp
instances.Register(BulletPrefab,
    spawn => new Bullet(sprite, spawn.Position));
Spawn(BulletPrefab, Position);
```

## Alarm 生命周期与 Spawn/Wave 编排

仅需要“存在一段时间后销毁”时，优先复用 Behavior：

```csharp
public Bullet(...)
{
    UseBehavior(new LifetimeBehavior(1.5d));
}
```

需要到期执行对象专属回调时继续使用 Alarm。Behavior 与 Alarm 都继承 Owner 的时间域和暂停语义，但 Behavior 更适合跨对象复用完整局部能力。

```csharp
private static readonly AlarmId Invulnerability = new("invulnerability");
public override void OnCreate() => SetAlarm(Invulnerability, 0.25d);

public override void OnAlarm(AlarmId alarm)
{
    if (alarm == Invulnerability)
        CanTakeDamage = true;
}
```

Alarm 到期后先移除再回调，所以可以在 `OnAlarm` 中安全重设同一个 ID。inactive 实例的 Alarm 会暂停。

敌人波次、阶段延迟和循环出怪应使用可读、可快照的 `SpawnSequence`，不要把关卡时间线拆成一串互相重设的 Alarm：

```csharp
private static readonly SpawnSequence Sequence = new SpawnSequenceBuilder()
    .Delay(1d)
    .Wave(count: 8, intervalSeconds: 0.45d)
    .Build(SpawnSequenceRepeat.Loop, maximumConcurrent: 24);

private readonly SpawnSequencePlayer player = new(Sequence);
private readonly SpawnEmissionHandler emit;

public EnemySpawner() => emit = SpawnAsteroid;

public override void OnStep(double deltaTime)
{
    player.Update(deltaTime, CountInstances<Asteroid>(), emit);
}
```

生成回调仍由游戏拥有随机参数和 Prefab 选择；序列只负责“何时、多少次、是否循环以及并发门控”。完整 API 与快照边界见 [Spawn/Wave Authoring](SPAWN_WAVE_AUTHORING.md)。

## 有进入/更新/退出阶段的玩法状态

```csharp
private enum EnemyState { Spawning, Active }

private readonly GameplayStateMachine<EnemyState> states =
    new GameplayStateMachine<EnemyState>(EnemyState.Spawning)
        .State(EnemyState.Spawning, step: UpdateSpawning)
        .State(EnemyState.Active, enter: EnableCollision, step: UpdateActive);

public override void OnCreate() => states.Start();
public override void OnStep(double dt) => states.Update(dt);
```

在当前状态回调中调用 `states.ChangeTo(EnemyState.Active)`。旧 Step 会先结束，再执行 Exit/Enter；新状态从下一次 Update 开始 Step。状态持续时间直接读取 `states.Elapsed`，无需为每个状态额外维护计时字段。需要重新进入当前状态时使用 `Restart()`，不要依赖同状态切换的隐式副作用。

## 跨帧追踪实例

不要为了追踪目标而长期保存裸 `GameInstance` 对象。使用弱、强类型引用，在每次需要时解析：

```csharp
private readonly InstanceRef<PlayerShip> target;

public override void OnStep(double deltaTime)
{
    if (Resolve(target) is { } player)
        MoveToward(player.Position, deltaTime);
}
```

目标销毁或离开 Scene 后 `Resolve` 返回 `null`；inactive 和 persistent 实例遵循正常 Scene 生命周期。完整帧边界见 [强类型 Instance 引用](INSTANCE_REFERENCES.md)。

## 碰撞响应

当碰撞只关心玩法身份而非具体实现类型时，集中定义 Tag：

```csharp
public static readonly GameplayTag Enemy = new("actor.enemy");

// Enemy 构造函数
AddTag(GameTags.Enemy);

// Projectile Step
if (FirstCollision(GameTags.Enemy) is { } enemy)
{
    DestroySelf();
    if (enemy is IHasGameplayHealth damageable &&
        damageable.Health.ApplyDamage(1f).BecameDepleted)
    {
        Destroy(enemy);
    }
}
```

Enemy 通过 `IHasGameplayHealth` 暴露 `GameplayHealth`。Tag 负责横切身份，接口负责可受伤能力，结构体变更结果确保死亡、计分和掉落只在 `BecameDepleted` 转换时执行一次。以后新增 `FlyingEnemy` 或 `Boss` 不需要修改 Projectile。完整边界见 [Gameplay Health 与 Damage](GAMEPLAY_HEALTH.md)。仍需具体类 API 时使用 `FirstCollision<Enemy>(GameTags.Damageable)`，同时保留编译期类型和运行时身份约束。

```csharp
Collider = CollisionShape2D.Circle(5f);

public override void OnStep(double deltaTime)
{
    MoveBy(velocity * (float)deltaTime);
    if (FirstCollision<Asteroid>() is not { } asteroid) return;
    Destroy(asteroid);
    DestroySelf();
}
```

Destroy 在当前 Step 结束后提交，因此对象仍能安全完成当前回调。多个对象可能在同一帧请求销毁同一目标；重复销毁是安全 no-op，玩法代码仍应避免重复计分等非幂等副作用。

## Scene 结束与重开

```csharp
if (FirstCollision<Asteroid>() is not null)
    SwitchScene(
        GameScenes.GameOver,
        new GameOverArgs(score, survivalSeconds));

if (KeyPressed(InputKey.Enter))
    SwitchScene(GameScenes.Main);
```

`GameScenes.GameOver` 是 `SceneRef<GameOverArgs>`，其注册函数直接获得强类型快照。Scene 请求在 Step 后提交；普通实例销毁，新定义重新创建，只有明确设置 `IsPersistent = true` 的实例跨 Scene 保留。无数据切换继续使用普通 `SceneRef`。

## 世界边缘环绕

```csharp
if (x < 0f) x += worldWidth;
else if (x > worldWidth) x -= worldWidth;
if (y < 0f) y += worldHeight;
else if (y > worldHeight) y -= worldHeight;
Position = new Vector2D(x, y);
```

这属于玩法规则，不应隐藏进 Transform 或 Scene。需要反弹、夹紧或删除时，各对象可以选择不同策略。

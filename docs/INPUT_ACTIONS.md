# 逻辑 Input Actions

逻辑输入把“玩家想做什么”和“当前设备上的哪个键”分开。玩法实例只引用稳定的 `InputActionRef` 或 `InputAxis2DRef`，物理键位集中在应用组合根中装配。

## 定义玩法输入

```csharp
using GameEngine.Core.Domain.Input;

public static class GameInputs
{
    public static readonly InputAxis2DRef Move = new("player.move");
    public static readonly InputActionRef Fire = new("player.fire");
    public static readonly InputActionRef Restart = new("game.restart");
}
```

名称大小写敏感，并且同一个名称不能同时作为 Action 和 Axis 使用。建议使用 `领域.意图` 形式的稳定名称，不要把 `Space`、`WASD` 等设备细节写进名称。

## 在 Hosting 组合根绑定物理键

```csharp
using GameEngine.Core.Infrastructure.Input;

using var game = GameApplication
    .Create(EngineWindowOptions.Default)
    .ConfigureInput(input => input
        .BindAxis2D(GameInputs.Move,
            InputKey.A, InputKey.D, InputKey.W, InputKey.S)
        .BindAxis2D(GameInputs.Move,
            InputKey.Left, InputKey.Right, InputKey.Up, InputKey.Down)
        .BindAction(GameInputs.Fire, InputKey.Space)
        .BindAction(GameInputs.Restart, InputKey.Enter))
    .UseDefault2DRenderer(...)
    .ConfigureScene(...)
    .Build();
```

- 同一 Action 可以绑定多个键，任一键满足条件即为满足。
- 同一 Axis 可以绑定多套数字方向键；各套结果相加后按轴钳制到 `[-1, 1]`。
- 相反方向会相互抵消；对角线不会自动归一化。
- 重复的物理绑定、Action/Axis 名称冲突和空配置会在 `Build()` 前快速失败。
- `Build()` 生成不可变快照；之后修改调用方数组不会改变运行时绑定。

## 在 GameInstance 中查询

```csharp
public override void OnStep(double deltaTime)
{
    Vector2D direction = InputAxis2D(GameInputs.Move).Normalize();
    MoveBy(direction * (Speed * (float)deltaTime));

    if (ActionPressed(GameInputs.Fire))
        Spawn(BulletPrefab, Position);
}
```

Action 提供三种语义：

- `ActionDown`：持续按住。
- `ActionPressed`：本输入帧刚按下。
- `ActionReleased`：本输入帧刚释放。

Scene 会把共享的不可变 `InputMap` 注入已有和后续实例。实例尚未进入 Scene 时使用 Null Object：查询返回 `false` 或零向量，不需要空值判断。已经配置 Input Map 后查询未知逻辑名称会抛出明确异常，避免拼写错误静默失效。

## 性能与边界

运行时查询直接遍历构建期冻结的小型按键数组，不创建委托、临时集合或结果数组；稳态 Action/Axis 查询为零托管分配。底层 `KeyDown/KeyPressed/KeyReleased` 与接收四个按键的 `InputAxis2D` 继续保留，适合诊断工具或确实依赖物理键位的特殊逻辑。

v1 只负责键盘到逻辑 Action/数字 Axis 的映射，暂不包括手柄、模拟轴、组合键、运行时改键、输入缓冲和玩家槽位。稳定的逻辑引用已经为这些能力建立边界，后续扩展不应改变普通玩法代码。

可运行示例见 [Airplane Shooter](../playgrounds/AirplaneShooter/README.md) 和 [Asteroids](../playgrounds/Asteroids/README.md)。

# 缓动、插值与平滑运动

Gameplay Authoring 提供三组无状态、零分配辅助 API：

- `Easing.Evaluate`：把归一化进度映射为曲线值。
- `Tween`：在标量、位置、颜色或弧度角之间做有限时长插值。
- `Motion`：让持续变化的对象以最大步长或与帧率无关的半衰期平滑追向目标。

它们都位于 `GameEngine.Core.Domain.Gameplay`，不需要注册 Manager，不隐式持有实例，也不改变 Scene 生命周期。

## 选择哪一个

| 目标 | 推荐入口 |
|---|---|
| 已知起点、终点和持续时间的开门、淡入、跳跃表现 | `Tween` |
| 摄像机、跟随对象或数值持续追踪一个会移动的目标 | `Motion.Damp` |
| 以固定速度追向目标且不能越过目标 | `Motion.MoveTowards` |
| 只需要取得曲线值并驱动自定义公式 | `Easing.Evaluate` |

第一版刻意不提供全局 Tween Manager、协程、反射属性路径或自动销毁回调。游戏对象显式持有“经过时间”和起始值，暂停、Scene 切换、存档及重放语义因此保持可见。

## 有限时长 Tween

```csharp
private Vector2D _start;
private double _elapsed;

public override void OnCreate() => _start = Position;

public override void OnStep(double deltaTime)
{
    _elapsed += deltaTime;
    Position = Tween.Lerp(
        _start,
        new Vector2D(640, 360),
        _elapsed,
        duration: 0.35,
        easing: EasingKind.CubicOut);

    float progress = Tween.EasedProgress(_elapsed, 0.35, EasingKind.CubicOut);
    Color = Tween.Lerp(Vector4.One, new Vector4(1, 1, 1, 0), progress);
}
```

`Tween.Progress` 只把秒数归一化并钳制到 `[0, 1]`；`Tween.EasedProgress` 明确返回已经过曲线映射的值。这一区分可避免把同一 easing 无意应用两次。最常用的 `Tween.Lerp(from, to, elapsed, duration, easing)` 重载会在内部完成归一化和一次曲线映射。时间要求有限，`duration` 必须大于零。

旋转使用项目既有的弧度约定：

```csharp
Rotation = Tween.AngleRadians(startRotation, targetRotation, progress, EasingKind.SineInOut);
```

`AngleRadians` 总是选择跨越 `-π/π` 边界的最短路径，返回值不强制归一化；这能避免接近边界时突然绕行一整圈。

## 持续平滑与固定速度

```csharp
public override void OnStep(double deltaTime)
{
    Position = Motion.Damp(Position, player.Position, halfLife: 0.12, deltaTime);
    Rotation = Motion.DampAngleRadians(Rotation, targetAngle, 0.08, deltaTime);

    // Speed * deltaTime 是 double，可直接传入，不需要每帧强转。
    Position = Motion.MoveTowards(Position, destination, Speed * deltaTime);
}
```

半衰期表示“误差缩小一半所需的秒数”。相同总时间拆成不同帧长会得到相同结果，因此 `Motion.Damp` 适合不同 FPS/UPS 下的连续跟随。`halfLife == 0` 且时间前进时会立即到达目标；`deltaTime == 0` 始终保持当前值。

## 曲线集合与边界

`EasingKind` 当前提供：

- Linear、SmoothStep、SmootherStep。
- Sine、Quad、Cubic、Expo 的 In/Out/InOut。
- Back 和 Bounce 的 In/Out/InOut。

所有曲线都保留 `0 → 0` 与 `1 → 1` 端点。输入进度会钳制，但 Back 曲线为了产生回拉/越过效果，中间输出允许小于 0 或大于 1；用它插值透明度等严格范围值时，由游戏逻辑决定是否额外钳制。

非有限进度、非正 duration、负数或非有限的 max delta、half-life 和 delta time 会抛出参数异常，使错误在第一次调用时暴露，而不是传播为难以诊断的 NaN Transform。

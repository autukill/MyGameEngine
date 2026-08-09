# Gameplay Authoring Experience

本切片让普通 `GameInstance` 在不接触 `SceneAggregate`、事件回调或渲染基础设施的情况下完成高频玩法：变换、输入、查找、生成、销毁和计时。底层 Scene API 继续保留为组合根与高级工具入口。

## 变换与输入

`Position`、`Rotation`、`Scale` 是 `Transform` 的便利视图；旋转继续使用弧度和逆时针正方向：

```csharp
public override void OnStep(double deltaTime)
{
    MoveBy(InputAxis2D() * (Speed * (float)deltaTime));
    RotateBy((float)deltaTime);

    if (KeyPressed(InputKey.Space))
        Spawn(new Bullet(Sprite, Position));
}
```

- `Controls` 始终非空；实例尚未注入真实输入时使用无状态 Null Object。
- `KeyDown` 查询持续按住，`KeyPressed/KeyReleased` 查询当前输入帧的边沿。
- `InputAxis2D()` 默认使用 WASD，返回每轴 `-1/0/1`，不会自动归一化对角线。
- `MoveBy/RotateBy/ScaleBy` 直接更新逻辑 Transform，不创建 GPU 状态。

## 实例级 Gameplay Context

Scene 会为已添加或已排队生成的实例注入窄化 `IGameplayContext`。`GameInstance` 子类通过以下 protected API 使用它：

- `Spawn(instance)`
- `DestroySelf()` / `Destroy(instance)`
- `FindById(id)`
- `FindFirst<T>()` / `FindAll<T>()`

Context 只暴露实例生命周期和查询，不暴露 Window、RenderPipeline、ShaderLibrary 或全局服务容器。脱离 Scene 的实例调用这些操作会收到明确异常。

## 帧边界语义

通过 Gameplay Context 发起的 Spawn/Destroy 使用请求顺序队列：

```text
Alarm
Begin Step
Step                <- 请求 Spawn / Destroy
End Step
Sprite animation
提交实例变更        <- OnCreate / OnDestroy
Scene OnAfterStep
Draw
```

- 新实例在提交时执行 `OnCreate`，从下一逻辑帧开始执行 Step。
- 待销毁实例会完成当前帧的 End Step，然后在提交时执行 `OnDestroy`。
- 当前 Step 内的 `Find*` 只观察已提交实例，不观察刚排队的 Spawn/Destroy。
- 同一帧多个请求按调用顺序提交。
- `SceneAggregate.Add/Destroy` 仍是立即生效的高级 API；普通实例逻辑推荐使用 Gameplay Context。

## 轻量 Alarm

Alarm 只在实际使用的实例上延迟分配字典，inactive 实例的 Alarm 保持暂停：

```csharp
private static readonly AlarmId Lifetime = new("lifetime");

public override void OnCreate() => SetAlarm(Lifetime, 1.5d);

public override void OnAlarm(AlarmId alarm)
{
    if (alarm == Lifetime) DestroySelf();
}
```

- Delay 使用秒，必须有限且不小于零。
- Alarm 到期后先从集合移除，再调用 `OnAlarm`，因此回调中可以安全重设同一 Alarm。
- `SetAlarm` 覆盖同名倒计时；`CancelAlarm` 和 `IsAlarmSet` 用于显式管理。
- 在 Step 中设置的零延迟 Alarm 最早于下一逻辑帧触发，不会递归进入当前 Step。

## 当前边界与后续

- 本阶段不引入全局 Service Locator、协程、完整物理、导航或 UI。
- 声明式 Scene 切换、类型安全 Prefab、Box/Circle 和区域/半径查询已经落地，详见 [Scene、Prefab 与碰撞查询](SCENE_PREFABS_COLLISION.md)。
- `FindAll<T>()` 与当前空间查询创建稳定结果数组，高频大规模查询将在性能数据证明必要后透明迁移到 Spatial Hash。
- Gameplay Cookbook 和强类型 Prefab 自定义参数已经落地；下一步聚焦 Scene 参数传递与生命周期遍历分配，离线 Shader 编译方向继续暂缓。

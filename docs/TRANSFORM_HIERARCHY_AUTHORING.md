# Transform Hierarchy 创作指南

`Engine.Features.TransformHierarchy` 现在同时提供底层树和面向 Gameplay 的场景级接入。它解决飞机枪口、角色武器、Boss 部位、跟随特效与嵌套 Prefab 的相对变换，但不会把 Scene 生命周期、Step 顺序、Layer/Depth 或碰撞索引改成递归树。

## 在 GameInstance 中启用

从 `Default2DGameContext.Transforms` 取得当前场景的运行时，并在实例构造期间挂载 Behavior：

```csharp
public sealed class PlayerPlane : GameInstance
{
    private readonly TransformBindingBehavior _transform;
    private readonly TransformAnchor _muzzle;

    public PlayerPlane(SceneTransformRuntime transforms, Vector2D position)
    {
        Position = position;
        _transform = this.UseTransformHierarchy(transforms);
        _muzzle = _transform.CreateAttachment(
            "muzzle",
            new LocalTransform2D(new Vector2(0, -40), 0, Vector2.One));
    }
}
```

绑定是显式 opt-in；没有调用 `UseTransformHierarchy` 的实例继续按原来的世界坐标 API 工作。绑定后的 `GameInstance.Position/Rotation/Scale` 也仍表示世界变换，因此现有 Draw、Collider、Camera 剔除和空间查询无需迁移。

## 纯挂点

枪口、受击点、音源位置等不需要 Step、Draw 或 Collider 的对象应使用 `TransformAnchor`，不要创建空壳 `GameInstance`：

```csharp
Vector2 point = _muzzle.WorldPosition;
Spawn(BulletPrefab, new Vector2D(point.X, point.Y));
```

挂点继承 owner 的位置、旋转和缩放。由 `TransformBindingBehavior.CreateAttachment` 创建的挂点跟随 owner 的 Scene 生命周期销毁。

## 父子实例与 Reparent

子实例保存自己的 Binding，并把 Anchor 挂到父 Anchor：

```csharp
TransformBindingBehavior childTransform =
    child.UseTransformHierarchy(context.Transforms);

childTransform.LocalTransform = new LocalTransform2D(
    new Vector2(24, 0),
    0,
    Vector2.One);
childTransform.Anchor.AttachTo(parentTransform.Anchor);
```

`AttachTo` 和 `Detach` 支持两种明确模式：

- `KeepLocal`：保留局部 TRS，世界姿态随新父节点变化。
- `KeepWorld`：保持当前世界姿态，反算新的局部 TRS。

结构修改在 Hosting 的 Step 后、Draw 前统一同步。Step 中 Spawn 的嵌套实例可在同一帧 Draw 前得到正确世界变换，并从下一逻辑帧开始 Step。

## 可复用的强类型 Transform Prefab

重复使用的组合拓扑通过静态 `TransformPrefab<TParts>` 声明。Marker 类型让编译器区分 Weapon 与 Muzzle，字符串名称只承担诊断和稳定作者标识：

```csharp
private sealed class WeaponPivot { }
private sealed class Muzzle { }

private readonly record struct PlaneRig(
    TransformNodeRef<WeaponPivot> Weapon,
    TransformNodeRef<Muzzle> Muzzle);

private static readonly TransformPrefab<PlaneRig> PlanePrefab = new(
    "player-plane.rig",
    static builder =>
    {
        var weapon = builder.Attachment<WeaponPivot>(
            "weapon",
            LocalTransform2D.Identity);
        var muzzle = builder.Attachment<Muzzle, WeaponPivot>(
            "muzzle",
            new LocalTransform2D(new Vector2(0, -40), 0, Vector2.One),
            weapon);
        return new PlaneRig(weapon, muzzle);
    });
```

实例构造时绑定根节点并取得强类型 Parts：

```csharp
_rig = PlanePrefab.Instantiate(this, context.Transforms).Parts;
Vector2 firePosition = _rig.Muzzle.WorldPosition;
```

同一 owner 内的纯节点名称大小写敏感且不可重复。Builder 只在装配回调期间可用，回调结束后冻结；空名称、跨 owner 父节点或装配失败会显式拒绝，失败装配已经声明的节点会完整回滚。

如果某个部件需要独立 Step、Collider 或 Health，它仍应是独立 `GameInstance`，再显式挂到强类型节点：

```csharp
_rig.Weapon.Attach(weaponInstanceTransform, TransformReparentMode.KeepLocal);
```

销毁 Prefab 根实例时，纯节点随根释放；独立部件实例自动保持世界姿态脱离，不会被空间关系隐式销毁。

## 生命周期边界

空间父子关系不代表所有权：

- 销毁父 `GameInstance` 不会隐式销毁子实例。
- 存活子实例会自动 `Detach(KeepWorld)`，保持画面位置。
- owner 创建的纯挂点由 owner 绑定负责释放。
- Layer/Depth、Active、Visible、Step 顺序和 Collider 仍由现有 Scene 系统管理。

如果玩法需要“父对象死亡时子对象一起死亡”，应在 Gameplay/Prefab 层显式表达，而不是由 Transform Tree 猜测。

## 兼容写入与冲突规则

绑定实例仍可直接写 `Position/Rotation/Scale`。同步时运行时会把世界写入反算为 Local；如果同一帧同时写 Local 与世界值，显式 Local 写入优先。新代码在父子关系内推荐只写 `LocalTransform`，根实例和旧代码可继续写世界属性。

运行时只存 TRS，不支持 shear。非均匀缩放与旋转的某些父子组合如果无法分解为世界 TRS，会明确抛出异常；纯挂点仍可通过 `WorldMatrix` 或 `TransformPointToWorld` 使用完整仿射矩阵。`KeepWorld` 也会拒绝不可逆父矩阵。

## 当前验证

无窗口测试覆盖：

- 深度 2048 的迭代传播、负缩放和稳定 generation handle。
- Local/World 互转、`KeepLocal/KeepWorld`、循环与 shear 拒绝。
- GameInstance 父子组合、旧世界坐标写入兼容和纯挂点。
- 销毁空间父节点时保留子实例世界姿态。
- 稳态 World 读取与脏子树传播 0 B 分配。

AirplaneShooter 已用纯 `player.muzzle` 挂点替代手写 `Position + offset` 发射坐标，作为真实创作路径示例。

## 下一边界

本切片已经支持代码声明式 `TransformPrefab<TParts>`、`root → weapon → muzzle` 纯节点拓扑和强类型挂点引用，但没有改变 `IInstanceFactory` 的单实例 Spawn 协议。下一边界应由真实玩法验证是否需要“一次 Spawn 原子创建多个 GameInstance”的 Composite Prefab；在明确部件所有权、失败回滚、Scene 切换和序列化语义前，不引入无类型对象图或隐式级联销毁。

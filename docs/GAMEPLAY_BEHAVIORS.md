# Gameplay Behavior 组合

Gameplay Behavior 用于封装“可复用、依附单个实例、随实例生命周期更新，但不决定实例核心身份”的玩法能力。它的目标是减少重复生命周期代码和深层继承，不是引入 ECS。

## 适用场景

### 生命周期行为

子弹、爆炸、残影、掉落物和临时伤害区域经常重复维护 Alarm 并请求销毁。Behavior 可以把它收敛为：

```csharp
UseBehavior(new LifetimeBehavior(1.2d));
```

### 通用运动修饰

持续旋转、匀速移动、世界边缘环绕、离开场景销毁和正弦漂浮通常是可复用的局部能力：

```csharp
UseBehavior(new AngularVelocityBehavior(0.7f));
UseBehavior(new WorldWrapBehavior(worldBounds));
```

核心移动规则仍可留在 `GameInstance.OnStep`，Behavior 只承载多个对象确实共享的部分。

### 战斗状态

短暂无敌、受击闪烁、中毒、燃烧、减速和自动回血需要动态应用、刷新、叠层或驱散时，不应为每个状态动态添加 Behavior；推荐固定装配一个 BuffContainer Behavior 管理内部 Runtime。完整推演见[技能与 Buff 功能设计思考](SKILLS_AND_BUFFS_DESIGN.md)。只有其他对象确实需要查询该状态时，才应把内部状态暴露为 Tag。

### 简单自动行为

在 Behavior Context 未来扩展查询能力后，追踪子弹、自动炮塔、宠物跟随和简单巡逻也可以复用 Behavior。复杂 AI 仍应使用状态机或专门控制器。

## 与现有机制的分工

| 需求 | 推荐机制 |
| --- | --- |
| 对象核心身份和主要玩法 | `GameInstance` 子类 |
| 出生、战斗、死亡等互斥阶段 | `GameplayStateMachine<TState>` |
| 可复用的实例局部附加能力 | `GameplayBehavior` |
| Enemy、Damageable 等横切身份 | `GameplayTag` |
| 延迟触发一次回调 | `Alarm` |
| 大量对象间的统一处理 | Scene/System |
| GPU 后处理和画面效果 | RenderEffect/Pass |

Behavior 可以把 `Enemy -> FlyingEnemy -> RotatingFlyingEnemy -> PoisonRotatingFlyingEnemy` 一类继承组合，改为一个表达核心身份的类型加若干正交行为。

## 不适用场景

- 不把一个角色拆成十几个彼此隐式依赖的 Behavior。
- 不用 Behavior 管理整个 Scene 的敌人生成或关卡规则。
- 不用 Behavior 替代复杂状态机、空间索引或渲染管线。
- 对 10,000 个对象统一执行的极热逻辑，直接字段或集中 System 往往更合适。

## 第一版边界

第一版只建立确定性生命周期、强类型 Owner、暂停/时间域继承、冻结装配和零稳态分配，并内置 `LifetimeBehavior`。Behavior 必须在实例进入 Scene 前装配；暂不支持运行时增删、Behavior 依赖图、绘制钩子、编辑器序列化或自动消息总线。

先用 Airplane Shooter 与 Asteroids 的子弹生命周期验证组合模型确实减少重复代码，再根据真实用例决定是否增加运动、战斗状态、输入或查询 Context。

## API 与生命周期

行为在实例构造期装配；`UseBehavior` 返回同一对象，Owner 可以保留它来调整公开状态。`FindBehavior<T>()` 查找第一个匹配行为：

```csharp
public Bullet(...)
{
    UseBehavior(new LifetimeBehavior(1.5d));
}
```

需要访问具体 Owner API 时继承泛型版本：

```csharp
public sealed class SpinBehavior : GameplayBehavior<Asteroid>
{
    public float RadiansPerSecond { get; set; }

    public SpinBehavior(float radiansPerSecond) =>
        RadiansPerSecond = radiansPerSecond;

    public override void OnStep(double deltaTime) =>
        Owner.RotateBy(RadiansPerSecond * (float)deltaTime);
}
```

固定调度顺序如下：

1. Owner `OnCreate`，Behavior 按声明顺序 `OnCreate`。
2. 每个 Begin/Step/End 阶段先执行 Owner，再按声明顺序执行 Behavior。
3. Owner `OnDestroy`，Behavior 按声明逆序 `OnDestroy`。

一个 Behavior 对象只能绑定一个 Owner；实例一旦加入或排队进入 Scene，Behavior 集合立即冻结。Behavior 创建失败会回滚已经初始化的 Behavior、调用 Owner 清理并中止 Scene 添加。普通销毁会尝试执行全部 Behavior 清理，并汇总异常。

Behavior 与 Owner 使用同一调度判断：inactive Owner 不执行；Gameplay Owner 在暂停时冻结；Unscaled Owner 继续使用真实 `deltaTime`。`LifetimeBehavior` 到期后请求帧边界销毁，不在 Behavior 遍历中直接修改 Scene。

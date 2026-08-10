# Scene Graph 与 Transform Hierarchy 设计思考

本文记录 MyGameEngine 对 Node Tree、Scene Graph 与父子 Transform 的术语、使用场景、边界和渐进实施方向。独立 `Engine.Features.TransformHierarchy` 已完成数学/Handle 核心与 Scene/GameInstance 第一轮接入；实际 API 和样例见 [Transform Hierarchy 创作指南](TRANSFORM_HIERARCHY_AUTHORING.md)。

参考实现中，Unity 的 `Transform` 同时提供相对父节点的 `localPosition/localRotation/localScale` 与世界空间变换；PixiJS 则以 `Container` 构成 Scene Graph，让父节点的变换、可见性和透明度影响子节点。参考：[Unity Transform](https://docs.unity3d.com/ja/6000.0/ScriptReference/Transform.html)、[PixiJS Scene Graph](https://pixijs.com/8.x/guides/concepts/scene-graph)和 [PixiJS Scene Objects](https://pixijs.com/8.x/guides/components/scene-objects)。

## 术语

这类设计通常由三个概念组成：

- **Scene Graph**：用父子关系组织场景对象的树形结构。
- **Transform Hierarchy**：让节点的位置、旋转和缩放相对父节点，并计算最终世界变换。
- **Retained Mode**：引擎长期保存对象树、状态和脏标记，而不是要求游戏每帧重新描述完整界面或场景。

从设计模式看，它也是 Composite Pattern 在游戏对象上的应用：纯分组节点和有可见内容的叶节点可以通过一致的父子 API 组合。

它不是 ECS 的同义词。Node Tree 负责作者可理解的组合关系；渲染、碰撞、更新和查询仍可以使用扁平索引或专用系统批量处理。

## Local 与 World Transform

节点保存局部变换，并从父节点推导世界变换：

```text
LocalMatrix = Scale × Rotation(-radians) × Translation
WorldMatrix = LocalMatrix × ParentWorldMatrix
```

以上采用 `System.Numerics` 的行向量约定；负号保持项目在 Y 向下屏幕坐标中“正弧度视觉逆时针”的既有行为。Sprite Origin 仍在绘制几何阶段应用，不进入通用节点 Transform。

因此，子节点的世界位置不是始终等于父子 Position 简单相加。父节点旋转或缩放后，子节点的位置、方向和间距也会被组合。

第一版候选状态：

```csharp
public readonly record struct LocalTransform2D(
    Vector2 Position,
    float Rotation,
    Vector2 Scale);

public readonly struct WorldTransform2D
{
    public Matrix3x2 Matrix { get; }
    public Vector2 Position { get; }
}
```

公共 API 应同时提供明确的 Local 与 World 入口，避免 `Position` 在挂接父节点后悄然改变语义：

```csharp
weapon.Transform.LocalPosition = new(18, 3);
weapon.Transform.LocalRotation = aimAngle;

Vector2 muzzlePosition = muzzle.Transform.WorldPosition;
```

旋转继续沿用项目约定：弧度制、正值逆时针。第一版不引入 Skew；负缩放必须有确定行为。

## 典型用途

父子 Transform 对复合游戏对象特别有价值：

```text
PlayerPlane
├─ PlaneSprite
├─ LeftMuzzle
├─ RightMuzzle
├─ EngineLight
└─ ExhaustEmitter
```

- 飞机、炮塔与枪口挂点。
- 角色、武器、影子与受击特效。
- Boss 的多个可破坏部位。
- 车辆、车轮、尾气和灯光。
- 摄像机跟随挂点和世界空间提示锚点。
- 复合 Prefab 与嵌套 Prefab。
- 粒子、光源、音源或 Stencil 几何跟随 owner。

子弹在枪口生成时读取 `Muzzle.WorldPosition`；生成完成后子弹属于 Scene，而不是继续作为飞机子节点。空间来源和生命周期所有权不能因为 API 便利而被隐式绑定。

## 一棵树不应包办所有关系

成熟引擎中至少存在以下不同关系：

| 关系 | 表达内容 | 是否应与 Transform 强绑定 |
|---|---|---|
| 空间父子 | 局部位置、旋转、缩放 | 是 |
| 生命周期所有权 | 谁销毁谁、谁持有资源 | 需要显式策略，不默认等同 |
| Gameplay 更新顺序 | Step 和确定性调度 | 否，继续由 Scene 阶段控制 |
| 渲染顺序 | Layer、Depth、材质批次 | 否，继续使用现有 Layer/Depth 与 Batch |
| 碰撞/物理关系 | Collider、Joint、Constraint | 否，世界 Transform 只是输入 |
| UI 布局 | 尺寸约束、Flexbox、滚动 | 否，使用独立布局树 |

PixiJS 也提供独立 Render Layers，使逻辑父子关系不必决定最终绘制顺序。参考：[PixiJS Render Layers](https://pixijs.com/8.x/guides/concepts/render-layers)。

推荐原则是：

> 作者看到树，运行时系统使用适合自己的稳定索引。

例如开发者通过 `Player → Weapon → Muzzle` 理解对象结构；Scene 仍扁平调度 GameInstance，碰撞系统遍历 Collider 索引，渲染器按 Layer/Depth 和 GPU 状态提交。

## Size、Bounds 与 Transform 的边界

普通世界 Transform 不应拥有含义模糊的通用 `Width/Height`。对象大小可能来自完全不同的来源：

- `SpriteMetadata.LogicalSize`：绘制大小。
- Collider Shape：碰撞大小。
- `LocalDrawBounds`：Camera 剔除大小。
- Yoga/Rect Layout：UI 布局大小。

因此保持以下分离：

```text
Transform2D       Position / Rotation / Scale / Origin
SpriteMetadata    LogicalSize
Collider          Shape / Bounds
RectTransform     Width / Height / Layout constraints
```

Unity 普通 `Transform` 也没有统一 Width/Height；UI 使用独立 `RectTransform`。PixiJS 的 `width/height` 会基于局部 Bounds 调整 Scale，也不能直接作为所有游戏对象的布局模型。

## 推荐运行时结构

不把 `SceneAggregate` 改成只能递归遍历的对象树。推荐保留现有扁平实例和索引，在旁边增加专用层级：

```text
SceneAggregate
├─ GameInstance 扁平集合
├─ TransformHierarchy
│  ├─ NodeHandle / ParentHandle
│  ├─ Children
│  ├─ LocalTransform
│  ├─ CachedWorldTransform
│  └─ LocalRevision / WorldRevision
├─ Layer/Depth 索引
├─ Collision 查询
└─ Gameplay 生命周期与 Signal
```

需要支持没有 Gameplay 行为的纯分组/挂点节点，避免为了一个 `Muzzle` 坐标创建完整的可 Step `GameInstance`。`GameInstance` 可以拥有或绑定一个 Transform Node，但两者不必成为同一个类型。

世界变换采用脏传播与 Revision 缓存：

- Local Transform 改变时标记自身世界变换过期。
- 父级 World Revision 改变时，子级在读取或统一更新阶段重算。
- 不在每次属性读取时递归扫描整棵子树。
- 稳态无变更时不重算，也不产生托管分配。
- 高频批量读取使用稳定 Handle/连续存储，避免递归对象枚举器成为热路径。

## 父子 API 与重新挂载

候选 API：

```csharp
public enum ReparentMode
{
    KeepLocal,
    KeepWorld
}

parent.AddChild(child, ReparentMode.KeepLocal);
child.SetParent(otherParent, ReparentMode.KeepWorld);
child.Detach(ReparentMode.KeepWorld);
```

- `KeepLocal`：保持局部变换，世界位置可能改变。
- `KeepWorld`：保持当前视觉位置，引擎反算新的局部变换。

必须拒绝：

- 自己成为自己的父级。
- 把节点挂到自己的后代，形成循环。
- 未经显式迁移跨 Scene 挂接。
- 已销毁或失效 Handle。
- 无法安全反算的父变换，例如不可逆 Scale；需要给出清晰诊断。

Scene Step 中发生的结构修改应进入现有安全帧边界，在稳定提交点应用，避免遍历期间修改层级和破坏确定性。

## 生命周期语义

空间父子不应偷偷决定销毁行为。销毁父节点时提供显式策略：

```csharp
public enum ChildDestroyPolicy
{
    DestroyChildren,
    DetachChildrenKeepWorld
}
```

默认可以是 `DestroyChildren`，但碎片、子弹、脱落部件等场景需要 `DetachChildrenKeepWorld`。

激活和可见性也应分离：

```text
ActiveSelf       节点自身是否参与 Gameplay
ActiveInHierarchy 父级级联后的最终 Gameplay 状态

VisibleSelf       节点自身是否可绘制
VisibleInHierarchy 父级级联后的最终可见状态
```

是否继承 Color/Alpha 应在渲染节点层明确，不直接混进 `GameInstance` 的通用生命周期。动态刚体默认作为 Transform 根节点；复杂物理关系未来由 Joint/Constraint 表达，而不是任意父级非均匀缩放。

## Prefab、序列化与确定性

Transform Hierarchy 应成为嵌套 Prefab 的稳定基础：

- Prefab 构建期验证父子循环、重复节点名和挂点引用。
- 实例化时一次分配连续节点范围并建立稳定父子顺序。
- `NodeRef<T>` 或强类型挂点引用保存稳定 Handle，不长期保存可失效对象引用。
- Replay/State Hash 明确写入 Local Transform、Parent 关系和稳定遍历顺序。
- 相同输入和 Scene 请求顺序必须得到相同 Node Handle、父子顺序和 World Transform。

Sibling 顺序不能隐式决定 Gameplay Step；如果它参与视觉遮挡，也只作为同一显式 Layer/Depth 内的可选次级顺序。

## 与多 Camera、碰撞和渲染的关系

- Camera 剔除使用 World Transform 变换后的 Bounds。
- Sprite Draw 使用 World Position/Rotation/Scale，Sprite Origin 继续属于绘制资源语义。
- Collider 查询使用 World Shape；第一版父级旋转 Collider 的支持范围需要显式限定。
- 一个节点可以被多个 View 绘制，但 Transform 只计算一次，不按 View 复制。
- Render Layer/Depth 不因 SetParent 自动改变。
- Render Effect owner 可以读取挂点 World Transform，但效果 DAG 不进入 Scene Graph。

## 与 HTML/CSS、Yoga 和 FairyGUI 的关系

世界 Scene Graph 与 UI Layout Tree 都是树，但父子关系表达不同语义：

```text
World Scene Graph
    Local Transform → World Transform

UI Layout Tree
    Style/Constraints → Yoga Layout Rect → UI Transform/Paint
```

Yoga 只负责 Flexbox 布局矩形，不应直接接管世界 `GameInstance.Transform`。未来可以共享 Node Handle、Revision、事件和诊断思想，但至少保持以下模块边界：

```text
Engine Core TransformHierarchy
    世界空间组合，不依赖 Yoga

Engine.Features.UiCore / RectTransform
    UI 节点、命中、焦点和 Paint

Engine.Integrations.YogaLayout
    可选 Flexbox 布局适配

Engine.Integrations.FairyGUI
    独立可选的设计器工作流
```

## 渐进实施建议

### 阶段 0：语义与数学核心（已完成）

- 固定 Local/World、矩阵乘法顺序、Origin 与负缩放约定。
- `TransformNodeHandle`、单父级树、循环校验。
- `KeepLocal/KeepWorld` Reparent。
- 无 GameInstance 的纯挂点节点。
- 无窗口测试覆盖深层组合和世界/局部往返。

### 阶段 1：Scene 与 GameInstance 接入（已完成）

- Scene 拥有 `TransformHierarchy`。
- GameInstance 暴露 Local/World 便利 API，现有根实例保持兼容。
- 结构修改在安全帧边界提交。
- Draw、Camera 剔除和 Collider 改用 World Transform。
- 预热后无变更树保持 0 B/帧。

### 阶段 2：组合 Authoring 与嵌套 Prefab（第一版已完成）

- `TransformPrefab<TParts>`、强类型具名挂点和嵌套纯节点。
- 重名、跨 owner、回调后 Builder 使用和失败装配回滚校验。
- AirplaneShooter 已迁移纯 Muzzle 挂点；后续增加强类型嵌套 Prefab 与 Engine Effect 组合。
- 明确 Destroy/Detach、Active/Visible 级联。

### 阶段 3：高级系统桥接

- Lighting、Audio、Particle 和 Stencil 跟随挂点。
- 复合 Collider 与物理约束边界。
- Scene/Prefab 调试树和 World Matrix 诊断。
- 由真实性能数据决定连续层级存储、并行更新或大子树缓存。

## 当前决策

- Transform Hierarchy 数学核心、Scene/GameInstance、纯挂点和强类型 Transform Prefab 已完成；原子多 GameInstance Composite Prefab 等真实玩法验证后再决定。
- Spawn/Wave Authoring 已完成；下一次接入只修改世界空间组合，不夹带渲染顺序或 UI 树。
- 第一阶段只做空间层级，不夹带完整 ECS、物理、GUI、编辑器或通用序列化系统。
- 世界变换树、生命周期、渲染顺序和 UI 布局保持显式分离。
- 在进入 HTML/CSS + Yoga GUI 前，先稳定 Local/World 与节点 Handle 思想；UI 仍使用自己的 Rect/Layout 语义。

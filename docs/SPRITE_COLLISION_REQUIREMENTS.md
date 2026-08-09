# Sprite 碰撞 Authoring 后续需求

本文记录 Sprite 异形碰撞、逐动画帧碰撞区域和多区域碰撞的需求与设计思考。它是后续方向，不是当前 Gameplay Authoring 主线；现阶段继续使用已经落地的 Box/Circle Collider，不为尚未验证的复杂碰撞模型提前增加运行时成本。

## 需求是否合理

三个需求都合理，但解决的问题不同：

- **逐帧碰撞区域**用于让碰撞跟随 Sprite 动画姿态，例如攻击挥砍、角色蹲下和 Boss 不同动作。
- **同一帧多个区域**用于表达不同语义，例如 Body、Hurtbox、Hitbox、FeetSensor 和 Interaction。它比“每帧只能有一个碰撞区”更符合真实游戏需求。
- **Sprite 异形碰撞**需要区分凸多边形碰撞与按透明像素判断的 Alpha Mask。前者适合大多数 Gameplay，后者只应作为精确窄相检测的可选能力。

因此，未来的数据模型应从“一个实例拥有一个形状”演进为“一个实例引用一个碰撞 Profile；Profile 的每个动画帧包含零到多个稳定命名的区域”。

## 建议的数据边界

概念模型如下，名称不是已承诺的最终公共 API：

```text
CollisionProfile
└── Frames[]
    └── Regions[]
        ├── Id          # 跨帧稳定，例如 body、sword、feet
        ├── Role        # Body / Hurtbox / Hitbox / Sensor / Interaction
        ├── Channel     # 本区域所属类别
        ├── Mask        # 本区域希望检测的类别
        └── Shape       # Box / Circle / ConvexPolygon / optional AlphaMask
```

固定规则建议：

- 单帧 Profile 可被所有 Sprite 帧复用；多帧 Profile 的帧数必须与 Sprite 帧数一致，不做隐式猜测。
- Region ID 在 Profile 内稳定且唯一，接触结果应能返回实例与 Region ID/Role；现有实例级 `FirstCollision<T>()` 保留为便利入口。
- Box、Circle 和凸多边形是默认窄相形状；凹多边形在离线阶段拆成多个凸多边形。
- Alpha Mask 从 Content 构建管线离线生成 CPU 可查询的位集或压缩数据，不能在运行时从 GPU 回读纹理。
- Atlas 只重映射渲染 UV，不改变碰撞 Profile 的 Sprite 局部坐标。

## 查询与性能分层

未来查询建议保持三段式：

1. 使用当前帧全部区域的合并包围盒做 Broad Phase。
2. 使用 Box/Circle/ConvexPolygon 做默认 Narrow Phase。
3. 仅当双方显式要求精确像素判断时，追加 Alpha Mask 测试。

这样，多区域不会自然退化成“每次查询都遍历每个像素”。Spatial Hash 仍应藏在现有查询接口后方，并只在真实游戏遥测证明线性扫描成为瓶颈后引入。

## 必须先解决的动画时序

当前动画在 End Step 后推进。如果碰撞查询发生在 Step，而 Draw 使用推进后的帧，就可能出现“逻辑碰撞仍是旧帧、画面已经是新帧”的一帧偏差。

实现逐帧碰撞前，必须明确并测试同一逻辑帧使用的 `ImageIndex` 快照：碰撞和绘制应消费同一个已提交动画帧。可选方案是在逻辑帧开始时推进并固定快照，或显式维护 Simulation Frame；不能由碰撞系统自行读取另一个时间点的动画值。

## 延后范围与恢复条件

当前不实现：

- 逐帧 Collision Profile 和区域级接触事件。
- Polygon、凹多边形离线分解和 Alpha Mask。
- 骨骼挂点、连续碰撞检测、刚体求解或完整物理系统。
- 因上述能力而提前引入 Spatial Hash。

当 Playground 出现 Box/Circle 无法合理表达的攻击判定、动画姿态或精确命中需求时，再按以下顺序推进：逐帧多区域 Box/Circle → Role/Channel 过滤与区域接触结果 → ConvexPolygon → 可选离线 Alpha Mask。

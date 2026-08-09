# StencilMask 圆形与 Sprite Alpha 几何

StencilMasking 现在用 `StencilMaskGeometry` 表达不携带 GPU 对象的二值遮罩，支持真正的程序化圆形和 Sprite Alpha。领域 owner 只声明意图；Factory 解析 Sprite，专用 Shader 决定哪些 fragment 能写入 Stencil Buffer。

## 显式 Mask 组

`StencilMaskGroupRef` 同时提供效果 Key 和输出 Surface，是游戏代码管理遮罩的首选入口：

```csharp
var visionMasks = new StencilMaskGroupRef("vision");

this.RequestStencilMask(
    visionMasks,
    center,
    radius,
    StencilMaskState.Spotlight,
    scene.RaiseEvent);

this.ReleaseStencilMask(visionMasks, scene.RaiseEvent);
```

同组 owner 共享一个 Pass、一个 RenderTarget 和一份 `StencilMaskState`。只有需要不同 Mode、StencilRef、MaskBits、输出层或独立生命周期时才创建其他 Slot。

## 圆形 Spotlight

```csharp
this.RequestStencilMask(
    center: Input.MousePosition,
    radius: 120f,
    state: StencilMaskState.Spotlight,
    raiseEvent: scene.RaiseEvent);
```

`StencilMaskGeometry.Circle` 保存有限的世界坐标中心和正半径。遮罩阶段仍提交一个 `2r × 2r` Quad，但 `StencilMaskShader` 使用局部 UV 丢弃单位圆外的 fragment：

```glsl
vec2 p = vUv * 2.0 - 1.0;
if (dot(p, p) > 1.0) discard;
```

只有圆内 fragment 会执行 Stencil Replace，因此 corners 不会再被误写为方形。这个路径不为每个圆创建网格，也不会产生每帧 Triangle Fan 分配。

## Sprite Alpha 遮罩

```csharp
this.RequestStencilSpriteMask(
    sprite: cooldownMask,
    subImage: imageIndex,
    transform: new Transform2D(position, rotationRadians, scale),
    alphaCutoff: 0.5f,
    state: StencilMaskState.Spotlight,
    raiseEvent: scene.RaiseEvent);
```

Sprite Alpha 几何保存：

- 逻辑 `SpriteRef` 与浮点 sub-image；帧索引继续使用 SpriteLibrary 的循环规则。
- `Transform2D`；绘制坐标对应 Sprite 原点，并支持旋转、非均匀缩放和负缩放。
- `[0,1]` 的 `AlphaCutoff`；采样 Alpha 小于阈值的 fragment 被丢弃。

Factory 在修改图之前确认 Sprite 已注册。运行时通过共享 `ISpriteResolver` 解析当前帧 Texture、UV、尺寸和原点，所以单图、多图帧及 Atlas 重映射都使用同一条路径。Sprite Library 仍拥有逻辑资源，Stencil Runtime 不接管纹理生命周期。

## Pass 两阶段执行

```text
阶段 1：写遮罩
  清空 Color + Stencil
  禁止颜色写入
  Stencil Always + Replace
  StencilMaskShader 绘制 Circle 与 SpriteAlpha

阶段 2：重绘 Scene
  恢复颜色写入与 AlphaBlend
  Spotlight  -> Stencil Equal
  FogOfWarHole -> Stencil NotEqual
  SpriteShader 重绘 Scene.DrawActive
```

相同 EffectKey 的多个 owner 共享一个目标和 Pass。所有圆形先批量提交，Sprite Alpha 按纹理及 cutoff 的状态变化 Flush；多个遮罩区域形成并集。共享 owner 的 `Mode`、`StencilRef` 和 `MaskBits` 必须一致。

## 多 Mask 管理策略

少量对象具有独立创建、失活和销毁生命周期时，让每个 `GameInstance` 成为一个 owner：

```csharp
scene.Add(new VisionEmitter(visionMasks, guardA));
scene.Add(new VisionEmitter(visionMasks, guardB));
scene.Add(new VisionEmitter(visionMasks, guardC));
```

同一个 owner 管理大量同生命周期形状时，使用一次批量快照，避免为每个区域创建实例和事件：

```csharp
StencilMaskGeometry[] geometry =
[
    StencilMaskGeometry.Circle(playerPosition, 120f),
    StencilMaskGeometry.Circle(companionPosition, 72f),
    StencilMaskGeometry.FromSprite(doorMask, 0f, doorTransform)
];

this.RequestStencilMasks(
    visionMasks,
    geometry,
    StencilMaskState.Spotlight,
    scene.RaiseEvent);
```

批量请求会复制一份几何快照，调用方之后可以安全复用数组。单几何请求不创建集合；inactive 实例在描述符和快照创建前直接 no-op。Pass 在 owner 更新时缓存总几何数和是否包含 SpriteAlpha，Draw 阶段不会为每个 Mask 创建临时集合。

选择规则：

| 场景 | 推荐 |
| --- | --- |
| Mask 有独立生命周期 | 同一组下多个 GameInstance owner |
| 一批 Mask 总是一起更新/释放 | 一个 owner + `RequestStencilMasks` |
| Inside/Outside 或 Stencil 位不同 | 不同 `StencilMaskGroupRef` |
| 仅仅位置、半径或 Sprite 帧不同 | 保持同组 |

每增加一个 owner 只增加描述符和几何绘制；每增加一个 group 才会增加完整 RenderTarget、Stencil Pass 和一次 `Scene.DrawActive` 重绘，因此应优先合组。

## 显式呈现

Stencil Runtime 只发布：

```csharp
StencilMaskEffectDescriptor.MaskOutput(key) // RGBA8/Display
```

需要在屏幕显示时，由组的“锚点 owner”声明。锚点同时拥有至少一个 Mask 和 Presentation，辅助 owner 只贡献几何：

```csharp
this.RequestPresentSurface(
    visionMasks.Output,
    scene.RaiseEvent,
    layer: 100,
    blend: PresentationBlendMode.AlphaBlend);
```

锚点在 `OnDestroy` 的同一事件批次先释放 Present，再释放自己的 Stencil 请求；辅助 owner 只释放自己的 Stencil 请求。这样辅助 Mask 可以自由增删，同时不会留下引用已删除 Surface 的 Presentation。若锚点需要销毁，应同时结束组的呈现，或在同一批事件中把呈现所有权转交给另一个仍持有 Mask 的 owner。

## 当前边界

- 遮罩是硬裁剪，不支持 feather、抗锯齿覆盖率或软阴影。
- `alphaCutoff = 0` 会保留 Alpha 为零的 fragment；需要排除完全透明像素时应使用大于零的阈值。
- Stencil Pass 会重绘 `Scene.DrawActive`，不是直接裁剪任意逻辑 Surface。
- 尚不支持 Ring、Arc、RoundedRectangle、任意矢量路径或布尔差集；Cooldown UI 的后续边界见 [需求记录](COOLDOWN_UI_EFFECTS_REQUIREMENTS.md)。

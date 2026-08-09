# StencilMask 圆形与 Sprite Alpha 几何

StencilMasking 现在用 `StencilMaskGeometry` 表达不携带 GPU 对象的二值遮罩，支持真正的程序化圆形和 Sprite Alpha。领域 owner 只声明意图；Factory 解析 Sprite，专用 Shader 决定哪些 fragment 能写入 Stencil Buffer。

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

## 显式呈现

Stencil Runtime 只发布：

```csharp
StencilMaskEffectDescriptor.MaskOutput(key) // RGBA8/Display
```

需要在屏幕显示时，由 GameInstance 单独声明：

```csharp
this.RequestPresentSurface(
    StencilMaskEffectDescriptor.MaskOutput(
        StencilMaskEffectDescriptor.DefaultKey),
    scene.RaiseEvent,
    layer: 100,
    blend: PresentationBlendMode.AlphaBlend);
```

释放或销毁时应在同一事件批次同时释放 Present 与 Stencil，避免终端继续引用已删除的生产者。

## 当前边界

- 遮罩是硬裁剪，不支持 feather、抗锯齿覆盖率或软阴影。
- `alphaCutoff = 0` 会保留 Alpha 为零的 fragment；需要排除完全透明像素时应使用大于零的阈值。
- Stencil Pass 会重绘 `Scene.DrawActive`，不是直接裁剪任意逻辑 Surface。
- 尚不支持 Ring、Arc、RoundedRectangle、任意矢量路径或布尔差集；Cooldown UI 的后续边界见 [需求记录](COOLDOWN_UI_EFFECTS_REQUIREMENTS.md)。

# Runner 的 StencilMask 几何实现解读

Runner 当前并没有实现真正的圆形 StencilMask。领域 API 使用 `Center` 和 `Radius` 表达 Spotlight 意图，但渲染阶段只是根据半径绘制一个边长为 `2 × Radius` 的白色 Quad，因此实际写入 Stencil Buffer 的区域是正方形。

## 调用链

### 1. GameInstance 声明 Spotlight

`SpotlightController` 在 `OnCreate` 请求初始遮罩，并在每个 `OnStep` 使用鼠标位置更新中心：

```csharp
private void Request(Vector2D center) =>
    this.RequestStencilMask(
        center,
        radius,
        StencilMaskState.Spotlight,
        raiseEvent);
```

`SpotlightController` 不持有 Pass、RenderTarget 或 Shader，只产生 `RenderEffectRequestedEvent`。请求最终包含一个 `StencilMaskEffectDescriptor`，其中保存：

- `RenderEffectKey`
- 世界坐标中心 `Center`
- 半径 `Radius`
- `StencilMaskState`

相同 EffectKey 的多个 owner 会被聚合到共享的 `StencilMaskPass`。

### 2. Factory 装配运行时资源

`StencilMaskEffectFactory` 从 `RenderTargetPool` 租用一个全尺寸 RGBA8 + Depth24Stencil8 目标，创建 `StencilMaskPass`，并发布逻辑 Mask Surface。

遮罩结果以 AlphaBlend、合成顺序 `100` 加入 Viewport Compositor。当前 HDR Runner 中，Tone Mapping 输出使用顺序 `0`，因此 Stencil 结果会覆盖在 Tone Mapping 后的基础画面之上。

### 3. StencilMaskPass 两阶段绘制

第一阶段只写 Stencil，不写颜色：

```text
清空 Color 与 Stencil
→ ColorMaskDisabled
→ Stencil Always + Replace(1)
→ 绘制遮罩几何
```

第二阶段重新绘制 Scene：

```text
恢复颜色写入与 AlphaBlend
→ ShowInside 使用 Stencil Equal(1)
→ ShowOutside 使用 Stencil NotEqual(1)
→ 重新绘制 Scene
```

因此 `StencilMaskState.Spotlight` 只显示被遮罩几何覆盖的区域；`FogOfWarHole` 则使用相反的测试结果。

## 为什么当前是正方形

`StencilMaskPass.DrawMask` 当前执行：

```csharp
_batch.Draw(
    _whiteTextureHandle,
    center - new Vector2(radius, radius),
    new Vector2(radius * 2f, radius * 2f),
    Vector4.One);
```

对应几何为：

```text
(center.x - radius, center.y - radius)
          ┌────────────────┐
          │                │
          │   白色 Quad    │
          │                │
          └────────────────┘
                 2r × 2r
```

通用 `SpriteShader` 只执行 `texture × color`，没有根据 Alpha 或圆形距离调用 `discard`。Stencil 写入依据 fragment 是否存活，而不是最终颜色是否透明；所以即使改成带透明圆形的纹理，透明区域的 fragment 仍会写入 Stencil，结果依然是完整 Quad。

`SetMaskCircle` 和领域中的 `Radius` 目前表达的是预期语义，不代表底层已经生成圆形几何。

## 推荐的真正圆形实现

建议增加专用 `StencilShapeShader`，仅在第一阶段写遮罩时使用。它根据 Quad UV 计算单位圆，并丢弃圆外 fragment：

```glsl
vec2 p = Frag_TexCoord * 2.0 - 1.0;

if (dot(p, p) > 1.0)
    discard;
```

只有圆内 fragment 会到达 Stencil 操作并执行 Replace。第二阶段仍使用原来的 `SpriteShader` 绘制 Scene。

建议的 Pass 结构为：

```text
Mask 阶段
  StencilShapeShader + Quad + 圆形 discard

Scene 阶段
  SpriteShader + Stencil Equal/NotEqual
```

这种方式继续复用 SpriteBatch Quad，不需要为每个圆构建 Triangle Fan；后续也可以在同一 Shader 中扩展椭圆、圆角矩形或基于有符号距离的形状。

## 当前边界

- Stencil 边缘是硬裁剪，不支持羽化或软阴影。
- 多个 owner 当前共享相同 Stencil 状态，它们的覆盖区域形成并集。
- Stencil Pass 会重新绘制 Scene，并不是直接裁剪 Tone Mapping 的逻辑 Surface。
- 任意矢量路径、复杂多边形与软边 Mask 尚未实现。

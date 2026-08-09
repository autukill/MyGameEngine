# Shader 材质参数块

材质参数块解决两个常见问题：同一个 Shader 需要多套参数，以及 Shader 热替换后 Uniform 值需要自动恢复。材质只保存逻辑 `ShaderRef` 和 CPU 侧参数，不保存 Program Handle 或 Uniform Location，因此 Program 被替换后 `MaterialRef` 和参数值都保持有效。

## 创建与使用

在 Scene 装配回调中，从共享 `ShaderLibrary` 创建材质：

```csharp
var hitMaterial = context.Shaders.CreateMaterial(
        "game.player-hit.material",
        new ShaderRef("game.player-hit"),
        ShaderUniformDefinition.Float("uFlash"),
        ShaderUniformDefinition.Vector4("uOverlay"))
    .SetFloat("uFlash", 0f)
    .SetVector4("uOverlay", new Vector4(1f, 0.2f, 0.2f, 1f));

var player = new Player(GameAssets.Sprites.PlayerIdle)
{
    Material = hitMaterial.Ref
};
context.Scene.Add(player);
```

`GameInstance.Material` 存储逻辑 `MaterialRef`。材质存在时优先于 `GameInstance.Shader`；未设置材质时，原有 `ShaderRef` 路径保持不变。

参数支持 `Float`、`Int`、`Vector2` 和 `Vector4`。Schema 区分大小写，重复名称、未声明参数、类型不匹配、非有限浮点值都会立即抛出异常。`uProjection` 与 `uTexture` 由引擎拥有，不能声明为材质参数。

## Program 契约校验

`CreateMaterial` 会对已经链接的 OpenGL Program 执行 Active Uniform 反射，而不是通过文本正则猜测 GLSL：

- Schema 中的 Uniform 必须在链接结果中存在且处于 active 状态。
- GLSL 类型必须与 `Float`、`Int`、`Vector2` 或 `Vector4` 精确对应。
- v1 不支持 Uniform Array；反射到数组会给出专门诊断。

失败会抛出结构化 `ShaderContractException`，其中包含 Shader、Material 和每个 `ShaderUniformContractIssue`。失败材质不会注册。Shader 热重载也会在候选 Program 全部编译、链接后执行同样的契约校验；任何现有材质不兼容时，所有候选 Handle 都会删除，旧 Program 与旧材质继续工作。

## 动态参数

保留 `ShaderMaterial`，在 Step 阶段修改参数：

```csharp
public override void OnStep(double deltaTime)
{
    _flash = MathF.Max(0f, _flash - (float)deltaTime * 4f);
    _hitMaterial.SetFloat("uFlash", _flash);
}
```

推荐在 Step 或 Draw 提交前更新，不要在一次实例的 `OnDraw` 已经提交部分 Quad 后直接改变共享材质。参数块只在值真正变化时增加 Revision；SpriteBatch 比较 `MaterialRef + Program Handle + Revision`：

- 材质和 Revision 均未变化：不 Flush，也不重复上传 Uniform。
- 同一 Shader 切换到另一个材质：先 Flush，再绑定另一套参数。
- 当前材质参数改变：先用旧参数 Flush 已排队 Quad，再上传新参数。
- Shader Program 热替换：新 Handle 会触发重新绑定，CPU 参数自动应用到新 Program。

这使多个实例共享同一个不可变材质时接近原有 `ShaderRef` 的批处理成本，同时允许需要不同参数的对象使用不同材质。材质数量与切换顺序仍会影响 Batch Flush，建议按实际复用粒度创建材质，不要为每个完全相同的实例创建一份。

## 与直接 Shader API 的关系

底层接口继续保留：

```csharp
instance.Shader = new ShaderRef("game.player-hit");
context.Shaders.TryGet("game.player-hit")?.SetFloat("uFlash", value);
```

直接 Program Uniform 适合 Pass、一次性调试和特殊图形，但值属于当前 GL Program 状态，热替换后不会自动恢复。游戏实例需要持久或多套参数时应使用材质。

## 当前边界

- v1 不支持 Matrix、纹理/采样器参数、Uniform Array、Uniform Buffer Object 或清单驱动材质。
- 材质与 ShaderLibrary 生命周期一致；当前不提供单个材质删除或热增删 Schema。
- 参数块面向主线程的 Step/Draw 流程，不保证跨线程并发修改安全。
- 材质 Schema 中缺失或被编译器优化为 inactive 的 Uniform 会在材质装配或热重载时失败；直接 `ShaderProgram.Set*` 仍保持 OpenGL `-1` 安全跳过语义。

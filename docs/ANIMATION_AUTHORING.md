# Animation Authoring 基础切片

`Engine.Features.Animation` 在 Sprite 多帧资源之上提供命名 Clip 和确定性播放器语义。当前是独立逻辑切片，尚未自动接管 `GameInstance.ImageIndex`；游戏可以把 `AnimationPlayer.CurrentSubImage` 显式写入实例，后续再提供薄适配层。

## 注册 Clip

```csharp
var animations = new AnimationLibrary();

AnimationClipRef attack = animations.Register(
    "player.attack",
    subImages: [4, 5, 6, 7],
    framesPerSecond: 12,
    loopMode: AnimationLoopMode.Once,
    markers:
    [
        new AnimationFrameMarker(2, new AnimationEventRef("attack.hit"))
    ]);
```

- 名称严格且不可重复。
- Sub-image 可以是不连续序列，但不能为负数。
- FPS 必须有限且大于零。
- Marker 使用 Clip 内帧索引；同帧多个 Marker 保持作者声明顺序。
- Library 冻结调用方传入的 Frame/Marker 数组。

## 播放

```csharp
private readonly AnimationPlayer _animation = new(animations);
private readonly AnimationEventBuffer _events = new();

_animation.Play(attack, restart: true);

AnimationUpdateResult result = _animation.Update(deltaTime, _events);
ImageIndex = result.CurrentSubImage;

foreach (AnimationEvent item in _events.Items)
{
    if (item.Event == new AnimationEventRef("attack.hit"))
        ApplyAttackHit();
}
```

Loop Mode：

- `Once`：到达终点后保持终帧，并只产生一次 `JustCompleted` 边沿。
- `Loop`：从末帧回到首帧，或反向从首帧回到末帧。
- `PingPong`：不重复边缘帧，回到起始边缘记为一个完整周期。

播放速度支持有限非零正负数；负数从 Clip 最后一帧开始。`SetSpeed` 改变符号会反转当前方向。

## 帧事件和分配

`AnimationEventBuffer` 由调用方复用，每次 Update 自动 Clear。大 delta 跨越多个帧时不会丢失中间 Marker；Buffer 容量热身后，Update 与事件写入保持 0 B。

Marker 是表现/玩法桥接点，不是全局事件总线。游戏可以将其转换为攻击判定、声音、粒子或 Scene-local Gameplay Signal。

## 当前边界

- 尚无 `GameInstance.Animations` 自动注入或 Hosting Catalog。
- 尚无 `animations.json`、强类型生成或 Content Hot Reload。
- Clip 只引用 Sprite sub-image，不跨 Sprite 切换。
- 尚无 Blend Tree、骨骼动画、过渡图、Root Motion 或 Timeline Editor。
- 播放器使用调用方传入 delta；接入 GameInstance 后才自动继承 Pause/TimeMode。
- Marker 不序列化任意 Callback，只保存稳定逻辑名称。

下一切片是把命名 Clip 注册接入 Content/Hosting，并提供 GameInstance 薄适配，同时保持底层 `ImageIndex/ImageSpeed` 兼容入口。

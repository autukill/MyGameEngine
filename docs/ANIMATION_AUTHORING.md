# Animation Authoring

`Engine.Features.Animation` 已形成一条从声明式 Content 到 `GameInstance` 的完整使用路径：动画 Clip 绑定一个逻辑 `SpriteRef`，用任意 sub-image 序列描述播放帧，并支持 Once、Loop、PingPong、正反向播放、帧事件、状态快照和开发期热重载。

## 推荐路径：在 Content 中声明

动画和它使用的 Sprite 放在同一个 `assets.json`，也可以引用传递依赖包中的 Sprite：

```json
{
  "sprites": [
    {
      "name": "player.sheet",
      "layout": "grid",
      "texture": "player.texture",
      "frameSize": { "width": 64, "height": 64 },
      "frameCount": 8,
      "origin": { "x": 32, "y": 56 }
    }
  ],
  "animations": [
    {
      "name": "player.attack",
      "sprite": "player.sheet",
      "frames": [4, 5, 6, 7],
      "framesPerSecond": 12,
      "loop": "once",
      "markers": [
        { "frame": 2, "event": "player.attack.hit" }
      ]
    }
  ]
}
```

固定规则：

- `name`、`sprite`、非空 `frames` 和正数 `framesPerSecond` 必填。
- `frames` 是目标 Sprite 的 sub-image 索引，可不连续，但必须位于 Sprite 帧范围内。
- `loop` 可为 `once`、`loop` 或 `pingPong`，省略时默认 `loop`。
- Marker 的 `frame` 是 Clip 内部帧索引，不是 Sprite sub-image；同帧多个 Marker 保持清单顺序。
- 动画只能引用本包或传递依赖闭包中的 Sprite。

MSBuild 会生成稳定的逻辑引用：

```csharp
AnimationClipRef attack = GameAssets.Animations.PlayerAttack;
AnimationEventRef hit = GameAssets.AnimationEvents.PlayerAttackHit;
```

Atlas 是否跨页、帧是否来自多张图片，都不会改变 Animation、Sprite 或事件引用。

## GameInstance 使用

在实例构造阶段挂一次动画 Behavior，之后直接使用扩展方法：

```csharp
public sealed class Player : GameInstance, IAnimationEventHandler
{
    public Player(AnimationLibrary animations)
    {
        this.UseAnimations(animations);
        this.PlayAnimation(GameAssets.Animations.PlayerAttack);
    }

    public void OnAnimationEvent(in AnimationEvent item)
    {
        if (item.Event == GameAssets.AnimationEvents.PlayerAttackHit)
            ApplyAttackHit();
    }
}
```

Hosting 的 `Default2DGameContext.Animations` 与 Content Manager 使用同一个 `AnimationLibrary`。Prefab/Scene Factory 应把它传给需要动画的实例；运行时也可用 `context.GetAnimation("player.attack")` 做动态查找。

Behavior 自动完成以下工作：

- 把当前 Clip 的 Sprite 与 sub-image 写入 `Owner.Sprite` / `Owner.ImageIndex`。
- 播放期间把基础 `ImageSpeed` 置为 `0`，避免 Sprite 自带 FPS 与 Clip 播放器重复推进；`StopAnimation()` 时恢复原值。
- 继承 owner 的 inactive、Gameplay Pause、TimeMode 和 TimeScale 调度，不创建第二套时钟。
- 在 owner 的 Behavior Step 中按帧顺序投递 `IAnimationEventHandler`；大 delta 跨越多帧时不会丢 Marker。
- 写入 Gameplay State Hash 所需的 Clip、帧、方向、累计时间、周期、速度与完成状态。

如果需要暂停 Clip 但不释放它，可取得 Behavior：

```csharp
SpriteAnimationBehavior animation = this.RequireAnimations();
animation.Pause();
animation.Resume();
animation.SetSpeed(-1f);
```

负速度从 Clip 最后一帧开始；运行中改变速度符号会反转当前方向。

## 底层播放器

不依赖 `GameInstance` 的系统仍可直接使用 `AnimationPlayer` 和复用的 `AnimationEventBuffer`：

```csharp
var player = new AnimationPlayer(animations);
var events = new AnimationEventBuffer();

player.Play(attack, restart: true);
AnimationUpdateResult result = player.Update(deltaTime, events);
AnimationPlayerState snapshot = player.CaptureState();
player.RestoreState(snapshot);
```

Buffer 容量热身后，Update 与事件写入保持 0 B。`AnimationPlayerState` 适合确定性快照/恢复；它不序列化任意回调。

## 热重载语义

Animation 与 Texture、Sprite 在同一个 Content 修订事务中提交。活动播放器保存逻辑 `AnimationClipRef`，下一次 Update 会采用新 Sprite、帧序列、FPS、Loop 和 Marker 定义；删除正在播放的 Clip 会安全停止，而不是抛出运行时查找异常。提交失败时旧 Texture、Sprite、Animation 和租约保持不变。

## 当前边界

- 一个 Clip 绑定一个 Sprite，但 Sprite 本身可以跨任意数量的 Texture/Atlas 页。
- 尚无状态过渡图、Blend Tree、交叉淡化、骨骼动画、Root Motion 或 Timeline Editor。
- 帧事件是稳定逻辑名称；声音、粒子、命中判定或 Gameplay Signal 由游戏在 Handler 中显式桥接。
- Content 热重载会替换 Clip 定义，但不会隐式迁移游戏自定义的动画状态机。

下一步不继续扩张动画模型；优先把真实字体解析、Glyph Texture 上传和 World/SceneGui `DrawText` 接成第二条 Authoring 黄金路径。

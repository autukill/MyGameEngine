# 声明式 Scene 转场

MyGameEngine 的 Scene 切换现在有两条明确路径：

- `Scenes.SwitchTo(target)`：兼容已有代码，在当前 Step 结束后的安全边界立即切换。
- `Scenes.SwitchTo(target, transition)`：执行 `Fade Out → Switching → Fade In`，并在画面完全遮住时提交 Scene 与 Content 租约切换。

转场是 Hosting 的应用级呈现能力，不属于某个 Camera、Render View 或 GameInstance。游戏只声明目标、颜色、时长和输入策略，不持有 RenderTarget、Shader 或绘制实例。

## 基本用法

可以集中定义游戏自己的导航预设：

```csharp
internal static class GameTransitions
{
    public static SceneTransitionOptions Navigation { get; } =
        SceneTransitions.FadeThroughBlack(
            fadeOutDuration: .18,
            fadeInDuration: .22);
}
```

普通 Scene 与带参数 Scene 使用相同语义：

```csharp
context.Scenes.SwitchTo(GameScenes.WorldMap, GameTransitions.Navigation);

var args = new LevelSceneArgs(level: 12);
context.Scenes.SwitchTo(GameScenes.Level, args, GameTransitions.Navigation);
```

自定义不透明颜色：

```csharp
var transition = SceneTransitions.FadeThroughColor(
    new Vector4(.08f, .12f, .2f, 1f),
    fadeOutDuration: .25,
    fadeInDuration: .3,
    blockInput: true);
```

RGB 必须是有限的 `[0,1]`，Alpha 必须恰好为 `1`。只有完全不透明的遮罩才能保证同步装配期间不暴露半切换画面。时长必须是有限的非负秒数；`0` 表示跳过对应渐变阶段。不要传入未初始化的 `default(SceneTransitionOptions)`。

## 状态与时序

`SceneNavigator.Transition` 返回零分配值快照：

| Phase | Opacity | Scene 状态 |
| --- | ---: | --- |
| `Idle` | 0 | 没有活动转场。 |
| `FadingOut` | 0 → 1 | 旧 Scene 继续 Step 和 Draw。 |
| `Switching` | 1 | 画面完全遮住；Hosting 同步加载目标 Content，结束旧 Scene，并配置目标 Scene。 |
| `FadingIn` | 1 → 0 | 新 Scene 已启动并继续 Step 和 Draw。 |

转场计时由 Hosting 的 Step `deltaTime` 推进，不依赖 Scene 的 Gameplay Pause 或 `TimeScale`。`Current` 只在 `Switching` 成功提交时改变；`Target`、`Opacity`、`Color` 和 `BlocksInput` 可用于诊断，但普通 GameInstance 不需要自己绘制遮罩。

同一目标、参数和选项的重复请求是幂等的。活动请求期间再请求不同目标、不同参数或不同选项会抛出冲突异常，避免一帧内由多个 owner 竞争导航。已有无转场 API 的帧边界行为保持不变。

## 输入门控

默认 `blockInput: true`。从请求被 Hosting 接受起，到 Fade In 完全结束，GameInstance 看到的是中性输入：

- Key、Mouse Button、Action 和 Axis 均未按下；
- Pointer 数量为 0，滚轮增量为 0；
- Viewport Drag、Wheel、Pinch、惯性输入状态在进入门控时重置。

Scene 模拟本身不会暂停，Alarm、动画、音频和 `OnStep` 仍正常运行。需要冻结 Gameplay 时，应另外使用暂停策略；不要把视觉转场隐式等同于时间暂停。

`blockInput: false` 适合明确希望旧/新 Scene 在渐变中继续响应输入的特殊效果。它不会改变 Scene 切换的安全边界。

## 全窗口 Overlay

Hosting 在 `RenderPipeline.Execute` 和最终 Presentation 之后，把一个 AlphaBlend 全窗口 Quad 绘制到默认 Framebuffer。因此转场会一次覆盖：

- 主 Camera 与所有 Render View；
- HDR、Bloom、Tone Mapping 等最终输出；
- SceneGui；
- `Contain` 产生的 Letterbox/Pillarbox 留边。

这也是它不作为普通 Scene Pass 或 GUI Instance 的原因：转场不能被旧 Scene 内容租约卸载，也不能因 Camera、Content Rect 或 RenderScale 改变而露出边缘。

## Content 与失败边界

Scene 使用 `UseContentCatalog()` 时，目标包仍在渲染线程同步加载，但转场路径会等到 `Switching`、即遮罩完全不透明时才执行。v1 没有后台解码或异步 GPU 上传；转场隐藏了视觉跳变，不承诺消除主线程停顿。

若目标 Content 在旧 Scene 结束前加载失败：

1. 旧 Scene、旧 Content 租约与 `Current` 保持不变；
2. Hosting 记录 `SceneNavigator.LastTransitionFailure`；
3. 遮罩从全不透明 Fade In，恢复显示旧 Scene；
4. 本次目标包的部分资源由 Content Manager 原子回滚。

目标包加载成功后，Hosting 才结束旧 Scene。此后的 Scene 配置异常仍属于应用装配错误，因为旧 Scene 的实例和 SceneAudio 已经结束，v1 不尝试逆向重建它。无转场的即时切换保留原有行为：Content 加载失败直接向调用方抛出。

## BubbleTa 实例

BubbleTa 把 Home → WorldMap 和 WorldMap → Home 统一为 `.18s` Fade Out、`.22s` Fade In 的黑色转场。世界按钮的点击音使用跨 Scene 一次性 Voice，所以在 Fade Out 与目标包切换后仍可自然播放完；两套 BGM 则继续由各自 `SceneAudio` 所有权管理。

隐藏窗口 smoke 会真实经历两个阶段，并在 WorldMap 启动后的 `FadingIn` 验证新 Scene 已经提交，而不是只调用同步切换 API。

## 当前非目标

- Scene 栈、Push/Pop、历史返回和转场队列。
- 异步 Prepare、Loading Scene、进度条或渐进 GPU 上传。
- 擦除、径向、Shader、自定义纹理或 Scene 间截图混合。
- 在转场期间自动暂停 Gameplay、音频或网络系统。
- 从目标 Scene 配置失败中恢复已经结束的旧 Scene。

这些能力应由真实游戏需求分别进入后续切片，避免把第一版 Fade 变成不可预测的通用动画框架。

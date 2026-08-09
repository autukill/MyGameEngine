# Camera 跟随、Dead Zone、边界与震屏

`CameraFollowController` 是围绕单个 `Camera2D` 的轻量玩法策略对象。它不拥有 Scene、Render View 或 GPU 资源，也不会自行订阅全局事件；游戏在自己的 Step 中明确调用 `Update`。因此主 View、分屏玩家和观察 Camera 可以使用完全不同的设置。

## 最小用法

```csharp
RenderView main = context.GetRenderView(RenderViewRef.Main);

var follow = new CameraFollowController(
    main.Camera,
    new CameraFollowSettings(
        anchor: new Vector2(0.5f, 0.5f),
        deadZoneSize: new Vector2(160, 90),
        halfLifeSeconds: 0.12f,
        worldBounds: new Bounds2D(0, 0, 4_096, 2_304)));

// Scene 配置完成后立即对齐，避免第一帧从原点滑入。
follow.SnapTo(player);

// 在玩法 Step 中更新；暂停时是否调用由游戏自己决定。
follow.Update(player, deltaTime);
```

`Update` 也接受 `System.Numerics.Vector2`，适合跟随两名玩家的中点、Boss 房间中心或由玩法计算出的预测位置，不要求目标一定是 GameInstance。

## 参数语义

- `Anchor` 是 Viewport 内 `[0,1]` 的归一化位置。`(0.5,0.5)` 居中，`(0.35,0.5)` 可给角色前方留出更多画面。
- `DeadZoneSize` 使用 Viewport 像素。Camera 缩放变化后，屏幕上的容忍区大小保持一致；目标在区域内时 Camera 不移动，越界后只移动到边缘。
- `HalfLifeSeconds` 是误差减半所需秒数，与帧率无关。`0` 表示立即跟随，常用起点是 `0.08–0.2`。
- `WorldBounds` 是可选世界矩形。稳定 Camera 的可见范围会被约束在其中；若 View 比世界更大，则沿该轴居中。

Anchor 和边界计算使用无震屏的稳定 Camera 变换，支持 Zoom 与 Rotation。震屏只影响呈现，不会反向污染鼠标拾取、跟随位置或世界边界约束。

## 多 Camera

每个 Render View 创建自己的控制器：

```csharp
var playerOne = new CameraFollowController(
    context.GetRenderView(RenderViewRef.Main).Camera,
    CameraFollowSettings.Default);

var observer = new CameraFollowController(
    context.GetRenderView(new RenderViewRef("observer")).Camera,
    new CameraFollowSettings(
        anchor: new Vector2(0.5f),
        deadZoneSize: new Vector2(320, 180),
        halfLifeSeconds: 0.3f));
```

控制器没有静态当前 Camera，也不读取全局绘制上下文。Scene 切换时可以保留控制器并换目标，也可以随 Scene 的组合根一起丢弃。

## 震屏

`Camera2D.Shake(magnitude, duration)` 保留覆盖语义；新请求替换当前震屏。需要多个玩法来源共同贡献时使用：

```csharp
follow.AddShake(magnitude: 3f, durationSeconds: 0.15f); // 枪械后坐
follow.AddShake(magnitude: 8f, durationSeconds: 0.4f);  // 同帧爆炸
```

叠加震屏以平方和开根组合幅度，并保留最长剩余时间；不为每个请求创建运行时对象。`IsShaking`、`ShakeMagnitude` 和 `ShakeTimeRemaining` 可用于调试。Hosting 每个 Step 会统一推进所有 Render View 的 `Camera.Update`，游戏代码不应重复推进。

## 边界与限制

- Dead Zone 当前是轴对齐的 Viewport 像素矩形，不提供前瞻速度、轨道或多目标自动缩放。
- WorldBounds 使用旋转后 View 的保守世界 AABB，保证不会露出边界外区域，但旋转较大时可移动范围会更小。
- Controller 不决定暂停策略。暂停时不调用 `Update` 即冻结跟随；仍可单独调用 `AddShake`。
- 本切片不把目标写进声明式 Scene/RenderView 清单；动态换目标仍由玩法代码掌握。
- `Update`、约束和震屏混合在预热后保持 0 B/frame。

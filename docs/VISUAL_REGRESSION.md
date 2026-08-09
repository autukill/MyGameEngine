# GPU 像素回归测试

自动 GPU 回归用于把关键渲染行为固定为可重复的 PNG 快照。它补充现有人工 `*.VisualTests`，不替代交互式窗口验证。

## 运行方式

在仓库根目录执行：

```powershell
# 验证所有场景
dotnet run --project src/Engine.VisualRegressionTests/Engine.VisualRegressionTests.csproj -- --verify

# 只验证一个场景
dotnet run --project src/Engine.VisualRegressionTests/Engine.VisualRegressionTests.csproj -- `
  --verify --scenario sprites-origin-transform

# 明确更新受版本控制的 PNG 基线
dotnet run --project src/Engine.VisualRegressionTests/Engine.VisualRegressionTests.csproj -- `
  --update-baselines

# 调试时显示测试窗口
dotnet run --project src/Engine.VisualRegressionTests/Engine.VisualRegressionTests.csproj -- `
  --verify --visible
```

默认模式等同于 `--verify`。正常通过返回 `0`，像素差异或场景断言失败返回 `1`，无法建立 OpenGL 上下文返回 `2`。CI 可以据此把“图形能力不可用”和“渲染发生回归”分开处理。

## 基线与差异产物

- 基线位于 `src/Engine.VisualRegressionTests/Baselines`，应随代码提交。
- 只有 `--update-baselines` 可以写基线；验证模式发现基线缺失时会失败。
- 失败产物写入被忽略的 `artifacts/visual-regression`，包括 expected、actual、diff PNG 和 JSON 指标。
- 更新基线后必须立即再运行一次 `--verify`，并人工检查有意义的画面变化。

比较要求宽高完全一致。默认允许单通道差值不超过 `2`；任一通道差值超过 `8` 立即构成失败；超过软阈值的像素比例最多为 `0.25%`。当 expected 与 actual 的 alpha 都为零时，透明像素的 RGB 不参与比较。

## 确定性边界

`Engine.Testing.Visual` 提供固定时间步隐藏窗口、当前 framebuffer RGBA8 读取、PNG 编解码与容差比较。测试主机在 `OnDraw` 内执行场景的确定性推进并在交换缓冲区前截图。

`EngineWindowOptions` 为此增加：

- `IsVisible`：隐藏或显示原生窗口。
- `FramesPerSecond` / `UpdatesPerSecond`：传递给窗口调度器。
- `FixedDeltaTime`：覆盖窗口回调提供的实际 update delta。

场景仍使用真实 `GraphicsDevice`、SpriteBatch、RenderPipeline、ScenePipelineBuilder 与 OpenGL RenderTarget；测试不会维护一套假的渲染实现。

## 当前场景

1. `sprites-origin-transform`：中心、左上和自定义原点，以及旋转、非均匀缩放、负缩放、颜色与透明度。
2. `shader-program-reload`：初始自定义 Shader、整批候选中后一个编译失败时全部保留旧 Handle，以及有效 Program 替换后的确定性画面变化。
3. `stencil-owner-lifecycle`：同一 EffectKey 的两个、一个和零个 Circle owner，验证真正圆形 corners、owner 聚合、Stencil 租约回收以及基础 Presentation 持续存在。
4. `dynamic-effect-resize`：活跃 Stencil + Presentation 从 320×240 重建到 400×300，并断言池中只保留一个新尺寸租约。
5. `stencil-sprite-alpha`：用带透明中心孔的 Sprite、非均匀缩放和旋转验证 AlphaCutoff、Sprite 原点与变换后的硬裁剪。
6. `bloom-ping-pong`：覆盖 Bloom 活跃、resize 和 release；活跃时断言恰好租用 Bright/Ping/Pong 三个目标，Glow 由显式 Presentation Additive 呈现。
7. `render-surface-chain`：真实执行 SceneColor → Bloom(main).glow → Bloom(secondary).glow，断言两个效果占用六个租约，并由单一 Presentation 终端稳定组合。
8. `hdr-tone-mapping`：真实执行 RGBA16F Scene → HDR Bloom → Tone Mapping → Presentation，同时把 `SceneGui` 作为曝光后 LDR 层；覆盖 ACES、低曝光 Reinhard、resize 与效果 release。
9. `multi-render-view-lifecycle`：主 View 执行 HDR Bloom + Tone Mapping，0.75 RenderScale observer 只执行 Tone Mapping；覆盖双 View 无缝呈现、resize 后三组租约尺寸、逐 View release，以及最后所有目标均归还 Pool 的状态。

checkpoint 可以携带独立的 `PixelComparisonOptions`。Bloom 的 active 与 resized-active 使用 soft `3`、hard `12`、差异比例 `0.5%`；release 和其他场景继续使用默认容差，因此浮点采样差异不会放宽整个回归套件。

`RenderTargetPool.TotalCount` 包含活动租约和可复用缓存。结构性 owner 释放为了原子性会先创建新图、再归还旧图，因此释放过程中 Total 可能增长；无泄漏的不变量是 `LeasedCount == 0` 且 `AvailableCount == TotalCount`，最终由 Pool 的 `Dispose` 统一释放缓存。

## 新增场景

实现 `IVisualRegressionScenario`，声明唯一名称、尺寸、总帧数和 checkpoint；在 `Initialize` 中创建 GPU 资源，在 `AdvanceAndDraw` 中以帧序号驱动确定性状态，在 `Dispose` 中按组合根顺序释放资源。然后把场景加入 `Program.CreateScenarios()`，显式更新基线并复验。

场景名称与 checkpoint 名共同形成稳定基线 ID。名称一旦提交，不应仅为整理目录而改动，否则会被识别为删除旧基线并新增另一份基线。

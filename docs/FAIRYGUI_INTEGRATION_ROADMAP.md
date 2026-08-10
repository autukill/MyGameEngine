# FairyGUI 可选集成渐进路线图

本文记录将 FairyGUI 作为 MyGameEngine 可选 GUI Authoring/Runtime 的实施边界。它是规划文档，不表示当前已经引用 FairyGUI Runtime、能够加载 `.fui` Package 或绘制 FairyGUI Component。

截至 2026-08 的官方资料仍将 MonoGame 列为支持 Runtime；FairyGUI Editor 使用 Package 组织资源，可发布二进制描述文件、Atlas 和 C# 绑定代码。官方文档还列出 Rich Text、内联 Image/MovieClip、Emoji Input、Virtual List、Typing Effect 等能力。参考：[官方下载](https://www.fairygui.com/download)、[Package](https://www.fairygui.com/docs/editor/package)、[Publish](https://www.fairygui.com/docs/editor/publish)、[Rich Text](https://en.fairygui.com/docs/editor/richtext)和 [FairyGUI MonoGame SDK](https://github.com/fairygui/FairyGUI-monogame)。

## 当前优先级结论

| 工作 | 优先级 | 结论 |
|---|---|---|
| 兼容性/许可证/维护状态 Spike | P1 调研 | 尽早验证，成本必须受限，不承诺产品化 |
| Package Parser + 最小 Render Adapter | P2 | 原生 Text/Input 基础稳定后实施 |
| Root、Input、Focus、Clipping、Window 生命周期 | P2 | 形成第一个可用 GUI 闭环 |
| C# Binding Code 与 MSBuild Pipeline | P2 | 基础 Runtime 稳定后提升 Authoring DX |
| RichText/Emoji/MovieClip 完整兼容 | P2/P3 | 按真实 FairyGUI 项目验收，不自行猜测 |
| 高级组件、滤镜、粒子、模型嵌入 | P3 | 由项目需求驱动 |

FairyGUI 的“Spike 优先级高、完整实现优先级中等”。原因是需要尽早确认现成 MonoGame Runtime 能复用多少，但在 Text、Input、SceneGui 和资源桥接未稳定前，不应投入长期 Fork。

## 为什么不能直接引用 MonoGame Runtime

MyGameEngine 使用 Silk.NET + OpenGL 3.3、自有 Window/Input、SpriteBatch、TextureLibrary、RenderSurface 和 Presentation；官方 MonoGame Runtime 使用 MonoGame 类型、Content Pipeline 和 GraphicsDevice。直接引用会带来两套图形设备、Texture、Input 和生命周期，破坏现有资源所有权。

禁止方案：

- 在同一 Window 中再创建 MonoGame GraphicsDevice。
- 把 FairyGUI Texture 裸 Handle 注入 GameInstance 或 Content Manifest。
- 用屏幕截图/离屏窗口把 FairyGUI 当视频贴图显示。
- Fork 后删除许可证或失去上游版本来源。
- 解析 `.fui` 后自行实现一个“看起来类似 FairyGUI”的不完整 GUI，并仍称为 FairyGUI Runtime。

推荐方向是对 Runtime 做受控适配：保留 FairyGUI 的 Package、对象树、Controller、Gear、Transition、Text 和 Event 语义，将最底层 Texture、Mesh、Shader、Scissor、Stencil、Input、Clock 和 Asset Load 接到 MyGameEngine。能否以扩展点完成，必须通过 Spike 读取并运行真实 Runtime 代码后决定；如果只能长期维护大面积 Fork，则需要重新评估收益。

## 稳定架构边界

建议独立集成项目：

```text
Engine.Integrations.FairyGUI
├─ FairyGUI Runtime（固定版本/来源）
├─ Engine.Core
├─ Engine.Features.ContentAssets
├─ Engine.Features.Presentation
└─ Engine.Hosting

Engine.Integrations.FairyGUI.Tests
Engine.Integrations.FairyGUI.VisualTests
```

它不是 `Engine.Core` 或 `Engine.Features.Presentation` 的反向依赖。未调用 `UseFairyGui()` 时，不加载 Runtime、不解析 Package、不创建 UI Atlas/Shader/Buffer，也不改变 Input Routing。

候选 Hosting API：

```csharp
GameApplication.Create(options)
    .UseDefault2DRenderer(renderer => renderer
        .UseFairyGui(gui => gui
            .UsePackage("ui.main", "UI/Main_fui")
            .UseDefaultFont(GameFonts.UiBody)))
    .ConfigureScene(GameScenes.Main, context =>
    {
        FairyGuiContext gui = context.FairyGui;
        gui.Show(GameUi.MainHud);
    });
```

`FairyGuiContext` 暴露逻辑 Package/Component 引用和 Root 操作，不暴露 GL、MonoGame GraphicsDevice 或 FairyGUI 内部 Texture 对象。高级用户可以通过显式 Adapter Escape Hatch 获取受限 Runtime 对象，但生命周期仍归 Context。

## Presentation 与渲染顺序

默认输出到已有 `SceneGui` RGBA8/Display Surface：

```text
World/HDR → Bloom/ToneMapping ─┐
                               ├→ Presentation
FairyGUI → SceneGui Surface ────┘
```

规则：

- FairyGUI 默认不受 World Camera、HDR Exposure、Bloom 或 Lighting 影响。
- GUI 使用 Window/Viewport 像素和显式 UI Scale；不借用 Scene World Position。
- 单窗口只有一个默认 GRoot；多个 RenderView 不自动复制 GUI。
- Mirrored Viewport 仍只呈现一次 SceneGui。
- 将 FairyGUI 渲染到 World Surface、游戏内屏幕或离屏 Texture 属于后续显式高级入口。
- Adapter 必须在结束后恢复 Blend、Depth、Stencil、Scissor、Program、Texture Unit、VAO/VBO 和 Viewport，不能污染后续 Pass。

## Package 与 Content Pipeline

FairyGUI 官方 Editor 以 Package 组织资源，发布后产生描述文件和一个或多个 Atlas；二进制格式是当前推荐发布形式。集成应保留 Package 语义，而不是拆成普通 Sprite 后丢失 Component、Controller、Gear 和 Transition 数据。

建议 Content 边界：

```csharp
public readonly record struct FairyGuiPackageRef(string Name);
public readonly record struct FairyGuiComponentRef(
    FairyGuiPackageRef Package,
    string Name);
```

加载流程：

```text
FairyGUI Editor Publish
    → AssetCompiler 校验 Package/Atlas/Font/MovieClip 路径
    → 复制到 CompiledAssets
    → 生成 GameUi.Packages / GameUi.Components
    → Hosting 加载 Package Lease
    → 创建 GRoot/Component
```

固定规则：

- Package 路径受 Content Root 约束，不能逃逸。
- Package ID、名称和 Component 名称在构建期校验。
- Package 间依赖必须无环并有稳定加载顺序。
- Atlas Texture 尽量交给 TextureLibrary/统一 GPU Backend；若 Runtime 必须拥有专用 Texture，则必须单独计入诊断和 Dispose。
- `UIPackage.RemovePackage` 前必须销毁所有依赖 Component；Lease Dispose 幂等。
- Editor 源工程与发布产物分开：`.fairy`/Package XML 是 Authoring Source，运行时只复制编译产物。
- 命令行发布可以作为 MSBuild 可选步骤，但不能默认要求每次 C# 编译都启动 GUI Editor。
- Editor 发布失败、无可靠退出码或版本不匹配时，构建应保留结构化日志并拒绝发布旧/新混合产物。

## C# 绑定代码

FairyGUI Editor 支持生成 C# 绑定代码。推荐把它视为外部生成源码，再由项目包装成稳定强类型引用：

```csharp
GameUi.MainHud.CreateInstance();
GameUi.MainHud.PlayerHealth;
```

要求：

- 生成路径位于 `obj`，不要求提交大量机器生成文件。
- Namespace、类型名和成员名冲突在编译前失败。
- Package/Component 删除后旧引用必须编译失败，不能运行时返回 null。
- Generated Binder 在创建任何 Component 前注册。
- NativeAOT 不依赖运行时扫描所有绑定类型；优先显式生成注册表。
- Editor/Runtime 版本写入编译元数据和诊断快照。

## Input、Focus 与 IME

GUI Input 不能和 Gameplay Input 同时消费同一个事件却互不知道。

需要增加明确路由：

```text
Window Event
    → FairyGUI Hit Test / Focus / Capture
        → consumed: 不进入 Gameplay Action
        → unconsumed: 继续 InputMap
```

必须覆盖：

- Mouse Move/Button/Wheel、Touch/Pointer 抽象。
- Focus、Pointer Capture、Drag、Double Click。
- Keyboard Navigation、Tab、Enter、Escape。
- TextInput 与 KeyDown 分离。
- Windows 中文 IME Composition、候选窗口位置、Caret。
- Clipboard、Selection 和 Password Input。
- Window resize、DPI/UI Scale 和 Screen→GUI 坐标。
- 多 View 下 GUI 仍基于最终 Window/Presentation Slot，而不是任意 World Camera。

Gameplay Replay 默认不记录 GUI 文本输入和 Pointer Move；需要进入玩法的 UI 操作应转换成明确 Gameplay Command/Signal，而不是回放 FairyGUI 内部 Event。

## 与原生 Text Rendering 的关系

FairyGUI 官方 Runtime 已拥有自己的 Text/RichText 能力，官方 Rich Text 文档也描述了内联图片/MovieClip；完整替换为 MyGameEngine TextLayout 风险很高。因此采用分阶段策略：

1. Adapter 初期保留 FairyGUI Runtime Text 语义，确保 Package 画面一致。
2. Font 文件、Texture 上传、显存诊断和 Content 生命周期尽量复用引擎基础设施。
3. 只有 Font Metrics、Glyph Atlas、Fallback、RichText Layout 被证明兼容时，才评估共享底层 Font Backend。
4. 不为了“统一”而破坏 FairyGUI Editor Preview 与 Runtime 的像素一致性。

原生 Text 仍服务 World Text、调试、字幕和无 FairyGUI 游戏；FairyGUI Text 服务其 Component Tree。两者可以使用同一字体资产，但 Layout Cache 和控件语义可以独立。

## 富文本、Emoji、彩色文字和动图

FairyGUI 集成的目标不是重新发明这些功能，而是忠实桥接 Runtime：

- Rich Text Color/Font/Size/Link。
- `img` 内联 Package Image。
- MovieClip/动画内联资源。
- Typing Effect。
- Emoji Input 和字体/图片回退。
- Virtual List 内文本与图片的复用。

验收使用由 FairyGUI Editor 发布的真实 Package，而不是在测试里手写假的二进制描述。Native Text Roadmap 的 Sprite Emoji 和 Grapheme Reveal 可以独立存在；两套语法不要求互相解析。

## 阶段 0：受限兼容性 Spike

目标：用最少代码回答“能否合理适配”，不提交长期公共 API。

检查内容：

- 固定 FairyGUI Runtime 仓库、Commit/Tag、MIT License、第三方依赖和维护状态。
- 统计 MonoGame 类型耦合：GraphicsDevice、Texture2D、Effect、SpriteBatch、ContentManager、Input、GameTime。
- 找出 Render/Input/Resource Loader 可替换点。
- 加载官方示例的一个二进制 Package。
- 在 MyGameEngine Window 中绘制 Image、Text、Button、Mask/Clip。
- Mouse Click 和 resize。
- NativeAOT 编译探针。
- 记录 Fork Patch 行数、上游同步方式和无法复用功能。

退出条件：

- 如果需要双 GraphicsDevice、修改 Runtime 核心大面积逻辑或无法满足许可证/分发要求，则停止完整集成，只保留研究结论。
- 如果主要改动集中在 Render/Input/Loader Adapter，则进入阶段 1。

## 阶段 1：Package + 最小 Render Adapter

目标：显示由 Editor 发布的静态 Component。

实施内容：

- 固定 Runtime Source/Package 和 NOTICE。
- Binary Package、Atlas 和基础 Font Loader。
- Quad/Triangle Mesh、Texture、Color、Blend、Transform。
- Scissor Clip 与基本 Stencil Mask。
- SceneGui RenderPass、resize 和 Dispose。
- Image、Graph、Text、Component 的最小 VisualTest。

暂不实现 InputField、IME、Drag、Virtual List、MovieClip、Filter 和异步 Package。

## 阶段 2：GRoot、Input 与基础控件

目标：形成按钮、列表和窗口可以交互的第一个实用闭环。

实施内容：

- GRoot 生命周期和 UI Scale。
- Mouse/Touch、Hit Test、Focus、Capture、Wheel。
- Button、Controller、Gear、Transition。
- ScrollPane、List 与对象池。
- Window/Popup/Modal 层级。
- Gameplay Input consumed 路由。
- Pause 时默认使用 Unscaled/Render Time。

验收：Main Menu、Settings、Inventory 三个真实 Component；Scene 切换后 Package/Root 生命周期无泄漏。

## 阶段 3：MSBuild、强类型绑定与热重载

目标：让 FairyGUI 成为可维护的 Authoring 工作流，而不是手写字符串 Demo。

实施内容：

- Editor CLI/外部 Publish 产物接入 AssetCompiler。
- 增量 Fingerprint、CompiledAssets、Publish Copy。
- C# Binder 与 `GameUi` 强类型引用。
- Package Dependency、版本和路径诊断。
- 开发期 Package 热重载：后台准备、帧边界替换、失败保留旧 UI。
- Component State 迁移只覆盖显式白名单；不尝试反射复制任意控件树。

## 阶段 4：Text、IME、Emoji 与 MovieClip

目标：覆盖用户补充的中文、富文本、打字机、Emoji、彩色文字和动图场景。

实施内容：

- 中文 Font/Fallback 与 Editor Preview 对照。
- RichText HTML 子集、Link 和 Inline Image/MovieClip。
- Typing Effect 的 Grapheme/Emoji 序列验证。
- Windows 中文 IME、Caret、Selection、Clipboard。
- Emoji 输入、Sprite Emoji 和 Runtime Font Emoji 能力矩阵。
- MovieClip 时间、循环、暂停和释放。

## 阶段 5：高级控件与性能产品化

按真实项目选择：Virtual/Loop List、Pixel Hit Test、复杂 Mask、Filter、Drag/Drop、Gesture、曲线 UI、粒子/模型嵌入。每项必须有 Draw Call、Vertex、Clip、GC、Package Memory 和 CPU Update 诊断，不把官方能力清单一次性全移植。

## NativeAOT、分发与更新策略

- Runtime 版本和 Source Commit 固定在依赖元数据中。
- 若 Vendoring Source，保留 LICENSE/NOTICE 和上游 URL；Patch 独立记录，便于升级审计。
- 不使用依赖反射扫描的自动绑定；使用生成注册表。
- 发布产物包含 Runtime、字体 Native Library 和 FairyGUI Package，但不包含 Editor。
- `gameengine doctor` 检查 Runtime/Editor Export Format、Package Version、Font Native Assets 和强类型绑定产物。
- 每次升级先跑官方/自有 Package Visual Baseline，再升级产品项目。

## 风险清单

- MonoGame Runtime 与 Silk.NET/OpenGL 后端耦合程度高于预期。
- Runtime/Editor 二进制 Package 格式版本不匹配。
- 官方 MonoGame Runtime 更新频率不足，需要长期维护 Fork。
- FairyGUI Text Metrics 与原生 Text 不一致。
- Stencil/Scissor/Blend 状态污染 RenderPipeline。
- 大量 UI Atlas 与现有 Content Package 重复占用显存。
- Virtual List 之外的大控件树产生 GC 或 Update 成本。
- IME、Clipboard、DPI 和多平台窗口行为差异。
- NativeAOT 因反射、动态代码或 Native Font 依赖失败。

这些风险决定了必须先 Spike，而不是直接将 FairyGUI 放入默认 SDK 聚合包。

## 推荐首次实施动作

当前只应创建一个不进入发布包的 Compatibility Spike：

1. 固定官方 MonoGame Runtime Source Revision 和许可证。
2. 编译其纯逻辑 Package/Object Tree 部分。
3. 列出必须替换的 MonoGame Render/Input/Content 类型。
4. 用 MyGameEngine GL Window 显示一个官方 Package 的 Image + 中文 Text + Button。
5. 测试 Click、resize、clip、Dispose 和 NativeAOT 编译。
6. 输出继续/停止决策和维护成本估算。

在 Spike 通过前，不创建稳定 `UseFairyGui()` 公共 API，不修改默认 Hosting，不让任何现有 Playground 依赖 FairyGUI。

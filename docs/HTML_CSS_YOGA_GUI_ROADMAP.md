# HTML/CSS、Yoga 与游戏 GUI 兼容性 Spike

本文记录 2026-08 对开发者优先 GUI Authoring 的技术调研和阶段决策。它是 Compatibility Spike 与路线文档，**不表示当前引擎已经能够解析 HTML/CSS、运行 Yoga/RmlUi/FairyGUI 或创建 GUI 控件树**。

## 目标

希望游戏开发者能使用结构清晰、适合版本控制和热重载的声明式文件编写 HUD、菜单与调试界面，同时保持：

- Silk.NET/OpenGL 3.3 渲染后端不被 MonoGame 或浏览器内核接管。
- NativeAOT、自包含发布和可选 Feature 边界。
- `SceneGui` RGBA8/Display Surface 与 Presentation 顺序。
- Texture、字体、RenderTarget 和 Native Runtime 的显式所有权。
- UI 输入消费与 Gameplay Logical Input 分离。
- 无 GUI 游戏不携带 GUI Native Library 或额外资源。

## 候选方案结论

| 候选 | 定位 | Spike 结论 | 当前决策 |
|---|---|---|---|
| Yoga | 只计算 Flexbox Layout | 适配面最小，但不提供控件、Markup、CSS 层叠、绘制或输入 | **绿色：允许进入后续 NativeAOT/C ABI 实验** |
| RmlUi | 面向游戏的 RML/RCSS UI Runtime | 功能最接近开发者优先 HTML/CSS GUI，但需要 C++ Bridge 和完整 Render/Input/Lifetime Adapter | **黄色：作为首选完整 Runtime 候选继续验证** |
| FairyGUI MonoGame | 设计器优先 GUI Runtime | Authoring 成熟，但现有 Runtime 直接依赖 MonoGame 语义，不能作为 Silk.NET 后端即插即用 | **黄色：保留可选设计器路线，需限定 Fork 成本** |
| 自研 HTML/CSS Runtime | 完全自控 | 长期维护 DOM、CSS、控件、滚动、焦点和文本成本过高 | **暂缓：只在现成候选均失败后重评** |
| 浏览器内核 | 完整 Web Runtime | 能力最完整，但体积、进程、GPU、输入、安全和 NativeAOT 边界不适合默认游戏 Runtime | **拒绝默认运行时；未来仅考虑编辑器/工具** |

## Yoga Spike

Yoga 官方将其定义为可嵌入、面向 Web 标准的高性能布局引擎；主实现当前使用 C++20 和 CMake，许可证为 MIT。参考：[Yoga 官方仓库](https://github.com/facebook/yoga)、[Yoga 文档](https://www.yogalayout.dev/)和 [MIT License](https://raw.githubusercontent.com/facebook/yoga/main/LICENSE)。

### Yoga 能提供

- Flex Direction、Grow、Shrink、Basis 与 Wrap。
- Justify、Align、Margin、Padding、Gap。
- 固定、百分比、最小/最大尺寸。
- 相对和绝对定位。
- 脏节点布局重算。
- 叶节点 Measure Callback，可连接引擎 Text Layout。

### Yoga 不提供

- HTML/Markup Parser 或 DOM。
- CSS Parser、Selector、Cascade、Variable。
- Panel、Image、Text 或 Border 绘制。
- 字体加载、文字塑形、Glyph Atlas。
- Hit Test、Pointer Capture、Focus、IME、Clipboard。
- ScrollView、Virtual List 或控件行为。
- Transition、Animation、Accessibility。

因此 Yoga 的准确定位是：

```text
UiNode + Style
      ↓
Yoga Adapter
      ↓
Layout Rect
      ↓
Engine Paint / Input / Text
```

不能把“使用 Yoga”宣传成“支持浏览器 HTML/CSS”。

### Yoga 后续可执行实验

创建隔离的 `Engine.Integrations.YogaLayout`，只验证：

1. 固定 Yoga Commit/Tag、许可证与 Windows/Linux/macOS 构建产物。
2. 提供极薄稳定 C ABI；C# 不直接绑定易变的 C++ 类型。
3. NativeAOT `LibraryImport`、Trim 和自包含发布。
4. 1,000 节点 Row/Column/Wrap 布局和脏子树重算。
5. 原生 TextRendering Measure Callback。
6. Dispose、错误路径和重复初始化/关闭。
7. DPI、像素取整、RTL 与零尺寸节点。

只有三平台 NativeAOT 产物和生命周期测试通过后，才建立稳定 `UseYogaLayout()` API。

## RmlUi Spike

RmlUi 是面向游戏和实时应用的 C++ HTML/CSS 风格 UI Runtime。官方集成文档要求应用提供 Render Interface；还允许替换 System、File、Font Engine 和 Text Input Handler。参考：[Integration](https://mikke89.github.io/RmlUiDoc/pages/cpp_manual/integrating.html)、[Custom Interfaces](https://mikke89.github.io/RmlUiDoc/pages/cpp_manual/interfaces.html)和 [Render Interface](https://mikke89.github.io/RmlUiDoc/pages/cpp_manual/interfaces/render.html)。

### 与当前引擎匹配的地方

- 自带文档树、RML/RCSS、事件、控件和滚动语义，不需要从零实现 CSS Runtime。
- Render Interface 由宿主提供，理论上可接入 Silk.NET/OpenGL。
- Texture Load/Generate/Release 可以映射到 Content/Texture 所有权。
- File、Clock、Cursor、Text Input 和 Font Engine 均存在显式适配点。
- Context 尺寸适合映射一个 `SceneGui` 或独立 GUI RenderSurface。
- 官方提供 GL3 Backend 和 Visual Test，可作为像素适配基线。

### 关键风险

- C++ Runtime 需要稳定 C ABI Wrapper，不能直接由 C# P/Invoke 虚函数接口。
- 官方 Render Interface 使用顺序提交、Compiled Geometry 和 Texture Handle；需要桥接现有 Batch 或独立 UI Mesh Batch。
- 基础渲染已要求纹理生成/释放和 Scissor；圆角裁剪、Filter、Mask、Shadow 还会要求 Clip Mask、Layer、Render Texture 与 Shader 能力。
- 官方约定 UI 颜色/纹理为 sRGB、预乘 Alpha；当前 Sprite/SceneGui 状态必须显式保存和恢复，不能假设 Blend 相同。
- 默认字体引擎依赖 FreeType；复杂中文塑形仍需验证 HarfBuzz 或自定义 Font Engine，不能仅凭“UTF-8 输入”推断排版完整。
- RmlUi 持有自定义接口的 non-owning pointer，关闭顺序必须是 Context/Runtime → Adapter → Engine GPU Resources。
- NativeAOT、跨平台 Native Binary、异常边界和 Debugger Plugin 体积尚未实测。

### 下一轮 RmlUi 验收

- 固定版本并构建最小 Core + FreeType/自定义 Font 两个变体。
- C ABI 只暴露 Context、Document、Input、Update、Render 和 Diagnostic Handle。
- Render Adapter 完成三角形、Texture、Scissor、Premultiplied Alpha。
- 将最小 RML 文档绘制到 `SceneGui`，resize 后重新布局。
- 鼠标 Move/Down/Up、Wheel、Keyboard、Text Input 和 Focus 往返。
- 真实中文 Font 与换行截图。
- NativeAOT Publish、重复创建/销毁和失败回滚。
- 与官方 GL3 Visual Test 的最小基线比较。

如果必须大面积修改 RmlUi Core、无法稳定包装异常/所有权，或高级裁剪要求重写现有渲染管线，则停止产品化。

## FairyGUI MonoGame Spike

FairyGUI 官方仍列出 MonoGame Runtime，仓库声明 MIT，并以 FairyGUI Editor 发布内容作为工作流。参考：[FairyGUI 下载页](https://www.fairygui.com/download)和 [FairyGUI MonoGame SDK](https://github.com/fairygui/FairyGUI-monogame)。

现有 Runtime 的问题不是 FairyGUI 能力不足，而是宿主图形抽象不同：MonoGame 的 `GraphicsDevice`、`Texture2D`、`SpriteBatch`、Effect、Content Pipeline 和 XNA 数据类型不能直接注入当前 Silk.NET/OpenGL 组合根。

因此只有两种诚实路径：

1. 找到足够窄且稳定的 FairyGUI 底层渲染/资源接口并实现 Adapter。
2. 维护受控 Fork，把 MonoGame 类型隔离到 Engine Adapter。

不能采用：

- 同时启动第二套 MonoGame GraphicsDevice。
- 把 FairyGUI 截图成视频纹理。
- 把 Package 拆成普通 Sprite 并丢失 Component/Controller/Gear/Transition 语义。
- 自己解析部分 `.fui` 后仍称为兼容 FairyGUI Runtime。

FairyGUI 的产品化门槛继续沿用 [FairyGUI 可选集成路线图](FAIRYGUI_INTEGRATION_ROADMAP.md)：真实 Editor Package、Render/Input/Loader Adapter、中文富文本、NativeAOT 和可接受 Fork 面积全部通过。

## 推荐架构边界

无论最终选择 Yoga、RmlUi 或 FairyGUI，都保持以下分层：

```text
Engine.Features.TextRendering
    Font / Layout / Glyph Atlas

Engine.Features.UiCore
    UiNode / Event / Focus / HitTest / Paint Contract

Engine.Integrations.YogaLayout
    只做 Flexbox Rect

Engine.Integrations.RmlUi
    可选完整 RML/RCSS Runtime

Engine.Integrations.FairyGUI
    可选设计器 Package Runtime
```

不要求三个 Integration 同时产品化。`UiCore` 也不应为了适配某一个候选而泄露其 Native Handle。

## 与世界 Scene Graph 的关系

世界节点树和 UI 布局树使用相似的数据结构，但表达不同语义：

```text
World Transform Hierarchy
    Local Transform × Parent World → World Transform

UI Layout Tree
    Style + Parent Constraints → Layout Rect → Paint Transform
```

Yoga 不接管 `GameInstance.Transform`；RmlUi/FairyGUI 的 Component Tree 也不进入 Scene 的 Gameplay Step 或 Collider 索引。完整边界见 [Scene Graph 与 Transform Hierarchy 设计思考](SCENE_GRAPH_TRANSFORM_HIERARCHY.md)。

## 当前决策与顺序

1. 先完成原生 TextRendering 基础，建立可测试的 Measure/Glyph/Atlas 边界。
2. Yoga 进入最小 NativeAOT/C ABI 实验，目标只是验证布局内核。
3. 同期对 RmlUi 做最小 Render/Input/中文文字实验，它是开发者优先完整 GUI 的首选候选。
4. FairyGUI 继续作为设计器优先的可选路线，只有 Fork 面积可控才接入。
5. 在 Yoga 自研 UiCore 与 RmlUi 之间做一次 Go/No-Go：不同时建设两套完整 HTML/CSS Runtime。
6. 完整浏览器内核不进入游戏默认 Runtime。

## Go/No-Go 指标

| 指标 | Go 条件 |
|---|---|
| NativeAOT | Windows/Linux/macOS 自包含发布成功，无反射/Trim 漏洞 |
| 所有权 | 初始化失败可回滚，重复 Dispose 安全，无 Native/GPU 泄漏 |
| 渲染 | SceneGui 状态恢复正确，resize/clip/alpha 基线稳定 |
| 中文 | 真实字体、Fallback、换行和输入至少通过一个代表性样例 |
| 输入 | Pointer、Wheel、Keyboard、Text Input、Focus 消费边界明确 |
| 资源 | Texture/Font/Document 不逃逸安全 Root，热重载失败保留旧修订 |
| 性能 | 静态树不逐帧重排；1,000 节点更新和 Paint 有可接受基线 |
| 维护 | Adapter/Fork 面积固定且能跟随上游升级，不复制整个 Runtime |

任一候选连续两轮无法满足 NativeAOT、所有权或中文文本三项硬门槛，应停止该候选，而不是继续向默认 SDK 扩散技术债。

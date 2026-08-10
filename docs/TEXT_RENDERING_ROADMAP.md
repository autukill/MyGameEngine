# 中文字体、文本绘制与富文本渐进路线图

本文记录 MyGameEngine 原生 Text Rendering 的长期边界和实施顺序。它是规划文档，不表示 API 已实现。目标是在不依赖完整 GUI 框架的情况下，先让世界空间文字、SceneGui、字幕、对话、伤害数字和调试信息正确显示中文，再渐进加入富文本、打字机、Emoji 和内联动画。

原生 Text 与可选 FairyGUI 的关系：Text Rendering 是引擎基础能力；FairyGUI 是可选 GUI Adapter。两者可以并存，但不能让 FairyGUI 成为世界空间文字、运行时诊断或非 FairyGUI 游戏的强制依赖。

## 当前优先级

| 能力 | 优先级 | 原因 |
|---|---|---|
| 中文字体加载、Glyph Atlas、基础绘制 | P1 高 | 几乎所有本地化游戏、字幕和调试都需要 |
| Unicode Layout、Fallback、换行和对齐 | P1 高 | 决定中文混排和多语言是否可靠 |
| 彩色文字与受限富文本 | P1/P2 | 建立在稳定 Layout 上，开发价值高 |
| Grapheme-aware 打字机效果 | P2 | 对话常用，但不能先于 Unicode Cluster |
| Sprite Emoji 与 Unicode Emoji 映射 | P2 | 可先复用 Sprite/Atlas，绕开复杂彩色字体格式 |
| 彩色 Emoji Font | P3 | COLR/CPAL、CBDT/CBLC、SBIX 等后端复杂 |
| 内联 Sprite/MovieClip 动画 | P2/P3 | 依赖 Rich Layout、Sprite 动画和固定 Inline Box |
| GIF/Animated WebP 直接解码 | P3 | 不是文本系统第一矛盾，优先转成 Sprite 动画资产 |
| IME、选择、Caret、Clipboard | P2 | 文本输入与 FairyGUI 产品化的前置项，不是纯绘制前置项 |

这里的 P1 表示完成当前 Gameplay Signals 与紧邻的 Spawn/Wave 小切片后，应进入高优先级候选队列；不表示必须抢在 Animation、Tilemap 和 Audio 之前一次性完成全部文本能力。

## 设计原则

- 接收 .NET `string`，内部按 Unicode Scalar、Grapheme Cluster、Glyph Run 分层处理，不能按 UTF-16 `char` 直接排版或揭示。
- 中文字体不能预烘焙全部 CJK 字符；需要动态 Glyph Atlas 或按本地化语料构建子集。
- Layout 与 Draw 分离。相同文本、Style 和 Wrap Width 可以缓存 Layout，而 Camera、颜色或位置变化不必重新 Shape。
- World Text 与 SceneGui Text 共享 Font/Layout/Glyph Atlas，只切换投影和裁剪空间。
- 富文本解析器使用受限、版本化语法，不执行脚本、不访问任意文件或网络。
- Inline Image/Animation 使用逻辑 `SpriteRef`，不暴露 Texture Handle；继续兼容 Atlas 与多图片动画。
- Typewriter 按 Grapheme/Inline Node 揭示，不能拆开代理项、组合音标、Emoji ZWJ 序列或富文本标签。
- 文本绘制默认属于表现层；Gameplay 不读取 Glyph 或屏幕像素决定规则。
- 所有字体、Atlas Page、Layout Cache 和外部 GUI 资源都有明确所有权、预算与诊断。

## 建议的数据模型

候选基础引用：

```csharp
public readonly record struct FontRef(string Name);

public readonly record struct TextStyle(
    FontRef Font,
    float Size,
    Vector4 Color,
    float LetterSpacing = 0f,
    float LineSpacing = 0f);

public readonly record struct TextLayoutOptions(
    float MaxWidth,
    TextHorizontalAlignment HorizontalAlignment,
    TextVerticalAlignment VerticalAlignment,
    TextWrapMode WrapMode,
    TextOverflowMode OverflowMode);
```

候选绘制入口：

```csharp
text.Draw(
    "你好，世界！",
    position,
    TextStyle.Default(GameFonts.UiBody, 24f));

using TextLayout layout = text.Layout(content, style, options);
text.Draw(layout, position, colorOverride);
```

`TextLayout` 保存不可变行、Run、Glyph Position 和 Cluster Mapping，不保存 RenderTarget 或当前 Camera。高频变化文本可以使用调用方持有的 `TextLayoutBuffer`，避免每帧创建对象。

## 字体资产

建议独立声明 `fonts.json`，不要把字体伪装成 Texture：

```json
{
  "schemaVersion": 1,
  "fonts": [
    {
      "name": "ui.body",
      "path": "fonts/NotoSansSC-Regular.otf",
      "fallbacks": ["ui.emoji", "ui.symbols"],
      "rasterization": "sdf",
      "hinting": "auto"
    }
  ]
}
```

固定规则：

- Font 名称大小写敏感，包内和依赖闭包全局唯一。
- 路径受 Content Root 安全边界约束。
- Fallback 不能循环，重复字体和缺失依赖在构建期失败。
- Font 文件由 FontLibrary 拥有；Glyph Atlas Page 由 Text Renderer/Font Cache 拥有。
- Content 热重载使用候选 Font/Atlas，成功后在帧边界原子替换；失败继续使用旧字体。
- 强类型生成 `GameFonts.UiBody` 等 `FontRef`，不公开字体文件路径或 GPU Handle。

## 字形生成技术验证

第一阶段先做小型后端验证，不直接把某个 Native Font 库扩散到公共 API。候选包括：

- FreeType + HarfBuzz：能力完整、行业常用，但需要管理本机库、AOT、分发与许可证清单。
- SkiaSharp + HarfBuzzSharp：项目已间接使用 SkiaSharp，接入成本可能较低，但仍需验证 Shape、NativeAOT、自包含发布和跨平台 Native Assets。
- stb_truetype 类轻量 Rasterizer：适合简单 Glyph Raster，不足以独立承担复杂 Shaping/Fallback/Bidi。

验收 Spike 必须覆盖：中文、拉丁、数字、标点、组合字符、Emoji ZWJ、Fallback、NativeAOT Publish 和无窗口 Layout Test。最终公共 API 只暴露 Font/Layout/Glyph 语义，不暴露具体第三方类型。

## 阶段 0：Unicode 与后端契约

目标：先证明字符串分段、Shaping、Rasterization 和发布链可行，不绘制完整 UI。

实施内容：

- `Rune`/Unicode Scalar 枚举与 Grapheme Cluster 边界。
- Font Face 加载、Glyph ID、Advance、Bearing、Kerning/Shaping 接口。
- 中英混排 Fallback Run。
- 字体度量：Ascent、Descent、LineGap、Baseline。
- 依赖库 NativeAOT、Windows x64 自包含发布验证。
- 确认 Bidi 与 UAX #14 Line Breaking 的实现来源和版本策略。

验收：

- `中文ABC123，。！？` 形成稳定 Glyph Run 和 Cluster Mapping。
- 代理项、组合字符和 Emoji 序列不会被拆成非法单元。
- 缺 Glyph 按 Fallback 顺序解析，最终缺失使用明确 Replacement Glyph。
- 同一输入在相同 Font Revision 下产生确定性 Layout 结果。

## 阶段 1：中文字体与基础绘制

目标：在 World 与 SceneGui 中高效绘制单色/彩色普通文本。

新增项目候选：

```text
Engine.Features.TextRendering
Engine.Features.TextRendering.Tests
Engine.Features.TextRendering.VisualTests
```

实施内容：

- `FontRef`、FontLibrary、FontMetadata、GlyphMetrics。
- 动态 Glyph Atlas，多 Page 增长，第一版不驱逐已生成 Glyph。
- Bitmap 与 SDF 二选一作为首个稳定 Raster Mode；像素字体保留 Nearest 路径。
- `TextBatch` 复用 SpriteBatch 类似的动态 VBO，但使用 Glyph Quad 和 Font Atlas。
- World Projection 与 SceneGui Projection。
- 单行、多行、颜色、缩放、旋转、Baseline 和像素对齐。
- `DrawText` 便利 API 与可缓存 `TextLayout` API。
- Font/Atlas/Page/Texture Switch/Draw Call 诊断。

验收：

- 简体中文、ASCII、全角标点和数字同屏。
- Camera 缩放/旋转下 World Text 正确；SceneGui 不受 Camera 影响。
- 多 Font Page 切换、resize、Scene 切换与释放无泄漏。
- 预热后重复 Draw 已缓存 Layout 保持 0 B/frame。
- 1,000 条短文本与一段长中文文本的 Release 基准。

## 阶段 2：Layout、Fallback、换行与对齐

目标：建立富文本和 GUI 可依赖的稳定排版核心。

实施内容：

- Font Fallback Chain 和按 Run 切分。
- Chinese/Latin 混排换行，禁止行首/行尾标点规则。
- Word、Character、NoWrap 模式。
- Left/Center/Right 与 Top/Middle/Bottom。
- MaxWidth、MaxLines、Clip、Ellipsis。
- 行级 Baseline、不同 Size/Font Run 对齐。
- 可选 Bidi；如果首版暂不支持 RTL，必须显式诊断而非输出错误顺序。
- Layout Cache Key 包含 Content、Font Revision、Style、Wrap Width、Locale/Direction。

验收：

- 中文无空格文本能按合法位置换行。
- 中英数字、标点、不同 Font Fallback 保持一致 Baseline。
- Ellipsis 不切断 Grapheme Cluster。
- 相同 Layout 输入命中缓存，Font 热替换后 Revision 使缓存失效。

## 阶段 3：彩色文字与受限富文本

目标：支持对话强调、物品品质色、链接、图标和常见样式，不实现完整浏览器 HTML/CSS。

建议语法：

```text
[color=#FFCC66]传说物品[/color]
[size=28]标题[/size]
[font=ui.title]章节一[/font]
[b]重要[/b]
[sprite=items.gold size=20x20]
```

实施内容：

- Versioned RichText Parser 和不可变 Node Tree。
- Color、Size、Font、Bold/Italic Face、Underline、Strike、Outline、Shadow。
- Inline Sprite 和 Link Metadata；点击处理留给 GUI/Input 层。
- 严格嵌套、转义、最大深度、最大节点数和最大文本长度。
- Parser/Style/Layout 分层缓存。

验收：

- 标签嵌套、错误闭合、未知标签、转义和资源缺失诊断。
- 不同 Style Run 正确换行、Baseline 和 Ellipsis。
- 彩色文字继续合批；Outline/Shadow 额外成本可诊断。
- RichText 不读取任意路径、不执行脚本或发起网络请求。

## 阶段 4：Grapheme-aware 打字机效果

目标：为对话、字幕和剧情文本提供可跳过、可暂停、可复现的揭示控制器。

候选 API：

```csharp
var reveal = new TextRevealController(layout)
{
    UnitsPerSecond = 24f,
    PunctuationDelay = 0.08f
};

reveal.Update(deltaTime);
text.Draw(layout, position, reveal.VisibleUnits);
```

实施内容：

- Reveal Unit 为 Grapheme Cluster 或 Inline Node，不是 UTF-16 char。
- 标点附加延迟、Instant/Skip、Pause/Resume、Completed。
- Rich Tag 不占揭示单位；Inline Sprite/Emoji 占一个单位。
- 已完成 Layout 不随揭示进度重复 Shape/Wrap，文字不会边出现边跳行。
- Gameplay 与 Unscaled 时间由调用方选择；控制器不读取全局时间。
- 可选 reveal event 返回稳定节点/Cluster 信息，声音播放由调用方决定。

验收：

- 中文、组合字符、代理项、ZWJ Emoji 不会显示半个字符。
- Skip 立即完成且只触发一次 Completed。
- 暂停和不同 delta 分片得到相同最终揭示顺序。
- 每 Tick 更新和 Draw 不重新分配 Layout。

## 阶段 5：Emoji

先实现可控的 Sprite Emoji，再评估彩色 Font Emoji。

### Sprite Emoji（P2）

- Emoji Sequence/Shortcode → `SpriteRef` 映射资产。
- 支持 `😀`、Variation Selector 和 ZWJ Sequence 作为一个 Cluster。
- Inline Box 使用固定 Advance、Baseline 和 Size，不因动画帧改变布局。
- 缺失映射回退到 Font Glyph 或 Replacement Glyph。
- 继续使用 Content Package、Atlas 和强类型/逻辑资源引用。

### Color Font Emoji（P3）

需要分别验证 COLR/CPAL、CBDT/CBLC、SBIX 等实际字体格式、Rasterizer 支持、缓存成本和 NativeAOT。只有真实字体资产无法用 Sprite Emoji 合理覆盖时才实施，不承诺第一版支持所有平台字体格式。

## 阶段 6：内联动画与动图

推荐顺序：

1. Inline `SpriteRef` 动画：复用 Sprite FPS、帧循环和 Atlas。
2. FairyGUI MovieClip Adapter：仅在 FairyGUI 集成内消费其 Package 动画。
3. Animated WebP/GIF：作为独立 AnimatedImage Asset 解码，不塞进 FontLibrary。

规则：

- Inline 动画拥有固定 Layout Box；不同帧尺寸不得推动后续文字跳动。
- 多图片动画继续按每帧 TextureRef 解析，允许 Flush 但必须进入诊断。
- 文本 Layout Cache 不包含当前动画帧；Draw 阶段解析当前帧。
- 动画时间域显式选择 Gameplay/Unscaled/RenderTime。
- GIF/Animated WebP 需要帧时长、循环次数、Dispose、内存预算和解码上限，不继承静态 WebP 的简单路径。

## 阶段 7：文本输入、IME 与选择

这是原生编辑框和 FairyGUI 完整集成的共同前置，不应与基础 DrawText 同时塞入第一切片。

实施内容：

- Window TextInput/Composition 事件，与 KeyDown 分离。
- 中文 IME Composition、候选窗口位置和提交文本。
- Caret、Selection、Home/End、Unicode-aware Backspace/Delete。
- Clipboard Copy/Cut/Paste 和长度限制。
- Focus、Mouse Capture、Tab Navigation 与 Game Input Routing。
- Password/Number/SingleLine/MultiLine 策略。
- Replay 默认不录制任意文本输入；需要玩法确定性时由游戏转换为明确命令。

验收必须人工覆盖 Windows 中文输入法，同时用无窗口模型测试 Cluster 删除、选择和 Clipboard Adapter。

## 性能与资源预算

建议首版保护线：

- Glyph Atlas 默认 1024×1024，多 Page 增长并设置最大页数。
- 不预烘焙全 CJK；支持本地化语料离线子集和运行时增量补字。
- 每帧新增 Glyph 有上限，防止一次恶意文本冻结主线程。
- Layout Parser/Shape/Raster/GPU Upload 分项诊断。
- 低频诊断报告 Font Faces、Atlas Pages、Glyph Count、Missing Glyph、Layout Cache Hit、Text Draw Call、Texture Switch 和显存估算。
- 动态数字/计时器提供无字符串或低分配 Formatter 路径，但不在 v1 过早设计复杂模板语言。

## 与其他路线图的关系

- Animation：Inline Sprite/MovieClip 直接复用命名 Animation Clip 和稳定帧盒；因此完整动图晚于 Animation Authoring 基础。
- Tilemap：世界标签和地图标记可复用 World Text，但两者互不依赖。
- Audio：Typewriter 声音由调用方或对话系统播放，Text Reveal 不拥有 Audio。
- Lighting：World Text 可选择 Unlit 或 Lit；SceneGui Text 默认绕过 HDR Exposure。
- FairyGUI：可先使用 FairyGUI 自身 Text Runtime；原生 Text 不强制替换它。只有共享 Font/Glyph Cache 被证明兼容且有收益时才桥接。
- Localization：文本系统只负责 Unicode/Layout，不负责翻译表、Plural Rule 或剧情数据库；后续可独立增加 Localization Assets。

## 推荐首次实施切片

首次只完成：

1. Font 后端 NativeAOT Spike。
2. `FontRef + fonts.json + FontLibrary`。
3. 中文/ASCII/Fallback 单行绘制。
4. 动态 Glyph Atlas 与 SceneGui/World 两种 Projection。
5. 基础诊断、无窗口度量测试和一个 VisualTest。

明确不夹带 RichText、Typewriter、Emoji、AnimatedImage、IME、FairyGUI 或完整 Bidi。基础中文字形和生命周期稳定后，再进入 Layout/换行切片。

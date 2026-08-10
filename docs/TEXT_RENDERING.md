# Text Rendering 使用指南

`Engine.Features.TextRendering` 已提供真实 TTF/OTF 字体解析、Unicode Rune 与 Grapheme Cluster 多行布局、有序 Font Fallback、中文/单词换行、Left/Center/Right 对齐、Clip/Ellipsis、动态 Glyph Atlas、可复用 Layout Buffer，以及 World/SceneGui 共用的 `DrawText` 路径。

## Hosting 快速开始

Hosting 为每个应用创建一个共享 `TextRuntime`，通过 `Default2DGameContext.Text` 暴露。字体没有隐式默认值，游戏必须明确提供并分发字体文件：

```csharp
.AddScene(GameScenes.Main, context =>
{
    FontRef latin = context.Text.LoadFont(
        "game.ui.latin",
        Path.Combine(AppContext.BaseDirectory, "Fonts", "Inter-Regular.ttf"));
    FontRef cjk = context.Text.LoadFont(
        "game.ui.cjk",
        Path.Combine(AppContext.BaseDirectory, "Fonts", "NotoSansSC-Regular.otf"));
    FontFamily ui = context.Text.CreateFamily(latin, cjk);

    context.Scene.Add(new HudText(context.Text, ui));
})
```

不要依赖开发机的系统字体作为正式游戏资产。`LoadSystemFont` 适合工具、诊断和实验；正式 Build/Publish 应复制具有明确授权的字体文件，并保持稳定路径。

## World Text 与 SceneGui Text

同一个 `TextRuntime` 和 `PreparedTextLayout` 可以在两个空间绘制：

```csharp
public sealed class HudText : GameInstance
{
    private readonly TextRuntime _text;
    private readonly PreparedTextLayout _worldLabel;
    private readonly PreparedTextLayout _hudLabel;

    public HudText(TextRuntime text, FontFamily fonts)
    {
        _text = text;
        _worldLabel = text.Prepare(fonts, "任务目标", 28f);
        _hudLabel = text.Prepare(fonts, "生命 100", 22f);
    }

    public override void OnDraw(ISpriteBatch batch) =>
        _text.Draw(batch, _worldLabel, new Vector2(320, 180), Vector4.One);

    public override void OnDrawGUI(ISpriteBatch batch) =>
        _text.Draw(batch, _hudLabel, new Vector2(24, 24), Vector4.One);
}
```

- `OnDraw` 使用当前 Render View 的 Camera 投影，因此文字会随世界平移、缩放和旋转。
- `OnDrawGUI` 使用屏幕正交投影，不受 Camera 影响，也绕过 Scene HDR Exposure。
- 绘制坐标是文本 Layout 左上角；Baseline 由字体指标计算。
- Glyph Quad 复用 SpriteBatch 的纹理切换、Blend 与 Draw Call 统计。

也可以直接绘制动态字符串：

```csharp
context.Text.Draw(batch, family, $"Score: {score}", position, 24f, color);
```

这条便利路径每次都会重新 Layout/Prepare。静态标签、菜单标题和不常变化的文本应缓存 `PreparedTextLayout`。

## 多行、换行与对齐

```csharp
PreparedTextLayout paragraph = text.Prepare(
    family,
    "多行中文在字素边界换行。\nLatin words prefer spaces.",
    24f,
    new TextLayoutOptions(
        MaxWidth: 480f,
        WrapMode: TextWrapMode.Word,
        Alignment: TextAlignment.Center,
        MaxLines: 4,
        Overflow: TextOverflow.Ellipsis,
        LineSpacing: 6f));
```

- `NoWrap` 不自动换行，但仍识别 `CRLF`、`CR` 和 `LF`；设置有限 `MaxWidth` 后按 Cluster 截断。
- `Character` 可在合法 Grapheme Cluster 之间换行，不拆分代理项、组合字符或 Emoji ZWJ Cluster。
- `Word` 优先使用拉丁空白和 CJK 字符边界；基础中文开闭标点禁则会避免常见标点出现在错误行首/行尾。
- `MaxWidth = 0` 表示不限制宽度；自动换行必须提供正宽度。
- `MaxLines = 0` 表示不限制行数；`Clip` 省略溢出 Cluster，`Ellipsis` 在最后一行追加 `…`。
- 极窄宽度下，短中文闭标点串可能有意略微超过 `MaxWidth`，避免产生标点开头的行。

当前换行器是确定性的基础 Unicode/CJK 实现，不宣称完整实现 UAX #14；复杂语言进入 HarfBuzz/Unicode Line Breaking 适配后再扩展。

## 高频动态文本 Buffer

调用方持有 `TextLayoutBuffer` 与 `PreparedTextLayoutBuffer`，可在内容变化时复用容量：

```csharp
private readonly TextLayoutBuffer _layout = new();
private readonly PreparedTextLayoutBuffer _prepared = new();

text.PrepareInto(
    family,
    scoreText,
    22f,
    new TextLayoutOptions(260f, TextWrapMode.NoWrap, TextAlignment.Right),
    _layout,
    _prepared);

text.Draw(batch, _prepared, new Vector2(24, 24), Vector4.One);
```

Buffer 按需几何扩容，容量稳定后相同长度级别的 Layout + Atlas Prepare 为 0 B。`Revision` 防止 Layout 在 Prepare 后被改写却继续绘制旧 Glyph；改写后必须重新 `PrepareInto`。字符串本身仍由调用方负责，高频 `$"Score: {score}"` 的字符串分配不会被 Buffer 隐藏。

`TextRuntime.CaptureDiagnostics()` 返回 Layout 次数、缺字数、Glyph Cache hit/miss、缓存字形和 Atlas 页数；Buffer 自身公开 `ExpansionCount`，便于定位容量抖动。

## 字体与 Fallback

```csharp
FontRef primary = text.LoadFont("font.primary", stream);
FontRef fallback = text.LoadFont("font.cjk", cjkStream, faceIndex: 0);
FontFamily family = text.CreateFamily(primary, fallback);
```

每个 Unicode scalar 按 FontFamily 顺序寻找 Glyph；全部缺失时使用 Primary Font 的 missing glyph。Grapheme Cluster 边界会保留，因此代理项和组合字符不会被截断成非法 UTF-16。

`.ttc` 字体集合可用 `faceIndex` 选择 Face。`SkiaGlyphRasterizer` 由 `FontLibrary` 拥有，`TextRuntime.Dispose()` 会释放所有 Skia Face/Font 和 Glyph Atlas 页。

## Glyph Atlas 与 TextureLibrary

Glyph 首次出现时才栅格化。Atlas 使用确定性 shelf packing，默认配置为 512×512、1 像素 padding、最多 16 页：

```csharp
var options = new GlyphAtlasOptions(
    PageWidth: 1024,
    PageHeight: 1024,
    Padding: 1,
    MaxPages: 8);
using var text = new TextRuntime(textures, options, "game.text-atlas");
```

Atlas 页面以透明 RGBA8 Texture 保存：RGB 为白色，Glyph Coverage 写入 Alpha。`TextureLibrary.UpdateRgba` 使用 OpenGL `TexSubImage2D` 局部更新，不重建 TextureRef 或 GPU Handle。

所有权顺序必须是：

```text
TextRuntime.Dispose
  → 删除 Glyph Atlas Texture
  → 释放 Font/Rasterizer
TextureLibrary.Dispose
```

Hosting 已保证这一逆序释放。手工创建多个 TextRuntime 并共享同一个 TextureLibrary 时，应为每个 Runtime 指定不同的 `atlasNamePrefix`。

## 当前边界

- 当前支持从左到右的多行基础 Layout、Grapheme 安全换行、基础中文禁则、对齐、最大行数与逻辑 Clip/Ellipsis；尚未提供像素 Scissor/Selection/Caret。
- `SKFont` 负责真实 Glyph ID、指标与栅格化，但当前尚未接入 HarfBuzz shaping；阿拉伯文、复杂印度文字、连字和高级 Kerning 不能视为完成。
- 支持单色 Glyph Coverage；尚无 Outline、Shadow、渐变、SDF/MSDF 或彩色 Font Emoji。
- 尚无声明式 `fonts.json`、强类型 `GameFonts`、Font Hot Reload、IME、RichText 或打字机控制器。
- 动态 Atlas v1 只增长、不驱逐；超过页上限会明确失败，后续结合实际显存数据设计预算与 LRU。

真实示例位于 `src/Engine.Features/TextRendering.VisualTests`：同屏显示中文/拉丁 Fallback、多行中文/拉丁换行、居中 SceneGui Text、World Text 和 Camera 变化；支持 `--smoke` 隐藏窗口回归。

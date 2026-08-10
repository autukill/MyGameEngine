# MyGameEngine Android 平台可行性

> 结论：MyGameEngine 可以渐进演进到 Android，但当前版本不能直接构建、运行或发布 Android 游戏。《驮春》采用 Steam PC 优先、玩法保持触控友好、未来独立 Spike 的策略。

本文记录当前仓库事实与未来验证边界，不表示 Android 已进入开发。

## 为什么不是更换 RuntimeIdentifier 就能发布

MyGameEngine 当前默认 Runtime 是明确的桌面技术组合：

- `EngineWindow` 固定启用 Silk.NET GLFW Windowing/Input。
- `EngineWindowOptions` 请求 OpenGL 3.3 Core Profile。
- 内置 Shader 使用 `#version 330 core`。
- Graphics、SpriteBatch、RenderTarget 和多个效果切片直接使用 `Silk.NET.OpenGL.GL`。
- Content Runtime 从 `AppContext.BaseDirectory/AssetsCompiled` 和普通文件路径读取资源。
- 真实短音效后端依赖桌面 OpenAL Soft native binary。

Android 应用则由 Activity 与 Surface 生命周期驱动，通常通过 EGL/OpenGL ES 绘制，资源位于 APK/AAB Asset 或应用私有目录。应用还必须处理 Touch、Back、Safe Area、Pause/Resume、Surface 丢失与重建。

因此 `net10.0-android` 只是托管 Runtime 和打包入口，不会自动把 GLFW、桌面 GL、Shader、文件系统和音频后端转换为移动实现。

## 当前能力矩阵

| 子系统 | 可复用程度 | 当前判断 |
|---|---:|---|
| Gameplay Domain、Scene、Prefab、Health、Behavior、Signal | 高 | 不依赖 GLFW/GL，可继续复用。 |
| 确定性 Clock、Random、Replay 与状态 Hash | 高 | 协议可复用；文件保存位置需改为 Android 应用目录。 |
| Manifest、Sprite、Animation、Tilemap Domain | 高 | 逻辑数据可复用。 |
| AssetCompiler | 高 | 继续在开发机/MSBuild Host 执行，不应在手机中运行。 |
| Content Runtime | 中 | Parser 可复用；文件路径读取需要 Stream/AssetStore 边界。 |
| Texture 解码 | 中高 | SkiaSharp 官方支持 Android，但必须验证 native asset、裁剪与发布。 |
| Text Layout/Glyph Atlas | 中高 | Unicode/Layout 可复用；字体来源要从路径扩展到 Stream/Asset。 |
| Window/Game Loop | 低 | 当前硬绑定 GLFW，需要 Android Activity/Surface Host。 |
| Graphics 与 Render Features | 中低 | 算法和 Render Graph 可复用；GL 调用、格式能力和 Shader 需要 GLES 适配。 |
| Input | 中 | `IInputProvider` 和逻辑 Action 可复用；需要 Pointer/Touch/Back 实现。 |
| Audio Domain | 高 | `IAudioBackend` 已形成设备边界。 |
| OpenAL 设备后端 | 低 | 当前 OpenAL Soft native 包没有 Android RID，需要 Android 音频后端。 |
| Build/Publish | 低 | 当前分发测试面向桌面/Windows AOT，需要 APK/AAB、签名和 ABI 流程。 |

## 需要新增的平台边界

未来若 Android Spike 成功，正确方向是保留 Domain 和 Gameplay API，在设备边界增加实现，而不是让游戏代码到处判断 `OperatingSystem.IsAndroid()`。

### Host 与生命周期

Android Host 负责把 Activity/Surface 回调映射到统一 Load、Step、Draw、Resize、Pause、Resume 和 Dispose。必须明确：

- Surface 可以在 Activity 尚未销毁时被重建。
- GL Context 丢失时，GPU Texture、Shader、Framebuffer 和 Glyph Atlas 都需要重建策略。
- 后台状态不能继续使用桌面窗口的无限更新语义。
- 窗口尺寸、显示密度、旋转和 Safe Area 需要独立快照。

### OpenGL ES

首个候选基线是 OpenGL ES 3.0，而不是兼容 ES 2.0：SpriteBatch 当前使用的 VAO、Framebuffer 和现代 Shader 输入在 ES 3.0 更接近桌面实现。

仍需真实验证：

- `#version 330 core` 到 `#version 300 es` 的 Shader 变体与 fragment precision。
- RGBA8、Depth24Stencil8 和 HDR `RGBA16F` RenderTarget 支持。
- Stencil、Bloom、Tone Mapping、Texture 格式和读回能力。
- 不同 GPU 厂商上的 GL Error、扩展与性能。

不应在验证前建立覆盖每个 GL 调用的巨大通用接口。先用最小 Sprite Spike 判断 Android 原生 GLES 与 Silk.NET OpenGLES 的装载、API 对齐和 NativeAOT/Trim 行为，再决定产品化适配层。

### Pointer 与触控

《驮春》的纯指针园艺天然适合触控，但游戏代码只能依赖逻辑 Pointer/Action：

- Primary pointer down/up/move。
- Pointer ID 与多点触控预留。
- 屏幕、Viewport 与世界坐标转换。
- 长按、拖动、取消和系统手势抢占。
- Android Back 与暂停菜单的显式映射。

PC 键鼠仍是首发输入；触控友好不等于现在实现移动端 UI。

### AssetStore

编译器仍在开发机产生标准 `AssetsCompiled`，但 Runtime 需要从“根目录字符串”演进为受限 AssetStore/Stream：

- 桌面实现读取普通目录。
- Android 实现读取 APK Asset，或在安装后复制到应用私有目录。
- Manifest、图片、WAV、TileMap 和字体共用同一安全路径模型。
- Hot Reload 只属于开发环境，不应成为移动端发布依赖。

### Audio 与字体

`IAudioBackend` 已允许增加 Android 设备实现。未来应先满足短 WAV 的播放、并发 Voice、暂停恢复和释放，再决定是否选择 AudioTrack、AAudio/OpenSL ES Adapter 或可分发的 Android OpenAL 实现。

SkiaSharp 官方声明支持 Android，因此 Font/Glyph Rasterizer 不必重写排版 Domain；但必须验证 Android native asset、字体 Stream、Trim/AOT、内存和 Surface 重建后的 Glyph Texture 恢复。

## 推荐路线

### 当前：PC 首发、预留兼容

- 《驮春》只记录概念，不开始开发。
- 未来 Gameplay 使用逻辑 Pointer/Action，不引用 GLFW 或 Android 类型。
- 以横屏 16:9 为设计基准，同时避免把交互坐标写死为单一分辨率。
- 游戏资源只通过 Content/Font API 访问，不直接读取绝对路径。
- 不为了尚未验证的 Android 需求重写现有 Renderer。

### 首个 PC 可玩切片之后：Android Sprite Spike

单独建立最小 `net10.0-android` 项目，只验证平台链路：

1. Activity 创建横屏 Surface 和 GLES 3.0 Context。
2. 上传一张编译内容包中的 Texture，并用一个 Sprite Shader 绘制。
3. Touch 控制 Sprite 或选中一个逻辑格子。
4. Pause/Resume、切到后台和 Surface 重建后画面恢复。
5. 从 APK/AAB 正确读取 Manifest、图片和字体。
6. 真机播放一个短音效并完成资源释放。
7. 在 arm64 真机生成可安装 APK，并验证 Release Trim/AOT 策略。

这个 Spike 不追求 Bloom、Stencil、Lighting、完整 Scene 或现有 Playground 兼容。

### Spike 通过之后：选择性产品化

只有最小链路稳定后，才增加 Android Host、GLES Shader 变体、Stream AssetStore、Android Audio Backend、移动发布诊断和设备矩阵。功能按《驮春》真实需要迁移，不追求第一天与桌面所有高级 Render Feature 等量齐观。

## Go / No-Go 边界

Android 路线继续产品化至少需要满足：

- Release APK 在 arm64 真机稳定启动、暂停、恢复和退出。
- Sprite/Texture/RenderTarget 没有持续 GL Error 或 Surface 重建泄漏。
- Pointer 坐标在分辨率、DPI 与宽高比变化下正确。
- 编译内容包、字体和短音效能从应用资源安全加载并释放。
- 简单 Sprite 场景可以稳定达到设备显示刷新节奏。
- 平台代码保持在 Host/Backend/AssetStore 边界，不泄漏进 Gameplay Domain。

若这些条件不能以可控复杂度满足，应继续保持 Steam PC 产品路线，而不是让未验证的跨平台抽象污染默认 Runtime。

## 当前非目标

- 不创建 Android 工程或安装包。
- 不修改现有 `EngineWindow`、Graphics 或 Shader。
- 不承诺 Google Play 发布或 PC/Android 同日上线。
- 不引入 MAUI UI、MonoGame GraphicsDevice 或第二套游戏生命周期。
- 不以 SilentAudioBackend 冒充真实 Android 音频支持。

## 官方参考

- [.NET for Android 安装与工作负载](https://learn.microsoft.com/en-gb/dotnet/android/getting-started/installation/net-android)
- [.NET for Android Build Items 与 native ABI 资源](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-items)
- [Silk.NET 官方仓库](https://github.com/dotnet/Silk.NET)
- [SkiaSharp 官方仓库与支持平台](https://github.com/mono/SkiaSharp)

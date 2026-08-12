# MyGameEngine 统一术语

本文是 MyGameEngine 文档中的统一语言入口。公共文档首次出现术语时使用“中文名称（代码/API 名称）”，
后续可以直接使用代码名称。API 为兼容性保留的既有命名不强制修改，但文档含义以本文为准。

## World、Map、Tile 与 Cell

| 统一术语 | 含义 | 不应混同 |
|---|---|---|
| 世界（World） | 游戏对象所在的连续坐标空间，例如 `0..12000 × 0..12000`。 | 窗口像素、图片像素。 |
| 地图（Map） | 对世界中地形、格子、对象或视觉内容的一种数据描述。 | World 本身。一个 World 可以组合多张 Map 或多个 Layer。 |
| 格子（Cell） | 逻辑网格中的一个位置及其数据槽，例如地形类型、Tile ID 或移动代价。 | Tile 图片。Cell 引用什么和最终画成什么可以不同。 |
| 图块（Tile） | 可复用的地图内容单元，通常通过 Tile ID 引用 TileSet 中的 Sprite、碰撞或其他属性。 | Chunk。一个 Chunk 通常包含许多 Cell/Tile。 |
| 图块集（TileSet） | Tile ID 到 Sprite、碰撞和属性的目录。 | Texture Atlas。TileSet 是逻辑目录，Atlas 是图片存储优化。 |
| 图层（Layer） | 具有独立深度、可见性或 Gameplay 语义的一层地图内容。 | LOD。Layer 表示内容分层，LOD 表示同一内容的精度层级。 |

## Chunk：世界分块

`Chunk` 统一称为“世界分块（Chunk）”：为了按区域加载、更新、绘制、缓存和释放，将连续世界切出的
固定空间单元。

```text
12000 × 12000 世界
    ↓ 每个 Chunk 覆盖 600 × 600 世界单位
20 列 × 20 行
    ↓
共 400 个 LOD0 Chunk
```

Chunk 首先是空间和生命周期边界，不是某一种资源格式：

- TileMap Chunk 可以保存许多 Cell、Tile ID 和静态碰撞数据。
- Raster Chunk 可以保存一个世界区域对应的 WebP/RGBA 视觉数据。
- 同一个 Chunk 可以包含多个 Layer 的 Payload。
- Chunk 可以尚未加载、只在 CPU 中准备完成，或已经上传为 GPU Texture。
- Chunk 不等于文件；多个 Chunk 可以一起存入一个 `.mgworld` 归档并按索引随机读取。
- Chunk 不等于 Texture；权威 Tile Chunk 可以没有独立 Texture，Raster Chunk 也可能包含多个 Layer Texture。

常见组合术语：

| 统一术语 | API / 含义 |
|---|---|
| Chunk 坐标 | `WorldChunkCoordinate`；分块网格中的整数列、行。 |
| Chunk 世界尺寸 | `WorldChunkLayout.ChunkSize` / `BaseChunkWorldSize`；一个 LOD0 Chunk 覆盖多少世界单位。 |
| Chunk Payload | 归档中属于一个 Chunk 的数据，可以是 Tile/碰撞或 Raster Layer。 |
| Chunk Lease | 已加载 Chunk 的唯一所有权凭证；离开保留范围后由 Streamer 释放。 |
| Chunk Texture | Raster Chunk 解码并上传 GPU 后的纹理资源；WebP 文件大小不等于其 RGBA 显存。 |

双网格（Dual Grid）、传统 TileMap 和 Raster 地图都可以使用 Chunk。双网格决定“如何从逻辑格推导
显示图块”，Chunk 决定“世界的哪一块何时加载和释放”，两者是互相独立的维度。双网格跨 Chunk
解析边缘时通常额外读取一圈邻居 Cell，但只发布本 Chunk 的显示结果。

## LOD：细节层级

LOD（Level of Detail）表示同一世界内容在不同观察距离下的精度版本：

| 统一术语 | 含义 |
|---|---|
| `LOD0` | 索引为 0 的最高细节层。TileMap 来源可以包含权威 Tile/碰撞；预切片来源可以是导入的 Raster LOD0。 |
| 生成式 `LOD1+` | 离线编译器从上一层逐级降采样得到的粗粒度视觉 Chunk，不参与 Gameplay 查询。 |
| 最粗可用 LOD | `LOD(lodCount - 1)`，仍由 Chunk 组成并按范围流式驻留。 |
| Active LOD | 当前稳定负责绘制的分块 LOD。 |
| Pending LOD | 已被 Zoom 选中、正在准备但尚未原子接管画面的分块 LOD。 |

LOD 编号越小越清晰，编号越大越粗：

```text
LOD0  最高细节，覆盖范围最小、所需 Chunk 最多
LOD1  比 LOD0 粗一级
LOD2  比 LOD1 粗一级
...
LOD(lodCount - 1)  最粗可用 LOD
```

`lodCount` 只统计分块 LOD：

- `lodCount: 1`：只有 LOD0；最粗可用 LOD 与 LOD0 是同一层，不存在生成式 LOD1+。
- `lodCount: 3`：存在 LOD0、LOD1、LOD2；LOD2 是最粗可用 LOD。
- Zoom 只在已有的 LOD 之间选择，不能凭空产生清单中不存在的粗 LOD。

`targetPixelsPerTexel` 的比例调整、滞回差异，以及“已经选择 LOD0”和“预算允许 LOD0 Chunk 显示”
之间的区别，见 [TileWorld 运行时 LOD 与流式加载](TILE_WORLD_RUNTIME_STREAMING.md#大白话如何调整-lod0-出现的-zoom)。

## Preview 回退图

Preview 回退图是清单 `fallbackSurfaces` 声明的低清全世界图片，常见源文件名为 `preview.webp`；
其归档和 API 类型名为 `Fallback Surface`。

它与最粗可用 LOD 的区别是：

| Preview 回退图 | 最粗可用 LOD |
|---|---|
| 是覆盖整个世界或指定 Layer 的低清 Surface。 | 是 `LOD(lodCount - 1)` 的分块层。 |
| 通常独立解码并常驻。 | Chunk 按 Visible/Preloaded/Retained 范围流式驻留。 |
| 不计入 `lodCount`，不参与 LOD 选择。 | 计入 `lodCount`，由 LOD Selector 选择。 |
| Chunk 未就绪或预算超限时保底。 | Active/Pending 缺块时优先提供分块回退。 |
| 不提供 Tile、碰撞或 Gameplay 查询。 | LOD0 可能提供权威数据；LOD1+ 只提供视觉数据。 |

现有诊断 API 中：

- `FallbackLevel` 或单独的 `Fallback` 表示最粗可用 LOD。
- `HasFallbackSurfaces`、`FallbackSurfacesReady`、`FallbackSurfaceQuads` 表示 Preview 回退图。

文档不使用没有限定词的“Fallback”同时指代这两类资源。

## Viewport、Camera 与范围

| 统一术语 | API / 含义 |
|---|---|
| Camera | 决定世界如何变换到 View 坐标，包括位置、Zoom 和旋转。 |
| Render View | 一次独立的 Scene 绘制视图，拥有 Camera、Render Surface、Viewport 槽位和可选效果链。 |
| Viewport | Render View 在输出中的观察窗口；承载屏幕映射、交互导航和当前可见世界范围。它不是 Camera，也不是 Window。 |
| 可见范围 | `Visible`；Viewport 当前实际覆盖、必须优先就绪的 Chunk。 |
| 预加载范围 | `Preloaded` / `PreloadMarginChunks`；在可见范围外提前加载的 Chunk。文档不写“预载范围”。 |
| 保留范围 | `Retained` / `RetainMarginChunks`；Chunk 离开该范围后才取消或释放。 |

Zoom 变大表示拉近：屏幕内覆盖的世界范围变小，需要的 Chunk 通常减少。Zoom 变小表示拉远：屏幕内
覆盖的世界范围变大，需要的 Chunk 通常增加。

## Camera Framing、参考画面与安全区域

相机构图（Camera Framing）决定 Render View 尺寸或宽高比改变后，Camera 应当看见多少世界。它由
`SceneCameraViewportPolicy` 声明，属于 Scene View，不是全局 Window 策略。

| 统一术语 | API / 含义 | 不应混同 |
|---|---|---|
| 参考画面（Reference View） | 美术和关卡按其创作的逻辑世界尺寸，例如 BubbleTa 的 `720×1280`。`ReferenceViewportSize` 保存该尺寸。 | Window 像素尺寸、RenderTarget 像素尺寸。 |
| 设计安全画面（Design Safe Frame） | 必须保持可见的核心构图区域。当前 Framing API 将完整 Reference View 作为默认 Design Safe Frame。 | 刘海、系统手势区域所形成的 Display Safe Area。 |
| 延展背景（Overscan） | 位于 Design Safe Frame 外、允许不同宽高比额外显示或裁掉的世界/背景内容。 | Texture Atlas padding、GPU overscan。 |
| 显示安全区域（Display Safe Area） | 由刘海、圆角屏和系统手势等平台限制形成的可安全放置 UI 的屏幕区域。 | 世界空间 Design Safe Frame。 |
| 相对 Zoom | Gameplay、Navigation 或 CameraFollow 在 Framing 基准缩放上施加的 Zoom 倍率。 | Window Resize 自动产生的基础缩放。 |

Camera Framing 的四个基础策略：

| 策略 | 缩放规则 | 统一含义 |
|---|---|---|
| 固定可见高度 | `FixedVisibleHeight`；只按输出高度计算。 | 世界高度稳定，宽度随宽高比扩展或裁切。 |
| 固定可见宽度 | `FixedVisibleWidth`；只按输出宽度计算。 | 世界宽度稳定，高度随宽高比扩展或裁切。 |
| 完整扩展 | `Expand`；选择宽高两轴中较小的缩放。 | Design Safe Frame 完整可见，剩余轴显示更多 Overscan 世界；文档也可首次写作“完整扩展（Expand / Show All）”。 |
| 填满裁切 | `Cover`；选择宽高两轴中较大的缩放。 | 输出被世界画面填满，Design Safe Frame 的剩余轴允许裁切。 |

`MatchRenderTarget` 是兼容默认值：Camera 的逻辑 Viewport 直接采用 RenderTarget 像素尺寸，窗口缩小会让
Camera 看见更少世界。它不是固定逻辑分辨率策略。

Camera `Expand` 与 Presentation `Contain` 都使用较小缩放，但结果不同：

- `Expand` 扩大 Camera 可见世界，使用 Overscan 填满输出，不产生留边。
- `Contain` 保持源画面边界，在目标槽位内完整呈现，剩余区域是 Letterbox/Pillarbox。
- Camera `Cover` 与 Presentation `Cover` 都具有“填满并裁切”的直觉，但前者裁切世界构图，后者裁切已经渲染好的 Surface。

文档使用“裁切（Crop）”表示内容不可见；使用“缩放（Scale）”表示尺寸变化。不得把等比缩小窗口后
Camera 仍显示同一世界范围描述为“裁切”，也不得把显示更多 Overscan 描述为“拉伸”。

## 加载、上传与驻留

这些阶段不能统称为“已加载”：

```text
请求 Chunk
  → 后台读取压缩 Payload
  → CPU 解码为 RGBA
  → 主线程上传 GPU Texture
  → 原子发布为可绘制 Chunk
  → 离开保留范围后释放 Lease
```

| 统一术语 | 含义 |
|---|---|
| 后台加载 | 文件读取、校验和图片解码；不触碰 OpenGL。 |
| GPU 上传 | 在图形线程把解码后的 RGBA 创建为 Texture。 |
| 驻留 | Session/Streamer 当前仍然持有资源；不等于当前可见，也不等于进程总内存。 |
| 逐帧上传预算 | 限制一次 `Update` 上传多少 Texture/RGBA 字节，控制单帧尖峰。 |
| 稳态驻留预算 | 限制一个 LOD 最终持有多少 Raster Chunk Texture，控制长期显存。 |
| 预算回退 | 所需 Chunk 数或 Texture 字节超过硬预算时，暂停该 LOD 并使用 Preview 回退图。 |

WebP 的压缩文件很小，不代表运行时显存同样小。RGBA8 Texture 的基础估算为：

```text
显存字节 ≈ 宽 × 高 × 4 × Texture 数量
```

例如一张 `600×600` RGBA8 Chunk Texture：

```text
600 × 600 × 4 = 1,440,000 字节 ≈ 1.37 MiB
```

根据诊断值计算稳态驻留预算的实例，见
[TileWorld 运行时 LOD 与流式加载](TILE_WORLD_RUNTIME_STREAMING.md#大白话如何计算-chunk-驻留预算)。

## 文档写作约定

- 首次出现写“世界分块（Chunk）”，后续写 `Chunk`。
- 使用 `LOD0`，不混写 `Level 0`、`LOD 0` 或“默认 LOD”。
- 使用“最粗可用 LOD”，不使用容易与 Preview 混淆的“Fallback LOD”。
- 使用“Preview 回退图（Fallback Surface）”，后续简称“Preview 回退图”。
- 使用“预加载范围”和“保留范围”，不使用含义不明确的“外围范围”“缓存圈”或“预载范围”。
- 使用“参考画面（Reference View）”“设计安全画面（Design Safe Frame）”和“延展背景（Overscan）”；只有平台刘海、圆角屏与系统手势区域使用“显示安全区域（Display Safe Area）”。
- 使用“完整扩展（Expand）”表示保证参考画面完整并显示更多世界；使用 `Contain` 时必须说明它属于 Presentation 并可能产生留边。
- 只有具体 API/诊断字段使用 `Level`、`Fallback`、`Surface` 等既有英文名称。
- 如果文档中的局部定义与本文冲突，以本文和当前代码行为为准，并同步修正文档。

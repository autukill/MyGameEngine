# 2D 光照、阴影与受光材质渐进路线图

本文记录 MyGameEngine 的 2D Lighting 长期边界与可执行阶段。它是规划文档，不表示其中 API 已经实现。实施时仍按垂直切片推进：每一阶段都必须有独立 Tests、VisualTests、诊断、资源释放验证和使用文档，不能先搭一个不可运行的“大光照框架”。

## 目标

- 为俯视射击、地牢、平台动作、夜景、室内场景和像素风游戏提供统一但可裁剪的 2D 光照。
- 让开发者以世界坐标和纯值描述灯光、遮挡与材质，不接触 GL Handle、Shader、Pass 或 RenderTarget。
- 一份 Scene 可被多个 Camera/View 重绘；每个 View 显式选择 Lighting Profile 并承担自己的成本。
- 普通移动灯不产生 Domain Event、Gameplay Signal 或每灯 RenderEffect 重建。
- 第一阶段优先获得稳定、可调试的视觉结果；Normal Map、软阴影和高级介质效果在基础边界稳定后加入。
- 可选 Feature 未启用时，不创建 Lighting Shader、Buffer、Pass 或额外 RenderTarget。

## 非目标

- 不实现完整 3D PBR、实时全局光照、光线追踪或通用材质节点编辑器。
- 不把 Collider 自动视为 ShadowCaster；碰撞、受击和视觉遮挡保持独立。
- 不读取最终 Light Buffer 像素驱动潜行、AI 或伤害判定。
- 不用 Gameplay Signal 每帧广播灯光位置、强度或颜色。
- 不承诺 v1 的透明彩色阴影、任意凹多边形、SDF、体积 Ray Marching 或无限数量动态阴影灯。
- 不让当前主 View 专属 Stencil 成为多灯阴影的底层依赖。

## 当前基础与缺口

已经可复用的能力：

- `RenderSurfaceKey`、Effect Plan、稳定拓扑排序和原子重建。
- `RenderTargetPool`、RGBA8/RGBA16F、Linear/Display 编码契约。
- Bloom、Tone Mapping 和唯一 Presentation 终端。
- 独立 RenderView、Camera、RenderScale、Layer Filter、Camera 可见性剔除和诊断。
- Sprite、Atlas、多图片帧、Shader/Material Assets、热重载和强类型生成引用。
- Frame Statistics、RenderTarget 租约和 GPU 像素回归。

需要补齐的边界：

- Texture 尚未显式区分 sRGB 颜色数据与 Linear Normal/Mask 数据。
- `ShaderMaterial` 尚不支持 Texture/Sampler 参数。
- `SpriteBatch` 当前只有 Albedo Texture 和一组 UV。
- `RenderTarget2D` 当前只有一个颜色 Attachment，不能直接形成 Albedo/Normal/Emission G-Buffer。
- Scene 没有 Lighting Source、ShadowCaster 和 Projected Shadow 的作者接口。
- 当前 Stencil 适合 Spotlight/遮罩，不适合大量逐灯阴影。

## 稳定概念边界

### 灯光

候选纯值类型：

```csharp
public readonly record struct Light2DKey(string Slot);

public readonly record struct PointLight2D(
    Light2DKey Key,
    Vector2D Position,
    float Height,
    float Radius,
    Vector3 Color,
    float Intensity,
    bool CastsShadows,
    int Priority,
    LightLayerMask Layers);
```

`Owner InstanceId + Light2DKey` 形成 Scene 内稳定身份。同一实例可以提供多盏灯，不需要为每盏灯创建 RenderEffect。

### 灯光来源

候选作者接口：

```csharp
public interface ILight2DSource
{
    void WriteLights(ref Light2DWriter writer);
}
```

Lighting Runtime 每帧使用可复用 Buffer 收集 active Source。`Light2DWriter` 只接收结构体并拒绝 NaN、负半径、空 Key 和非法颜色；不允许调用方传入集合或 GPU 对象。

该模式支持一个实例写入多盏灯，也避免引入全局 Registry 或让 Prefab Factory 捕获渲染服务。若真实性能证明全 Scene 扫描成为瓶颈，再增加 Scene 维护的 Source 索引；第一版不提前维护第二套实例生命周期。

### 遮挡

遮挡使用独立作者接口和形状：

```csharp
public interface IShadowCaster2DSource
{
    void WriteShadowCasters(ref ShadowCaster2DWriter writer);
}

public enum ShadowCasterShapeKind
{
    Box,
    Circle,
    ConvexPolygon
}
```

Caster 保存 Owner Transform、本地几何、Height、LayerMask、Revision 与是否静态。可以提供显式 `FromCollider()` 快照便利入口，但不会自动跟随 Collider 变化。

### 材质与假阴影

三个名称不能混用：

- `LightingMaterial2DRef`：Sprite 如何接收光照，包括 Normal、Emission 和响应强度。
- `ShadowCaster2D`：什么几何阻挡光线。
- `ProjectedShadowStyle`：角色脚下椭圆、偏移 Sprite、拉伸投影等美术化假阴影。

这避免把“表面明暗”“遮挡其他物体”和“额外绘制一块黑色形状”包装成含义不清的 `ShadowMaterial`。

## RenderSurface 数据流

基础阶段：

```text
SceneColor
    ├───────────────┐
    │               │
Visible Lights      │
    ↓               │
Light Accumulation  │
    ↓               │
LightBuffer ────────┤
                    ↓
             Lighting Composite
                    ↓
              Lighting.lit
                    ↓
       Bloom → Tone Mapping → Presentation
```

启用受光材质后：

```text
Scene Geometry
    ├─ Albedo
    ├─ World Normal
    └─ Emission

Albedo + Normal + Visible Lights + Shadows
    → Lighting.lit

Lighting.lit + Emission
    → Bloom/Tone Mapping/Presentation
```

Lighting 发布逻辑 `Lighting.lit` Surface。Bloom 和 Tone Mapping 消费该输出，而不是继续绕过 Lighting 读取原始 SceneColor。Lighting Runtime 内部可以拥有 LightBuffer、Shadow Geometry Buffer 或 G-Buffer，但领域描述符不暴露物理目标。

## 每 View 成本模型

Lighting 是 RenderView Profile 的显式选项：

```csharp
renderer.UseRenderViews(views => views
    .Primary(main => main.UseLighting(Lighting2DSettings.Default))
    .Add("minimap", minimap => minimap.UseDirect()));
```

- 每个启用 Lighting 的 View 独立收集可见灯、执行 Camera/Radius 剔除并拥有 LightBuffer。
- Mirrored Viewport 复用同一个已完成的 Lighting 输出，不重复计算。
- 小地图、Observer 或调试 View 默认不继承主 View Lighting 成本。
- Resize、RenderScale、释放顺序和租约数量必须出现在现有 RenderView 诊断中。

建议默认预算：

```csharp
new Lighting2DSettings(
    AmbientColor: new Vector3(0.08f),
    AmbientIntensity: 1f,
    BufferResolution: LightingResolution.Half,
    MaxVisibleLights: 256,
    MaxShadowCastingLights: 32,
    MaxShadowSegmentsPerLight: 512);
```

超过预算时按 `Priority → Camera 距离 → Light2DKey` 稳定选择，并报告丢弃数量；不能依赖 Dictionary 顺序或随机删除。

## 阶段 0：颜色空间与回归基线

目标：在任何“真实光照”进入项目之前，固定颜色纹理和数据纹理的解释方式。

实施内容：

- Texture 资产增加显式颜色空间：Albedo/颜色为 sRGB，Normal/Mask/Height 为 Linear。
- 明确 OpenGL 上传格式、采样解码、Linear Scene Surface、Tone Mapping 和最终显示编码。
- 选择并记录现有无 Lighting LDR 路径的兼容策略；不能静默改变所有旧 Sprite 明暗。
- 为 Sprite、Alpha Blend、Bloom 和 Tone Mapping 建立颜色空间 GPU 基线。
- `gameengine doctor` 或 AssetCompiler 对 Normal Map 被声明为 sRGB、颜色贴图被声明为 Linear 给出可理解诊断。

验收：

- 一张标准颜色测试图在 LDR 与 HDR/Linear 路径下最终显示一致。
- Normal/Mask 采样值保持原始线性数值。
- Alpha Blend 的参考像素符合选定颜色空间策略。
- 所有迁移都在文档和基线中显式体现。

## 阶段 1：无阴影 Environment + Point Light

目标：用最小完整切片验证 Lighting 数据面、每 View RenderSurface 链和开发者调用体验。

新增项目：

- `Engine.Features.Lighting2D`
- `Engine.Features.Lighting2D.Tests`
- `Engine.Features.Lighting2D.VisualTests`

实施内容：

- `LightingEnvironment2D`：Ambient Color、Intensity、Exposure。
- `PointLight2D`、`Light2DKey`、`LightLayerMask`、`ILight2DSource` 和零分配 Writer。
- 一个共享 Lighting Effect Runtime；所有可见灯批量写入同一 Light Mesh/Instance Batch。
- Light Buffer 支持 Full/Half/Quarter，默认 Half。
- Camera Bounds + Light Radius 剔除、稳定预算选择和每 View 独立 Profile。
- 平面受光模型：默认法线 `(0,0,1)`，Point Light Height 参与衰减方向。
- Lighting Composite 发布 `Lighting.lit`，并正确串入 Bloom/Tone Mapping。
- 不实现 Shadow、Normal Map、Cookie 或 Gameplay 可见度。

首版目标：每 View 最多渲染 256 个可见无阴影灯；世界注册数量不设较低硬上限，依靠 View 剔除。

验收：

- Ambient-only、单灯、多灯、重叠灯、屏幕边缘灯和完全不可见灯。
- 灯随 GameInstance 移动、失活、销毁和 Scene 切换。
- Full/Half/Quarter resize 尺寸正确，最后 View 释放后租约归零。
- Mirrored Viewport 不重复 Lighting Pass，独立 RenderView 明确重复成本。
- 热身后 Source 收集、剔除和批量构建保持 0 B/frame。
- GPU 基线覆盖 LDR/HDR、Bloom on/off 和不同分辨率。

## 阶段 2：Spot Light 与几何硬阴影

目标：支持地牢火把、墙壁遮挡、手电筒和俯视角室内场景。

实施内容：

- `SpotLight2D`：Direction、InnerAngle、OuterAngle、EdgeSoftness。
- Box、Circle、Convex Polygon ShadowCaster。
- Point/Spot Light 的 CPU 可见性多边形。
- Lighting 切片内部 `LightMeshBatch`，将多盏灯的可见区域批量提交，不一灯一个 Draw Call。
- 只查询 Light Radius 内、LayerMask 匹配的遮挡边。
- Caster Revision；静态几何只在变化时重建线段。
- 默认最多 32 个可见阴影灯、每灯 512 个候选线段；超限稳定降级并输出诊断。
- 阴影为硬边；Circle 使用配置化分段近似。

验收：

- 单墙、房间、门开关、圆柱、多个遮挡和灯在遮挡内部。
- 灯位于边/顶点、共线线段、零面积多边形和非法凸多边形快速失败。
- Spot 边界和 Shadow 边界组合正确。
- 动态 Caster 移动更新，静态 Caster 稳态不重复构建。
- 多 View 使用各自 Camera 剔除，但共享不变的静态世界几何缓存。
- 32 个阴影灯压力样例报告 CPU Build、候选边、输出三角形、Draw Call 和分配量。

## 阶段 3：Directional、接触阴影与投射阴影

目标：覆盖户外、平台游戏、角色落地感和不值得计算几何遮挡的小物体。

实施内容：

- `DirectionalLight2D`：Direction、Color、Intensity。
- Caster Height 和 Receiver Plane，按统一方向生成投影。
- `ProjectedShadowStyle`：Color、Opacity、Length、Skew、Scale、Receiver Layers。
- 椭圆 Contact Shadow 和 Sprite Alpha Projected Shadow。
- 角色 Elevation 控制阴影偏移、缩小和淡出。
- 假阴影进入独立 Layer/Pass，不能污染几何 ShadowCaster 数据。

验收：

- 太阳方向变化导致投影方向/长度变化。
- 角色跳起时接触阴影与 Sprite 分离且稳定插值。
- 负缩放、旋转、非中心 Origin 和动画帧正确。
- Projected Shadow 可关闭接收层，不影响 SceneGui 或不相关背景。
- 同屏大量 Contact Shadow 仍能使用 SpriteBatch 合批。

## 阶段 4：Lighting Material、Normal 与 Emission

目标：让 Sprite 表面参与逐像素光照，同时保持动画、Atlas、旋转和翻转正确。

推荐先实现声明式资产：

```json
{
  "schemaVersion": 1,
  "materials": [
    {
      "name": "player.lit",
      "albedo": "player.idle",
      "normal": "player.idle.normal",
      "emissive": "player.idle.emissive",
      "normalStrength": 1.0,
      "emissionStrength": 0.5,
      "normalConvention": "openGl"
    }
  ]
}
```

实施内容：

- `LightingMaterial2DRef` 与强类型生成引用。
- Albedo、Normal、Emission 的帧数、逻辑尺寸、Origin 和 Frame 索引校验。
- Texture/Sampler 材质参数，或 Lighting 专用多通道解析器；不通过裸 GL Handle 旁路。
- Atlas 编译器支持配对通道的稳定帧映射；不同 Atlas 页也能按当前帧解析。
- 扩展 RenderTarget/RenderSurface，形成能保持 Scene Draw 顺序的 Albedo/WorldNormal/Emission G-Buffer。优先选择明确的 Multi-Attachment 边界，而不是悄悄重绘一次不完整的 Scene。
- Normal 在水平/垂直翻转、旋转和非均匀缩放下转换到正确世界方向。
- Emission 在线性 HDR 中叠加并进入 Bloom。
- 材质预设：Unlit、FlatLit、NormalMapped、Emissive。

重要限制：Custom `OnDraw`、透明混合和 G-Buffer 写入必须有明确契约。在契约完成前，不能宣称任意自定义 Shader 自动获得 Normal/Emission 支持。

验收：

- 旋转、水平/垂直翻转和非中心 Origin 的法线方向。
- 多图片动画、规则图集、跨 Atlas 页材质帧。
- Normal 缺失时回退 FlatLit；Emission 缺失时为零。
- Alpha Cutout、半透明 Sprite 和重叠 Sprite 的 G-Buffer 结果有固定规则。
- AssetCompiler 拒绝错帧、错尺寸、错 Origin、错误颜色空间和无效 Normal 约定。
- Shader 热重载失败保持旧材质和旧 Pipeline 可用。

## 阶段 5：Light Cookie、Line Light 与环境介质

目标：用少量高价值能力覆盖霓虹、窗户、车灯、水下焦散和可见光束。

实施内容：

- Point/Spot Cookie Texture、旋转、缩放和强度。
- `LineLight2D`，避免用大量 Point Light 模拟灯管和霓虹。
- 低分辨率 Fog/Volumetric Overlay，第一版使用分析光锥或纹理，不做 Ray Marching。
- 水下 Caustics、树叶光斑等 Cookie 示例。
- Emissive 粒子和 Light 彼此独立；粒子是否受光由材质选择。

验收：

- Cookie 跟随灯光旋转，Linear/Srgb 语义正确。
- Line Light 比等效多个 Point Light 使用更少灯条目和更低 Overdraw。
- Fog/光束不改变 Gameplay 可见度，也不进入 ShadowCaster 逻辑。

## 阶段 6：软阴影与阴影后端评估

只有硬阴影已经产生真实美术需求和性能数据后才进入本阶段。

候选方案：

- Penumbra Geometry：适合有限几何和可控 Area Light。
- 多样本抖动：实现简单但成本近似乘以样本数。
- 极坐标 Shadow Map：适合大量 Point Light，但需要额外纹理格式、Pass 和缓存策略。
- SDF Ray Marching：适合静态 Tilemap，动态更新和内存成本更高。

进入条件：

- 至少一个真实 Playground/游戏明确需要软阴影。
- 已记录硬阴影在目标硬件上的灯数、线段数和帧时间。
- 能证明新后端比调低硬阴影对比度或使用 Contact Shadow 更值得。

本阶段不预先选定唯一算法；允许不同 Lighting Profile 选择不同 Shadow Backend，但不能让开发者 API 暴露 GL 实现细节。

## 阶段 7：空间索引、静态缓存与大场景

只有线性收集或每灯线段查询超过预算时引入。

候选优化：

- Scene 维护 Light Source/ShadowCaster 类型索引。
- Uniform Grid 或 Spatial Hash 查询 Light Radius 内 Caster。
- Tilemap Chunk 静态线段合并和 Revision Cache。
- 静态 Light + 静态 Caster 的 Visibility Mesh 缓存。
- 多 View 共享 Source/Caster 收集结果，但仍按各 Camera 选择可见灯。

保持规则：先测量再添加索引；优化不能改变稳定预算选择、遮挡边界或销毁语义。

## Gameplay 与表现层分离

Lighting 默认只影响画面。潜行检测、光照伤害、植物生长等玩法不得读取 GPU Light Buffer。

玩法侧应使用固定 Tick 的简化模型：

```text
Light Radius / Spot Angle
    + Gameplay Occluder Query
    + 确定性强度计算
    = Gameplay Visibility
```

- 纯表现 Flicker 可以使用渲染时间或噪声。
- 影响 AI 的 Flicker 必须使用固定 Tick 和确定性 `GameplayRandom`。
- Gameplay Signal 适合“灯已打开”“电源已断开”等离散事实，不适合连续灯光状态。
- Gameplay Light 与 Render Light 可以共享不可变设置值，但运行时结果和诊断保持分离。

## 真实场景配方

| 场景 | 最小组合 |
|---|---|
| 地牢火把 | 冷暗 Ambient + 暖 Point + 墙体硬阴影 + 轻微 Flicker |
| 夜间街道 | 蓝色 Ambient + 暖 Spot/Line + Directional 投影 + Emission/Bloom |
| 俯视射击 | 少量阴影灯 + 大量短命无阴影枪口/爆炸灯 + Emissive 粒子 |
| 室内房间 | 房间 Ambient + 门窗 Spot/Area + 动态门 ShadowCaster |
| 平台游戏 | Directional Sun + Contact Shadow + 少量局部 Point Light |
| 科幻霓虹 | Line Light + Emission + Bloom，降低阴影对比 |
| 水下场景 | 蓝绿 Ambient + Caustics Cookie + Fog Overlay + 柔和低对比阴影 |
| 潜行游戏 | Spot/Point 表现 + 独立固定 Tick Gameplay Visibility |

## 诊断与性能预算

Lighting 诊断应并入现有 Runtime Render Diagnostics：

```text
Registered Light Sources
Collected / Visible / Rendered Lights
Shadow Candidates / Rendered Shadow Lights
Dropped By Light Budget / Shadow Budget
Collected Casters / Candidate Segments
Visibility Vertices / Triangles
Light Mesh Draw Calls
Light Buffer Resolution / Format / Bytes
Collect / Cull / Shadow Build / GPU Pass Time
```

第一阶段目标预算不是跨硬件承诺，而是默认保护线：

- 256 个可见无阴影灯。
- 32 个可见硬阴影灯。
- 每个阴影灯最多 512 个候选线段。
- Half Resolution Light Buffer。
- 灯光 Mesh 在相同状态下批量提交，不允许固定一灯一个 Draw Call。
- Source/Caster 收集和稳态 Mesh Buffer 复用保持 0 B/frame。

GPU Visual Regression 至少覆盖：Ambient-only、单点光、多点重叠、硬阴影、Spot、Directional、Normal 翻转、Emission + Bloom、resize、release 和多 View 成本隔离。

## 项目与依赖方向

第一阶段项目：

```text
Engine.Features.Lighting2D
├─ Engine.Core
├─ Engine.Features.Camera
├─ Engine.Features.RenderPipeline
├─ Engine.Features.Presentation
└─ Engine.Features.ShaderAssets（按实际装配需要）

Engine.Features.Lighting2D.Tests
Engine.Features.Lighting2D.VisualTests
```

后续材质资产可以增加 Lighting2D 对 ContentAssets、TextureAssets、Sprites 和 TextureAtlas 的依赖，但这些底层切片不能反向依赖 Lighting2D。StencilMasking 与 Lighting2D 保持独立；两者可以同时消费 SceneColor/DepthStencil 边界，但互不拥有对方 Runtime。

## 建议的首次实施切片

首次实现只包含两个连续、可独立提交的里程碑：

1. 颜色空间契约与 GPU 基线，不新增灯光 API。
2. Environment + 无阴影 Point Light + 每 View Light Buffer/Composite。

明确不夹带：ShadowCaster、Spot、Normal、Emission、Cookie、Fog、Soft Shadow、Gameplay Visibility 或空间索引。

完成这两个里程碑后，用一个独立 Lighting Playground 验证：夜间环境、静态灯、跟随玩家的灯、短命枪口灯、256 灯预算、两个 Camera 中仅主 View 启用 Lighting、Bloom 串联、resize 和释放。画面与性能数据通过后，再进入几何硬阴影阶段。

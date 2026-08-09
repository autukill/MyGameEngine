# Cooldown UI Effects 需求记录

本需求作为后续 `Engine.Features.UiEffects` 垂直切片保存，不纳入当前 Present/Stencil 实施。

## 目标

为技能、道具和能力图标提供常见的 CD 冷却倒计时视觉：图标剩余区域蒙灰，随着冷却推进按指定方式逐步恢复原色。

该能力属于 Tone Mapping 之后的 LDR GUI 绘制效果，不使用 Stencil，也不应进入 HDR、曝光或 Bloom 链路。

## 固定进度语义

- `Remaining01 = 1`：刚进入冷却，整个有效区域蒙灰。
- `Remaining01 = 0`：冷却结束，不再蒙灰。
- 输入在绘制时钳制到 `[0,1]`。
- 默认起点为 12 点方向，默认顺时针清除灰色区域。
- 旋转继续使用项目弧度约定，不引入 GMS 角度制。

## 裁剪形状

- Sprite Alpha：使用 Sprite 当前帧的 Alpha，包括 Atlas UV、透明洞和多图片动画。
- Circle：圆形区域。
- Ring：可配置内半径的圆环。
- RoundedRectangle：可配置圆角比例的圆角矩形。
- Arc：通过 Ring、起始角和角度跨度组合表达。

## 进度方式

- RadialSweep：扇形/环形顺时针或逆时针扫描。
- RadialShrink：从外向内或从内向外收缩。
- LinearHorizontal：水平清除。
- LinearVertical：垂直清除。

形状和进度方式必须正交组合，例如 Circle + RadialSweep、Ring + RadialSweep、RoundedRectangle + LinearVertical。

## 外观设置

- OverlayColor。
- OverlayOpacity。
- Desaturation。
- Darkness。
- EdgeSoftness。
- 后续可扩展完成闪烁、边缘发光与数字倒计时，但不属于 v1。

## Sprite 语义

- Position 对应 Sprite 原点。
- 支持 `SubImage`、旋转、非均匀缩放和负缩放。
- 帧解析继续使用 `ISpriteResolver`，支持 Atlas 与跨 Texture 动画。
- Texture UV 与 `0..1` Local UV 必须作为两个独立顶点属性；程序化形状不能使用 Atlas UV 计算中心。

## 批处理要求

不为每个图标更新独立 uniform。后续新增专用 `CooldownBatch`，把进度、形状、方向、颜色和边缘参数写入顶点数据；不同进度和形状不应打断 Batch，只有纹理切换或缓冲区满时 Flush。

## Shader 边界

- 一次采样与一次绘制完成原图和蒙灰混合。
- Circle、Ring 与 RoundedRectangle 使用解析距离/SDF。
- 使用 `fwidth` 提供约一个像素的抗锯齿软边。
- 输出仍采用当前 straight-alpha RGBA8/Display 约定。
- Stencil 是二值缓冲，不承担渐变、羽化或蒙灰混合。

## 测试要求

- `Remaining01` 为 `1、0.75、0.5、0.25、0`。
- 顺时针与逆时针。
- Circle、Ring、RoundedRectangle、Sprite Alpha 与 Arc。
- Atlas 帧、跨纹理帧、透明洞、旋转、翻转和 resize。
- Exposure 或 Bloom 设置改变时 GUI 像素保持不变。
- 批处理中不同进度和形状不会产生额外 Draw Call，纹理切换行为可预测。

## 推荐实施顺序

1. 先完成显式 Present 与 LDR SceneGui Surface。
2. 完成 Circle/Sprite Alpha Stencil，验证共享 Mask 几何语义。
3. 新增 `Engine.Features.UiEffects`、`CooldownDrawCommand` 与 `CooldownBatch`。
4. 增加固定时间步动画 GPU 基线。

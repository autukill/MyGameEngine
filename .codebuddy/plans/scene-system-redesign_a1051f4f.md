---
name: scene-system-redesign
overview: 重新设计 Scene 系统，将 Viewport/Camera、Layer、Background、Hook 生命周期、Instance 管理统一收敛到 SceneAggregate 聚合根中，对齐 GMS Room 的完整能力，并保持 DDD + VSA 架构一致性。
todos:
  - id: domain-layer-config
    content: 在 Engine.Core/Domain/ValueObjects/ 新增 SceneLayerConfig、BackgroundConfig 值对象，在 Domain/Events/ 新增场景级领域事件
    status: completed
  - id: domain-scene-aggregate
    content: 重构 SceneAggregate 聚合根：新增 Viewport 尺寸、Layer 配置管理、Background 配置、Scene 生命周期 Hook、按 Layer 分组的 DrawActive 渲染调度
    status: completed
    dependencies:
      - domain-layer-config
  - id: entity-layer-name
    content: 为 GameInstance 新增 LayerName 属性，适配按图层归属的渲染流程
    status: completed
    dependencies:
      - domain-scene-aggregate
  - id: application-commands
    content: 扩展 Commands.cs 和 SceneCommandHandlers.cs，新增 AddLayer、SetBackground 等命令及其处理器
    status: completed
    dependencies:
      - domain-scene-aggregate
  - id: render-adaptation
    content: 适配 SceneRenderPass 为 Layer 感知渲染流程，废弃 SceneRenderContext 桥接层
    status: completed
    dependencies:
      - domain-scene-aggregate
  - id: runner-tests
    content: 更新 MyGame.Runner/Program.cs 和 Engine.DddTests/Program.cs，使用新的 Scene API 装配场景
    status: completed
    dependencies:
      - render-adaptation
      - application-commands
---

## 用户需求

重构 Scene 系统，使其成为类似 GameMaker Room 的统一管理器，负责管理：

- **Viewport**：场景视口尺寸（只存尺寸，Camera 是渲染关注点）
- **GameInstance**：场景内所有游戏实例的完整生命周期
- **Layer**：图层的名称、深度次序、可见性配置
- **Background**：场景背景颜色、精灵、平铺模式
- **Hook**：场景级生命周期回调（OnStart/OnEnd/OnBeforeStep/OnAfterStep）

## 当前核心矛盾

1. `SceneAggregate`（Domain 聚合根）只管理 GameInstance → 缺失 Layer、Background、Viewport、Hook
2. `SceneRenderContext`（Infrastructure 临时桥接）管 Layer+Camera → 又不是聚合根，注释写明"Phase 1.4 之后废弃"
3. 渲染管线（SceneRenderPass）直接用 `SceneAggregate.DrawActive()` 遍历所有实例，完全绕过 Layer 系统，导致 Layer 按层分组渲染的设计从未生效
4. GameInstance 没有 `LayerName` 属性，无法标记自己属于哪个 Layer

## 核心设计原则

- 遵循 DDD + VSA 架构，Scene 是聚合根
- 保持 Scene 上下文约束：不直接调 OpenGL，不计算碰撞
- Camera2D 留在 Camera feature，不归属 Scene（它是渲染关注点）
- 废弃 SceneRenderContext，用增强后的 SceneAggregate 替代其 Layer 管理职能

## 技术方案

### 架构思路

采用"DDD 聚合根增强 + Infrastructure 适配"策略，不引入新架构模式。所有 Domain 层新增类型均为 `readonly record struct`（零 GC）。

### 核心设计决策

**1. SceneAggregate 增强为完整聚合根**

- 拥有 `ViewportWidth/ViewportHeight`：作为 Room 尺寸定义（Scene 上下文职责）
- 拥有 `SceneLayerConfig` 列表：领域层元数据（Name/DepthOrder/IsVisible），不含渲染逻辑
- 拥有 `BackgroundConfig` 值对象：描述背景颜色、精灵引用、平铺模式
- 拥有 Scene 生命周期 Hook 委托：`OnBeforeStep` / `OnAfterStep` / `OnStart` / `OnEnd`
- Layer 深度排序在聚合根内维护（`AddLayer` 时插入排序）

**2. GameInstance 轻量扩展**

- 新增 `LayerName` 属性（默认为 "Instances"），标记归属图层
- `DrawActive` 改为按 Layer 分组遍历：先遍历 Layer 配置（含 IsVisible 过滤），再遍历该 Layer 下的实例
- 实例的 `OnDraw` 不变，防御式设计：无 LayerName 的实例降级到 "Instances" 默认 Layer

**3. Infrastructure Layer 适配**

- 现有 `Layer` 类改为纯渲染辅助器：接收 `SceneLayerConfig` + `IEnumerable<RenderCommand>`，负责 GPU 提交
- `SceneRenderContext` 标记 `[Obsolete]`，移除 `Camera2D` 管理职责
- `SceneRenderPass` 增强为 Layer 感知：从 `SceneAggregate` 读取 Layer 配置，每层之间可 apply GL 状态覆盖

**4. Camera 职责不变**

- `Camera2D` 仍由渲染 Pass 层持有和注入（不在 SceneAggregate）
- `SceneRenderPass` 构造函数参数保持 `(SceneAggregate, Camera2D, RenderTarget2D?)`

### 性能考虑

- `SceneLayerConfig` 用 `readonly record struct`，Layer 列表用 `List<SceneLayerConfig>`（遍历开销 O(Layer数)，通常 < 10）
- 每帧 `DrawActive` 按 Layer 分组 O(N)，N 为实例数，无额外内存分配
- Background 用值对象，默认值（纯色）零分配

### 向后兼容

- `SceneAggregate.DrawActive(ISpriteBatch)` 行为变更：按 Layer 分组而非全局 Depth 排序
- 新增重载 `DrawActive(ISpriteBatch, string layerName)` 支持单 Layer 渲染（StencilMaskPass 场景）
- 旧 `SceneRenderContext` 保留但标记废弃，不影响编译
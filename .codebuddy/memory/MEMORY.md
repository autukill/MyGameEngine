# 项目长期记忆 (MEMORY.md)

## 项目概况
- **项目**：`MyGameEngine` — 基于 C# 自行构建的 2D 游戏引擎，从 0 到 1 再逐步完善。
- **技术栈**：C# (.NET 10, ASP.NET Core) + Silk.NET (底层 OpenGL/Vulkan/DirectX 绑定) + Vue 3 (可选编辑器前端) + AI Agent (Gemini/DeepSeek) 辅助开发。
- **设计依据文档**：`docs/C# 2D 游戏引擎从零构建.md` —— 该文档是本项目架构与实施的主参考。

## 核心架构准则（必须始终遵循）

### 1. DDD 战略设计：划分限界上下文 (Bounded Contexts)
引擎划分为 4 个核心限界上下文：
1. **Scene & Instance Context（场景与实例上下文）**：管理实例生命周期（Create/Step/Destroy）、挂载数据组件、响应游戏逻辑。约束：不直接调用任何 OpenGL/Vulkan 绘图指令，也不计算空间碰撞。
2. **Graphics & Render Context（图形与渲染上下文）**：Render Target、Stencil Masking、Sprite Batching、Camera/Viewport 矩阵映射。接收场景层发出的"渲染需求"领域事件。
3. **Physics & Spatial Context（物理与空间上下文）**：空间分割（QuadTree/Spatial Hash），提供高效空间查询（对标 GMS `place_meeting`）。
4. **AI Agent Bridge Context（AI 协同上下文）**：暴露 Engine API Schema、Roslyn 动态代码解析/热重载，方便 AI Agent 生成游戏逻辑切片。

### 2. DDD 战术设计（DDD 构建块）
- **值对象 (Value Objects)**：不可变、无身份标识，用 C# `readonly record struct` 实现零 GC 分配。例如 `Vector2D`、`Transform2D`、`InstanceId`、`LayerDepth`、`StencilState`。
- **实体 (Entities)**：有唯一标识 (`InstanceId`)，状态随 Step 循环变化，如 `GameInstance`。
- **聚合根 (Aggregate Root)**：`SceneAggregate`，负责维护实例集合强一致性（InstanceId 唯一、空间索引同步）。
- **领域事件 (Domain Events)**：解耦逻辑层与渲染管线，例如 `InstanceSpawnedEvent`、`InstanceMovedEvent`、`StencilMaskPassRequestedEvent`。

### 3. 垂直切片架构 (VSA)
- **不按"大层"（所有渲染层/物理层）划分，而是按功能切片 (Feature Slices) 开发**。
- 每个切片 = 一个完整闭环（状态 + Command + 领域事件 + Handler），文件集中在单一文件夹，便于 AI Agent 局部生成/修改。
- 示例切片结构：`Engine.Features/StencilMasking/`、`SpatialCollision/`、`SpriteAnimation/`、`AgentBridge/`、`TextureAtlas/`、`Camera/`。
- 切片核心示例：`StencilMasking`（解决 GMS 无法操作 `GL_STENCIL_TEST` 的痛点）。
- 关键原则：**每次改变 GPU 状态（开启/关闭 Stencil、切换混合模式）之前必须显式 `SpriteBatch.Flush()`**，避免批处理内容被错误施加遮罩。

### 4. 目录结构约定
```
src/
├── Engine.Core/           # 引擎核心基础设施（跨切片共享）：Domain / Application / Infrastructure
├── Engine.Features/       # 引擎功能垂直切片区（每个 Feature 是独立 module project + 独立测试目录）
│   ├── Camera/            Camera.csproj             (无 Silk 依赖，最底层)
│   ├── Camera.Tests/      Camera.Tests.csproj       (冒烟测试)
│   ├── Camera.VisualTests/ Camera.VisualTests.csproj (图形看效果 Demo)
│   ├── ContentAssets/    ContentAssets.csproj        (声明式 Texture/Sprite 包装配)
│   ├── ContentAssets.Tests/ ContentAssets.Tests.csproj
│   ├── RenderPipeline/    RenderPipeline.csproj      (依赖 Camera + Silk.NET.OpenGL)
│   ├── RenderPipeline.Tests/
│   ├── RenderPipeline.VisualTests/
│   ├── SceneSystem/       SceneSystem.csproj        (依赖 Camera + RenderPipeline)
│   ├── SceneSystem.Tests/
│   ├── SceneSystem.VisualTests/
│   ├── Sprites/         Sprites.csproj              (逻辑 Sprite 资源库)
│   ├── Sprites.Tests/   Sprites.Tests.csproj        (帧/动画/几何测试)
│   ├── Sprites.VisualTests/ Sprites.VisualTests.csproj
│   ├── TextureAssets/    TextureAssets.csproj         (PNG/WebP 解码与 GPU 所有权)
│   ├── TextureAssets.Tests/ TextureAssets.Tests.csproj
│   ├── StencilMasking/    StencilMasking.csproj     (依赖 Camera + RenderPipeline + Silk.NET.OpenGL)
│   ├── StencilMasking.Tests/
│   └── StencilMasking.VisualTests/
├── Engine.Editor.Web/     # (可选) ASP.NET Core + Vue 3 编辑器服务
├── Engine.DddTests/       # DDD 测试
└── MyGame.Runner/         # 游戏主程序入口 (Native AOT 发布)
```
关键约定：`Xxx.Tests` 目录下可放多个测试项目（冒烟 + VisualTests）；命名空间固定 `GameEngine.Features.Xxx.*`，拆模块不改变 namespace，只改 csproj 引用。依赖方向单向无循环：Camera（底）← RenderPipeline ← SceneSystem/StencilMasking。

## 实施路线（Phase 0 -> 1 -> 100）
- **Phase 0 -> 1（MVE 最小可行引擎）**：Silk.NET 窗口初始化 + 8-bit Stencil Buffer；`SpriteBatch` 零 GC 批处理渲染器；`StencilMasking` 切片；`TextureAtlas` 图集（Shelf Bin Packing）减少纹理切换；GMS 式生命周期主循环 `PreStep -> Step -> PostStep -> DrawBegin -> Draw -> DrawGUI`。
- **Phase 1 -> 10（功能对齐 GM 特性超越）**：`SceneAggregate` + Layer 深度排序 + Multi-Camera 矩阵；Spatial Hash 碰撞查询（`place_meeting` 复杂度 O(N) 降为 O(1)）；Source Generator 静态事件调度。
- **Phase 10 -> 100（AI Agent 原生协同）**：AI 友好 Schema/DSL 建模导出 JSON Schema；Vue 3 编辑器（SignalR/gRPC Bridge）；Native AOT 编译极速发布。

## 关键性能设计取向
- 追求**零 GC / Zero-Allocation**：用 `readonly record struct`、`Span<T>`、`MemoryMarshal.Cast`、静态 EBO + 动态 VBO。
- 追求**极低 DrawCall**：Texture Atlas 让数千精灵共享同一纹理实现单次 DrawCall。
- `SpriteBatch` 支持状态打断自动 Flush（纹理变更 / BlendMode 变更 / 缓冲满）。

## 当前代码进度（2026-08-08）
- `src/Engine.Core`：Domain 含逻辑 SpriteRef、SpriteDrawCommand/Metadata/Resolver、Blend/Shader/Input 抽象；Infrastructure 含 SpriteBatch 状态机、Shader 与 Input 实现。
- `src/Engine.Features`：7 个独立 module project + 12 个测试项目（7 Feature 冒烟 + 5 VisualTests）。ContentAssets 依赖 TextureAssets + Sprites，支持版本化 `assets.json`、依赖图、Single/Grid/多图片 Frames、引用计数和原子回滚。
- `SpriteLibrary` 逐帧保存 `TextureRef + PixelRectI`；同一动画可跨多个独立纹理，未来 Atlas 只重映射帧来源，不改变 SpriteRef、GameInstance 或 Draw API。
- `src/MyGame.Runner`：通过自己的 `Assets/assets.json` 装配 OrbitingSprite 与白纹理；统一使用 EngineWindow.Input，并已接入 DrawGUI、resize 与包/纹理关闭释放链路。
- 解决方案 `MyGameEngine.slnx` 共 22 个项目；8 个无窗口冒烟项目（含 Engine.DddTests）。
- **待办**：离线 TextureAtlas 构建切片（大帧旁路、跨页动画）；第二阶段事件装配（RenderEffectRequested + RenderTargetPool）；自动 GPU 回归测试。

## 设计方向（2026-08-07 推演，第一阶段已实施）
- **用户明确偏好**：游戏/测试业务逻辑必须放入 GameInstance 子类，像 GMS 一样通过实例事件（Create/BeginStep/Step/EndStep/BeginDraw/Draw/EndDraw/DrawGUI/输入事件）控制业务逻辑与 shader/blend mode；Program 只做装配（组合根）。当前 VisualTests 逻辑堆在 Program 静态方法是"缺事件模型"导致的临时形态。
- **渲染管理三层模型**：拓扑（Pass 图，组合根装配）／状态（实例 RenderStyle 值对象声明 blend/depth，SceneRenderPass 做 diff→Flush→Apply→复位状态机）／特需（实例发 RenderEffectRequested 事件，PipelineBuilder 消费装配，泛化 RequestStencilMask 范式）。
- **Shader 引用**：ShaderRef 值对象 + ShaderLibrary 解析，实例绝不持有 GL 对象（保 VSA 依赖方向）。
- **BlendState 归属**：Feature 层 BlendState 依赖 Silk 不能提升 Core；Core 需自建 BlendMode 枚举供 ISpriteBatch 用。

## 开发工作流偏好
- 偏好使用 DDD 头脑风暴 -> 战略设计 -> 战术设计 -> 垂直切片实施的渐进式流程。
- 使用 AI Agent 辅助开发，切片化便于 Agent 局部工作。
- 用户偏好"先思考推演最佳实践再动手"，重大设计先给推演再确认实施。
- `docs/` 采用渐进式维护：公共 API、清单格式、所有权、限制或里程碑随对应代码在同一提交更新；入口为 `docs/README.md`。

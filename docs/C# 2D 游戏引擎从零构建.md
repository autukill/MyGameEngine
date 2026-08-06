# C# 2D 游戏引擎从零构建

## Prompt:
Gamemaker Studio 2 是闭源 的 2d 游戏引擎. 单开源了 html5 Runtime: (https://github.com/YoYoGames/GameMaker-HTML5), 有些 图形API 都没提供, 且难以扩展集成, 比如 opengl 中的 GL_STENCIL_TEST 来实现遮罩功能, Gamemaker  就没有提供相关 function.   GameMaker-Manual : `https://github.com/YoYoGames/GameMaker-Manual`  A rewrite of the GameMaker Classic engine runners with additional tooling: https://github.com/OpenGMK/OpenGMK`  `https://github.com/misternebula/OpenGM`  我喜好 C# (dotnet 10, asp.net core), vue3, 独立游戏, 现在还有了 LLM AI agent Gemini, deepseek..., coding copliot, DDD  Vertical Slice Architecture   我想自行 ( AI Agents )采用 C# 实现一套自己的 2d 游戏引擎, 需要你分析 Gamemaker, 给出一个从 0 -> 1 , 再 从 1 -> 100 的 渐进式的 实施计划  下面, 或许你可以 DDD 头脑风暴开始, 再到 战略设计... 战术设计... 等

## Response:
将 GameMaker Studio (GMS) 的**易用事件模型**与 **C# .NET 10 的极致性能**（Native AOT、Hardware Intrinsics）、**DDD 战略设计** 以及 **AI Agent 辅助编程** 结合，完全可以打造一套既具备 GMS 开发效率、又拥有现代架构与底层图形操控权（如 Stencil Buffer 深度自定义）的 2D 游戏引擎。

下面从 **GameMaker 领域拆解**、**DDD 战略/战术设计**、**垂直切片架构 (VSA)** 到 **0 -> 1 -> 100 的渐进式实施计划** 为你进行全面规划。

## 1\. GameMaker 架构痛点与 DDD 领域解构

GameMaker 核心机制在于 **"面向对象/事件驱动 (Object-Event Model)"** 和 **"隐藏式渲染管线"**。要重构它，首先要对 GMS 的概念进行 DDD 领域映射：

| GMS 概念 | DDD 映射 / 领域术语 | GMS 痛点分析 | 现代 C# 改进方案 |
| --- | --- | --- | --- |
| **Room** | `Scene` 聚合根 (Aggregate Root) | 空间层级耦合，渲染与逻辑绑定 | 基于 Spatial Hash / QuadTree 的场景图，逻辑与渲染层彻底分离 |
| **Object / Instance** | `GameInstance` 实体 / 组件集合 | 庞大的单体基类，内存不连续，扩展极难 | 采用 **VSA (垂直切片)** 或 **轻量 Entity-Component**，按需组合 |
| **Events (Step, Draw...)** | `DomainEvent` 管道 | 固化的生命周期，无法自由插入 API（如 Stencil 模板测试） | 可插拔的生命周期管道，开放底层 `RenderPass` 和 `GraphicsDevice` |
| **Sprite / Tilemap** | `VisualAsset` 值对象 (Value Object) | 图集管理粒度粗 | 自动 Texture Atlas 批处理 + Compute Shader / Direct Batching |

> **关键打破**：GMS 最大的瓶颈在于其渲染管线是黑盒。采用 C# 时，我们底层选择 **Silk.NET** (直接操作 OpenGL/Vulkan/DirectX) 或 **Veldrid**，使得像 `GL_STENCIL_TEST` 这种图形 API 成为 **可被显式调用的 RenderPass**，而不是传不到底层的限制。

## 2\. 战略设计 (Strategic Design) & 领域划分

将引擎划分为 4 个核心限界上下文 (Bounded Contexts)：

                               ```
                               ┌──────────────────────────────────┐
                               │     AI Agent Integration Context │
                               └────────────────┬─────────────────┘
                                                │ (API Schema / AST)
                                                ▼
┌──────────────────────────────┐       ┌──────────────────────────┐
│   Scene & Instance Context   │◄─────►│ Graphics & Render Context│
│  (Lifecycle, World, Nodes)   │ Domain│ (Batching, Stencil, Shader)│
└──────────────┬───────────────┘ Events └──────────────────────────┘
               │                                ▲
               ▼                                │
┌──────────────────────────────┐                │
│  Physics & Collision Context │────────────────┘
│  (Spatial Hash, Raycast, AABB)
└──────────────────────────────┘
```

1.  **Scene & Instance Context（场景与实例上下文）**
    
    *   **职责**：维护实例生命周期（Create, Step, Destroy）、挂载数据组件、响应游戏逻辑。
        
2.  **Graphics & Render Context（图形与渲染上下文）**
    
    *   **职责**：处理 Render Target、Stencil Masking、Sprite Batching、Camera/Viewport 矩阵映射。
        
3.  **Physics & Spatial Context（物理与碰撞上下文）**
    
    *   **职责**：空间分割（QuadTree/Spatial Hash），提供类似 GMS `place_meeting` / `instance_place` 的高效空间查询。
        
4.  **AI Agent Bridge Context（AI 协同与工具链上下文）**
    
    *   **职责**：暴露 Engine API Schema、Roslyn 动态代码解析/热重载，方便 AI Agent (Gemini/DeepSeek) 自动生成游戏逻辑切片。
        

## 3\. 战术设计 (Tactical Design) 与 垂直切片 (VSA)

采用 **Vertical Slice Architecture (VSA)** 来设计引擎功能与游戏逻辑。不按传统的“大层”（所有渲染层、所有物理层）划分，而是**按功能切片 (Feature Slices)** 开发：

```
Features/
├── RenderMasking/            # 针对 Stencil 遮罩功能的垂直切片
│   ├── ApplyStencilMaskCommand.cs
│   ├── StencilRenderPassHandler.cs
│   └── StencilStateValueObject.cs
├── InstanceLifecycle/        # 实例生命周期切片
│   ├── StepRunner.cs
│   └── InstanceDestroyedEvent.cs
└── SpatialCollision/         # 碰撞检测切片
    ├── CheckCollisionQuery.cs
    └── QuadTreeManager.cs
```

## 4\. 0 -> 1 -> 100 渐进式实施路线图

### 🚩 Phase 0 -> 1：最小可行引擎 (MVE - Minimal Viable Engine)

> **目标**：建立最基础的跨平台 Windowing，实现 GMS 式 Step/Draw 循环，并**显式支持 GL\_STENCIL\_TEST 遮罩**。

*   **Step 0.1：基础设施与底层绑定**
    
    *   框架选择：`.NET 10` + `Silk.NET.OpenGL`（或 Silk.NET.Vulkan）。
        
    *   建立基础设施：创建 WindowContext，初始化 OpenGL 3.3/4.5 Core Profile，显式开启 Stencil Buffer (`GL_STENCIL_BUFFER_BIT`)。
        
*   **Step 0.2：渲染切片 - Batcher & Stencil Pass**
    
    *   构建 `SpriteBatch` 处理 2D 顶点（Position, UV, Color）。
        
    *   实现 `StencilMaskSlice`：
        
        1.  启用 `GL_STENCIL_TEST`。
            
        2.  设置 `glStencilFunc(GL_ALWAYS, 1, 0xFF)` 与 `glStencilOp(GL_KEEP, GL_KEEP, GL_REPLACE)` 绘制遮罩形状。
            
        3.  设置 `glStencilFunc(GL_EQUAL, 1, 0xFF)` 绘制被遮罩内容。
            
*   **Step 0.3：逻辑切片 - GMS 生命周期核心循环**
    
    *   定义 `IGameInstance` 接口与基础属性（`X`, `Y`, `Depth`, `Persistent`）。
        
    *   实现核心 Loop 主循环：`PreStep` -> `Step` -> `PostStep` -> `DrawBegin` -> `Draw` -> `DrawGUI`。
        

### 🚩 Phase 1 -> 10：功能对齐与 GM 特性超越

> **目标**：具备完整 2D 独立游戏开发能力（Layer、Camera、QuadTree 物理、Tilemap、音效）。

*   **Step 1.1：场景与图层系统 (Scene & Layer Context)**
    
    *   引入 `Layer` 概念：Instance Layer, Asset Layer, Tile Layer。
        
    *   根据 `Depth` / `Layer` 自动对渲染队列排序，减少 DrawCall。
        
*   **Step 1.2：空间索引与碰撞查询 (Physics Slice)**
    
    *   实现 `SpatialHash` / `QuadTree` 算法。
        
    *   提供极简 API：`Physics.PlaceMeeting(x, y, targetType)`，比 GMS 的 O(N) 逐像素碰撞快数倍。
        
*   **Step 1.3：自定义管道与多 Viewport/Camera**
    
    *   实现 RenderTarget2D 与 Multi-Camera 映射。
        
    *   允许给每个 Layer 附加自定义 Shader 和 BlendState（彻底解决 GMS 无法自由组合混合模式与 stencil 的难题）。
        
*   **Step 1.4：C# 10 Source Generator 静态代码生成**
    
    *   利用 Source Generator 自动扫描带有 `[GameEvent]` 的类，生成零反射的低开销事件调度器。
        

### 🚩 Phase 10 -> 100：AI Agent 原生协同与生态构建

> **目标**：打造 **"Agent-Driven Engine"**，利用 C# Ecosystem + Vue 3 可视化工具 + Gemini/DeepSeek 实现高效游戏工程产出。

*   **Step 10.1：AI 友好型 Schema 与 DSL 建模**
    
    *   定义引擎 API 的 C# Context Schema（导出 JSON Schema / TypeScript 定义）。
        
    *   让 Gemini/DeepSeek Agent 能够精准理解 Engine API（例如：“用 Stencil 切片创建一个光照遮罩”），自动生成合规的 VSA Slice 代码。
        
*   **Step 10.2：开发 Vue 3 Web 控制台 / 编辑器（可选）**
    
    *   利用 ASP.NET Core WebSockets / gRPC 作为 Bridge。
        
    *   编写 Vue 3 前端实现可调参的编辑器 UI（Scene View, Entity Inspector, Tilemap Editor），通过 SignalR 与 C# 引擎进程实时交互。
        
*   **Step 10.3：Native AOT 编译与极速发布**
    
    *   优化代码以完全兼容 .NET 10 Native AOT，去除动态反射。
        
    *   游戏一键编译为无依赖的单一 Native 扩展二进制文件，启动时间 < 50ms。
        

## 5\. 关键底层技术验证 (PoC 代码示例)

下面是在 C# (`Silk.NET.OpenGL`) 中实现 **GMS 缺失的 Stencil 遮罩功能** 的战术切片参考：

```c#
public class StencilMaskRenderSlice
{
    private readonly GL _gl;

    public StencilMaskRenderSlice(GL gl) => _gl = gl;

    public void ExecuteMaskedDraw(Action drawMaskShape, Action drawContent)
    {
        // 1. 开启模板测试并清空 Buffer
        _gl.Enable(EnableCap.StencilTest);
        _gl.Clear((uint)ClearBufferMask.StencilBufferBit);

        // 2. 配置模板写入模式：只写模板缓冲，关闭颜色写入
        _gl.ColorMask(false, false, false, false);
        _gl.StencilFunc(StencilFunction.Always, 1, 0xFF);
        _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
        _gl.StencilMask(0xFF);

        // 绘制遮罩图形 (比如一个圆形或多边形)
        drawMaskShape();

        // 3. 配置模板测试模式：只在模板值为 1 的区域绘制颜色
        _gl.ColorMask(true, true, true, true);
        _gl.StencilFunc(StencilFunction.Equal, 1, 0xFF);
        _gl.StencilMask(0x00); // 禁用模板写入

        // 绘制被遮罩覆盖的内容
        drawContent();

        // 4. 重置状态
        _gl.Disable(EnableCap.StencilTest);
    }
}
```

## Prompt:
请详细展开 C# 2D 游戏引擎的 DDD 战略与战术设计，包含完整的实体(Entities)、聚合根(Aggregates)和领域事件(Domain Events)定义示例。

## Response:
在基于 GameMaker (GMS) 思想与 C# .NET 10 构建 2D 游戏引擎时，引入 **DDD (领域驱动设计)** 可以解决传统游戏引擎设计中“逻辑、物理、图形渲染高度耦合”的痛点 。

下面将为你深入展开 **战略设计** 与 **战术设计** 的完整架构，并提供符合现代 C#（采用 .NET 10 特性如 `record struct`、主构造函数等）的强类型代码实现。

## 一、 战略设计 (Strategic Design) 深化

战略设计的核心在于**划分限界上下文 (Bounded Context)**，并明确它们之间的交互关系与数据边界 。

```
+-------------------------------------------------------------------------+
|                      1. Scene & Lifecycle Context                       |
|  (聚合根: SceneAggregate | 实体: GameInstance | 领域事件: InstanceSpawned)  |
+------------------------------------+------------------------------------+
                                     |
                         Domain Events (解耦通信)
                                     |
            +------------------------+------------------------+
            |                                                 |
            v                                                 v
+-----------------------+                         +-----------------------+
|  2. Physics & Spatial |                         |   3. Graphics & Render|
|        Context        |                         |        Context        |
|  (空间Hash, AABB碰撞)  |                         | (SpriteBatch, Stencil)|
+-----------------------+                         +-----------------------+
```

### 1\. 限界上下文 (Bounded Contexts) 职责划分

*   **Scene & Lifecycle Context (场景与生命周期上下文)**
    
    *   **核心职责**：管理游戏对象的生成、销毁、层级挂载与 Step 生命周期更新 。
        
    *   **核心约束**：不直接调用任何 OpenGL/Vulkan 底层绘图指令，也不直接计算复杂的空间物理碰撞；仅维护游戏逻辑状态 。
        
*   **Graphics & Render Context (图形与渲染上下文)**
    
    *   **核心职责**：接收来自场景层发出的“渲染需求”领域事件，管理 GPU Buffer、Texture Atlas、Shader 管道以及 **Stencil 遮罩测试 (GL\_STENCIL\_TEST)** 。
        
*   **Physics & Spatial Context (物理与空间上下文)**
    
    *   **核心职责**：维护 QuadTree / Spatial Hash 结构 。响应场景实体的移动，高效执行 `PlaceMeeting` 或范围查询 。
        
*   **AI Agent Bridge & Scripting Context (AI 协同上下文)**
    
    *   **核心职责**：将引擎状态和实体行为映射为 JSON Schema/AST，供 DeepSeek/Gemini 等 AI Agent 读取并生成新的切片代码 。
        

## 二、 战术设计 (Tactical Design) 模型构建

在战术层面上，我们将 GMS 的核心概念拆解为标准的 DDD 构建块 ：

1.  **值对象 (Value Objects)**：不可变、无身份标识的状态载体（如位置坐标、旋转角度、图层深度） 。采用 C# `.NET 10` 的 `readonly record struct` 实现零内存分配（Zero Allocation）。
    
2.  **实体 (Entities)**：具有唯一标识 (`InstanceId`)，状态随游戏 Step 循环不断变化的实例对象 。
    
3.  **聚合根 (Aggregate Root)**：场景 (`SceneAggregate`)，负责维护实例集合的强一致性（如确保 InstanceId 唯一、控制空间索引更新同步） 。
    
4.  **领域事件 (Domain Events)**：解耦逻辑层与渲染管线的关键机制（例如：逻辑层触发 `StencilMaskPassRequestedEvent`，渲染层捕获后显式配置 `GL_STENCIL_TEST`） 。
    

## 三、 C# 代码示例与完整实现

### 1\. 值对象定义 (Value Objects)

```c#
namespace GameEngine.Domain.ValueObjects;

/// <summary>
/// 2D 向量位置（值对象：不可变，零 GC 分配）
/// </summary>
public readonly record struct Vector2D(float X, float Y)
{
    public static Vector2D Zero => new(0f, 0f);
    public static Vector2D operator +(Vector2D a, Vector2D b) => new(a.X + b.X, a.Y + b.Y);
}

/// <summary>
/// 2D 变换组件（包含位置、旋转、缩放）
/// </summary>
public readonly record struct Transform2D(Vector2D Position, float Rotation, Vector2D Scale)
{
    public static Transform2D Default => new(Vector2D.Zero, 0f, new Vector2D(1f, 1f));
}

/// <summary>
/// 实例唯一标识（强类型 ID）
/// </summary>
public readonly record struct InstanceId(Guid Value)
{
    public static InstanceId New() => new(Guid.NewGuid());
}

/// <summary>
/// 图层深度（对应 GMS 的 Depth，深度小的优先绘制）
/// </summary>
public readonly record struct LayerDepth(int Value) : IComparable<LayerDepth>
{
    public int CompareTo(LayerDepth other) => Value.CompareTo(other.Value);
}
```

### 2\. 领域事件定义 (Domain Events)

```c#
namespace GameEngine.Domain.Events;

using GameEngine.Domain.ValueObjects;

public interface IDomainEvent
{
    DateTime OccurredOn { get; }
}

/// <summary>
/// 实例生成事件
/// </summary>
public record InstanceSpawnedEvent(InstanceId Id, string ObjectType, Vector2D Position, LayerDepth Depth) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

/// <summary>
/// 实例移动事件（告知物理系统更新空间 Hash）
/// </summary>
public record InstanceMovedEvent(InstanceId Id, Vector2D OldPosition, Vector2D NewPosition) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

/// <summary>
/// 关键突破：请求模板遮罩渲染 Pass 事件（解耦 GameMaker 无法操作 GL_STENCIL_TEST 的痛点）
/// </summary>
public record StencilMaskPassRequestedEvent(
    InstanceId ProviderId,
    Action RenderMaskShape,
    Action RenderMaskedContent
) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
```

### 3\. 实体定义 (`GameInstance`)

`GameInstance` 对应 GMS 的 `Object Instance`，但通过事件驱动与逻辑高内聚 。

```c#
namespace GameEngine.Domain.Entities;

using GameEngine.Domain.ValueObjects;
using GameEngine.Domain.Events;

public class GameInstance
{
    public InstanceId Id { get; }
    public string ObjectTypeName { get; }
    public Transform2D Transform { get; private set; }
    public LayerDepth Depth { get; private set; }
    public bool IsActive { get; private set; } = true;

    public GameInstance(InstanceId id, string objectTypeName, Transform2D transform, LayerDepth depth)
    {
        Id = id;
        ObjectTypeName = objectTypeName;
        Transform = transform;
        Depth = depth;
    }

    /// <summary>
    /// 战术行为：移动实例，触发移动领域事件
    /// </summary>
    public void MoveTo(Vector2D newPosition, Action<IDomainEvent> raiseEvent)
    {
        if (Transform.Position == newPosition) return;

        var oldPos = Transform.Position;
        Transform = Transform with { Position = newPosition };

        // 发送领域事件，物理系统捕获后自动更新 QuadTree
        raiseEvent(new InstanceMovedEvent(Id, oldPos, newPosition));
    }

    /// <summary>
    /// 战术行为：提交 Stencil 遮罩绘制指令（解决 GMS 底层黑盒痛点）
    /// </summary>
    public void RequestStencilMask(Action drawMaskShape, Action drawContent, Action<IDomainEvent> raiseEvent)
    {
        raiseEvent(new StencilMaskPassRequestedEvent(Id, drawMaskShape, drawContent));
    }

    public void Destroy()
    {
        IsActive = false;
    }
}
```

### 4\. 聚合根定义 (`SceneAggregate`)

`SceneAggregate` 对应 GMS 的 `Room`，控制实体的整体生命周期与一致性边界 。

```c#
namespace GameEngine.Domain.Aggregates;

using GameEngine.Domain.Entities;
using GameEngine.Domain.ValueObjects;
using GameEngine.Domain.Events;

public class SceneAggregate
{
    public Guid SceneId { get; }
    public string SceneName { get; private set; }

    private readonly Dictionary<InstanceId, GameInstance> _instances = new();
    private readonly List<IDomainEvent> _uncommittedEvents = new();

    public IReadOnlyCollection<IDomainEvent> UncommittedEvents => _uncommittedEvents.AsReadOnly();
    public IReadOnlyCollection<GameInstance> ActiveInstances => _instances.Values.Where(i => i.IsActive).ToList();

    public SceneAggregate(Guid sceneId, string sceneName)
    {
        SceneId = sceneId;
        SceneName = sceneName;
    }

    /// <summary>
    /// 生成新实例（维持聚合内部一致性）
    /// </summary>
    public GameInstance Spawn(string objectTypeName, Vector2D position, LayerDepth depth)
    {
        var id = InstanceId.New();
        var transform = Transform2D.Default with { Position = position };
        var instance = new GameInstance(id, objectTypeName, transform, depth);

        _instances.Add(id, instance);
        
        // 记录领域事件
        RaiseEvent(new InstanceSpawnedEvent(id, objectTypeName, position, depth));

        return instance;
    }

    /// <summary>
    /// 执行一帧的物理与逻辑 Step 循环
    /// </summary>
    public void PerformStep(Action<GameInstance> stepLogic)
    {
        foreach (var instance in _instances.Values.Where(i => i.IsActive))
        {
            stepLogic(instance);
        }
    }

    public void RaiseEvent(IDomainEvent domainEvent)
    {
        _uncommittedEvents.Add(domainEvent);
    }

    public void ClearEvents()
    {
        _uncommittedEvents.Clear();
    }
}
```

## 四、 在 Vertical Slice Architecture (VSA) 中串联使用

按照垂直切片架构 (VSA)，每一个游戏功能（例如：绘制带 Stencil 遮罩的聚光灯切片）都是独立处理的 ：

```c#
namespace GameEngine.Features.LightMaskSlice;

using GameEngine.Domain.Aggregates;
using GameEngine.Domain.Events;
using GameEngine.Domain.ValueObjects;

// 1. Command / Command Handler 切片
public record ApplySpotlightMaskCommand(SceneAggregate Scene, InstanceId SpotlightId);

public class ApplySpotlightMaskHandler
{
    public void Handle(ApplySpotlightMaskCommand command)
    {
        var instance = command.Scene.ActiveInstances.FirstOrDefault(i => i.Id == command.SpotlightId);
        if (instance == null) return;

        // 逻辑层注册 Stencil 遮罩的回调
        instance.RequestStencilMask(
            drawMaskShape: () => 
            {
                // 绘制圆形遮罩图形的逻辑
            },
            drawContent: () => 
            {
                // 在遮罩内绘制受光照影响的贴图
            },
            raiseEvent: command.Scene.RaiseEvent
        );
    }
}

// 2. 渲染上下文中的 Event Handler 处理切片（底层的渲染逻辑）
public class StencilRenderEventHandler
{
    public void Handle(StencilMaskPassRequestedEvent evt)
    {
        // 渲染层直接与 Silk.NET / OpenGL 打交道
        // 彻底解决 GameMaker 隐藏渲染管线、无法调用 GL_STENCIL_TEST 的问题！
        
        // 1. Enable GL_STENCIL_TEST
        // 2. Clear Stencil Buffer
        // 3. evt.RenderMaskShape()
        // 4. Set glStencilFunc(GL_EQUAL, 1, 0xFF)
        // 5. evt.RenderMaskedContent()
        // 6. Reset Render Pipeline State
    }
}
```

## 五、 DDD 方案给游戏引擎带来的核心收益

1.  **绝对解耦与高扩展性**：逻辑层不需要了解任何 OpenGL / DirectX API 的存在，但又能通过 `DomainEvents` 精确指挥底层进行像 `GL_STENCIL_TEST` 这样的高级图形操作 。
    
2.  **极佳的性能潜力**：使用 `.NET 10` 的 `readonly record struct` 建模值对象，彻底避免了 GMS 中对象频繁创建导致的垃圾回收 (GC) 停顿 。
    
3.  **AI Agent 友好**：聚合根和领域事件构成了清晰的 API Schema 。像 DeepSeek 或 Gemini 可以极其轻松地理解 `SceneAggregate.Spawn()` 或编写一个独立的 Handler 切片代码 。

## Prompt:
我想了解一下 目录结构, 垂直切面

## Response:
在基于 **DDD (领域驱动设计)** 与 **VSA (垂直切片架构, Vertical Slice Architecture)** 构建 2D 游戏引擎时，我们将传统的“水平分层”（按 Layer 划分：Domain, Infra, UI）转变为“按功能切片”（按 Feature 划分）。

这种设计的最大优势是：**功能高度内聚，变更局部化** 。尤其在配合 Gemini 或 DeepSeek 等 AI Agent 进行辅助开发时，Agent 只需要在特定的切片文件夹内生成或修改代码，无需跨越多个项目层级 。

## 一、 引擎整体目录结构 (Directory Structure)

基于 `.NET 10` 与 `Vue 3` 的现代工程目录设计如下 ：

```plaintext
MyGameEngine/
├── src/
│   ├── Engine.Core/                         # 引擎核心基础设施 (跨切片共享)
│   │   ├── Domain/                          # 核心领域基类与值对象
│   │   │   ├── ValueObjects/                # Vector2D, Transform2D, InstanceId 等 [cite: 23]
│   │   │   └── Events/                      # IDomainEvent 接口与总线定义 
│   │   └── Infrastructure/                  # 底层硬件与 API 封装
│   │       ├── Graphics/                    # Silk.NET OpenGL 上下文、Shader 编译 [cite: 10, 19]
│   │       ├── Windowing/                   # 跨平台窗口管理 
│   │       └── Physics/                     # QuadTree / SpatialHash 底层数据结构 [cite: 6, 15]
│   │
│   ├── Engine.Features/                     # ⚡ 引擎功能垂直切片区 (Engine Feature Slices)
│   │   ├── StencilMasking/                  # 切片 1：GL_STENCIL 遮罩渲染切片 [cite: 1, 13]
│   │   │   ├── StencilMaskPassRequestedEvent.cs
│   │   │   ├── ApplyStencilMaskCommand.cs
│   │   │   └── StencilMaskHandler.cs
│   │   ├── SpatialCollision/                # 切片 2：GMS 式 place_meeting 碰撞切片 [cite: 10, 15]
│   │   │   ├── CheckCollisionQuery.cs
│   │   │   └── CheckCollisionHandler.cs
│   │   ├── SpriteAnimation/                 # 切片 3：2D 精灵图集与帧动画切片 [cite: 9]
│   │   │   ├── SpriteRenderPass.cs
│   │   │   └── AnimationComponent.cs
│   │   └── AgentBridge/                     # 切片 4：AI Agent 交互与 C# Schema 导出切片 
│   │       ├── SchemaGenerator.cs
│   │       └── RoslynScriptCompiler.cs
│   │
│   ├── Engine.Editor.Web/                   # 🛠️ ASP.NET Core + Vue 3 编辑器服务层 [cite: 2, 18]
│   │   ├── Hubs/                            # SignalR 实时双向通信通道 [cite: 18]
│   │   └── Controllers/                     # Scene / Asset API 接口 [cite: 18]
│   │
│   └── MyGame.Runner/                       # 🎮 游戏主程序入口 (Native AOT 编译发布) [cite: 18]
│       ├── Features/                        # 游戏业务逻辑专属切片 (如 PlayerControl, EnemyAI)
│       ├── GameScenes/                      # 场景配置数据
│       └── Program.cs                       # 引擎启动与配置入口 [cite: 12, 18]
│
├── client-editor/                           # 🌐 Vue 3 + TypeScript 引擎前端编辑器 [cite: 2, 18]
│   ├── src/
│   │   ├── components/                      # Viewport, Inspector, SceneTree 组件 [cite: 18]
│   │   └── services/                        # SignalR Client Bridge [cite: 18]
│   └── package.json
│
└── MyGameEngine.sln                         # C# 解决方案文件
```

## 二、 垂直切面 (Vertical Slice) 深度剖析

传统的层级架构中，为了新增一个 `Stencil 遮罩功能`，你需要在 `Domain` 增加模型，在 `Graphics` 增加渲染器，在 `Application` 增加服务，文件分散在整个项目中。

而在 **垂直切片架构 (VSA)** 中，一个切片就是一个完整的闭环 ：

### 核心切片示例：`StencilMasking` 切片

这个切片彻底解决了 GameMaker 无法自由插入 OpenGL `GL_STENCIL_TEST` 的缺陷 ：

```plaintext
Engine.Features/
└── StencilMasking/
    ├── StencilStateValueObject.cs       # 1. 状态定义 (Mask Mode, Ref Value)
    ├── ApplyStencilMaskCommand.cs       # 2. 输入命令 (Command)
    ├── StencilMaskPassRequestedEvent.cs # 3. 领域事件 (Domain Event)
    └── StencilMaskHandler.cs            # 4. 垂直执行器 (逻辑 + OpenGL 底层绘制)
```

#### 切片内部完整代码实现 (C# .NET 10)

```c#
namespace GameEngine.Features.StencilMasking;

using Silk.NET.OpenGL;
using GameEngine.Domain.Events;

// 1. 切片包含的值对象：描述模板测试状态
public readonly record struct StencilState(uint RefValue, uint Mask)
{
    public static StencilState Default => new(1, 0xFF);
}

// 2. 切片发起的领域事件：通知渲染管道插入 Stencil Pass
public record StencilMaskPassRequestedEvent(
    Action DrawMaskShape, 
    Action DrawMaskedContent,
    StencilState State
) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

// 3. 切片垂直处理器：内聚处理 OpenGL 底层逻辑 (彻底打破 GMS 黑盒)
public class StencilMaskHandler
{
    private readonly GL _gl;

    public StencilMaskHandler(GL gl) => _gl = gl;

    /// <summary>
    /// 处理 Stencil Pass 的具体绘制流程
    /// </summary>
    public void Handle(StencilMaskPassRequestedEvent evt)
    {
        // 开启 Stencil 测试并清空缓冲
        _gl.Enable(EnableCap.StencilTest);
        _gl.Clear((uint)ClearBufferMask.StencilBufferBit);

        // 步骤 A: 写入遮罩形状到 Stencil Buffer（禁止颜色输出）
        _gl.ColorMask(false, false, false, false);
        _gl.StencilFunc(StencilFunction.Always, (int)evt.State.RefValue, evt.State.Mask);
        _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
        _gl.StencilMask(evt.State.Mask);

        evt.DrawMaskShape(); // 执行遮罩形状绘制回调

        // 步骤 B: 在遮罩内绘制目标内容（恢复颜色输出）
        _gl.ColorMask(true, true, true, true);
        _gl.StencilFunc(StencilFunction.Equal, (int)evt.State.RefValue, evt.State.Mask);
        _gl.StencilMask(0x00); // 保护 Stencil Buffer 不被写入

        evt.DrawMaskedContent(); // 执行内容绘制回调

        // 重置状态
        _gl.Disable(EnableCap.StencilTest);
    }
}
```

## 三、 VSA 与 DDD 对比及 AI Agent 协同优势

| 维度 | 传统分层架构 (Layered) | 垂直切片架构 (VSA) |
| --- | --- | --- |
| **代码组织方式** | 按技术职责划分（`Controllers`, `Services`, `Repositories`） | 按业务/引擎功能划分（`StencilMasking`, `SpatialCollision`） |
| **新增/修改功能** | 依赖高，需要同时在多个工程项目中切换修改 | **低耦合**，仅需修改/新增当前 Feature 文件夹内的代码 |
| **与 DDD 的配合** | 容易演变为“贫血模型”，逻辑分散在系统各处 | **限界上下文**落地更彻底，领域事件直接在切片内部消化 |
| **AI Agent 辅助体验** | Prompt 极难包含全项目上下文， Agent 容易产生误改 | **极其友好**！Agent 每次仅需读取/编写单一文件夹的切片 |

  ```plaintext
  Prompt: "帮我实现一个基于 QuadTree 的碰撞查询切片"
                     │
                     ▼
  AI Agent (Gemini / DeepSeek)
                     │
                     ▼ 生成独立文件夹
  Engine.Features/SpatialCollision/
  ├── CheckCollisionQuery.cs
  └── CheckCollisionHandler.cs
```

通过这种架构，你既享有 **DDD 战略设计带来的边界清晰性** ，又获得了 **垂直切片架构带来的极高迭代速度** ，同时彻底解耦了 GameMaker 的渲染限制 。

你希望首先深入实现哪一个具体的垂直切片功能（例如：`SpatialCollision` 碰撞查询切片，还是基于 Roslyn 的 `AI Agent Bridge` 热重载切片）？

## Prompt:
深入探索 Phase 0.1：搭建基于 Silk.NET 的 C# 引擎基础设施与 Window Loop

## Response:
Phase 0.1 的目标是**打牢基础设施的基石**。 GameMaker (GMS) 最让人头疼的黑盒模式（如隐藏渲染管线、无法自主申请 Stencil Buffer 格式）将在这一阶段被彻底打破 。

使用 **.NET 10 + Silk.NET**，我们能够在原生窗口创建阶段，直接向 GPU 驱动显式申请 **8-bit Stencil Buffer**，并搭出一个兼具 GMS 易用性与现代 C# 高性能的 **Engine Window Loop**。

## 1\. 工程依赖与基础设施目录结构

在 `Engine.Core` 项目中引入 Silk.NET 库（Silk.NET 是现代 C# 跨平台绑定库，底层使用 `NativeMemory` 零 GC 交互）：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Silk.NET.Windowing" Version="2.22.0" />
    <PackageReference Include="Silk.NET.OpenGL" Version="2.22.0" />
    <PackageReference Include="Silk.NET.Input" Version="2.22.0" />
  </ItemGroup>
</Project>
```

基础设施部分的精简目录规划如下：

```plaintext
Engine.Core/
└── Infrastructure/
    ├── Windowing/
    │   ├── EngineWindowOptions.cs    # 窗口与 Buffer 属性配置
    │   └── EngineWindow.cs           # 窗口生命周期与 Loop 主控
    └── Graphics/
        ├── GraphicsDevice.cs         # OpenGL 上下文封装与 Stencil 状态初始化
        └── GlExtensions.cs           # OpenGL 辅助函数
```

## 2\. Phase 0.1 核心代码实现

### Step 1: 窗口与 GPU Buffer 配置 (`EngineWindowOptions.cs`)

要实现 `GL_STENCIL_TEST`，最关键的第一步就是在操作系统创建 Framebuffer 时，**明确请求 Stencil 位深**（GMS 很多导出平台默认设为 0-bit 导致 Mask 失效）。

```c#
namespace GameEngine.Core.Infrastructure.Windowing;

using Silk.NET.Maths;
using Silk.NET.Windowing;

public record EngineWindowOptions(
    string Title = "Custom C# 2D Engine",
    Vector2D<int> Size = default,
    bool VSync = true,
    int StencilBits = 8,  // 显式申请 8-bit Stencil 缓冲 (0-255 值域)
    int DepthBits = 24    // 24-bit 深度缓冲
)
{
    public static EngineWindowOptions Default => new(
        Size: new Vector2D<int>(1280, 720)
    );

    public WindowOptions ToSilkWindowOptions()
    {
        var opts = WindowOptions.Default;
        opts.Title = Title;
        opts.Size = Size;
        opts.VSync = VSync;
        
        // 配置 OpenGL 3.3 Core Profile
        opts.API = new GraphicsAPI(
            ContextAPI.OpenGL, 
            ContextProfile.Core, 
            ContextFlags.ForwardCompatible, 
            new APIVersion(3, 3)
        );

        // 显式向驱动申请 Depth 与 Stencil Buffer 位深
        opts.PreferredDepthBufferBits = DepthBits;
        opts.PreferredStencilBufferBits = StencilBits;

        return opts;
    }
}
```

### Step 2: Graphics 渲染设备封装 (`GraphicsDevice.cs`)

初始化 OpenGL 接口上下文，暴露底层的 `GL` 句柄，并提供默认的状态清屏能力。

```c#
namespace GameEngine.Core.Infrastructure.Graphics;

using Silk.NET.OpenGL;
using Silk.NET.Windowing;

public sealed class GraphicsDevice : IDisposable
{
    public GL Gl { get; }
    public int ViewportWidth { get; private set; }
    public int ViewportHeight { get; private set; }

    public GraphicsDevice(IWindow window)
    {
        // 绑定 OpenGL 函数指针
        Gl = GL.GetApi(window);
        
        ViewportWidth = window.Size.X;
        ViewportHeight = window.Size.Y;

        InitializeDefaultStates();
    }

    private void InitializeDefaultStates()
    {
        // 设置默认清屏颜色 (暗灰色背景)
        Gl.ClearColor(0.1f, 0.12f, 0.15f, 1.0f);
        
        // 设置默认 Stencil Buffer 清空基准值
        Gl.ClearStencil(0);

        // 开启 2D 精灵渲染必须的 Alpha 混合
        Gl.Enable(EnableCap.Blend);
        Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }

    public void OnResize(int width, int height)
    {
        ViewportWidth = width;
        ViewportHeight = height;
        Gl.Viewport(0, 0, (uint)width, (uint)height);
    }

    /// <summary>
    /// 每帧开始前，同时清空 Color Buffer 和 Stencil Buffer
    /// </summary>
    public void ClearBuffers()
    {
        Gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit));
    }

    public void Dispose()
    {
        Gl.Dispose();
    }
}
```

### Step 3: 主循环与生命周期 Hook (`EngineWindow.cs`)

将 Silk.NET 的 `OnUpdate` 和 `OnRender` 事件拆解映射为类似 GameMaker 的生命周期管道：`PreStep` $\\rightarrow$ `Step` $\\rightarrow$ `PostStep` $\\rightarrow$ `Draw` $\\rightarrow$ `DrawGUI` 。

```c#
namespace GameEngine.Core.Infrastructure.Windowing;

using Silk.NET.Windowing;
using GameEngine.Core.Infrastructure.Graphics;

public class EngineWindow
{
    private readonly IWindow _nativeWindow;
    public GraphicsDevice Graphics { get; private set; } = null!;

    // 暴露类似 GameMaker 生命周期管道的回调委托
    public event Action<double>? OnPreStep;
    public event Action<double>? OnStep;      // 对应 GMS 的 Step Event
    public event Action<double>? OnPostStep;
    
    public event Action? OnDrawBegin;
    public event Action? OnDraw;              // 对应 GMS 的 Draw Event
    public event Action? OnDrawGUI;           // 对应 GMS 的 Draw GUI Event

    public EngineWindow(EngineWindowOptions options)
    {
        _nativeWindow = Window.Create(options.ToSilkWindowOptions());

        _nativeWindow.Load += HandleLoad;
        _nativeWindow.Update += HandleUpdate;
        _nativeWindow.Render += HandleRender;
        _nativeWindow.Resize += HandleResize;
        _nativeWindow.Closing += HandleClosing;
    }

    public void Run()
    {
        _nativeWindow.Run();
    }

    private void HandleLoad()
    {
        // 1. 初始化 Graphics Device
        Graphics = new GraphicsDevice(_nativeWindow);
        Console.WriteLine($"[Engine EngineWindow] OpenGL Initialized. Version: {Graphics.Gl.GetStringS(StringName.Version)}");
    }

    private void HandleUpdate(double deltaTime)
    {
        // Step 阶段：只跑游戏逻辑、物理移动、碰撞检测，绝对不包含任何 Render 操作
        OnPreStep?.Invoke(deltaTime);
        OnStep?.Invoke(deltaTime);
        OnPostStep?.Invoke(deltaTime);
    }

    private void HandleRender(double deltaTime)
    {
        // 1. 重置 Framebuffer (清空 Color 和 Stencil Buffer)
        Graphics.ClearBuffers();

        // 2. 游戏场景世界渲染 (受 Camera & Layer 影响)
        OnDrawBegin?.Invoke();
        OnDraw?.Invoke();

        // 3. UI 界面渲染 (不受 Camera 影响的屏幕坐标系)
        OnDrawGUI?.Invoke();
    }

    private void HandleResize(Silk.NET.Maths.Vector2D<int> size)
    {
        Graphics?.OnResize(size.X, size.Y);
    }

    private void HandleClosing()
    {
        Graphics?.Dispose();
    }
}
```

## 3\. Phase 0.1 验证运行 (Hello Window & Stencil Test 验证)

下面编写 `MyGame.Runner/Program.cs` 进行 Phase 0.1 的底层验证。检查 OpenGL 是否成功分配了 8-bit Stencil 缓冲，并跑通基础设施 Loop：

```c#
using GameEngine.Core.Infrastructure.Windowing;
using Silk.NET.OpenGL;

var options = EngineWindowOptions.Default with { Title = "C# 2D Engine - Phase 0.1 Proof of Concept" };
var engineWindow = new EngineWindow(options);

// 注册简单的测试逻辑
engineWindow.OnStep += (delta) =>
{
    // 逻辑帧：更新计数器或位置
};

engineWindow.OnDraw += () =>
{
    var gl = engineWindow.Graphics.Gl;

    // 验证 Stencil Buffer 是否真正配置成功 (如果成功，Stencil Bits 应为 8)
    gl.GetInteger(GetPName.StencilBits, out int stencilBits);
    
    // 简单打一次 log 验证
    // Console.WriteLine($"Stencil Buffer Bit Depth: {stencilBits}");
};

// 启动引擎循环
engineWindow.Run();
```

## 四、 架构收益与下一步衔接

1.  **绝对控制力**：不同于 GameMaker 把 OpenGL 隐藏在 C++ Runtime 背后，我们现在拥有原生的 `GL` 对象和标准的 Step/Draw 循环钩子 。
    
2.  **零 GC 损耗**：底层基于 Silk.NET 绑定与 `.NET 10` 原生指针结构，每一帧的 Loop 调度没有任何隐式堆内存分配 。
    
3.  **解耦为 VSA 打基础**：`OnDraw` 回调为后续切片（如 `StencilMasking` 切片、`SpriteBatch` 切片）提供了极简的挂载入口 。
    

下一步，我们要在这个基础设施上接入 **Phase 0.2**：编写 `SpriteBatch` 处理顶点与贴图，并落地第一个完整的 `StencilMaskSlice` 遮罩切片 。你希望接下来看 `SpriteBatch` 的高性能顶点 Batch 缓冲区实现，还是直接看 Stencil 遮罩的具体 Shader 与 RenderPass 挂接？

## Prompt:
继续  Phase 0.2, 看 SpriteBatch 的高性能顶点 Batch 缓冲区实现

## Response:
进入 **Phase 0.2** 的第一核心模块：**高性能 2D `SpriteBatch` 顶点 Batch 缓冲区** 。

在 GameMaker (GMS) 中，`draw_sprite` 等函数在底层会把多次绘图指令合并为极少量的 DrawCall。我们采用 **.NET 10 (`Span<T>` / `MemoryMarshal`) + Silk.NET**，构建一个**零 GC 内存分配 (Zero-Allocation)、动态 VBO 批量提交、具备状态自动 Flush** 的 `SpriteBatch` 渲染器。

## 1\. 批处理 (Sprite Batching) 核心设计原理

2D 精灵渲染的核心是矩形（Quad），每个矩形由 **4 个顶点** 和 **6 个索引（两个三角形）** 组成：

  ```plaintext
  (0,0) Vertex 0 ────────────── Vertex 1 (1,0)
          │    \                 │
          │      \   Triangle 1  │
          │        \             │
          │ Triangle 2 \         │
          │              \       │
  (0,1) Vertex 3 ────────────── Vertex 2 (1,1)
  
  索引排列模式 (固定不变): [0, 1, 2,  2, 3, 0]
```

### 高性能的关键策略：

1.  **静态 EBO (Index Buffer)**：四边形的顶点索引顺序（0-1-2, 2-3-0, 4-5-6, 6-7-4...）在生成时即固定。因此 **EBO 仅需在初始化时向 GPU 上传一次**，后续帧无需重复写入。
    
2.  **动态 VBO 内存映射**：在 CPU 端使用 `Vertex2D[]` 连续内存存储顶点，通过 `.AsSpan()` 和 `MemoryMarshal.Cast` 零拷贝批量更新 GPU（`glBufferSubData` 或 `glMapBufferRange`）。
    
3.  **状态打断与自动 Flush**：当检测到**纹理变更**、**BlendMode 变更** 或 **批次缓冲区满了** 时，自动触发 `Flush()` 执行一次 GPU DrawCall。
    

## 2\. Phase 0.2 完整代码实现

### Step 1: 顶点数据结构与 Layout (`Vertex2D.cs`)

为保证内存对齐与 GPU 高效传输，使用 `LayoutKind.Sequential` 声明：

```c#
namespace GameEngine.Core.Infrastructure.Graphics;

using System.Numerics;
using System.Runtime.InteropServices;

/// <summary>
/// 2D 渲染顶点（对齐 32 字节内存）
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Vertex2D
{
    public Vector2 Position; // 8 bytes (X, Y)
    public Vector2 TexCoord; // 8 bytes (U, V)
    public Vector4 Color;    // 16 bytes (R, G, B, A)

    public Vertex2D(Vector2 position, Vector2 texCoord, Vector4 color)
    {
        Position = position;
        TexCoord = texCoord;
        Color = color;
    }
}
```

### Step 2: 简单 2D 正交 Shader (`SpriteShader.cs`)

GLSL Shader 接收 2D 世界投影矩阵，将顶点坐标转为 NDC 坐标系并采样贴图：

```c#
namespace GameEngine.Core.Infrastructure.Graphics;

using Silk.NET.OpenGL;
using System.Numerics;

public class SpriteShader : IDisposable
{
    private readonly GL _gl;
    public uint Handle { get; }

    private const string VertexShaderSource = @"
        #version 330 core
        layout (location = 0) in vec2 aPos;
        layout (location = 1) in vec2 aTexCoord;
        layout (location = 2) in vec4 aColor;

        out vec2 Frag_TexCoord;
        out vec4 Frag_Color;

        uniform mat4 uProjection;

        void main() {
            gl_Position = uProjection * vec4(aPos, 0.0, 1.0);
            Frag_TexCoord = aTexCoord;
            Frag_Color = aColor;
        }";

    private const string FragmentShaderSource = @"
        #version 330 core
        in vec2 Frag_TexCoord;
        in vec4 Frag_Color;

        out vec4 FragColor;

        uniform sampler2D uTexture;

        void main() {
            FragColor = texture(uTexture, Frag_TexCoord) * Frag_Color;
        }";

    public SpriteShader(GL gl)
    {
        _gl = gl;
        
        uint vert = CompileShader(ShaderType.VertexShader, VertexShaderSource);
        uint frag = CompileShader(ShaderType.FragmentShader, FragmentShaderSource);

        Handle = _gl.CreateProgram();
        _gl.AttachShader(Handle, vert);
        _gl.AttachShader(Handle, frag);
        _gl.LinkProgram(Handle);

        _gl.DeleteShader(vert);
        _gl.DeleteShader(frag);
    }

    public void Use() => _gl.UseProgram(Handle);

    public void SetProjection(Matrix4x4 matrix)
    {
        Use();
        int loc = _gl.GetUniformLocation(Handle, "uProjection");
        unsafe
        {
            _gl.UniformMatrix4(loc, 1, false, (float*)&matrix);
        }
    }

    private uint CompileShader(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        return shader;
    }

    public void Dispose() => _gl.DeleteProgram(Handle);
}
```

### Step 3: 高性能 `SpriteBatch.cs` 实现

支持最高 **2048 个 Quad (8192 个顶点)** 一次性 DrawCall，自动进行纹理切换处理：

```c#
namespace GameEngine.Core.Infrastructure.Graphics;

using Silk.NET.OpenGL;
using System.Numerics;
using System.Runtime.InteropServices;

public unsafe class SpriteBatch : IDisposable
{
    private const int MaxQuads = 2048;                  // 单次 Batch 最大 Quad 数量
    private const int MaxVertices = MaxQuads * 4;       // 8192 Vertices
    private const int MaxIndices = MaxQuads * 6;        // 12288 Indices

    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ebo;

    private readonly Vertex2D[] _vertexBuffer = new Vertex2D[MaxVertices];
    private int _quadCount = 0;
    
    private uint _currentTextureHandle = 0;
    private bool _isBeginning = false;

    public SpriteBatch(GL gl)
    {
        _gl = gl;

        // 1. 创建并绑定 VAO
        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);

        // 2. 创建动态 VBO (分配内存空间，不传初始数据)
        _vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(MaxVertices * sizeof(Vertex2D)), null, BufferUsageARB.DynamicDraw);

        // 3. 创建静态 EBO 并填充全量 Quad 索引数据
        _ebo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        
        ushort[] indices = new ushort[MaxIndices];
        ushort offset = 0;
        for (int i = 0; i < MaxIndices; i += 6)
        {
            indices[i + 0] = (ushort)(offset + 0);
            indices[i + 1] = (ushort)(offset + 1);
            indices[i + 2] = (ushort)(offset + 2);
            indices[i + 3] = (ushort)(offset + 2);
            indices[i + 4] = (ushort)(offset + 3);
            indices[i + 5] = (ushort)(offset + 0);
            offset += 4;
        }

        fixed (ushort* iPtr = indices)
        {
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(MaxIndices * sizeof(ushort)), iPtr, BufferUsageARB.StaticDraw);
        }

        // 4. 设置 Vertex Attribute Pointers
        // Location 0: Position (Vector2)
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, (uint)sizeof(Vertex2D), (void*)0);

        // Location 1: TexCoord (Vector2)
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, (uint)sizeof(Vertex2D), (void*)sizeof(Vector2));

        // Location 2: Color (Vector4)
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, (uint)sizeof(Vertex2D), (void*)(sizeof(Vector2) * 2));

        _gl.BindVertexArray(0);
    }

    /// <summary>
    /// 开始 Batch 绘制
    /// </summary>
    public void Begin()
    {
        if (_isBeginning) throw new InvalidOperationException("SpriteBatch.Begin() has already been called.");
        _isBeginning = true;
        _quadCount = 0;
        _currentTextureHandle = 0;
    }

    /// <summary>
    /// 提交精灵（类似 GameMaker draw_sprite）
    /// </summary>
    public void Draw(uint textureHandle, Vector2 position, Vector2 size, Vector4 color, Vector4 uvBounds = default)
    {
        if (!_isBeginning) throw new InvalidOperationException("Call SpriteBatch.Begin() first.");

        // 默认全图 UV (U0=0, V0=0, U1=1, V1=1)
        if (uvBounds == default) uvBounds = new Vector4(0, 0, 1, 1);

        // 检测 1：纹理改变，立刻 Flush 之前批次
        if (_currentTextureHandle != 0 && _currentTextureHandle != textureHandle)
        {
            Flush();
        }

        // 检测 2：Buffer 溢出，立刻 Flush
        if (_quadCount >= MaxQuads)
        {
            Flush();
        }

        _currentTextureHandle = textureHandle;

        // 计算 4 个角的位置
        int vIndex = _quadCount * 4;
        
        float x1 = position.X;
        float y1 = position.Y;
        float x2 = position.X + size.X;
        float y2 = position.Y + size.Y;

        // 填充 4 个顶点数据 (顺时针)
        _vertexBuffer[vIndex + 0] = new Vertex2D(new Vector2(x1, y1), new Vector2(uvBounds.X, uvBounds.Y), color); // Top-Left
        _vertexBuffer[vIndex + 1] = new Vertex2D(new Vector2(x2, y1), new Vector2(uvBounds.Z, uvBounds.Y), color); // Top-Right
        _vertexBuffer[vIndex + 2] = new Vertex2D(new Vector2(x2, y2), new Vector2(uvBounds.Z, uvBounds.W), color); // Bottom-Right
        _vertexBuffer[vIndex + 3] = new Vertex2D(new Vector2(x1, y2), new Vector2(uvBounds.X, uvBounds.W), color); // Bottom-Left

        _quadCount++;
    }

    /// <summary>
    /// 将缓冲区的顶点数据批量上传 GPU 并执行一次 DrawCall
    /// </summary>
    public void Flush()
    {
        if (_quadCount == 0 || _currentTextureHandle == 0) return;

        // 1. 绑定当前 Batch 的纹理到 Texture Unit 0
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _currentTextureHandle);

        // 2. 将 CPU 端的 Vertex2D 数组通过 Span 零拷贝更新到 GPU VBO
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        
        ReadOnlySpan<Vertex2D> vertexSpan = _vertexBuffer.AsSpan(0, _quadCount * 4);
        fixed (Vertex2D* vPtr = vertexSpan)
        {
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(_quadCount * 4 * sizeof(Vertex2D)), vPtr);
        }

        // 3. 执行单次索引绘制 (DrawElements)
        _gl.BindVertexArray(_vao);
        _gl.DrawElements(PrimitiveType.Triangles, (uint)(_quadCount * 6), DrawElementsType.UnsignedShort, null);
        _gl.BindVertexArray(0);

        // 4. 重置计数器，准备填充下一个 Batch
        _quadCount = 0;
    }

    /// <summary>
    /// 结束当前 Batch 绘制
    /// </summary>
    public void End()
    {
        if (!_isBeginning) return;
        Flush(); // 提交剩余未绘制的 Quads
        _isBeginning = false;
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
    }
}
```

## 3\. 在基础设施主循环中验证接入 (`Phase 0.2 Test`)

将 `SpriteShader` 与 `SpriteBatch` 挂载到 Phase 0.1 打造的 `EngineWindow` 渲染流程中：

```c#
// Program.cs
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Core.Infrastructure.Graphics;
using System.Numerics;

var window = new EngineWindow(EngineWindowOptions.Default);

SpriteShader shader = null!;
SpriteBatch batch = null!;

// 初始化阶段
window.OnDrawBegin += () =>
{
    if (shader == null)
    {
        var gl = window.Graphics.Gl;
        shader = new SpriteShader(gl);
        batch = new SpriteBatch(gl);

        // 设置 2D 正交投影矩阵 (0,0 在左上角，匹配 GameMaker 坐标系)
        Matrix4x4 projection = Matrix4x4.CreateOrthographicOffCenter(
            0, window.Graphics.ViewportWidth, 
            window.Graphics.ViewportHeight, 0, 
            -1.0f, 1.0f
        );
        shader.SetProjection(projection);
    }
};

// 渲染阶段 (10,000 个精灵仅需极少 DrawCalls)
window.OnDraw += () =>
{
    shader.Use();
    batch.Begin();

    // 模拟 GMS draw_sprite：在屏幕上批量画 500 个不同位置的精灵
    for (int i = 0; i < 500; i++)
    {
        Vector2 pos = new(i * 2, i * 1.5f);
        Vector2 size = new(32, 32);
        Vector4 color = new(1.0f, 0.5f, 0.2f, 1.0f); // 橙色透明度

        // 假定 1 为默认的白贴图 Handle
        batch.Draw(1, pos, size, color);
    }

    batch.End(); // 触发全量批次提交！
};

window.Run();
```

## 4\. 本阶段的技术突破

1.  **绝对掌控 GC**：渲染逻辑没有任何 `new` 引用对象产生，`Vertex2D` 数组完全复用，避免了 C# 在高帧率下的 GC 卡顿问题。
    
2.  **极佳的性能扩展**：通过静态索引 Buffer 与 `BufferSubData` 机制，可以在 60/144 FPS 下维持成千上万个 2D Instance 的实时渲染。
    

下一步，我们将在这个 `SpriteBatch` 的基础上，**接入 Phase 0.2 的后半部分：把 `GL_STENCIL_TEST` 封装为可插拔的 `StencilMasking` 垂直切片**，实现类似“聚光灯遮罩”或“UI 视口剪裁”功能。

你希望直接看 **`StencilMasking` 切片与 `SpriteBatch` 混合渲染** 的实战代码，还是想了解 **Texture Atlas (图集自动打包)** 的设计？

## Prompt:
继续Phase 0.2 的后半部分, 然后  StencilMasking 切片与 SpriteBatch 混合渲染 的实战代码

## Response:
进入 **Phase 0.2 的后半部分**。在上一小节中，我们搭建了零 GC、自动 Batch 的 `SpriteBatch` 。现在我们需要解决一个 **2D 渲染引擎中非常经典的难题**：**批处理渲染器 (SpriteBatch) 与 OpenGL 状态切换 (如 `GL_STENCIL_TEST`) 的协同与冲突** 。

在 GameMaker (GMS) 中，渲染管线是黑盒，你无法在 `draw_sprite` 之间精确控制 Stencil 测试 。而在我们自行打造的引擎中，将通过 **垂直切片 (VSA)** 彻底解决这一痛点 。

## 1\. 核心瓶颈：SpriteBatch 与 GPU 状态变化的冲突

在编写 Stencil 遮罩前，必须先理清 **状态打断 (State Flushing)** 的原理：

`SpriteBatch.Draw()` 并不是立即调用 GPU 进行绘制，而是先将顶点存入 CPU 内存缓冲区，等到 `End()` 或缓冲区满时才统一提交 `glDrawElements` 。

如果你在代码中直接写：

```c#
batch.Draw(bgTexture);          // 企图画背景
gl.Enable(EnableCap.StencilTest); // 开启模板测试
batch.Draw(maskTexture);        // 企图画遮罩
```

**结果会彻底乱套**！因为当 `batch.End()` 真正执行时，OpenGL 此时已经开启了 Stencil 测试，导致前面的 `bgTexture` 也被错误地施加了 Stencil 遮罩 。

**解决方案**：在每次改变 GPU 状态（如开启/关闭 Stencil、切换混合模式）之前，**必须显式调用 `SpriteBatch.Flush()` 强制清空当前批次** 。

## 2\. 战术设计：`StencilMasking` 垂直切片实现

我们将 Stencil 遮罩逻辑封装为一个独立的 **Render Pass 切片** ，负责掌控正确的 OpenGL 状态转换与 `SpriteBatch` 清空时机。

### 1) 模板遮罩配置值对象 (`StencilMaskState.cs`)

```c#
namespace GameEngine.Features.StencilMasking;

/// <summary>
/// 遮罩模式定义
/// </summary>
public enum StencilMaskMode
{
    /// <summary> 显示遮罩内部 (Inverted = false) </summary>
    ShowInside,
    /// <summary> 显示遮罩外部/反向遮罩 (Inverted = true) </summary>
    ShowOutside
}

public readonly record struct StencilMaskState(
    uint StencilRef = 1,
    uint MaskBits = 0xFF,
    StencilMaskMode Mode = StencilMaskMode.ShowInside
);
```

### 2) 遮罩渲染切片处理器 (`StencilMaskHandler.cs`)

```c#
namespace GameEngine.Features.StencilMasking;

using Silk.NET.OpenGL;
using GameEngine.Core.Infrastructure.Graphics;

public class StencilMaskHandler
{
    private readonly GL _gl;
    private readonly SpriteBatch _batch;

    public StencilMaskHandler(GL gl, SpriteBatch batch)
    {
        _gl = gl;
        _batch = batch;
    }

    /// <summary>
    /// 执行带 Stencil 遮罩的批次渲染 Pass
    /// </summary>
    /// <param name="drawMaskShape">绘制遮罩形状的操作 (如聚光灯圆形贴图)</param>
    /// <param name="drawContent">绘制被遮罩覆盖内容的操作 (如背景、角色)</param>
    /// <param name="state">模板测试参数</param>
    public void ExecuteMaskedPass(Action drawMaskShape, Action drawContent, StencilMaskState state = default)
    {
        // 关键步骤 0：清空之前存留在 SpriteBatch 里的任何常规精灵！
        _batch.Flush();

        // 步骤 1：开启 Stencil 测试，清空 Stencil 缓冲区
        _gl.Enable(EnableCap.StencilTest);
        _gl.Clear((uint)ClearBufferMask.StencilBufferBit);

        // -------------------------------------------------------------
        // 步骤 2：绘制遮罩形状 (只写 Stencil Buffer，关闭 Color Buffer 写入)
        // -------------------------------------------------------------
        _gl.ColorMask(false, false, false, false); // 禁止颜色输出到屏幕
        _gl.DepthMask(false);                       // 禁止写入深度缓冲

        // 配置：总是通过测试，将符合条件的像素在 Stencil Buffer 中写入 StencilRef (如 1)
        _gl.StencilFunc(StencilFunction.Always, (int)state.StencilRef, state.MaskBits);
        _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
        _gl.StencilMask(state.MaskBits); // 开启 Stencil 写入

        // 执行遮罩绘制并强制 Flush 进 GPU
        _batch.Begin();
        drawMaskShape();
        _batch.End(); // 此处 Flush 会把遮罩形状写入 Stencil Buffer

        // -------------------------------------------------------------
        // 步骤 3：绘制被遮罩的内容 (恢复 Color Buffer 写入，开启 Stencil 测试)
        // -------------------------------------------------------------
        _gl.ColorMask(true, true, true, true);   // 恢复颜色输出
        _gl.DepthMask(true);

        // 根据模式选择：如果是 ShowInside，只有 Stencil 值 Equal 1 的地方才绘制
        var func = state.Mode == StencilMaskMode.ShowInside 
            ? StencilFunction.Equal 
            : StencilFunction.NotEqual;

        _gl.StencilFunc(func, (int)state.StencilRef, state.MaskBits);
        _gl.StencilMask(0x00); // 禁用 Stencil 写入，保护遮罩形状不被改变

        // 执行目标内容绘制并强制 Flush 进 GPU
        _batch.Begin();
        drawContent();
        _batch.End(); // 此处 Flush 的精灵会受 Stencil 测试裁切

        // -------------------------------------------------------------
        // 步骤 4：恢复默认渲染状态
        // -------------------------------------------------------------
        _gl.Disable(EnableCap.StencilTest);
    }
}
```

## 3\. 实战代码：混合渲染聚光灯遮罩 (Spotlight Masking)

下面在游戏入口 `Program.cs` 中，演示如何利用上面编写的 `StencilMaskHandler` 联合 `SpriteBatch`，实现类似 **“黑夜迷雾中只有手电筒/聚光灯范围可见”** 的游戏效果 。

```c#
using System.Numerics;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.StencilMasking;

// 1. 初始化 Phase 0.1 搭建的基础设施窗口
var window = new EngineWindow(EngineWindowOptions.Default with { Title = "Phase 0.2 - Stencil Masking & SpriteBatch Batching Demo" });

SpriteShader shader = null!;
SpriteBatch batch = null!;
StencilMaskHandler stencilHandler = null!;

// 假设的 Texture Handles (实际开发中由 TextureLoader 载入)
uint bgTextureHandle = 1;         // 地形背景贴图
uint spotlightMaskHandle = 2;    // 圆形遮罩贴图 (Alpha 通道决定遮罩形状)
uint playerTextureHandle = 3;    // 玩家/怪物贴图

Vector2 mousePosition = Vector2.Zero;

// 模拟 Mouse Move 监听，让聚光灯跟随鼠标
window.OnStep += (delta) =>
{
    // 在这里更新鼠标世界坐标，例如：mousePosition = Input.GetMousePos();
};

window.OnDrawBegin += () =>
{
    if (shader == null)
    {
        var gl = window.Graphics.Gl;
        shader = new SpriteShader(gl);
        batch = new SpriteBatch(gl);
        stencilHandler = new StencilMaskHandler(gl, batch);

        // 设置正交投影矩阵
        Matrix4x4 projection = Matrix4x4.CreateOrthographicOffCenter(
            0, window.Graphics.ViewportWidth, 
            window.Graphics.ViewportHeight, 0, 
            -1.0f, 1.0f
        );
        shader.SetProjection(projection);
    }
};

window.OnDraw += () =>
{
    shader.Use();

    // -------------------------------------------------------------
    // PASS 1: 正常绘制不受遮罩影响的暗色底层 (如全屏暗沉的背景)
    // -------------------------------------------------------------
    batch.Begin();
    // 绘制暗色背景 (全屏，RGB 0.2 表现为黑夜)
    batch.Draw(bgTextureHandle, Vector2.Zero, new Vector2(1280, 720), new Vector4(0.2f, 0.2f, 0.2f, 1.0f));
    batch.End();

    // -------------------------------------------------------------
    // PASS 2: 结合 StencilMaskHandler 执行聚光灯遮罩混合 Pass
    // -------------------------------------------------------------
    stencilHandler.ExecuteMaskedPass(
        // A. 绘制遮罩形状 (一个跟随鼠标移动的 256x256 圆形)
        drawMaskShape: () =>
        {
            Vector2 maskPos = mousePosition - new Vector2(128, 128);
            batch.Draw(spotlightMaskHandle, maskPos, new Vector2(256, 256), Vector4.One);
        },
        
        // B. 绘制被遮罩覆盖的高亮内容 (只有在聚光灯圆形内部才会显现！)
        drawContent: () =>
        {
            // 在聚光灯内部绘制高亮的正常颜色背景 (RGB 1.0)
            batch.Draw(bgTextureHandle, Vector2.Zero, new Vector2(1280, 720), Vector4.One);

            // 在聚光灯内部绘制隐藏的宝箱或敌人
            batch.Draw(playerTextureHandle, new Vector2(400, 300), new Vector2(64, 64), Vector4.One);
        },
        
        // C. 设置为显示遮罩内部 (ShowInside)
        state: new StencilMaskState(StencilRef: 1, Mode: StencilMaskMode.ShowInside)
    );

    // -------------------------------------------------------------
    // PASS 3: 绘制不受遮罩影响的上层 UI (如血条、暂停菜单)
    // -------------------------------------------------------------
    batch.Begin();
    // batch.Draw(uiTextureHandle, ...);
    batch.End();
};

window.Run();
```

## 4\. 本阶段完成后的能力对比

| 功能特性 | GameMaker Studio (GMS) | 我们的 C# 2D Engine (Phase 0.2) |
| --- | --- | --- |
| **Stencil 遮罩** | ❌ 原生不支持，必须写复杂且低效的 Surface+Blendmode 绕路 | **原生支持**，通过 `StencilMaskHandler` 切片一行代码调用 |
| **DrawCall 性能** | ⚠️ 黑盒合并，容易因状态打断产生大量微小 DrawCall | **透明且受控**，采用静态 EBO + 动态 VBO，自动状态 Flush |
| **内存与 GC 占用** | ⚠️ GML 引用类型较多，存在 GC 停顿风险 | **零 GC (Zero-Alloc)**，`Span<T>` 内存连续更新 |
| **架构扩展性** | ❌ 单体结构，难以被 AI Agent 局部修改 | **垂直切片 (VSA)**，每一个 Render Pass 均可作为独立模块让 AI Agent 生成 |

## 5\. Phase 0 基础设施阶段总结与后续规划

至此，**Phase 0 -> 1 (最小可行引擎)** 的图形与基础设施核心已经全部实现 ：

1.  **Phase 0.1**：完成了 `Silk.NET` 跨平台窗口初始化、8-bit Stencil Buffer 配置与 GMS 式生命周期主循环 (`EngineWindow`) 。
    
2.  **Phase 0.2**：完成了高性能 `SpriteBatch` 批处理渲染器与零 GC 动态 VBO，并彻底打通了 `StencilMasking` 遮罩切片与批处理渲染器的混合提交 。
    

### 下一步规划建议：

你可以选择进入 **Phase 0.3** 或直接跨入 **Phase 1.0 (功能对齐与GM特性超越)** ：

## Prompt:
请展开 Phase 0.3：如何实现高效的 Texture Atlas 图集管理与自动 UV 映射切片，以降低 SpriteBatch 的纹理切换打断？

## Response:
在 2D 游戏引擎中，**`SpriteBatch` 的性能命门在于 DrawCall 数量** 。而在 Phase 0.2 中我们提到：只要渲染过程中**切换了绑定纹理 (Texture Swapping)**，`SpriteBatch` 就必须被迫调用 `Flush()` 将当前批次提交给 GPU 。

如果游戏中有 1000 个精灵，分别来自 100 张独立的 `.png` 图片，引擎就会产生 **100 次以上的 DrawCall**，导致性能骤降。

**Phase 0.3 的核心任务**：实现 **Texture Atlas (图集/纹理大图) 管理器** 与 **自动 UV 映射切片**。将成百上千张散图合成为一张 GPU 大图（如 $2048 \\times 2048$ 或 $4096 \\times 4096$），使所有精灵共享同一个 GPU 纹理 Handle，从而实现 **千张精灵 1 次 DrawCall 的极致渲染** 。

## 一、 图集映射与 Bin Packing 核心算法原理

Texture Atlas 的运作原理是通过计算归一化 **UV 坐标**，在同一张大图上裁切出各个子精灵（Sub-Textures）：

### 1\. UV 坐标映射计算公式

GPU 纹理坐标系采用归一化 $(0.0 \\sim 1.0)$ 标度，左上角为 $(0, 0)$，右下角为 $(1, 1)$：

$$\\begin{aligned} u\_0 = \\frac{\\text{PosX}}{\\text{AtlasWidth}}, &\\quad v\_0 = \\frac{\\text{PosY}}{\\text{AtlasHeight}} \\\\ u\_1 = \\frac{\\text{PosX} + \\text{Width}}{\\text{AtlasWidth}}, &\\quad v\_1 = \\frac{\\text{PosY} + \\text{Height}}{\\text{AtlasHeight}} \\end{aligned}$$

在之前写的 `SpriteBatch.Draw()` 中，顶点数据包含 `Vector4 uvBounds` 。通过将计算好的 $(u\_0, v\_0, u\_1, v\_1)$ 传给 `SpriteBatch`，Shader 便能在采样大图时精准截取对应子图，**完全不需要在 GPU 侧切换 Texture Handle** ！

### 2\. 装箱算法 (Shelf / MaxRects Bin Packing)

在构建 Atlas 时，算法需要在 2D 矩形画布中高效摆放不规则尺寸的子图。我们采用工业级且高效的 **Shelf Packing (货架装箱)** 算法，支持**开发期工具预打包**或**运行时动态装箱**。

## 二、 战术设计：`TextureAtlas` 垂直切片架构

在 VSA (垂直切片) 架构中，图集管理切片划分为以下结构：

```plaintext
Engine.Features/
└── TextureAtlas/
    ├── SpriteRegion.cs          # 1. 值对象：描述子精灵的 UV 与尺寸元数据
    ├── ShelfBinPacker.cs         # 2. 算法切片：2D 矩形装箱计算器
    ├── TextureAtlasAggregate.cs # 3. 聚合根/实体：管理 GPU 纹理与精灵查找表
    └── AtlasBuilderService.cs   # 4. 垂直切片服务：读取散图文件夹并生成 Atlas
```

## 三、 C# .NET 10 代码实现

### Step 1: 精灵区域值对象 (`SpriteRegion.cs`)

使用 `.NET 10` 的 `readonly record struct` 实现零堆内存开销（Zero GC Allocation）：

```c#
namespace GameEngine.Features.TextureAtlas;

using System.Numerics;

/// <summary>
/// 图集中某个子精灵的元数据 (值对象)
/// </summary>
/// <param name="Name">子图标识名称 (如 "player_idle_0")</param>
/// <param name="UvBounds">归一化 UV 坐标 (u0, v0, u1, v1)</param>
/// <param name="SourceSize">原始图像尺寸 (Width, Height)</param>
public readonly record struct SpriteRegion(
    string Name,
    Vector4 UvBounds,
    Vector2 SourceSize
)
{
    public float Width => SourceSize.X;
    public float Height => SourceSize.Y;
}
```

### Step 2: 货架装箱算法实现 (`ShelfBinPacker.cs`)

负责在 $N \\times N$ 的空白 Pixel 缓冲区中规划散图的位置，生成对应的 `UvBounds`：

```c#
namespace GameEngine.Features.TextureAtlas;

using System.Numerics;

public record PackNode(string Name, int X, int Y, int Width, int Height);

public class ShelfBinPacker
{
    private readonly int _atlasWidth;
    private readonly int _atlasHeight;
    private readonly int _padding;

    private int _currentX = 0;
    private int _currentY = 0;
    private int _shelfHeight = 0;

    public ShelfBinPacker(int atlasWidth = 2048, int atlasHeight = 2048, int padding = 2)
    {
        _atlasWidth = atlasWidth;
        _atlasHeight = atlasHeight;
        _padding = padding;
    }

    /// <summary>
    /// 将一组子图打包并计算 UV 坐标
    /// </summary>
    public Dictionary<string, SpriteRegion> Pack(IEnumerable<(string Name, int Width, int Height)> imageSizes)
    {
        var result = new Dictionary<string, SpriteRegion>();

        // 按高度降序排序（Shelf 算法最佳实践，减少空间浪费）
        var sortedImages = imageSizes.OrderByDescending(img => img.Height).ToList();

        foreach (var img in sortedImages)
        {
            int w = img.Width + _padding * 2;
            int h = img.Height + _padding * 2;

            // 如果当前 Shelf 放不下，切换到下一行 Shelf
            if (_currentX + w > _atlasWidth)
            {
                _currentY += _shelfHeight;
                _currentX = 0;
                _shelfHeight = 0;
            }

            // 图集空间溢出检查
            if (_currentY + h > _atlasHeight)
            {
                throw new InvalidOperationException($"Texture Atlas out of bounds! Exceeded {_atlasWidth}x{_atlasHeight}.");
            }

            // 计算实际像素位置（加上 Padding 防止采样渗色/Bleeding）
            int pixelX = _currentX + _padding;
            int pixelY = _currentY + _padding;

            // 计算 0.0 ~ 1.0 的 UV 坐标
            float u0 = (float)pixelX / _atlasWidth;
            float v0 = (float)pixelY / _atlasHeight;
            float u1 = (float)(pixelX + img.Width) / _atlasWidth;
            float v1 = (float)(pixelY + img.Height) / _atlasHeight;

            var region = new SpriteRegion(
                img.Name,
                new Vector4(u0, v0, u1, v1),
                new Vector2(img.Width, img.Height)
            );

            result.Add(img.Name, region);

            // 更新 Shelf 位置
            _currentX += w;
            if (h > _shelfHeight) _shelfHeight = h;
        }

        return result;
    }
}
```

### Step 3: 图集聚合与 GPU 纹理申请 (`TextureAtlasAggregate.cs`)

管理申请好的 GPU OpenGL Texture Handle，并提供对子精灵的闪电查找：

```c#
namespace GameEngine.Features.TextureAtlas;

using Silk.NET.OpenGL;
using System.Numerics;

public class TextureAtlasAggregate : IDisposable
{
    private readonly GL _gl;
    public uint TextureHandle { get; }
    public int Width { get; }
    public int Height { get; }

    private readonly Dictionary<string, SpriteRegion> _regions;

    public TextureAtlasAggregate(GL gl, int width, int height, Dictionary<string, SpriteRegion> regions, Span<byte> pixelData)
    {
        _gl = gl;
        Width = width;
        Height = height;
        _regions = regions;

        // 向 GPU 申请大图纹理内存
        TextureHandle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, TextureHandle);

        // 设置纹理过滤（2D 像素风选用 Nearest，现代插画风选用 Linear）
        _gl.TextureParameter(TextureHandle, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TextureParameter(TextureHandle, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.TextureParameter(TextureHandle, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TextureParameter(TextureHandle, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);

        unsafe
        {
            fixed (byte* ptr = pixelData)
            {
                _gl.TexImage2D(
                    TextureTarget.Texture2D, 0, 
                    (int)InternalFormat.Rgba, 
                    (uint)width, (uint)height, 0, 
                    PixelFormat.Rgba, PixelType.UnsignedByte, 
                    ptr
                );
            }
        }

        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    /// <summary>
    /// 根据名称高效获取子图 UV
    /// </summary>
    public bool TryGetRegion(string name, out SpriteRegion region)
    {
        return _regions.TryGetValue(name, out region);
    }

    public SpriteRegion GetRegion(string name)
    {
        if (_regions.TryGetValue(name, out var region)) return region;
        throw new KeyNotFoundException($"Sprite '{name}' not found in Texture Atlas.");
    }

    public void Dispose()
    {
        _gl.DeleteTexture(TextureHandle);
    }
}
```

## 四、 混合缝合实战：图集与 `SpriteBatch` 联调

下面展示如何在场景渲染中使用 `TextureAtlas`。全场 1000 个不同外观的精灵共享同一个 `TextureHandle`，**实现全流程零 Flush 提交** ：

```c#
// Program.cs
using System.Numerics;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.TextureAtlas;

var window = new EngineWindow(EngineWindowOptions.Default);

SpriteShader shader = null!;
SpriteBatch batch = null!;
TextureAtlasAggregate globalAtlas = null!;

window.OnDrawBegin += () =>
{
    if (shader == null)
    {
        var gl = window.Graphics.Gl;
        shader = new SpriteShader(gl);
        batch = new SpriteBatch(gl);

        // 1. 模拟打包散图，生成 2048x2048 的 Atlas (实际开发中可通过 StbImage 加载磁盘图片)
        var packer = new ShelfBinPacker(2048, 2048);
        var mockImages = new List<(string Name, int Width, int Height)>
        {
            ("player_idle", 64, 64),
            ("enemy_goblin", 48, 48),
            ("coin_gold", 16, 16),
            ("terrain_grass", 128, 128)
        };

        var regions = packer.Pack(mockImages);
        
        // 假定开辟 RGBA 像素内存并创建 Atlas
        byte[] emptyPixels = new byte[2048 * 2048 * 4];
        globalAtlas = new TextureAtlasAggregate(gl, 2048, 2048, regions, emptyPixels);

        // 设置投影矩阵
        Matrix4x4 projection = Matrix4x4.CreateOrthographicOffCenter(
            0, window.Graphics.ViewportWidth, 
            window.Graphics.ViewportHeight, 0, 
            -1.0f, 1.0f
        );
        shader.SetProjection(projection);
    }
};

window.OnDraw += () =>
{
    shader.Use();
    batch.Begin();

    // -------------------------------------------------------------
    // 渲染 1,000 个不同子图精灵（玩家、金币、怪物、草地）
    // 关键点：由于它们全部来自 globalAtlas.TextureHandle，
    // SpriteBatch 内部不会发生任何纹理打断 (Flush)！只有 1 次 GPU DrawCall！
    // -------------------------------------------------------------
    uint atlasHandle = globalAtlas.TextureHandle;

    // 1. 画草地背景
    var grassRegion = globalAtlas.GetRegion("terrain_grass");
    for (int x = 0; x < 1280; x += 128)
    {
        for (int y = 0; y < 720; y += 128)
        {
            batch.Draw(atlasHandle, new Vector2(x, y), grassRegion.SourceSize, Vector4.One, grassRegion.UvBounds);
        }
    }

    // 2. 画 500 个散射的金币
    var coinRegion = globalAtlas.GetRegion("coin_gold");
    for (int i = 0; i < 500; i++)
    {
        Vector2 pos = new(i * 2 + 10, (i * 37) % 700);
        batch.Draw(atlasHandle, pos, coinRegion.SourceSize, Vector4.One, coinRegion.UvBounds);
    }

    // 3. 画玩家
    var playerRegion = globalAtlas.GetRegion("player_idle");
    batch.Draw(atlasHandle, new Vector2(400, 300), playerRegion.SourceSize, Vector4.One, playerRegion.UvBounds);

    batch.End(); // 提交全场唯一的一次 DrawCall！
};

window.Run();
```

## 五、 Phase 0.3 架构收益与 AI Agent 协同优势

1.  **DrawCall 从 $N$ 降至 $1$**：通过 Atlas 图集聚合，游戏界面中即便有数万个像素精灵，只要使用的材质 Shader 相同，就可以**在一拍之内通过单次 DrawCall 完成渲染** 。
    
2.  **零 GC 状态传递**：在渲染循环中传给 `SpriteBatch.Draw()` 的是轻量 `readonly record struct` 中的 `UvBounds` (Vector4)，没有任何引用对象分配 。
    
3.  **AI Agent 生态友好**：AI Agent (如 Gemini / DeepSeek) 可以被训练直接输出包含图集索引元数据的 JSON（如与 MonoGame / TexturePacker 兼容的 JSON Schema），并自动由 C# `AtlasBuilderService` 转化为运行时使用的引擎切片代码 。
    

### 下一步规划 (Entering Phase 1.0)

基础设施 (Phase 0) 已经全线贯通（窗口与 Loop $\\rightarrow$ 高性能顶点 Batcher $\\rightarrow$ Stencil 遮罩 Pass $\\rightarrow$ Atlas 图集管理）。接下来我们可以步入 **Phase 1.0 (功能对齐与 GM 特性超越)**：

## Prompt:
继续 Phase 1.0 (功能对齐与 GM 特性超越)

## Response:
正式进入 **Phase 1.0：功能对齐与 GameMaker 特性超越 (Feature Parity & Beyond GMS)**。

在前期的 **Phase 0** 阶段，我们成功搭建了基于 Silk.NET 的基础设施 、零 GC 批处理渲染器 `SpriteBatch` 、`StencilMasking` 垂直切片 以及 `TextureAtlas` 自动图集装箱机制 。

在 **Phase 1.0** 中，我们将破除 GameMaker (GMS) 在**场景管理、图层排序、2D 碰撞查询以及相机变换**上的诸多底层限制（例如 GMS Room 强制耦合逻辑与渲染 、`place_meeting` 逐像素低效查询等 ），在 DDD 聚合根（`SceneAggregate`）与 VSA 切片的指引下，落地三大核心切片模块。

## 一、 Phase 1.1：`SceneAggregate` 聚合根、图层 (Layer) 与 Multi-Camera 矩阵切片

GameMaker 的 Room 机制最大的痛点在于**场景图 (Scene Graph)、图层排序与逻辑帧强制绑定在一起** 。我们将其重构为 **`SceneAggregate` 聚合根** ，将相机视角（Viewport/Camera Matrix）与图层渲染队列完全解耦。

### 1\. 2D 正交相机切片 (`Camera2D.cs`)

2D 相机本质上是一个计算 View-Projection 矩阵的数学变换器，支持平移、缩放（Zoom）、旋转与震屏（Camera Shake）：

```c#
namespace GameEngine.Features.Camera;

using System.Numerics;

public class Camera2D
{
    public Vector2 Position { get; set; } = Vector2.Zero;
    public float Zoom { get; set; } = 1.0f;
    public float Rotation { get; set; } = 0.0f; // 弧度
    public Vector2 ViewportSize { get; private set; }

    public Camera2D(Vector2 viewportSize)
    {
        ViewportSize = viewportSize;
    }

    public void ResizeViewport(float width, float height)
    {
        ViewportSize = new Vector2(width, height);
    }

    /// <summary>
    /// 获取 2D 正交 View-Projection 变换矩阵
    /// </summary>
    public Matrix4x4 GetViewProjectionMatrix()
    {
        // 1. 平移矩阵 (以相机中心为原点)
        var translation = Matrix4x4.CreateTranslation(-Position.X, -Position.Y, 0f);
        
        // 2. 旋转矩阵
        var rotation = Matrix4x4.CreateRotationZ(Rotation);
        
        // 3. 缩放矩阵
        var scale = Matrix4x4.CreateScale(Zoom, Zoom, 1.0f);

        // 4. View 变换矩阵
        var view = translation * rotation * scale;

        // 5. Orthographic 投影矩阵 (0,0 位于屏幕左上角)
        var projection = Matrix4x4.CreateOrthographicOffCenter(
            0, ViewportSize.X, 
            ViewportSize.Y, 0, 
            -1.0f, 1.0f
        );

        return view * projection;
    }
}
```

### 2\. 图层与深度队列 (`Layer.cs`)

GMS 中 Depth 越大越靠后绘制，且频繁调整 Depth 会导致批处理打断 。我们在引擎内部建立 `Layer` 抽象，每个 Layer 在渲染前**先按 Depth 进行不稳定性排序 (Timsort/Array.Sort)，并保持材质绑定优先**，确保 `SpriteBatch` 最大的连贯性 ：

```c#
namespace GameEngine.Features.SceneSystem;

using System.Numerics;
using GameEngine.Core.Infrastructure.Graphics;

public class RenderCommand
{
    public uint TextureHandle;
    public Vector2 Position;
    public Vector2 Size;
    public Vector4 Color;
    public Vector4 UvBounds;
    public int Depth; // 深度 (Depth 越大越靠后绘制)
}

public class Layer
{
    public string Name { get; }
    public int DepthOrder { get; set; }
    public bool IsVisible { get; set; } = true;

    private readonly List<RenderCommand> _commandBuffer = new(1024);

    public Layer(string name, int depthOrder)
    {
        Name = name;
        DepthOrder = depthOrder;
    }

    public void Submit(RenderCommand command)
    {
        _commandBuffer.Add(command);
    }

    /// <summary>
    /// 渲染当前图层的所有 Command，按 Depth 排序并提交给 SpriteBatch
    /// </summary>
    public void Draw(SpriteBatch batch)
    {
        if (!IsVisible || _commandBuffer.Count == 0) return;

        // 按 Depth 从大到小排序 (实现从后往前绘制)
        _commandBuffer.Sort((a, b) => b.Depth.CompareTo(a.Depth));

        for (int i = 0; i < _commandBuffer.Count; i++)
        {
            var cmd = _commandBuffer[i];
            batch.Draw(cmd.TextureHandle, cmd.Position, cmd.Size, cmd.Color, cmd.UvBounds);
        }

        _commandBuffer.Clear(); // 绘制完毕，清空当前帧 Buffer (零垃圾回收)
    }
}
```

### 3\. 场景聚合根 (`SceneAggregate.cs`)

掌控所有游戏实体（GameInstances）、图层（Layers）与当前激活相机（Active Camera）的一致性边界 ：

```c#
namespace GameEngine.Features.SceneSystem;

using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.Camera;

public class SceneAggregate
{
    public Guid SceneId { get; } = Guid.NewGuid();
    public Camera2D MainCamera { get; }
    
    private readonly Dictionary<string, Layer> _layers = new();
    private readonly List<Layer> _sortedLayers = new();

    public SceneAggregate(Vector2D<int> viewportSize)
    {
        MainCamera = new Camera2D(new System.Numerics.Vector2(viewportSize.X, viewportSize.Y));

        // 默认创建 GMS 经典的三个基础图层
        AddLayer("Background", 10000);
        AddLayer("Instances", 0);
        AddLayer("UI", -10000);
    }

    public void AddLayer(string name, int depthOrder)
    {
        var layer = new Layer(name, depthOrder);
        _layers[name] = layer;
        _sortedLayers.Add(layer);
        _sortedLayers.Sort((a, b) => b.DepthOrder.CompareTo(a.DepthOrder));
    }

    public Layer GetLayer(string name)
    {
        return _layers[name];
    }

    /// <summary>
    /// 场景全流程 Step 逻辑更新
    /// </summary>
    public void Update(double deltaTime)
    {
        // 更新逻辑、处理实体移动...
    }

    /// <summary>
    /// 场景渲染入口：应用 Camera 变换矩阵并按 Layer 提交渲染
    /// </summary>
    public void Render(SpriteShader shader, SpriteBatch batch)
    {
        // 1. 更新 Shader 中的 Camera 变换矩阵 (View-Projection)
        shader.SetProjection(MainCamera.GetViewProjectionMatrix());

        batch.Begin();

        // 2. 依次绘制每一个 Layer (从小到大/由远及近)
        for (int i = 0; i < _sortedLayers.Count; i++)
        {
            _sortedLayers[i].Draw(batch);
        }

        batch.End();
    }
}
```

## 二、 Phase 1.2：基于 Spatial Hash 的高性能碰撞查询切片 (超越 GMS `place_meeting`)

在 GameMaker 中，`place_meeting(x, y, obj)` 函数是 2D 动作/平台跳跃游戏的核心 API 。然而 GMS 底层的碰撞检测是 $O(N)$ 复杂度的，当场景中有上千个子弹或碰撞体时，CPU 帧率会迅速下降 。

我们将使用 **Spatial Hash Grid (空间哈希网格)**，将 2D 世界分割为固定尺寸（例如 $64 \\times 64$ 像素）的网格桶 。碰撞查询复杂度直接降为 **$O(1)$** ！

### 1\. AABB 碰撞盒与 Spatial Hash 算法 (`SpatialHashGrid.cs`)

```c#
namespace GameEngine.Features.Physics;

using System.Numerics;
using System.Runtime.CompilerServices;

public readonly record struct AABB(Vector2 Min, Vector2 Max)
{
    public bool Intersects(in AABB other)
    {
        return !(Max.X < other.Min.X || Min.X > other.Max.X ||
                 Max.Y < other.Min.Y || Min.Y > other.Max.Y);
    }
}

public class ColliderEntity
{
    public uint InstanceId { get; }
    public string Tag { get; }
    public AABB Bounds { get; set; }

    public ColliderEntity(uint id, string tag, AABB bounds)
    {
        InstanceId = id;
        Tag = tag;
        Bounds = bounds;
    }
}

public class SpatialHashGrid
{
    private readonly int _cellSize;
    private readonly Dictionary<long, List<ColliderEntity>> _buckets = new();

    public SpatialHashGrid(int cellSize = 64)
    {
        _cellSize = cellSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long HashCellKey(int x, int y)
    {
        // 将 2D 网格坐标打包为唯一的 64-bit 组合 Hash Key
        return ((long)x << 32) | (uint)y;
    }

    public void Clear()
    {
        foreach (var bucket in _buckets.Values)
        {
            bucket.Clear();
        }
    }

    /// <summary>
    /// 将碰撞实体插入空间网格桶
    /// </summary>
    public void Insert(ColliderEntity entity)
    {
        int minX = (int)Math.Floor(entity.Bounds.Min.X / _cellSize);
        int maxX = (int)Math.Floor(entity.Bounds.Max.X / _cellSize);
        int minY = (int)Math.Floor(entity.Bounds.Min.Y / _cellSize);
        int maxY = (int)Math.Floor(entity.Bounds.Max.Y / _cellSize);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                long key = HashCellKey(x, y);
                if (!_buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new List<ColliderEntity>(16);
                    _buckets[key] = bucket;
                }
                bucket.Add(entity);
            }
        }
    }

    /// <summary>
    /// 高性能对标 GMS place_meeting：检测目标预测位置是否与指定 Tag 的物体重叠
    /// </summary>
    public bool PlaceMeeting(AABB predictedBounds, string targetTag, out ColliderEntity? hitEntity)
    {
        hitEntity = null;

        int minX = (int)Math.Floor(predictedBounds.Min.X / _cellSize);
        int maxX = (int)Math.Floor(predictedBounds.Max.X / _cellSize);
        int minY = (int)Math.Floor(predictedBounds.Min.Y / _cellSize);
        int maxY = (int)Math.Floor(predictedBounds.Max.Y / _cellSize);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                long key = HashCellKey(x, y);
                if (_buckets.TryGetValue(key, out var bucket))
                {
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var target = bucket[i];
                        if (target.Tag == targetTag && predictedBounds.Intersects(target.Bounds))
                        {
                            hitEntity = target;
                            return true; // 命中碰撞！立刻返回 $O(1)$
                        }
                    }
                }
            }
        }

        return false;
    }
}
```
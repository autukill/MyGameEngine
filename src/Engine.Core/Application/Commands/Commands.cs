namespace GameEngine.Core.Application.Commands;

using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// 命令（Command）与领域事件（DomainEvent）的区别：
///   - DomainEvent 描述"已经发生的事"，由聚合根内部发出。
///   - Command 描述"想要做的事"，由外部调用方发起，触发聚合根行为。
///
/// 引入 Command 层是为了让 AI Agent / 编辑器 / 网络同步等外部源都通过统一入口驱动引擎。
///
/// 注意：本文件只保留**共享内核级**命令（Spawn/Destroy）。
/// 切片专属命令应放在对应 Vertical Slice 的 Application 子目录：
///   - FocusCameraCommand      → src/Features/Camera/Application/
///   - ApplySpotlightMaskCommand → src/Features/StencilMasking/Application/
/// 避免共享内核反向依赖切片。
/// </summary>

/// <summary>
/// 在指定场景中生成一个实例。
/// </summary>
public sealed record SpawnInstanceCommand(
    SceneAggregate Scene,
    string ObjectTypeName,
    Vector2D Position,
    LayerDepth Depth
);

/// <summary>
/// 在指定场景中销毁一个实例。
/// </summary>
public sealed record DestroyInstanceCommand(
    SceneAggregate Scene,
    InstanceId InstanceId
);

/// <summary>
/// 在指定场景中添加/更新一个图层。
/// </summary>
public sealed record AddLayerCommand(
    SceneAggregate Scene,
    string LayerName,
    int DepthOrder,
    bool IsVisible = true
);

/// <summary>
/// 设置图层的可见性。
/// </summary>
public sealed record SetLayerVisibleCommand(
    SceneAggregate Scene,
    string LayerName,
    bool IsVisible
);

/// <summary>
/// 设置场景背景配置。
/// </summary>
public sealed record SetBackgroundCommand(
    SceneAggregate Scene,
    BackgroundConfig Background
);

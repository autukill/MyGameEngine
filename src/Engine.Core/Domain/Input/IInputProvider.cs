namespace GameEngine.Core.Domain.Input;

using GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// 输入提供者抽象（GMS 的 keyboard_check / mouse_x / mouse_wheel 等价物）。
///
/// Domain 不依赖 Silk.NET；由 Infrastructure 层实现（EngineWindow 内置 InputSystem）。
/// 实例在 OnStep 中通过 GameInstance.Input 轮询查询（WASD 持续按住用 IsKeyDown）。
/// 按下/释放"沿事件"通过 SceneAggregate.PerformInput 分发给实例的 OnKeyDown/OnKeyUp。
/// </summary>
public interface IInputProvider
{
    /// <summary>该键当前是否按住（每帧轮询，对应 keyboard_check）</summary>
    bool IsKeyDown(InputKey key);

    /// <summary>Whether the key transitioned to down during the current input frame.</summary>
    bool WasKeyPressed(InputKey key) => false;

    /// <summary>Whether the key transitioned to up during the current input frame.</summary>
    bool WasKeyReleased(InputKey key) => false;

    /// <summary>鼠标屏幕坐标（像素，左上原点）</summary>
    Vector2D MousePosition { get; }

    /// <summary>本帧滚轮累积位移（向上为正，Scroll 事件累积）</summary>
    float MouseScrollDelta { get; }

    /// <summary>鼠标按键是否按住</summary>
    bool IsMouseButtonDown(MouseButton button);
}

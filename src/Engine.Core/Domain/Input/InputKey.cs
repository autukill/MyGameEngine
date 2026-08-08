namespace GameEngine.Core.Domain.Input;

/// <summary>
/// 引擎自有按键枚举（对应 GMS 的 vk_* 常量）。
/// 不依赖 Silk.NET.Input.Key，由 Infrastructure 输入层把底层按键映射为本枚举。
/// </summary>
public enum InputKey
{
    None = 0,

    // 方向
    Up,
    Down,
    Left,
    Right,

    // 字母（常用操作键）
    W,
    A,
    S,
    D,
    Q,
    E,
    R,
    F,
    M,
    B,

    // 功能键
    Space,
    Enter,
    Shift,
    Control,
    Tab,
    Backspace,
    Escape,
}

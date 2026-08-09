namespace GameEngine.Core.Infrastructure.Input;

using Silk.NET.Input;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;

using SilkKey = Silk.NET.Input.Key;
using SilkMouseButton = Silk.NET.Input.MouseButton;
using DomainMouseButton = GameEngine.Core.Domain.Input.MouseButton;

/// <summary>
/// 输入系统：缓存 IKeyboard/IMouse 设备（避免每帧 CreateInput 丢失状态——Camera.VisualTests 已踩坑），
/// 实现 Domain 的 IInputProvider（轮询查询），并暴露本帧按键沿事件（KeysPressed/KeysReleased）供场景分发。
///
/// 零分配：按下/释放缓冲用双缓冲 + 引用交换；滚轮累积在 BeginFrame 读取后清零。
/// </summary>
public sealed class InputSystem : IInputProvider, IDisposable
{
    private readonly IInputContext _context;
    private readonly IKeyboard? _keyboard;
    private readonly IMouse? _mouse;
    private bool _disposed;

    // 双缓冲（累积 ↔ 消费），BeginFrame 时引用交换，零分配
    private List<InputKey> _pressed = new();
    private List<InputKey> _pressedRead = new();
    private List<InputKey> _released = new();
    private List<InputKey> _releasedRead = new();

    private float _scrollAccumulated;
    private float _scrollDelta;
    private Vector2D _mousePosition;

    public IReadOnlyList<InputKey> KeysPressed => _pressedRead;
    public IReadOnlyList<InputKey> KeysReleased => _releasedRead;

    public InputSystem(IInputContext context)
    {
        _context = context;
        _keyboard = context.Keyboards.Count > 0 ? context.Keyboards[0] : null;
        _mouse = context.Mice.Count > 0 ? context.Mice[0] : null;

        if (_keyboard is not null)
        {
            _keyboard.KeyDown += OnKeyDown;
            _keyboard.KeyUp += OnKeyUp;
        }

        if (_mouse is not null)
        {
            _mouse.MouseMove += (_, pos) => _mousePosition = new Vector2D(pos.X, pos.Y);
            _mouse.Scroll += (_, scroll) => _scrollAccumulated += scroll.Y;
        }
    }

    /// <summary>
    /// 每帧开头调用：把帧间累积的沿事件交换到消费缓冲，清空累积器与滚轮 delta。
    /// </summary>
    public void BeginFrame()
    {
        (_pressed, _pressedRead) = (_pressedRead, _pressed);
        _pressed.Clear();
        (_released, _releasedRead) = (_releasedRead, _released);
        _released.Clear();

        _scrollDelta = _scrollAccumulated;
        _scrollAccumulated = 0f;
        if (_mouse is not null)
            _mousePosition = new Vector2D(_mouse.Position.X, _mouse.Position.Y);
    }

    private void OnKeyDown(IKeyboard keyboard, SilkKey key, int scancode)
    {
        var k = ToInputKey(key);
        if (k != InputKey.None) _pressed.Add(k);
    }

    private void OnKeyUp(IKeyboard keyboard, SilkKey key, int scancode)
    {
        var k = ToInputKey(key);
        if (k != InputKey.None) _released.Add(k);
    }

    // ============ IInputProvider（轮询查询） ============

    public bool IsKeyDown(InputKey key) =>
        _keyboard is not null && _keyboard.IsKeyPressed(ToSilkKey(key));

    public bool WasKeyPressed(InputKey key) => _pressedRead.Contains(key);

    public bool WasKeyReleased(InputKey key) => _releasedRead.Contains(key);

    public Vector2D MousePosition => _mousePosition;

    public float MouseScrollDelta => _scrollDelta;

    public bool IsMouseButtonDown(DomainMouseButton button) =>
        _mouse is not null && _mouse.IsButtonPressed(ToSilkButton(button));

    // ============ 枚举映射 ============

    private static InputKey ToInputKey(SilkKey key) => key switch
    {
        SilkKey.W => InputKey.W,
        SilkKey.A => InputKey.A,
        SilkKey.S => InputKey.S,
        SilkKey.D => InputKey.D,
        SilkKey.Q => InputKey.Q,
        SilkKey.E => InputKey.E,
        SilkKey.R => InputKey.R,
        SilkKey.F => InputKey.F,
        SilkKey.M => InputKey.M,
        SilkKey.B => InputKey.B,
        SilkKey.Space => InputKey.Space,
        SilkKey.Enter => InputKey.Enter,
        SilkKey.ShiftLeft or SilkKey.ShiftRight => InputKey.Shift,
        SilkKey.ControlLeft or SilkKey.ControlRight => InputKey.Control,
        SilkKey.Tab => InputKey.Tab,
        SilkKey.Backspace => InputKey.Backspace,
        SilkKey.Escape => InputKey.Escape,
        SilkKey.Up => InputKey.Up,
        SilkKey.Down => InputKey.Down,
        SilkKey.Left => InputKey.Left,
        SilkKey.Right => InputKey.Right,
        _ => InputKey.None,
    };

    private static SilkKey ToSilkKey(InputKey key) => key switch
    {
        InputKey.W => SilkKey.W,
        InputKey.A => SilkKey.A,
        InputKey.S => SilkKey.S,
        InputKey.D => SilkKey.D,
        InputKey.Q => SilkKey.Q,
        InputKey.E => SilkKey.E,
        InputKey.R => SilkKey.R,
        InputKey.F => SilkKey.F,
        InputKey.M => SilkKey.M,
        InputKey.B => SilkKey.B,
        InputKey.Space => SilkKey.Space,
        InputKey.Enter => SilkKey.Enter,
        InputKey.Shift => SilkKey.ShiftLeft,
        InputKey.Control => SilkKey.ControlLeft,
        InputKey.Tab => SilkKey.Tab,
        InputKey.Backspace => SilkKey.Backspace,
        InputKey.Escape => SilkKey.Escape,
        InputKey.Up => SilkKey.Up,
        InputKey.Down => SilkKey.Down,
        InputKey.Left => SilkKey.Left,
        InputKey.Right => SilkKey.Right,
        _ => SilkKey.Unknown,
    };

    private static SilkMouseButton ToSilkButton(DomainMouseButton button) => button switch
    {
        DomainMouseButton.Left => SilkMouseButton.Left,
        DomainMouseButton.Right => SilkMouseButton.Right,
        _ => SilkMouseButton.Middle,
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_keyboard is not null)
        {
            _keyboard.KeyDown -= OnKeyDown;
            _keyboard.KeyUp -= OnKeyUp;
        }

        _context.Dispose();
    }
}

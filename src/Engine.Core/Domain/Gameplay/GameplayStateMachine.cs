namespace GameEngine.Core.Domain.Gameplay;

/// <summary>
/// A small, strongly typed state machine for gameplay objects. Configuration allocates once;
/// Start, Update, ChangeTo, and Restart do not allocate after the runtime has warmed up.
/// </summary>
public sealed class GameplayStateMachine<TState> where TState : struct, Enum
{
    private const int MaximumTransitionsPerOperation = 32;

    private readonly Dictionary<TState, StateCallbacks> _states = new();
    private readonly TState _initialState;
    private TState _currentState;
    private TState _previousState;
    private TState _pendingState;
    private bool _hasPreviousState;
    private bool _hasPendingTransition;
    private bool _pendingRestart;
    private bool _isInvokingCallback;

    public GameplayStateMachine(TState initialState)
    {
        _initialState = initialState;
        _currentState = initialState;
    }

    /// <summary>The active state, or the configured initial state before Start.</summary>
    public TState CurrentState => _currentState;

    /// <summary>The state exited by the most recently committed transition.</summary>
    public TState? PreviousState => _hasPreviousState ? _previousState : null;

    /// <summary>Elapsed update time in the current state. Reset to zero on enter.</summary>
    public double Elapsed { get; private set; }

    public bool IsStarted { get; private set; }

    public bool Is(TState state) =>
        EqualityComparer<TState>.Default.Equals(_currentState, state);

    /// <summary>Registers one state. Configuration is frozen by Start.</summary>
    public GameplayStateMachine<TState> State(
        TState state,
        Action? enter = null,
        Action<double>? step = null,
        Action? exit = null)
    {
        if (IsStarted)
            throw new InvalidOperationException("State machine configuration is frozen after Start.");
        if (!_states.TryAdd(state, new StateCallbacks(enter, step, exit)))
            throw new ArgumentException($"State '{state}' is already registered.", nameof(state));
        return this;
    }

    /// <summary>Starts the machine and invokes the initial state's enter callback exactly once.</summary>
    public void Start()
    {
        if (IsStarted)
            throw new InvalidOperationException("State machine has already started.");
        if (!_states.TryGetValue(_initialState, out StateCallbacks initial))
        {
            throw new InvalidOperationException(
                $"Initial state '{_initialState}' has not been registered.");
        }

        IsStarted = true;
        _currentState = _initialState;
        _hasPreviousState = false;
        Elapsed = 0d;
        try
        {
            Invoke(initial.Enter);
            DrainTransitions();
        }
        catch
        {
            ClearPendingTransition();
            IsStarted = false;
            throw;
        }
    }

    /// <summary>
    /// Advances the current state. A transition requested by the step callback commits after that
    /// callback returns; the new state starts receiving step callbacks on the next Update.
    /// </summary>
    public void Update(double deltaTime)
    {
        EnsureStarted();
        if (!double.IsFinite(deltaTime) || deltaTime < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deltaTime), deltaTime, "Delta time must be finite and non-negative.");
        }

        Elapsed += deltaTime;
        StateCallbacks current = _states[_currentState];
        Invoke(current.Step, deltaTime);
        DrainTransitions();
    }

    /// <summary>
    /// Changes state. Calling this with the current state is an idempotent no-op; use Restart to
    /// intentionally run its exit and enter callbacks again.
    /// </summary>
    public void ChangeTo(TState nextState)
    {
        EnsureStarted();
        EnsureRegistered(nextState);
        if (EqualityComparer<TState>.Default.Equals(_currentState, nextState))
            return;

        QueueTransition(nextState, restart: false);
        if (!_isInvokingCallback)
            DrainTransitions();
    }

    /// <summary>Exits and re-enters the current state, resetting Elapsed to zero.</summary>
    public void Restart()
    {
        EnsureStarted();
        QueueTransition(_currentState, restart: true);
        if (!_isInvokingCallback)
            DrainTransitions();
    }

    private void QueueTransition(TState state, bool restart)
    {
        if (_hasPendingTransition)
        {
            if (EqualityComparer<TState>.Default.Equals(_pendingState, state) &&
                _pendingRestart == restart)
            {
                return;
            }
            throw new InvalidOperationException(
                "A callback requested more than one different state transition.");
        }

        _pendingState = state;
        _pendingRestart = restart;
        _hasPendingTransition = true;
    }

    private void DrainTransitions()
    {
        int transitionCount = 0;
        while (_hasPendingTransition)
        {
            if (++transitionCount > MaximumTransitionsPerOperation)
            {
                ClearPendingTransition();
                throw new InvalidOperationException(
                    "State transitions did not settle. Check enter/exit callbacks for a cycle.");
            }

            TState next = _pendingState;
            bool restart = _pendingRestart;
            ClearPendingTransition();
            CommitTransition(next, restart);
        }
    }

    private void CommitTransition(TState nextState, bool restart)
    {
        if (!restart && EqualityComparer<TState>.Default.Equals(_currentState, nextState))
            return;

        TState previous = _currentState;
        StateCallbacks previousCallbacks = _states[previous];
        Invoke(previousCallbacks.Exit);

        _previousState = previous;
        _hasPreviousState = true;
        _currentState = nextState;
        Elapsed = 0d;

        StateCallbacks nextCallbacks = _states[nextState];
        Invoke(nextCallbacks.Enter);
    }

    private void Invoke(Action? callback)
    {
        if (callback is null) return;
        _isInvokingCallback = true;
        try
        {
            callback();
        }
        catch
        {
            ClearPendingTransition();
            throw;
        }
        finally
        {
            _isInvokingCallback = false;
        }
    }

    private void Invoke(Action<double>? callback, double deltaTime)
    {
        if (callback is null) return;
        _isInvokingCallback = true;
        try
        {
            callback(deltaTime);
        }
        catch
        {
            ClearPendingTransition();
            throw;
        }
        finally
        {
            _isInvokingCallback = false;
        }
    }

    private void EnsureStarted()
    {
        if (!IsStarted)
            throw new InvalidOperationException("State machine must be started first.");
    }

    private void EnsureRegistered(TState state)
    {
        if (!_states.ContainsKey(state))
            throw new ArgumentException($"State '{state}' has not been registered.", nameof(state));
    }

    private void ClearPendingTransition()
    {
        _hasPendingTransition = false;
        _pendingRestart = false;
    }

    private readonly record struct StateCallbacks(
        Action? Enter,
        Action<double>? Step,
        Action? Exit);
}

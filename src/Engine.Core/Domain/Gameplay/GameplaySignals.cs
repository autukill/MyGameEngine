namespace GameEngine.Core.Domain.Gameplay;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// Strongly typed Scene-local receiver for one transient gameplay notification. Implementers opt
/// in during construction with GameInstance.ListenSignal; no reflection or global registry is used.
/// </summary>
public interface IGameplaySignalHandler<TSignal> where TSignal : struct
{
    void OnGameplaySignal(in TSignal signal);
}

/// <summary>Wraps a failing receiver with deterministic publisher/handler diagnostics.</summary>
public sealed class GameplaySignalDispatchException : InvalidOperationException
{
    public Type SignalType { get; }
    public InstanceId PublisherId { get; }
    public InstanceId HandlerId { get; }

    internal GameplaySignalDispatchException(
        Type signalType,
        InstanceId publisherId,
        InstanceId handlerId,
        Exception innerException)
        : base(
            $"Gameplay signal '{signalType.FullName}' published by '{publisherId}' failed in " +
            $"handler '{handlerId}'.",
            innerException)
    {
        SignalType = signalType;
        PublisherId = publisherId;
        HandlerId = handlerId;
    }
}

internal interface IGameplaySignalSubscription
{
    Type SignalType { get; }
    void Attach(GameplaySignalHub hub);
    void Detach(GameplaySignalHub hub);
}

internal interface IGameplaySignalContext
{
    void PublishSignal<TSignal>(GameInstance publisher, in TSignal signal)
        where TSignal : struct;
}

internal sealed class GameplaySignalSubscription<TSignal>(
    GameInstance owner,
    IGameplaySignalHandler<TSignal> handler) : IGameplaySignalSubscription
    where TSignal : struct
{
    private GameplaySignalHub? _hub;

    public Type SignalType => typeof(TSignal);
    public GameInstance Owner { get; } = owner;

    public void Attach(GameplaySignalHub hub)
    {
        if (_hub is not null && !ReferenceEquals(_hub, hub))
            throw new InvalidOperationException(
                "A gameplay signal subscription cannot belong to two Scenes.");
        if (ReferenceEquals(_hub, hub)) return;
        hub.Subscribe(this);
        _hub = hub;
    }

    public void Detach(GameplaySignalHub hub)
    {
        if (!ReferenceEquals(_hub, hub)) return;
        hub.Unsubscribe(this);
        _hub = null;
    }

    public void Dispatch(in TSignal signal) => handler.OnGameplaySignal(in signal);
}

/// <summary>
/// Type-erased coordinator whose per-type channels retain payloads as structs. The global queue
/// stores only channel/index pairs, preserving cross-type publication order without boxing payloads.
/// </summary>
internal sealed class GameplaySignalHub
{
    private readonly Dictionary<Type, IGameplaySignalChannel> _channels = [];
    private readonly List<IGameplaySignalChannel> _channelOrder = [];
    private List<PendingDispatch> _pending = [];
    private List<PendingDispatch> _dispatching = [];
    private bool _isDispatching;

    public int PendingCount => _pending.Count;

    public void Subscribe<TSignal>(GameplaySignalSubscription<TSignal> subscription)
        where TSignal : struct =>
        GetChannel<TSignal>().Subscribe(subscription);

    public void Unsubscribe<TSignal>(GameplaySignalSubscription<TSignal> subscription)
        where TSignal : struct =>
        GetChannel<TSignal>().Unsubscribe(subscription);

    public void Publish<TSignal>(GameInstance publisher, in TSignal signal)
        where TSignal : struct
    {
        GameplaySignalChannel<TSignal> channel = GetChannel<TSignal>();
        int index = channel.Enqueue(publisher.Id, in signal);
        _pending.Add(new PendingDispatch(channel, index));
    }

    public void DispatchPending(bool gameplayPaused)
    {
        if (_pending.Count == 0) return;
        if (_isDispatching)
            throw new InvalidOperationException("Gameplay signal dispatch cannot be re-entered.");

        for (int i = 0; i < _channelOrder.Count; i++)
            _channelOrder[i].BeginDispatch();
        (_pending, _dispatching) = (_dispatching, _pending);
        _isDispatching = true;
        try
        {
            for (int i = 0; i < _dispatching.Count; i++)
            {
                PendingDispatch dispatch = _dispatching[i];
                dispatch.Channel.Dispatch(dispatch.Index, gameplayPaused);
            }
        }
        finally
        {
            _isDispatching = false;
            _dispatching.Clear();
            for (int i = 0; i < _channelOrder.Count; i++)
                _channelOrder[i].EndDispatch();
        }
    }

    public void ClearPending()
    {
        _pending.Clear();
        _dispatching.Clear();
        for (int i = 0; i < _channelOrder.Count; i++)
            _channelOrder[i].ClearPending();
    }

    private GameplaySignalChannel<TSignal> GetChannel<TSignal>() where TSignal : struct
    {
        Type type = typeof(TSignal);
        if (_channels.TryGetValue(type, out IGameplaySignalChannel? existing))
            return (GameplaySignalChannel<TSignal>)existing;

        var channel = new GameplaySignalChannel<TSignal>();
        _channels.Add(type, channel);
        _channelOrder.Add(channel);
        return channel;
    }

    private readonly record struct PendingDispatch(
        IGameplaySignalChannel Channel,
        int Index);
}

internal interface IGameplaySignalChannel
{
    void BeginDispatch();
    void Dispatch(int index, bool gameplayPaused);
    void EndDispatch();
    void ClearPending();
}

internal sealed class GameplaySignalChannel<TSignal> : IGameplaySignalChannel
    where TSignal : struct
{
    private readonly List<GameplaySignalSubscription<TSignal>> _subscribers = [];
    private List<PublishedSignal> _pending = [];
    private List<PublishedSignal> _dispatching = [];

    public void Subscribe(GameplaySignalSubscription<TSignal> subscription)
    {
        if (_subscribers.Contains(subscription)) return;
        _subscribers.Add(subscription);
    }

    public void Unsubscribe(GameplaySignalSubscription<TSignal> subscription) =>
        _subscribers.Remove(subscription);

    public int Enqueue(InstanceId publisherId, in TSignal signal)
    {
        int index = _pending.Count;
        _pending.Add(new PublishedSignal(publisherId, signal));
        return index;
    }

    public void BeginDispatch() => (_pending, _dispatching) = (_dispatching, _pending);

    public void Dispatch(int index, bool gameplayPaused)
    {
        PublishedSignal publication = _dispatching[index];
        TSignal signal = publication.Signal;
        for (int i = 0; i < _subscribers.Count; i++)
        {
            GameplaySignalSubscription<TSignal> subscription = _subscribers[i];
            GameInstance owner = subscription.Owner;
            if (!owner.IsActive ||
                (gameplayPaused && owner.TimeMode == InstanceTimeMode.Gameplay))
            {
                continue;
            }

            try
            {
                subscription.Dispatch(in signal);
            }
            catch (Exception exception)
                when (exception is not GameplaySignalDispatchException)
            {
                throw new GameplaySignalDispatchException(
                    typeof(TSignal), publication.PublisherId, owner.Id, exception);
            }
        }
    }

    public void EndDispatch() => _dispatching.Clear();

    public void ClearPending()
    {
        _pending.Clear();
        _dispatching.Clear();
    }

    private readonly record struct PublishedSignal(
        InstanceId PublisherId,
        TSignal Signal);
}

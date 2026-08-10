namespace GameEngine.Features.TransformHierarchy.Gameplay;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.TransformHierarchy.Domain;

/// <summary>Opt-in GameInstance behavior that publishes a hierarchy node as world Transform.</summary>
public sealed class TransformBindingBehavior : GameplayBehavior
{
    private readonly SceneTransformRuntime _runtime;

    public TransformBindingBehavior(SceneTransformRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        Anchor = _runtime.CreateBindingAnchor(this, LocalTransform2D.Identity);
    }

    public TransformAnchor Anchor { get; }
    public LocalTransform2D LocalTransform
    {
        get => Anchor.LocalTransform;
        set => Anchor.LocalTransform = value;
    }
    internal Transform2D LastPublished { get; private set; } = Transform2D.Default;

    public TransformAnchor CreateAttachment(
        string name,
        in LocalTransform2D localTransform) =>
        _runtime.CreateAttachment(this, Anchor, name, localTransform);

    internal TransformAnchor CreateAttachment(
        TransformAnchor parent,
        string name,
        in LocalTransform2D localTransform) =>
        _runtime.CreateAttachment(this, parent, name, localTransform);

    internal void DiscardAuthoring() => _runtime.DiscardAuthoring(this);

    public override void OnCreate() => _runtime.Register(this);
    public override void OnDestroy() => _runtime.Unregister(this);

    protected override void OnWriteGameplayState(ref GameplayStateWriter writer)
        => _runtime.WriteGameplayState(ref writer, this);

    internal void SetLastPublished(in Transform2D value) => LastPublished = value;
    internal void Publish(in Transform2D world)
    {
        Owner.Position = world.Position;
        Owner.Rotation = world.Rotation;
        Owner.Scale = world.Scale;
        LastPublished = world;
    }
}

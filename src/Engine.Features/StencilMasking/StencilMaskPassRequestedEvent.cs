using GameEngine.Core.Domain.Events;

namespace GameEngine.Features.StencilMasking;

public record StencilMaskPassRequestedEvent(
    Action DrawMaskShape,
    Action DrawMaskedContent,
    StencilState State
) : IDomainEvent {
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
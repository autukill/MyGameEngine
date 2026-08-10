namespace GameEngine.Features.TransformHierarchy.Domain;

public enum TransformReparentMode
{
    /// <summary>Preserves the node's transform relative to its old parent.</summary>
    KeepLocal,

    /// <summary>Preserves the node's current world matrix.</summary>
    KeepWorld
}

namespace GameEngine.Core.Domain.Graphics;

/// <summary>Transient material resolution used by SpriteBatch state comparison.</summary>
public readonly record struct ResolvedMaterial(uint ProgramHandle, long ParameterRevision);

namespace GameEngine.Core.Domain.ValueObjects;

/// <summary>Controls conservative per-View draw culling for one gameplay instance.</summary>
public enum InstanceViewCullingMode
{
    /// <summary>
    /// Uses LocalDrawBounds when provided, otherwise the logical Sprite rectangle. Instances with
    /// no known bounds remain visible so custom drawing is never discarded accidentally.
    /// </summary>
    Automatic = 0,

    /// <summary>Always schedules Draw callbacks, even when the instance is outside the Camera.</summary>
    AlwaysVisible = 1
}

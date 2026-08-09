namespace GameEngine.Features.RenderPipeline.Domain;

/// <summary>Controls how a rendered Surface is fitted into a presentation Viewport.</summary>
public enum ViewportFitMode
{
    /// <summary>Fill the Viewport and allow aspect-ratio distortion.</summary>
    Stretch,

    /// <summary>Show the complete source and leave unused space around it.</summary>
    Contain,

    /// <summary>Fill the Viewport while cropping the source around its center.</summary>
    Cover
}

namespace GameEngine.Hosting;

using System.Numerics;
using GameEngine.Core.Domain.Gameplay;

/// <summary>Visible lifecycle of a declarative Scene transition.</summary>
public enum SceneTransitionPhase
{
    Idle,
    FadingOut,
    Switching,
    FadingIn
}

/// <summary>Immutable fade timing, color and input policy for one Scene switch.</summary>
public readonly record struct SceneTransitionOptions
{
    public Vector4 Color { get; }
    public double FadeOutDuration { get; }
    public double FadeInDuration { get; }
    public bool BlockInput { get; }
    internal bool IsInitialized { get; }

    public SceneTransitionOptions(
        Vector4 color,
        double fadeOutDuration,
        double fadeInDuration,
        bool blockInput = true)
    {
        ValidateColor(color);
        ValidateDuration(fadeOutDuration, nameof(fadeOutDuration));
        ValidateDuration(fadeInDuration, nameof(fadeInDuration));
        Color = color;
        FadeOutDuration = fadeOutDuration;
        FadeInDuration = fadeInDuration;
        BlockInput = blockInput;
        IsInitialized = true;
    }

    private static void ValidateColor(Vector4 color)
    {
        if (!float.IsFinite(color.X) || !float.IsFinite(color.Y) ||
            !float.IsFinite(color.Z) || !float.IsFinite(color.W) ||
            color.X < 0f || color.X > 1f || color.Y < 0f || color.Y > 1f ||
            color.Z < 0f || color.Z > 1f || color.W != 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(color), "Transition RGB must be finite in [0,1] and alpha must be exactly 1.");
        }
    }

    private static void ValidateDuration(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0d)
            throw new ArgumentOutOfRangeException(parameterName);
    }
}

/// <summary>Common transition presets; games can construct explicit options for custom timing.</summary>
public static class SceneTransitions
{
    public static SceneTransitionOptions FadeThroughBlack(
        double fadeOutDuration = .2d,
        double fadeInDuration = .2d,
        bool blockInput = true) =>
        new(new Vector4(0f, 0f, 0f, 1f),
            fadeOutDuration,
            fadeInDuration,
            blockInput);

    public static SceneTransitionOptions FadeThroughColor(
        Vector4 color,
        double fadeOutDuration = .2d,
        double fadeInDuration = .2d,
        bool blockInput = true) =>
        new(color, fadeOutDuration, fadeInDuration, blockInput);
}

/// <summary>Allocation-free read-only transition state for draw, diagnostics and gameplay gates.</summary>
public readonly record struct SceneTransitionSnapshot(
    SceneTransitionPhase Phase,
    SceneRef Target,
    float Opacity,
    Vector4 Color,
    bool BlocksInput)
{
    public bool IsActive => Phase != SceneTransitionPhase.Idle;
}

/// <summary>A pre-commit Scene content load that recovered by fading the old Scene back in.</summary>
public sealed record SceneTransitionFailure(
    SceneRef Source,
    SceneRef Target,
    Exception Exception);

internal sealed record SceneSwitchRequest(
    ISceneActivation Activation,
    SceneTransitionOptions? Transition)
{
    public bool HasSameRequest(SceneSwitchRequest other) =>
        Activation.HasSamePayload(other.Activation) && Transition == other.Transition;
}

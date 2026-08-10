namespace GameEngine.Hosting;

public enum AudioInitializationFailureMode
{
    UseSilentBackend,
    Throw
}

/// <summary>Opt-in audio device and voice configuration for the default host.</summary>
public sealed record AudioHostingOptions(
    int MaxVoices = 32,
    bool ForceSilentBackend = false,
    AudioInitializationFailureMode FailureMode = AudioInitializationFailureMode.UseSilentBackend)
{
    internal void Validate()
    {
        if (MaxVoices is < 1 or > 1024)
            throw new InvalidOperationException("Audio max voices must be between 1 and 1024.");
    }
}

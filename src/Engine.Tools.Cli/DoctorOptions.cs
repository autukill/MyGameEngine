namespace GameEngine.Tools.Cli;

public sealed record DoctorOptions(
    string ProjectPath,
    string Configuration = "Debug",
    bool ProbeOpenGl = false)
{
    public static DoctorOptions CurrentDirectory => new(Environment.CurrentDirectory);
}

public interface IOpenGlContextProbe
{
    OpenGlProbeResult Probe();
}

public sealed record OpenGlProbeResult(bool Success, string Message);

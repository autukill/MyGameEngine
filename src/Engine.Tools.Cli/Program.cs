namespace GameEngine.Tools.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || !StringComparer.Ordinal.Equals(args[0], "doctor"))
        {
            WriteUsage();
            return 2;
        }

        try
        {
            DoctorOptions options = ParseDoctorOptions(args.AsSpan(1));
            DoctorReport report = new ProjectDoctor().Run(options);
            Print(report);
            return report.HasErrors ? 1 : 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            WriteUsage();
            return 2;
        }
    }

    private static DoctorOptions ParseDoctorOptions(ReadOnlySpan<string> args)
    {
        string projectPath = Environment.CurrentDirectory;
        string configuration = "Debug";
        bool probeOpenGl = false;
        bool projectSet = false;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case "--configuration":
                    if (++index >= args.Length)
                        throw new ArgumentException("--configuration requires a value.");
                    configuration = args[index];
                    break;
                case "--probe-opengl":
                    probeOpenGl = true;
                    break;
                default:
                    if (argument.StartsWith('-'))
                        throw new ArgumentException($"Unknown option '{argument}'.");
                    if (projectSet)
                        throw new ArgumentException("Only one project path can be specified.");
                    projectPath = argument;
                    projectSet = true;
                    break;
            }
        }

        return new DoctorOptions(projectPath, configuration, probeOpenGl);
    }

    private static void Print(DoctorReport report)
    {
        Console.WriteLine("=== MyGameEngine Doctor ===");
        foreach (DoctorDiagnostic diagnostic in report.Diagnostics)
        {
            string label = diagnostic.Severity switch
            {
                DoctorDiagnosticSeverity.Info => "PASS",
                DoctorDiagnosticSeverity.Warning => "WARN",
                _ => "FAIL"
            };
            Console.WriteLine($"[{label}] {diagnostic.Code} {diagnostic.Message}");
            if (diagnostic.Remediation is not null)
                Console.WriteLine($"       Fix: {diagnostic.Remediation}");
        }
        Console.WriteLine(
            $"Summary: {report.ErrorCount} error(s), {report.WarningCount} warning(s).");
    }

    private static void WriteUsage() => Console.Error.WriteLine(
        "Usage: gameengine doctor [project-or-directory] " +
        "[--configuration <name>] [--probe-opengl]");
}

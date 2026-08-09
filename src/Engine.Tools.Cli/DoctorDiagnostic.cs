namespace GameEngine.Tools.Cli;

public enum DoctorDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record DoctorDiagnostic(
    string Code,
    DoctorDiagnosticSeverity Severity,
    string Message,
    string? Remediation = null);

public sealed record DoctorReport(IReadOnlyList<DoctorDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(item =>
        item.Severity == DoctorDiagnosticSeverity.Error);

    public int WarningCount => Diagnostics.Count(item =>
        item.Severity == DoctorDiagnosticSeverity.Warning);

    public int ErrorCount => Diagnostics.Count(item =>
        item.Severity == DoctorDiagnosticSeverity.Error);
}

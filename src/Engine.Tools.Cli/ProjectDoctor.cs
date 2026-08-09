namespace GameEngine.Tools.Cli;

using System.Text.Json;
using System.Xml.Linq;

public sealed class ProjectDoctor
{
    private const string GameSdkPackage = "MyGameEngine.GameSdk";
    private const string ContentPipelinePackage = "MyGameEngine.ContentPipeline";
    private readonly IOpenGlContextProbe _openGlProbe;

    public ProjectDoctor(IOpenGlContextProbe? openGlProbe = null)
    {
        _openGlProbe = openGlProbe ?? new OpenGlContextProbe();
    }

    public DoctorReport Run(DoctorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var diagnostics = new List<DoctorDiagnostic>();
        CheckRuntime(diagnostics);

        string? project = ResolveProject(options.ProjectPath, diagnostics);
        if (project is not null)
            CheckProject(project, options.Configuration, diagnostics);

        if (options.ProbeOpenGl)
            CheckOpenGl(diagnostics);
        else
            Add(diagnostics, "GE300", DoctorDiagnosticSeverity.Info,
                "OpenGL probe skipped; pass --probe-opengl to create a hidden 3.3 context.");

        return new DoctorReport(diagnostics);
    }

    private static void CheckRuntime(List<DoctorDiagnostic> diagnostics)
    {
        if (Environment.Version.Major >= 10)
        {
            Add(diagnostics, "GE001", DoctorDiagnosticSeverity.Info,
                $".NET runtime {Environment.Version} satisfies the net10.0 requirement.");
        }
        else
        {
            Add(diagnostics, "GE001", DoctorDiagnosticSeverity.Error,
                $".NET runtime {Environment.Version} is older than 10.",
                "Install the .NET 10 SDK and reinstall MyGameEngine.Cli.");
        }
    }

    private static string? ResolveProject(
        string requestedPath,
        List<DoctorDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            Add(diagnostics, "GE100", DoctorDiagnosticSeverity.Error,
                "Project path cannot be empty.");
            return null;
        }

        string path;
        try
        {
            path = Path.GetFullPath(requestedPath);
        }
        catch (Exception ex)
        {
            Add(diagnostics, "GE100", DoctorDiagnosticSeverity.Error,
                $"Project path is invalid: {ex.Message}");
            return null;
        }

        if (File.Exists(path))
        {
            if (!path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                Add(diagnostics, "GE101", DoctorDiagnosticSeverity.Error,
                    $"Project file must use the .csproj extension: {path}");
                return null;
            }
            return path;
        }

        if (!Directory.Exists(path))
        {
            Add(diagnostics, "GE100", DoctorDiagnosticSeverity.Error,
                $"Project path does not exist: {path}");
            return null;
        }

        string[] projects = Directory.GetFiles(path, "*.csproj", SearchOption.TopDirectoryOnly);
        if (projects.Length == 1) return projects[0];
        if (projects.Length == 0)
        {
            Add(diagnostics, "GE102", DoctorDiagnosticSeverity.Error,
                $"No .csproj was found in: {path}",
                "Pass an explicit project file or run the command from a game project directory.");
            return null;
        }

        Add(diagnostics, "GE103", DoctorDiagnosticSeverity.Error,
            $"Multiple .csproj files were found in: {path}",
            "Pass the exact game project path.");
        return null;
    }

    private static void CheckProject(
        string projectPath,
        string configuration,
        List<DoctorDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(configuration) ||
            configuration.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            Add(diagnostics, "GE104", DoctorDiagnosticSeverity.Error,
                $"Configuration name is invalid: '{configuration}'.");
            return;
        }

        XDocument document;
        try
        {
            document = XDocument.Load(projectPath, LoadOptions.None);
        }
        catch (Exception ex)
        {
            Add(diagnostics, "GE105", DoctorDiagnosticSeverity.Error,
                $"Could not parse project XML: {ex.Message}");
            return;
        }

        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        Add(diagnostics, "GE106", DoctorDiagnosticSeverity.Info,
            $"Project: {projectPath}");

        string? targetFramework = Property(document, "TargetFramework");
        string? targetFrameworks = Property(document, "TargetFrameworks");
        bool targetsNet10 = StringComparer.OrdinalIgnoreCase.Equals(targetFramework, "net10.0") ||
            (targetFrameworks?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains("net10.0", StringComparer.OrdinalIgnoreCase) ?? false);
        Add(diagnostics, "GE110",
            targetsNet10 ? DoctorDiagnosticSeverity.Info : DoctorDiagnosticSeverity.Error,
            targetsNet10
                ? "Project targets net10.0."
                : "Project does not directly declare a net10.0 target.",
            targetsNet10 ? null : "Set TargetFramework to net10.0 or include it in TargetFrameworks.");

        var packageReferences = document.Descendants()
            .Where(node => node.Name.LocalName == "PackageReference")
            .Select(node => new PackageReference(
                Attribute(node, "Include") ?? Attribute(node, "Update") ?? string.Empty,
                Attribute(node, "Version") ?? ChildValue(node, "Version")))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToArray();

        PackageReference? gameSdk = packageReferences.FirstOrDefault(item =>
            StringComparer.OrdinalIgnoreCase.Equals(item.Name, GameSdkPackage));
        PackageReference? contentPipeline = packageReferences.FirstOrDefault(item =>
            StringComparer.OrdinalIgnoreCase.Equals(item.Name, ContentPipelinePackage));

        CheckPackage(gameSdk, GameSdkPackage, required: true, diagnostics, "GE120");
        CheckPackage(contentPipeline, ContentPipelinePackage, required: false, diagnostics, "GE121");
        if (gameSdk is not null && contentPipeline is not null &&
            !string.IsNullOrWhiteSpace(gameSdk.Version) &&
            !string.IsNullOrWhiteSpace(contentPipeline.Version) &&
            !StringComparer.OrdinalIgnoreCase.Equals(gameSdk.Version, contentPipeline.Version))
        {
            Add(diagnostics, "GE122", DoctorDiagnosticSeverity.Error,
                $"GameSdk version {gameSdk.Version} does not match ContentPipeline version {contentPipeline.Version}.",
                "Use the same MyGameEngine version for runtime and build packages.");
        }

        CheckRestore(projectDirectory, gameSdk, contentPipeline, diagnostics);
        CheckContent(document, projectDirectory, configuration, targetFramework ?? "net10.0", diagnostics);
    }

    private static void CheckPackage(
        PackageReference? package,
        string name,
        bool required,
        List<DoctorDiagnostic> diagnostics,
        string code)
    {
        if (package is null)
        {
            Add(diagnostics, code,
                required ? DoctorDiagnosticSeverity.Error : DoctorDiagnosticSeverity.Warning,
                $"PackageReference '{name}' is missing.",
                required
                    ? $"Add PackageReference Include=\"{name}\" to the game project."
                    : "Add MyGameEngine.ContentPipeline to enable declarative content builds.");
            return;
        }

        Add(diagnostics, code, DoctorDiagnosticSeverity.Info,
            string.IsNullOrWhiteSpace(package.Version)
                ? $"PackageReference '{name}' is present; version is managed externally."
                : $"PackageReference '{name}' version {package.Version} is present.");
    }

    private static void CheckRestore(
        string projectDirectory,
        PackageReference? gameSdk,
        PackageReference? contentPipeline,
        List<DoctorDiagnostic> diagnostics)
    {
        string assetsPath = Path.Combine(projectDirectory, "obj", "project.assets.json");
        if (!File.Exists(assetsPath))
        {
            Add(diagnostics, "GE130", DoctorDiagnosticSeverity.Warning,
                "NuGet restore output obj/project.assets.json is missing.",
                "Run dotnet restore.");
            return;
        }

        try
        {
            using JsonDocument assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
            JsonElement libraries = assets.RootElement.GetProperty("libraries");
            bool sdkResolved = gameSdk is null || libraries.EnumerateObject().Any(item =>
                item.Name.StartsWith(GameSdkPackage + "/", StringComparison.OrdinalIgnoreCase));
            bool pipelineResolved = contentPipeline is null || libraries.EnumerateObject().Any(item =>
                item.Name.StartsWith(ContentPipelinePackage + "/", StringComparison.OrdinalIgnoreCase));
            if (sdkResolved && pipelineResolved)
            {
                Add(diagnostics, "GE130", DoctorDiagnosticSeverity.Info,
                    "NuGet restore output resolves the declared MyGameEngine packages.");
            }
            else
            {
                Add(diagnostics, "GE130", DoctorDiagnosticSeverity.Error,
                    "NuGet restore output does not resolve every declared MyGameEngine package.",
                    "Run dotnet restore and inspect NuGet sources/version constraints.");
            }
        }
        catch (Exception ex)
        {
            Add(diagnostics, "GE130", DoctorDiagnosticSeverity.Warning,
                $"Could not inspect obj/project.assets.json: {ex.Message}",
                "Run dotnet restore to regenerate the file.");
        }
    }

    private static void CheckContent(
        XDocument project,
        string projectDirectory,
        string configuration,
        string targetFramework,
        List<DoctorDiagnostic> diagnostics)
    {
        string? packagesRootValue = Property(project, "GameEngineContentPackagesRoot");
        if (string.IsNullOrWhiteSpace(packagesRootValue))
        {
            Add(diagnostics, "GE200", DoctorDiagnosticSeverity.Warning,
                "GameEngineContentPackagesRoot is not directly declared; content diagnostics were skipped.",
                "Declare the property in the game project when using ContentPipeline.");
            return;
        }

        string? packagesRoot = ExpandProjectPath(packagesRootValue, projectDirectory);
        if (packagesRoot is null)
        {
            Add(diagnostics, "GE200", DoctorDiagnosticSeverity.Error,
                $"Content packages root contains unsupported MSBuild expressions: {packagesRootValue}",
                "Use a path relative to the project or $(MSBuildProjectDirectory).");
            return;
        }
        if (!Directory.Exists(packagesRoot))
        {
            Add(diagnostics, "GE201", DoctorDiagnosticSeverity.Error,
                $"Content packages root does not exist: {packagesRoot}");
            return;
        }

        string manifestRelative = Property(project, "GameEngineContentManifest") ?? "assets.json";
        string manifestPath;
        try
        {
            manifestPath = Path.GetFullPath(Path.Combine(packagesRoot, manifestRelative));
        }
        catch (Exception ex)
        {
            Add(diagnostics, "GE202", DoctorDiagnosticSeverity.Error,
                $"Content manifest path is invalid: {ex.Message}");
            return;
        }

        if (!IsWithin(packagesRoot, manifestPath))
        {
            Add(diagnostics, "GE202", DoctorDiagnosticSeverity.Error,
                $"Content manifest escapes the packages root: {manifestRelative}");
            return;
        }
        if (!File.Exists(manifestPath))
        {
            Add(diagnostics, "GE203", DoctorDiagnosticSeverity.Error,
                $"Content manifest does not exist: {manifestPath}");
            return;
        }

        try
        {
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            JsonElement root = manifest.RootElement;
            int schemaVersion = root.TryGetProperty("schemaVersion", out JsonElement schema)
                ? schema.GetInt32()
                : 0;
            string? id = root.TryGetProperty("id", out JsonElement packageId)
                ? packageId.GetString()
                : null;
            if (schemaVersion != 1 || string.IsNullOrWhiteSpace(id))
            {
                Add(diagnostics, "GE204", DoctorDiagnosticSeverity.Error,
                    "Content manifest must declare schemaVersion 1 and a non-empty id.");
                return;
            }
            Add(diagnostics, "GE204", DoctorDiagnosticSeverity.Info,
                $"Content manifest '{id}' uses schemaVersion 1.");
        }
        catch (Exception ex)
        {
            Add(diagnostics, "GE204", DoctorDiagnosticSeverity.Error,
                $"Content manifest JSON is invalid: {ex.Message}");
            return;
        }

        string generated = Path.Combine(
            projectDirectory, "obj", configuration, targetFramework, "GameEngine.Content.g.cs");
        CheckBuildArtifact(
            diagnostics, "GE210", generated, manifestPath,
            "Strongly typed content source", "Run dotnet build.");

        string outputSubdirectory = Property(project, "GameEngineContentOutputSubdirectory") ??
            "AssetsCompiled";
        string compiledManifest = Path.Combine(
            projectDirectory, "bin", configuration, targetFramework,
            outputSubdirectory, manifestRelative);
        CheckBuildArtifact(
            diagnostics, "GE211", compiledManifest, manifestPath,
            "Compiled runtime content", "Run dotnet build with the selected configuration.");
    }

    private void CheckOpenGl(List<DoctorDiagnostic> diagnostics)
    {
        OpenGlProbeResult result = _openGlProbe.Probe();
        Add(diagnostics, "GE300",
            result.Success ? DoctorDiagnosticSeverity.Info : DoctorDiagnosticSeverity.Error,
            result.Message,
            result.Success ? null : "Update the graphics driver and verify OpenGL 3.3 Core support.");
    }

    private static void CheckBuildArtifact(
        List<DoctorDiagnostic> diagnostics,
        string code,
        string artifact,
        string manifest,
        string label,
        string remediation)
    {
        if (!File.Exists(artifact))
        {
            Add(diagnostics, code, DoctorDiagnosticSeverity.Warning,
                $"{label} is missing: {artifact}", remediation);
            return;
        }

        if (File.GetLastWriteTimeUtc(artifact) < File.GetLastWriteTimeUtc(manifest))
        {
            Add(diagnostics, code, DoctorDiagnosticSeverity.Warning,
                $"{label} is older than the source manifest: {artifact}", remediation);
            return;
        }

        Add(diagnostics, code, DoctorDiagnosticSeverity.Info,
            $"{label} is present and current: {artifact}");
    }

    private static string? ExpandProjectPath(string value, string projectDirectory)
    {
        string expanded = value.Replace(
            "$(MSBuildProjectDirectory)",
            projectDirectory,
            StringComparison.OrdinalIgnoreCase);
        if (expanded.Contains("$(", StringComparison.Ordinal)) return null;
        return Path.GetFullPath(Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(projectDirectory, expanded));
    }

    private static bool IsWithin(string root, string path)
    {
        string normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        string normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string? Property(XDocument document, string name) =>
        document.Descendants()
            .Where(node => node.Name.LocalName == name)
            .Select(node => node.Value.Trim())
            .LastOrDefault(value => value.Length > 0);

    private static string? Attribute(XElement element, string name) =>
        element.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value.Trim();

    private static string? ChildValue(XElement element, string name) =>
        element.Elements()
            .FirstOrDefault(child => child.Name.LocalName == name)?.Value.Trim();

    private static void Add(
        List<DoctorDiagnostic> diagnostics,
        string code,
        DoctorDiagnosticSeverity severity,
        string message,
        string? remediation = null) =>
        diagnostics.Add(new DoctorDiagnostic(code, severity, message, remediation));

    private sealed record PackageReference(string Name, string? Version);
}

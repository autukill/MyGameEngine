namespace GameEngine.Tools.Cli.Tests;

using GameEngine.Tools.Cli;

internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        Console.WriteLine("=== GameEngine CLI Tests ===\n");
        Run("Healthy project", HealthyProject);
        Run("Missing and ambiguous project", MissingAndAmbiguousProject);
        Run("Framework, package, and path diagnostics", InvalidProjectConfiguration);
        Run("Manifest and build artifact diagnostics", ManifestAndBuildArtifacts);
        Run("OpenGL probe is explicit and injectable", OpenGlProbe);

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All GameEngine CLI tests passed ==="
            : $"=== {_failures} GameEngine CLI test(s) FAILED ===");
        return _failures == 0 ? 0 : 1;
    }

    private static void HealthyProject()
    {
        using var project = TestProject.Create();
        project.WriteProject(ProjectXml());
        project.WriteManifest("mygame.assets");
        project.WriteRestoreAssets();
        project.WriteBuildOutputs();

        DoctorReport report = new ProjectDoctor(new FakeProbe(true)).Run(
            new DoctorOptions(project.Root));

        Check(!report.HasErrors && report.WarningCount == 0,
            "A restored and built template-style project is healthy");
        Check(Has(report, "GE001", DoctorDiagnosticSeverity.Info) &&
              Has(report, "GE110", DoctorDiagnosticSeverity.Info) &&
              Has(report, "GE122") == false &&
              Has(report, "GE210", DoctorDiagnosticSeverity.Info) &&
              Has(report, "GE211", DoctorDiagnosticSeverity.Info),
            "Runtime, framework, packages, and content outputs are reported");
        Check(Has(report, "GE300", DoctorDiagnosticSeverity.Info),
            "OpenGL is skipped without invoking the probe");
    }

    private static void MissingAndAmbiguousProject()
    {
        using var project = TestProject.Create();
        DoctorReport missing = new ProjectDoctor(new FakeProbe(true)).Run(
            new DoctorOptions(project.Root));
        Check(Has(missing, "GE102", DoctorDiagnosticSeverity.Error),
            "A directory without a project is rejected");

        project.WriteProject(ProjectXml(), "One.csproj");
        project.WriteProject(ProjectXml(), "Two.csproj");
        DoctorReport ambiguous = new ProjectDoctor(new FakeProbe(true)).Run(
            new DoctorOptions(project.Root));
        Check(Has(ambiguous, "GE103", DoctorDiagnosticSeverity.Error),
            "A directory with multiple projects requires an explicit path");
    }

    private static void InvalidProjectConfiguration()
    {
        using var project = TestProject.Create();
        project.WriteProject(ProjectXml(
            targetFramework: "net9.0",
            sdkVersion: "0.1.0-alpha.1",
            pipelineVersion: "0.2.0",
            packagesRoot: "$(UnknownRoot)\\Assets",
            manifest: "../outside.json"));
        project.WriteRestoreAssets();

        DoctorReport report = new ProjectDoctor(new FakeProbe(true)).Run(
            new DoctorOptions(project.ProjectPath));
        Check(Has(report, "GE110", DoctorDiagnosticSeverity.Error),
            "Non-net10 projects are rejected");
        Check(Has(report, "GE122", DoctorDiagnosticSeverity.Error),
            "Mismatched GameSdk and ContentPipeline versions are rejected");
        Check(Has(report, "GE200", DoctorDiagnosticSeverity.Error),
            "Unsupported MSBuild expressions in content paths are rejected");
    }

    private static void ManifestAndBuildArtifacts()
    {
        using var project = TestProject.Create();
        project.WriteProject(ProjectXml());
        project.WriteRestoreAssets();
        project.WriteManifest("", schemaVersion: 2);

        DoctorReport invalidManifest = new ProjectDoctor(new FakeProbe(true)).Run(
            new DoctorOptions(project.ProjectPath));
        Check(Has(invalidManifest, "GE204", DoctorDiagnosticSeverity.Error),
            "Unsupported schema and empty package id are rejected");

        project.WriteManifest("mygame.assets");
        DoctorReport missingOutputs = new ProjectDoctor(new FakeProbe(true)).Run(
            new DoctorOptions(project.ProjectPath));
        Check(!missingOutputs.HasErrors &&
              Has(missingOutputs, "GE210", DoctorDiagnosticSeverity.Warning) &&
              Has(missingOutputs, "GE211", DoctorDiagnosticSeverity.Warning),
            "Missing generated and runtime outputs are actionable warnings");
    }

    private static void OpenGlProbe()
    {
        using var project = TestProject.Create();
        project.WriteProject(ProjectXml());
        project.WriteManifest("mygame.assets");
        project.WriteRestoreAssets();
        project.WriteBuildOutputs();

        var skippedProbe = new FakeProbe(false);
        DoctorReport skipped = new ProjectDoctor(skippedProbe).Run(
            new DoctorOptions(project.ProjectPath));
        Check(skippedProbe.CallCount == 0 && !skipped.HasErrors,
            "Default doctor run has no graphics side effect");

        var failingProbe = new FakeProbe(false);
        DoctorReport failed = new ProjectDoctor(failingProbe).Run(
            new DoctorOptions(project.ProjectPath, ProbeOpenGl: true));
        Check(failingProbe.CallCount == 1 &&
              Has(failed, "GE300", DoctorDiagnosticSeverity.Error),
            "An explicitly requested OpenGL failure controls the report status");
    }

    private static bool Has(
        DoctorReport report,
        string code,
        DoctorDiagnosticSeverity? severity = null) =>
        report.Diagnostics.Any(item =>
            item.Code == code && (severity is null || item.Severity == severity));

    private static string ProjectXml(
        string targetFramework = "net10.0",
        string sdkVersion = "0.1.0-alpha.1",
        string pipelineVersion = "0.1.0-alpha.1",
        string packagesRoot = "$(MSBuildProjectDirectory)\\Assets",
        string manifest = "assets.json") => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>{targetFramework}</TargetFramework>
            <GameEngineContentPackagesRoot>{packagesRoot}</GameEngineContentPackagesRoot>
            <GameEngineContentManifest>{manifest}</GameEngineContentManifest>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="MyGameEngine.GameSdk" Version="{sdkVersion}" />
            <PackageReference Include="MyGameEngine.ContentPipeline" Version="{pipelineVersion}" />
          </ItemGroup>
        </Project>
        """;

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"  [PASS] {name}");
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine($"  [FAIL] {name}: {ex.Message}");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FakeProbe(bool success) : IOpenGlContextProbe
    {
        public int CallCount { get; private set; }

        public OpenGlProbeResult Probe()
        {
            CallCount++;
            return new OpenGlProbeResult(success, success ? "probe ok" : "probe failed");
        }
    }

    private sealed class TestProject : IDisposable
    {
        public string Root { get; }
        public string ProjectPath => Path.Combine(Root, "Game.csproj");

        private TestProject(string root)
        {
            Root = root;
        }

        public static TestProject Create() => new(
            Directory.CreateTempSubdirectory("mygame-doctor-tests-").FullName);

        public void WriteProject(string content, string name = "Game.csproj") =>
            File.WriteAllText(Path.Combine(Root, name), content);

        public void WriteManifest(string id, int schemaVersion = 1)
        {
            string assets = Directory.CreateDirectory(Path.Combine(Root, "Assets")).FullName;
            File.WriteAllText(Path.Combine(assets, "assets.json"), $$"""
                {
                  "schemaVersion": {{schemaVersion}},
                  "id": "{{id}}",
                  "dependencies": [],
                  "textures": [],
                  "sprites": []
                }
                """);
        }

        public void WriteRestoreAssets()
        {
            string obj = Directory.CreateDirectory(Path.Combine(Root, "obj")).FullName;
            File.WriteAllText(Path.Combine(obj, "project.assets.json"), """
                {
                  "libraries": {
                    "MyGameEngine.GameSdk/0.1.0-alpha.1": {},
                    "MyGameEngine.ContentPipeline/0.1.0-alpha.1": {}
                  }
                }
                """);
        }

        public void WriteBuildOutputs()
        {
            string generatedDirectory = Directory.CreateDirectory(
                Path.Combine(Root, "obj", "Debug", "net10.0")).FullName;
            string outputDirectory = Directory.CreateDirectory(
                Path.Combine(Root, "bin", "Debug", "net10.0", "AssetsCompiled")).FullName;
            string generated = Path.Combine(generatedDirectory, "GameEngine.Content.g.cs");
            string compiled = Path.Combine(outputDirectory, "assets.json");
            File.WriteAllText(generated, "// generated");
            File.Copy(Path.Combine(Root, "Assets", "assets.json"), compiled, overwrite: true);
            DateTime current = DateTime.UtcNow.AddSeconds(1);
            File.SetLastWriteTimeUtc(generated, current);
            File.SetLastWriteTimeUtc(compiled, current);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

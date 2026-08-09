namespace GameEngine.Distribution.Tests;

using System.Diagnostics;
using System.IO.Compression;
using System.Security;
using System.Text.Json;
using System.Xml.Linq;

internal static class Program
{
    private static readonly string[] RuntimeAssemblies =
    [
        "Bloom.dll",
        "Camera.dll",
        "ContentAssets.dll",
        "Engine.Core.dll",
        "Engine.Hosting.dll",
        "Presentation.dll",
        "RenderPipeline.dll",
        "SceneSystem.dll",
        "Sprites.dll",
        "StencilMasking.dll",
        "TextureAssets.dll",
        "TextureAtlas.dll",
        "ToneMapping.dll"
    ];

    private static int _failures;

    private static int Main()
    {
        Console.WriteLine("=== Game SDK Distribution Tests ===\n");
        string repositoryRoot = FindRepositoryRoot();
        string version = ReadPackageVersion(repositoryRoot);
        string workspace = Directory.CreateTempSubdirectory("mygame-distribution-tests-").FullName;

        try
        {
            VerifyDistribution(repositoryRoot, workspace, version);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All Game SDK distribution tests passed ==="
            : $"=== {_failures} Game SDK distribution test(s) FAILED ===");
        return _failures == 0 ? 0 : 1;
    }

    private static void VerifyDistribution(
        string repositoryRoot,
        string workspace,
        string version)
    {
        string feed = Directory.CreateDirectory(Path.Combine(workspace, "local feed")).FullName;
        string cliHome = Directory.CreateDirectory(Path.Combine(workspace, "dotnet home")).FullName;
        string packages = Directory.CreateDirectory(Path.Combine(workspace, "consumer packages")).FullName;
        string toolPath = Directory.CreateDirectory(Path.Combine(workspace, "installed tools")).FullName;
        string nugetConfig = Path.Combine(workspace, "NuGet.Config");

        RunRepositoryDotNet(repositoryRoot,
            "pack", "src/Engine.Distribution.GameSdk/Engine.Distribution.GameSdk.csproj",
            "--configuration", "Release", "--output", feed);
        CopyRestoredDependencyPackages(repositoryRoot, feed);
        RunRepositoryDotNet(repositoryRoot,
            "pack", "src/Engine.Build.ContentPipeline/Engine.Build.ContentPipeline.csproj",
            "--configuration", "Release", "--output", feed);
        RunRepositoryDotNet(repositoryRoot,
            "pack", "src/Engine.Tools.Cli/Engine.Tools.Cli.csproj",
            "--configuration", "Release", "--output", feed);
        RunRepositoryDotNet(repositoryRoot,
            "pack", "src/Engine.Templates/Engine.Templates.csproj",
            "--configuration", "Release", "--output", feed);

        string gameSdkPackage = Path.Combine(feed, $"MyGameEngine.GameSdk.{version}.nupkg");
        string contentPackage = Path.Combine(feed, $"MyGameEngine.ContentPipeline.{version}.nupkg");
        string cliPackage = Path.Combine(feed, $"MyGameEngine.Cli.{version}.nupkg");
        string templatePackage = Path.Combine(feed, $"MyGameEngine.Templates.{version}.nupkg");
        Check(File.Exists(gameSdkPackage) &&
              File.Exists(contentPackage) &&
              File.Exists(cliPackage) &&
              File.Exists(templatePackage),
            "GameSdk, ContentPipeline, CLI, and Templates packages share one version");

        VerifyGameSdkPackage(repositoryRoot, gameSdkPackage);
        VerifyCliPackage(cliPackage);
        VerifyTemplatePackage(templatePackage, version);

        WriteNuGetConfig(nugetConfig, feed);
        RunDotNet(workspace, cliHome,
            "tool", "install", "MyGameEngine.Cli",
            "--tool-path", toolPath,
            "--version", version,
            "--configfile", nugetConfig,
            "--no-cache");
        RunDotNet(workspace, cliHome,
            "new", "install", templatePackage, "--force");

        string consumer = Path.Combine(workspace, "generated game with spaces");
        RunDotNet(workspace, cliHome,
            "new", "mygameengine-game",
            "--name", "SampleGame",
            "--output", consumer);

        string projectPath = Path.Combine(consumer, "SampleGame.csproj");
        string projectText = File.ReadAllText(projectPath);
        string allText = string.Join(
            Environment.NewLine,
            Directory.GetFiles(consumer, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetExtension(path) is ".cs" or ".csproj" or ".json")
                .Select(File.ReadAllText));
        Check(File.Exists(projectPath) && File.Exists(Path.Combine(consumer, "Assets", "white.webp")),
            "dotnet new creates source, manifest, and real WebP asset");
        Check(projectText.Contains($"MyGameEngine.GameSdk\" Version=\"{version}", StringComparison.Ordinal) &&
              projectText.Contains($"MyGameEngine.ContentPipeline\"", StringComparison.Ordinal) &&
              !projectText.Contains("ProjectReference", StringComparison.Ordinal),
            "Generated project uses version-aligned PackageReferences only");
        Check(!allText.Contains(repositoryRoot, StringComparison.OrdinalIgnoreCase),
            "Generated project contains no repository absolute path");
        string toolManifest = File.ReadAllText(
            Path.Combine(consumer, ".config", "dotnet-tools.json"));
        Check(toolManifest.Contains($"\"version\": \"{version}\"", StringComparison.Ordinal) &&
              toolManifest.Contains("\"gameengine\"", StringComparison.Ordinal),
            "Generated project pins the local gameengine tool version");

        RunDotNet(consumer, cliHome,
            "restore", projectPath,
            "--configfile", nugetConfig,
            "--packages", packages,
            "--no-cache");
        ProcessResult build = RunDotNet(consumer, cliHome,
            "build", projectPath, "--no-restore");
        string generatedReferences = Path.Combine(
            consumer, "obj", "Debug", "net10.0", "GameEngine.Content.g.cs");
        string compiledAssets = Path.Combine(
            consumer, "bin", "Debug", "net10.0", "AssetsCompiled", "assets.json");
        Check(build.Output.Contains("Generated content references:", StringComparison.Ordinal) &&
              File.Exists(generatedReferences) &&
              File.Exists(compiledAssets),
            "External build compiles assets and generates strongly typed references");

        string toolCommand = Path.Combine(
            toolPath,
            OperatingSystem.IsWindows() ? "gameengine.exe" : "gameengine");
        ProcessResult doctor = RunProcess(
            toolCommand, consumer, cliHome,
            "doctor", projectPath);
        Check(doctor.Output.Contains("Summary: 0 error(s), 0 warning(s).", StringComparison.Ordinal),
            "Installed gameengine doctor validates the generated Debug project");
        ProcessResult openGlDoctor = RunProcess(
            toolCommand, consumer, cliHome,
            "doctor", projectPath, "--probe-opengl");
        Check(openGlDoctor.Output.Contains("[PASS] GE300", StringComparison.Ordinal),
            "Installed gameengine doctor creates a hidden OpenGL 3.3 context");
        ProcessResult failedDoctor = RunProcess(
            toolCommand, consumer, cliHome, expectSuccess: false,
            "doctor", Path.Combine(consumer, "missing.csproj"));
        Check(failedDoctor.ExitCode == 1 &&
              failedDoctor.Output.Contains("[FAIL] GE100", StringComparison.Ordinal),
            "Installed gameengine doctor returns exit code 1 for diagnostic errors");
        ProcessResult invalidDoctor = RunProcess(
            toolCommand, consumer, cliHome, expectSuccess: false,
            "doctor", projectPath, "--unknown");
        Check(invalidDoctor.ExitCode == 2 &&
              invalidDoctor.Output.Contains("Usage: gameengine doctor", StringComparison.Ordinal),
            "Installed gameengine doctor returns exit code 2 for invalid usage");

        RunDotNet(consumer, cliHome,
            "run", "--project", projectPath,
            "--no-build", "--no-restore", "--", "--smoke");
        Check(true, "Generated game completes the hidden three-frame smoke run");

        string publish = Path.Combine(workspace, "published game");
        RunDotNet(consumer, cliHome,
            "publish", projectPath,
            "--configuration", "Release",
            "--output", publish,
            "--configfile", nugetConfig,
            "--packages", packages);
        Check(File.Exists(Path.Combine(publish, "SampleGame.dll")) &&
              File.Exists(Path.Combine(publish, "AssetsCompiled", "assets.json")) &&
              !File.Exists(Path.Combine(publish, "GameEngine.Content.g.cs")),
            "External publish contains runtime and compiled content without generated source");
    }

    private static void VerifyGameSdkPackage(string repositoryRoot, string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        string[] entries = archive.Entries.Select(entry => entry.FullName).ToArray();
        Check(RuntimeAssemblies.All(assembly => entries.Contains(
                $"lib/net10.0/{assembly}", StringComparer.Ordinal)),
            "GameSdk package contains every supported runtime assembly");
        Check(!entries.Any(entry =>
                entry.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) ||
                entry.EndsWith("Engine.Distribution.GameSdk.dll", StringComparison.Ordinal)),
            "GameSdk package excludes symbols and its empty packaging assembly");

        ZipArchiveEntry nuspec = archive.Entries.Single(entry =>
            entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        string nuspecText = ReadText(nuspec);
        Check(nuspecText.Contains("Silk.NET.Windowing", StringComparison.Ordinal) &&
              nuspecText.Contains("SkiaSharp", StringComparison.Ordinal) &&
              !nuspecText.Contains("Engine.Core\"", StringComparison.Ordinal),
            "GameSdk exposes third-party dependencies without leaking source project packages");
        Check(!archive.Entries
                .Where(entry => entry.Length is > 0 and < 1_000_000)
                .Any(entry => ReadText(entry).Contains(
                    repositoryRoot, StringComparison.OrdinalIgnoreCase)),
            "GameSdk package metadata contains no repository absolute path");
    }

    private static void VerifyTemplatePackage(string packagePath, string version)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        string[] entries = archive.Entries.Select(entry => entry.FullName).ToArray();
        Check(entries.Contains(
                  "content/templates/mygameengine-game/.template.config/template.json",
                  StringComparer.Ordinal) &&
              entries.Contains(
                  "content/templates/mygameengine-game/Assets/white.webp",
                  StringComparer.Ordinal) &&
              entries.Contains(
                  "content/templates/mygameengine-game/.config/dotnet-tools.json",
                  StringComparer.Ordinal) &&
              entries.Contains("README.md", StringComparer.Ordinal),
            "Template package contains metadata, local tools, source, documentation, and WebP asset");

        ZipArchiveEntry project = archive.Entries.Single(entry =>
            entry.FullName.EndsWith("/MyGameTemplate.csproj", StringComparison.Ordinal));
        string projectText = ReadText(project);
        int versionOccurrences = projectText.Split(version, StringSplitOptions.None).Length - 1;
        Check(versionOccurrences == 2,
            "Template package references the exact shared engine version twice");

        ZipArchiveEntry toolManifest = archive.Entries.Single(entry =>
            entry.FullName.EndsWith("/.config/dotnet-tools.json", StringComparison.Ordinal));
        Check(ReadText(toolManifest).Contains($"\"version\": \"{version}\"", StringComparison.Ordinal),
            "Template package pins the exact shared CLI version");
    }

    private static void VerifyCliPackage(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        string[] entries = archive.Entries.Select(entry => entry.FullName).ToArray();
        Check(entries.Contains(
                  "tools/net10.0/any/DotnetToolSettings.xml", StringComparer.Ordinal) &&
              entries.Contains(
                  "tools/net10.0/any/GameEngineCli.dll", StringComparer.Ordinal) &&
              entries.Contains("README.md", StringComparer.Ordinal),
            "CLI package contains the gameengine command and documentation");
        Check(!entries.Any(entry => entry.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)),
            "CLI package excludes symbols");
    }

    private static void CopyRestoredDependencyPackages(string repositoryRoot, string feed)
    {
        string assetsPath = Path.Combine(
            repositoryRoot,
            "src", "Engine.Distribution.GameSdk", "obj", "project.assets.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        string packageRoot = document.RootElement
            .GetProperty("packageFolders")
            .EnumerateObject()
            .First().Name;

        foreach (JsonProperty library in document.RootElement.GetProperty("libraries").EnumerateObject())
        {
            if (library.Value.GetProperty("type").GetString() != "package") continue;
            string[] identity = library.Name.Split('/');
            string id = identity[0].ToLowerInvariant();
            string version = identity[1].ToLowerInvariant();
            string source = Path.Combine(packageRoot, id, version, $"{id}.{version}.nupkg");
            if (!File.Exists(source))
                throw new FileNotFoundException(
                    $"Restored package archive is unavailable for isolated testing: {library.Name}",
                    source);
            File.Copy(source, Path.Combine(feed, Path.GetFileName(source)), overwrite: true);
        }
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        string extension = Path.GetExtension(entry.FullName);
        if (extension is not (".props" or ".targets" or ".nuspec" or ".json" or ".xml" or ".csproj" or ".md"))
            return string.Empty;
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static void WriteNuGetConfig(string path, string feed)
    {
        string escapedFeed = SecurityElement.Escape(feed) ?? feed;
        File.WriteAllText(path, $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="{escapedFeed}" />
              </packageSources>
            </configuration>
            """);
    }

    private static ProcessResult RunRepositoryDotNet(
        string workingDirectory,
        params string[] arguments) =>
        RunProcess("dotnet", workingDirectory, cliHome: null, arguments);

    private static ProcessResult RunDotNet(
        string workingDirectory,
        string cliHome,
        params string[] arguments) =>
        RunProcess("dotnet", workingDirectory, cliHome, arguments);

    private static ProcessResult RunProcess(
        string fileName,
        string workingDirectory,
        string? cliHome,
        params string[] arguments)
        => RunProcess(fileName, workingDirectory, cliHome, expectSuccess: true, arguments);

    private static ProcessResult RunProcess(
        string fileName,
        string workingDirectory,
        string? cliHome,
        bool expectSuccess,
        params string[] arguments)
    {
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (cliHome is not null) start.Environment["DOTNET_CLI_HOME"] = cliHome;
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        foreach (string argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ??
            throw new InvalidOperationException($"Could not start '{fileName}'.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(outputTask, errorTask);
        string combined = outputTask.Result + Environment.NewLine + errorTask.Result;
        if (expectSuccess && process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Command failed ({process.ExitCode}): {fileName} {string.Join(' ', arguments)}" +
                Environment.NewLine + combined);
        return new ProcessResult(process.ExitCode, combined);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MyGameEngine.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate MyGameEngine.slnx.");
    }

    private static string ReadPackageVersion(string repositoryRoot)
    {
        var document = XDocument.Load(Path.Combine(repositoryRoot, "Directory.Build.props"));
        return document.Descendants("GameEnginePackageVersion").Single().Value;
    }

    private static void Check(bool condition, string name)
    {
        if (condition) Console.WriteLine($"  [PASS] {name}");
        else
        {
            _failures++;
            Console.WriteLine($"  [FAIL] {name}");
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}

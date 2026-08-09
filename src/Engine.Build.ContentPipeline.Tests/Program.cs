namespace GameEngine.Build.ContentPipeline.Tests;

using System.Diagnostics;
using System.IO.Compression;
using System.Security;
using System.Xml.Linq;

internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        Console.WriteLine("=== Content Pipeline Package Integration Tests ===\n");
        string repositoryRoot = FindRepositoryRoot();
        string version = ReadPackageVersion(repositoryRoot);
        string workspace = Directory.CreateTempSubdirectory("mygame-package-tests-").FullName;
        try
        {
            VerifyPackagesAndExternalConsumer(repositoryRoot, workspace, version);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All Content Pipeline package tests passed ==="
            : $"=== {_failures} Content Pipeline package test(s) FAILED ===");
        return _failures == 0 ? 0 : 1;
    }

    private static void VerifyPackagesAndExternalConsumer(
        string repositoryRoot,
        string workspace,
        string version)
    {
        string feed = Directory.CreateDirectory(Path.Combine(workspace, "local feed")).FullName;
        string toolPath = Directory.CreateDirectory(Path.Combine(workspace, "installed tool")).FullName;
        string nugetConfig = Path.Combine(workspace, "NuGet.Config");
        WriteNuGetConfig(nugetConfig, feed);

        RunDotNet(repositoryRoot,
            "pack",
            "src/Engine.Tools.AssetCompiler/Engine.Tools.AssetCompiler.csproj",
            "--configuration", "Release",
            "--output", feed);
        RunDotNet(repositoryRoot,
            "pack",
            "src/Engine.Build.ContentPipeline/Engine.Build.ContentPipeline.csproj",
            "--configuration", "Release",
            "--output", feed);

        string toolPackage = Path.Combine(feed, $"MyGameEngine.AssetCompiler.{version}.nupkg");
        string buildPackage = Path.Combine(feed, $"MyGameEngine.ContentPipeline.{version}.nupkg");
        Check(File.Exists(toolPackage) && File.Exists(buildPackage),
            "Tool and buildTransitive packages are produced at the shared version");
        VerifyPackageLayout(repositoryRoot, toolPackage, buildPackage);

        RunDotNet(workspace,
            "tool", "install", "MyGameEngine.AssetCompiler",
            "--tool-path", toolPath,
            "--version", version,
            "--configfile", nugetConfig,
            "--no-cache");
        string toolCommand = Path.Combine(
            toolPath,
            OperatingSystem.IsWindows() ? "gameengine-assets.exe" : "gameengine-assets");
        Check(File.Exists(toolCommand), "The package installs the gameengine-assets command");

        string sourceRoot = Path.Combine(workspace, "source assets with spaces");
        CreateAssetPackage(repositoryRoot, sourceRoot);
        string toolOutput = Path.Combine(workspace, "tool output with spaces");
        RunProcess(toolCommand, workspace,
            "--rebuild", sourceRoot, "assets.json", toolOutput);
        Check(File.Exists(Path.Combine(toolOutput, "assets.json")) &&
              File.Exists(Path.Combine(toolOutput, "white.webp")),
            "Installed Tool compiles a real image package from paths containing spaces");

        ProcessResult checkCurrent = RunProcess(toolCommand, workspace,
            "--check", sourceRoot, "assets.json", toolOutput);
        Check(checkCurrent.ExitCode == 0 && checkCurrent.Output.Contains(
                "Build status: UpToDate", StringComparison.Ordinal),
            "Tool check mode reports a current output without rewriting it");

        string consumer = Path.Combine(workspace, "consumer project with spaces");
        Directory.CreateDirectory(consumer);
        CopyDirectory(sourceRoot, Path.Combine(consumer, "Assets"));
        CreateShaderAssets(consumer);
        File.WriteAllText(Path.Combine(consumer, "Program.cs"), ConsumerProgram);
        File.WriteAllText(Path.Combine(consumer, "EngineReferenceStubs.cs"), EngineReferenceStubs);
        File.WriteAllText(
            Path.Combine(consumer, "Consumer.csproj"),
            ConsumerProject.Replace("$PACKAGE_VERSION$", version, StringComparison.Ordinal));

        string consumerPackages = Path.Combine(workspace, "consumer packages");
        RunDotNet(consumer,
            "restore", "Consumer.csproj", "--configfile", nugetConfig, "--no-cache",
            "--packages", consumerPackages);
        ProcessResult firstBuild = RunDotNet(consumer,
            "build", "Consumer.csproj", "--no-restore");
        string debugAssets = Path.Combine(consumer, "bin", "Debug", "net10.0", "AssetsCompiled");
        Check(firstBuild.Output.Contains("Build status: Built", StringComparison.Ordinal) &&
              firstBuild.Output.Contains("Generated content references:", StringComparison.Ordinal) &&
              firstBuild.Output.Contains("Generated Shader references:", StringComparison.Ordinal) &&
              File.Exists(Path.Combine(debugAssets, "assets.json")) &&
              File.Exists(Path.Combine(
                  consumer, "obj", "Debug", "net10.0", "GameEngine.Content.g.cs")) &&
              File.Exists(Path.Combine(
                  consumer, "obj", "Debug", "net10.0", "GameEngine.Shaders.g.cs")),
            "PackageReference compiles runtime content and strongly typed Shader references");

        ProcessResult cachedBuild = RunDotNet(consumer,
            "build", "Consumer.csproj", "--no-restore");
        Check(cachedBuild.Output.Contains("Build status: UpToDate", StringComparison.Ordinal) &&
              cachedBuild.Output.Contains("Reference status: UpToDate", StringComparison.Ordinal) &&
              cachedBuild.Output.Contains("Shader reference status: UpToDate", StringComparison.Ordinal),
            "A second consumer build reuses content and both generated-reference outputs");

        RunDotNet(consumer,
            "build", "Consumer.csproj", "--configuration", "Release", "--no-restore");
        string releaseAssets = Path.Combine(consumer, "bin", "Release", "net10.0", "AssetsCompiled");
        Check(File.Exists(Path.Combine(releaseAssets, "assets.json")),
            "Release uses an isolated content output and receives runtime files");

        string publish = Path.Combine(workspace, "published consumer");
        RunDotNet(consumer,
            "publish", "Consumer.csproj", "--configuration", "Release",
            "--no-restore", "--output", publish);
        Check(File.Exists(Path.Combine(publish, "AssetsCompiled", "assets.json")) &&
              File.Exists(Path.Combine(publish, "AssetsCompiled", ".mygame-assets.json")) &&
              !File.Exists(Path.Combine(publish, "GameEngine.Content.g.cs")) &&
              !File.Exists(Path.Combine(publish, "GameEngine.Shaders.g.cs")),
            "Publish includes runtime assets and revision metadata but excludes generated sources");

        ProcessResult invalidMode = RunDotNet(
            consumer,
            expectSuccess: false,
            "build", "Consumer.csproj", "--no-restore",
            "-p:GameEngineContentBuildMode=invalid");
        Check(invalidMode.ExitCode != 0 && invalidMode.Output.Contains(
                "GameEngineContentBuildMode must be", StringComparison.Ordinal),
            "Invalid MSBuild content mode fails before invoking the compiler");

        ProcessResult invalidShaderGeneration = RunDotNet(
            consumer,
            expectSuccess: false,
            "build", "Consumer.csproj", "--no-restore",
            "-p:GameEngineShaderGenerateReferences=invalid");
        Check(invalidShaderGeneration.ExitCode != 0 && invalidShaderGeneration.Output.Contains(
                "GameEngineShaderGenerateReferences must be", StringComparison.Ordinal),
            "Invalid Shader reference generation switch fails before invoking the generator");
    }

    private static void VerifyPackageLayout(
        string repositoryRoot,
        string toolPackage,
        string buildPackage)
    {
        using var toolArchive = ZipFile.OpenRead(toolPackage);
        string[] toolEntries = toolArchive.Entries.Select(entry => entry.FullName).ToArray();
        Check(toolEntries.Contains("tools/net10.0/any/DotnetToolSettings.xml", StringComparer.Ordinal) &&
              toolEntries.Contains("tools/net10.0/any/GameEngineAssetCompiler.dll", StringComparer.Ordinal) &&
              toolEntries.Contains("tools/net10.0/any/ShaderAssets.dll", StringComparer.Ordinal) &&
              toolEntries.Contains("README.md", StringComparer.Ordinal),
            "Tool package contains command metadata, compiler, and Shader assets support");
        Check(!toolEntries.Any(entry => entry.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)),
            "Tool package excludes compiler and dependency symbols");

        using var buildArchive = ZipFile.OpenRead(buildPackage);
        string[] buildEntries = buildArchive.Entries.Select(entry => entry.FullName).ToArray();
        Check(buildEntries.Contains(
                  "buildTransitive/MyGameEngine.ContentPipeline.props", StringComparer.Ordinal) &&
              buildEntries.Contains(
                  "buildTransitive/MyGameEngine.ContentPipeline.targets", StringComparer.Ordinal) &&
              buildEntries.Contains(
                  "tools/net10.0/any/GameEngineAssetCompiler.dll", StringComparer.Ordinal) &&
              buildEntries.Contains(
                  "tools/net10.0/any/ShaderAssets.dll", StringComparer.Ordinal) &&
              buildEntries.Contains("README.md", StringComparer.Ordinal),
            "ContentPipeline package contains convention-named imports and a private compiler payload");
        Check(!buildEntries.Any(entry =>
                  entry.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) ||
                  entry.Contains("/publish/", StringComparison.OrdinalIgnoreCase) ||
                  entry.EndsWith("GameEngineAssetCompiler.exe", StringComparison.OrdinalIgnoreCase)),
            "Build package excludes symbols, nested publish output, and platform apphosts");

        string normalizedRoot = repositoryRoot.Replace('\\', '/');
        bool leaksRepositoryPath = buildArchive.Entries
            .Where(entry => entry.Length is > 0 and < 1_000_000)
            .Any(entry => ReadText(entry).Replace('\\', '/').Contains(
                normalizedRoot, StringComparison.OrdinalIgnoreCase));
        Check(!leaksRepositoryPath, "Package metadata and MSBuild imports contain no repository absolute path");
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        string extension = Path.GetExtension(entry.FullName);
        if (extension is not (".props" or ".targets" or ".nuspec" or ".json" or ".xml"))
            return string.Empty;
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static void CreateAssetPackage(string repositoryRoot, string target)
    {
        Directory.CreateDirectory(target);
        File.Copy(
            Path.Combine(repositoryRoot, "src", "MyGame.Runner", "Assets", "white.webp"),
            Path.Combine(target, "white.webp"));
        File.WriteAllText(Path.Combine(target, "assets.json"), AssetManifest);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
    }

    private static void CreateShaderAssets(string projectRoot)
    {
        string root = Directory.CreateDirectory(Path.Combine(projectRoot, "Shaders")).FullName;
        File.WriteAllText(Path.Combine(root, "sprite.vert.glsl"), "void main() {}");
        File.WriteAllText(Path.Combine(root, "sprite.frag.glsl"), "void main() {}");
        File.WriteAllText(Path.Combine(root, "shaders.json"), ShaderManifest);
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

    private static ProcessResult RunDotNet(string workingDirectory, params string[] arguments) =>
        RunProcess("dotnet", workingDirectory, arguments);

    private static ProcessResult RunDotNet(
        string workingDirectory,
        bool expectSuccess,
        params string[] arguments) =>
        RunProcess("dotnet", workingDirectory, expectSuccess, arguments);

    private static ProcessResult RunProcess(
        string fileName,
        string workingDirectory,
        params string[] arguments) =>
        RunProcess(fileName, workingDirectory, expectSuccess: true, arguments);

    private static ProcessResult RunProcess(
        string fileName,
        string workingDirectory,
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
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ??
            throw new InvalidOperationException($"Could not start '{fileName}'.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(outputTask, errorTask);
        string output = outputTask.Result;
        string error = errorTask.Result;
        string combined = output + Environment.NewLine + error;
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

    private const string ConsumerProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <RootNamespace>Consumer</RootNamespace>
            <GameEngineContentPackagesRoot>$(MSBuildProjectDirectory)\Assets</GameEngineContentPackagesRoot>
            <GameEngineContentManifest>assets.json</GameEngineContentManifest>
            <GameEngineShaderManifest>$(MSBuildProjectDirectory)\Shaders\shaders.json</GameEngineShaderManifest>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="MyGameEngine.ContentPipeline"
                              Version="$PACKAGE_VERSION$"
                              PrivateAssets="all" />
          </ItemGroup>
        </Project>
        """;

    private const string ConsumerProgram = """
        using Consumer.Content;

        System.Console.WriteLine(GameAssets.Packages.Root.Id);
        System.Console.WriteLine(GameAssets.Textures.PackageWhite.Name);
        System.Console.WriteLine(GameAssets.Sprites.PackageWhite.Name);
        System.Console.WriteLine(GameShaders.ManifestPath);
        System.Console.WriteLine(GameShaders.Shaders.PackageSprite.Name);
        System.Console.WriteLine(GameShaders.Materials.PackageMaterial.Name);
        System.Console.WriteLine(GameShaders.Parameters.PackageMaterial.Gain.Name);
        """;

    private const string EngineReferenceStubs = """
        namespace GameEngine.Core.Domain.ValueObjects
        {
            public readonly record struct TextureRef(string Name);
            public readonly record struct SpriteRef(string Name);
        }

        namespace GameEngine.Features.ContentAssets.Domain
        {
            public readonly record struct ContentPackageRef(string Id, string Manifest);
        }

        namespace GameEngine.Core.Domain.Graphics
        {
            public readonly record struct ShaderRef(string Name);
            public readonly record struct MaterialRef(string Name);
            public readonly record struct MaterialParameterRef<T>(MaterialRef Material, string Name);
        }
        """;

    private const string AssetManifest = """
        {
          "schemaVersion": 1,
          "id": "package.integration.assets",
          "dependencies": [],
          "textures": [
            { "name": "package.white", "path": "white.webp", "sampling": "pixelArt" }
          ],
          "sprites": [
            {
              "name": "package.white",
              "layout": "single",
              "texture": "package.white",
              "origin": { "x": 0, "y": 0 }
            }
          ]
        }
        """;

    private const string ShaderManifest = """
        {
          "schemaVersion": 1,
          "shaders": [
            {
              "name": "package.sprite",
              "vertex": "sprite.vert.glsl",
              "fragment": "sprite.frag.glsl"
            }
          ],
          "materials": [
            {
              "name": "package.material",
              "shader": "package.sprite",
              "uniforms": [
                { "name": "uGain", "type": "float", "default": 1.0 }
              ]
            }
          ]
        }
        """;
}

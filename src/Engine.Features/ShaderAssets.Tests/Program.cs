namespace ShaderAssets.Tests;

using System.Numerics;
using System.Text;
using GameEngine.Features.ShaderAssets.Infrastructure;

internal static class Program
{
    private static int Main()
    {
        Console.WriteLine("=== Shader Assets Smoke Test ===\n");
        ParseCompleteManifest();
        RejectInvalidContracts();
        ValidateSafeFiles();
        Console.WriteLine("\n=== All Shader Assets smoke tests passed ===");
        return 0;
    }

    private static void ParseCompleteManifest()
    {
        var manifest = Parse(ValidManifest);
        var material = manifest.Materials.Single();
        Check(manifest.SchemaVersion == 1 && manifest.Shaders.Single().Name == "game.sprite",
            "Versioned Shader program definitions parse");
        Check(material.Shader == "game.sprite" && material.Uniforms.Count == 4,
            "Material references and typed uniforms parse");
        Check(material.Uniforms[0].DefaultValue.FloatValue == 1.25f &&
              material.Uniforms[1].DefaultValue.IntValue == 2 &&
              material.Uniforms[2].DefaultValue.Vector2Value == new Vector2(1, -1) &&
              material.Uniforms[3].DefaultValue.Vector4Value == new Vector4(.2f, .4f, .6f, .8f),
            "All supported default value shapes remain strongly typed");
    }

    private static void RejectInvalidContracts()
    {
        CheckThrows("schemaVersion", ValidManifest.Replace("\"schemaVersion\":1", "\"schemaVersion\":2"),
            "Unknown schema versions are rejected");
        CheckThrows("unknown shader", ValidManifest.Replace("\"shader\":\"game.sprite\"", "\"shader\":\"missing\""),
            "Unknown Material Shader references are rejected");
        CheckThrows("unsupported type", ValidManifest.Replace("\"type\":\"float\"", "\"type\":\"matrix4\""),
            "Unknown uniform types are rejected");
        CheckThrows("does not match", ValidManifest.Replace("\"default\":1.25", "\"default\":{}"),
            "Default values must match the declared type");
        CheckThrows("owned by the engine", ValidManifest.Replace("\"uGain\"", "\"uProjection\""),
            "Engine-owned uniforms remain reserved");
        CheckThrows("could not be mapped", ValidManifest.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"extra\":true"),
            "Unknown JSON fields are rejected");
        CheckThrows("could not be mapped", ValidManifest.Replace("\"schemaVersion\":1", "\"SchemaVersion\":1"),
            "Shader manifest property names remain case-sensitive");
        CheckThrows("Invalid shader asset manifest JSON", string.Empty,
            "Empty Shader manifests retain the InvalidDataException contract");
    }

    private static void ValidateSafeFiles()
    {
        string root = Directory.CreateTempSubdirectory("shader-assets-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "sprite.vert"), "vertex");
            File.WriteAllText(Path.Combine(root, "sprite.frag"), "fragment");
            string manifestPath = Path.Combine(root, "shaders.json");
            File.WriteAllText(manifestPath, ValidManifest);
            var loaded = ShaderAssetManifestLoader.Load(manifestPath);
            Check(loaded.RootDirectory == root && loaded.Manifest.Materials.Count == 1,
                "Loader validates existing sources inside the manifest directory");

            File.WriteAllText(manifestPath, ValidManifest.Replace("sprite.vert", "../outside.vert"));
            CheckThrows<InvalidDataException>(
                () => ShaderAssetManifestLoader.Load(manifestPath),
                "Shader source paths cannot escape the manifest directory");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static GameEngine.Features.ShaderAssets.Domain.ShaderAssetManifest Parse(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return ShaderAssetManifestParser.Parse(stream);
    }

    private static void CheckThrows(string expected, string json, string message)
    {
        try
        {
            Parse(json);
        }
        catch (InvalidDataException exception)
        {
            Check(exception.Message.Contains(expected, StringComparison.OrdinalIgnoreCase), message);
            return;
        }
        throw new InvalidOperationException($"[FAIL] {message}");
    }

    private static void CheckThrows<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T)
        {
            Check(true, message);
            return;
        }
        throw new InvalidOperationException($"[FAIL] {message}");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"[FAIL] {message}");
        Console.WriteLine($"  [PASS] {message}");
    }

    private const string ValidManifest = """
        {
          "schemaVersion":1,
          "shaders":[
            {"name":"game.sprite","vertex":"sprite.vert","fragment":"sprite.frag"}
          ],
          "materials":[
            {
              "name":"game.sprite.material",
              "shader":"game.sprite",
              "uniforms":[
                {"name":"uGain","type":"float","default":1.25},
                {"name":"uMode","type":"int","default":2},
                {"name":"uDirection","type":"vector2","default":{"x":1,"y":-1}},
                {"name":"uTint","type":"vector4","default":{"x":0.2,"y":0.4,"z":0.6,"w":0.8}}
              ]
            }
          ]
        }
        """;
}

namespace GameEngine.Features.ShaderAssets.Infrastructure;

using System.Text.Json;
using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ShaderAssetManifestParser.ManifestDto))]
[JsonSerializable(typeof(ShaderAssetManifestParser.Vector2Dto))]
[JsonSerializable(typeof(ShaderAssetManifestParser.Vector4Dto))]
internal sealed partial class ShaderAssetManifestJsonContext : JsonSerializerContext;

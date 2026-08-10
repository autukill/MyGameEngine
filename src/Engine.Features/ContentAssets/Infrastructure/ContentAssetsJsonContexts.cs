namespace GameEngine.Features.ContentAssets.Infrastructure;

using System.Text.Json;
using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    PropertyNameCaseInsensitive = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(AssetPackageManifestParser.ManifestDto))]
internal sealed partial class AssetPackageManifestJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CompiledContentRevisionReader.Metadata))]
internal sealed partial class CompiledContentRevisionJsonContext : JsonSerializerContext;

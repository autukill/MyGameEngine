namespace GameEngine.Features.TextureAssets.Infrastructure;

using System.Text.Json;
using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    PropertyNameCaseInsensitive = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(TextureManifestLoader.ManifestDocument))]
internal sealed partial class TextureManifestJsonContext : JsonSerializerContext;

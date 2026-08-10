namespace MyGame.Runner;

using System.Text.Json.Serialization;
using GameEngine.Hosting;

[JsonSerializable(typeof(RuntimePerformanceSnapshot))]
[JsonSerializable(typeof(ContentHotReloadDiagnostic))]
[JsonSerializable(typeof(ShaderHotReloadDiagnostic))]
internal sealed partial class RunnerJsonContext : JsonSerializerContext;

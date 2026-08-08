namespace ToneMapping.Tests;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.ToneMapping.Application;
using GameEngine.Features.ToneMapping.Domain;

internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        Console.WriteLine("=== Tone Mapping Feature Smoke Test ===\n");
        TestSettings();
        TestDescriptorAndEvents();
        TestSharedPolicy();
        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All Tone Mapping smoke tests passed ==="
            : $"=== {_failures} Tone Mapping test(s) FAILED ===");
        return _failures == 0 ? 0 : 1;
    }

    private static void TestSettings()
    {
        Console.WriteLine("1. Settings validation");
        Check(ToneMappingSettings.Default ==
              new ToneMappingSettings(ToneMappingOperator.Aces, 0f, 2.2f),
            "Defaults are explicit and stable");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new ToneMappingSettings((ToneMappingOperator)99, 0f, 2.2f),
            "Unknown operator is rejected");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new ToneMappingSettings(ToneMappingOperator.Aces, -10.1f, 2.2f),
            "Exposure below -10 EV is rejected");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new ToneMappingSettings(ToneMappingOperator.Aces, 10.1f, 2.2f),
            "Exposure above 10 EV is rejected");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new ToneMappingSettings(ToneMappingOperator.Aces, float.NaN, 2.2f),
            "Non-finite exposure is rejected");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new ToneMappingSettings(ToneMappingOperator.Aces, 0f, 0f),
            "Non-positive gamma is rejected");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new ToneMappingSettings(ToneMappingOperator.Aces, 0f, 4.1f),
            "Gamma above four is rejected");
    }

    private static void TestDescriptorAndEvents()
    {
        Console.WriteLine("2. Descriptor and GameInstance events");
        var bloom = new RenderSurfaceKey("bloom", "main", "glow");
        var descriptor = new ToneMappingEffectDescriptor(
            ToneMappingEffectDescriptor.DefaultKey,
            ToneMappingSettings.Default,
            bloomSource: bloom);
        Check(descriptor.Source == RenderSurfaceKey.SceneColor &&
              descriptor.BloomSource == bloom &&
              ToneMappingEffectDescriptor.ColorOutput(descriptor.Key) ==
              RenderSurfaceKey.FromEffect(descriptor.Key, "color"),
            "Descriptor carries HDR scene, optional Bloom, and LDR output keys");
        CheckThrows<ArgumentException>(
            () => new ToneMappingEffectDescriptor(
                new RenderEffectKey("other", "main"), ToneMappingSettings.Default),
            "Descriptor rejects a foreign effect kind");

        var events = new List<IDomainEvent>();
        var instance = new GameInstance("tone-owner", Vector2D.Zero, LayerDepth.Instances);
        instance.RequestToneMapping(ToneMappingSettings.Default, events.Add, bloomSource: bloom);
        Check(events.Single() is RenderEffectRequestedEvent
            {
                Descriptor: ToneMappingEffectDescriptor { BloomSource: var source }
            } && source == bloom,
            "Active instance requests typed Tone Mapping");
        events.Clear();
        instance.ReleaseToneMapping(events.Add);
        Check(events.Single() is RenderEffectReleasedEvent released &&
              released.EffectKey == ToneMappingEffectDescriptor.DefaultKey,
            "Release uses the matching key");
        instance.SetActive(false, _ => { });
        events.Clear();
        instance.RequestToneMapping(ToneMappingSettings.Default, events.Add);
        Check(events.Count == 0, "Inactive instance request is a no-op");
    }

    private static void TestSharedPolicy()
    {
        Console.WriteLine("3. Shared owner policy");
        var key = ToneMappingEffectDescriptor.DefaultKey;
        var owners = new Dictionary<InstanceId, IRenderEffectDescriptor>
        {
            [InstanceId.New()] = new ToneMappingEffectDescriptor(key, ToneMappingSettings.Default),
            [InstanceId.New()] = new ToneMappingEffectDescriptor(key, ToneMappingSettings.Default)
        };
        Check(ToneMappingEffectPolicy.ValidateAndGetConfiguration(key, owners).Settings ==
              ToneMappingSettings.Default,
            "Owners with identical configuration share one effect");
        owners[owners.Keys.Last()] = new ToneMappingEffectDescriptor(
            key,
            new ToneMappingSettings(ToneMappingOperator.Reinhard, 0f, 2.2f));
        CheckThrows<InvalidOperationException>(
            () => ToneMappingEffectPolicy.ValidateAndGetConfiguration(key, owners),
            "Conflicting settings are rejected");
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

    private static void CheckThrows<T>(Action action, string name) where T : Exception
    {
        try { action(); Check(false, name); }
        catch (T) { Check(true, name); }
    }
}

namespace Bloom.Tests;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.Bloom.Application;
using GameEngine.Features.Bloom.Domain;
using GameEngine.Features.Bloom.Infrastructure;
using GameEngine.Features.RenderPipeline.Domain;

internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        Console.WriteLine("=== Bloom Feature Smoke Test ===\n");
        TestSettingsAndDescriptor();
        TestInstanceEventsAndSharing();
        TestTargetPlanning();
        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All Bloom smoke tests passed ==="
            : $"=== {_failures} Bloom test(s) FAILED ===");
        return _failures == 0 ? 0 : 1;
    }

    private static void TestSettingsAndDescriptor()
    {
        Console.WriteLine("1. Settings and descriptor validation");
        var defaults = BloomSettings.Default;
        Check(defaults == new BloomSettings(0.35f, 1.25f, 1f, 2, BloomResolution.Half),
            "Default settings are explicit and stable");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new BloomSettings(-0.1f, 1f, 1f, 1, BloomResolution.Full),
            "Threshold below zero is rejected");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new BloomSettings(1.1f, 1f, 1f, 1, BloomResolution.Full),
            "Threshold above one is rejected");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new BloomSettings(float.NaN, 1f, 1f, 1, BloomResolution.Full),
            "Non-finite threshold is rejected");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new BloomSettings(0.5f, 0f, 1f, 1, BloomResolution.Full),
            "Non-positive intensity is rejected");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new BloomSettings(0.5f, 8.1f, 1f, 1, BloomResolution.Full),
            "Intensity above eight is rejected");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new BloomSettings(0.5f, 1f, 0f, 1, BloomResolution.Full),
            "Non-positive blur radius is rejected");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new BloomSettings(0.5f, 1f, 4.1f, 1, BloomResolution.Full),
            "Blur radius above four is rejected");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new BloomSettings(0.5f, 1f, 1f, 0, BloomResolution.Full),
            "Zero iterations are rejected");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new BloomSettings(0.5f, 1f, 1f, 9, BloomResolution.Full),
            "More than eight iterations are rejected");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new BloomSettings(0.5f, 1f, 1f, 1, (BloomResolution)3),
            "Unknown resolution is rejected");
        Check(new BloomSettings(0f, 8f, 4f, 8, BloomResolution.Quarter).Threshold == 0f &&
              new BloomSettings(1f, float.Epsilon, float.Epsilon, 1, BloomResolution.Full).Threshold == 1f,
            "Inclusive and exclusive setting boundaries are accepted");

        var descriptor = new BloomEffectDescriptor(BloomEffectDescriptor.DefaultKey, defaults);
        Check(descriptor.Key.Kind == BloomEffectDescriptor.EffectKind &&
              descriptor.Settings == defaults &&
              descriptor.Source == RenderSurfaceKey.SceneColor &&
              descriptor.ColorFormat == RenderTargetColorFormat.Rgba8 &&
              descriptor.Encoding == RenderSurfaceEncoding.Display &&
              descriptor.Presentation == BloomPresentation.Additive &&
              BloomEffectDescriptor.GlowOutput(descriptor.Key) ==
              RenderSurfaceKey.FromEffect(descriptor.Key, "glow"),
            "Descriptor carries a typed key, SceneColor input, and logical glow output");
        CheckThrows<ArgumentException>(
            () => new BloomEffectDescriptor(new RenderEffectKey("other", "main"), defaults),
            "Descriptor rejects a non-Bloom key");
        var hdrDescriptor = new BloomEffectDescriptor(
            BloomEffectDescriptor.DefaultKey,
            defaults,
            colorFormat: RenderTargetColorFormat.Rgba16Float,
            encoding: RenderSurfaceEncoding.Linear,
            presentation: BloomPresentation.SurfaceOnly);
        Check(hdrDescriptor.ColorFormat == RenderTargetColorFormat.Rgba16Float &&
              hdrDescriptor.Presentation == BloomPresentation.SurfaceOnly,
            "Bloom can publish an HDR Linear surface without direct composition");
        CheckThrows<ArgumentException>(
            () => new BloomEffectDescriptor(
                BloomEffectDescriptor.DefaultKey,
                defaults,
                colorFormat: RenderTargetColorFormat.Rgba16Float),
            "HDR Bloom rejects Display encoding");
    }

    private static void TestInstanceEventsAndSharing()
    {
        Console.WriteLine("2. Owner events and shared policy");
        var events = new List<IDomainEvent>();
        var instance = new GameInstance("BloomOwner", Vector2D.Zero, LayerDepth.Instances);
        instance.RequestBloom(BloomSettings.Default, events.Add);
        Check(events.Single() is RenderEffectRequestedEvent
            {
                Descriptor: BloomEffectDescriptor bloom
            } && bloom.Settings == BloomSettings.Default,
            "Active instance raises a typed Bloom request");

        var upstreamKey = new RenderEffectKey(BloomEffectDescriptor.EffectKind, "upstream");
        var upstreamGlow = BloomEffectDescriptor.GlowOutput(upstreamKey);
        events.Clear();
        instance.RequestBloom(
            BloomSettings.Default,
            events.Add,
            new RenderEffectKey(BloomEffectDescriptor.EffectKind, "downstream"),
            upstreamGlow);
        Check(events.Single() is RenderEffectRequestedEvent
            {
                Descriptor: BloomEffectDescriptor { Source: var source }
            } && source == upstreamGlow,
            "Bloom request can consume another effect's logical output");

        events.Clear();
        instance.ReleaseBloom(events.Add);
        Check(events.Single() is RenderEffectReleasedEvent released &&
              released.EffectKey == BloomEffectDescriptor.DefaultKey,
            "Explicit release carries the same effect key");

        instance.SetActive(false, _ => { });
        events.Clear();
        instance.RequestBloom(BloomSettings.Default, events.Add);
        Check(events.Count == 0, "Inactive instance does not request Bloom");

        var key = BloomEffectDescriptor.DefaultKey;
        var owners = new Dictionary<InstanceId, IRenderEffectDescriptor>
        {
            [InstanceId.New()] = new BloomEffectDescriptor(key, BloomSettings.Default),
            [InstanceId.New()] = new BloomEffectDescriptor(key, BloomSettings.Default)
        };
        Check(BloomEffectPolicy.ValidateAndGetSettings(key, owners) == BloomSettings.Default,
            "Multiple owners share identical settings");
        owners[owners.Keys.Last()] = new BloomEffectDescriptor(
            key, new BloomSettings(0.4f, 1.25f, 1f, 2, BloomResolution.Half));
        CheckThrows<InvalidOperationException>(
            () => BloomEffectPolicy.ValidateAndGetSettings(key, owners),
            "Owners with conflicting settings are rejected");

        owners = new Dictionary<InstanceId, IRenderEffectDescriptor>
        {
            [InstanceId.New()] = new BloomEffectDescriptor(key, BloomSettings.Default),
            [InstanceId.New()] = new BloomEffectDescriptor(
                key, BloomSettings.Default, upstreamGlow)
        };
        CheckThrows<InvalidOperationException>(
            () => BloomEffectPolicy.ValidateAndGetConfiguration(key, owners),
            "Owners with conflicting logical sources are rejected");
    }

    private static void TestTargetPlanning()
    {
        Console.WriteLine("3. Target dimensions and draw planning");
        Check(BloomPass.CalculateTargetSize(801, 601, BloomResolution.Full) == (801, 601),
            "Full resolution keeps viewport dimensions");
        Check(BloomPass.CalculateTargetSize(801, 601, BloomResolution.Half) == (401, 301),
            "Half resolution rounds odd dimensions upward");
        Check(BloomPass.CalculateTargetSize(3, 2, BloomResolution.Quarter) == (1, 1),
            "Quarter resolution is clamped to at least one pixel");
        Check(BloomPass.CalculateInternalDrawCount(1) == 3 &&
              BloomPass.CalculateInternalDrawCount(8) == 17,
            "Each iteration adds one horizontal and one vertical draw");
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

namespace GameEngine.Features.RenderPipeline.Infrastructure;

using GameEngine.Features.RenderPipeline.Domain;

public interface IRenderSurfaceResolver
{
    bool TryResolve(RenderSurfaceKey key, out RenderTarget2D? surface);
    RenderTarget2D Resolve(RenderSurfaceKey key);
    RenderSurfaceSpec Describe(RenderSurfaceKey key);
}

public readonly record struct RenderEffectOutput(
    RenderSurfaceKey Key,
    RenderTarget2D Surface);

internal readonly record struct RenderSurfaceRegistration(
    RenderTarget2D Surface,
    RenderSurfaceSpec Spec);

internal sealed class RenderSurfaceRegistry : IRenderSurfaceResolver
{
    private readonly Dictionary<RenderSurfaceKey, RenderSurfaceRegistration> _surfaces;

    public RenderSurfaceRegistry(
        IReadOnlyDictionary<RenderSurfaceKey, RenderSurfaceRegistration>? roots = null)
    {
        _surfaces = roots is null
            ? new Dictionary<RenderSurfaceKey, RenderSurfaceRegistration>()
            : new Dictionary<RenderSurfaceKey, RenderSurfaceRegistration>(roots);
    }

    public bool TryResolve(RenderSurfaceKey key, out RenderTarget2D? surface)
    {
        if (_surfaces.TryGetValue(key, out var registration))
        {
            surface = registration.Surface;
            return true;
        }
        surface = null;
        return false;
    }

    public RenderTarget2D Resolve(RenderSurfaceKey key) =>
        _surfaces.TryGetValue(key, out var registration)
            ? registration.Surface
            : throw new InvalidOperationException($"Render surface '{key}' is not available.");

    public RenderSurfaceSpec Describe(RenderSurfaceKey key) =>
        _surfaces.TryGetValue(key, out var registration)
            ? registration.Spec
            : throw new InvalidOperationException($"Render surface '{key}' is not available.");

    public void Add(RenderSurfaceSpec spec, RenderTarget2D surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (surface.ColorFormat != spec.ColorFormat)
            throw new InvalidOperationException(
                $"Render surface '{spec.Key}' uses {surface.ColorFormat}, expected {spec.ColorFormat}.");
        if (!_surfaces.TryAdd(spec.Key, new RenderSurfaceRegistration(surface, spec)))
            throw new InvalidOperationException($"Render surface '{spec.Key}' is already registered.");
    }
}

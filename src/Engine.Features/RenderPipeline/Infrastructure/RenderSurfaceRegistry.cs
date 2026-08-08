namespace GameEngine.Features.RenderPipeline.Infrastructure;

using GameEngine.Features.RenderPipeline.Domain;

public interface IRenderSurfaceResolver
{
    bool TryResolve(RenderSurfaceKey key, out RenderTarget2D? surface);
    RenderTarget2D Resolve(RenderSurfaceKey key);
}

public readonly record struct RenderEffectOutput(
    RenderSurfaceKey Key,
    RenderTarget2D Surface);

internal sealed class RenderSurfaceRegistry : IRenderSurfaceResolver
{
    private readonly Dictionary<RenderSurfaceKey, RenderTarget2D> _surfaces;

    public RenderSurfaceRegistry(
        IReadOnlyDictionary<RenderSurfaceKey, RenderTarget2D>? roots = null)
    {
        _surfaces = roots is null
            ? new Dictionary<RenderSurfaceKey, RenderTarget2D>()
            : new Dictionary<RenderSurfaceKey, RenderTarget2D>(roots);
    }

    public bool TryResolve(RenderSurfaceKey key, out RenderTarget2D? surface) =>
        _surfaces.TryGetValue(key, out surface);

    public RenderTarget2D Resolve(RenderSurfaceKey key) =>
        _surfaces.TryGetValue(key, out var surface)
            ? surface
            : throw new InvalidOperationException($"Render surface '{key}' is not available.");

    public void Add(RenderSurfaceKey key, RenderTarget2D surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (!_surfaces.TryAdd(key, surface))
            throw new InvalidOperationException($"Render surface '{key}' is already registered.");
    }
}

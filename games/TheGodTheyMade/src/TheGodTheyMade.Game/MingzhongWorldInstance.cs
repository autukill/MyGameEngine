namespace TheGodTheyMade.Game;

using System.Numerics;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.Tilemaps.Domain;
using GameEngine.Features.Tilemaps.Infrastructure;
using TheGodTheyMade.Game.Content;
using TheGodTheyMade.Simulation.Navigation;

internal sealed class MingzhongWorldInstance : GameInstance
{
    private const float WorldWidth = MingzhongNavigation.Width * MingzhongNavigation.TileSize;
    private const float WorldHeight = MingzhongNavigation.Height * MingzhongNavigation.TileSize;
    private static readonly SpriteRef Solid = GameAssets.Sprites.DebugSolid;
    private static readonly Region[] Regions =
    [
        new(21, 2, 16, 5, new Vector4(0.10f, 0.25f, 0.42f, 1f)),
        new(3, 11, 12, 12, new Vector4(0.28f, 0.22f, 0.16f, 1f)),
        new(17, 12, 8, 8, new Vector4(0.34f, 0.31f, 0.23f, 1f)),
        new(26, 17, 19, 12, new Vector4(0.36f, 0.31f, 0.08f, 1f)),
        new(3, 25, 12, 6, new Vector4(0.22f, 0.24f, 0.25f, 1f)),
        new(38, 4, 8, 9, new Vector4(0.20f, 0.22f, 0.27f, 1f)),
        new(38, 13, 8, 6, new Vector4(0.12f, 0.28f, 0.18f, 1f))
    ];

    private readonly TileMap _map;
    private readonly TileMapRenderer _renderer;
    private readonly Camera2D _camera;
    private readonly Func<Vector2D, Vector2D?> _screenToWorld;
    private readonly NavigationGrid _navigation;
    private readonly Action _close;
    private readonly bool _smoke;
    private bool _previousPrimaryDown;
    private bool _captured;
    private Vector2D _captureStart;
    private Vector2D? _pointerWorld;
    private int _steps;

    public bool GateBlocked { get; private set; } = true;

    public MingzhongWorldInstance(
        TileMap map,
        TileMapRenderer renderer,
        Camera2D camera,
        Func<Vector2D, Vector2D?> screenToWorld,
        NavigationGrid navigation,
        Action close,
        bool smoke)
        : base("MingzhongWorld", Vector2D.Zero, LayerDepth.Background)
    {
        _map = map;
        _renderer = renderer;
        _camera = camera;
        _screenToWorld = screenToWorld;
        _navigation = navigation;
        _close = close;
        _smoke = smoke;
        ViewCulling = InstanceViewCullingMode.AlwaysVisible;
    }

    public override void OnStep(double deltaTime)
    {
        Vector2D movement = InputAxis2D(GameInputs.CameraMove);
        _camera.Position += new Vector2(movement.X, movement.Y) * (360f * (float)deltaTime);
        float scroll = Controls.MouseScrollDelta;
        if (scroll != 0f)
            _camera.Zoom = Math.Clamp(_camera.Zoom * MathF.Pow(1.12f, scroll), 0.75f, 2f);
        ConstrainCamera();

        _pointerWorld = _screenToWorld(Controls.MousePosition);
        bool primaryDown = Controls.IsMouseButtonDown(MouseButton.Left);
        bool pressed = primaryDown && !_previousPrimaryDown;
        bool released = !primaryDown && _previousPrimaryDown;
        if (pressed && _pointerWorld is { } pressedWorld)
        {
            _captured = true;
            _captureStart = pressedWorld;
        }
        if (released && _captured)
        {
            if (_pointerWorld is { } releasedWorld)
                HandleRelease(releasedWorld);
            _captured = false;
        }
        _previousPrimaryDown = primaryDown;

        if (KeyPressed(InputKey.Escape)) _close();
        if (_smoke && ++_steps >= 4) _close();
    }

    public override void OnDraw(ISpriteBatch batch)
    {
        if (!_camera.TryGetVisibleWorldBounds(out Bounds2D visible)) return;
        _renderer.Draw(batch, _map, visible, color: new Vector4(0.16f, 0.30f, 0.17f, 1f));
        foreach (Region region in Regions)
            DrawCellRect(batch, region.X, region.Y, region.Width, region.Height, region.Color);

        DrawCellRect(batch, 6, 5, 4, 5, new Vector4(0.42f, 0.28f, 0.12f, 1f));
        DrawCellRect(batch, 29, 8, 5, 5, new Vector4(0.11f, 0.31f, 0.38f, 1f));
        DrawCellRect(batch, 18, 22, 1, 1, new Vector4(0.18f, 0.46f, 0.62f, 1f));
        DrawCellRect(batch, 31, 11, 1, 1, GateBlocked
            ? new Vector4(0.48f, 0.18f, 0.10f, 1f)
            : new Vector4(0.13f, 0.42f, 0.21f, 0.7f));

        if (_pointerWorld is not { } pointer) return;
        GridCell hover = WorldToCell(pointer);
        if (_navigation.Contains(hover))
            DrawCellRect(batch, hover.X, hover.Y, 1, 1, new Vector4(1f, 0.86f, 0.24f, 0.35f));
        if (_captured)
            DrawCircle(batch, _captureStart, 72f, new Vector4(0.98f, 0.78f, 0.22f, 0.18f));
    }

    protected override void OnWriteGameplayState(ref GameplayStateWriter writer)
    {
        writer.Write("mingzhong.gateBlocked", GateBlocked);
        writer.Write("mingzhong.navigationRevision", _navigation.Revision);
        writer.Write("mingzhong.pointerCaptured", _captured);
    }

    private void HandleRelease(Vector2D world)
    {
        GridCell cell = WorldToCell(world);
        if (!GateBlocked || cell != MingzhongNavigation.GateBoulder) return;
        GateBlocked = false;
        _navigation.SetBlocked(MingzhongNavigation.GateBoulder, false);
    }

    private void ConstrainCamera()
    {
        float visibleWidth = _camera.ViewportSize.X / _camera.Zoom;
        float visibleHeight = _camera.ViewportSize.Y / _camera.Zoom;
        float x = visibleWidth >= WorldWidth
            ? (WorldWidth - visibleWidth) * 0.5f
            : Math.Clamp(_camera.Position.X, -64f, WorldWidth - visibleWidth + 64f);
        float y = visibleHeight >= WorldHeight
            ? (WorldHeight - visibleHeight) * 0.5f
            : Math.Clamp(_camera.Position.Y, -64f, WorldHeight - visibleHeight + 64f);
        _camera.Position = new Vector2(x, y);
    }

    private static GridCell WorldToCell(Vector2D world) => new(
        (int)MathF.Floor(world.X / MingzhongNavigation.TileSize),
        (int)MathF.Floor(world.Y / MingzhongNavigation.TileSize));

    private static void DrawCellRect(
        ISpriteBatch batch,
        int x,
        int y,
        int width,
        int height,
        Vector4 color) => batch.DrawSpriteStretched(
            Solid,
            0,
            new Vector2(x * MingzhongNavigation.TileSize, y * MingzhongNavigation.TileSize),
            new Vector2(width * MingzhongNavigation.TileSize, height * MingzhongNavigation.TileSize),
            color);

    private static void DrawCircle(ISpriteBatch batch, Vector2D center, float radius, Vector4 color)
    {
        const int strips = 12;
        float height = radius * 2f / strips;
        for (int i = 0; i < strips; i++)
        {
            float y = -radius + (i + 0.5f) * height;
            float halfWidth = MathF.Sqrt(MathF.Max(0f, radius * radius - y * y));
            batch.DrawSpriteStretched(
                Solid,
                0,
                new Vector2(center.X - halfWidth, center.Y + y - height * 0.5f),
                new Vector2(halfWidth * 2f, height + 1f),
                color);
        }
    }

    private readonly record struct Region(
        int X,
        int Y,
        int Width,
        int Height,
        Vector4 Color);
}

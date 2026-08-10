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
using TheGodTheyMade.Simulation.Beliefs;
using TheGodTheyMade.Simulation.World;
using TheGodTheyMade.Simulation.Village;
using TheGodTheyMade.Simulation.Familiar;
using TheGodTheyMade.Simulation.Scenario;

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
    private readonly MingzhongWorldSimulation _world;
    private readonly BeliefSimulation _beliefs;
    private readonly FamiliarLearning _familiar;
    private readonly MingzhongIslandScenario _scenario;
    private readonly bool _smoke;
    private readonly bool _scriptedBelief;
    private readonly bool _suppressRawInput;
    private bool _previousPrimaryDown;
    private bool _captured;
    private Vector2D _captureStart;
    private Vector2D? _pointerWorld;
    private int _steps;

    public bool GateBlocked => _world.Gate == GateState.Blocked;

    public MingzhongWorldInstance(
        TileMap map,
        TileMapRenderer renderer,
        Camera2D camera,
        Func<Vector2D, Vector2D?> screenToWorld,
        NavigationGrid navigation,
        MingzhongWorldSimulation world,
        BeliefSimulation beliefs,
        FamiliarLearning familiar,
        MingzhongIslandScenario scenario,
        Action close,
        bool smoke,
        bool scriptedBelief,
        bool replayPlayback)
        : base("MingzhongWorld", Vector2D.Zero, LayerDepth.Background)
    {
        _map = map;
        _renderer = renderer;
        _camera = camera;
        _screenToWorld = screenToWorld;
        _navigation = navigation;
        _world = world;
        _beliefs = beliefs;
        _familiar = familiar;
        _scenario = scenario;
        _close = close;
        _smoke = smoke;
        _scriptedBelief = scriptedBelief;
        _suppressRawInput = scriptedBelief || replayPlayback;
        ViewCulling = InstanceViewCullingMode.AlwaysVisible;
    }

    public override void OnStep(double deltaTime)
    {
        Vector2D movement = InputAxis2D(GameInputs.CameraMove);
        _camera.Position += new Vector2(movement.X, movement.Y) * (360f * (float)deltaTime);
        float scroll = _suppressRawInput ? 0f : Controls.MouseScrollDelta;
        if (scroll != 0f)
            _camera.Zoom = Math.Clamp(_camera.Zoom * MathF.Pow(1.12f, scroll), 0.75f, 2f);
        ConstrainCamera();

        if (!_suppressRawInput)
        {
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
        }

        if (_scriptedBelief && _world.Tick == 10)
            _world.TryApply(MingzhongCommand.RingBell(_world.Tick));
        if (_scriptedBelief && _world.Tick == 20)
            _world.TryApply(MingzhongCommand.Rain(_world.Tick, MingzhongVillage.Bell));

        _world.AdvanceTick();
        _beliefs.Update(_world);
        _scenario.Advance(_world, _beliefs, _familiar);
        if (_world.Gate == GateState.Open)
            _navigation.SetBlocked(MingzhongNavigation.GateBoulder, false);

        if (!_suppressRawInput && KeyPressed(InputKey.Escape)) _close();
        _steps++;
        if (_smoke && !_scriptedBelief && _steps >= 4) _close();
        if (_scriptedBelief && _world.Tick >= MingzhongVillage.TicksPerSecond) _close();
    }

    public override void OnDraw(ISpriteBatch batch)
    {
        if (!_camera.TryGetVisibleWorldBounds(out Bounds2D visible)) return;
        _renderer.Draw(batch, _map, visible, color: new Vector4(0.16f, 0.30f, 0.17f, 1f));
        foreach (Region region in Regions)
            DrawCellRect(batch, region.X, region.Y, region.Width, region.Height, region.Color);

        DrawCellRect(batch, 6, 5, 4, 5, new Vector4(0.42f, 0.28f, 0.12f, 1f));
        DrawCellRect(batch, 29, 8, 5, 5, new Vector4(0.11f, 0.31f, 0.38f, 1f));
        float reservoirFill = _world.ReservoirUnits / 100f;
        DrawCellRect(batch, 21, 2, 16, 5, new Vector4(0.08f, 0.25f + reservoirFill * 0.24f, 0.48f + reservoirFill * 0.28f, 0.92f));
        DrawField(batch, 0, 26, 17, 6, 12);
        DrawField(batch, 1, 33, 17, 5, 12);
        DrawField(batch, 2, 39, 17, 6, 12);
        DrawCellRect(batch, 18, 22, 1, 1, new Vector4(0.18f, 0.46f, 0.62f, 1f));
        DrawCellRect(batch, 31, 11, 1, 1, GateBlocked
            ? new Vector4(0.48f, 0.18f, 0.10f, 1f)
            : new Vector4(0.13f, 0.42f, 0.21f, 0.7f));
        if (_world.Canal != CanalState.Dry)
            DrawCellRect(batch, 30, 6, 2, 12, _world.Canal == CanalState.Flowing
                ? new Vector4(0.12f, 0.48f, 0.72f, 0.78f)
                : new Vector4(0.18f, 0.38f, 0.58f, 0.52f));
        if (_world.IsRaining)
        {
            Vector2D rainCenter = new(
                (_world.RainCenter.X + 0.5f) * MingzhongNavigation.TileSize,
                (_world.RainCenter.Y + 0.5f) * MingzhongNavigation.TileSize);
            DrawCircle(batch, rainCenter,
                _world.RainRadiusCells * MingzhongNavigation.TileSize,
                new Vector4(0.22f, 0.56f, 0.94f, 0.18f));
        }

        Vector4 ruinColor = _scenario.Ruin switch
        {
            RuinPuzzleState.Decoded => new Vector4(0.25f, 0.88f, 0.72f, 1f),
            RuinPuzzleState.Revealed => new Vector4(0.34f, 0.68f, 0.84f, 1f),
            _ => new Vector4(0.32f, 0.34f, 0.38f, 1f)
        };
        DrawCellRect(batch, MingzhongIslandScenario.RuinTablet.X,
            MingzhongIslandScenario.RuinTablet.Y, 1, 1, ruinColor);
        if (_scenario.Funeral is FuneralOutcome.Active or FuneralOutcome.LanternsPreserved)
            DrawCircle(batch,
                new Vector2D((MingzhongVillage.Cemetery.X + 0.5f) * MingzhongNavigation.TileSize,
                    (MingzhongVillage.Cemetery.Y + 0.5f) * MingzhongNavigation.TileSize),
                52f,
                _scenario.Funeral == FuneralOutcome.Active
                    ? new Vector4(1f, 0.58f, 0.18f, 0.30f)
                    : new Vector4(0.95f, 0.78f, 0.32f, 0.18f));
        if (_scenario.Mural is not null)
        {
            DrawCellRect(batch, 18, 14, 2, 4, new Vector4(0.42f, 0.62f, 0.92f, 1f));
            DrawCellRect(batch, 20, 14, 2, 4, _scenario.GateResolution == GateResolution.Familiar
                ? new Vector4(0.82f, 0.76f, 0.52f, 1f)
                : new Vector4(0.52f, 0.76f, 0.58f, 1f));
            DrawCellRect(batch, 22, 14, 2, 4, _scenario.Funeral == FuneralOutcome.LanternsLostToRain
                ? new Vector4(0.28f, 0.38f, 0.56f, 1f)
                : new Vector4(0.92f, 0.56f, 0.28f, 1f));
        }

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
        writer.Write("mingzhong.tick", _world.Tick);
        writer.Write("mingzhong.godIntent", _world.GodIntent);
        writer.Write("mingzhong.reservoir", _world.ReservoirUnits);
        writer.Write("mingzhong.canal", (int)_world.Canal);
        for (int i = 0; i < _world.FieldCount; i++)
        {
            FieldSnapshot field = _world.GetField(i);
            writer.Write($"mingzhong.field.{i}.moisture", field.Moisture);
            writer.Write($"mingzhong.field.{i}.withered", field.Withered);
        }
        writer.Write("mingzhong.worldHash", _world.ComputeStateHash());
        writer.Write("mingzhong.beliefHash", _beliefs.ComputeStateHash());
        writer.Write("mingzhong.hasDoctrine", _beliefs.Doctrine is not null);
        writer.Write("mingzhong.scenarioHash", _scenario.ComputeStateHash());
        writer.Write("mingzhong.chapterPhase", (int)_scenario.Phase);
        writer.Write("mingzhong.chapterComplete", _scenario.IsComplete);
    }

    private void HandleRelease(Vector2D world)
    {
        GridCell cell = WorldToCell(world);
        if (GateBlocked && cell == MingzhongNavigation.GateBoulder)
        {
            if (_world.TryApply(MingzhongCommand.OpenGate(_world.Tick)))
                _navigation.SetBlocked(MingzhongNavigation.GateBoulder, false);
            return;
        }

        _world.TryApply(MingzhongCommand.Rain(_world.Tick, cell));
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

    private void DrawField(ISpriteBatch batch, int index, int x, int y, int width, int height)
    {
        FieldSnapshot field = _world.GetField(index);
        float wet = field.Moisture / 100f;
        Vector4 color = field.Withered
            ? new Vector4(0.34f, 0.20f, 0.07f, 0.92f)
            : new Vector4(0.36f - wet * 0.14f, 0.31f + wet * 0.30f, 0.08f, 0.92f);
        DrawCellRect(batch, x, y, width, height, color);
    }

    private readonly record struct Region(
        int X,
        int Y,
        int Width,
        int Height,
        Vector4 Color);
}

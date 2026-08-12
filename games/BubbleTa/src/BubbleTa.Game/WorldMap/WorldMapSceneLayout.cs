namespace BubbleTa.Game.WorldMap;

using System.Numerics;
using GameEngine.Core.Domain.Gameplay;

internal static class WorldMapSceneLayout
{
    public static Bounds2D RoomBounds { get; } = new(0f, 0f, 1_048f, 16_100f);
    public static Vector2 InitialCameraPosition { get; } = new(164f, 14_820f);
}

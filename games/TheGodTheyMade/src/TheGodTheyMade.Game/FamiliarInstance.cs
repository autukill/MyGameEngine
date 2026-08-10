namespace TheGodTheyMade.Game;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.ValueObjects;
using TheGodTheyMade.Game.Content;
using TheGodTheyMade.Simulation.Navigation;
using TheGodTheyMade.Simulation.Village;

internal sealed class FamiliarInstance : GameInstance
{
    public FamiliarInstance()
        : base(
            "Familiar.Ape",
            new Vector2D(
                (MingzhongVillage.FamiliarRest.X + 0.5f) * MingzhongNavigation.TileSize,
                (MingzhongVillage.FamiliarRest.Y + 0.5f) * MingzhongNavigation.TileSize),
            new LayerDepth(5000 - MingzhongVillage.FamiliarRest.Y * MingzhongNavigation.TileSize))
    {
        Sprite = GameAssets.Sprites.DebugFamiliar;
        Color = new System.Numerics.Vector4(0.88f, 0.88f, 0.82f, 1f);
        Collider = CollisionShape2D.Circle(13f, new Vector2D(0f, -12f));
    }
}

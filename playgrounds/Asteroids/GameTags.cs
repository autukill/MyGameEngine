namespace AsteroidsPlayground;

using GameEngine.Core.Domain.Gameplay;

public static class GameTags
{
    public static readonly GameplayTag Player = new("actor.player");
    public static readonly GameplayTag Enemy = new("actor.enemy");
    public static readonly GameplayTag Damageable = new("combat.damageable");
    public static readonly GameplayTag PlayerProjectile = new("projectile.player");
}

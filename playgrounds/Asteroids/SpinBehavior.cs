namespace AsteroidsPlayground;

using GameEngine.Core.Domain.Gameplay;

public sealed class SpinBehavior : GameplayBehavior<Asteroid>
{
    public float RadiansPerSecond { get; set; }

    public SpinBehavior(float radiansPerSecond)
    {
        if (!float.IsFinite(radiansPerSecond))
            throw new ArgumentOutOfRangeException(nameof(radiansPerSecond));
        RadiansPerSecond = radiansPerSecond;
    }

    public override void OnStep(double deltaTime) =>
        Owner.RotateBy(RadiansPerSecond * (float)deltaTime);
}

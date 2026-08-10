namespace AirplaneShooter;

using System.Numerics;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.TransformHierarchy.Domain;
using GameEngine.Features.TransformHierarchy.Gameplay;
using GameEngine.Features.Audio;

public sealed class PlayerPlane : GameInstance
{
    public static readonly PrefabRef<PlayerBullet> BulletPrefab = new("player.bullet");
    private const float MoveSpeed = 360f;
    private const double FireInterval = 0.12d;
    private const float HalfSize = 40f;
    private static readonly TransformPrefab<PlaneRig> s_transformPrefab = new(
        "player-plane.rig",
        static builder =>
        {
            TransformNodeRef<WeaponPivot> weapon = builder.Attachment<WeaponPivot>(
                "weapon",
                LocalTransform2D.Identity);
            TransformNodeRef<Muzzle> muzzle = builder.Attachment<Muzzle, WeaponPivot>(
                "muzzle",
                new LocalTransform2D(new Vector2(0f, -HalfSize), 0f, Vector2.One),
                weapon);
            return new PlaneRig(weapon, muzzle);
        });

    private readonly float _worldWidth;
    private readonly float _worldHeight;
    private readonly GameplayCooldown _fireCooldown = new(FireInterval);
    private readonly PlaneRig _rig;
    private readonly AudioRuntime _audio;
    private readonly AudioClipRef _laser;

    public PlayerPlane(
        SpriteRef sprite,
        SceneTransformRuntime transforms,
        AudioRuntime audio,
        AudioClipRef laser,
        Vector2D position,
        float worldWidth,
        float worldHeight)
    {
        Sprite = sprite;
        Position = position;
        Collider = CollisionShape2D.Box(52f, 64f);
        _worldWidth = worldWidth;
        _worldHeight = worldHeight;
        _audio = audio;
        _laser = laser;
        _rig = s_transformPrefab.Instantiate(this, transforms).Parts;
    }

    public override void OnStep(double deltaTime)
    {
        float dt = (float)deltaTime;
        Vector2D direction = InputAxis2D(GameInputs.Move);

        if (direction != Vector2D.Zero)
            MoveBy(direction.Normalize() * (MoveSpeed * dt));

        Position = new Vector2D(
            Math.Clamp(Position.X, HalfSize, _worldWidth - HalfSize),
            Math.Clamp(Position.Y, HalfSize, _worldHeight - HalfSize));

        _fireCooldown.Update(deltaTime);
        if (ActionDown(GameInputs.Fire) && _fireCooldown.TryUse())
        {
            Vector2 muzzle = _rig.Muzzle.WorldPosition;
            Spawn(BulletPrefab, new Vector2D(muzzle.X, muzzle.Y));
            _audio.Play(_laser, AudioPlayOptions.Sfx);
        }
    }

    protected override void OnWriteGameplayState(ref GameplayStateWriter writer)
    {
        writer.Write("plane.fireCooldown", _fireCooldown);
        writer.Write("plane.worldWidth", _worldWidth);
        writer.Write("plane.worldHeight", _worldHeight);
    }

    private sealed class WeaponPivot { }
    private sealed class Muzzle { }
    private readonly record struct PlaneRig(
        TransformNodeRef<WeaponPivot> Weapon,
        TransformNodeRef<Muzzle> Muzzle);
}

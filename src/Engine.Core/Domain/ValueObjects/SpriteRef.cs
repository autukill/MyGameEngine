namespace GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// Sprite 逻辑引用（对应 GMS sprite_index）。
/// 只携带稳定名称，不包含 GPU 纹理句柄；由 ISpriteResolver 在绘制时解析。
/// </summary>
public readonly record struct SpriteRef(string Name)
{
    public static SpriteRef Empty => default;

    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);

    public override string ToString() => Name ?? string.Empty;
}

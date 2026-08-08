namespace GameEngine.Core.Domain.Graphics;

using GameEngine.Core.Domain.ValueObjects;

/// <summary>Sprite 逻辑引用到元数据与 GPU 帧数据的解析边界。</summary>
public interface ISpriteResolver
{
    bool TryGetMetadata(SpriteRef sprite, out SpriteMetadata metadata);

    /// <summary>解析指定帧；实现负责将超出范围的帧索引循环到合法范围。</summary>
    bool TryResolve(SpriteRef sprite, int subImage, out ResolvedSpriteFrame frame);
}

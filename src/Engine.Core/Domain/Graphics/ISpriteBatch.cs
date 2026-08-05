namespace GameEngine.Core.Domain.Graphics;

using System.Numerics;

/// <summary>
/// SpriteBatch 的领域抽象接口。
/// 让 GameInstance.OnDraw(ISpriteBatch) 不直接依赖 Infrastructure 层的 SpriteBatch 实现，
/// 保持 Domain 层的纯净性（Domain 不引用 Silk.NET / OpenGL）。
///
/// GMS 对照：GMS 中 Draw 事件直接调用 draw_* 函数；
/// 这里通过 ISpriteBatch 抽象同样让 OnDraw 可以"画"，但解除了对具体 GL 实现的耦合。
/// </summary>
public interface ISpriteBatch
{
    void Begin();
    void End();
    void Draw(uint textureHandle, Vector2 position, Vector2 size, Vector4 color, Vector4 uvBounds);
    void Flush();
}

namespace GameEngine.Features.SceneSystem.Domain;

using System.Numerics;

/// <summary>
/// 渲染命令（值对象）：描述"画什么"——纹理、位置、大小、颜色、UV、深度。
/// 不包含 GL 调用，只携带数据。由 Layer (Infrastructure) 收集，
/// 由 SpriteBatch (Engine.Core/Infrastructure) 消费。
/// </summary>
public class RenderCommand
{
    public uint TextureHandle;
    public Vector2 Position;
    public Vector2 Size;
    public Vector4 Color;
    public Vector4 UvBounds;
    public int Depth;
}

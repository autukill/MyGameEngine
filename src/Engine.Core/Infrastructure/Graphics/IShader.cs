namespace GameEngine.Core.Infrastructure.Graphics;

using System.Numerics;

/// <summary>
/// 通用 Shader 接口：让 Pass 不绑定具体 Shader 类型。
/// SpriteShader / BlitShader 及各 Feature 专用 Shader 均实现此接口。
/// </summary>
public interface IShader : IDisposable
{
    uint Handle { get; }
    void Use();
    void SetProjection(Matrix4x4 matrix);
}

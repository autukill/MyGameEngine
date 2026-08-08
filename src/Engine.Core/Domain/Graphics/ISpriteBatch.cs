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

    /// <summary>
    /// 切换混合模式（GMS gpu_set_blendmode）。
    /// 状态未变化时零开销；变化时自动 Flush 并 Apply 到 GL。
    /// </summary>
    void SetBlendMode(BlendMode mode);

    /// <summary>
    /// 设置深度测试/写入状态。状态变化前自动 Flush，避免影响已入批顶点。
    /// </summary>
    void SetDepthState(bool depthTest, bool depthWrite);

    /// <summary>
    /// 切换 Shader（GMS shader_set）。null = 默认 shader。
    /// 状态未变化时零开销；变化时自动 Flush 并 UseProgram。
    /// </summary>
    void SetShader(ShaderRef? shader);
}

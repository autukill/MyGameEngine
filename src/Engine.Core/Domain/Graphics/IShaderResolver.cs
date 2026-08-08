namespace GameEngine.Core.Domain.Graphics;

/// <summary>
/// ShaderRef → GL program handle 解析器抽象。
/// 由 Infrastructure 的 ShaderLibrary 实现，注入 SpriteBatch / SceneRenderPass。
/// Domain 层只依赖此接口（名字 → handle），不依赖任何 GL 类型。
/// </summary>
public interface IShaderResolver
{
    /// <summary>
    /// 解析 ShaderRef 并返回 GL program handle。
    /// 未知或空名字返回 0（表示使用默认 shader）。
    /// </summary>
    uint Resolve(ShaderRef shader);
}

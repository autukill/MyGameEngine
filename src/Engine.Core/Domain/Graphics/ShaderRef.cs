namespace GameEngine.Core.Domain.Graphics;

/// <summary>
/// Shader 引用值对象（对应 GMS 的 shader_index）。
/// 只携带名字，由渲染层的 IShaderResolver（ShaderLibrary）解析为 GL program。
///
/// 为什么实例不直接持有 ShaderProgram：
///   ShaderProgram 属于 Infrastructure（持有 GL 对象），GameInstance 属于 Core Domain，
///   实例持有 GL 对象会破坏 VSA 依赖方向。这里用名字引用，渲染层负责解析与释放。
/// </summary>
public readonly record struct ShaderRef(string Name)
{
    public static ShaderRef Empty => default;

    /// <summary>是否有效引用（名字非空）</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Name);

    public override string ToString() => Name;
}

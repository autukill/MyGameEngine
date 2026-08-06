namespace GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// 场景图层配置值对象（Domain 层元数据）。
///
/// 描述一个 Scene Layer 的领域属性：名称、深度次序、是否可见。
/// 不包含渲染逻辑——渲染由 Infrastructure 层的 Layer 类消费此配置执行。
///
/// GMS 对照：Room 中的 Layer 定义（GMS 没有显式 Layer 概念，但 Depth 分组等效于 Layer）。
/// </summary>
public readonly record struct SceneLayerConfig(string Name, int DepthOrder, bool IsVisible)
{
    public static SceneLayerConfig Default(string name, int depthOrder) =>
        new(name, depthOrder, IsVisible: true);

    public override string ToString() =>
        $"Layer[{Name}] order={DepthOrder} visible={IsVisible}";
}

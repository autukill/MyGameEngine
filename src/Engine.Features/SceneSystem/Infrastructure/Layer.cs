namespace GameEngine.Features.SceneSystem.Infrastructure;

using System.Numerics;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.SceneSystem.Domain;

/// <summary>
/// 图层（Infrastructure）：收集 RenderCommand 并按 Depth 排序后提交到 SpriteBatch。
/// 可附加 Per-Layer 渲染状态覆盖（LayerRenderState）。
/// </summary>
public class Layer
{
    public string Name { get; }
    public int DepthOrder { get; set; }
    public bool IsVisible { get; set; } = true;
    public LayerRenderState? RenderStateOverride { get; set; }

    private readonly List<RenderCommand> _commandBuffer = new(1024);

    public Layer(string name, int depthOrder)
    {
        Name = name;
        DepthOrder = depthOrder;
    }

    public void Submit(RenderCommand command) => _commandBuffer.Add(command);

    public void Draw(SpriteBatch batch)
    {
        if (!IsVisible || _commandBuffer.Count == 0) return;

        _commandBuffer.Sort((a, b) => b.Depth.CompareTo(a.Depth));

        for (int i = 0; i < _commandBuffer.Count; i++)
        {
            var cmd = _commandBuffer[i];
            batch.Draw(cmd.TextureHandle, cmd.Position, cmd.Size, cmd.Color, cmd.UvBounds);
        }

        _commandBuffer.Clear();
    }
}

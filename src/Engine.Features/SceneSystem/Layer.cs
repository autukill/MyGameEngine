namespace GameEngine.Features.SceneSystem;

using System.Numerics;
using GameEngine.Core.Infrastructure.Graphics;

public class RenderCommand
{
    public uint TextureHandle;
    public Vector2 Position;
    public Vector2 Size;
    public Vector4 Color;
    public Vector4 UvBounds;
    public int Depth; // 深度 (Depth 越大越靠后绘制)
}

public class Layer
{
    public string Name { get; }
    public int DepthOrder { get; set; }
    public bool IsVisible { get; set; } = true;

    private readonly List<RenderCommand> _commandBuffer = new(1024);

    public Layer(string name, int depthOrder)
    {
        Name = name;
        DepthOrder = depthOrder;
    }

    public void Submit(RenderCommand command)
    {
        _commandBuffer.Add(command);
    }

    /// <summary>
    /// 渲染当前图层的所有 Command，按 Depth 排序并提交给 SpriteBatch
    /// </summary>
    public void Draw(SpriteBatch batch)
    {
        if (!IsVisible || _commandBuffer.Count == 0) return;

        // 按 Depth 从大到小排序 (实现从后往前绘制)
        _commandBuffer.Sort((a, b) => b.Depth.CompareTo(a.Depth));

        for (int i = 0; i < _commandBuffer.Count; i++)
        {
            var cmd = _commandBuffer[i];
            batch.Draw(cmd.TextureHandle, cmd.Position, cmd.Size, cmd.Color, cmd.UvBounds);
        }

        _commandBuffer.Clear(); // 绘制完毕，清空当前帧 Buffer (零垃圾回收)
    }
}

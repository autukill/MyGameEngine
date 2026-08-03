namespace GameEngine.Features.StencilMasking;

using Silk.NET.OpenGL;
using GameEngine.Core.Domain.Events;

// 切片包含的值对象：描述模板测试状态
public readonly record struct StencilState(uint RefValue, uint Mask)
{
    public static StencilState Default => new(1, 0xFF);
}

// 切片发起的领域事件：通知渲染管道插入 Stencil Pass
public record StencilMaskPassRequestedEvent(
    Action DrawMaskShape,
    Action DrawMaskedContent,
    StencilState State
) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

// 切片垂直处理器：内聚处理 OpenGL 底层逻辑 (彻底打破 GMS 黑盒)
public class StencilMaskHandler
{
    private readonly GL _gl;

    public StencilMaskHandler(GL gl) => _gl = gl;

    /// <summary>
    /// 处理 Stencil Pass 的具体绘制流程
    /// </summary>
    public void Handle(StencilMaskPassRequestedEvent evt)
    {
        _gl.Enable(EnableCap.StencilTest);
        _gl.Clear((uint)ClearBufferMask.StencilBufferBit);

        _gl.ColorMask(false, false, false, false);
        _gl.StencilFunc(StencilFunction.Always, (int)evt.State.RefValue, evt.State.Mask);
        _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
        _gl.StencilMask(evt.State.Mask);

        evt.DrawMaskShape();

        _gl.ColorMask(true, true, true, true);
        _gl.StencilFunc(StencilFunction.Equal, (int)evt.State.RefValue, evt.State.Mask);
        _gl.StencilMask(0x00);

        evt.DrawMaskedContent();

        _gl.Disable(EnableCap.StencilTest);
    }
}

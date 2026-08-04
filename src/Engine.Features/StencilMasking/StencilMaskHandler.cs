using GameEngine.Core.Infrastructure.Graphics;

namespace GameEngine.Features.StencilMasking;

using Silk.NET.OpenGL;

// 切片包含的值对象：描述模板测试状态
public readonly record struct StencilState( uint RefValue, uint Mask ) {
    public static StencilState Default => new(1, 0xFF);
}

// 切片发起的领域事件：通知渲染管道插入 Stencil Pass

// 切片垂直处理器：内聚处理 OpenGL 底层逻辑 (彻底打破 GMS 黑盒)
public class StencilMaskHandler {
    private readonly GL _gl;
    private readonly SpriteBatch _batch;

    public StencilMaskHandler( GL gl, SpriteBatch batch ) {
        _gl = gl;
        _batch = batch;
    }

    /// <summary>
    /// 执行带 Stencil 遮罩的批次渲染 Pass
    /// </summary>
    /// <param name="drawMaskShape">绘制遮罩形状的操作 (如聚光灯圆形贴图)</param>
    /// <param name="drawContent">绘制被遮罩覆盖内容的操作 (如背景、角色)</param>
    /// <param name="state">模板测试参数</param>
    public void ExecuteMaskedPass( Action drawMaskShape, Action drawContent, StencilMaskState state = default ) {
        // 关键步骤 0：清空之前存留在 SpriteBatch 里的任何常规精灵！
        _batch.Flush();

        // 步骤 1：开启 Stencil 测试，清空 Stencil 缓冲区
        _gl.Enable( EnableCap.StencilTest );
        _gl.Clear( (uint)ClearBufferMask.StencilBufferBit );

        // -------------------------------------------------------------
        // 步骤 2：绘制遮罩形状 (只写 Stencil Buffer，关闭 Color Buffer 写入)
        // -------------------------------------------------------------
        _gl.ColorMask( false, false, false, false ); // 禁止颜色输出到屏幕
        _gl.DepthMask( false ); // 禁止写入深度缓冲

        // 配置：总是通过测试，将符合条件的像素在 Stencil Buffer 中写入 StencilRef (如 1)
        _gl.StencilFunc( StencilFunction.Always, (int)state.StencilRef, state.MaskBits );
        _gl.StencilOp( StencilOp.Keep, StencilOp.Keep, StencilOp.Replace );
        _gl.StencilMask( state.MaskBits ); // 开启 Stencil 写入

        // 执行遮罩绘制并强制 Flush 进 GPU
        _batch.Begin();
        drawMaskShape();
        _batch.End(); // 此处 Flush 会把遮罩形状写入 Stencil Buffer

        // -------------------------------------------------------------
        // 步骤 3：绘制被遮罩的内容 (恢复 Color Buffer 写入，开启 Stencil 测试)
        // -------------------------------------------------------------
        _gl.ColorMask( true, true, true, true ); // 恢复颜色输出
        _gl.DepthMask( true );

        // 根据模式选择：如果是 ShowInside，只有 Stencil 值 Equal 1 的地方才绘制
        var func = state.Mode == StencilMaskMode.ShowInside
            ? StencilFunction.Equal
            : StencilFunction.Notequal;

        _gl.StencilFunc( func, (int)state.StencilRef, state.MaskBits );
        _gl.StencilMask( 0x00 ); // 禁用 Stencil 写入，保护遮罩形状不被改变

        // 执行目标内容绘制并强制 Flush 进 GPU
        _batch.Begin();
        drawContent();
        _batch.End(); // 此处 Flush 的精灵会受 Stencil 测试裁切

        // -------------------------------------------------------------
        // 步骤 4：恢复默认渲染状态
        // -------------------------------------------------------------
        _gl.Disable( EnableCap.StencilTest );
    }
}
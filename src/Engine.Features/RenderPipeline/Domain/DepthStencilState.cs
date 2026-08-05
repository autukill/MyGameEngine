namespace GameEngine.Features.RenderPipeline.Domain;

using Silk.NET.OpenGL;

/// <summary>
/// 深度/模板状态值对象。
/// 与 Phase 0.2 的 StencilMasking 切片共享同一套语义。
/// </summary>
public readonly record struct DepthStencilState(
    bool DepthTestEnable = false,
    bool DepthWriteEnable = false,
    bool StencilTestEnable = false,
    StencilFunction StencilFunc = StencilFunction.Always,
    int StencilRef = 1,
    uint StencilMask = 0xFF,
    StencilOp StencilFail = StencilOp.Keep,
    StencilOp StencilDepthFail = StencilOp.Keep,
    StencilOp StencilPass = StencilOp.Keep ) {
    public static DepthStencilState None => new();

    /// <summary>标准 Stencil 遮罩写入阶段</summary>
    public static DepthStencilState StencilWrite( int refValue = 1, uint mask = 0xFF ) => new(
        StencilTestEnable: true,
        StencilFunc: StencilFunction.Always,
        StencilRef: refValue,
        StencilMask: mask,
        StencilPass: StencilOp.Replace);

    /// <summary>标准 Stencil 遮罩测试阶段（只在遮罩内绘制）</summary>
    public static DepthStencilState StencilTest( int refValue = 1, uint mask = 0xFF ) => new(
        StencilTestEnable: true,
        StencilFunc: StencilFunction.Equal,
        StencilRef: refValue,
        StencilMask: mask);

    /// <summary>
    /// 反向 Stencil 遮罩测试（只在遮罩外绘制）。
    /// 用于战争迷雾挖孔、透视洞、黑洞吞噬等 ShowOutside 场景。
    /// 注意：Silk.NET 遵循 OpenGL C 命名，GL_NOTEQUAL 对应 StencilFunction.Notequal。
    /// </summary>
    public static DepthStencilState StencilTestNotEqual( int refValue = 1, uint mask = 0xFF ) => new(
        StencilTestEnable: true,
        StencilFunc: StencilFunction.Notequal,
        StencilRef: refValue,
        StencilMask: mask);

    public void Apply( GL gl ) {
        if ( DepthTestEnable ) gl.Enable( EnableCap.DepthTest );
        else gl.Disable( EnableCap.DepthTest );
        gl.DepthMask( DepthWriteEnable );

        if ( StencilTestEnable ) {
            gl.Enable( EnableCap.StencilTest );
            gl.StencilFunc( StencilFunc, StencilRef, StencilMask );
            gl.StencilOp( StencilFail, StencilDepthFail, StencilPass );
        }
        else {
            gl.Disable( EnableCap.StencilTest );
        }
    }
}
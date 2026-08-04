using System.Numerics;
using Silk.NET.OpenGL;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.SceneSystem;
using GameEngine.Features.Physics;
using GameEngine.Features.StencilMasking;

// 1. 实例化引擎窗口
var window = new EngineWindow( EngineWindowOptions.Default );

// 2. 声明全局服务与资源句柄（使用 null! 配合 OnLoad 初始化）
SpriteShader shader = null!;
SpriteBatch batch = null!;
SceneAggregate activeScene = null!;
SpatialHashGrid physicsGrid = null!;
StencilMaskHandler stencilHandler = null!;

Vector2 playerPos = new(100, 100);

// =========================================================================
// Hook 1: OnLoad (一次性初始化钩子)
// 触发时机：窗口与 OpenGL Context 创建完成后、主循环 Run() 启动前/首帧前执行一次
// 作用：加载 GPU 资源、实例化物理网格与场景聚合根，确保 OnStep 安全可访问
// =========================================================================
window.OnLoad += () => {
    var gl = window.Graphics.Gl;

    // 初始化 GPU 渲染基础设施
    shader = new SpriteShader( gl );
    batch = new SpriteBatch( gl );
    stencilHandler = new StencilMaskHandler( gl, batch );

    // 初始化场景聚合根与 Spatial Hash 物理网格
    activeScene = new SceneAggregate( window.Graphics.ViewportWidth, window.Graphics.ViewportHeight );
    physicsGrid = new SpatialHashGrid( cellSize: 64 );

    // 在物理网格中注册静态障碍物 (Wall)
    var wallBounds = new AABB( new Vector2( 200, 100 ), new Vector2( 264, 300 ) );
    physicsGrid.Insert( new ColliderEntity( 1, "Wall", wallBounds ) );
};

// =========================================================================
// Hook 2: OnStep (逻辑帧循环)
// 触发时机：每帧最先执行！由于 OnLoad 已提前准备好所有服务，此处绝对不会报空指针
// =========================================================================
window.OnStep += ( deltaTime ) => {
    // 1. 玩家移动输入预测
    Vector2 moveInput = new Vector2( 1, 0 ) * (float)(100 * deltaTime); // 向右移动

    // 2. 利用 Spatial Hash 进行 $O(1)$ 高性能碰撞查询 (对标 GMS place_meeting)
    var predictedBounds = new AABB( playerPos + moveInput, playerPos + moveInput + new Vector2( 32, 32 ) );

    if ( !physicsGrid.PlaceMeeting( predictedBounds, "Wall", out _ ) ) {
        // 无碰撞，更新玩家位置
        playerPos += moveInput;
    }

    // 3. 2D 相机跟随平滑插值 (Camera Follow)
    activeScene.MainCamera.Position = Vector2.Lerp( activeScene.MainCamera.Position, playerPos, 0.1f );
};

// =========================================================================
// Hook 3: OnDrawBegin (每帧渲染前置钩子)
// 职责：仅负责每帧提交前的 OpenGL Framebuffer 清屏与绘图状态复位
// =========================================================================
window.OnDrawBegin += () => {
    var gl = window.Graphics.Gl;

    // 设置背景清屏颜色
    gl.ClearColor( 0.1f, 0.1f, 0.12f, 1.0f );

    // 清空上一帧的颜色缓冲区、深度缓冲区与 Stencil 模板缓冲区
    gl.Clear( (uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit) );
};

// =========================================================================
// Hook 4: OnDraw (渲染帧提交)
// 职责：提交图层命令与触发批处理绘制
// =========================================================================
window.OnDraw += () => {
    // 1. 将当前帧的精灵提交给 SceneAggregate 的图层 (Instances Layer)
    var instanceLayer = activeScene.GetLayer( "Instances" );

    // 提交玩家绘制命令 (TextureHandle 1)
    instanceLayer.Submit( new RenderCommand {
        TextureHandle = 1,
        Position = playerPos,
        Size = new Vector2( 32, 32 ),
        Color = Vector4.One,
        Depth = 0
    } );

    // 2. 渲染整个场景 (自动应用 Camera 2D 正交变换矩阵与 Layer Depth 深度排序)
    activeScene.Render( shader, batch );
};

// 启动主循环
window.Run();
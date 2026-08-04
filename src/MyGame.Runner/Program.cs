using System.Numerics;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.SceneSystem;
using GameEngine.Features.Physics;
using GameEngine.Features.StencilMasking;
using Silk.NET.OpenGL;

// 1. 实例化引擎窗口
var window = new EngineWindow( EngineWindowOptions.Default );

// 2. 声明全局服务与资源句柄（使用 null! 配合 OnLoad 初始化）
SpriteShader shader = null!;
SpriteBatch batch = null!;
SceneAggregate activeScene = null!;
SpatialHashGrid physicsGrid = null!;

StencilMaskHandler stencilHandler = null!;

Vector2 playerPos = new(100, 100);

// 1. 在 OnLoad 钩子中集中做【一次性资源与物理网格构建】
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

// 2. 游戏逻辑更新 (OnStep 绝对安全，不会报空指针)
window.OnStep += ( deltaTime ) => {
    // 1. 玩家移动输入预测
    Vector2 moveInput = new Vector2( 1, 0 ) * (float)(100 * deltaTime);

    // 2. 利用 Spatial Hash 进行 $O(1)$ 高性能碰撞查询 (对标 GMS place_meeting)
    var predictedBounds = new AABB( playerPos + moveInput, playerPos + moveInput + new Vector2( 32, 32 ) );
    if ( !physicsGrid.PlaceMeeting( predictedBounds, "Wall", out _ ) ) {
        // 无碰撞，更新玩家位置
        playerPos += moveInput;
    }

    activeScene.MainCamera.Position = Vector2.Lerp( activeScene.MainCamera.Position, playerPos, 0.1f );
};

// 3. 世界场景渲染 (HandleRender 内部会自动调用 Graphics.ClearBuffers()，外部无需手动清屏)
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

// 4. UI 渲染 (完全独立于场景世界相机)
window.OnDrawGUI += () => {
    // 这里可以渲染不受 Camera 矩阵影响的血条、调试 FPS 文本等
};

// 启动主循环
window.Run();
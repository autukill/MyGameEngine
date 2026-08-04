namespace GameEngine.Features.SceneSystem;

using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.Camera;

public class SceneAggregate {
    public Guid SceneId { get; } = Guid.NewGuid();
    public Camera2D MainCamera { get; }

    private readonly Dictionary<string, Layer> _layers = new();
    private readonly List<Layer> _sortedLayers = new();

    public SceneAggregate( int width, int height ) {
        MainCamera = new Camera2D( new System.Numerics.Vector2( width, height ) );

        // 默认创建 GMS 经典的三个基础图层
        AddLayer( "Background", 10000 );
        AddLayer( "Instances", 0 );
        AddLayer( "UI", -10000 );
    }

    public void AddLayer( string name, int depthOrder ) {
        var layer = new Layer( name, depthOrder );
        _layers[name] = layer;
        _sortedLayers.Add( layer );
        _sortedLayers.Sort( ( a, b ) => b.DepthOrder.CompareTo( a.DepthOrder ) );
    }

    public Layer GetLayer( string name ) {
        return _layers[name];
    }

    /// <summary>
    /// 场景全流程 Step 逻辑更新
    /// </summary>
    public void Update( double deltaTime ) {
        // 更新逻辑、处理实体移动...
    }

    /// <summary>
    /// 场景渲染入口：应用 Camera 变换矩阵并按 Layer 提交渲染
    /// </summary>
    public void Render( SpriteShader shader, SpriteBatch batch ) {
        // 1. 更新 Shader 中的 Camera 变换矩阵 (View-Projection)
        shader.SetProjection( MainCamera.GetViewProjectionMatrix() );

        batch.Begin();

        // 2. 依次绘制每一个 Layer (从小到大/由远及近)
        for (int i = 0; i < _sortedLayers.Count; i++) {
            _sortedLayers[i].Draw( batch );
        }

        batch.End();
    }
}
namespace GameEngine.Features.Camera;

using System.Numerics;

public class Camera2D
{
    public Vector2 Position { get; set; } = Vector2.Zero;
    public float Zoom { get; set; } = 1.0f;
    public float Rotation { get; set; } = 0.0f; // 弧度
    public Vector2 ViewportSize { get; private set; }

    public Camera2D(Vector2 viewportSize)
    {
        ViewportSize = viewportSize;
    }

    public void ResizeViewport(float width, float height)
    {
        ViewportSize = new Vector2(width, height);
    }

    /// <summary>
    /// 获取 2D 正交 View-Projection 变换矩阵
    /// </summary>
    public Matrix4x4 GetViewProjectionMatrix()
    {
        // 1. 平移矩阵 (以相机中心为原点)
        var translation = Matrix4x4.CreateTranslation(-Position.X, -Position.Y, 0f);
        
        // 2. 旋转矩阵
        var rotation = Matrix4x4.CreateRotationZ(Rotation);
        
        // 3. 缩放矩阵
        var scale = Matrix4x4.CreateScale(Zoom, Zoom, 1.0f);

        // 4. View 变换矩阵
        var view = translation * rotation * scale;

        // 5. Orthographic 投影矩阵 (0,0 位于屏幕左上角)
        var projection = Matrix4x4.CreateOrthographicOffCenter(
            0, ViewportSize.X, 
            ViewportSize.Y, 0, 
            -1.0f, 1.0f
        );

        return view * projection;
    }
}

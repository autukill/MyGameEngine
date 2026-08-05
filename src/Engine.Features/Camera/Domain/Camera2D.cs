namespace GameEngine.Features.Camera.Domain;

using System.Numerics;

/// <summary>
/// 2D 正交相机：平移 / 缩放 / 旋转 + 震屏。
/// 本质是 View-Projection 矩阵生成器。
/// </summary>
public class Camera2D
{
    public Vector2 Position { get; set; } = Vector2.Zero;
    public float Zoom { get; set; } = 1.0f;
    public float Rotation { get; set; } = 0.0f; // 弧度
    public Vector2 ViewportSize { get; private set; }

    // 震屏参数
    private float _shakeTime = 0f;
    private float _shakeMagnitude = 0f;
    private readonly Random _rng = new();

    public Camera2D(Vector2 viewportSize) => ViewportSize = viewportSize;

    public void ResizeViewport(float width, float height)
        => ViewportSize = new Vector2(width, height);

    /// <summary>触发相机震屏</summary>
    public void Shake(float magnitude, float durationSeconds)
    {
        _shakeMagnitude = magnitude;
        _shakeTime = durationSeconds;
    }

    public Matrix4x4 GetViewProjectionMatrix()
    {
        Vector2 pos = Position;
        // 震屏偏移
        if (_shakeTime > 0)
        {
            float ox = ((float)_rng.NextDouble() * 2 - 1) * _shakeMagnitude;
            float oy = ((float)_rng.NextDouble() * 2 - 1) * _shakeMagnitude;
            pos += new Vector2(ox, oy);
        }

        var translation = Matrix4x4.CreateTranslation(-pos.X, -pos.Y, 0f);
        var rotation = Matrix4x4.CreateRotationZ(Rotation);
        var scale = Matrix4x4.CreateScale(Zoom, Zoom, 1.0f);
        var view = translation * rotation * scale;

        var projection = Matrix4x4.CreateOrthographicOffCenter(
            0, ViewportSize.X,
            ViewportSize.Y, 0,
            -1.0f, 1.0f);

        return view * projection;
    }

    /// <summary>更新计时器（在 Step 阶段调用）</summary>
    public void Update(double deltaTime)
    {
        if (_shakeTime > 0)
            _shakeTime -= (float)deltaTime;
    }
}

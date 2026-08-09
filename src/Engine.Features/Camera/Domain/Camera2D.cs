namespace GameEngine.Features.Camera.Domain;

using System.Numerics;
using GameEngine.Core.Domain.Gameplay;

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
    private Vector2 _shakeOffset;
    private readonly Random _rng = new();

    public bool IsShaking => _shakeTime > 0f;
    public float ShakeMagnitude => IsShaking ? _shakeMagnitude : 0f;
    public float ShakeTimeRemaining => MathF.Max(0f, _shakeTime);

    public Camera2D(Vector2 viewportSize) => ViewportSize = viewportSize;

    public void ResizeViewport(float width, float height)
        => ViewportSize = new Vector2(width, height);

    /// <summary>触发相机震屏</summary>
    public void Shake(float magnitude, float durationSeconds)
    {
        ValidateShake(magnitude, durationSeconds);
        if (magnitude == 0f || durationSeconds == 0f)
        {
            _shakeMagnitude = 0f;
            _shakeTime = 0f;
            _shakeOffset = Vector2.Zero;
            return;
        }
        _shakeMagnitude = magnitude;
        _shakeTime = durationSeconds;
    }

    /// <summary>
    /// Adds an independent shake request without allocating a runtime effect object. Magnitudes
    /// combine as orthogonal energy and the longest remaining duration wins.
    /// </summary>
    public void AddShake(float magnitude, float durationSeconds)
    {
        ValidateShake(magnitude, durationSeconds);
        if (magnitude == 0f || durationSeconds == 0f) return;
        _shakeMagnitude = MathF.Sqrt(
            _shakeMagnitude * _shakeMagnitude + magnitude * magnitude);
        _shakeTime = MathF.Max(_shakeTime, durationSeconds);
    }

    public Matrix4x4 GetViewProjectionMatrix()
        => CreateViewProjectionMatrix(Position + _shakeOffset);

    /// <summary>
    /// Returns the gameplay transform without transient camera shake. Coordinate
    /// conversion uses this matrix so pointer picking does not jitter with presentation.
    /// </summary>
    public Matrix4x4 GetStableViewProjectionMatrix() =>
        CreateViewProjectionMatrix(Position);

    /// <summary>
    /// Returns a conservative world-space AABB for the actual rendered View, including the current
    /// presentation shake. A rotated Camera therefore keeps its enclosing corners rather than
    /// incorrectly rejecting visible content.
    /// </summary>
    public bool TryGetVisibleWorldBounds(out Bounds2D bounds)
        => TryGetWorldBounds(GetViewProjectionMatrix(), out bounds);

    /// <summary>Returns Camera bounds without presentation-only shake.</summary>
    public bool TryGetStableVisibleWorldBounds(out Bounds2D bounds)
        => TryGetWorldBounds(GetStableViewProjectionMatrix(), out bounds);

    private bool TryGetWorldBounds(Matrix4x4 viewProjection, out Bounds2D bounds)
    {
        if (ViewportSize.X <= 0f || ViewportSize.Y <= 0f ||
            !Matrix4x4.Invert(viewProjection, out Matrix4x4 inverse))
        {
            bounds = default;
            return false;
        }

        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;
        Include(-1f, -1f);
        Include(1f, -1f);
        Include(1f, 1f);
        Include(-1f, 1f);
        if (!float.IsFinite(minX) || !float.IsFinite(minY) ||
            !float.IsFinite(maxX) || !float.IsFinite(maxY))
        {
            bounds = default;
            return false;
        }

        bounds = new Bounds2D(minX, minY, maxX, maxY);
        return true;

        void Include(float clipX, float clipY)
        {
            Vector4 world = Vector4.Transform(new Vector4(clipX, clipY, 0f, 1f), inverse);
            minX = MathF.Min(minX, world.X);
            minY = MathF.Min(minY, world.Y);
            maxX = MathF.Max(maxX, world.X);
            maxY = MathF.Max(maxY, world.Y);
        }
    }

    public Vector2 WorldToViewport(Vector2 worldPosition)
    {
        Vector4 clip = Vector4.Transform(
            new Vector4(worldPosition, 0f, 1f),
            GetStableViewProjectionMatrix());
        return new Vector2(
            (clip.X + 1f) * 0.5f * ViewportSize.X,
            (1f - clip.Y) * 0.5f * ViewportSize.Y);
    }

    public bool TryViewportToWorld(Vector2 viewportPosition, out Vector2 worldPosition)
    {
        if (ViewportSize.X <= 0f || ViewportSize.Y <= 0f ||
            !Matrix4x4.Invert(GetStableViewProjectionMatrix(), out Matrix4x4 inverse))
        {
            worldPosition = default;
            return false;
        }

        var clip = new Vector4(
            viewportPosition.X / ViewportSize.X * 2f - 1f,
            1f - viewportPosition.Y / ViewportSize.Y * 2f,
            0f,
            1f);
        Vector4 world = Vector4.Transform(clip, inverse);
        worldPosition = new Vector2(world.X, world.Y);
        return float.IsFinite(worldPosition.X) && float.IsFinite(worldPosition.Y);
    }

    private Matrix4x4 CreateViewProjectionMatrix(Vector2 position)
    {
        var translation = Matrix4x4.CreateTranslation(-position.X, -position.Y, 0f);

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
        {
            _shakeOffset = new Vector2(
                ((float)_rng.NextDouble() * 2f - 1f) * _shakeMagnitude,
                ((float)_rng.NextDouble() * 2f - 1f) * _shakeMagnitude);
            _shakeTime -= (float)deltaTime;
            if (_shakeTime <= 0f)
            {
                _shakeTime = 0f;
                _shakeMagnitude = 0f;
                _shakeOffset = Vector2.Zero;
            }
        }
        else
        {
            _shakeOffset = Vector2.Zero;
        }
    }

    private static void ValidateShake(float magnitude, float durationSeconds)
    {
        if (!float.IsFinite(magnitude) || magnitude < 0f)
            throw new ArgumentOutOfRangeException(nameof(magnitude));
        if (!float.IsFinite(durationSeconds) || durationSeconds < 0f)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
    }
}

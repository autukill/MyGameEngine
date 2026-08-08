namespace GameEngine.Testing.Visual;

using Silk.NET.OpenGL;

public static class FramebufferCapture
{
    /// <summary>Reads the current framebuffer and returns top-left-origin RGBA8 pixels.</summary>
    public static unsafe CapturedFrame Capture(GL gl, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(gl);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        var bottomUp = new byte[checked(width * height * 4)];
        gl.Finish();
        gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
        fixed (byte* pixels = bottomUp)
        {
            gl.ReadPixels(
                0,
                0,
                (uint)width,
                (uint)height,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                pixels);
        }

        var topDown = new byte[bottomUp.Length];
        int stride = width * 4;
        for (int y = 0; y < height; y++)
        {
            System.Buffer.BlockCopy(
                bottomUp,
                (height - 1 - y) * stride,
                topDown,
                y * stride,
                stride);
        }
        return new CapturedFrame(width, height, topDown);
    }
}

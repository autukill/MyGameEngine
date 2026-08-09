namespace GameEngine.Tools.Cli;

using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

public sealed class OpenGlContextProbe : IOpenGlContextProbe
{
    public OpenGlProbeResult Probe()
    {
        string result = "OpenGL probe did not reach the Load event.";
        bool success = false;
        try
        {
            var options = WindowOptions.Default;
            options.Title = "MyGameEngine Doctor";
            options.Size = new Vector2D<int>(1, 1);
            options.IsVisible = false;
            options.VSync = false;
            options.API = new GraphicsAPI(
                ContextAPI.OpenGL,
                ContextProfile.Core,
                ContextFlags.ForwardCompatible,
                new APIVersion(3, 3));

            using IWindow window = Window.Create(options);
            window.Load += () =>
            {
                try
                {
                    using GL gl = GL.GetApi(window);
                    result = $"OpenGL 3.3 context created; vendor={gl.GetStringS(StringName.Vendor)}, renderer={gl.GetStringS(StringName.Renderer)}.";
                    success = true;
                }
                catch (Exception ex)
                {
                    result = $"OpenGL context loaded but API inspection failed: {ex.Message}";
                }
                finally
                {
                    window.Close();
                }
            };
            window.Run();
        }
        catch (Exception ex)
        {
            result = $"Could not create a hidden OpenGL 3.3 context: {ex.Message}";
        }

        return new OpenGlProbeResult(success, result);
    }
}

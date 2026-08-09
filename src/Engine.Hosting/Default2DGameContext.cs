namespace GameEngine.Hosting;

using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.ContentAssets.Infrastructure;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;
using GameEngine.Features.Sprites.Infrastructure;
using GameEngine.Features.TextureAssets.Infrastructure;

/// <summary>Scene 装配期的强类型上下文；不是全局服务容器。</summary>
public sealed class Default2DGameContext
{
    private readonly Action _close;
    public EngineWindow Window { get; }
    public SceneAggregate Scene { get; }
    public TextureLibrary Textures { get; }
    public SpriteLibrary Sprites { get; }
    public LoadedContentPackage? Content { get; }
    public Camera2D Camera { get; }
    public RenderPipeline Pipeline { get; }
    public ScenePipelineBuilder Effects { get; }
    public RenderTargetPool RenderTargets { get; }

    internal Default2DGameContext(
        EngineWindow window,
        SceneAggregate scene,
        TextureLibrary textures,
        SpriteLibrary sprites,
        LoadedContentPackage? content,
        Camera2D camera,
        RenderPipeline pipeline,
        ScenePipelineBuilder effects,
        RenderTargetPool renderTargets,
        Action close)
    {
        Window = window;
        Scene = scene;
        Textures = textures;
        Sprites = sprites;
        Content = content;
        Camera = camera;
        Pipeline = pipeline;
        Effects = effects;
        RenderTargets = renderTargets;
        _close = close ?? throw new ArgumentNullException(nameof(close));
    }

    public SpriteRef GetSprite(string name) => RequireContent().GetSprite(name);

    public TextureRef GetTexture(string name) => RequireContent().GetTexture(name);

    public void RegisterRenderEffectFactory(IRenderEffectFactory factory) =>
        Effects.RegisterFactory(factory);

    public RenderPassHandle AddRenderPass(RenderPass pass) => Pipeline.AddPass(pass);

    /// <summary>请求在当前 Step/Draw 回调完成后的安全帧边界关闭窗口。</summary>
    public void Close() => _close();

    private LoadedContentPackage RequireContent() => Content ??
        throw new InvalidOperationException(
            "No content package is configured. Call UseContent on Default2DRendererOptions.");
}

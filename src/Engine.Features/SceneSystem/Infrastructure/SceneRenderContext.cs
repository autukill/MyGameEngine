namespace GameEngine.Features.SceneSystem.Infrastructure;

using System.Numerics;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.RenderPipeline.Domain;

/// <summary>
/// [已废弃] 场景渲染上下文。
///
/// Phase 1.4 起，Layer/Camera 管理已合并到 SceneAggregate 聚合根中。
/// SceneRenderPass 直接消费 SceneAggregate（Layer 感知版 DrawActive）。
/// 本类保留以兼容旧引用，但不再维护。
///
/// 迁移指南：
///   - 旧: new SceneRenderContext(w, h) -> ctx.AddLayer(...) -> ctx.Render(...)
///   - 新: new SceneAggregate("scene") -> scene.AddLayer(...) -> SceneRenderPass.Execute()
/// </summary>
[Obsolete("SceneRenderContext 已废弃。请使用 SceneAggregate + SceneRenderPass。")]
public class SceneRenderContext
{
    public Guid ContextId { get; } = Guid.NewGuid();
    public Camera2D MainCamera { get; }

    private readonly Dictionary<string, Layer> _layers = new();
    private readonly List<Layer> _sortedLayers = new();

    public SceneRenderContext(int viewportWidth, int viewportHeight)
    {
        MainCamera = new Camera2D(new Vector2(viewportWidth, viewportHeight));

        // 默认三个 GMS 经典图层
        AddLayer("Background", 10000);
        AddLayer("Instances", 0);
        AddLayer("UI", -10000);
    }

    public void AddLayer(string name, int depthOrder)
    {
        var layer = new Layer(name, depthOrder);
        _layers[name] = layer;
        _sortedLayers.Add(layer);
        _sortedLayers.Sort((a, b) => b.DepthOrder.CompareTo(a.DepthOrder));
    }

    public Layer GetLayer(string name) => _layers[name];

    public IReadOnlyList<Layer> GetSortedLayers() => _sortedLayers.AsReadOnly();

    /// <summary>把所有 Layer 的 RenderCommand 提交到 SpriteBatch</summary>
    /// <remarks>
    /// Per-Layer 状态覆盖（BlendState/DepthStencilState/ShaderOverride）
    /// 统一由 SceneRenderPass 在调用此方法前/后通过 RenderPassContext.GL 应用。
    /// 本方法只关心 Layer 命令提交，保持单一职责。
    /// </remarks>
    public void Render(SpriteShader shader, SpriteBatch batch, Camera2D? camera = null)
    {
        var cam = camera ?? MainCamera;
        shader.SetProjection(cam.GetViewProjectionMatrix());

        batch.Begin();
        for (int i = 0; i < _sortedLayers.Count; i++)
        {
            _sortedLayers[i].Draw(batch);
        }
        batch.End();
    }

    public void Update(double deltaTime) => MainCamera.Update(deltaTime);
}

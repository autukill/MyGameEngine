namespace GameEngine.Hosting;

using GameEngine.Features.RenderPipeline.Infrastructure;
using GameEngine.Core.Infrastructure.Diagnostics;

/// <summary>默认 2D Runtime 的显式只读诊断快照；不持有任何 GPU 对象。</summary>
public sealed record Default2DRenderDiagnostics(
    RenderPipelineDiagnostics Pipeline,
    ScenePipelineDiagnostics Effects,
    RenderTargetPoolDiagnostics RenderTargets,
    FrameStatisticsSnapshot? FrameStatistics,
    IReadOnlyList<ViewportSlotDiagnostics> Viewports);

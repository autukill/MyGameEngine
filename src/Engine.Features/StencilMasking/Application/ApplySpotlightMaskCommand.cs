namespace GameEngine.Features.StencilMasking.Application;

using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.StencilMasking.Domain;

/// <summary>
/// 在指定场景中对一个实例施加聚光灯遮罩（典型 DDD + VSA 演示命令）。
/// 被 StencilMaskCommandHandler 捕获后，会构造 StencilMaskEffectDescriptor，
/// 并通过通用 RenderEffectRequestedEvent 发出。
/// </summary>
/// <param name="MaskState">
///   ShowInside = 聚光灯/小地图/ScrollView 裁剪框（遮罩内可见）
///   ShowOutside = 战争迷雾挖孔/透视洞/黑洞吞噬（遮罩内不可见）
/// </param>
public sealed record ApplySpotlightMaskCommand(
    SceneAggregate Scene,
    InstanceId SpotlightId,
    Vector2D MaskCenter,
    float MaskRadius,
    StencilMaskState MaskState
);

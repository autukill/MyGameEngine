// MyGame.Runner — 引擎启动与配置入口 (Phase 0.1 占位)
using GameEngine.Core.Infrastructure.Windowing;

var options = EngineWindowOptions.Default with { Title = "C# 2D Engine - Phase 0.1 Proof of Concept" };
var engineWindow = new EngineWindow(options);

engineWindow.OnStep += delta =>
{
    // 逻辑帧：更新计数器或位置
};

engineWindow.OnDraw += () =>
{
    // 渲染帧：绘制场景
};

engineWindow.Run();

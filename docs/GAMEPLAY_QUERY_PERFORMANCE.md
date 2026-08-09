# Gameplay 空间查询基准

## 目的

当前 Find、Collision、Area 和 Radius 查询使用 Scene 内已提交实例的线性扫描。引入 Spatial Hash 会增加位置同步、负缩放、Spawn/Destroy 和 Scene 切换的一致性成本，因此先用数据决定。

普通代码可继续使用返回稳定数组的便利 API。高频多结果查询可复用调用方 Buffer：

```csharp
private readonly GameplayQueryBuffer<Enemy> nearby = new(initialCapacity: 32);

public override void OnStep(double deltaTime)
{
    QueryRadius(Position, 160f, nearby);
    foreach (Enemy enemy in nearby)
        Track(enemy);
}
```

每次查询先清空内容但保留容量；具体 `GameplayQueryBuffer<T>` 上的 `foreach` 使用 struct enumerator。只需要数量时使用 `CountInstances<T>()`，不要创建 `FindAll<T>()` 结果。

需要跨类型身份过滤时，使用 `FindAll(GameTags.Enemy, buffer)`、`CountInstances<T>(tag)` 或空间查询的 Tag 重载。Tag 查询仍走同一线性扫描并计入原有分类遥测；实例内部 Tag 集合在首次添加时延迟创建，预热后的 Buffer 查询保持 0 B。

## 运行

```powershell
dotnet build src/Engine.DddTests/Engine.DddTests.csproj -c Release
$env:DOTNET_TieredCompilation = '0'
dotnet run --project src/Engine.DddTests/Engine.DddTests.csproj `
  -c Release --no-build -- --benchmark-spatial
```

关闭本次进程的 tiered compilation 可以避免三个规模按顺序执行时，后一个规模恰好受益于 JIT 重编译。基准先预热，再对每个规模执行 500 次小半径 Circle 查询。

## 2026-08-09 基线

环境：Windows、.NET SDK 10.0.302、Release、单进程。

| 活跃 Collider | 稳定数组 API | 可复用 Buffer | 数组分配 | Buffer 分配 |
|---:|---:|---:|---:|---:|
| 100 | 0.0022 ms | 0.0021 ms | 120 B | 0 B |
| 1,000 | 0.0209 ms | 0.0208 ms | 120 B | 0 B |
| 10,000 | 0.2088 ms | 0.2104 ms | 120 B | 0 B |

本机 1,000 Collider 明显低于初始 1 ms/查询目标，因此本阶段不实现 Spatial Hash。Buffer 消除了结果集合分配，但不会改变 O(n) 扫描成本；无命中的稳定数组路径仍复用 `Array.Empty<T>()`。

## 真实玩法统计

启用 Hosting `PerformanceTelemetryOptions` 会同时打开查询统计。`RuntimePerformanceSnapshot.GameplayQueries` 按 Find、Collision、Area、Radius 提供 Query、Candidate、Hit、Elapsed，并提供采样 Step 数和平均每 Step 查询毫秒数。遥测发布后自动开始新的查询统计区间。

Asteroids Playground 可直接观察真实负载：

```powershell
dotnet run --project playgrounds/Asteroids/Asteroids.csproj -- --diagnostics
```

关闭遥测时不读取高精度时钟；查询行为与结果不变。

## 重新评估条件

- 实际 Playground 或游戏稳定超过约 1,000 个活跃 Collider。
- 单帧执行大量独立空间查询，累计时间进入帧预算的 5% 以上。
- 性能遥测或采样器确认瓶颈位于查询，而不是 Draw、脚本逻辑或结果消费。

满足条件后可在 Scene 后方加入 uniform grid/Spatial Hash，同时保持现有公开查询 API。位置直接赋值、负缩放、inactive、帧末 Spawn/Destroy 和 Scene 切换必须纳入索引一致性测试。

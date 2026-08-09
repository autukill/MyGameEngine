# Gameplay 空间查询基准

## 目的

当前 `FirstCollision`、`Collisions`、`QueryArea` 和 `QueryRadius` 使用 Scene 内已提交实例的线性扫描。引入 Spatial Hash 会增加位置同步、负缩放、Spawn/Destroy 和 Scene 切换的一致性成本，因此先用数据决定。

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

| 活跃 Collider | 平均查询时间 | 托管分配/查询 |
|---:|---:|---:|
| 100 | 0.0021 ms | 120 B |
| 1,000 | 0.0201 ms | 120 B |
| 10,000 | 0.2050 ms | 120 B |

本机 1,000 Collider 明显低于初始 1 ms/查询目标，因此本阶段不实现 Spatial Hash。120 B 来自稳定结果集合；无命中路径仍复用 `Array.Empty<T>()`。

## 重新评估条件

- 实际 Playground 或游戏稳定超过约 1,000 个活跃 Collider。
- 单帧执行大量独立空间查询，累计时间进入帧预算的 5% 以上。
- 性能遥测或采样器确认瓶颈位于查询，而不是 Draw、脚本逻辑或结果消费。

满足条件后可在 Scene 后方加入 uniform grid/Spatial Hash，同时保持现有公开查询 API。位置直接赋值、负缩放、inactive、帧末 Spawn/Destroy 和 Scene 切换必须纳入索引一致性测试。

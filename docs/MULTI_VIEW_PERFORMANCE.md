# 多 Render View 稳定性能基准

`Engine.PerformanceBenchmarks` 是无窗口、无 OpenGL 的独立可执行基准。它把性能实验与 `Engine.DddTests` 领域烟测分离，避免为了观察多 View 调度成本而运行整套领域验证。

## 运行

先构建 Release，再直接运行：

```powershell
dotnet build MyGameEngine.slnx -c Release --no-restore
dotnet run -c Release --project src/Engine.PerformanceBenchmarks/Engine.PerformanceBenchmarks.csproj --no-build --no-restore
```

默认预热 128 帧、测量 1,000 帧。需要延长样本时使用：

```powershell
dotnet run -c Release --project src/Engine.PerformanceBenchmarks/Engine.PerformanceBenchmarks.csproj --no-build --no-restore -- --warmup 256 --frames 5000
```

## 场景模型

每个规模使用 100、1,000 和 10,000 个实例，稳定分布到四个有实例的 Layer：

- main View 允许全部 Layer，候选数等于实例总数；Camera 可见 20%。
- observer 排除 `MainOnly`，候选数为 75%；Camera 可见 15%。
- 两个 View 使用互不重叠的世界范围。
- 每个实例都有可解析的 16×16 Sprite 视觉边界，但 Draw 回调为空；因此结果只描述 Scene 调度、Layer 过滤和保守剔除的 CPU 成本，不代表真实 GPU 绘制成本。

`unculled` 与 `culled` 交替先后执行，减少固定执行顺序带来的缓存偏差。毫秒值只用于同一机器、同一构建配置的前后对比，不作为 CI 阈值。

## 确定性守卫

可执行程序会验证：

- main/observer 候选数分别为 `100%/75%`。
- main/observer Draw 数分别为 `20%/15%`。
- Culled 数等于候选数减 Draw 数。
- 两个 View 的排序比较数均为零。
- 预热后的双 View 调度保持 `0 B/frame`。

任何守卫失败都会返回非零退出码。这里刻意不对毫秒值设置硬阈值：调度计数和分配量是确定性的，而短时 CPU 时间会受到 JIT、功耗策略和其他进程影响。

## 2026-08-09 本机基线

.NET 10.0.10、32 逻辑处理器、Release、预热 128 帧并测量 1,000 帧：

| 实例数 | 无剔除双 View | 带剔除双 View | 候选 main/observer | Draw main/observer | 分配 |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 100 | 0.0130 ms | 0.0296 ms | 100/75 | 20/15 | 0 B/frame |
| 1,000 | 0.0395 ms | 0.0931 ms | 1,000/750 | 200/150 | 0 B/frame |
| 10,000 | 0.1101 ms | 0.4314 ms | 10,000/7,500 | 2,000/1,500 | 0 B/frame |

Null Batch 下剔除检查比空 Draw 回调更贵是预期结果；真实 Sprite 顶点生成、Batch 状态和 GPU 提交成本不在本基准内。10,000 实例的调度仍低于 `0.5 ms`，当前没有足够证据承担跨 View 缓存的一致性复杂度。

## 决策边界

只有在真实项目的同机 Release 数据表明逐 View 候选检查成为瓶颈时，才重新考虑跨 View 可见性缓存。缓存必须先证明不会破坏同帧 Layer/Depth 变更、不同 Camera 边界和自定义 Draw Bounds 语义；当前结论是保留简单的逐 View 检查，并把本基准作为未来复核的稳定对照组。

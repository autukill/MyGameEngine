# SceneAggregate 生命周期性能

`SceneAggregate` 的常规帧路径在实例数量稳定并完成一次预热后，不再产生托管堆分配。覆盖范围包括：

- `PerformInput` 的按键按下与释放分发。
- `PerformStep` 的 Alarm、Begin Step、Step、End Step 与 Sprite 动画推进。
- `DrawActive` 的 Layer 筛选、Depth 排序和 Draw Begin/Draw/Draw End。
- `DrawGUI` 的活跃实例分发。

## 实现边界

聚合根持有可复用的生命周期、GUI 与绘制快照列表。列表容量只在实例峰值首次增长时扩容；场景进入稳定实例规模后，后续帧复用已有存储。

绘制顺序使用原地 O(n log n) 堆排序，不创建 LINQ 迭代器、临时集合或比较器对象。排序规则保持为：

1. 同一 Layer 中 `Depth` 较大的实例先绘制。
2. `Depth` 相同时保持实例加入 Scene 的先后顺序。

## 生命周期语义保持

优化没有把整个 Step 合并成单一快照。聚合根仍会在 Alarm、Begin Step、Step、End Step 和动画推进前分别捕获当前实例，因此保留既有语义：

- 直接在 Begin Step 调用 `SceneAggregate.Add` 的实例可参与同帧 Step。
- 直接在 Begin Step 调用 `SceneAggregate.Destroy` 的实例不再参与同帧 Step。
- 通过 Gameplay API 请求的 `Spawn/Destroy` 仍在 End Step 后按请求顺序提交；新实例从下一帧开始 Step，待销毁实例完成当前帧。
- Gameplay/Unscaled 时间域、暂停时的输入和更新过滤、暂停期间继续 Draw 的行为不变。

实例回调可以修改 Scene，因为当前正在遍历的是快照；不支持从回调重入调用同一个 Scene 的 `PerformStep` 或 `DrawActive`。

## 分配口径

“零稳态分配”特指上述每帧调度路径。以下显式、低频行为不包含在该承诺中：

- Scene 首次达到新的实例峰值时，内部快照列表扩容。
- `Add`、`Destroy`、领域事件和调用方自己的实例回调产生的分配。
- `AllInstances`、`ActiveInstances`、空间查询结果和事件快照等面向调用方的稳定结果集合。
- 调试诊断、日志和 benchmark 输出。

## 回归测试与基准

`Engine.DddTests` 使用 128 个无分配 Probe 预热后连续验证 512 次 Input、Step、Draw 和 DrawGUI，要求各阶段 `GC.GetAllocatedBytesForCurrentThread()` 增量均为 0。同时验证阶段间直接 Add/Destroy 的可见性和相同 Depth 的稳定绘制顺序。

可选 Release 基准：

```powershell
dotnet run --configuration Release --project src/Engine.DddTests/Engine.DddTests.csproj --no-restore -- --benchmark-lifecycle
```

2026-08-09 本机基线（240 帧，包含 Step、Draw 与 DrawGUI；空输入）：

| 实例数 | 时间/帧 | 托管分配/帧 |
|---:|---:|---:|
| 100 | 0.0650 ms | 0 B |
| 1,000 | 0.2739 ms | 0 B |
| 10,000 | 1.5946 ms | 0 B |

这些数字用于观察同一机器上的趋势，不作为跨硬件性能承诺。若实例数量、Layer 数量或回调工作量不同，应以真实 Playground 的低频遥测为准。

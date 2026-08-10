# 首版模拟数据契约

## 目的

本文件冻结首个 10 分钟灰盒所需的身份、Tick、观察、信仰和神兽学习语义。它是游戏侧实现契约，不是 MyGameEngine 公共 API。

设计意图分别见[信仰推演系统](BELIEF_SIMULATION.md)和[神兽学习系统](FAMILIAR_LEARNING.md)；如果叙述性文档与本文件的首版数值冲突，以本文件为实现基准。

## 时间与身份

- 固定更新：60 Tick/s。
- 所有计时保存为整数 Tick。
- Gameplay Pause 不推进 Tick；Unscaled GUI 仍可绘制。
- Scene 内运行时引用使用 `InstanceRef`，跨状态与存档使用稳定字符串 ID。
- 资源、角色、地点、事件、因果和动作 ID 均区分大小写并在装配阶段拒绝重复。

建议游戏侧值对象：

```csharp
readonly record struct VillagerId(string Value);
readonly record struct WorldEventId(ulong Value);
readonly record struct BeliefCauseId(string Value);
readonly record struct BeliefEffectId(string Value);
readonly record struct FamiliarSituationId(string Value);
readonly record struct FamiliarActionId(string Value);
```

## 世界观察

```csharp
readonly record struct WorldObservation(
    WorldEventId Id,
    long Tick,
    ObservationKind Kind,
    ObservationChannel Channel,
    string SubjectId,
    string? TargetId,
    GridCell Cell,
    byte Salience);
```

- `Salience` 范围 `0..100`。
- Visual 使用网格 Bresenham 视线并受阻挡 Tile 遮挡。
- Auditory 不要求视线，但按距离衰减。
- Direct 只发送给动作参与者。
- 每名村民保留最近 32 条关键观察；满时优先移除低显著性旧记录。

### 首版事件表

| Kind | Channel | Salience | 基础范围/Cell | 可作为 |
|---|---|---:|---:|---|
| `BellRang` | Auditory | 90 | 20 | Cause |
| `RainStarted` | Visual+Auditory | 100 | 12 | Cause、Effect |
| `RainEnded` | Visual | 55 | 10 | Context |
| `CropWithered` | Visual | 75 | 6 | Cause |
| `CropRecovered` | Visual | 90 | 6 | Effect |
| `OfferingPlaced` | Visual | 65 | 5 | Cause |
| `FuneralStarted` | Visual+Auditory | 85 | 10 | Cause |
| `FamiliarArrived` | Visual | 70 | 7 | Cause |
| `FamiliarActed` | Visual | 80 | 7 | Cause |
| `GateOpened` | Visual+Auditory | 85 | 8 | Effect |
| `FireStarted` | Visual | 95 | 10 | Cause |
| `FireExtinguished` | Visual | 90 | 8 | Effect |
| `VillagerInjured` | Visual+Direct | 100 | 8 | Effect |

## 因果白名单

只有下列组合可形成首版假说：

| Cause | Effect | 期待窗口 | 真相属性 |
|---|---|---:|---|
| `BellRang` | `RainStarted` | 8s | 偶然相关候选 |
| `OfferingPlaced` | `RainStarted` | 8s | 偶然相关候选 |
| `FamiliarArrived` | `RainStarted` | 8s | 偶然相关候选 |
| `CropWithered` | `RainStarted` | 12s | 玩家响应候选 |
| `RainStarted` | `CropRecovered` | 15s | 真实物理因果 |
| `FamiliarActed` | `GateOpened` | 8s | 真实行为因果 |
| `FuneralStarted` | `RainStarted` | 12s | 偶然相关候选 |
| `BellRang` | `CropRecovered` | 20s | 可经降雨形成的间接误解 |

“真相属性”仅供策划和测试使用，村民推演过程不能读取它。

## 信仰整数模型

每名村民每条假说保存：

```text
Score                 -1000..1000
SupportingEvidence    0..255
Contradictions        0..255
LastUpdatedTick
LastEvidenceEventIds  最近 4 组
```

证据强度使用整数计算：

```text
Evidence = Salience × Temporal × Distance × Reliability / 1_000_000
```

其中各因子范围 `0..100`。更新规则：

- 观察到 Cause 后在窗口内观察到 Effect：`Score += Evidence × 4`。
- 亲眼观察 Cause，但窗口结束仍未观察到 Effect：`Score -= Evidence × 3`。
- Score 始终钳制到 `-1000..1000`。
- 已有地方传统可提供一次性 Prior，钟声召雨对岑伯和眠婆为 `+120`，其他人为 `0`。

解释阈值：

| Score | 状态 |
|---:|---|
| `< -200` | 明确反对 |
| `-200..99` | 未形成看法 |
| `100..299` | 怀疑或猜测 |
| `300..449` | 个人相信 |
| `>= 450` | 愿意公开倡议 |

公共教义需要一名 `Score >= 450` 的倡议者，以及至少两名经交流后 `Score >= 300` 的响应者。交流只增加证言证据，不伪装成亲眼观察；单次证言增量上限为 80。

每名村民最多保留 8 条活跃假说。淘汰顺序为绝对 Score 最低、最久未更新、稳定 Cause/Effect ID 排序。

## 神兽态势分类

强化学习不直接使用所有布尔特征的幂集。感知系统按以下优先级选出一个最显著态势：

| 优先级 | Situation | 进入条件 |
|---:|---|---|
| 1 | `FireEmergency` | 观察范围内存在可到达火源。 |
| 2 | `VillagerInDanger` | 亲近村民受伤或处于即时危险。 |
| 3 | `BlockedWaterGate` | 水闸阻塞且存在可搬动物体。 |
| 4 | `DryCropHoldingWater` | 附近有干旱作物且神兽持水。 |
| 5 | `DryCropNeedsWater` | 附近有干旱作物且可定位水源。 |
| 6 | `BellGathering` | 多名村民在钟塔附近聚集。 |
| 7 | `IdleVillage` | 没有更高优先级态势。 |

候选动作保持有限：`FetchWater`、`PourWater`、`CarryObject`、`RingBell`、`ComfortVillager`、`Flee`。没有合法动作时由普通状态机等待，不把 `Wait` 写进 Q 表。

## 表格型强化学习参数

所有 Q 值和奖励使用整数毫点：

```text
Q 范围             -8000..8000
Alpha               350 / 1000
Gamma               200 / 1000
Epsilon             80 / 1000
DemonstrationPrior  +800
DecisionCooldown    60 Tick
FailureCooldown     180 Tick
```

整数更新：

```text
Target = Reward + Gamma × MaxNextQ / 1000
Delta  = Alpha × (Target - CurrentQ) / 1000
NewQ   = Clamp(CurrentQ + Delta, -8000, 8000)
```

### 奖励表

| Reward Reason | 值 |
|---|---:|
| 玩家嘉许 | `+2500 × TrustMultiplier` |
| 玩家制止 | `-3000 × TrustMultiplier` |
| 作物因本次行为恢复 | `+1600` |
| 火因本次行为熄灭 | `+2200` |
| 水闸因搬运打开 | `+1800` |
| 亲近村民脱险 | `+1400` |
| 行为导致村民受伤 | `-4000` |
| 动作完成但没有效果 | `-300` |
| 路径或 Affordance 失败 | `-600` |
| 首次探索新安全对象 | `+100` |

`TrustMultiplier` 范围 `750..1250 / 1000`。同一结果只归因给最近一个仍在信用窗口内的动作；首版信用窗口为 5 秒，不进行跨越任意长任务链的追溯。

### 行为选择

- 先以身体能力、Affordance、路径可达性和剧情保护规则过滤动作。
- 每次新态势或动作结束后决策，至少间隔 60 Tick。
- 使用 GameplayRandom 做千分制探索判定。
- 探索只在两个以上合法安全动作时发生。
- 非探索时比较 `Q + Instinct + Personality - Risk`。
- 同分按固定动作 ID 顺序选择。
- 最近 16 次选择与价值更新写入解释环形缓冲区，供梦境和诊断使用。

## 状态 Hash、Replay 与未来存档

Gameplay State Hash 至少贡献：

- 章节 Tick 和日程阶段。
- 水位、水闸、水渠及三块田地湿度。
- 12 名村民的稳定 ID、Cell、任务、个人假说和传播队列。
- 神兽 Cell、当前态势、动作、Q 表、冷却、信赖和 GameplayRandom 状态。
- 已产生观察的单调 EventId，以及未完成因果窗口。

Replay 仍只负责复现逻辑输入和验证状态，不等于正式存档。正式 Snapshot 等垂直切片状态稳定后另行设计。

## 无窗口验收

- 固定事件序列产生完全一致的信仰分数、传播顺序和公共教义。
- 不可见事件不能进入个人证据。
- Cause 窗口成功、超时和边界 Tick 行为明确。
- 固定训练序列产生一致 Q 表和动作选择。
- Q 值、奖励、计数和环形缓冲区永不越界。
- 非法动作即使具有最高 Q 值也不能成为候选。
- Snapshot 恢复后的下一次信仰更新和神兽选择与原运行一致。

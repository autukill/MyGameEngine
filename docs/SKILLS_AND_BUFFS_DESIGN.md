# 技能与 Buff 功能设计思考

> 状态：需求与架构设计记录，尚未实现运行时代码。

本文分析不依赖 UI 的技能与 Buff 运行时。图标、技能栏、Tooltip、进度条和编辑器不是当前范围；它们未来只能观察运行时状态，不能反向定义领域语义。

## 结论

- **Skill 是一次能力执行流程**：接收施放请求，验证条件，经历前摇/提交/后摇/冷却，并在提交点产生一次明确效果。
- **Buff 是附着在实例上的持续状态**：具有来源、持续时间、层数、重应用规则和移除原因，按 Owner 时间域更新。
- Skill 和 Buff 可以复用 `GameplayBehavior`、`GameplayTag`、`GameplayStateMachine`、`InputActionBuffer` 与 Gameplay 时间，但不应简单等同于这些基础原语。
- 第一版不做“万能 Effect JSON”或反射式属性字典。技能效果仍由游戏代码执行，Buff 运行时只管理可靠的生命周期与叠层。
- 如果 Buff 要修改移动速度、攻击力等数值，应先建立“基础值 + Modifier 重算”的属性边界，不能通过乘上/除回或减去旧值来撤销效果。
- 推荐建立独立 `Engine.Features.GameplayAbilities` 垂直切片，依赖 Core；Core 不继续膨胀为完整 RPG 框架。

## 为什么不能只用 GameplayBehavior

Behavior 适合固定装配、随 Owner 共存的局部能力，而 Buff 常常需要运行时添加、刷新、叠层、驱散和过期。当前 Behavior 集合在实例进入 Scene 后冻结，这是保证确定性和零分配调度的重要约束，不应为了 Buff 打开任意运行时增删。

因此推荐：

- 一个固定装配的 `BuffContainerBehavior<TInstance>` 管理 Owner 的全部动态 Buff。
- 一个固定装配的 `SkillBookBehavior<TInstance>` 或普通 `SkillBook` 管理 Loadout 和技能运行状态。
- Buff 和单个 Skill Runtime 是容器内部的小型值状态，不为每个 Buff 创建 `GameInstance`，也不动态添加 Behavior。

## Skill 模型

### 稳定引用与定义

建议公共模型：

```csharp
public readonly record struct SkillRef(string Name);

public sealed record SkillDefinition(
    SkillRef Ref,
    double CastSeconds,
    double RecoverySeconds,
    double CooldownSeconds,
    int MaxCharges = 1,
    SkillCooldownStart CooldownStart = SkillCooldownStart.OnCommit);
```

名称大小写敏感，定义在组合期冻结。Definition 只包含可验证、可序列化的数据，不包含 `Action`、GPU 对象、输入键、Prefab Factory 或 Scene 回调。

输入键通过现有 `InputActionRef` 映射到施放意图，而不是写入 SkillDefinition：

```csharp
UpdateActionBuffer(castFireball, deltaTime);
if (castFireball.IsBuffered && skills.TryRequest(
        GameSkills.Fireball,
        SkillTarget.Point(aimPosition)))
{
    castFireball.TryConsume();
}
```

### 目标模型

第一版目标应是无分配的显式值，而不是 `object` 参数包：

```csharp
public enum SkillTargetKind { None, Self, Point, Direction, Instance }

public readonly record struct SkillTarget(
    SkillTargetKind Kind,
    Vector2D PointOrDirection,
    InstanceId InstanceId);
```

创建方法负责验证：方向必须有限且可选择归一化，Instance 目标保存稳定 `InstanceId`，提交时重新查询并验证是否仍存在。目标失效策略由技能定义或执行器明确选择，不能静默改为 Self。

### 生命周期

推荐状态：

```text
Ready
  -> Requested/Validated
  -> Casting       前摇，可被打断
  -> Committed     效果只执行一次
  -> Recovery      后摇
  -> Ready

Cooldown/Charge 恢复与上述阶段并行记录
```

规则：

- `TryRequest` 同步完成廉价结构验证：技能已装备、当前可请求、目标值合法。
- 法力、弹药、沉默、地面状态等游戏专属条件由游戏的 Gate/Executor 验证，通用引擎不假设资源类型。
- 效果只在 Commit 点执行一次，不能同时在 Request 和动画事件中重复执行。
- 默认从 Commit 开始冷却；需要按下即进入冷却的技能显式选择 `OnRequest`。
- Cancel 必须携带原因，并明确“是否返还 Charge/资源、是否进入部分冷却”。第一版可以只支持提交前取消和一种固定策略。
- 同一 Owner 第一版只允许一个有前摇/后摇的 Active Cast；瞬发技能是否允许并行需要显式设置，不能由调用顺序偶然决定。
- 所有计时使用传入的 Owner `deltaTime`，不读取 `DateTime` 或全局时钟。

### 效果执行边界

不要把任意效果做成字符串解释器：

```json
{ "effect": "damage", "formula": "atk * 1.2 + level" }
```

这会过早引入表达式语言、反射、调试困难和资产版本问题。第一版建议 SkillBook 只产出强类型提交记录：

```csharp
public readonly record struct SkillCommit(
    SkillRef Skill,
    InstanceId Owner,
    SkillTarget Target,
    long Sequence);
```

游戏代码或注册的 `ISkillExecutor` 消费 Commit，执行 Spawn、伤害、位移或 ApplyBuff。Executor 由组合根按 `SkillRef` 注册，不存进 Definition，也不直接持有 Shader、Texture 或 RenderPass。

如果后续出现大量重复效果，再逐步加入少量强类型标准 Effect，例如 `ApplyBuffEffect`、`SpawnPrefabEffect<TArgs>`；不要先设计覆盖所有游戏的万能 Effect Graph。

## Buff 模型

### 定义与应用

建议公共模型：

```csharp
public readonly record struct BuffRef(string Name);

public sealed record BuffDefinition(
    BuffRef Ref,
    double? DurationSeconds,
    BuffReapplyPolicy ReapplyPolicy,
    int MaxStacks = 1,
    BuffStackScope StackScope = BuffStackScope.Shared);

public readonly record struct BuffApplication(
    BuffRef Buff,
    InstanceId Source,
    float Magnitude = 1f);
```

`DurationSeconds = null` 表示永久 Buff；有限持续时间必须为正值。运行时应保存规范化 Definition、Source、Remaining、Stacks、Magnitude 和稳定 Apply Sequence。

### 重应用与叠层

第一版应明确支持有限集合，不用多个 bool 组合出含糊语义：

- `Ignore`：已有时忽略新申请。
- `RefreshDuration`：保持层数，刷新共享剩余时间。
- `AddStack`：增加层数但不刷新时间。
- `AddStackAndRefresh`：增加到上限并刷新时间。
- `Replace`：用新 Source/Magnitude 重置运行时。

`MaxStacks` 必须至少为 1。第一版每个 Active Buff 只有一个共享剩余时间，不支持“每层独立倒计时”；独立层计时会显著增加移除顺序、快照和 UI 展示复杂度，应由真实玩法推动。

来源范围需要显式：

- `Shared`：同一个 `BuffRef` 在目标上只有一个 Runtime，不同来源共同叠层。
- `PerSource`：Runtime Key 为 `(BuffRef, SourceId)`，不同施法者分别计时。

不应只保留“最后一次 Source”却假装支持多来源 DoT。

### 更新与安全修改

Buff Handler 可能在 `OnApply/OnStep/OnRemove` 中继续申请或移除其他 Buff，因此容器不能一边遍历一边直接修改 Active 集合。推荐阶段：

```text
Owner OnStep
BuffContainer OnStep
  1. 更新本帧开始时已存在的 Buff
  2. 收集过期项
  3. 逆序/稳定顺序调用 Remove
  4. 按请求序列提交本帧 Apply/Remove
Owner/Behavior End Step
```

新申请的 Buff 在提交后可查询，但从下一 Step 才扣减完整持续时间，避免刚添加就损失一帧。相同帧的 Apply/Remove 冲突采用请求顺序，重复 Remove 是安全 no-op。Owner 销毁时按 Active Apply Sequence 的逆序清理，确保 Tag 或 Modifier 不泄漏。

推荐回调：

- `OnApply`
- `OnRefresh`
- `OnStackChanged`
- `OnStep`
- `OnRemove(BuffRemovalReason)`

Handler 应由 `BuffRef` 注册且尽量无状态；每个 Buff 的可变状态保存在 Runtime，不在共享 Handler 字段中。

### Buff 与 GameplayTag

Buff 可以在应用时给 Owner 添加 `Poisoned`、`Stunned` 等可查询 Tag，移除时撤销。但需要来源计数：两个不同 Buff 都声明 `Stunned` 时，移除其中一个不能直接删除仍由另一个 Buff 提供的 Tag。

因此 BuffContainer 应维护 Tag Contribution 计数，只有最后一个贡献者移除时才从 Owner 删除 Tag。Owner 构造函数原本就具有的 Tag 不属于容器，BuffContainer 不得误删。

## 属性 Modifier 边界

以下做法不可取：

```csharp
owner.MoveSpeed *= 1.5f; // Apply
owner.MoveSpeed /= 1.5f; // Remove
```

多 Buff、刷新、替换、浮点误差和中途修改基础值都会让“除回去”失效。正确模型是保留 Base Value，并从当前 Modifier 集合重算：

```csharp
public readonly record struct GameplayStatRef(string Name);

public readonly record struct StatModifier(
    GameplayStatRef Stat,
    BuffRuntimeKey Source,
    StatModifierKind Kind,
    float Value,
    int Priority);
```

建议固定计算顺序：

```text
(Base + Sum(Add)) * Product(Multiply)，最后按 Priority 应用 Override
```

相同优先级 Override 必须快速失败或使用稳定 Sequence，不能依赖 Dictionary 枚举顺序。Modifier 以 Buff Runtime Key 为 Source，移除 Buff 时可以精确撤销并重新计算。

通用 StatBlock 是否值得实现，需要至少两个真实玩法验证。第一版 Buff Runtime 可以只做生命周期、叠层、Tag Contribution 和游戏自定义 Handler，把通用属性系统放在独立后续切片。

## 与现有系统的关系

| 现有能力 | Skill/Buff 如何复用 |
| --- | --- |
| `GameplayBehavior` | 固定装配 SkillBook/BuffContainer；不动态添加每个 Buff Behavior |
| `GameplayTag` | 技能禁用条件、Buff 状态暴露、Buff 分类；需要贡献计数 |
| `GameplayStateMachine` | 单个复杂技能的 Casting/Recovery；SkillBook 可用更紧凑的专用状态 |
| `InputActionBuffer` | 前摇技能和冷却结束前的预输入 |
| `GameplayTime` | 所有技能、冷却和 Buff 默认继承 Owner 暂停与 TimeScale |
| `PrefabRef<T,TArgs>` | Skill Commit 后由游戏 Executor 生成子弹、区域或召唤物 |
| Find/Collision/Tag Query | 验证目标和技能命中；定义阶段不保存对象引用 |
| Scene 安全边界 | Spawn/Destroy 继续排队；Buff 容器内部也使用稳定请求序列 |
| RenderEffect | 只消费技能/Buff 事件做表现，不进入 Skill/Buff Domain Definition |

当前 Behavior 只提供强类型 Owner 和安全销毁，不提供完整 Spawn/Find/Input Context。不要为了 Skill/Buff 一次性复制 `GameInstance` 的全部 protected API。先让 Owner 或 Executor 消费 Skill Commit；只有多个真实 Behavior 都需要同一窄能力时，再扩展名字明确的 Behavior Context。

## 事件、表现与 UI 边界

技能和 Buff 运行时未来可发布纯值通知：

- `SkillRequested/Started/Committed/Cancelled/Ready`
- `BuffApplied/Refreshed/StackChanged/Removed`

这些通知用于音频、粒子、RenderEffect、日志和未来 UI，但 Domain 不直接调用它们。通知必须带稳定 Ref、Owner/Source `InstanceId`、Sequence 和 Removal/Cancel Reason，不携带 Texture、SpriteBatch、Shader 或 UI 控件。

第一版不需要全局事件总线。可以由 Owner-local 容器提供“获取并清空快照”，组合根或明确消费者在 Step 边界处理，与当前 Scene 事件快照保持同样思路。

## 性能与确定性

- Skill Loadout 在构建后冻结，按稳定数组迭代；Ref 索引只在配置或请求时查询。
- Buff Active/Request/Removal 列表复用容量，不使用 LINQ、闭包或每帧快照数组。
- 实例没有 Skill/Buff 时不创建容器集合；一个 Buff 不对应一个 GameInstance。
- 稳态无 Apply/Remove 时目标为 `0 B/frame`；Apply 时允许容器扩容，但应支持预留容量。
- 所有冲突使用显式 Sequence 和声明顺序解决，不依赖 HashSet/Dictionary 枚举顺序。
- 时间只来自 Owner `deltaTime`；Gameplay pause 冻结，Unscaled Owner 继续。
- inactive Owner 不推进技能、冷却或 Buff。
- 将来保存/回放只需要记录 Ref、阶段、剩余时间、层数、Source 和 Sequence；不保存委托或对象引用。

## 推荐渐进路线

### 切片 1：Buff Runtime 边界

- 新建 `Engine.Features.GameplayAbilities` 与无窗口 Tests。
- 实现 `BuffRef`、Definition、Application、RuntimeKey、叠层/刷新策略和 RemovalReason。
- 实现固定装配的 BuffContainer Behavior、请求队列、共享/按来源 Runtime、暂停感知更新和幂等移除。
- 先支持 Handler 与 Tag Contribution，不做通用 StatBlock。
- 用 Asteroids 增加一个无 UI 的临时加速或无敌拾取物验证 Apply/Refresh/Expire。

### 切片 2：Skill Runtime 边界

- 实现 `SkillRef`、Definition、Target、Commit、CancelReason、Charge 与 Cooldown。
- 一个 Owner 同时只允许一个非瞬发 Active Cast。
- Owner/Game-specific Executor 消费 Commit；不做万能 Effect DSL。
- 用 Airplane Shooter 增加一个按键触发、带冷却的冲刺或多发射击技能。

### 切片 3：可逆属性 Modifier

- 至少有移动速度和伤害两个真实用例后，再设计 `GameplayStatRef/StatBlock/StatModifier`。
- 固定运算顺序、Source Key、Priority 和重算语义。
- Buff 移除只撤销自身贡献，不反向修改累计结果。

### 切片 4：声明式资产与生成引用

- 只有 C# 定义稳定后，才把纯数据 Definition 接入 `assets.json`/独立 abilities manifest。
- AssetCompiler 生成强类型 `GameSkills/GameBuffs` 引用并做重复、范围和依赖校验。
- Executor/Handler 仍由代码注册；JSON 不嵌入类型名、反射构造或脚本字符串。

## 暂不实现

- UI 技能栏、Buff 图标和 Tooltip。
- 法力、怒气、Health、Damage 的统一 RPG 模型。
- 万能公式语言、脚本解释器和 Effect Graph。
- 每层独立 Buff 倒计时、Aura 自动传播和复杂驱散优先级。
- Behavior 运行时任意增删。
- 网络同步、预测回滚、时间回溯和存档格式。
- 资产热重载过程中迁移正在施放的 Skill 或 Active Buff。

## 下一步建议

优先实施 **Buff Runtime 边界**，而不是同时完成 Skill、Buff、Stat、Damage 和声明式资产。它能最先验证运行时动态状态与冻结 Behavior 组合是否协调，也能暴露叠层、来源、暂停和安全修改的真实问题。Skill Runtime 随后建立在已经验证的 Buff Apply 与 Owner 时间语义上。

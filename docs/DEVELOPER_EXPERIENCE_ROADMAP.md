# Developer Experience Roadmap

本路线聚焦“让游戏开发者更容易正确使用引擎”，暂不扩展 UI 系统。优先消除组合根样板、字符串资产名和难以诊断的装配错误，同时保留底层 RenderPass、Factory 与资源 API 作为高级逃生口。

## 阶段 1：Engine Hosting 与默认 2D 渲染预设（已实现）

目标是把普通游戏入口从手工创建 Window、Shader、Batch、Library、RenderTarget、Pipeline 和 Factory，收敛为声明式启动代码。

计划能力：

- `GameApplicationBuilder`：配置窗口、内容包、初始 Scene 和默认渲染预设。
- `GameApplication`：统一接管 Load、Step、Draw、Resize、Closing 与异常清理。
- `Default2DRendererOptions`：按需启用 HDR/Tone Mapping、Bloom、Stencil 和 SceneGui。
- `Default2DGameContext`：向 Scene 装配回调提供强类型 Scene、Content、Texture、Sprite、Camera 和渲染扩展入口。
- 资源所有权：固定 Builder → Pipeline → Pool → RenderTarget → Content/Library → Batch/Shader 的释放顺序，初始化失败时逆序回滚。
- 高级逃生口：仍允许注册自定义 `IRenderEffectFactory`、根 Surface 和 RenderPass。

验收结果：Runner 已使用 Hosting API，不再维护静态 GPU 字段或窗口回调；默认预设保留 Spotlight、HDR Bloom、Tone Mapping、resize、ESC 和关闭释放行为。配置验证、默认 owner 事件顺序与逆序资源清理已有无窗口测试。

## 阶段 2：强类型 Content 访问（已实现）

从 `assets.json` 或编译产物生成稳定的 C# 标识：

```csharp
GameAssets.Sprites.PlayerIdle
GameAssets.Textures.WorldTiles
GameAssets.Packages.SharedPrimitives
```

目标是把资源拼写错误从运行时提前到编译期。生成器只产生逻辑 `SpriteRef`、`TextureRef` 和 `ContentPackageRef`，不包含 GPU 句柄，也不改变 ContentPackageManager 的生命周期。

验收结果：AssetCompiler 从编译后的 Manifest 依赖图生成确定性 `.g.cs`；已打入 Atlas 的源 Texture 与内部 Atlas 页不会泄漏为公开引用，标识符冲突在构建期失败。Runner 已使用 `GameAssets.Packages.Root` 和 `GameAssets.Sprites.RunnerOrbiting`，Hosting 会校验包 ID。

## 阶段 3：项目模板与命令行体验（已实现）

- `MyGameEngine.GameSdk` 聚合正式运行时程序集并声明第三方运行依赖；源码 Feature 仍保持垂直切片。
- `MyGameEngine.Templates` 提供 `dotnet new mygameengine-game` 最小项目模板。
- 模板默认包含 `Assets/assets.json`、真实 WebP、首个 Scene、实例示例和内容构建配置。
- 隔离式分发测试执行 Pack → 安装模板 → 仓库外创建 → Restore/Build/Run/Publish，并拒绝仓库路径与 `ProjectReference` 泄漏。
- `gameengine doctor` 检查 SDK、包版本、Restore、内容清单与 Build 输出；`--probe-opengl` 显式验证隐藏 OpenGL 3.3 Context。
- 给出 Debug、Release、Publish 的可复制命令。

验收结果：四个分发包共享版本；模板项目只引用 GameSdk 与 ContentPipeline，并通过本地 Tool Manifest 固定 CLI。Build 自动生成强类型 Content，`--smoke` 可隐藏窗口运行三帧并正常释放，Doctor 普通检查与 OpenGL Probe 均在仓库外生成项目通过，Publish 只携带运行时程序集与编译后资产。

## 阶段 4：诊断与可观察性

- 输出 RenderSurface 依赖图、Effect owner、Pass 顺序和 RenderTarget 租约快照。（已实现）
- 为未知资源、缺失 Factory、格式不匹配和依赖循环提供带上下文的诊断。
- 可选帧统计：FPS/UPS、Draw Call、有效 Batch Flush、纹理切换和活跃 Pass。（已实现）
- Texture/Atlas、根目标与 Pool 缓存显存估算，支持高级资源显式补充。（已实现）
- 结构化性能预算、低频 Sink 与 Runner 控制台/JSON Lines 导出。（已实现）
- 诊断 API 默认只读，不改变运行时状态。

当前验收：`Default2DGameContext.CaptureRenderDiagnostics()` 聚合 Pipeline、Builder、Pool 与可选最近帧统计；`FrameRateSettings` 支持启动及运行时 FPS/UPS/VSync 控制。统计默认关闭，开启后以零帧分配值快照记录内置 SpriteBatch 与后处理 Draw Call。`CapturePerformanceSnapshot()` 进一步聚合 Texture/Atlas、根目标、活动及缓存 RT 和自定义资源，预算超限以结构化值交给低频 Sink；Runner 支持控制台与 JSON Lines。

## 阶段 5：开发期热重载

- 优先支持内容包和 Shader 热重载，再考虑代码热重载。
- 失败时继续使用上一份有效资源，不破坏当前 Scene。
- 与 Content 指纹、Atlas 原子替换和 Hosting 生命周期共享同一所有权边界。

当前验收：Content 包已使用编译元数据轮询和去抖，后台完成 Manifest 图校验、图片解码与 Sprite 规范化；Texture/Sprite/包索引在 Step 与 Draw 之间事务切换，失败保留旧修订。自定义 Sprite Shader 支持安全根文件注册、稳定源码快照、整批 Program 原子替换和驱动错误诊断；投影同步覆盖主 Scene 与 Stencil 重绘。类型化材质参数块以逻辑 Shader 引用保存 CPU 参数，支持多材质共享 Program、按 Revision 批处理和热替换后自动重放。材质装配与热重载候选使用 GL 反射验证 Uniform 名称、类型和数组边界；编译诊断保留源码路径、行号、阶段和原始驱动日志。`shaders.json` 已把 Program 文件、Material Schema 和默认值暴露给 Hosting 与 MSBuild；AssetCompiler 在 CoreCompile 前静态校验，并生成 `GameShaders` 下的强类型 Shader、Material 与 Uniform 参数键，运行时继续由真实驱动复核。Runner 提供 `--content-hot-reload` 与 `--shader-hot-reload`。

离线 GLSL 编译仍保留为显式可选方向，但不再占用当前开发体验主线；适配器、诊断、缓存与恢复条件记录在[可选离线 Shader 编译方向](OFFLINE_SHADER_COMPILATION.md)。

## 阶段 6：Gameplay Authoring Experience（当前主线）

目标从“继续完善引擎装配基础设施”转向“减少普通玩法类每天重复编写的样板”。游戏对象应能在不接触 `SceneAggregate`、领域事件回调、可空输入或渲染基础设施的情况下完成常见行为。

- `Position/Rotation/Scale` 与 `MoveBy/RotateBy/ScaleBy` 提供直接变换入口。
- 非空 `Controls`、`KeyDown/KeyPressed/KeyReleased` 和 WASD `InputAxis2D` 收敛输入查询。
- 不可变 `InputMap`、`InputActionRef` 与 `InputAxis2DRef` 已把玩法意图从物理键位解耦；Hosting 集中绑定，Scene 注入现有和后续实例，稳态查询保持 0 B。
- `InputActionBuffer` 与 `GameplayGracePeriod` 提供显式捕获/消费、暂停感知和零分配的预输入与条件宽限，不把跳跃、冷却等玩法规则塞入输入系统。
- `GameplayCooldown` 提供 ready/use/progress/restart/reset 的 owner-local 冷却语义；AirplaneShooter 与 Asteroids 已移除重复的手写浮点计时，并继续继承暂停、时间缩放和 inactive 调度。
- `GameplayHealth`、`GameplayHealthChange` 与 `IHasGameplayHealth` 提供钳制生命值、一次性耗尽/复活转换和 Tag + capability 的伤害调用方式；不把护甲、来源、死亡表现或 RPG 规则固化进 Core。
- 强类型 `GameplayTag` 与 Find/Collision/Area/Radius 对称重载已让横切玩法身份脱离继承树；类型和单 Tag 可组合，Buffer 路径保持 0 B，不提前维护 Tag 索引。
- 轻量 `GameplayBehavior<TInstance>` 提供强类型 Owner、冻结装配、确定性生命周期和暂停感知调度；`LifetimeBehavior` 已替代两个 Playground 的重复子弹 Alarm，稳态分发保持 0 B。
- 技能与 Buff 已完成需求分析：推荐独立 Abilities 切片，以固定 BuffContainer/SkillBook 管理动态 Runtime，先验证 Buff 叠层、来源和安全修改，再实现 Skill 提交与游戏专属 Executor；不提前引入万能 Effect DSL 或通用 RPG 属性系统。详见[技能与 Buff 功能设计思考](SKILLS_AND_BUFFS_DESIGN.md)。
- Scene 注入实例级 `IGameplayContext`，提供 `Spawn/DestroySelf/Destroy/Find`，不引入全局 Service Locator。
- Gameplay Spawn/Destroy 在 End Step 后按请求顺序确定性提交；新实例下一帧 Step，待销毁实例完成当前 End Step。
- `AlarmId`、`SetAlarm/CancelAlarm/OnAlarm` 提供无协程依赖的轻量计时。
- 项目模板使用 WASD 移动、Space 生成 Bullet 和 Alarm 自动销毁展示黄金路径。
- 声明式 Scene 目录、安全帧边界切换和持久实例语义。
- `SceneRef<TArgs>` 把结算/关卡入口参数与目标 Scene 编译期绑定，在请求时复制并由配置函数直接消费。
- 类型安全、构建后冻结的 Instance Factory / Prefab。
- `PrefabRef<T, TArgs>` 与 `in TArgs` 提供不装箱的强类型构造参数。
- Box/Circle Collider，以及按类型的相交、区域和半径查询。
- AirplaneShooter 与 Asteroids 分别验证直线射击和旋转推进/周期生成/重启流程。
- Gameplay Cookbook 收敛常见配方；Release 基准记录 100/1,000/10,000 Collider 线性查询成本。
- `Easing`、`Tween` 与 `Motion` 提供归一化曲线、值/最短弧度角插值、限速追踪和半衰期平滑，不引入全局 Manager。
- SceneAggregate 使用可复用阶段快照和原地稳定排序；Input、Step、Draw、DrawGUI 在实例规模预热后保持 0 B/帧，同时保留阶段间直接变更与 Gameplay 帧边界提交语义。
- `GameplayStateMachine<TState>` 提供强类型 Enter/Step/Exit、状态计时、确定性回调后切换和冲突/循环保护；配置后 Update/Change/Restart 保持零稳态分配。
- `GameplayQueryBuffer<T>`、`CountInstances<T>()` 与 Buffer 查询重载保留便利数组 API，同时给高频路径提供 0 B 结果复用；可选遥测按真实 Step 汇总调用、候选、命中和耗时。

当前验收：无窗口顺序测试覆盖输入边沿、变换、生成可见性、Create/Step/Destroy 顺序、实例查询、DestroySelf、inactive Alarm、Prefab 冻结及参数传递、Collider 组合和 Scene 请求；两个 Playground 冒烟均真实跨 Scene。完整语义见 [Gameplay Authoring Experience](GAMEPLAY_AUTHORING.md)、[Scene、Prefab 与碰撞查询](SCENE_PREFABS_COLLISION.md)和 [Gameplay Cookbook](GAMEPLAY_COOKBOOK.md)。

下一步优先级：多 Camera/Viewport 路线已经闭环；Gameplay Authoring 已具备查询 Buffer、强类型状态机、暂停时间域、Scene 生命周期零分配、Cooldown 以及 Health/Damage。下一项优先评估轻量 Gameplay Signals/消息边界，让命中、击杀和生成等对象间通知不依赖全局事件总线；若实际示例不足以证明需求，则改为小型组合式计数器/资源值，而不提前展开完整 Skill/Buff、UI、协程或物理系统。当前 1,000 Collider 线性扫描约 0.0209 ms/查询，不提前引入 Spatial Hash；逐帧多区域与 Sprite 异形碰撞继续保持需求记录。

## 设计约束

- 不引入全局 Service Locator。
- 默认预设负责常用路径，但不隐藏逻辑 RenderSurface 和资源所有权。
- 可选 Feature 未启用时不创建对应 Shader、Factory 或 RenderTarget。
- Host 不把 GPU 对象注入领域描述符或 GameInstance 状态。
- 所有便捷 API 必须能映射回现有底层 API，避免形成第二套渲染实现。

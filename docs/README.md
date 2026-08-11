# 文档索引

本目录保存 MyGameEngine 的渐进式文档。文档应与可运行代码一起演进：实现新切片时补充对应使用说明，公共 API 或行为变化时同步修改已有文档，而不是等到功能全部完成后集中补写。

## 当前文档

- [Windows x64 Native AOT 发布](NATIVE_AOT_PUBLISHING.md)：AOT 工具链、显式发布命令、自包含目录边界、零 IL 告警和真实 smoke 验收。
- [项目现状](PROJECT_STATUS.md)：当前能力、限制和近期里程碑。
- [Developer Experience Roadmap](DEVELOPER_EXPERIENCE_ROADMAP.md)：Hosting、强类型资产、项目模板、诊断与热重载演进顺序。
- [Gameplay Authoring Experience](GAMEPLAY_AUTHORING.md)：实例变换、输入、Spawn/Destroy/Find、帧边界语义与轻量 Alarm。
- [Spawn/Wave Authoring](SPAWN_WAVE_AUTHORING.md)：确定性延迟/波次时间线、循环、并发门控、状态快照和 Asteroids 用例。
- [Animation Authoring](ANIMATION_AUTHORING.md)：声明式 Clip、强类型引用、Once/Loop/PingPong、GameInstance 播放、帧事件、快照与热重载。
- [Text Rendering 使用指南](TEXT_RENDERING.md)：真实 TTF/OTF、中文/单词多行换行、对齐、Ellipsis、复用 Buffer、Glyph Atlas 及 World/SceneGui DrawText。
- [Audio 短音效黄金路径](AUDIO_RUNTIME.md)：声明式 WAV、OpenAL/Silent 后端、Clip/Bus/Voice、抢占、诊断与资源释放。
- [Scene Graph 与 Transform Hierarchy 设计思考](SCENE_GRAPH_TRANSFORM_HIERARCHY.md)：Local/World 变换、父子挂点、Reparent、生命周期边界、扁平系统索引及与 Yoga UI 树的关系。
- [Transform Hierarchy 创作指南](TRANSFORM_HIERARCHY_AUTHORING.md)：`context.Transforms`、GameInstance Binding、纯挂点、帧边界 Reparent 与销毁语义。
- [缓动、插值与平滑运动](EASING_TWEEN_MOTION.md)：Easing 曲线、有限时长 Tween、最短弧度角和与帧率无关的 Motion。
- [Gameplay 暂停、时间缩放与回溯方向](GAMEPLAY_TIME_CONTROL.md)：无 UI 时间域、Pause owner 生命周期、调度语义及可选快照回溯边界。
- [Scene、Prefab 与碰撞查询](SCENE_PREFABS_COLLISION.md)：声明式 Scene 切换、类型安全实例工厂、Box/Circle 与区域/半径查询。
- [Gameplay Cookbook](GAMEPLAY_COOKBOOK.md)：移动、旋转推进、射击冷却、参数化 Prefab、Alarm、Spawn/Wave、碰撞和 Scene 重启配方。
- [Gameplay Cooldown](GAMEPLAY_COOLDOWN.md)：owner-local 冷却的 ready/use/progress、输入缓冲组合与暂停时间域语义。
- [Gameplay Health 与 Damage](GAMEPLAY_HEALTH.md)：生命钳制、零分配变化结果、一次性耗尽/复活转换和 Tag + capability 组合。
- [强类型 Instance 引用](INSTANCE_REFERENCES.md)：跨帧弱引用、O(1) 解析、类型安全销毁和 Scene 生命周期语义。
- [确定性 Simulation Clock 与 Gameplay Random](DETERMINISTIC_SIMULATION.md)：固定 Tick、暂停/缩放累计时间、PCG32 随机流与回放边界。
- [逻辑输入 Tick 录制与回放](LOGICAL_INPUT_REPLAY.md)：Action/Axis 帧协议、Hosting Record/Replay、兼容性与失败边界。
- [Gameplay 状态 Hash 与首次分叉诊断](GAMEPLAY_STATE_HASHING.md)：显式状态贡献、稳定 contributor、基线录制和回放分叉定位。
- [可持久化 Replay Bundle](REPLAY_BUNDLES.md)：版本化二进制文件、会话式 Hosting API、身份校验、安全读取上限与 Playground 用法。
- [Gameplay 空间查询基准](GAMEPLAY_QUERY_PERFORMANCE.md)：线性扫描测量方法、当前基线和 Spatial Hash 引入条件。
- [Camera 与 Viewport 当前边界](CAMERA_VIEWPORT_STATUS.md)：单主视图现状、底层多视图能力和声明式 View 后续边界。
- [Camera 跟随、Dead Zone、边界与震屏](CAMERA_FOLLOWING.md)：每 View 独立控制器、平滑参数、世界约束和可叠加震屏请求。
- [SceneAggregate 生命周期性能](SCENE_LIFECYCLE_PERFORMANCE.md)：可复用快照、零稳态帧分配、阶段可见性与 Release 基准。
- [Gameplay 强类型状态机](GAMEPLAY_STATE_MACHINE.md)：Enter/Step/Exit、确定性切换、时间域继承和零稳态分配边界。
- [Sprite 碰撞 Authoring 后续需求](SPRITE_COLLISION_REQUIREMENTS.md)：逐帧多区域、Polygon/Alpha Mask、查询分层与动画帧一致性；当前明确延后。
- [可选离线 Shader 编译方向](OFFLINE_SHADER_COMPILATION.md)：暂缓原因、适配边界、诊断缓存与恢复条件。
- [Game SDK 与项目模板](GAME_SDK_AND_TEMPLATES.md)：运行时聚合包、模板安装、仓库外项目与分发验证边界。
- [`gameengine doctor` 开发环境诊断](GAMEENGINE_DOCTOR.md)：项目、包版本、Content 产物、OpenGL Probe 与退出码。
- [运行时渲染诊断与帧统计](RUNTIME_RENDER_DIAGNOSTICS.md)：FPS/UPS 控制、Draw/Flush/Texture/Pass 统计、Surface 图、Effect owner 与 RenderTarget 租约。
- [性能预算与低频遥测](PERFORMANCE_TELEMETRY.md)：Texture/Atlas/RT 显存估算、预算超限、可选 Sink、控制台与 JSON Lines。
- [Content 包开发期热重载](CONTENT_HOT_RELOAD.md)：编译指纹、后台准备、帧边界原子替换、失败回退与拓扑边界。
- [自定义 Sprite Shader 与热重载](SHADER_HOT_RELOAD.md)：文件注册、ShaderRef、投影约定、整批 Program 原子替换和编译失败回退。
- [Shader 材质参数块](SHADER_MATERIALS.md)：MaterialRef、MaterialParameterRef&lt;T&gt;、多材质共享 Shader、Revision 批处理与热替换参数重放。
- [声明式 Shader 与 Material Assets](SHADER_ASSETS.md)：shaders.json、默认参数、Hosting 自动装配、强类型引用生成与热重载边界。
- [Engine Hosting 与默认 2D 启动套件](ENGINE_HOSTING.md)：GameApplication、渲染预设、强类型 Context、帧循环和资源所有权。
- [强类型 Content 引用](STRONGLY_TYPED_CONTENT.md)：GameAssets 生成、Atlas 边界、命名冲突和 MSBuild 配置。
- [动态渲染效果使用指南](DYNAMIC_RENDER_EFFECTS.md)：效果事件、owner 共享、ScenePipelineBuilder、RenderTargetPool 与 Stencil/Bloom 装配。
- [逻辑 RenderSurface 与动态效果依赖图](RENDER_SURFACES.md)：纯逻辑输入输出、根 Surface、稳定拓扑、原子重建与效果串联。
- [显式 Presentation 与 HDR/LDR UI 边界](PRESENTATION.md)：唯一屏幕终端、呈现层级、SceneGui 根 Surface 与生命周期。
- [Bloom 效果使用指南](BLOOM_EFFECT.md)：独立描述符、设置边界、三目标 ping-pong 链、resize 与释放语义。
- [HDR 与 Tone Mapping 使用指南](TONE_MAPPING.md)：RGBA16F Scene/Bloom、曝光、ACES/Reinhard 和显示输出边界。
- [2D 光照、阴影与受光材质渐进路线图](LIGHTING_2D_ROADMAP.md)：颜色空间、每 View Light Buffer、灯光预算、几何硬阴影、投射阴影、Normal/Emission 材质及高级后端进入条件。
- [中文字体、文本绘制与富文本渐进路线图](TEXT_RENDERING_ROADMAP.md)：Unicode/Grapheme、中文 Font/Fallback、Glyph Atlas、富文本、彩色文字、打字机、Emoji、内联动画和 IME。
- [FairyGUI 可选集成渐进路线图](FAIRYGUI_INTEGRATION_ROADMAP.md)：MonoGame Runtime 兼容性验证、Package/Render/Input Adapter、强类型绑定、中文富文本与产品化退出条件。
- [HTML/CSS、Yoga 与游戏 GUI 兼容性 Spike](HTML_CSS_YOGA_GUI_ROADMAP.md)：Yoga、RmlUi、FairyGUI、浏览器内核的适配面、风险和 Go/No-Go 指标。
- [StencilMask 分组与几何](STENCIL_MASK_GEOMETRY.md)：显式组、多 owner/批量管理、Circle、Sprite Alpha、性能与呈现边界。
- [Cooldown UI Effects 需求记录](COOLDOWN_UI_EFFECTS_REQUIREMENTS.md)：圆形、环形、圆角矩形、弧形与 Sprite Alpha 蒙灰倒计时边界。
- [GPU 像素回归测试](VISUAL_REGRESSION.md)：固定时间步截图、PNG 基线、容差、差异产物和场景扩展方式。
- [Content Assets 使用指南](CONTENT_ASSETS.md)：声明式 Texture/Sprite 包、依赖、多图片动画和资源生命周期。
- [离线 Texture Atlas 使用指南](TEXTURE_ATLAS.md)：Atlas 构建配置、AssetCompiler、多页、大帧旁路与编译产物。
- [可分发内容工具链](CONTENT_PIPELINE_PACKAGES.md)：AssetCompiler .NET Tool、ContentPipeline NuGet 包、本地 Feed 和外部项目接入。
- [C# 2D 游戏引擎从零构建](C%23%202D%20游戏引擎从零构建.md)：长期架构与路线推演原稿；其中示例不保证都已实现。

## 游戏设计档案

- [《天天泡泡TA / BubbleTa》](../games/BubbleTa/README.md)：Gate 0 已完成首个真实 HomeScene，具备旧首页内容装配、确定性动画和 WorldMap 占位切换；核心泡泡玩法尚未开始。
- [《驮春 / Bloomback》](../games/Bloomback/README.md)：尚未立项的迁徙巨兽与生态花园概念档案。
- [《神意难测 / The God They Made》](../games/TheGodTheyMade/README.md)：Gate 4 工程切片已完成；具备 30 分钟三谜题、葬礼选择、结局壁画、灰盒视听与确定性回归，等待外部盲测关闭 Gate。

## 更新约定

Gameplay 协作补充：[Scene 作用域 Gameplay Signals](GAMEPLAY_SIGNALS.md)：结构体通知、构造期监听、确定性投递、暂停/销毁语义与 Asteroids 一对多样例。

每次功能切片发生以下变化时，应在同一提交中更新相关文档：

1. 新增或修改公共 API。
2. 新增清单格式、配置字段或固定验证规则。
3. 改变资源所有权、加载顺序或释放顺序。
4. 增加新的限制、性能边界或失败模式。
5. 完成原计划中的能力，或调整下一阶段边界。

文档以当前代码行为为准；长期设计文档只提供方向，不能覆盖实际实现和测试结果。

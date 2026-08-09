# 文档索引

本目录保存 MyGameEngine 的渐进式文档。文档应与可运行代码一起演进：实现新切片时补充对应使用说明，公共 API 或行为变化时同步修改已有文档，而不是等到功能全部完成后集中补写。

## 当前文档

- [项目现状](PROJECT_STATUS.md)：当前能力、限制和近期里程碑。
- [Developer Experience Roadmap](DEVELOPER_EXPERIENCE_ROADMAP.md)：Hosting、强类型资产、项目模板、诊断与热重载演进顺序。
- [Gameplay Authoring Experience](GAMEPLAY_AUTHORING.md)：实例变换、输入、Spawn/Destroy/Find、帧边界语义与轻量 Alarm。
- [缓动、插值与平滑运动](EASING_TWEEN_MOTION.md)：Easing 曲线、有限时长 Tween、最短弧度角和与帧率无关的 Motion。
- [Gameplay 暂停、时间缩放与回溯方向](GAMEPLAY_TIME_CONTROL.md)：无 UI 时间域、Pause owner 生命周期、调度语义及可选快照回溯边界。
- [Scene、Prefab 与碰撞查询](SCENE_PREFABS_COLLISION.md)：声明式 Scene 切换、类型安全实例工厂、Box/Circle 与区域/半径查询。
- [Gameplay Cookbook](GAMEPLAY_COOKBOOK.md)：移动、旋转推进、射击冷却、参数化 Prefab、Alarm、碰撞和 Scene 重启配方。
- [Gameplay 空间查询基准](GAMEPLAY_QUERY_PERFORMANCE.md)：线性扫描测量方法、当前基线和 Spatial Hash 引入条件。
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
- [StencilMask 分组与几何](STENCIL_MASK_GEOMETRY.md)：显式组、多 owner/批量管理、Circle、Sprite Alpha、性能与呈现边界。
- [Cooldown UI Effects 需求记录](COOLDOWN_UI_EFFECTS_REQUIREMENTS.md)：圆形、环形、圆角矩形、弧形与 Sprite Alpha 蒙灰倒计时边界。
- [GPU 像素回归测试](VISUAL_REGRESSION.md)：固定时间步截图、PNG 基线、容差、差异产物和场景扩展方式。
- [Content Assets 使用指南](CONTENT_ASSETS.md)：声明式 Texture/Sprite 包、依赖、多图片动画和资源生命周期。
- [离线 Texture Atlas 使用指南](TEXTURE_ATLAS.md)：Atlas 构建配置、AssetCompiler、多页、大帧旁路与编译产物。
- [可分发内容工具链](CONTENT_PIPELINE_PACKAGES.md)：AssetCompiler .NET Tool、ContentPipeline NuGet 包、本地 Feed 和外部项目接入。
- [C# 2D 游戏引擎从零构建](C%23%202D%20游戏引擎从零构建.md)：长期架构与路线推演原稿；其中示例不保证都已实现。

## 更新约定

每次功能切片发生以下变化时，应在同一提交中更新相关文档：

1. 新增或修改公共 API。
2. 新增清单格式、配置字段或固定验证规则。
3. 改变资源所有权、加载顺序或释放顺序。
4. 增加新的限制、性能边界或失败模式。
5. 完成原计划中的能力，或调整下一阶段边界。

文档以当前代码行为为准；长期设计文档只提供方向，不能覆盖实际实现和测试结果。

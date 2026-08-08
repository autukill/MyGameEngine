# 文档索引

本目录保存 MyGameEngine 的渐进式文档。文档应与可运行代码一起演进：实现新切片时补充对应使用说明，公共 API 或行为变化时同步修改已有文档，而不是等到功能全部完成后集中补写。

## 当前文档

- [项目现状](PROJECT_STATUS.md)：当前能力、限制和近期里程碑。
- [动态渲染效果使用指南](DYNAMIC_RENDER_EFFECTS.md)：效果事件、owner 共享、ScenePipelineBuilder、RenderTargetPool 与 Stencil/Bloom 装配。
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

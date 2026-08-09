# 可选离线 Shader 编译方向（已记录，暂缓实施）

离线 Shader 编译仍是有效方向，但当前不作为 Developer Experience 主线。现阶段游戏开发者更频繁面对的是移动、输入、实例生成/销毁、计时和 Scene 组织；因此先完善 Gameplay Authoring，等真实项目出现 CI Shader 校验、较大 Shader 集合或跨团队协作需求后再恢复本方向。

## 目标

- 在没有窗口和 OpenGL Context 的 Build/CI 中尽早发现 GLSL 语法、阶段和链接问题。
- 复用 `shaders.json`、安全路径、Material Schema 与现有结构化 Shader 诊断。
- 支持确定性缓存，使未变化的源码和编译配置不重复执行外部工具。
- 保留运行时真实驱动的编译、链接与 Active Uniform 复核。

## 固定边界

- 离线编译器必须显式启用；默认 Build 不下载、不安装也不探测外部工具。
- 外部编译器通过适配接口或可配置可执行文件接入，不把具体供应商绑定进 Core/Domain。
- 工具不可用时：显式启用模式应给出清晰错误；未启用模式保持现有静态清单校验。
- 不使用源码正则伪装 GLSL 编译或 Uniform 反射。
- 离线成功不代表所有 OpenGL 3.3 驱动都接受该 Program，运行时仍是最终复核者。
- v1 不改变 `ShaderRef`、`MaterialRef`、`GameShaders` 或 Shader 热替换生命周期。

## 建议接口

后续可在 ShaderAssets/AssetCompiler 边界增加：

```csharp
public interface IOfflineShaderCompiler
{
    OfflineShaderCompilerInfo Probe();
    OfflineShaderCompilationResult Compile(
        OfflineShaderCompilationRequest request);
}
```

请求包含清单中的逻辑 Shader 名、阶段、绝对源码路径、入口点和目标环境；结果只包含成功状态、结构化诊断、工具身份与可缓存指纹，不包含运行时 GL Handle。

MSBuild 建议使用显式属性：

```xml
<GameEngineShaderOfflineValidation>true</GameEngineShaderOfflineValidation>
<GameEngineShaderCompilerPath>...</GameEngineShaderCompilerPath>
```

不应把某个机器上的绝对工具路径写入生成代码、NuGet 包或内容指纹；指纹只记录规范化工具身份/版本、源码内容和编译选项。

## 诊断与缓存

- 将外部工具的文件、行、列、阶段、严重级别和原始消息映射到现有 Shader 诊断模型。
- 无法解析的输出仍保留原始日志，不能静默丢失。
- 缓存键至少包含：清单 Schema、顶点/片元源码 SHA-256、工具身份、工具版本、目标环境和编译选项。
- 任一 Shader 失败时 Build 失败，但不得覆盖上一份有效运行时资产或生成引用。
- CI 可选择将诊断输出为文本和机器可读 JSON；本地默认保持简洁摘要。

## 恢复实施的触发条件

满足下列任一实际需求时重新排期：

- 项目 Shader 数量增长，运行时逐个发现错误明显拖慢开发。
- 无显示器 CI 必须在启动 GPU 测试前完成 GLSL 预检。
- 多人协作频繁出现 Shader/Material 契约漂移。
- 需要为多个目标平台建立可审计的 Shader 兼容性报告。

建议实施顺序：适配接口与假实现测试 → 单个显式外部工具适配器 → AssetCompiler/MSBuild 可选开关 → 缓存与结构化诊断 → 外部分发验证。当前不进入具体工具选型。

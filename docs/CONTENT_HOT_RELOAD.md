# Content 包开发期热重载

Content 热重载面向开发运行，不监测源图片，也不会在游戏进程中调用 AssetCompiler。它只观察编译器已经完整发布到 `AssetsCompiled` 的修订，把构建与运行时资源替换分成两个清晰边界。

## Compiler 与 Runtime 的修订契约

AssetCompiler 在权威输出目录写入 `.mygame-assets.json`，其中最重要的字段是：

- `owner/schemaVersion`：证明目录由兼容的 MyGameEngine Compiler 拥有。
- `compilerVersion`：编译算法版本变化时使旧缓存失效。
- `rootPackageId/rootManifest`：防止 Runtime 把别的项目输出当作当前包。
- `inputFingerprint`：完整依赖图修订的稳定 SHA-256 身份。
- 每个输出文件与 Package 的 Hash/统计：供编译器验证缓存，不直接暴露给 Gameplay。

Compiler 先在同级临时目录生成完整输出，校验并写入元数据，再以目录移动替换权威 `CompiledAssets`。MSBuild 从权威目录复制到 `bin/.../AssetsCompiled` 时，普通资源先复制，`.mygame-assets.json` 最后复制。Hot Reload 只观察这份最后出现的提交标记，因此不会把正在逐文件复制的中间状态当成新修订。

`CompiledContentRevisionReader` 不重新 Hash 大文件；它验证元数据 owner/schema/root 身份并读取 Compiler 已发布的 Fingerprint。这让每次轮询保持低成本，而完整性责任仍位于发布修订的 Compiler 边界。

## 开启方式

```csharp
renderer
    .UseContent(GameAssets.Packages.Root)
    .EnableContentHotReload(new ContentHotReloadOptions(
        sink,
        pollInterval: TimeSpan.FromMilliseconds(250),
        debounce: TimeSpan.FromMilliseconds(250)));
```

Runner 可直接使用：

```powershell
dotnet run --project src/MyGame.Runner -- --content-hot-reload
```

运行期间修改 `Assets` 后，在另一个终端执行项目 Build。MSBuild 会调用 AssetCompiler，把完整修订写入 `obj/.../CompiledAssets`，再把资源复制到运行进程读取的 `bin/.../AssetsCompiled`。`.mygame-assets.json` 最后复制，作为新修订可见的提交标记。

```powershell
dotnet build src/MyGame.Runner/MyGame.Runner.csproj
```

## Coordinator 状态机

`ContentHotReloadCoordinator.Tick` 位于 Hosting 的 Step 尾部、Draw 之前。它不是每帧扫描所有内容，而是维护一个小状态机：

```text
Active revision
  → 到达 PollInterval 后读取提交标记
  → 新 Fingerprint 成为 Candidate，并发布 Detected
  → Candidate 在 Debounce 内保持不变
  → 后台 PrepareReloadAsync
  → Step 边界 CommitReload
  → Applied，或记录 Failed Fingerprint
```

- 正在准备时不会并发启动第二份 Prepare。
- 同一个失败 Fingerprint 不会每帧重试；只有 Compiler 发布不同 Fingerprint 后才再次尝试。
- Candidate 在 debounce 期间变化会重新计时，避免连续 Build 造成无意义解码。
- Dispose 会取消后台准备；诊断 Sink 只接收结构化状态，不参与资源所有权。

## 两阶段替换

```text
AssetCompiler 完整发布
  → 指纹轮询与去抖
  → 后台解析完整 Manifest 依赖图
  → 后台解码全部新图片并规范化 Sprite 帧与 Animation Clip
  → Step 完成
  → GPU 上传暂不可见的新 Texture
  → 激活 Texture 映射
  → 校验并激活 Sprite 映射
  → 校验并激活 Animation 映射
  → 更新包资源索引
  → Draw 使用新修订
  → 释放旧 GPU Texture
```

后台准备阶段不调用 OpenGL，也不修改 `TextureLibrary`、`SpriteLibrary`、`AnimationLibrary` 或包引用计数。提交固定发生在 `ScenePipelineBuilder.ApplyEvents` 之后、`RenderPipeline.Execute` 之前，因此一帧不会混用两个内容修订。

### Prepare 阶段为何安全

开始准备前，Manager 捕获当前加载图的 `revisionGeneration`、包 ID/Manifest/直接依赖以及各包拥有的 Texture/Sprite/Animation 名称。后台线程随后：

1. 在读取前确认提交标记仍等于目标 Revision。
2. 重新读取完整 Manifest 图并验证 ID、路径、循环与拓扑未变。
3. 按依赖优先顺序解码所有新图片为 CPU RGBA8。
4. 仅用逻辑元数据规范化 Sprite 帧和 Animation 定义，不触碰当前 Library。
5. 准备结束后再次读取提交标记；若 Fingerprint 已变化，整份结果作废。

`PreparedContentPackageReload` 携带 owner、目标包、基础 generation 和一次性 consumed 标记。它不能交给另一个 Manager、不能重复提交，也不能在加载图已经 Load/Dispose 变化后提交，从而避免后台旧结果覆盖更新状态。

### Commit 阶段为何近似原子

图形线程依次创建 Texture、Sprite、Animation Replacement Transaction：新 Texture 先上传但尚未成为最终修订，然后各逻辑映射 Activate，最后更新每个 `PackageState` 的资源索引。全部成功后按 Animation → Sprite → Texture 提交 Transaction 并增加 generation；任何异常都会恢复旧 PackageState，未提交 Transaction 在 Dispose 时恢复旧映射并释放新 GPU 资源。

这个顺序保证 Sprite 永远只引用已准备的 Texture，Animation 永远只引用已准备的 Sprite。逻辑 Ref 名称不变，所以现有 `GameInstance` 和 AnimationPlayer 无需重建；删除的名称则在同一提交边界消失。

`TextureRef`、`SpriteRef`、`AnimationClipRef` 和已有 `GameInstance.Sprite` 都是逻辑名称。资源仍存在时，提交后这些引用自动解析到新 GPU Handle、UV、尺寸、原点、帧数、FPS 和 Clip 定义，不需要重建实例。活动 `AnimationPlayer` 会在下一次 Update 采用替换定义；若 Clip 被删除则安全停止。

## 失败回退

下列任一步失败时，当前 Scene 继续使用上一份有效修订：

- 编译元数据缺失、尚在复制或与入口包不匹配。
- Manifest、依赖、路径或资源名称验证失败。
- PNG/WebP 解码失败，帧矩形越界或帧尺寸不一致。
- GPU 上传失败，或新名称与替换范围之外的已加载资源冲突。
- 准备期间又发布了更新指纹。
- Prepare 之后发生了包 Load/Dispose，使 Manager generation 与快照不一致。

`IContentHotReloadSink` 接收 `Detected`、`Applied`、`Failed` 结构化诊断，包括包 ID、指纹、耗时和错误。相同失败指纹不会每帧重复尝试；源内容再次构建出新指纹后才重试。

## v1 拓扑边界

当前修订可以：

- 修改、新增或删除现有依赖图内的 Texture、Sprite 与 Animation。
- 修改图片像素、尺寸、采样、Sprite 帧、原点、逻辑尺寸、Animation 帧序列、FPS、Loop 和 Marker。
- 更新传递依赖包内部的内容；共享该依赖的其他已加载根包会看到同一逻辑资源更新。

当前不能在运行中新增、删除或重定向包依赖边。包 ID、Manifest 路径和依赖拓扑发生变化时，本次修订被拒绝。重新启动游戏即可加载新拓扑。后续若开放拓扑热替换，需要先把包引用计数拆分为“外部租约”和“依赖持有”，避免共享依赖被提前卸载。

Audio Clip、TileSet 和 TileMap 的首版也不参与热替换；只要目标依赖图含这些资源，Prepare 会明确拒绝并要求重启。原因不是 Manifest 无法读取，而是它们尚未实现与 Texture/Sprite/Animation 同等级的 Replacement Transaction 和活动运行时迁移语义。

## 性能与使用建议

- 轮询读取很小的元数据文件，不使用 `FileSystemWatcher`。编译器会原子替换目录，轮询提交标记在 Windows 与跨平台环境中更稳定。
- 每次修订会全量解码目标依赖图的图片；它是开发便利功能，不应在正式运行中默认开启。
- GPU 峰值会短暂包含旧、新两套 Texture。新修订完全激活后才释放旧 Handle。
- 频繁编辑大图片时可提高 debounce，避免连续构建触发无意义准备。
- 热重载不保留 CPU 像素缓存，也不改变 Atlas 的运行时边界；它消费 AssetCompiler 已生成的 Atlas 页与旁路大纹理。

## 实现组件对应关系

| 组件 | 职责 |
|---|---|
| `ContentBuildPipeline` | 发布完整编译目录、Fingerprint 和 `.mygame-assets.json`。 |
| `CompiledContentRevisionReader` | 低成本读取并验证提交标记身份。 |
| `ContentHotReloadCoordinator` | Poll、Debounce、失败抑制、后台任务和诊断。 |
| `ContentPackageManager.PrepareReloadAsync` | 捕获快照、后台读图/解码/规范化并验证修订未撕裂。 |
| `ContentPackageManager.CommitReload` | 图形线程 Replacement Transaction 与 PackageState 原子切换。 |
| Texture/Sprite/Animation Library | 提供逻辑名称稳定、旧映射回滚和旧资源延迟释放的事务原语。 |

核心代码位于 [`ContentHotReload.cs`](../src/Engine.Hosting/ContentHotReload.cs)、[`ContentPackageManager.Reload.cs`](../src/Engine.Features/ContentAssets/Infrastructure/ContentPackageManager.Reload.cs) 和 [`CompiledContentRevisionReader.cs`](../src/Engine.Features/ContentAssets/Infrastructure/CompiledContentRevisionReader.cs)。

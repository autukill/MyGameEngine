# Content 包开发期热重载

Content 热重载面向开发运行，不监测源图片，也不会在游戏进程中调用 AssetCompiler。它只观察编译器已经完整发布到 `AssetsCompiled` 的修订，把构建与运行时资源替换分成两个清晰边界。

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

`TextureRef`、`SpriteRef`、`AnimationClipRef` 和已有 `GameInstance.Sprite` 都是逻辑名称。资源仍存在时，提交后这些引用自动解析到新 GPU Handle、UV、尺寸、原点、帧数、FPS 和 Clip 定义，不需要重建实例。活动 `AnimationPlayer` 会在下一次 Update 采用替换定义；若 Clip 被删除则安全停止。

## 失败回退

下列任一步失败时，当前 Scene 继续使用上一份有效修订：

- 编译元数据缺失、尚在复制或与入口包不匹配。
- Manifest、依赖、路径或资源名称验证失败。
- PNG/WebP 解码失败，帧矩形越界或帧尺寸不一致。
- GPU 上传失败，或新名称与替换范围之外的已加载资源冲突。
- 准备期间又发布了更新指纹。

`IContentHotReloadSink` 接收 `Detected`、`Applied`、`Failed` 结构化诊断，包括包 ID、指纹、耗时和错误。相同失败指纹不会每帧重复尝试；源内容再次构建出新指纹后才重试。

## v1 拓扑边界

当前修订可以：

- 修改、新增或删除现有依赖图内的 Texture、Sprite 与 Animation。
- 修改图片像素、尺寸、采样、Sprite 帧、原点、逻辑尺寸、Animation 帧序列、FPS、Loop 和 Marker。
- 更新传递依赖包内部的内容；共享该依赖的其他已加载根包会看到同一逻辑资源更新。

当前不能在运行中新增、删除或重定向包依赖边。包 ID、Manifest 路径和依赖拓扑发生变化时，本次修订被拒绝。重新启动游戏即可加载新拓扑。后续若开放拓扑热替换，需要先把包引用计数拆分为“外部租约”和“依赖持有”，避免共享依赖被提前卸载。

## 性能与使用建议

- 轮询读取很小的元数据文件，不使用 `FileSystemWatcher`。编译器会原子替换目录，轮询提交标记在 Windows 与跨平台环境中更稳定。
- 每次修订会全量解码目标依赖图的图片；它是开发便利功能，不应在正式运行中默认开启。
- GPU 峰值会短暂包含旧、新两套 Texture。新修订完全激活后才释放旧 Handle。
- 频繁编辑大图片时可提高 debounce，避免连续构建触发无意义准备。
- 热重载不保留 CPU 像素缓存，也不改变 Atlas 的运行时边界；它消费 AssetCompiler 已生成的 Atlas 页与旁路大纹理。

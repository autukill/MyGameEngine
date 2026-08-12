# BubbleTa.HomeScene 实现说明

`BubbleTa.HomeScene` 是 2015 年 GameMaker Studio 1 `rm_ini` 的首个可运行重建场景。目标是验证 MyGameEngine 能否承载一个真实旧项目的内容装配、相机坐标、周期动画、确定性装饰和场景切换，而不是重新设计首页或开始核心泡泡玩法。

## 运行结果

- 窗口初始为 720×1280，游戏固定更新为 60 Hz。
- Scene 保留旧 Room 的 960×1280 世界坐标；相机从 `(120, 0)` 观察中央 720×1280 区域。
- 背景、12 片 Logo、泡泡、云、三名角色、三颗大星、五个闪点和五条流星均由 Scene 实例装配。
- 世界按钮使用 Sprite 逻辑边界和屏幕到世界坐标转换；只有同一指针在按钮内按下并在按钮内释放才切换场景。
- WorldMap 当前只是纯色占位场景，用来验证 `Home → WorldMap → Home` 生命周期，不代表 `rm_world` 已经迁移。
- Home 在 Scene 注册期声明固定 Camera，不创建 Navigation Controller；WorldMap 占位 Scene 独立声明纵向 Drag/Decelerate/Bounce。切换时 Hosting 重置 Camera、清理 Pointer 捕获和惯性，不再依赖 Scene 回调手工覆盖 Renderer 全局状态。
- Home 进入时通过 `SceneAudio.PlayMusic` 循环播放流式 OGG；离开 Scene 时 Hosting 会在卸载 Home 内容包之前停止音乐。
- 世界按钮仅在一次有效的内部按下/内部释放后播放 WAV 点击音并切换 Scene。点击音使用全局一次性 Voice，让已经上传的静态 OpenAL Buffer 跨过切换边界自然播放完；Clip 从内容库移除后，Backend 会在 Voice 完成时释放 Buffer。
- 设置图标仍只展示。存档初始化和旧全局控制对象均未进入本切片。

## 内容管线

顶层 `src/BubbleTa.Game/Assets/assets.json` 是稳定的显式聚合根，只依赖 `Home/assets.json`；Home 子清单独立拥有首页的 Texture、Sprite 和 Atlas 配置。新增场景时只需给聚合根增加一条依赖，不再改动 Home 清单。构建时 Content Compiler 会：

1. 严格解析 Texture 与 Sprite 声明；
2. 把 32 张无损 WebP 以 `smooth` 采样打入最多 2048×2048 的无损 WebP Atlas 页面；
3. 校验并复制流式 OGG BGM 与预解码 WAV 点击音；
4. 生成运行时内容包和强类型 `GameAssets` 常量；
5. 将女主九帧光效注册为约 9.2 FPS 的多帧 Sprite。

运行时代码只使用 `SpriteRef` 和生成的资源名称，不读取旧 GameMaker 工程，也不持有源 WebP 路径。BubbleTa 现通过 `UseContentCatalog()` 与带包参数的 `AddScene` 声明 Home 租约：配置 Home 前加载 `bubbleta.home`，离开 Home 后释放其 Sprite 与两页 Atlas，返回时重新装配。顶层聚合根仍负责离线编译与强类型引用生成，但不再作为运行期常驻租约。详细语义见 [Scene 级 Content 生命周期](../../../docs/SCENE_CONTENT_LIFECYCLE.md)。

迁移时每张 WebP 都以 libwebp lossless + exact 模式编码，并重新解码验证完整 RGBA 与原 PNG 逐像素相同，包括 alpha 为 0 的隐藏 RGB。源图片由 991,320 bytes 降到 577,636 bytes；两张编译 Atlas 页面由 1,052,090 bytes 降到 639,206 bytes。WebP 只减少仓库和发布包体积，上传 GPU 后仍解码为 RGBA8，不减少显存。

## 时间与动画迁移

旧 `rm_ini` 以 46 FPS 表达 Alarm 和逐 Step 速度。新实现不模拟旧帧率，而是把时间换算为秒，并在 60 Hz 固定更新中推进：

- 旧 Alarm 延迟使用 `旧帧数 ÷ 46` 秒；
- 流星速度 `40 px/step` 换算为 `1840 px/s`；
- Logo 和角色入场使用现有 `Tween`/`Easing`，没有引入全局 Tween Manager；
- 周期浮动、缩放和淡出保留在各实例的 elapsed/phase 状态中，稳定阶段不创建逐帧对象；
- 所有首页装饰使用 `InstanceTimeMode.Unscaled`，因此不受未来 Gameplay 暂停影响。

集中式 `HomeSceneLayout` 保存旧位置、缩放、Depth、入场延迟和固定随机种子，实例类不再散落 Room 魔法数字。

## 确定性随机装饰

五条流星和五个闪点分别按索引从固定根种子派生 `GameplayRandom`。同一构建、相同更新序列会得到完全一致的首次等待和后续周期：

- 流星首次等待 1–6 秒，之后每 3–6 秒回到固定出生点；
- 闪点每 3–6 秒开始一次 0.4 秒线性淡出，并在 1 秒周期结束时恢复；
- 每个实例独立持有随机状态，不依赖全局随机源或绘制次数。

这使无窗口测试和隐藏窗口 smoke 可以复现首页表现，同时保留视觉上的伪随机感。

## 测试边界

`BubbleTa.Game.Tests` 通过 `InternalsVisibleTo` 检查游戏内部表现状态，不把首页类提升成引擎公共 API。测试覆盖 Logo 时序、周期装饰、角色入场、流星与闪点确定性、按钮捕获语义和 ESC 回调。

`--smoke` 使用隐藏窗口、Silent Audio Backend 和固定推进，验证编译内容包及两类 Audio Clip 可装配、Home 实例建立、动画进入稳定阶段、切换到 WorldMap 并自动关闭。人工检查仍用于确认最终图层、中央裁切、音乐循环、点击反馈和美术观感。

## 资产发布 Gate

`Assets/Home` 中的 32 张 WebP 由用户自己的 2015 年旧工程 PNG 无损转码而来；BGM 和点击音由旧 MP3 分别转为 OGG 与 WAV。它们仅用于内部重建原型。格式转换不会改变许可状态；进入仓库不等于已经获得公开发布或商业分发许可。MP3 转 OGG 还是有损到有损的转码，正式版本应优先取得无损母带或替换音乐。

任何公开演示、安装包或商店发布之前，必须完成逐项来源、作者、授权范围、可修改性和再分发权限审计；无法确认的资源必须替换。详细清单边界见 `src/BubbleTa.Game/Assets/ASSET_PROVENANCE.md`。

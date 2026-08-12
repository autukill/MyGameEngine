# BubbleTa 原型资产来源

`Home/` 中的 32 张 WebP 由用户 2015 年 GameMaker Studio 1 工程中 `rm_ini` 使用的 PNG 背景和 Sprite 帧无损转码而来；`Home/Audio` 中的 BGM 和点击音由同一旧工程的 MP3 转码而来。`WorldMap/` 中的 53 张 WebP 来自旧 `rm_world` 第一岛屿上下图、上下云层帧、关卡节点图和景观装饰帧；`WorldMap/Audio/world-bgm.ogg` 来自旧 `snd_bgm_world.mp3`。所有这些文件仅用于 MyGameEngine 内部重建原型。图片转换使用 libwebp lossless + exact，并通过重新解码确认完整 RGBA 与原图逐像素相同。

当前状态：`legacy-project / provenance-review-required`。

- 已迁移：首页背景、世界入口、设置入口、流星、闪点、星星、泡泡、云、三名角色、女主九帧特效、十二片 Logo、首页 BGM 和点击音效；世界地图第一岛屿上下主体、两组四帧云、四种关卡节点图、烟雾、石像、蘑菇、三名人物、六帧飞鸟、水边人物、二十五帧鱼群、苹果与 WorldMap BGM。
- 未迁移：GML、GameMaker Object、第三方 UI 组件、扩展、SDK、支付代码及其他 Scene 的音频。
- Home BGM 源为 `snd_bgm_home.mp3`（SHA-256 `FAB22C389A596EBA3AE1E2EAF0375E7D8BF513156D258297A28DEB1C424A9930`），转为流式 `Home/Audio/home-bgm.ogg`；WorldMap BGM 源为 `snd_bgm_world.mp3`（SHA-256 `3E1A851C5B5937EB1375D90049BC175B28C22F9310682E6DA588C8731381F5D8`），转为流式 `WorldMap/Audio/world-bgm.ogg`；点击源为 `snd_click.MP3`（SHA-256 `75DC4FA32BB33014DA38E8B0CE0CD9751B9D0C6A2D1AB90C3E519B55CD50981B`），解码为 PCM16 `Home/Audio/click.wav`。
- MP3 → OGG 是有损转码，当前 OGG 仅服务内部原型；正式版本优先取得原始无损母带，否则必须替换。WAV 保存旧 MP3 的解码结果，不能恢复 MP3 编码前已经损失的信息。
- 在逐项确认创作者、委托合同、购买许可或其他再发布依据前，这些图片和声音不能被视为可公开分发资产。
- Content Compiler 对图片的打包、重命名、Atlas 化或对声音的格式转换不会改变其许可状态。

正式发布 Gate 必须为每一项资产补充来源、权利人、许可文本和可发布平台范围；来源不明的图片或声音必须替换。

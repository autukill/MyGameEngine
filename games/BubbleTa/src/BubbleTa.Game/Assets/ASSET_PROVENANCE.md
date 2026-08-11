# BubbleTa 原型资产来源

`Home/` 中的 32 张 WebP 由用户 2015 年 GameMaker Studio 1 工程中 `rm_ini` 使用的 PNG 背景和 Sprite 帧无损转码而来，仅用于 MyGameEngine 内部重建原型。转换使用 libwebp lossless + exact，并通过重新解码确认完整 RGBA 与原图逐像素相同。

当前状态：`legacy-project / provenance-review-required`。

- 已迁移：首页背景、世界入口、设置入口、流星、闪点、星星、泡泡、云、三名角色、女主九帧特效和十二片 Logo。
- 未迁移：GML、GameMaker Object、第三方 UI 组件、扩展、SDK、支付代码、首页 MP3 BGM 和点击音效。
- 在逐项确认创作者、委托合同、购买许可或其他再发布依据前，这些图片不能被视为可公开分发资产。
- Content Compiler 对图片的打包、重命名或 Atlas 化不会改变其许可状态。

正式发布 Gate 必须为每一项资产补充来源、权利人、许可文本和可发布平台范围；来源不明的图片必须替换。

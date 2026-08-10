# 可持久化 Replay Bundle

Replay Bundle 把“玩家每个固定 Tick 的逻辑输入”和“该 Tick 提交后的 Gameplay 状态 Hash”保存为一个 `.mgreplay` 文件。它用于复现缺陷、保存回归用例和定位首次确定性分叉，不是录像、存档或状态快照。

## 当前真实能力与定位

Replay v1 已经能够承担“同一游戏构建下，可保存、可共享、可自动验证的确定性玩法复现”：

- 录制移动、射击、暂停等逻辑 Action/Axis，保存短时 Gameplay Bug 的精确操作序列。
- 在另一位开发者或自动化环境中加载同一个文件；GameId、BuildId、fixed delta 或逻辑输入协议不兼容时在运行前拒绝。
- 允许回放构建更换物理键位，因为协议记录玩法意图，不记录 Space、WASD 等设备绑定。
- 逐 Tick 验证 Scene、实例内建状态与 `OnWriteGameplayState` 自定义贡献，在首次分叉处报告 Tick、expected/actual Hash 和首个不同 contributor。
- 覆盖 Scene 切换、Spawn/Destroy、暂停/恢复、Cooldown、Health、GameplayRandom、动画状态及游戏显式声明的私有状态。
- 回放最后一个 Tick 成功后自动退出，可作为 CI 冒烟、缺陷回归或仓库内 Replay 样本。
- 通过 Stream 接口接入内存夹具、文件或游戏自有存储，并以受限读取、SHA-256 和原子文件替换保护开发期基线。

Asteroids 已真实验证“隐藏窗口启动 → 录制 → 暂停/恢复 → Scene 切换 → 磁盘保存 → 重新加载 → 逐 Tick 状态验证 → 自动退出”的完整链路。

因此当前准确定位是：**面向开发和测试的确定性 Gameplay Bug 复现与首次分叉诊断工具**。它不是玩家录像、游戏存档、时间回溯或网络 Rollback。这个里程碑现已闭环；除非长会话调试产生明确的中途跳转需求，否则不继续建设 Checkpoint、压缩或状态恢复。

## 推荐用法

录制时创建一个会话，并在 `GameApplication.Run()` 返回后保存：

```csharp
var identity = new ReplayIdentity("my-game", "debug-2026-08-10");
ReplaySession replay = ReplaySession.Record(identity);

using var game = GameApplication
    .Create(EngineWindowOptions.Default.WithFixedUpdateRate(60d))
    .ConfigureInput(ConfigureInputs)
    .UseReplayRecording(replay)
    .UseDefault2DRenderer()
    .ConfigureScene("Game", ConfigureGame)
    .Build();

game.Run();
replay.Save("failure.mgreplay");
```

回放时必须提供期望的游戏与构建身份：

```csharp
ReplaySession replay = ReplaySession.Load("failure.mgreplay", identity);

using var game = GameApplication
    .Create(EngineWindowOptions.Default.WithFixedUpdateRate(60d))
    .ConfigureInput(ConfigureInputs)
    .UseReplayPlayback(replay)
    .UseDefault2DRenderer()
    .ConfigureScene("Game", ConfigureGame)
    .Build();

game.Run();
```

`UseReplayRecording` 同时装配 `LogicalInputRecorder` 和 `GameplayStateRecorder`。`UseReplayPlayback` 同时装配逻辑输入回放和 `GameplayStateVerifier`；默认在最后一个 Tick 成功验证后关闭应用，适合无人值守回归。需要由游戏自行决定后续行为时，可传入 `closeWhenComplete: false`，继续请求超出录制范围的 Tick 仍会快速失败。

## 身份与兼容性

`ReplayIdentity(GameId, BuildId)` 完全由游戏提供。推荐使用稳定的产品 ID，以及 Git commit、内容构建 ID 或发布版本作为 BuildId。加载时两者都必须逐字匹配，避免把旧玩法代码生成的输入直接送进不兼容的新构建。

Hosting 还会在运行前验证：

- 固定 delta 与文件保存的 IEEE bits 完全一致。
- 当前 `InputMap` 的逻辑 Action/Axis 名称和种类一致；物理键位可以不同。
- 输入帧与状态快照都从 Tick 1 开始、数量相同且连续。
- 状态 Hash 算法、输入模型、状态轨迹和容器版本都受显式版本号保护。

兼容性判断不会比较纹理、Shader 或物理输入设备。游戏仍有责任使用相同初始 Scene、内容修订、GameplayRandom seed 和外部确定性数据。

## 文件协议与完整性

v1 使用确定性 little-endian 二进制容器：

```text
MGRP magic + container version + payload length
  identity + component versions + fixed delta
  logical input schema + Tick frames
  gameplay state hashes + contributor diagnostics
SHA-256(payload)
```

相同 Bundle 会产生逐字节相同的文件。末尾 SHA-256 用于发现截断和意外损坏，不是数字签名，也不能证明文件来自可信作者。

按路径保存时，Writer 先在内存完成序列化，再写入同目录临时文件并原子替换目标；序列化失败不会截断已有基线。`Read(Stream)` / `Write(Stream)` 不关闭调用方拥有的流，便于测试、网络传输或自定义存储。

## 不可信文件限制

Reader 在分配数组前应用 `ReplayBundleLimits`。默认值为：

- 文件最大 256 MiB。
- 最多 1,000,000 个 Tick。
- 最多 1,024 个 Action、256 个 Axis2D。
- 每 Tick 最多 100,000 个状态 contributor。
- 单个 UTF-8 字符串最大 1 MiB。

游戏可按自己的会话长度收紧限制。未知版本、非有限 delta/Axis、重复逻辑名称、非法计数、尾随数据、错误校验和与空标识都会以 `InvalidDataException` 拒绝。

## Playground 示例

Asteroids 已提供最小命令行入口：

```powershell
dotnet run --project playgrounds/Asteroids -- --record-replay asteroids.mgreplay
dotnet run --project playgrounds/Asteroids -- --replay asteroids.mgreplay
```

也可以给 `--smoke` 录制短会话；回放会在最后一个验证 Tick 自动退出。

## 明确不包含

- 不保存可恢复的完整实例状态，因此不能从中途跳转、读档或时间回溯。
- 不嵌入资产、屏幕截图、原始键鼠事件或墙钟时间。
- v1 不提供压缩、跨版本迁移、网络 Rollback、Checkpoint 或 Replay 编辑。
- 录制期间输入帧与状态 contributor 保存在内存，长时间压力测试应设置明确时长上限。

如果未来真实用例需要快速跳到长 Replay 的中段，下一边界应是版本化、游戏参与的 Checkpoint 协议，而不是让 Hash 反向承担状态恢复职责；当前路线回到 Gameplay Authoring Experience。

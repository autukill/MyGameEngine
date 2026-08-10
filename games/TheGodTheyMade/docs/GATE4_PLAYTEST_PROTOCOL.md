# Gate 4 外部盲测协议

## 当前状态

Gate 4 的工程内容已经可运行：30 分钟章节、三个谜题、葬礼抉择、无操作恢复路线、三联壁画、灰盒视觉和关键短声音均已接入。Gate 只有在不了解内部规则的真实玩家完成盲测后才能正式关闭；自动测试不能替代这项证据。

## 启动

```powershell
dotnet run --project games/TheGodTheyMade/src/TheGodTheyMade.Game/TheGodTheyMade.Game.csproj
```

- WASD/方向键：移动镜头。
- 滚轮：缩放。
- 在世界中按下并释放左键：向最终 Cell 施放局部降雨；点击水闸巨石会触发灰盒清障命令。
- Q：在神兽最近动作的信用窗口内嘉许。
- E：在信用窗口内制止。
- ESC：退出。

测试员不应预先解释钟声、信仰公式、神兽 Q 表、湿遗迹或葬礼的设计答案。可以说明基本操作，但不能提示“正确路线”。

## 分发盲测包

主持人在干净工作区中生成 Windows x64 自包含压缩包：

```powershell
powershell -File games/TheGodTheyMade/tools/Build-PlaytestPackage.ps1
```

输出位于 `artifacts/playtest-package`，包含 ZIP、对应 SHA-256、`BUILD_INFO.txt`、完整 .NET 运行时、编译后内容包、`Start-Playtest.cmd` 和无剧透操作说明。构建脚本会运行发布版隐藏窗口烟测；工作区有未提交差异时默认拒绝打包，避免 commit 身份与二进制不一致。`-AllowDirty` 只用于本机探针，不能用于正式外部证据。

测试员无需源码、SDK 或预装 .NET：解压后双击 `Start-Playtest.cmd`，输入主持人分配的唯一编号。游戏以 `--playtest` 预设自动把命令流和主报告写入同目录的 `PlaytestData`；相同编号已有任一证据文件时会拒绝覆盖。主持人回收整个 `PlaytestData`，核对压缩包 SHA-256 和 `BUILD_INFO.txt`，再完成访谈问卷。

## 每位玩家记录

1. 是否能在不读内部数值的情况下解释至少一条村民信仰来自哪些实际事件。
2. 是否意识到等待、不降雨和错开钟声也是有效表达。
3. 是否能把神兽的一次动作或错误，与此前示范、嘉许或制止联系起来。
4. 是否发现湿润遗迹；未发现时，是否仍能完成章节。
5. 葬礼时做了什么，认为自己保住和牺牲了什么。
6. 是否能用自己的话复述三联壁画，而不是只报告资源数字。
7. 首次困惑、错误理解、卡住位置、退出原因和完成时间。

## 盲测证据留档

真实玩家会话使用 Gameplay Command Journal 记录被模拟接受的最终世界命令，而不是屏幕像素轨迹：

```powershell
dotnet run --project games/TheGodTheyMade/src/TheGodTheyMade.Game/TheGodTheyMade.Game.csproj -- --record-commands artifacts/playtests/tester-01.commands.json --playtest-report artifacts/playtests/tester-01.report.json --tester-id tester-01
```

仓库内主持人也可用等价预设简化启动：

```powershell
dotnet run --project games/TheGodTheyMade/src/TheGodTheyMade.Game/TheGodTheyMade.Game.csproj -- --playtest tester-01 --playtest-output artifacts/playtests
```

会话结束后，`.commands.json` 包含固定 Tick 的降雨落点、水闸操作及被神兽学习系统接受的 Q/E 嘉许或制止；`.report.json` 包含终局、田地、信仰、壁画、神兽解释轨迹和四组状态 Hash，并预留七项人工问卷。报告中的问卷默认是 `null`，主持人必须根据测试员回答补录，程序不会推测玩家理解。

同一命令流可再次运行，验证动态世界结果而不依赖窗口坐标：

```powershell
dotnet run --project games/TheGodTheyMade/src/TheGodTheyMade.Game/TheGodTheyMade.Game.csproj -- --play-commands artifacts/playtests/tester-01.commands.json --playtest-report artifacts/playtests/tester-01.replay.report.json --tester-id tester-01-replay
```

回放若遇到错过 Tick 或命令被拒绝会立即失败。命令流只证明最终 Gameplay 命令与模拟结果可复现，不记录镜头移动、鼠标像素路径、犹豫过程或玩家口述；这些仍由观察表和可选屏幕录像补充。

主持人将每位测试员的主报告放在一个独立目录中，并把 `questionnaire` 的四个布尔值与三个文字回答从 `null` 改为真实访谈结果。即使没有困惑，也要把 `confusionAndBlockers` 明确写为“无”，不能留空。不要把命令回放生成的复核报告混入该目录。完成后运行：

```powershell
dotnet run --project games/TheGodTheyMade/src/TheGodTheyMade.Game/TheGodTheyMade.Game.csproj -- --audit-playtests artifacts/playtests --gate-audit-report artifacts/gate4.audit.json
```

审计器严格读取当前 schema，拒绝未知字段、重复测试员 ID 和超过安全上限的报告；按实际独立测试员数量计算向上取整的 80%/60%/80% 阈值，同时检查所有会话完成、所有问卷完整和至少两种壁画历史。未满足时进程返回 `2` 并逐项列出缺失证据；全部满足才返回 `0`。自动判定不能证明测试员此前不了解规则，这一点仍由招募记录负责。

自动确定性基线使用不依赖原始鼠标像素的固定命令脚本保存 Replay Bundle：

```powershell
dotnet run --project games/TheGodTheyMade/src/TheGodTheyMade.Game/TheGodTheyMade.Game.csproj -- --smoke --scripted-regression --record-replay artifacts/regression.mgreplay
dotnet run --project games/TheGodTheyMade/src/TheGodTheyMade.Game/TheGodTheyMade.Game.csproj -- --smoke --scripted-regression --replay artifacts/regression.mgreplay
```

Replay v1 记录逻辑 Action/Axis 和 Gameplay State Hash，不记录原始鼠标位置；它继续用于固定脚本的引擎集成回归。真实 Pointer 盲测改用上述版本化 Gameplay Command Journal 复现动态世界落点，并另外保留观察表和可选屏幕录像。两者职责不同，不能把命令流声称为完整交互录像。

## Gate 通过条件

- 至少 5 名此前不了解规则的玩家完成测试。
- 至少 4/5 能解释一条信仰的真实证据。
- 至少 3/5 认为等待或不回应也是有效选择。
- 至少 4/5 能把神兽行为与示范/奖励联系起来。
- 五次流程至少产生两种不同、均可完成的壁画历史。
- 自动 Replay 基线没有确定性 Hash 分叉；所有人工完成流程没有永久路径卡死或无反馈的终止状态。

未通过时先修改反馈、时间窗、空间关系与脚本节奏，不增加第二座岛、第二种神迹或第二种神兽身体。

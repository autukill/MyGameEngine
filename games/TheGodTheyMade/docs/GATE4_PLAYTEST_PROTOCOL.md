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

## 每位玩家记录

1. 是否能在不读内部数值的情况下解释至少一条村民信仰来自哪些实际事件。
2. 是否意识到等待、不降雨和错开钟声也是有效表达。
3. 是否能把神兽的一次动作或错误，与此前示范、嘉许或制止联系起来。
4. 是否发现湿润遗迹；未发现时，是否仍能完成章节。
5. 葬礼时做了什么，认为自己保住和牺牲了什么。
6. 是否能用自己的话复述三联壁画，而不是只报告资源数字。
7. 首次困惑、错误理解、卡住位置、退出原因和完成时间。

自动确定性基线使用不依赖原始鼠标像素的固定命令脚本保存 Replay Bundle：

```powershell
dotnet run --project games/TheGodTheyMade/src/TheGodTheyMade.Game/TheGodTheyMade.Game.csproj -- --smoke --scripted-regression --record-replay artifacts/regression.mgreplay
dotnet run --project games/TheGodTheyMade/src/TheGodTheyMade.Game/TheGodTheyMade.Game.csproj -- --smoke --scripted-regression --replay artifacts/regression.mgreplay
```

当前 Replay v1 记录逻辑 Action/Axis 和 Gameplay State Hash，不记录原始鼠标位置；因此真实 Pointer 盲测另外记录观察表和可选屏幕录像，不能声称已生成完整交互 Replay。Pointer 最终命令的固定脚本已有自动回归。若真实长会话必须复现动态世界落点，下一边界应是版本化 Gameplay Command Stream，而不是把鼠标像素塞入逻辑按键协议。

## Gate 通过条件

- 至少 5 名此前不了解规则的玩家完成测试。
- 至少 4/5 能解释一条信仰的真实证据。
- 至少 3/5 认为等待或不回应也是有效选择。
- 至少 4/5 能把神兽行为与示范/奖励联系起来。
- 五次流程至少产生两种不同、均可完成的壁画历史。
- 自动 Replay 基线没有确定性 Hash 分叉；所有人工完成流程没有永久路径卡死或无反馈的终止状态。

未通过时先修改反馈、时间窗、空间关系与脚本节奏，不增加第二座岛、第二种神迹或第二种神兽身体。

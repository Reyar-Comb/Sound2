# BaiYu-Test：独立关卡组合场景

`Assets/Scenes/BaiYu-Test.unity` 使用 `LevelWorld` 组织现有独立关卡，保留 SONICSPACE 开场、共享点云、玩家交互、生命系统和已有地图编辑结果。

当前内容有 8 个关卡、96 个房间、695 个原有道具和 14 个电梯入口。每条连接的出发、到达入口分别属于各自关卡；目的地与运输时间只记录在总场景的 Connections 中。现有连接仍按原内容顺序前进，但系统允许任意关卡配对、双向连接以及任意关卡数量。

每关的独立模板已保存到 `Assets/Levels/F1.prefab` 至 `F8.prefab`。单关没有目的地配置，不需要创建对应楼层的电梯或保持垂直对齐。将关卡放到总场景后再配置连接。复制模板后继续编辑时，保留其几何与道具资源依赖。

外部左键开门；进入后左键使用内部按钮运输。运输期间禁止移动和投放，允许环顾；到达后恢复。手持物品保持在手上，轿厢内松散物品保持相对位置，随后属于目标关卡。现有白色电梯表面的 HUD 对比度逻辑继续保留。

详细制作步骤见 [关卡编辑器](LevelEditor.md)。八层生成器、对应 Tools 菜单及专用运行时组件已移除，现有房间、走廊、手工网格修改与道具没有重新生成。

## SOUND & SHELL 新起始场景

`Assets/Scenes/SoundAndShell.unity` 是新的完整入口，也是 Build Settings 中的首个场景。原 `BaiYu-Test` 保留。

- Player 位于真实白色房间中央；电梯和开放窗户在相对两侧，窗外是黑色虚空。标题沿用 SECONDERSIGN 字体、点阵和闪烁效果，文字为 `SOUND & SHELL`。
- 标题期间使用 Player 摄像机并锁定移动与视角；左键使标题消失后恢复控制。WASD 移动，空格跳跃，电梯外部与内部按钮继续使用现有左键交互。
- 从窗跳出、落到房间下方后等待 1.5 秒，背景变黑，`Assets/Cards/0-the-fool.png` 从中心迅速放大。再次左键淡出卡牌、渐变返回房间中央，不重复标题；可以重复跳窗。
- 单向电梯顺序：起始房间 → 学校 → 图书馆 → 家 → 超市 → 婚礼 → 医院 → Boss。“家”的同一部电梯承担到达及出发，因此不会在到达后消隐。
- 七关内容复制自 `Assets/Levels/Level*.unity`，原场景资源未改动。组合场景中的 `LevelLayout.manualGeometry` 保留手工几何，防止保存及自动门洞同步重新生成封闭墙体；需要重新生成某关时，应先明确关闭此选项。

`SoundShellOpening` 位于 Player 摄像机，可在 Inspector 调整坠落等待、卡牌冲近和返回渐变时长。`SonicSpaceStartMenu.usePlayerView` 选择真实摄像机标题模式，旧场景仍可使用原来的模拟菜单房间。

验证脚本：`AgentScripts/VerifySoundShell.cs`，`Opening` 检查实际输入、跳窗和两次返回，`Elevators` 检查每段运输及到达电梯出口。运行结果保存到 `Logs/SoundShell-*-report.json`。

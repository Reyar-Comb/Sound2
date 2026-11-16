# F8 Boss 战

## SoundAndShell 初始房间结局

当前总场景 `Assets/Scenes/SoundAndShell.unity` 中，初始房间的床带 `StartingRoomBed`：新一轮游戏隐藏 Renderer、关闭碰撞且不可交互；实际进入 Boss 关卡后解锁，经记忆桌“返回初始房间”回去可交互，触发 `9-the-hermit`。

普通跳窗仍触发 `0-the-fool`。Boss 战真正开始后，玩家第一次死亡会锁定本轮倒吊人条件；复活后返回初始房间跳窗，改为 `12-the-hanged-man`。只进入 Boss 房或开始战斗但未死亡不会切换此分支。三张卡共用 `SoundShellOpening` 的黑屏、卡牌冲近、点击淡出与房间重置流程。

倒吊人条件达成后，`StartingRoomWindowVines` 覆盖窗框及窗口空洞，床上没有藤蔓。它复用 `MonsterVines` 的生长和红点渲染，窗口内的支撑面仅为不可见触发器，无实体碰撞、无伤害、不阻止跳窗。白房间只额外绘制藤蔓，保留原有白色表现。

`StoryRunProgress` 保存进入 Boss、开启 Boss 和开启后死亡三个状态，复活、关卡快照重建、返回按钮及结局返回均不清除；新一轮 Play/游戏运行清空。只配置初始房间的床实例，其他关卡的普通床不受影响。验证脚本：`AgentScripts/VerifyStartingRoomEndings.cs`，报告及画面对照：`Logs/StartingRoomEndings/`。

F8 跳过信物条件与 F9 开发模式通过 `Debug.isDebugBuild` 限制在编辑器/开发构建中，替代 Unity 6000.6 已弃用的 `DEVELOPMENT_BUILD` 宏。`AgentScripts/FixCollisionBakeWarnings.cs` 审计构建场景及 Resources 引用的碰撞网格，按现有 MeshCollider 的凸包/三角网格用途开启预烘焙；不改动几何数据或 Collider 配置。

## 战斗参数（原 F8 测试流程）

直接打开 `Assets/Levels/LevelBoss.unity` 编辑和测试。按 Play 后玩家出生在圆厅门口，向前进入大厅即开始演出。在 Hierarchy 选中 `F8 > Boss Encounter`，用 `LevelArenaBoss` 调节战斗参数。圆厅布局也可以通过关卡编辑器修改。

在总场景 `BaiYu-Test` 里，从 F7 乘电梯进入 F8。独立场景、总场景与 `Assets/Levels/F8.prefab` 分别保存各自的关卡数据和战斗参数，修改一处需要同步到需要这些修改的版本。

| 要调整的内容 | Inspector 字段 |
| --- | --- |
| 音乐与揭幕 | `Battle Music`、`Reveal Music Time`（默认 21.33 秒）、`Music Volume` |
| 音乐循环 | `Loop From Reveal` 默认开启。关闭后填写 `Loop Start Time`，也可以填写 `Loop End Time`。循环结束填 0 会播放到音频末尾。 |
| 红色揭幕声波 | `Reveal Wave Speed`、`Reveal Wave Afterglow`、`Reveal Wave Range`。范围填 0 时按房间大小计算。 |
| 战斗节奏 | `Before Combat Seconds`、`Survival Seconds`（默认 120 秒）、`Between Skills`、`Rotation Order`。 |
| 召唤与下砸 | 召唤数量、存在时长、召唤距离、飞越时间、蓄力时间、下砸时间、下砸半径和三连砸间隔。 |
| 激光与横扫 | `Laser Delay` 与 `Laser Lookback` 默认均为 1 秒。下砸与横扫使用红色点阵声呐，两组声波可分别调整速度、距离、前沿厚度、离地高度和余辉，横扫另有横向宽度。 |
| 胜利演出 | 红色声波、升空高度和时长、展开尺寸和时长、停留、白屏渐变，以及白屏结束时触发的 `On Victory`。 |

音轨槽为空时，默认黑屏 2 秒后揭幕，可用 `Reveal Without Music` 调整。循环起点与揭幕点使用同一个音轨时间时，只在第一次经过该点亮起 Boss 并解锁玩家。四个技能及召唤敌人的接触伤害共用 `Skill Damage`，默认每次有效命中扣 50 点血。玩家死亡后从 F8 的电梯复活，再进入大厅可重新开战。

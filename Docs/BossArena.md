# F8 Boss 战

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

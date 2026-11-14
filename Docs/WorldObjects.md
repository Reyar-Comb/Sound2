# 金币、经济、球状闪电与声波密度边界

体验场景为 `Assets/Scenes/SonarHall.unity`。已放置 6 枚金币与 1 个球状闪电，玩家右上角显示 COINS 与 HP。金币和闪电分别有独立 prefab，可以拖入其他使用共享波场的场景。

## 金币

`Assets/Prefabs/Coin.prefab` 的 `CoinPickup` 负责收集与动画，`SonarObjectEmitter` 负责周期黄波。视图中心为子节点 Floating visual：在地面上方 0.9m，按正弦曲线柔和上下浮动 0.12m，周期 3.2 秒，每秒绕竖直轴自转 48°。

| 参数位置 | 默认 | 含义 |
| --- | --- | --- |
| Sonar Object Emitter / Interval | 3 秒 | 固定发波周期 |
| Range / Wave Speed / Afterglow | 2m / 5m/s / 0.45 秒 | 短程黄波 |
| Pulse Kind | Coin | 黄色、使用金币独立波槽 |
| Coin Pickup / Body Reveal Seconds | 0.55 秒 | 自己发波时金币本体显现的时间 |
| Pickup Radius / Value | 0.65m / 1 | 距玩家胶囊表面的收集距离、入账数量 |
| Bob Amplitude / Bob Period / Spin Degrees Per Second | 0.12 / 3.2 / 48 | 浮动幅度、周期与自转速度 |

金币本体使用黄色 Unlit 材质，在自己发波后短暂开启 Renderer，其余时间关闭，其他来源的声波不会提前显示隐藏本体。黄色环境点阵仍由统一点阵渲染器生成。收集以玩家胶囊的实际距离检查，并用射线阻止隔墙拾取；同一枚金币只能入账一次，成功后停止发波并销毁。

## 初步经济系统

玩家 `PlayerWallet` 是经济入口，HUD 只读余额。奖励调用 `TryCredit(amount, reason)`，商店等系统调用 `TrySpend(amount, reason)`，可以用 `CanAfford` 预览。

- 非正数交易、溢出和余额不足会失败，不会修改余额或发送成功事件。
- `Changed` 发布 WalletTransaction，包含 delta、balance、reason，供后续 UI、音效和任务系统订阅。
- `TotalEarned` / `TotalSpent` 记录本局累计收入和支出。
- `Capture()` / `Restore(WalletSnapshot)` 是后续存档接口。当前余额属于本局，重新运行会从 Starting Coins 开始，没有自动写 PlayerPrefs 或磁盘。

## 球状闪电

`Assets/Prefabs/BallLightning.prefab` 包含 NavMeshAgent、BallLightning、SonarObjectEmitter 和只用于深度点阵显影的黑色球面。球的 Collider 是 Trigger，平静和活跃都不会实体阻挡玩家；活跃时通过伤害逻辑造成危险。

球面视觉保持世界高度 1.35m，不上下浮动。导航根节点在地面，球面跟随它的水平位置。以默认 0.28m/s 在出生位置周围 6m 内随机选取可达导航目标，目标计算不读取玩家位置。

| 状态 | 周期 | 范围 | 强度 / 余像 | 玩家效果 |
| --- | --- | --- | --- | --- |
| Calm 平静 | 6 秒 | 2m | 0.25 / 0.25 秒 | 可穿过，无伤害 |
| Active 活跃 | 0.7 秒 | 8m | 0.95 / 0.65 秒 | 距胶囊表面 2m 内，每秒 20 伤害 |

默认 Auto Cycle 开启：平静 12 秒、活跃 7 秒，循环进行。可关闭自动循环，在组件菜单使用 Set calm / Set active，或由机关调用 `SetState(LightningState.Active)`。切换会立即重新安排扫描节奏，避免沿用平静状态的长等待时间。Calm / Active 两组配置各自持有亮度和密度参数，互不修改已发出的波。

伤害基于距离、节奏和墙体遮挡检查，远处或隔墙不受伤；`PlayerVitality` 默认 100 HP，提供伤害、生命变化与死亡事件。当前尚未接死亡流程或复活规则，HP 耗尽时 HUD 显示 Health depleted。

闪电自己的波及其球面使用蓝色。玩家白波或靠近显影照出球面时，仍将原点阵转为蓝色以保持物体身份，不重新生成点位。球体识别使用最多 8 个球形视觉包围体，适配本 prefab 的球面；如果后续替换为其他形状，需要同步调整识别方式。

## 怪物避让闪电

PatrolMonster 增加 Evade 状态。怪物在 7m 感知距离内检查闪电位置及当前路径，平静状态避让半径 1.4m，活跃状态 3.2m，另加代理半径与 0.5m 安全余量。

已靠近危险区时选择可达的向外路径；原路径会穿过闪电时，选择保持安全距离的绕行目标。避让优先于听声调查，期间不被新声音引回危险区；脱离后恢复原巡视或调查。它使用共享 NavMesh 和路径距离检查，没有在场景中新增会阻挡玩家的实体墙。

## 密度衰减与范围上限

`SonarDensitySettings` 与亮度衰减 `SonarDistanceSettings` 分离。怪物 `MonsterScanEmitter / Density Settings` 默认开启，6m 开始减少点数，10m 降到零，超过 10m 无面点阵和轮廓点。

密度倍率为 `pow(1 - smoothstep(start, end, distance), power)`。面点云用它乘到原 FaceDensityAt 的密度，轮廓用它筛选同一 OrderedRank；保留点仍沿用原来的大小与位置，没有靠缩小点或只降低亮度来伪装稀疏。现有亮度/余像衰减仍单独作用。Range、亮度终止距离和密度终止距离中最小者决定实际可探索边界。

这个配置已接入正式通用物体组件，金币和闪电也可以独立调整。既有白波及脚步默认不额外开启新的密度衰减，因此保留原有图案。

## 渲染与波槽

共用 SonarPulseField，24 槽：0–3 主波、4–7 脚步、8–11 怪物、12–19 金币、20–23 闪电。各组循环，不互相覆盖。金币等局部波使用与脚步一致的 24×24×6 遮挡射线，主波保持 48×48×6。

共享 Shader 一次采样原普通网格与彩色网格，彩色网格存面/边亮度及各自的颜色编号，仍调用同一 FaceDensityAt、OrderedRank、圆点半径和准星避让逻辑。绘制按白→黄→蓝→红优先级组合，先清除对应圆点覆盖再绘制，避免重叠出现白色或混合成紫色。没有活动波时跳过对应颜色的合成。

## 验证入口

- `AgentScripts/ConfigureWorldObjects.cs`：可重复运行的场景配置入口，创建缺少的 prefab 和物体，不重建已有金币布局。
- `AgentScripts/VerifyWorldObjects.cs`：真实金币发波、显现/动画/隔墙收集、经济交易、闪电移动/恒高/状态/伤害、怪物逃离与绕行。
- `AgentScripts/VerifyColoredSonar.cs`：实际 GPU 截图比较黄蓝点阵与原图案、颜色优先级、密度减少与硬边界、保留点亮度、波槽隔离和闪电本体身份。

本次验证结果：物体/经济/伤害/避让 28 项、彩色点阵 GPU 画面 21 项全部通过。原怪物行为 18 项、红波画面 13 项、脚步 16 项、面点激活 14 项、面点云 8 项、亮度衰减 19 项、显影开关 10 项及准星避让 6 组检查通过。C# 编译无错误或警告，实际渲染后的 Shader 消息为空。蓝色与白色通道的截图比较允许 2/255 的 HDR 通道量化误差；点阵排布、半径和密度使用同一计算。

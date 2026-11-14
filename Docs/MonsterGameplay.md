# 怪物巡视、红色扫描与听声调查

## 体验入口

打开 `Assets/Scenes/TeammateSonarHall.unity` 并进入 Play。场景中的 `Patrolling Monster (invisible)` 从东侧通道出发，沿 `Monster Patrol Route` 下的五个节点巡视。它没有 Renderer，不会直接露出模型或被玩家声波扫描成实体轮廓。玩家通过怪物发出的红色环境点阵及脚边、邻近墙面蔓延的红色藤蔓判断危险位置。

`PatrolMonster.Awake` 自动补齐 `MonsterVines`，已有场景和程序化楼层均生效。藤蔓以真实移动方向为主向，在静态地面生长分枝，遇墙转向攀爬；邻墙也会产生向前、向上的枝条。点位保留世界坐标，前端按出生时序柔和长出，背后的枝条随时间和距离消退；停止移动时仍有局部生长，禁用、卸层或瞬移时清理旧轨迹。不会增加实体、碰撞体或导航障碍。

默认每 0.32 秒生长一次，寿命 2.8 秒，主枝长度约 2.8m，每怪物最多 768 点。射线排除触发器、刚体和玩家 CharacterController；缺少支撑的边缘停止生长。渲染复用屏幕点阵尺寸和网格，保持红色并使用场景深度遮挡，同时避让准心、字幕和所有注册的玩家 UI。可在 Inspector 调整 Growth Interval、Lifetime、Reach、Surfaces。

藤蔓使用稳定的表面坐标系：地面为 X/Z，墙面为水平／竖直。主枝、转折、分枝及攀墙后的方向均吸附到四轴向与四对角方向（45° 间隔）；侧枝按 ±90° 分出，取消逐点随机曲线扰动。仍优先向怪物实际前进方向生长，尾部持续消退。

移动会按原有步距触发脚步，静止或只转头不发脚步。靠近小球按 E 拾取，再次按 E 扔向墙面；有效碰撞会把怪物的调查目标改成实际撞击位置。怪物到达后停留搜索，安静一段时间后恢复巡视。尚未加入攻击、碰撞伤害或失败机制。

## Inspector 调节

选择 `Patrolling Monster (invisible)`：

| 组件 / 参数 | 默认 | 用途 |
| --- | --- | --- |
| Patrol Monster / Patrol Points | 5 个节点 | 顺序巡视路线，可移动节点或重新指定数组 |
| Patrol Speed / Investigate Speed | 1.1 / 1.8 m/s | 平时与调查时速度 |
| Patrol Pause / Search Seconds | 0.8 / 3 秒 | 节点停顿与到达声源后的搜索时间 |
| Investigate Timeout | 14 秒 | 调查超时后搜索并恢复巡视 |
| Hearing Sensitivity | 1 | 乘到各声音事件的可听距离 |
| Through Wall Range Multiplier | 0.45 | 隔墙时可听距离乘数 |
| Sound Priority Memory | 1.5 秒 | 刚听到强声音后，短时忽略明显更弱的刺激 |
| Monster Scan Emitter / Near Distance / Far Distance | 2 / 16 m | 扫描频率插值区间 |
| Near Interval / Far Interval | 0.65 / 3.5 秒 | 越近扫描越频繁，区间外钳制 |
| First Delay | 0.8 秒 | 首次发波等待 |
| Range / Wave Speed / Afterglow | 14 m / 10 m/s / 0.8 秒 | 红色波的传播和余像 |
| Distance Settings | 1–14 m，强度 0.9 | 独立控制红波距离衰减 |
| Play Sound / Volume | 开 / 0.22 | 复用 SonarCueSource 的低音调提示，可换 Clip |

脚步可听距离在玩家 `FootstepEmitter.hearingRange`，默认 6m；球碰撞可听距离在 `CollisionSonarEmitter.hearingRange`，基础 20m，会随原有碰撞强度对应的声波范围同比放大。听觉距离、实际音频音量、可视声波距离分别配置；即使暂时没有脚步素材，怪物也能听见玩法事件。设 Hearing Range = 0 可禁止该源引起怪物注意。

## 复用与行为边界

- `GameplayNoise` 是独立听觉事件总线，携带固定世界坐标、可听范围、类型及来源。`FootstepEmitter` 与 `CollisionSonarEmitter` 在真实触发点发布事件。
- `PatrolMonster` 只对可听范围内且能寻路到的事件位置进行调查。距离使用直线距离；射线遇墙时缩短范围。这是玩法层近似听觉，并非 Steam Audio 声学传播结果。事件后玩家移动不会让怪物自动知道新位置。
- 玩家位置只用于扫描节奏，不用于追踪或调查目标。扫描周期在近远端线性插值，按实时周期累计相位。
- `MonsterScanEmitter` 调用共享 `SonarPulseField.EmitDangerFrom`。不另建点云，红白都调用同一套 FaceDensityAt、OrderedRank、点半径、遮挡和到达/消退计算。红白点重叠时红色优先；与红点覆盖区域无关的白点保留。
- 波场共 24 槽：0–3 主动/物体，4–7 脚步，8–11 危险，12–19 金币，20–23 闪电。各组独立循环，频繁怪物扫描不会挤掉玩家或脚步波。危险和脚步均用 24×24×6 遮挡采样，主波使用 48×48×6；细小物体边缘可能存在角采样误差。
- `RuntimePatrolNavigation` 从房间根节点的静态 Collider 在运行时生成共享 NavMesh，排除 Rigidbody、触发器、CharacterController 和怪物代理。没有引入新插件。改地图后下次 Play 会重建；运行中移动墙体目前不会自动更新路径。
- 新场景可以组合一个共享导航对象、一个共享波场，以及若干空怪物对象（NavMeshAgent + PatrolMonster + MonsterScanEmitter）。多个怪物共享四个危险波槽，数量增加时可能覆盖尚未结束的旧红波，需按目标数量扩容或限制节奏。

## 验证

`AgentScripts/VerifyVinesAndStars.cs` 验证自动接入、地面与墙壁附着、实际 GPU 红点、实体和 UI 遮挡、世界坐标固定、转向跟随、尾迹消退、瞬移和禁用清理，并验证菜单以外的白色空间星点。

`AgentScripts/VerifyMonster.cs` 验证实际巡视/绕墙路径、真实脚步钩子与球墙碰撞、调查目标、安静后恢复巡视、隔墙听觉和实际定时扫描次数。

`AgentScripts/VerifyDangerSonar.cs` 对实际 GPU 截图检查红白点逐像素一致、红色重叠优先、到达/衰减/消退、保留波槽及 Shader 编译。

本次验证：怪物行为 18 项、危险波 GPU 画面 13 项全部通过；原有声波面点阵 14 项、面点云 8 项、可视衰减 19 项、显影开关 10 项、脚步行为 16 项及准星避让 6 组 GPU 检查通过。C# 编译与 Shader 消息均无错误或警告。尚未在 Windows 构建或多怪物负载下验证性能。


新增金币、闪电与密度边界说明见 [WorldObjects.md](WorldObjects.md)。怪物 Density Settings 默认 6–10m 从原密度逐渐稀疏到零，实际探索上限 10m；该密度设置与原有亮度衰减分开调整。怪物会优先避让附近闪电，安全后恢复巡视/调查。

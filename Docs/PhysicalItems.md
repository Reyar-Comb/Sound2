# 物理拿放与体积闪电

普通球、棱晶和出生房间方块统一使用 PickupItem。左键／E 拿起或抛出，右键放下。取消资源收集、钱包组件和物品入账；HUD 仅保留生命及原有楼层提示。旧 MapResource 仅作为生成棱晶的兼容标记，自动要求 PickupItem。PlayerWallet 源码保留为未使用的历史接口，不再挂到场景或由玩法调用。

放下优先使用瞄准范围内的水平支撑面，否则探测手持位置下方；没有支撑则在手边零冲量释放。目的地与释放路径都必须无障碍，否则保留手中物品并显示中文提示。放下后的拾取冷却 0.12 秒，投掷沿用原冷却。靠墙回缩、刚体姿态同步与跨层父节点迁移继续生效。

PlayerPickup 的持物惯性由四元数指数追随和 SmoothDamp 位置平滑组成，默认旋转迟滞 0.12 秒、位置平滑 0.06 秒、最大角差 12°。以当前镜头位置计算偏移，避免走路时物品拖在身后；拾取及超过 2.5m 的瞬移重置惯性，墙体限制即时生效。

球状闪电默认自动循环平静 12 秒／活跃 7 秒，场景方块不再控制状态。BallLightning prefab 已删除球面渲染组件。BlackBoxPointCloudFeature 在 GPU 直接投影球体内部的三维样本，不依赖实体表面；默认 512 点，保留前后层点并缓慢旋转。使用场景深度遮挡、共享屏幕点阵网格／点半径／UI 避让和蓝色身份，仍遵守声波与近距离显影设置。

本轮更新 BaiYu-Test、TeammateSonarHall、可投掷物／闪电 prefab 及楼层中文引导，未修改 ProjectSettings 或 Packages。

## 本机验证

- VerifyPhysicalItems：47 项通过，包括三类道具各三次拿放、投掷、真实右键输入、阻挡放下、惯性归位、瞬移复位、自动闪电循环。
- VerifyPickupAndShaftRegression：同球 8 轮投捡、贴墙、3 次下降及跨层携带全部通过。
- VerifyLightningCloud：9 项 GPU 检查通过，包括背面独占投影点（按像素亮度阈值统计，168/188 个格点中心可见）、体积旋转、墙体遮挡、UI 避让、显影模式及 shader 编译。
- VerifyColoredSonar：29 项共享颜色、密度、波槽、遮挡与 UI 渲染检查通过。

报告和 GPU 截图位于 `Logs/PhysicalItems`、`Logs/TeammateIntegration_20261114`。测试在 Unity Editor Play Mode 执行。旧 VerifyWorldObjects 的经济断言属于已移除玩法，脚本已删除；使用上述物理道具及渲染回归入口。

金币 prefab 移除后，依赖它的 ConfigureCoinPoints、VerifyCoinPoints，以及会重新写入金币引导的一次性 ConfigureBaiYuChinese 也已删除。当前中文引导保存在 `Assets/Resources/BaiYuTestFloorProgression.asset`；新建测试场景使用 ConfigureBaiYuTest。


所有 PickupItem 在新建和载入时补齐碰撞声波组件，关闭定时发波／共享切换键；有效碰撞发布独立 AI 听觉事件。VerifyThrowableWorld 的 48 项检查覆盖四种对象两次真实撞墙、落地、近远怪物反应、八向藤蔓和后续无硬币楼层；报告在 Logs/ThrowableWorld。

## 可摔碎道具与延时闹钟

`Assets/Prefabs/PickupAlarmClock.prefab` 是可拾取的双铃闹钟，复用 TickingClock、SonarObjectEmitter 和原有拿放逻辑。`Assets/Prefabs/PickupGlassMug.prefab` 是开口圆柱杯，带杯底和单侧把手。编辑时显示浅色模型，运行时沿用黑色材质和点云显影。两者已放在 `test.unity` 的 `Editable Level/Props` 下，玩家前方左侧为闹钟、右侧为杯子；地图编辑器增加了对应快捷入口。

PickupItem 新增可选的 Breakage 配置，原有球默认关闭：

| 参数 | 作用 |
|---|---|
| Breakable | 是否允许碰撞破坏 |
| Break Speed | 接触面法线方向的碰撞速度阈值；闹钟默认 7m/s，杯子默认 3.8m/s |
| Break Layers | 哪些碰撞层可以触发破坏；玩家身体碰撞不触发 |
| Broken Prefab | 完整物体破坏后的物理零件/碎片预制体 |
| Fragment Separation Speed | 碎片附加的轻微分离速度，默认 0.35m/s |
| Fragment Lifetime | 碎片保留秒数；0 表示随场景/楼层卸载，不自动清理 |
| Break Pulse Range / Break Hearing Range | 一次性破碎声波与怪物听觉范围，默认 10m / 16m |
| Break Clip | 可选破碎录音，当前留空 |

破坏速度复用 CollisionSonarEmitter 的接触法线计算，不以刚体总速度代替。低速落地与高速擦墙不会因总速度较大而误碎。碰撞发声和 PickupItem 共用幂等破坏入口，破坏时只发一次破碎声波，不再叠加普通碰撞声波。

`AlarmClockBroken.prefab` 包含 13 个独立物理零件，包括外壳、表盘、四段外圈、两根指针、两只铃、两只脚和按钮；指针碎片继承打碎瞬间的位置与角度。`GlassMugBroken.prefab` 包含 33 个预切碎片：24 块杯壁、6 块杯底、3 段把手，与完整杯模型对应。碎片继承打碎时的运动并参与物理碰撞，不包含 PickupItem、InteractableObject、TickingClock 或重复发声组件，因此不能拾取，也不会在落地时连续发波。

闹钟上的独立 `PickupAlarm` 组件监听成功拾取事件：首次拿起后开始倒计时，默认 `Delay After Pickup = 5` 秒。放下、扔出和再次拾取都不重置倒计时。到时暂停 TickingClock，按 `Ring Interval = 0.2` 秒快速发波；默认 `Ring Range = 12m`、`Hearing Range = 16m`。菜单暂停玩法时同步暂停倒计时。`Alarm Clip` 是快速响铃录音，普通 tick 录音仍在 SonarCueSource.Clip；两者及破碎录音目前全部留空，Generate Default Ping 关闭，无合成音兜底。

打碎会停止全部闹钟音频并销毁完整物体，倒计时和快速响铃随之终止。已经发出的视觉波按原有余辉自然结束。手持物体在玩家层级停用时，会在层级切换完成后安全释放；退出 Play 时不尝试在正在关闭的层级内重设父节点。

`AgentScripts/ConfigureBreakableProps.cs` 生成模型和预制体并提供 test 场景放置入口。`AgentScripts/VerifyBreakableProps.cs` 使用真实物理碰撞验证拾取、轻放、低速/高速/擦碰/层过滤破坏、拿在手中与放下后的延时响铃、重复拿起不重置、提前摔碎取消响铃、碎片不可拾取及点云显影、普通球回归和玩家停用时持物释放。报告与模型预览在 `Logs/BreakableProps`。

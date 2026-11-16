# 医院贴地修正与婚礼 → 医院性能测试

2026-11-16，Unity 6000.6.6f1，当前 Editor。

## 场景修正

修正 `Assets/Levels/LevelHospital.unity` 与实际游戏总场景 `Assets/Scenes/SoundAndShell.unity` 中对应的 **55 件物品**：53 件悬空物品下移，2 张轻微埋地的病床上移。其中 54 件为可推动病床/医疗车，1 件为 `RedVineArea (4)`。多数悬空床车的底部原来高于地面约 1.23 m；以实际碰撞体和可读模型最低点对齐地面，保留作者设置的约 1 cm 接触余量，只修改 Y 坐标。

保留水平位置、旋转、缩放、道具数量、模型、碰撞体、音效和推行参数。桌上的 HeartRateMonitor 保留桌面支撑，壁钟和门不参与地面吸附。`RedVineArea (6)` 位于地板范围之外，向下没有可吸附表面，保持原位，没有擅自移动进房间。

开始时医院场景有未保存编辑，已先用 Editor SaveScene(saveAsCopy) 保存完整备份到 `Logs/HospitalJourney/Backup/LevelHospital.unsaved-before.unity`，并保留磁盘原版。最终与该备份加载后的对象比较：**167 件 Props 数量一致，恰好 55 件仅高度改变，全部水平位置/旋转/缩放一致，建筑网格顶点与三角形哈希一致**。医院保存时 Unity 重新序列化了部分几何对象 ID，因此文本 diff 比实际几何修改大；对照验证确认建筑形状未变。总场景 diff 为 55 行高度替换。

## 测量方法与结果

核实实际连接为 `05 - 婚礼 / LevelElevator (1)` → `06 - 医院 / LevelElevator`。每次测试新开 Play Mode，先在录制外将玩家布置到婚礼出发电梯，等待婚礼音源/导航就绪，再调用正常 `LevelWorld.Travel`。保留碰撞、AI、伤害和声音逻辑，没有关闭真实玩法来改善数字。

轻量 Profiler，未开 Deep Profile。以下只统计旅行请求到到达完成的帧，排除预录窗口和随后死亡/重置阶段。事件使用最近提交的 Profiler 帧，边界存在一帧左右误差。

| 首次进入样本 | 旅行时间 | 有效帧 | CPU 中位数 | P95 | 最大帧 |
|---|---:|---:|---:|---:|---:|
| cold-final | 6.02 s | 115 | 14.14 ms | 128.86 ms | **656.43 ms** |
| cold-confirm | 7.50 s | 154 | 26.33 ms | 154.99 ms | **387.23 ms** |

两轮都是修正摆放后的首次进入，不是修复前后 A/B；只能确认仍有明显且波动较大的卡顿，不能据此计算贴地操作带来的性能提升。数字包含 Editor 开销，不代表独立 Player 性能。GPU 和 GC 分配汇总字段没有有效记录，0 不表示没有负载。

第二轮到达时：**152 件可推动家具、154 个 Rigidbody、1,861 个 Collider、154 个就绪音源**，导航 Ready、37 个导航源。玩家推行请求为 **0**，但已有 **169 次碰撞声呐、12 次移动声事件**，说明存在自动物理扰动。

## 尖峰原因

第一轮第 136 帧（656.43 ms）调用链：

- `FixedUpdate.PhysicsFixedUpdate` 累计 **592.52 ms / 6 次固定步**。
- `Physics.SendContactEvents` 约 **569.79 ms**，其中 `CollisionSonarEmitter.OnCollisionEnter` **469 次、556.97 ms**。
- 该碰撞调用链下 `Sonar.Emit` **87 次、278.04 ms**，其中射线批处理约 205 ms。
- 同帧 `SteamAudioSource.Awake` **88 次、188.42 ms**；大部分由碰撞立即发声引发。真实发声沿即时初始化路径运行，不受后台每帧最多 2 个音源的预热上限约束。

以上均有父子包含关系，不能相加。物理高耗时主要包含碰撞回调、声呐和音源创建，不能全部归因于 PhysX 求解。第二轮第 131 帧再次出现同类调用链：328 次碰撞回调累计约 279 ms，34 次 Steam Audio Awake 约 80 ms。

第一轮旅行段的整关备份主线程整合约 **19.55 ms**，导航协程帧峰值约 **2.83 ms**；它们不是本轮最大尖峰的主要组成。

摆放审计在 152 件可推动家具之间检测到 **19 对超过 1 mm 的碰撞穿插，其中 18 对是同预制体、同位置、同旋转、同缩放的完全重复物体**。重复床车的最大穿插约 0.36–0.40 m；另有 `MedicalCart (64)` 与 `(62)` 约 0.08 m 的局部穿插。这是进入时物理分离和声呐连锁的明确风险来源；未做去重版本的对照采样，因此尚未量化它独立贡献了多少耗时。

本次没有删除重复物体或调整房间横向布局。后续最直接的优化是先清理这 18 对重复实例并处理另一处穿插，再以相同流程复测；如仍有集中初始化，可进一步设计关卡准备期间的碰撞发声门控，同时保留正常玩家碰撞反馈。

## 测试额外发现

两轮均在到达后死亡：分别约 2.11 s 和 0.41 s。第二轮死亡调用栈明确为 `MonsterVineArea.Update → TryContactDamage → PlayerVitality.TakeLethalDamage`，即藤蔓致死接触，不是测试脚本主动重置。死亡后的关卡替换已单独记录，未混入上面的旅行统计。还在重置后的只读观察中发现床车继续下落至地面以下；这属于额外的重置/碰撞问题，本轮未确认根因或修改逻辑，不能把重置后场景验收为正常。

初次诊断脚本在死亡重置销毁旧 LevelLayout 后导出失败，随后已修正为记录死亡与对象替换，并完成上面两轮有效的旅行段捕获；不使用首次不完整的 `cold.data` 做结论。

## 证据与复现

`Logs/HospitalJourney/` 保存：

- `grounding-primary.json`、`grounding-applied.json`、`grounding-audit.json`、`grounding-verification.json`；主场景与独立场景高度同步。
- `authored-contacts.json`：19 对穿插物体的名称、深度及重复判定。
- `before-grounding.png`、`after-grounding-final.png`：同视角编辑器截图。
- `cold-final.data`、`cold-confirm.data`，对应 events、metrics、session；`cold-transition-summary.json`（帧 51–165）和 `confirm-transition-summary.json`（帧 51–204）。
- `cold-frame136.json`、`confirm-frame131.json` 与 `cold-confirm-death-stack.txt`。
- `Backup/`：医院开始任务时的磁盘和未保存内容、修改前总场景。

诊断入口：`GroundHospitalProps.Run(false)` 只审计；`Run(true)` 保存贴地修改。`VerifyHospitalGrounding.Run()` 对比备份与现有医院；`AuditHospitalContacts.Run()` 检查床车穿插；Play Mode 下 `ProfileHospitalJourney.Run("label", false, true)` 执行真实过渡，`AnalyzeHospitalJourney.Run("label")` 分析当前加载的捕获。

已退出测试 Play Mode，恢复医院独立场景编辑，撤销 auto-tick 和后台运行覆盖。保留用户开始任务时已有的医院、学校和字体修改。未提交 Git。

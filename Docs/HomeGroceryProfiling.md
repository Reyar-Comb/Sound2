# 家 → 超市卡顿排查

2026-11-16，Unity 6000.6.6f1，当前工作区已有去重、碎片刚体预算和 InstantiateAsync 改动。本次只新增诊断脚本与报告，没有实施新的玩法性能修复。

通过真实 `LevelWorld.Travel` 复现。测试布置是在 Play Mode 中切至家并把玩家放进出发电梯，空手进入超市；不包含之前楼层的行走。采样覆盖关门前、备份、激活、导航、开门和到达后约 4 秒。第二次回到家再走同一电梯，保留本次运行已创建的备份、音源及道具沉降状态。该对照用于区分首次初始化，不是仅改变单一变量的性能实验。

脚本编译、测试布置和 JSON 序列化在录制窗口之外。未开启 Deep Profile。四个捕获均完整保留起始帧，首帧不纳入统计。所有计时来自 Editor，不能作为独立 Player 帧率保证。

| 捕获 | 有效帧数 | CPU 中位数 | P95 | 最大帧 |
|---|---:|---:|---:|---:|
| cold-empty：首次，保留原 Profiler 模块 | 307 | 38.069 ms | 119.971 ms | 211.126 ms |
| warm-empty：再次，保留原 Profiler 模块 | 319 | 33.910 ms | 71.276 ms | 141.451 ms |
| cold-lean：重新进入 Play Mode 后首次，轻量模块 | 567 | 14.273 ms | 80.944 ms | 232.964 ms |
| warm-lean：再次，轻量模块 | 594 | 13.662 ms | 46.769 ms | 64.147 ms |

轻量捕获只保留 CPU / Physics / GPU 模块请求。其 GPU 和 GC 分配数据没有有效记录，结果中的 0 不代表无 GPU 工作或无分配。原模块捕获有 GPU 数据，cold-empty 的 GPU 中位数 3.243 ms、最大 7.693 ms，观察到的大尖峰位于 CPU 调用链。

实测原因按调用链说明如下；不同项发生在不同帧，不能直接相加。

1. **首次备份仍有整关主线程整合。** `LevelWorld.CaptureInitialLevelAsync` 把整个超市传给一次 `InstantiateAsync`，异步返回后仍由 Unity 整合完整对象层级。cold-lean 第 193 帧为 129.878 ms，其中 `AsyncInstantiateOperation.IntegrateInMainThread` 116.842 ms，`InstantiateMultiple.Awake` 103.520 ms（自身 95.210 ms），恢复协程/层级映射 8.367 ms。cold-empty 同一类整合为 177.567 ms。这里的 `InstantiateMultiple.Awake` 是引擎整合 marker，不能全部解读成用户脚本 Awake。再次进入没有这条整合调用。

2. **309 个被动道具在 Start 时提前创建完整音源。** `SonarObjectEmitter.Start → Initialize → AddComponent<SpatialAudioEmitter> → Awake → AddComponent<SteamAudioSource> → Awake`。超市这 309 个 SonarObjectEmitter 的 automatic 都是 false，但仍全部初始化。cold-lean 共 309 次 Start，累计 1167.768 ms，其中 SteamAudioSource.Awake 累计 822.438 ms。第 216 帧有 7 次初始化，Start 合计 68.622 ms，Steam Audio 部分 62.365 ms，整帧 105.273 ms。再次进入没有这些首次 Start/Awake 调用。

3. **完整道具的碰撞模型数量很大，首次激活/沉降引起物理负载，并叠加固定步追赶。** 超市有 13,239 个 Transform、11,809 个 Collider（其中 11,080 个 MeshCollider）、310 个 Rigidbody。动态刚体下有 11,144 个 Collider，静态 Collider 为 665。家只有 436 个节点、344 个 Collider、14 个 Rigidbody。cold-lean 第 271 帧共执行 8 次物理步，`PhysicsFixedUpdate` 累计 121.858 ms，`Physics.Simulate` 累计 119.201 ms，内含 FetchPhysXResults 等待；整帧 232.964 ms。第 327 帧还出现单次物理步骤 109.052 ms，主要是 `Physics.SwapPhysXBuffersJob` 107.325 ms。不能把 121.858 ms 当作一个固定步的耗时，也不能把等待时间全部归因于主线程计算。再次进入的物理帧峰值降至 13.870 ms；道具已经沉降，不能据此单独计算碰撞体简化的收益。

| 道具 | 数量 | 每件 Collider | Collider 总数 |
|---|---:|---:|---:|
| PickupGlassMug | 139 | 49 | 6,811 |
| PickupCeramicVase | 35 | 66 | 2,310 |
| PickupWaterCup | 101 | 18 | 1,818 |
| PickupDeskLamp | 34 | 6 | 204 |

这些是**尚未打碎的道具**的碰撞体。`ConfigureHouseholdProps.Vessel` 为每片杯壁/杯底创建凸 MeshCollider，用于保留中空形状；因此此前把 WineGlassBroken 的碎片刚体降到 25 个，并没有减少完整杯子的 49 个碰撞体。当前四次过渡均记录 0 次破碎、0 次碰撞声呐；旧的重复道具破碎风暴不是本次证据显示的原因。

4. **导航还有同步收集尖峰。** cold-lean 第 284 帧的 `RuntimePatrolNavigation.BuildAsync` 为 30.258 ms，其中 `CollectSources` 28.557 ms。异步 NavMesh 构建没有让前面的收集阶段自动异步化。warm-lean 导航协程峰值仍有 23.349 ms。

现有 `LevelActivation` 的 2 ms / 24 件预算只在遍历与 `SetActive` 后检查，昂贵的 Start 位于稍后的 `CoroutinesDelayedCalls`，物理模拟也在该预算以外。实测 Transport 协程峰值仅约 3.27 ms，而同帧后续工作可以超过 100 ms，所以只继续降低 SetActive 的时间预算不能完整解决卡顿。

编辑器会放大观察到的总帧时间。cold-empty 的 `Profiler.FlushMemoryCounters` 平均每帧 12.577 ms，Hierarchy 刷新峰值 28.473 ms，此外还有 Inspector/状态栏、Gizmos、SceneView 等工作。关闭额外模块后 MemoryCounters 降到每帧平均约 0.0016 ms，但实际游戏初始化尖峰仍存在。cold-lean 最慢帧中游戏更新 PlayerLoop 约 151.108 ms，而整帧为 232.964 ms；不能把整帧数字全归给玩法。warm-lean 最慢帧的游戏更新约 21.030 ms，整帧 64.147 ms，同样含明显 Editor 开销。

建议后续按下面顺序实施并重新测量：

1. 给完整杯子/花瓶设计较少的复合碰撞体，和碎片视觉/碎片物理分离；保留中空玩法需求时使用少量壁面代理，避免直接改成封口实心盒。必须同步修改生成脚本及现有场景实例。
2. 对被动道具的空间音源采用有预算的预热或共享音源池。单纯延迟到首次碰撞创建会把卡顿转移到第一次交互，需同时验证音效延迟、定位和并发。
3. 拆分整关备份的复制单元，或改为保存可重置状态；保留跨道具引用映射和取消/重置语义。一次大层级 InstantiateAsync 并不保证整合阶段服从 2 ms 的游戏预算。
4. 缓存/预烘焙静态导航数据，或缩小收集范围，避免每次进场扫描所有碰撞体。
5. 在 Development Player 上复测首次进入与再次进入，分别统计游戏 CPU、渲染、物理和音频，不用 Editor 总帧直接验收 60 FPS。

证据位于 `Logs/HomeGroceryProfile/`：四个 `.data`、各自 `*-summary.json`、`*-events.json`，轻量组 `*-metrics.json`，关键帧 `cold-lean-frame193/216/271/284/327.json`，以及 `inventory.json`、`collider-inventory.json`。阶段事件记录的是最近已提交的 Profiler 帧，边界仅用于定位，不保证与当前游戏帧一一对应。

复现脚本为 `AgentScripts/ProfileHomeGrocery.cs`，调用 `ProfileHomeGrocery.Run("label", false, true)`；每个首次样本须新开 Play Mode，再次样本留在同一运行中。分析脚本 `AgentScripts/AnalyzeHomeGrocery.cs` 读取当前已加载的 Profiler 捕获，输出每帧各 marker 的累计时间与调用次数。

测试完成后已退出本次启动的 Play Mode，撤销运行后台覆盖和 auto-tick。当前编译正常、Console 当前错误数为 0；Pipeline 返回的 05:08 UTC SteamAudio 错误属于早于本次 05:44 UTC 开始的历史队列，最后游标检查没有新增错误。本次未提交 Git。

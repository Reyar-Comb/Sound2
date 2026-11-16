# 音源分帧初始化与保守碰撞体优化

2026-11-16；Unity 6000.6.6f1。承接 `HomeGroceryProfiling.md` 的排查，在当前工作区原有修改基础上实施。未提交 Git。

## 音源初始化

新增 `Assets/Scripts/Audio/SonarAudioWarmup.cs`：将 `SonarObjectEmitter.Start` 的空间音源创建移入共享队列，每帧最多预热 **2 个**，以 **6 ms** 为软时间预算。使用 `Sonar.AudioWarmup` Profiler marker。单次 Steam Audio 原生初始化无法中断，因此不能保证每帧严格低于 6 ms。

队列去重，跳过已销毁、禁用或已经就绪的对象；重新启用后继续预热。真正调用 Emit 时仍同步初始化并立即播放，不会丢弃首次声音，也不改变音量、空间定位、声呐范围或自动发声时间表。预热本身不播放声音、不产生声呐。

`LevelActivation` 等待目标楼层所有启用的音源就绪后结束准备，使第一次碰撞不再集中承担剩余音源初始化。代价是首次电梯准备时间更长。真实发声请求可以绕过队列，因此“每帧 2 个”仅约束后台预热，并非所有发声场景的硬上限。

## 全项目碰撞体审计与修改

遍历 Assets 下全部 **123 个场景和预制体**（包括未启用对象），原来引用 **333 种碰撞网格**；没有额外 FBX/OBJ/Blend 模型资产。最终审计引用 340 种网格，其中新增 7 个共享静态碰撞网格。

只对没有移动、动画、参数化几何或其他玩法组件的静态 LevelProp，按一致的层、标签、物理材质及接触设置分组，把多个 BoxCollider 的原有六面烘焙到一个非凸 MeshCollider。保留所有原始表面和内部间隙，保留 Renderer 与原子物体 Transform。未用包围盒或凸包填平凹处。这里减少的是物理形状/组件数量，并不减少表面三角形数。

| 源预制体 | 原 Collider 数 | 现 Collider 数 |
|---|---:|---:|
| Chair | 10 | 1 |
| Computer | 62 | 3 |
| LibraryBookshelf | 112 | 1 |
| Refrigerator | 12 | 1 |
| RestaurantStage | 11 | 4 |
| Stove | 21 | 9 |
| WritingDesk | 10 | 1 |

Computer、Stove、RestaurantStage 原有曲面、凸网格或其他形状保持独立。每个生成网格按几何内容复用；两个道具生成脚本同步调用优化器，避免重新生成后退回大量 BoxCollider。也修正了 Household 生成脚本缺少 Editor 命名空间而无法解析已有 WineGlassFragmentBudget 的问题。

通过预制体继承，F1–F7 和使用这些家具的场景自动受益，无需保存无意义的楼层覆盖。以 F5 为例，Collider 从 5,532 降为 468。全部审计资产的计数总和从 **64,898 降至 48,030（减少 16,868，约 26%）**。此总和重复计算了预制体及其场景实例，不代表运行时同时存在这么多碰撞体。

保留了杯子、花瓶、把手和容器空腔、可拾取物、碎片、触发器、脚本引用的 Collider、禁用子节点，以及镜像/剪切变换对象。超市完整道具仍有 11,809 个 Collider：它们以可交互的中空复合形状为主，不能仅因数量大就认定完全多余。本轮没有通过改变杯壁精度来换取性能，超市帧时间改善也不能归因于减少了这些动态碰撞体。

最终全项目审计发现 **0 个新增网格挂在非运动学动态刚体上的非法组合，0 组重复烘焙碰撞体**。非凸网格与盒形体的求解接触点、内部出射/背面行为并非数学上完全等价；本轮保持表面，使用实际玩法回归约束风险，不能据此保证所有未来物理场景逐位一致。新增刚体或动画时应重新选择适合动态物体的碰撞形状。

## 验证

- 改写前对 7 类家具逐一比较原盒形与烘焙版本：每类 4,096 条固定种子的内外射线，共 **28,672 条**，命中状态一致，距离误差不超过 0.1 mm；Renderer 数量保持一致。
- 既有重叠放置检查 **21 项通过**，覆盖非凸模型间隙、内部空间和子碰撞体排除。
- 音源生命周期及声音行为 **9 项通过**：无批量即时分配、首次真实播放、禁用/销毁跳过、启用恢复、每对象唯一音源、无预热脉冲、空间参数、复用。
- 实际 Play Mode 玩法 **9 项通过**：优化书架仍挡射线，正常拾取/持有禁用碰撞体/投掷恢复，玻璃真实撞击书架后破碎；保留现有 **25 个碎片刚体、49 个可视碎片**。
- 超市重置：删除台灯后执行真实 ResetCurrentLevel，恢复 **309 件道具及 309 个就绪音源**，到达点引用重新映射正常。
- 空手和携带闹钟分别走真实家→超市电梯，到达前 309 个音源全部就绪；过渡期间无意外破碎或碰撞声呐。
- 两个生成脚本 dry-run 编译通过；最终 Editor 编译正常，Console 当前错误数为 0。退出测试 Play Mode，关闭测试用 auto-tick 和后台运行覆盖。

## Profile 结果及限制

同一 Editor、轻量 Profiler 模块、非 Deep Profile；首次样本各自重新进入 Play Mode。布置和脚本编译不在捕获窗口中。采样覆盖关门前至到达后约 4 秒。

| 捕获 | 有效帧 | CPU 中位数 | P95 | 最大帧 | 旅行请求到完成 |
|---|---:|---:|---:|---:|---:|
| 原 cold-lean，空手 | 567 | 14.273 ms | 80.944 ms | 232.964 ms | 8.07 s |
| final-empty，空手 | 749 | 11.635 ms | 62.007 ms | 134.670 ms | 11.43 s |
| final-held，持闹钟 | 626 | 16.950 ms | 68.925 ms | 141.466 ms | 12.04 s |

最终空手样本的 SonarObjectEmitter.Start 帧累计峰值从 68.622 ms 降至 0.069 ms；309 次 SteamAudioSource.Awake 分散到 156 帧，单帧最多 2 次。携带样本同样为 309 次/156 帧/最多 2 次。

仍然存在卡顿：空手样本一次原生 Steam Audio 初始化耗时达 52.48 ms，音源预热 marker 峰值 53.68 ms；持物样本相应峰值为 7.53/11.48 ms，说明单次分配有波动。整关备份主线程整合仍约 123–125 ms，空手样本导航峰值约 31 ms，物理每帧累计峰值约 61 ms（最多 8 个固定步）。本轮未改整关备份、导航收集或固定步设置。

曾尝试 2 ms 预算，但多数帧只能预热 1 个音源，首次旅行约 17.38 s，因此最终改用 6 ms/2 个。以上是 Editor 的观测结果，各次总帧数量和阶段占比不同，未进行统计显著性实验；不能当作独立 Player 帧率保证。GPU 与 GC 分配字段在轻量捕获中无有效数据，0 不代表没有负载。需要进一步减少剩余尖峰时，应继续拆分整关备份，并在 Development Player 上复测。

## 证据与复现

- `Logs/CollisionOptimization/before-audit.json`、`final-audit.json`：全资产清单及最终碰撞体数量。
- 同目录 `shape-checks.json`、`audio-checks.json`、`gameplay-checks.json`、`reset-checks.json`、`instance-override-checks.json`，以及 `final-arrival.png`。
- `Logs/CollisionOptimization/Backup/`：迁移前的全部 123 个资产和 meta，包含开始任务时原有的工作区修改。不要用整个备份覆盖后续编辑。
- `Logs/HomeGroceryProfile/final-empty.data`、`final-held.data` 及相应 summary/events/metrics JSON；原基线 `cold-lean` 同目录保留。
- `AuditProjectColliders.Run("label")` 可重新生成资产审计；`OptimizeProjectColliders.Run(false, "label-")` 在临时内容上检查候选，不保存场景/预制体（若发现新形状，可能创建对应网格资产）。`Run(true, ...)` 才保存迁移。
- `VerifyShapes()` 是迁移前的形状对照入口；迁移后源预制体已没有足够 BoxCollider，不应将空结果误当成重新验证通过。音源、投掷和重置回归使用相应 `Verify*.cs`，在 Play Mode 执行。

设计参考：[Unity 6.6 Mesh Collider](https://docs.unity3d.com/6000.6/Documentation/Manual/class-MeshCollider.html)、[Compound colliders](https://docs.unity3d.com/6000.6/Documentation/Manual/compound-colliders-introduction.html)。保守合并静态表面的决定依据本项目审计与测试，并非“MeshCollider 普遍比 BoxCollider 更快”的结论。

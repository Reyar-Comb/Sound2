# test 场景闹钟卡顿排查（2026-11-14）

## 第一轮：优化前的排查结论

本轮确实通过 Unity CLI 采集并分析了 Unity Profiler `.data`，不是仅靠代码推断。
主要卡顿来自 **同步的视觉声呐遮挡射线采样**，不是本次闹钟的混响，也不是 GPU 算不动。
本节为第一轮排查记录：当时尚未修改生产脚本、Shader、预制体或场景默认值，仅新增两个诊断脚本。实际集成结果见文末第二轮。

## 环境与边界

- Unity 6000.6.6f1；场景 `Assets/Scenes/test.unity`；Editor Play Mode。
- Game View 2560 × 1440，VSync 0，targetFrameRate -1。
- Unity CLI 1.0.0，官方二进制及 SHA256 校验，保存在被 Git 忽略的 `Library/AgentTools/unity.exe`，没有修改系统 PATH。
- 使用已安装 Pipeline 1.0.0 的命令文档，Profiler 模块包含 CPU/GPU/Rendering/Memory/Audio/Physics；未启用 Deep Profile。
- 将实际场景中的 `PickupAlarmClock (1)` 移到拾取范围，调用真实 `PlayerPickup.TryPickUp`，未另建简化场景。
- 当前场景各闹钟 alarmClip 均为空，反射/混响配置为 false。不能据此推断未来配置了录音、反射的场景没有音频成本。
- 以下是 Editor+Profiler 的短时捕获，不是发布版保证值；有编辑器开销、Profiler 对大量逐条射线的记录开销和机器负载波动。

## 实测

每份捕获默认排除可能不完整的第一帧。基准为 240 帧，其余为 300 帧。

| 条件 | CPU帧中位数 | CPU P95 | 最慢CPU帧 | GPU中位数 | 超16.67ms帧占比 |
|---|---:|---:|---:|---:|---:|
| 手持，闹钟与其滴答停止；其他钟表保留 | 5.914 ms | 9.670 ms | 113.947 ms | 4.069 ms | 0.8% |
| 手持并响铃，其他钟表保留 | 6.963 ms | 29.803 ms | 144.927 ms | 3.454 ms | 6.0% |
| 仅手持闹钟响铃，停止其他钟表滴答 | 7.268 ms | 14.824 ms | 46.398 ms | 3.957 ms | 5.0% |
| 同上一条件，额外关闭点云 RendererFeature | 5.553 ms | 10.667 ms | 32.696 ms | 2.065 ms | 3.3% |

基准本身也有周期性大尖峰，不能把整帧最慢值都归因于手持动作。

### 尖峰调用链

1. 完整响铃捕获，frame 168：
   - CPU 144.927 ms，GPU 3.649 ms。
   - `TickingClock.Update` 同帧合计 138.372 ms。
   - 其中 **82,944 次 Physics.Raycast，102.974 ms**。
   - `82,944 = 6 × 13,824`：同一帧多个钟表滴答同步发射。7 个 TickingClock.Update 调用中并非每个都实际发射。
2. 隔离闹钟捕获，frame 88：
   - CPU 46.398 ms，GPU 2.352 ms。
   - `PickupAlarm.Update` 合计 26.071 ms，其中只有手持闹钟处于响铃。
   - **13,824 次 Physics.Raycast，20.142 ms**。
   - 剩余帧时间包含渲染、编辑器等，不能把 46.398 ms 全记为闹钟采样。
3. 当前 `SonarPulseField` 主波采样为六面各 48²：每次 13,824 条。
   - `PickupAlarm.ringInterval = 0.2`，约每秒五次主波采样，名义上每秒约 69,120 条。
   - 非主波使用六面各 24²，但闹钟当前属于 Main，不会自动得到低分辨率路径。
   - 当前版本已经使用单面 Texture2D 上传 + Graphics.CopyTexture，并非每次整份大 Texture2DArray.Apply；本轮不把旧版本上传问题当成已确认瓶颈。

## 不改生产代码的批量查询实验

`AgentScripts/BenchmarkSonarRays.cs` 在同一手持位置、12m 范围，用相同方向/层/触发器设置比较同步 Raycast 和 RaycastCommand.ScheduleBatch。
每档预热两轮，记录十轮。完成批次后读取结果；NativeArray 使用显式 Dispose。

| 每面尺寸 | 射线条数 | 同步中位数 | 批量中位数 |
|---|---:|---:|---:|
| 48 | 13,824 | 19.441 ms | 1.204 ms |
| 24 | 3,456 | 5.659 ms | 0.525 ms |
| 16 | 1,536 | 2.373 ms | 0.305 ms |

**不能将此表称为整帧或完整 Emit 的优化结果。** 方向/命令准备、Transform 同步、平面编码、纹理上传和全局参数发布不包含在这里；场景原有 Update 仍在运行。只说明批量物理查询值得优先集成验证。

结果核对：24/16档没有超过本脚本严格阈值的差异。48档一条：同为 Mouse collider，距离差约 0.000084m，法线分量差约 0.001；其余射线无超过阈值差异。这不是全部场景等价性证明；需要门缝、墙角、薄壁、移动物和背面设置回归。

## 优化顺序建议

1. **首选：同密度的 RaycastCommand 批量查询。**
   - 保留原有槽位、范围、频率、波速、颜色和手动触发语义，不先靠降低效果质量解决。
   - 持久复用 NativeArray，缓存六面方向，避免每次分配。
   - 初版可同帧 Complete，先验证真实完整 Emit/整帧收益；进一步异步需要处理音画发射时刻、禁用/销毁、任务缓冲区生命周期和最多一帧延迟。
2. **多个周期源错峰／集中采样预算。**
   - 避免六个滴答把所有工作集中在一帧；仅错峰不能解决单次闹钟约26ms更新，因此不能替代第1步。
   - 不应偷偷改变机械时钟节奏或音画同步。可将准备/任务调度前移，而非简单丢事件。
3. **可选质量档：高频局部源24或16，玩家主波保留48。**
   - 必须作为明确配置，不直接把闹钟塞进 Footstep bank；后者会改变容量和波被替换的行为。
   - 有门缝漏光／遮挡精度代价，需要视觉对照。
4. **其次：分项 Profiling 后再优化平面写入和上传。**
   - 增加 SyncTransforms、Commands、RayBatch、EncodePlanes、Upload、Publish 的 ProfilerMarker。
   - 评估六面一次提交的 staging 方案，但不把未测得的上传开销当作主因。
5. **静态源缓存只作为后续选项。**
   - 固定钟表可考虑缓存遮挡，但动态门/物体需要失效策略；手持闹钟会移动，不能无条件复用旧遮挡。
6. 本次不优先削减混响或继续改 Shader。关点云有约2ms量级GPU收益，但并未消除CPU尖峰。

## 复现命令

在项目目录运行（CLI绝对路径为 `H:/Code/Unity/Sound2/Library/AgentTools/unity.exe`）：

```powershell
$u = '.\Library\AgentTools\unity.exe'
& $u command editor.playmode start
& $u command script run --file AgentScripts/ProfileAlarmClock.cs --entry ProfileAlarmClock.Hold
& $u command script run --file AgentScripts/ProfileAlarmClock.cs --entry ProfileAlarmClock.Phase --args '["baseline"]'
# 等待若干帧稳定后录制
& $u command diagnostics profiler_connect --target editor
& $u command diagnostics profiler_record --frames 300 --areas CPU,GPU,Rendering,Memory,Audio,Physics --output-path Logs/AlarmProfiler/example.data
& $u command diagnostics profiler_record_status
# status=completed 后打开
& $u command diagnostics profiler_session --capture Logs/AlarmProfiler/example.data --confirm true
# 用返回的 sessionId 调用 profiler_summary / profiler_frame / profiler_markers
```

Phase 参数：baseline / full / alarm-only / render-off / audio-off。依次执行 full 再 alarm-only；后者会停止其他钟表，切回 full 不会自动恢复其滴答。每组独立 Play 会话最可靠。当前真实闹钟无录音，audio-off 对本场景不是有效音频负载对照。

退出前调用 Phase baseline 恢复 RendererFeature，再 editor.playmode stop；不要保存实验期间的运行时值。脚本仅供此场景诊断，不是游戏运行组件。

## 输出

`Logs/AlarmProfiler/`（Git忽略）保留四份 `.data`、各组 summary/session/slow-frame JSON、ray-benchmark.json 和 CLI 输出。
本轮未运行破坏性回归或修改 `.vscode/settings.json`，其原有修改保持不动。


## 第二轮：批量射线已接入（2026-11-14）

### 改动

生产代码只修改 `Assets/Scripts/Gameplay/SonarPulseField.cs`：

- 默认启用 `batchRaycasts`，保留同步回退开关，可临时关闭做 A/B，不需要改场景或预制体。
- 原有主波六面48²、其他波六面24²的方向提前缓存，范围、衰减裁剪、槽位、颜色、波速和触发频率不变。
- 持久复用 RaycastCommand/RaycastHit NativeArray，粗网格只使用对应的子数组；Disable 时释放，重新启用时重建。
- 当前物理场景每次发射只获取一次，层、Ignore Trigger、全局背面设置每次重新捕获。
- ScheduleBatch 每任务至少64条、每条只收集1个最近命中；同帧 Complete 后才编码/上传/发布，无跨帧延迟或未完成任务。
- 编码阶段只检查 colliderEntityId，不为每条命中解析托管 Collider 对象。
- 改用时间戳计时，去掉每次新建 Stopwatch 的托管分配。
- 保留已有单面上传流程，不改 Shader、不减少精度，也不改音频配置。

分项 Marker：`Sonar.Emit`、`Sonar.SyncTransforms`、`Sonar.PrepareRays`、`Sonar.RaycastBatch`、`Sonar.RaycastLegacy`、`Sonar.EncodePlanes`、`Sonar.UploadPlanes`、`Sonar.Publish`。

### 最终代码 A/B

同一次 test Play 会话、实际持有 `PickupAlarmClock (1)`、同一位置与朝向、同一渲染/音频设置。先录批量，再通过 Sampling(false) 切换同步路径，再恢复批量后暂停其他钟表。

每组录制约6秒，Profiler已有2000帧历史容量；保存了全部采集帧，没有只截取最后300帧。各组下表排除第一帧。

| 条件 | 分析帧数 | 完整Emit调用数 | Emit平均/次 | 同帧Emit最大合计 | 整帧CPU中位数 | 整帧CPU P95 | 最慢整帧CPU | 超16.67ms比例 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 同步回退，完整钟表+闹钟 | 972 | 65 | 16.190 ms | 95.080 ms | 4.654 ms | 7.787 ms | 102.977 ms | 4.0% |
| 批量查询，完整钟表+闹钟 | 927 | 65 | 3.331 ms | 34.377 ms | 5.448 ms | 12.111 ms | 48.583 ms | 1.4% |
| 批量查询，仅闹钟响铃 | 1137 | 30 | 2.423 ms | 3.141 ms | 4.876 ms | 7.602 ms | 17.894 ms | 0.1% |

Emit平均按 `Sonar.Emit.totalMs.sum / calls` 计算，包含同步Transform、准备、查询、编码、纹理上传和参数发布，不是只有射线阶段。完整场景两组均65次，平均发射成本约下降79%。同步回退也享有缓存方向和新编码流程，因此它并非对第一轮原源码的逐字复测，而是相同新版本中的查询路径对照。

必须注意：这轮批量组的整帧中位数/P95没有优于同步组，不能声称平均FPS全面提高或已消除卡顿。优势集中在采样重帧和最坏尖峰；背景负载、编辑器、GC和采集顺序会影响整帧分布。仍有六个钟表集中触发时约34ms的Emit合计，后续可针对命令准备和同帧并发继续优化，但本轮没有改变它们的滴答时序。

仅闹钟组最慢17.894ms的热路径为增量垃圾回收（约12.12ms），不是这次声呐发射。场景其他系统仍约有25KB/帧的托管分配；本轮没有改它们。

原始文件为 `Logs/AlarmProfiler/final-{batch-full,legacy-full,batch-alarm-only}.data` 及各自 session/summary/markers/slow-frame JSON。先前短时的 `optimized-full` 是中间实现，不代替本节最终数据。

### 回归结果

`AgentScripts/VerifyBatchedSonar.cs` 在最终代码上通过179项检查、210,816条射线的命中对象一致性核对，并对两路径实际上传的六面GPU纹理逐像素比较（允许小于0.005的浮点分量误差）。覆盖：

- Main / Footstep / Danger / Coin / Lightning 各自分辨率与槽位。
- 六面墙、细门、门缝、倾斜面、曲面、源点在Collider内部。
- 移动门后无需调用方手动同步、LayerMask运行时修改、全局Trigger开关不影响Ignore规则。
- 原有命中后改为无命中时，复用缓冲和GPU纹理不残留旧遮挡。
- 亮度/密度衰减对应的有效查询范围，Mesh背面开关。
- 连续三次禁用/重新启用，NativeArray释放与重建、禁用时拒绝发射、发射后销毁。
- 实际 SonarObjectEmitter 手动触发，以及真实 Rigidbody 碰撞触发通过共享波场同帧发布。
- 预热后连续10次完整批量发射：主线程托管分配 **0 bytes**。这不等于整个场景零GC。

`AgentScripts/VerifyConcurrentSonar.cs` 的11项回归另行验证活动波保留、过期槽复用、七钟+快速闹钟、各颜色槽隔离和实际GPU点云；前后同一测试物体均937个亮点。

Unity编译无错误。场景/预制体和原有 `.vscode/settings.json` 修改均未改动。

### 复测方式

先按前文 Hold / Phase(full) 建立实际持有场景，然后临时切换（仅Play模式，退出不保存）：

```powershell
& $u command script run --file AgentScripts/ProfileAlarmClock.cs --entry ProfileAlarmClock.Sampling --args '[false]'
# 记录同步对照后恢复默认批量
& $u command script run --file AgentScripts/ProfileAlarmClock.cs --entry ProfileAlarmClock.Sampling --args '[true]'
& $u command script run --file AgentScripts/VerifyBatchedSonar.cs --entry VerifyBatchedSonar.Main
& $u command script run --file AgentScripts/VerifyConcurrentSonar.cs --entry VerifyConcurrentSonar.Main
```

VerifyBatchedSonar / VerifyConcurrentSonar 会隔离运行时根物体创建夹具，并恢复根物体活动状态，但持物会因Player停用而释放。因此不要在一次Profiler录制中执行它们，下一轮采集应使用新的Play会话重新Hold。

# audiotest：手工波形 / 纯峰值声波

## 当前版本

新路径已改为**只读峰值包络**：没有频带、RMS 混合、Band Delay 或 FFT。原有手动波形保留，不修改原有模型预制体与 test 场景。

- **B**：球在 ManualWave（原有手工碰撞波）与 AudioDriven（所选撞击录音的峰值）之间切换。碰撞处理互斥，同一次碰撞不会重复发两套波。
- **V**：收音机开关；也支持原有交互。默认持续循环。
- 球沿用原有拾取、投掷、轻放。
- 看纯峰值强弱时，按 **3** 关闭近距离常亮显影，否则原有常亮点可能盖住峰值的明暗变化。**4** 仍只控制旧声呐路径。
- 保留用户已选的收音机 `tv` 录音，并重新烘焙为峰值；球的录音入口仍为空。

## 接入录音

1. 球选择 `Assets/Audio/Experiments/BallImpact.asset`；收音机选择 `Assets/Audio/Experiments/RadioLoop.asset`。
2. 将录音拖到 **Clip**，点击 **Bake peak envelope**。
3. 进入 Play。球按 B 对照；收音机自动循环，按 V 开关。

不要直接改物体的 ImpactSoundPool.clip；播放与分析应由同一个分析资源决定。修改录音或分析率后重新 Bake。如果 GetData 失败，手动将音频 Load Type 设为 Decompress On Load 后重试，工具不会自动修改导入设置。

球在启动/切换模式时让旧模式也使用所选的同一条撞击录音。未选录音时旧模式保留生成 ping，新模式不回退到假波形。这里读取的是碰撞**录音**，不是刚体接触力曲线。

## 如何让强弱更明显

选中场景中的收音机或球，展开 **AudioReactiveSonarEmitter → Visual**：

| 参数 | 当前默认值 | 调整效果 |
| --- | --- | --- |
| Gain | 1 | 输入增益。不要无脑增大，否则弱峰与强峰都饱和成白色，反而没有对比。 |
| Peak Floor | 0.05 | 低于此值的峰保持暗；增大可压掉背景小波动。 |
| Peak Ceiling | 0.8 | 达到此值才满亮；增大可避免过早饱和，减小则整体更容易亮。 |
| Response Exponent | 1.8 | 大于 1 压暗中弱峰；可试 2–3。小于 1 会抬亮弱峰、降低对比。 |
| Afterglow | 0.06 秒 | 减小可让强峰后的余亮更短，减少糊成一片；0 最省采样。 |

映射：`brightness = pow(clamp01((peak * Gain - Peak Floor) / (Peak Ceiling - Peak Floor)), Response Exponent)`，之后再乘距离衰减和撞击强度。

例如默认设置下，0.2 与 0.6 的原始峰约映射到 0.055 与 0.572，最终仍受距离、遮挡、点密度以及其他显示通道影响。这是白色点阵显影强度，不是有正负摆幅的示波器曲线。

**Gain、Peak Floor、Peak Ceiling、Response Exponent 支持 Play 中实时修改，不需重启收音机。** 其余可视参数在触发时快照，改完需停止再启动；Play 中调整不会自动保存到场景。

其他接口：Range / Wave Speed / Afterglow / Distance / Density / High Quality Occlusion，以及 Volume / Minimum Impact Speed / Cooldown / Loop / Play On Start。两个物体默认均使用24²遮挡采样；高质量48²会增加单次触发开销。旧球仍保留按撞击速度扩大范围的逻辑，比较时留意新旧路径范围与波速可独立配置。

`Trigger()` 支持 UnityEvent；`TriggerAt(position, strength)` 支持交互/机关，strength 仅影响可视强度。`Stop()` / `Toggle()` 控制播放。

## 3D 音频不变

独立 ImpactSoundPool.Settings 仍控制原有 3D / HRTF / Reverb / Reflection Mix / Physics、Linear、Curve 或 dB 衰减。峰值分析取自**干声**，不计算混响的反射声波。

房间沿用独立 Steam Audio 几何 `AudioTestGeometry.asset` 和材质 `AudioTestSurfaces.asset`，不更改全局声学质量。听觉衰减是声源→听者，可视衰减是声源→表面。

## 性能与数据

- Bake 只按时间窗扫描 PCM 的绝对值最大值，多声道不做相位相加，反相立体声不会相消。没有 FFT，也不计算 RMS。
- 默认60Hz，1字节/帧、-60～0dB量化，最多4096帧；长录音自动降低有效分析率，最低10Hz、最长约6.8分钟。保留绝对级别，不逐录音归一化。旧10通道实验数据可自动提取其原峰值通道。
- 量化值在触发上传时一次性解码为线性 RFloat 纹理。运行时没有 FFT 或逐帧 PCM 读取；Shader 没有频带循环或逐采样 dB 解码。每次可视采样用一组峰值（含余辉最多三组），最后只做一次对比映射。
- 分析纹理从4096×3 RGBA32改为4096×1 RFloat，容量4个声源，分析纹理内存降为原来的1/3；烘焙数据为原来的1/10。没有新增全屏通道/RT。
- 一次播放只采一份遮挡；循环不重复造波、射线或上传。瞬态可替换最早结束的瞬态，连续源不会被挤掉；4个槽全部为连续源时新触发失败。不占原有52个手动波槽。
- 24²每次3456射线，48²每次13824射线；可能有启动/碰撞尖峰，与循环期间的稳定开销区别看待。
- 使用DSP时间同步。一次性声音考虑调度等待和原音频池的前置静音；循环直接播放原录音，避免插入循环间隙。停止后只保留已发出、还在传播的余波。
- HUD缓存字符串，仅在状态变化时更新，避免每次OnGUI重复拼接。

## 第一版边界

静态遮挡在开始播放时快照；移动门、移动声源轨迹和反射可视化暂不支持。改变障碍后停止再启动。恒定峰值的录音可能形成较稳定显影，不人为添加假振荡。切换球模式时已发出的旧波可能还在传播，严格对照请等余波结束。

## 验证与本机采样（2026-11-15）

- Unity CLI 编译：0错误、0警告。
- Edit：13项峰值烘焙、反相立体声、频率无关性、绝对级别、对比曲线、旧数据迁移和Shader检查。
- Play：25项真实GPU点阵、实时对比调整、遮挡、3D设置、DSP、抢占和循环检查。
- ScenePlay：12项实际球碰撞、模式互斥、同录音对照及收音机循环检查。测试录音资源只作临时验证，不替换已保存的用户录音。

Profiler原始捕获与JSON在 `Logs/AudioReactive/`（Git忽略）。修改前180帧中CPU中位5.632ms、GPU中位2.852ms；最慢帧63.520ms，其中CollectGPUSamples占58.905ms。修改后在2560×1440 Play画面下捕获300帧中CPU中位4.896ms、P95为7.339ms、GPU中位3.439ms，没有超过16.667ms的帧。AudioSonar.Update平均0.010ms、0托管分配；它不是整个渲染或混响耗时。这些是短时间Editor采样，内容阶段、温度与编辑器状态并非严格控制变量，不能据此宣称固定性能提升或代表移动、投掷、长时间运行。

同轮机器快照：CPU负载77%，31.7GB内存中空闲6.5GB；RTX4060 Laptop利用率50%、显存4967/8188MiB、温度88°C；再次查询温度86°C时 `clocks_event_reasons.sw_thermal_slowdown = Active`。这是检测到的软件热限频状态，不等于已证明用户所有卡顿都由温度造成。没有关闭其他应用、改驱动、散热设置或全局帧率。

原始实现中的FFT仅发生在Editor Bake，并非运行时；当前已连Bake的FFT也去掉。不能把尖峰直接归因于FFT或混响，仍需在温度稳定、目标分辨率下做长时间与实际投掷对照。`ConfigureAudioReactiveSonar`拒绝覆盖已存在的audiotest场景。

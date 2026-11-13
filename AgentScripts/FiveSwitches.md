# 圆形大厅：五个玩法开关

打开 `Assets/Scenes/CircularHall.unity`，进入 Play。左上角面板可以直接点击，数字键 1–5 也可以切换。点击场景捕获鼠标，Esc 释放鼠标；WASD 移动，鼠标转向，左键或空格发射，R 清理全部球和声音。点击面板不会额外发射或捕获鼠标。

| 键 | 开关 | 行为 |
| --- | --- | --- |
| 1 | Ring shot | 一次水平 360° 发射 24 个球，朝向等间隔。 |
| 2 | Bounce count only | 不使用 lifetime，按场景碰撞次数删除球；默认 1 次，面板 +/- 可以改为 1–20 次。关闭后恢复原来的 lifetime。 |
| 3 | Impact distance curve | 使用主摄像机的可编辑曲线控制反弹音效远近音量；关闭后恢复 Steam Audio 的反比距离衰减。 |
| 4 | Flight sine | 球飞行时持续播放 440Hz 单声道正弦音；声音位置跟随球，并使用 Steam Audio 的 HRTF、遮挡和当前衰减。首次碰撞时保留一次撞击声，球消失，正弦音以 20ms 淡出结束。此模式始终忽略 lifetime，始终限制首次碰撞后结束。 |
| 5 | Sand fan | 一次朝视线方向的水平 70°、垂直 16° 扇形随机投出 32 个小球；直径 9cm，速度有轻微随机差异。 |

1 和 5 是互斥的发射模式，2、3、4 可以与任意发射模式组合。1、2、4、5 的改变适用于之后发射的球；3 会同时作用于已经播放的声音。

场景默认：2 和 3 开启，1、4、5 关闭，反弹次数 1，保留已有混响设置。飞行正弦音使用直接空间音频，不额外计算持续音的反射；碰撞声继续使用大厅几何的 Steam Audio 实时反射。

## 反弹音效的可编辑距离曲线

选中 Main Camera，在 BouncePlaytest Inspector 顶部编辑 **Impact Distance Curve**。也可在游戏面板点击 **Edit IMPACT curve in Inspector** 跳到该对象。

- 横轴：撞击点到玩家的距离（米）。
- 纵轴：音量增益（0–1）。
- 默认：0–0.5m 为 100%，1m 为 80%，3m 为 35%，6m 为 12%，12m 为 2%，20m 为 0%。
- 抬高左侧关键点，近处反弹更响；压低右侧关键点，远处反弹更轻。
- 第 3 个开关开启时生效，作用于整个反弹信号（包括该次撞击的混响）。Unity 距离曲线恒定 1，Steam Audio 的直达声物理距离衰减关闭，避免重复衰减。
- 关闭第 3 个开关，碰撞音恢复 Steam Audio 的 PhysicsBased 衰减。
- 可以在 Play 时试听修改；要长期保留场景组件上的曲线，请在退出 Play 后调整并保存场景。
- 这是可调的玩法音量曲线；房间反射仍由 Steam Audio 几何模拟计算。

飞行正弦音继续使用之前独立的指数衰减：`10 ^ (-AttenuationDbPerMeter × max(0,d-1) / 20)`；关闭开关 3 后恢复物理距离衰减。它不使用 Impact Distance Curve。

## 大厅中央固定嗡鸣

对象 **Hall Center Hum (H)** 位于 `(0, 1.2, 0)`，低频载波默认 110Hz，振幅 LFO 默认 0.6Hz、深度 40%。有一个小型绿色球体作为位置标记，不带碰撞体。音源固定，不随玩家移动，也不会被 R 键清理。

- H 或面板复选框开关嗡鸣。
- 面板可即时调整 Hum volume、Tone、LFO 频率、LFO depth。
- 嗡鸣由连续生成的单声道正弦基频加少量二次谐波组成，LFO 调制振幅。
- 嗡鸣默认关闭房间反射，使用 HRTF 和遮挡，并在空间化之后额外压低远侧耳朵的增益。`Far Ear Gain` 默认 0.12，声源正侧方时远侧耳朵额外乘以 12%；设为 1 可关闭这项额外分离。它保持独立的平缓距离曲线，不受反弹曲线或开关 3 影响。P 仍控制 HRTF；需要恢复房间反射时可在嗡鸣组件打开 `Room Reflections`，此时 M 控制其混响开关。
- 嗡鸣组件 `Distance Curve` 的横轴为米，纵轴为增益：默认 1m 100%、3m 85%、6m 65%、12m 40%、20m 18%、30m 0%。不会再额外乘以反比距离衰减。
- 编辑场景中 HallHumEmitter 组件并保存，可持久化默认频率、LFO、音量、远侧耳朵增益和距离曲线。运行面板修改用于当前 Play 会话。

在主摄像机的 BouncePlaytest Inspector 内还可调整：`Ring Ball Count`、`Sand Ball Count`、`Fan Angle`、`Fan Vertical Angle`、`Attenuation Db Per Meter`（只用于飞行正弦音）、`Sine Frequency`。飞行音频率修改用于之后新发射的球。

最多同时保留 96 个球；达到上限后需等待球结束或按 R 清理。每批球的声源增益按球数平方根归一化，减少齐射造成的音量叠加。球之间及球与玩家之间不发生碰撞。32 个撞击音声源满载时会复用旧声源。

## 运行验证

在 Play 模式通过 Unity CLI 执行：

```sh
unity command script run --file AgentScripts/VerifyFiveSwitches.cs --entry VerifyFiveSwitches.Main --project-path /Users/reyar/unity/Sound2 --json
```

结果写入 `/tmp/Sound2-FiveSwitches.json`，约十几秒后 `complete` 为 true。验证包括：寿命与次数模式、1/2 次碰撞、环射、扇形、正弦波及销毁、环射组合持续音、实际听者输出距离差异。实际输出比较会暂时关闭混响、遮挡和双耳 HRTF，以隔离距离增益。测试会将玩家移回大厅中央，请在独立的运行测试期间执行；结束后退出 Play 可恢复保存的场景配置。

验证固定嗡鸣与曲线独立性：

```sh
unity command script run --file AgentScripts/VerifyHallHumAndCurve.cs --entry VerifyHallHumAndCurve.Main --project-path /Users/reyar/unity/Sound2 --json
```

结果写入 `/tmp/Sound2-HallHumAndCurve.json`。测试会临时静音嗡鸣、关闭混响和 HRTF，以比较反弹音效输出；同时验证修改反弹曲线不会改变嗡鸣或飞行音的衰减。

验证直达嗡鸣与左右耳差异：

```sh
unity command script run --file AgentScripts/VerifyDirectHum.cs --entry VerifyDirectHum.Main --project-path /Users/reyar/unity/Sound2 --json
```

结果写入 `/tmp/Sound2-DirectHum.json`。测试临时暂停移动和 LFO、关闭遮挡，采集前方、左右侧、1/6/12m 的双声道听者输出，并恢复测试前设置。

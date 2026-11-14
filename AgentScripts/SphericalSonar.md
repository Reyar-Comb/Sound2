# 球形声波：复用队友的轮廓效果

打开 `Assets/Scenes/SonarHall.unity`，进入 Play。

- 左键 / Enter / 左上角按钮：发射。
- WASD / 鼠标：移动和转头；Space / 左 Shift：上升和下降；Esc / Tab：释放鼠标并暂停移动，点击面板外恢复。
- F：自动连续发射；R：清空声波；F1：隐藏面板。
- H：原来的中心嗡鸣，默认静音。

显影直接使用队友的 `BlackBoxPointCloudFeature` 和 `BlackBoxScreenPoints.shader`，包括原有的 `Corner` 检测、`DrawPoints` 固定屏幕尺寸白点和轮廓合成。声波模式恢复原来的稀疏面点阵，星点关闭。轮廓点直径 3 px、间距 9 px（720p 参考），并使用原来的密度筛选，避免连成实线。按 1 / 2 独立切换面点阵 / 轮廓。原有 `Corner`、`DrawPoints`、`DrawFaces`、`DrawCorners` 函数和 Renderer Feature 未改写。

`SphericalSonar` 不创建点、线或 Mesh。它只记录每次发射的位置、时刻、球形范围，以及首个碰撞表面的遮挡数据。原 Shader 在采样轮廓时读取 `SonarRevealMask.hlsl`，按距离和时间控制轮廓的显现、淡出。声波关闭时，BlackBox 原有的近距离效果仍走原来的分支。

SonarHall 与 BlackBoxPointCloud 共用 `Assets/Prefabs/Player.prefab`，由 `PlayerController` 统一处理移动、视角和光标。声纳挂在 `Player/Main Camera`，只处理发射、显影、提示音和声纳面板。相机上禁用的 `BouncePlaytest` 仅保留大厅嗡鸣依赖的音频配置，不参与角色控制。

Player/Main Camera 的 Spherical Sonar 当前场景参数：Range 40 m、Wave Speed 10 m/s、Afterglow 0.4 s、Emission Interval 0.8 s。SonarHall 使用独立的 SonarDots 材质参数副本；点大小统一在 Spherical Sonar 的 Dot Radius 调整，间距、轮廓宽度可在材质调整。渲染仍使用队友的原 Shader。队友原场景的材质参数不受影响。

视觉传播刻意放慢；暂未接入反射后的二次显影或空间回声，发射声仍是短促提示音。遮挡采用 6 × 48 × 48 个方向采样，薄小物体及物体边界可能有采样误差。主动探测和物体发波共用 4 个槽，脚步另有 4 个槽，互不覆盖。脚步采用 6 × 24 × 24 方向采样，降低频繁触发的开销。

`VerifySphericalSonar.cs` 在 Play 下验证复用原渲染器、面点阵和轮廓开关、发射不生成 Renderer、遮挡数据、发射原点固定及 Shader 编译。自动发射关闭且冷却结束后运行。

`VerifySharedPlayer.cs` 在 Play 下用临时虚拟键鼠验证共享角色的移动、视角、升降、声纳按键及输入释放／恢复，并检查相机和监听器唯一性。测试结束恢复输入设置、角色姿态及自动发射状态。创建场景时自动使用共享 Player；已有旧场景可运行 `CreateSonarHall.cs` 的 `CreateSonarHall.ReusePlayer` 入口迁移，保留当前声纳参数及音频引用。

## 方块自主发声

大厅内的 Cube 已挂上 `SonarObjectEmitter`，默认每 3 秒自动发射，范围 12 m，速度 7 m/s，余像 0.2 s。按 C 开关方块的自动发射。发射起点固定在方块当前的位置，沿用现有点阵显影；玩家移动不改变已发出的波。方块自己先显现，随后声波到达周围环境。初始视角朝向该方块，方便比较。

方块的参数在 Cube > Sonar Object Emitter 调整，与 Main Camera 的玩家发射参数独立。`Play Sound` 控制方块提示音；短提示音使用物体位置的 3D AudioSource 和现有 Steam Audio 空间化，暂不开启该提示音的房间反射。C 只停止后续自动发射，已发出的声波会自然结束。

## 独立显影开关

`SphericalSonar` 的 `Proximity Rendering`（按键 3）与 `Sonar Rendering`（按键 4）可分别开启、同时开启或全部关闭。运行时面板也提供两个复选框。同时开启时取两种显影亮度的最大值；R 清空声波后靠近显影仍可见。默认保持仅声波模式，声音和发射逻辑独立于显影开关。


## 正式的声波距离配置

玩家、方块和脚步组件统一使用 `SonarDistanceSettings`，在各自 Inspector 的 `Distance Settings` 折叠栏中调整，三者各持有独立配置：

| 参数 | 用途 |
| --- | --- |
| Enabled | 是否启用视觉距离衰减；关闭时恢复原显影强度 |
| Fade Start Distance | 开始减弱的距离，单位米 |
| Fade End Distance | 完全不可见的距离，实际不超过 Range |
| Falloff Power | 越大越快变暗 |
| Pulse Strength | 初始视觉强度，0–1 |

旧版平铺参数会自动迁移。当前 SonarHall 保留玩家 0.5–3m / 指数 2 / 强度 0.65、方块 3–10m / 指数 2 / 强度 0.65，两者都启用。新添加的玩家或物体组件默认关闭衰减。

玩家位置：`Player/Main Camera > Spherical Sonar > Distance Settings`。按 5 或面板 `Distance attenuation` 只切换玩家随后发出的主动波。方块位置：`Cube > Sonar Object Emitter > Distance Settings`，不受按键 5 控制。按 C 只控制方块自动发射；组件菜单 `Emit sonar now (Play mode)` 可单次发射。

参数在发波时固定，距离按固定世界原点计算，调整参数不会改变旧波。显影亮度为 `strength * pow(1 - smoothstep(start, end, distance), power) * timeFade²`，到达 end 后直接跳过。多个波取最大亮度，音频音量、点大小、密度和轮廓检测不变。`Volume` 控制声音，`Pulse Strength` 控制画面亮度。

## 移动脚步波

SonarHall 的 `Player` 根物体已经添加 `Footstep Emitter`，不修改共享 Player prefab。按 6 或面板的 `Footstep pulses` 可切换。它读取角色实际水平位移，每行进 1.5m 触发一步，最快间隔 0.18s。停下、原地转头、顶墙、释放输入和纯升降不触发；单帧大幅传送会重置步距。

默认脚步波从胶囊底部上方 0.12m 发出：Range 3m、Wave Speed 8m/s、Afterglow 0.3s；Distance Settings 启用，0.4m 开始衰减，3m 完全消失，指数 2，强度 0.6。希望更局部时减小 Fade End Distance；希望近处更亮时增加 Pulse Strength；希望消退更快时减小 Afterglow。每一步有独立固定原点，玩家走远后旧波不会跟随。

当前角色支持自由升降，`Require Grounded` 默认关闭；以后换成贴地行走控制时打开，可屏蔽空中脚步。距离按水平路径累计，所以斜向移动也按实际路程触发。脚步专用 4 个波槽，不覆盖主动探测或方块波；采样更稀疏，极薄物体和边界仍可能有遮挡误差。

音频素材后续接入：在同一组件填入 `Audio Source` 和 `Clips`，每步轮流 `PlayOneShot`；`Volume` 调整音量。未指定素材时只显示脚步波。需要世界固定位置的音效和空间反射时，可用 `On Step (Vector3)` 事件或 C# `Stepped` 事件接已有空间音效池；传入位置与声波原点一致。

体验脚步局部探索时，关闭 `Proximity Rendering`（3），开启 `Sonar Rendering`（4），关闭主动自动发射（F）和方块自动发射（C），按 R 清波后开始走动。独立的靠近显影仍可同时开启，但它不会受脚步距离配置限制。

`VerifyPulseAttenuation.cs` 检查实际画面的距离递减、范围外全黑、关闭后逐像素一致、混合独立参数、零强度及玩家/物体接入。`VerifyFootsteps.cs` 使用真实输入帧检查移动触发、停步、顶墙、转头、纯升降、传送、贴地选项、事件、按键 6 和专用波槽隔离。

## 声波激活面点阵

声波现在同时激活面点阵和轮廓。面点阵以每次发波的固定原点为中心，直接共用原 FaceDensityAt 的完整核心区、外圈、距离分档和 OrderedRank 规则。图案按该波的有效范围（Range 与启用的 Fade End Distance 取较小值）相对原材质 Reveal Distance 缩放，因此保留中心密、逐圈变稀的分布，也不受玩家所在位置限制。有效范围等于原材质范围且原点一致时，面点结果与原靠近显影一致。主动波、方块波、脚步波统一生效，距离衰减、遮挡、到达时刻和余像保持有效。多个声源分别选择面点后取最大亮度，避免混用其他波的范围或密度。

脚步地面的核心区保持较密，向外逐圈稀疏；投影圈之外仍使用最外圈的稀疏档，随后由该波的距离衰减完全消失。原靠近显影继续以玩家为中心、比例 1 调用同一套墙面投影、核心和外圈规则，并保留原投影圈外不显示的边界。按 1 控制面点阵、2 控制轮廓；可关闭轮廓来单独观察面点。面仍是稀疏点阵，不会填成实心面；点大小继续和轮廓共用 Dot Radius。

`VerifySonarFaces.cs` 检查远离玩家的平墙面点、脚步槽、面开关、距离硬裁剪、独立波混合、到达时间及余像，并逐像素比较原密度图案、统计核心与外圈实际点密度；`VerifyFaceCloud.cs` 继续逐像素验证原靠近点云。


怪物声波也复用上述点云：SonarHall 的 Patrolling Monster (invisible) 上，MonsterScanEmitter 根据玩家距离在 0.65–3.5 秒间调整扫描周期。红波使用独立 8–11 槽，与玩家/脚步共用同一空间采样与疏密算法。地图巡视、脚步与投球引怪参数见 `Docs/MonsterGameplay.md`。

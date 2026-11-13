# 球形声波：复用队友的轮廓效果

打开 `Assets/Scenes/SonarHall.unity`，进入 Play。

- 左键 / Space / 左上角按钮：发射。
- WASD / 鼠标：移动和转头；Esc / Tab：释放鼠标。
- F：自动连续发射；R：清空声波；F1：隐藏面板。
- H：原来的中心嗡鸣，默认静音。

显影直接使用队友的 `BlackBoxPointCloudFeature` 和 `BlackBoxScreenPoints.shader`，包括原有的 `Corner` 检测、`DrawPoints` 固定屏幕尺寸白点和轮廓合成。声波模式恢复原来的稀疏面点阵，星点关闭。轮廓点直径 3 px、间距 9 px（720p 参考），并使用原来的密度筛选，避免连成实线。按 1 / 2 独立切换面点阵 / 轮廓。原有 `Corner`、`DrawPoints`、`DrawFaces`、`DrawCorners` 函数和 Renderer Feature 未改写。

`SphericalSonar` 不创建点、线或 Mesh。它只记录每次发射的位置、时刻、球形范围，以及首个碰撞表面的遮挡数据。原 Shader 在采样轮廓时读取 `SonarRevealMask.hlsl`，按距离和时间控制轮廓的显现、淡出。声波关闭时，BlackBox 原有的近距离效果仍走原来的分支。

Main Camera 的 Spherical Sonar 参数：Range（默认 12 m）、Wave Speed（8 m/s）、Afterglow（1.4 s）、Emission Interval（0.8 s）。SonarHall 使用独立的 SonarDots 材质参数副本；点大小、间距、轮廓宽度可在该材质调整。渲染仍使用队友的原 Shader。队友原场景的材质参数不受影响。

视觉传播刻意放慢；暂未接入反射后的二次显影或空间回声，发射声仍是短促提示音。遮挡采用 6 × 48 × 48 个方向采样，薄小物体及物体边界可能有采样误差。最多保留四次发射。

`VerifySphericalSonar.cs` 在 Play 下验证复用原渲染器、面点阵和轮廓开关、发射不生成 Renderer、遮挡数据、发射原点固定及 Shader 编译。自动发射关闭且冷却结束后运行。

## 方块自主发声

大厅内的 Cube 已挂上 `SonarObjectEmitter`，默认每 3 秒自动发射，范围 12 m，速度 7 m/s，余像 0.7 s。按 C 开关方块的自动发射。发射起点固定在方块当前的位置，沿用现有点阵显影；玩家移动不改变已发出的波。方块自己先显现，随后声波到达周围环境。初始视角朝向该方块，方便比较。

方块的参数在 Cube > Sonar Object Emitter 调整，与 Main Camera 的玩家发射参数独立。`Play Sound` 控制方块提示音；短提示音使用物体位置的 3D AudioSource 和现有 Steam Audio 空间化，暂不开启该提示音的房间反射。C 只停止后续自动发射，已发出的声波会自然结束。

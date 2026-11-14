# SONICSPACE 房间开始菜单

入口为 `Assets/Scenes/BaiYu-Test.unity` 中的 `SONICSPACE Entry`。白色密闭立方体房间替代频谱表演，黑色点阵显示房间棱角、随机墙面声波及星点。鼠标移动控制独立的菜单视角，平滑跟随水平转向和上下俯仰；手柄右摇杆也可转头。游戏玩家仍暂停，不会随菜单转动或移动。

主标题 SONICSPACE 的实际字形几何始终水平、竖直居中，不随转头移动，也不再上下漂浮。保留约 0.55 秒后开始的逐字出现；完整标题出现后，点位独立随机闪动或非线性淡出／淡入，绝大多数点持续可见以维持可读性。使用现有 SECONDERSIGN 字体。菜单只保留英文标题，不再创建中文副标题、开始文字或按钮。

房间在 shader 中按真实三维射线与立方体六个面求交，观察点固定在立方体的几何正中心（0, 0, 0），与六面墙等距；鼠标仅改变朝向。黑点投影使用游戏共用的左上角屏幕网格及圆点尺寸；旋转只改变看见的墙面和棱角，不扭曲网格。随机波源位于墙面，按空间距离等待传播到达，再使用与游戏声波相同的平方余辉衰减。所有房间点避让完整 UI 字形边缘，主标题点暂时消失时也不会被背景点填入。

开场就绪后，在画面任意位置按鼠标左键（或 Enter／空格／手柄确认），进入 2.6 秒的网格消散。白色表面在原格点处收缩成白点，按中心向外的随机时序，混合直接熄灭、柔和渐隐及闪动熄灭。没有点位平移、漩涡或透视拉伸；末尾露出游戏画面并恢复声音、输入和 F0 字幕。保留按住输入时的防误触及入场点击消费逻辑。

星点固定在空间方向上，随视角转动投影，不黏在屏幕上。菜单为黑色星点，使用独立随机周期柔和显隐，并避让完整标题；游戏为白色星点，独立于近距离显现、声波和表面闪烁开关，沿用 Stars 样式开关。两者都对齐原有屏幕点阵，游戏星点也服从准心、字幕和交互 UI 的避让。

## 可调参数

SonicSpaceStartMenu：Room Size 默认 10m，Room Field Of View 默认 80°，Mouse Sensitivity 默认 0.18，Look Smooth Time 默认 0.065 秒，Title Shimmer 默认 1（0 可关闭随机显隐）。保留 Title Delay、Character Interval、Title Dot Scale（1.65）、Title Point Density（0.5）及 Transition Seconds。

实现沿用 uGUI/TMP 字形遮罩，英文主标题占红通道。不创建新 EventSystem、不改输入资源和项目设置。遮罩纹理仅在初始化／分辨率变化时分配，退出或禁用菜单时释放图形资源并恢复暂停状态。

## 验证

- `AgentScripts/VerifyMenuRoom.cs`：27 项实际 GPU／输入检查，包括居中、房间、星点、显隐、网格和游戏隔离。报告和截图在 `Logs/MenuRoom/`。
- `AgentScripts/VerifyStartMenu.cs`：14 项真实任意位置点击、无按钮／中文菜单、暂停与输入交接、过渡结束和 F0 字幕检查。
- `AgentScripts/VerifyVinesAndStars.cs`：22 项空间星点、贴面藤蔓、动态消退、UI 与实体遮挡检查，报告在 `Logs/VinesStars/`。
- `AgentScripts/VerifyMenuDissolve.cs`：6 项网格消散 GPU 检查，移除了已取消的频谱断言。
- `AgentScripts/VerifyMergeBuild.cs`：Windows x64 构建验证。

旧 VerifyMenuMotion 的漂浮／频谱验证已由 VerifyMenuRoom 替代。ConfigureStartMenu 可更新已保存的测试场景菜单参数；新建场景仍由 ConfigureBaiYuTest 创建同一菜单组件。

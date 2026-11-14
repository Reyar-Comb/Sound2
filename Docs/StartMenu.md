# SONICSPACE 开始菜单

入口在 `Assets/Scenes/BaiYu-Test.unity` 的 `SONICSPACE Entry`。进入 Play 从纯白开始，横贯画面的细点线快速浮现，随后展开为离散的竖直频谱柱。每柱由共享网格上的一列圆点组成，柱间留两列空位，各柱按独立相位柔和起伏，整体振幅与活跃宽度逐渐扩大。约 0.55 秒后居中逐字出现 S → SO → … → SONICSPACE，每字间隔 0.13 秒；已有字符平滑向两侧让位，各字符通过独立平滑噪声持续轻微上下漂浮。副标题“声 之 域”和“开 始”随后浮现。所有字形使用 GameUiTheme 中的 SECONDERSIGN 字体，中文缺字检测通过。

文字点位密度为原来的 50%，点径为场景基础点径的 1.65 倍（保留圆点间隙）；屏幕网格坐标、720p 缩放与有序筛选规则仍与游戏共用。声纹使用较细的点，位于文字下层，并根据每帧真实字形向外留出约 7.5 像素的避让空间。

开场完成后点击开始，或按 Enter／空格／手柄确认键，播放默认 2.6 秒的网格消散。所有圆点始终位于原屏幕网格中心，没有旋转、漩涡、平移或透视拉伸。白色表面在原格点处收缩成白点，消散时序由距屏幕中心的距离加上固定的逐点／局部随机延迟决定，形成从中心向外扩散的不规则前沿。每个点独立采用直接熄灭、柔和非线性渐隐、随机闪动后熄灭三种方式之一；同一圈的点不会同步消失。最后黑场平滑露出游戏，并接入游戏声波。可调参数为 `Title Delay`、`Character Interval`、`Title Dot Scale`、`Title Point Density`、`Character Float` 和 `Transition Seconds`。开场计时从首个可绘制帧开始，并限制加载卡顿对时间推进的影响，避免跳过白场和字母。

`SonicSpaceStartMenu` 使用 uGUI 全屏显示和 TMP 字形遮罩，不创建新 EventSystem，不更改输入资源或项目配置。遮罩纹理只在进入菜单和分辨率变化时分配，复用 CommandBuffer 每帧更新字符位置，结束时释放。已有游戏场景在白底后初始化；菜单持有 Time.timeScale、AudioListener.pause 与玩家输入的暂时控制权，动画用未缩放时间推进，结束或被禁用时恢复原值。楼层字幕队列保留到进入游戏才开始播放，开始点击不会传给拾取或声波逻辑。仅该测试场景新增入口组件，其他场景原入口保持不变。

验证入口：

- `AgentScripts/VerifyStartMenu.cs`：真实鼠标点击、输入隔离、中文、阶段截图、重复开始、实际时间过渡和 F0 字幕，共 14 项。
- `VerifyStartMenu.Resolutions`：1280×720、1920×1080、800×600、2560×1080 的实际 GPU 字体点阵，确认无越界、圆点对齐同一屏幕网格。
- `AgentScripts/VerifyMenuMotion.cs`：21 项动画/GPU 检查，涵盖白场、横线、居中逐字、独立漂浮、点数约减半、加粗、声纹避让、中心塌陷与无白屏闪烁，截图和结果在 `Logs/MenuMotion/`。
- `AgentScripts/VerifyMenuDissolve.cs`：9 项 GPU 检查，验证竖直频谱柱的空列间隔、独立起伏、121 个过渡时刻中的三种消失行为、中心先于外围消散、同圈随机延迟及固定网格点位。实际采样 1062 个点的亮度重心，最大栅格化偏差 0.17 像素，无偏离网格的点；结果和截图在 `Logs/MenuDissolve/`。
- `AgentScripts/VerifyColoredSonar.cs`：共享点阵规则提取后的 29 项声波渲染回归。
- 菜单中途禁用：恢复游戏时间、音频、输入，无暂停状态残留。

截图与结果保存在 `Logs/StartMenu/`。`AgentScripts/ConfigureStartMenu.cs` 可给已保存的 BaiYu-Test 添加或更新入口；`ConfigureBaiYuTest.cs` 新建场景时也会创建菜单。

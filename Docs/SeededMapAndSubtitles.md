# 种子地图、层数与字幕

## 使用入口

打开 **Tools → Sound2 → 关卡编辑器**，将“生成模式”切到 **种子程序化**。手工布局模式及已有场景地图保持原样。

可直接打开 `Assets/Scenes/SeededMapDemo.unity` 并运行，或用“创建独立程序化场景”创建新的场景。选择场景内的 `Seeded Map` 后，可输入“预览层数 / 种子”，点击“预览此层”查看该层的俯视几何。预览是临时对象，不会把运行时网格写入场景；实际游戏从 F0 开始。

编辑预览使用亮色材质且不生成顶面，包含资源物体也使用预览材质。实际运行时沿用黑色点云，并在墙高 3.2m 处自动封顶。F0 完整封顶；F1 起在局部原点保留 2×2m 的上层竖井入口，确保玩家能从上一层穿顶落下。当前层的出口仍向下敞开，出口上方有正常顶面。点云玩家的配置与手工导出共用 `LevelBuilder.ConfigurePointCloudPlayer`，近处持续显影默认关闭。

## 生成规则

- **层数就是种子**：F0 使用 0、F1 使用 1，以此类推。相同楼层和增长配置产生相同的房间、连接、竖井和资源位置，不依赖 Unity 的全局随机状态。
- 每层有一个安全的初始房间，生成树保证房间连通，再添加可用的回路。出口在远离入口的房间，避免落地后直接跌进下一个竖井。
- 默认房间数为 `5 + 2 × growth`，资源数为 `6 + 3 × growth`。`growth = min(层数, growthLimit)`；默认增长至 F24，Inspector 可调至 F100。达到上限后仍使用不同楼层种子生成地图，但规模停止膨胀。
- 默认 F0 为 5 房间 / 6 资源；F5 为 15 房间 / 21 资源；F12 为 29 房间 / 42 资源。资源是可左键收集的物体，走同一套交互圈和遮挡检查。
- 初始准备 F0 和 F1。完成下落时准备再下一层，最多保留上一层、当前层和下一层，释放更旧楼层的网格、碰撞和未收集资源。

`LevelMap` 是新模式的入口。`SeededFloorPlan` 负责确定性数据，`SeededFloorGeometry` 在运行时构建地板、墙体和空心竖井，原手工地图的 `LevelLayout` / `LevelBuilder` / Clipper2 流程不变。

## 楼层变化与数值

`PlayerFloorProgression.CurrentFloor` 初始为 0。检测玩家上一帧与当前帧的运动线段是否向下穿过竖井出口平面，并检查是否位于井口宽度内。只接受当前层到下一层的连接；向上穿越、井外下落、重复穿越同一出口都不会增加层数。

`Assets/Resources/FloorProgression.asset` 提供默认增长规则和每层覆盖项：

- `difficulty`：供后续敌人、资源消耗、事件系统使用。
- `sonarRangeMultiplier`、`sonarAfterglowMultiplier`：已应用于玩家声纳，相对于进入游戏时的基准值计算，避免重复乘算。
- `entrySubtitles`：该层进入时依次播放的字幕。当前 F0/F1/F2 文案是可替换的初始引导。
- 未单独配置的层使用增长公式与 `defaultEntryText`；`{floor}` 会替换成层数。

扩展点：`FloorChanged(FloorRule)`、Inspector 中的 `onFloorChanged(int)`。资源收集累计在 `CollectedResources`，通过 `onResourcesChanged(int)` 和资源上的 `onCollected(int)` 接入后续玩法。

## 字幕和字体

共享 Player 预制体已配置 `PlayerSubtitles` 和 `PlayerFloorProgression`。右上显示当前 `F数字`，底部显示带淡入淡出的字幕。

- `Show(text, duration)` / `Enqueue(SubtitleCue)`：加入队列。
- `Replace(cues)`：替换旧队列，楼层切换使用此入口。
- `Clear()`：清空字幕。
- 打开菜单、禁用玩家或暂停时间时，字幕计时暂停；玩家输入暂停时隐藏 HUD。

字体从 `E:/Downloads/Miscellaneous/SECONDERSIGN.ttf` 原样复制到 `Assets/UI/SECONDERSIGN.ttf`，SHA-256 已核对。`GameUiTheme.asset` 统一配置 TMP 和旧式 IMGUI/uGUI 字体。主 SDF 预烘焙常用字符；动态回退仍使用同一个 TTF，1024×1024、清除构建时动态数据；缺失中文可回退到项目原中文字体。当前入层字幕无缺字。

## 所有玩家 UI 的点阵避让

`PlayerUiPointMask` 收集可见 uGUI 文字的实际字形边界、控件边界和交互圆环范围，旧 IMGUI 调试面板也登记遮罩。对附近完整的面点、轮廓点与星点进行柔和淡出，不裁切圆点，也不使用黑底覆盖文字周围。

`GameUiTheme` 的 `pointPadding` 和 `pointFeather` 控制留白和渐变，单位为 720p 参考像素。遮罩跟随 UI 位置、字幕内容、透明度与分辨率；隐藏的 UI 不留下遮罩。中心准心保留其圆形留白参数。

交互圈线宽由实际玩家相机的点阵渲染配置计算，等于 **点半径 × 2**，包含声纳半径及网格间距限制；`ringThickness` 仅在相机没有点阵渲染器时作为备用值。

## 验证入口

- `VerifySeededMap.Plans`：F0–F24 的确定性、可达性、资源增长和极大种子上限。
- `VerifySeededMap.Play`：独立加载新示例，真实重力连续下落三层、计层去重、安全落地、数值与字幕、资源收集、卸载旧层和线宽同步。
- `VerifyUiPointMask`：720p / 1080p 下偏离屏幕中心的矩形与圆形遮罩，以及透明 UI 不遮挡点阵。
- `VerifyInteraction`、`VerifyReticleRendering`：原交互行为与准心留白回归验证。

以上脚本位于 `AgentScripts`，通过 Unity CLI 的 `script run` 执行。

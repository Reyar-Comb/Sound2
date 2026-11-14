# 准心与交互提示

共享 `Assets/Prefabs/Player.prefab` 上的 `PlayerInteraction` 在运行时创建 uGUI HUD：中心白点半径 2，物体提示圆环半径 18，尺寸以 720p 为基准随屏幕高度缩放。提示锚定物体的世界位置，不随屏幕中心移动；0.28 秒内通过五次 smootherstep 从小点柔和扩张，失焦时在 0.18 秒内收缩淡出。切换目标先收回旧提示，再展开新提示。

面点、轮廓点和环境星点在准心周围半径 14 的区域内隐藏，外侧 6 像素渐变。遮罩作用于完整渲染点，并考虑点半径，不切割圆点。参数通过每个相机的 MaterialPropertyBlock 传入，仅应用于绑定玩家相机；打开菜单、禁用玩家或交互组件时 HUD 与留白一起关闭。

## 添加可交互物体

1. 给物体或碰撞体的父对象添加 `InteractableObject`，确保有启用的非 Trigger 碰撞体。
2. 可选设置 `Interaction Anchor`，用于瞄准判定、遮挡检查和圆环定位；默认使用物体原点，建议放在物体可见部分中心。
3. 在 `On Interact (PlayerInteraction)` 绑定点击事件，或继承该类重写 `protected virtual void OnInteract(PlayerInteraction player)`；基类方法为空，不包含物品效果。`CanInteract` / `TryInteract` 仍可重写。
4. `Interaction Enabled` 可控制物体当前是否接受交互。查询遵循玩家 `PlayerPickup` 的 `Pickup Range`、`Pickup Layers` 和 `Obstacle Layers`。

目标必须进入玩家范围、在相机前方、锚点位于准心 8 度内，且没有遮挡。当前目标额外保留 1.5 度容差以避免边缘闪烁。点击前再次检查目标状态，圆环切换期间不会点击尚未显示的新目标。

`InteractableObject` 可作为独立组件，与同一物体上的 `PickupItem` 共存。玩家只有一个左键入口：空手点击先调用该物体上的非拾取交互组件，再尝试拾取；只有拾取组件时仍直接拾取。手持时左键只抛出，绝不再次调用物品交互；右键仅放下。E 的拾取／投掷绑定已移除。拾取冷却期间不可拾取；独立交互组件按自己的启用状态判断。菜单打开时不处理交互。

电视、收音机、录音机、餐铃、游戏机、心率仪、戒指的 prefab 分别为 `Television`、`Radio`、`TapeRecorder`、`ServiceBell`、`GameConsole`、`HeartRateMonitor`、`Ring`，已加入地图编辑器下拉列表。新道具只挂空交互组件，不自动增加拾取、播放音频、切换电源等效果。闹钟额外挂独立交互组件，仍保留原有拾取、延时响铃和破碎行为。

本批模型位于 `Assets/Models/InteractiveProps`，每个道具一个 `.asset`；按材质合并网格以减少渲染对象。编辑时显示预览色，运行时由 `LevelProp` 切换为黑色点云材质。心率仪屏幕曲线只是静态模型细节，没有动态监测逻辑。

## 验证

`AgentScripts/VerifyInteractiveProps.cs` 验证七个模型配置、真实左键调用、E 无响应、双组件交互后拾取、抛出不重复交互、普通拾取兼容与遮挡。`AgentScripts/VerifyInteraction.cs` 验证准心、动画、范围、方向、遮挡、菜单暂停、真实鼠标输入、准确目标拾取、投掷冷却与销毁清理。`AgentScripts/VerifyPickup.cs` 验证 E 已移除及拾取物理行为。`AgentScripts/ConfigureInteraction.cs` 可幂等地把组件配置到共享玩家预制体。

`AgentScripts/VerifyReticleRendering.cs` 在 GPU 上分别运行面点、轮廓点和星点绘制通道，验证 720p / 1080p 下准心留白区内的渲染点为零，且面点和轮廓点在区域外正常保留。

新增：交互圈厚度自动跟随实际渲染点的直径；字幕、楼层标识和其他玩家 UI 也应用附近完整点阵的柔和隐藏。配置与新地图模式见 [SeededMapAndSubtitles.md](SeededMapAndSubtitles.md)。

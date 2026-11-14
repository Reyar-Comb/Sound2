# 准心与交互提示

共享 `Assets/Prefabs/Player.prefab` 上的 `PlayerInteraction` 在运行时创建 uGUI HUD：中心白点半径 2，物体提示圆环半径 18，尺寸以 720p 为基准随屏幕高度缩放。提示锚定物体的世界位置，不随屏幕中心移动；0.28 秒内通过五次 smootherstep 从小点柔和扩张，失焦时在 0.18 秒内收缩淡出。切换目标先收回旧提示，再展开新提示。

面点、轮廓点和环境星点在准心周围半径 14 的区域内隐藏，外侧 6 像素渐变。遮罩作用于完整渲染点，并考虑点半径，不切割圆点。参数通过每个相机的 MaterialPropertyBlock 传入，仅应用于绑定玩家相机；打开菜单、禁用玩家或交互组件时 HUD 与留白一起关闭。

## 添加可交互物体

1. 给物体或碰撞体的父对象添加 `InteractableObject`，确保有启用的非 Trigger 碰撞体。
2. 可选设置 `Interaction Anchor`，用于瞄准判定、遮挡检查和圆环定位；默认使用物体原点，建议放在物体可见部分中心。
3. 在 `On Interact (PlayerInteraction)` 绑定点击事件，也可以继承该类重写 `CanInteract` / `TryInteract`。
4. `Interaction Enabled` 可控制物体当前是否接受交互。查询遵循玩家 `PlayerPickup` 的 `Pickup Range`、`Pickup Layers` 和 `Obstacle Layers`。

目标必须进入玩家范围、在相机前方、锚点位于准心 8 度内，且没有遮挡。当前目标额外保留 1.5 度容差以避免边缘闪烁。点击前再次检查目标状态，圆环切换期间不会点击尚未显示的新目标。

`PickupItem` 已继承交互组件。鼠标左键拾取圆环指示的物体，持有时再次左键投掷；原有 E 键最近物体拾取/投掷保留。拾取冷却期不显示提示，不接受鼠标拾取。UI 菜单打开时不处理鼠标交互。

## 验证

在 Unity Play Mode 使用 `AgentScripts/VerifyInteraction.cs` 验证准心、动画、范围、方向、遮挡、菜单暂停、真实鼠标输入、准确目标拾取、投掷冷却与销毁清理。`AgentScripts/VerifyPickup.cs` 验证原有 E 键和拾取物理行为。`AgentScripts/ConfigureInteraction.cs` 可幂等地把组件配置到共享玩家预制体。

`AgentScripts/VerifyReticleRendering.cs` 在 GPU 上分别运行面点、轮廓点和星点绘制通道，验证 720p / 1080p 下准心留白区内的渲染点为零，且面点和轮廓点在区域外正常保留。

新增：交互圈厚度自动跟随实际渲染点的直径；字幕、楼层标识和其他玩家 UI 也应用附近完整点阵的柔和隐藏。配置与新地图模式见 [SeededMapAndSubtitles.md](SeededMapAndSubtitles.md)。

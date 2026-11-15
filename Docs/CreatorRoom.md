# 造物主房间

打开 `Assets/Scenes/CreatorRoom.unity`，或用 Unity 菜单 `Tools → Creator Room → Open Scene`，然后进入 Play。房间启动时根据目录生成工位，当前有 56 件道具和场景设施。球、杯子、花瓶、闹钟和收音机在第一排。准星对着模型或工位时，左上角显示名称。

- **V** 在实体材质和游戏点云之间切换。两个视图都使用同一组物理和音效组件。
- **R** 重载房间，将玩家和道具送回初始位置，清理碎片、声音和计时，并恢复实体视图。
- **左键**交互、拾取或投掷，**右键**放下。拿起可投掷道具后，朝工位背板扔。对准收音机左键开关循环播放。闹钟在首次拾取后开始倒计时。

## 试听与接线

| 触发 | 配置入口 |
| --- | --- |
| 球、杯子等的非粉碎撞击 | `CollisionSonarEmitter` 设置速度门槛和冷却，填写「撞击录音」和「撞击音量」。录音留空时沿用 `SonarCueSource`。 |
| 咖啡杯、高脚玻璃杯、陶瓷花瓶和闹钟粉碎 | `PickupItem.Break Clip` 和「粉碎音量」。达到 `Break Speed` 时从碎片位置播放一次粉碎录音。 |
| 挂钟和闹钟的滴答 | `TickingClock.Tick Interval` 设置周期，在「滴答录音列表」中按播放顺序添加录音并分别设音量。每次滴答只播一条，跳过空项，播到末尾后回到第一条。列表留空时沿用 `SonarCueSource`。 |
| 闹钟倒计时后的响铃 | `PickupAlarm.Alarm Clip`、`Delay After Pickup` 和 `Ring Interval`。 |
| 购物车移动 | 给 `ShoppingCartRoll.asset` 指定可循环的轮声并点击 **Bake peak envelope**。实际贴地移动时，录音和连续峰值声波一起播放。停止后一起淡出，默认 0.12 秒。 |
| 收音机循环 | 测试房使用 `AudioReactiveSonarEmitter` 循环播放 `RadioLoop.asset`，用烘焙后的录音峰值驱动声波。`Volume` 调声音音量，`Visual` 调声波。预制体也可以配置自己的音效或交互事件。 |
| 空交互或缺少播放逻辑的道具 | 在 Prefab 根节点添加 `PropAudio`。现有 `InteractableObject` 会触发 `Interaction Clip`，也可以勾选 `Toggle On Interact`。周期和循环使用 `Playback Clip`、`Mode`、`Interval` 和 `Play On Start`。可分别设置音量、距离曲线、HRTF 和反射。 |

`PropAudio` 可通过物理接触触发 `Impact Clip`，将组件加在接收碰撞回调的 Rigidbody 或 Collider 对象上。自定义玩法可以从 UnityEvent 调用 `PlayInteraction()`、`PlayImpact()`、`PlayBreak()`、`StartPlayback()`、`StopPlayback()` 和 `TogglePlayback()`。会立即销毁本体的 `PickupItem` 使用自身的 `Break Clip`。为已有音效的触发点沿用对应组件的配置，避免同一事件叠播。录音槽为空时保留对应的物理、交互和视觉反馈。

撞击音量和每条滴答音量都乘以 `SonarObjectEmitter.Volume`。粉碎音量和录音驱动声波的播放音量分别在自己的组件上调节。修改道具的 Prefab 后，下次进入 Play 或在房间按 **R** 即可试听。

`CollisionSonarEmitter` 的「落地音效冷却」默认为 0.4 秒。可以在一条撞击录音里剪入落地和翻面两声，窗口内的第二次地面接触只发原有声波和怪物听觉事件。撞墙和粉碎仍按各自的条件触发。设为 0 可关闭这层音效冷却。

## 新增道具

- 把道具 Prefab 放在 `Assets/Prefabs`。给根节点加 `LevelProp` 后也会进入地图编辑器目录。
- 在 `Assets/Models` 下新增 Unity 可导入的独立模型、独立 Mesh 或模型 Prefab，会自动获得工位。为模型加道具 Prefab 后，目录会用 Prefab 替换独立模型的工位。玩家、球形闪电、通用拾取模板和破碎后的模型有各自用途，目录会跳过它们。
- 编辑器在导入、移动和删除资源时更新 `CreatorRoomCatalog.asset`，下次进入 Play 时按新目录摆放。菜单 `Tools → Creator Room → Refresh Catalog` 也可手动刷新。分享新道具时可一起提交目录的变更。

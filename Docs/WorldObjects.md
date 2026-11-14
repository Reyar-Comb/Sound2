# 物理道具、球状闪电与声波密度

体验入口为 `Assets/Scenes/BaiYu-Test.unity` 与 `Assets/Scenes/TeammateSonarHall.unity`。所有普通球、金币、棱晶和方块均为物理道具，没有自动收集、背包或资源入账；状态 HUD 只显示生命。操作与本轮验证见 [PhysicalItems.md](PhysicalItems.md)。

## 金币

`Assets/Prefabs/Coin.prefab` 使用 PickupItem、Rigidbody 和凸 MeshCollider；CoinPickup 只负责动画与白色点阵显影。出生时在地面上方 0.9m 浮动，第一次拿起后停止装饰浮动，交由持物与刚体物理控制。

金币仍使用稀疏白色点阵，密度 0.16，显影 0.55 秒；前 65% 时间从币面中心向外传播，每个点按共享声波的平方余辉衰减。环境小波周期 3 秒、范围 2m、速度 5m/s、余辉 0.45 秒。手持时重复小波以便观察；放下后恢复周期显影。其他声波不能把金币提前全部照亮。圆点沿用共享屏幕点阵尺寸、抗锯齿、准心与字幕避让。

## 球状闪电

`Assets/Prefabs/BallLightning.prefab` 保留 NavMeshAgent、BallLightning、SonarObjectEmitter 与 Trigger Collider；删除 MeshRenderer 和 MeshFilter。渲染器直接生成 512 个分布在球体内部的三维点（可调 64–1024），按相机投影吸附到共享屏幕点阵，形成真实的体积点云。前面的点不遮挡后面的点；场景深度仍遮挡墙后的点，UI 避让仍对整点生效。体积缓慢旋转，活跃时更快、更亮。

默认自动循环：平静 12 秒、活跃 7 秒；场景内没有状态开关。编辑器调试仍可调用 SetState，不影响默认玩法。

| 状态 | 发波周期 | 范围 | 强度 / 余像 | 玩家效果 |
| --- | --- | --- | --- | --- |
| Calm 平静 | 6 秒 | 2m | 0.25 / 0.25 秒 | 可穿过，无伤害 |
| Active 活跃 | 0.7 秒 | 8m | 0.95 / 0.65 秒 | 距胶囊表面 2m 内，每秒 20 伤害 |

闪电保持距地 1.35m 的高度，以 0.28m/s 在出生地附近 6m 范围游荡，不追踪玩家。种子楼层使用相对出生高度；原演示场景使用绝对高度。伤害仍检查节奏和墙体遮挡。PlayerVitality 默认 100 HP，耗尽显示提示，未新增死亡／复活规则。

闪电自身波为蓝色，玩家白波或靠近显影照出体积点时也保持蓝色。独立 GPU 绘制无需球面深度，也不再依赖最多 8 个球面的识别覆盖。

## 怪物避让闪电

PatrolMonster 增加 Evade 状态。怪物在 7m 感知距离内检查闪电位置及当前路径，平静状态避让半径 1.4m，活跃状态 3.2m，另加代理半径与 0.5m 安全余量。

已靠近危险区时选择可达的向外路径；原路径会穿过闪电时，选择保持安全距离的绕行目标。避让优先于听声调查，期间不被新声音引回危险区；脱离后恢复原巡视或调查。它使用共享 NavMesh 和路径距离检查，没有在场景中新增会阻挡玩家的实体墙。

## 密度衰减与范围上限

`SonarDensitySettings` 与亮度衰减 `SonarDistanceSettings` 分离。怪物 `MonsterScanEmitter / Density Settings` 默认开启，6m 开始减少点数，10m 降到零，超过 10m 无面点阵和轮廓点。

密度倍率为 `pow(1 - smoothstep(start, end, distance), power)`。面点云用它乘到原 FaceDensityAt 的密度，轮廓用它筛选同一 OrderedRank；保留点仍沿用原来的大小与位置，没有靠缩小点或只降低亮度来伪装稀疏。现有亮度/余像衰减仍单独作用。Range、亮度终止距离和密度终止距离中最小者决定实际可探索边界。

这个配置已接入正式通用物体组件，金币和闪电也可以独立调整。既有白波及脚步默认不额外开启新的密度衰减，因此保留原有图案。

## 渲染与波槽

共用 SonarPulseField，24 槽：0–3 主波、4–7 脚步、8–11 怪物、12–19 金币、20–23 闪电。各组循环，不互相覆盖。金币等局部波使用与脚步一致的 24×24×6 遮挡射线，主波保持 48×48×6。

共享 Shader 一次采样原普通网格与彩色网格，彩色网格存面/边亮度及各自的颜色编号，仍调用同一 FaceDensityAt、OrderedRank、圆点半径和准星避让逻辑。绘制按玩家白／金币白→蓝→红优先级组合，先清除对应圆点覆盖再绘制，避免重叠出现白色或混合成紫色。没有活动波时跳过对应颜色的合成。

## 验证入口

本轮验证见 [PhysicalItems.md](PhysicalItems.md)。ConfigureWorldObjects 与 ConfigurePhysicalItems 维护 prefab 及场景配置；VerifyColoredSonar 验证共享颜色、密度与 UI 避让；VerifyCoinPoints 验证金币外扩波纹。

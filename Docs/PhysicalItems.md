# 物理拿放与体积闪电

普通球、棱晶、金币和出生房间方块统一使用 PickupItem。左键／E 拿起或抛出，右键放下。取消资源收集、钱包组件和物品入账；HUD 仅保留生命及原有楼层提示。旧 MapResource 仅作为生成棱晶的兼容标记，自动要求 PickupItem。PlayerWallet 源码保留为未使用的历史接口，不再挂到场景或由玩法调用。

放下优先使用瞄准范围内的水平支撑面，否则探测手持位置下方；没有支撑则在手边零冲量释放。目的地与释放路径都必须无障碍，否则保留手中物品并显示中文提示。放下后的拾取冷却 0.12 秒，投掷沿用原冷却。靠墙回缩、刚体姿态同步与跨层父节点迁移继续生效。

PlayerPickup 的持物惯性由四元数指数追随和 SmoothDamp 位置平滑组成，默认旋转迟滞 0.12 秒、位置平滑 0.06 秒、最大角差 12°。以当前镜头位置计算偏移，避免走路时物品拖在身后；拾取及超过 2.5m 的瞬移重置惯性，墙体限制即时生效。

球状闪电默认自动循环平静 12 秒／活跃 7 秒，场景方块不再控制状态。BallLightning prefab 已删除球面渲染组件。BlackBoxPointCloudFeature 在 GPU 直接投影球体内部的三维样本，不依赖实体表面；默认 512 点，保留前后层点并缓慢旋转。使用场景深度遮挡、共享屏幕点阵网格／点半径／UI 避让和蓝色身份，仍遵守声波与近距离显影设置。

本轮更新 BaiYu-Test、TeammateSonarHall、金币／闪电 prefab 及楼层中文引导，未修改 ProjectSettings 或 Packages。

## 本机验证

- VerifyPhysicalItems：58 项通过，包括四类道具各三次拿放、投掷、真实右键输入、阻挡放下、惯性归位、瞬移复位、自动闪电循环。
- VerifyPickupAndShaftRegression：同球 8 轮投捡、贴墙、3 次下降及跨层携带全部通过。
- VerifyLightningCloud：9 项 GPU 检查通过，包括背面独占投影点（按像素亮度阈值统计，168/188 个格点中心可见）、体积旋转、墙体遮挡、UI 避让、显影模式及 shader 编译。
- VerifyColoredSonar：29 项共享颜色、密度、波槽、遮挡与 UI 渲染检查通过。
- VerifyCoinPoints：11 项通过，金币仍为稀疏白色点阵，中心向外传播且中心先消退；修正测试中新增插值刚体的定位与前帧残留等待。

报告和 GPU 截图位于 `Logs/PhysicalItems`、`Logs/CoinPoints`、`Logs/TeammateIntegration_20261114`。测试在 Unity Editor Play Mode 执行，未进行独立 Player 构建。旧 VerifyWorldObjects 的经济断言属于已移除玩法，不再作为现行入口；使用上述物理道具及渲染回归入口。

所有 PickupItem 在新建和载入时补齐碰撞声波组件，关闭定时发波／共享切换键；有效碰撞发布独立 AI 听觉事件。

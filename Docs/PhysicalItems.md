# 物理拿放与体积闪电

## 学校道具与场景分类（2026-11-15）

道具浏览器按“家、学校、超市、餐厅、图书馆、雪林、医院、通用”提供分类按钮与数量；“全部”按同一顺序分组展示。中文名称和 Prefab 名称搜索与当前分类共同筛选，未知道具默认归入“通用”。分类和中文名称统一维护在 `Assets/LevelEditor/Editor/PropCatalog.cs`，当前共 62 种：家 12、学校 5、超市 5、餐厅 4、图书馆 1、雪林 2、医院 5、通用 28。

### 医院移动病床与移动医疗车

“医院”分类新增 `MobileHospitalBed`（移动病床）与 `MedicalCart`（移动医疗车）。病床带床垫、枕头、两侧护栏、端部推手、升降支架造型和四脚轮；医疗车带工作托盘、双抽屉、下层置物盘、侧边收纳桶、推手和四脚轮，顶层支持吸附摆放其他道具。

两者均使用购物车的 `PushableProp`：玩家走向并接触物体即可推动，离开后减速停止，保持直立并受实体墙阻挡。复用购物车的低摩擦物理材质、移动录音／连续声波配置及碰撞反馈，不作为可拾取物品。病床质量 45、最大推速 1.4 m/s；医疗车质量 20、最大推速 1.8 m/s。脚轮与升降架是模型结构，不单独模拟轮轴或调节床高。

资产位于 `Assets/Prefabs` 和 `Assets/Models/HospitalMobileProps`；生成脚本为 `AgentScripts/ConfigureHospitalMobileProps.cs`，编辑器与运行验证分别为 `VerifyHospitalMobileProps.Edit`（26 项通过）、`VerifyHospitalMobilePhysics.Play`（40 项通过，保留 Steam Audio 服务的真实键盘推动测试）。报告及实际模型预览位于 `Logs/HospitalMobileProps`。

### 空货架与陈列道具

| 道具 | 分类 | Prefab | 尺寸及特点 |
|---|---|---|---|
| 双面空货架（三层） | 超市 | `EmptyDoubleShelf` | 高 2.1 米，与 `LibraryBookshelf` 实际模型等高；三块独立层板，前后开放，侧面支撑；不使用封住层间空间的整体碰撞盒 |
| 小方块 | 通用 | `ShelfBlock` | 边长 0.18 米，木质倒角，可分别吸附到货架各层的前后两侧 |
| 头戴式耳罩 | 通用 | `Earmuffs` | 弧形头梁、头部软垫、左右耳杯与内侧耳垫，高约 0.28 米 |
| 毛绒小熊 | 家 | `TeddyBear` | 坐姿棕色玩偶，圆耳、布面肚皮、脚掌、缝线及领结，高约 0.50 米 |

以上 Prefab 位于 `Assets/Prefabs`，模型与新增材质位于 `Assets/Models/DisplayProps`。均支持编辑器地面／层板吸附和保存，采用现有彩色编辑预览与黑色游戏材质约定；是摆放道具，不额外加入拾取或穿戴机制。生成脚本 `AgentScripts/ConfigureDisplayProps.cs` 默认不覆盖已有资产；`VerifyDisplayProps.Edit` 通过 50 项检查，报告和四格实际渲染预览位于 `Logs/DisplayProps`。

`LevelProp.surfaceAnchor` 描述道具默认吸附面的挂点。切换吸附面时，`LevelPropPlacement` 改用模型包围盒底面（地面）或背面（墙面），避免橱柜、壁画、镜子、花篮、钟表、黑板和广播切换地面后陷入地板，以及地面道具切换墙面后陷入墙体。预览、新增和重新吸附共用同一姿态计算；默认挂点和门的独立嵌墙逻辑保持不变。已放置的道具可通过“重新吸附到新位置 → 地面”修正。`VerifyPropSurfaceAnchors.Run` 对 55 种普通道具双向吸附、预览与实际网格位置、缩放后重新吸附、撤销重做和保存重开通过 339 项检查；报告在 `Logs/PropSurfaceAnchors/edit.txt`。

### 红色藤蔓区域

从“通用 → 红色藤蔓区域”放置 `Assets/Prefabs/RedVineArea.prefab`。选中实例后在关卡编辑器设置矩形宽度、长度、攀爬高度及每次生长源数量；默认范围为 6×4 米、高 3 米。支持旋转、撤销重做，以及场景／布局 Prefab 保存导出。编辑时的红色范围框在运行时隐藏。

只复用红色怪物的藤蔓，不包含红色扫描点阵或怪物行为。藤蔓在矩形内有静态碰撞体的地面和墙面上生长、分枝、消退，遵循真实遮挡，不产生实体碰撞；空中无附着面时不会生长。移动或缩小范围会立即清除旧轨迹。可进一步设置生长间隔、存活时间、枝条长度和附着表面层。范围宽／长上限各 100 米，高度上限 30 米；单个道具最多保留 768 个点，大范围可调生长源数量或放置多个区域以增加覆盖密度。

区域同时提供致死接触伤害：以旋转、缩放后的矩形体积中距玩家最近的点为起点，复用红色怪物的玩家胶囊体距离、同场景及墙体遮挡判定，默认接触距离 0.65 米、检查间隔 0.5 秒。有效命中从满血直接触发既有死亡／苏醒流程；仍遵守菜单、输入暂停、受伤无敌和重生保护。禁用区域或 MonsterVines 后不再造成伤害，编辑模式不执行伤害。

`AgentScripts/VerifyRedVineDamage.cs` 在 Play 模式通过 19 项检查，覆盖现有 Prefab 默认值、区域内外及高度边界、旋转缩放、墙体遮挡、满血致死、死亡事件去重、无敌保护、组件禁用和普通两次命中规则。

`AgentScripts/VerifyRedVineArea.cs` 的 `Edit` / `Play` 分别通过 20 / 24 项检查，涵盖范围参数、旋转、撤销、导出、贴地爬墙、真实渲染与遮挡、移动清理及大范围边缘裁剪。报告及渲染截图位于 `Logs/RedVineArea`。

新增学校预制体位于 `Assets/Prefabs`：

| 道具 | Prefab | 放置与模型 |
|---|---|---|
| 黑板 | SchoolBlackboard | 贴墙，木框绿板、粉笔槽、板擦与粉笔；宽 2.4m |
| 课桌 | SchoolDesk | 地面，木桌面、金属管腿、桌斗、书包挂钩与笔槽；桌面 0.66 × 0.46m，高约 0.76m |
| 课椅 | SchoolChair | 地面，木座面与靠背、金属管架及脚垫；座高约 0.45m |
| 粉笔盒 | SchoolChalkBox | 地面或桌面，开放盒体、白色与粉色粉笔；0.20 × 0.13m |
| 广播 | SchoolSpeaker | 贴墙，木质箱体、双扬声器、防护栅格和壁挂支架 |

本批为静态模型，使用现有 HouseholdProps 木材、钢材、炭黑等预览材质，仅新增黑板和粉笔材质；运行时沿用 `LevelProp` 的 PureBlack 材质切换。每件道具合并为一个持久化网格，存于 `Assets/Models/SchoolProps`，搭配简化 BoxCollider。墙面道具本地 +Z 朝外、背面锚点为零；落地道具底部为零。广播本轮仅制作模型，未新增播放交互。

`AgentScripts/ConfigureSchoolProps.cs` 创建资源，重复执行跳过已有预制体。`AgentScripts/VerifySchoolProps.cs` 的 Edit 入口通过 62 项检查，覆盖分类计数、真实按钮回调、分类与中英文搜索、模型资源、旋转布局吸附、粉笔盒桌面放置以及保存/导出/重新载入。报告为 `Logs/SchoolProps/edit.txt`，模型与面板截图为同目录的 `models.png`、`browser.png`；模型图右下角为原有椅子，供风格对照。

普通球、棱晶和出生房间方块统一使用 PickupItem。左键拿起或抛出，右键放下，E 已废除。取消资源收集、钱包组件和物品入账；HUD 仅保留生命及原有楼层提示。旧 MapResource 仅作为生成棱晶的兼容标记，自动要求 PickupItem。PlayerWallet 源码保留为未使用的历史接口，不再挂到场景或由玩法调用。

放下优先使用瞄准范围内的水平支撑面，否则探测手持位置下方；没有支撑则在手边零冲量释放。目的地与释放路径都必须无障碍，否则保留手中物品并显示中文提示。放下后的拾取冷却 0.12 秒，投掷沿用原冷却。靠墙回缩、刚体姿态同步与跨层父节点迁移继续生效。

PlayerPickup 的持物惯性由四元数指数追随和 SmoothDamp 位置平滑组成，默认旋转迟滞 0.12 秒、位置平滑 0.06 秒、最大角差 12°。以当前镜头位置计算偏移，避免走路时物品拖在身后；拾取及超过 2.5m 的瞬移重置惯性，墙体限制即时生效。

球状闪电默认自动循环平静 12 秒／活跃 7 秒，场景方块不再控制状态。BallLightning prefab 已删除球面渲染组件。BlackBoxPointCloudFeature 在 GPU 直接投影球体内部的三维样本，不依赖实体表面；默认 512 点，保留前后层点并缓慢旋转。使用场景深度遮挡、共享屏幕点阵网格／点半径／UI 避让和蓝色身份，仍遵守声波与近距离显影设置。

本轮更新 BaiYu-Test、TeammateSonarHall、可投掷物／闪电 prefab 及楼层中文引导，未修改 ProjectSettings 或 Packages。

## 本机验证

- VerifyPhysicalItems：47 项通过，包括三类道具各三次拿放、投掷、真实右键输入、阻挡放下、惯性归位、瞬移复位、自动闪电循环。
- VerifyPickupAndShaftRegression：同球 8 轮投捡、贴墙、3 次下降及跨层携带全部通过。
- VerifyLightningCloud：9 项 GPU 检查通过，包括背面独占投影点（按像素亮度阈值统计，168/188 个格点中心可见）、体积旋转、墙体遮挡、UI 避让、显影模式及 shader 编译。
- VerifyColoredSonar：29 项共享颜色、密度、波槽、遮挡与 UI 渲染检查通过。

报告和 GPU 截图位于 `Logs/PhysicalItems`、`Logs/TeammateIntegration_20261114`。测试在 Unity Editor Play Mode 执行。旧 VerifyWorldObjects 的经济断言属于已移除玩法，脚本已删除；使用上述物理道具及渲染回归入口。

金币 prefab 移除后，依赖它的 ConfigureCoinPoints、VerifyCoinPoints，以及会重新写入金币引导的一次性 ConfigureBaiYuChinese 也已删除。当前中文引导保存在 `Assets/Resources/BaiYuTestFloorProgression.asset`；新建测试场景使用 ConfigureBaiYuTest。


所有 PickupItem 在新建和载入时补齐碰撞声波组件，关闭定时发波／共享切换键；有效碰撞发布独立 AI 听觉事件。VerifyThrowableWorld 的 48 项检查覆盖四种对象两次真实撞墙、落地、近远怪物反应、八向藤蔓和后续无硬币楼层；报告在 Logs/ThrowableWorld。

## 可摔碎道具与延时闹钟

### 玩家推动候选评估（2026-11-14）

第一批已启用购物车和垃圾桶的玩家推动，具体规则见下节。其余候选仍为设计建议。`PlayerController` 通过 `CharacterController.Move` 移动，仅对明确挂载 `PushableProp` 的物体传递水平推动意图；普通家具和 PickupItem 保持原有行为。

| 优先级 | 道具 | 建议与依据 |
|---|---|---|
| 优先 | 购物车 ShoppingCart | 有四轮和推把，最符合推动操作；可用于移动遮挡、运物和制造轮声。现有 6 个复合碰撞体较简洁。建议允许地面平移与转向，限制翻倒。 |
| 优先 | 椅子 Chair | 体积小（约 0.48 × 0.96 × 0.48m），适合作为轻型可移动障碍；推动时产生拖擦声。现有 10 个碰撞体，启用物理前可适当合并。 |
| 优先 | 垃圾桶 TrashBin | 约 0.46m 直径、0.53m 高，适合轻推、碰撞与声音引诱；可根据玩法允许翻倒。现有 18 个碰撞体，建议先简化。 |
| 优先，但按尺寸限制 | 桌子 ResizableTable | 默认 2 × 0.8 × 1m，5 个碰撞体；适合拖动堵门或改变路线。推力、阻力应随尺寸变化，极大的桌子可设为不可推动。 |
| 次选 | 书桌 WritingDesk | 约 1.4 × 0.8 × 0.70m，有抽屉柜，适合较重的可移动掩体或堵门物。建议推得慢，阻力高于椅子。 |
| 特定谜题 | 图书架 LibraryBookshelf | 高 2.1m、深 0.42m，窄而高，装满书；适合揭露暗门或沿指定方向滑动。现有 112 个碰撞体，不能直接照搬为高开销的自由动态物；建议简化碰撞、限制倾倒。 |
| 特定谜题 | 冰箱 Refrigerator | 高 1.8m，适合重型障碍或一次性移开入口；不宜像购物车一样轻易滑动，可采用受限推移。 |

贴墙壁画、镜子、花篮、吊柜和挂钟建议固定。灶台按固定厨房设施处理，新增吧台也按固定家具处理；若以后需要移动吧台，建议另做短尺寸带脚轮版本。电脑、电视、收音机、录音机、游戏机、心率仪、餐铃和戒指更适合交互或另行设计拿放；已支持 PickupItem 的杯子、台灯、闹钟、球和花瓶继续走现有拿放流程。

当前已完成 **购物车、垃圾桶**；后续可加入椅子、桌子和书桌。初始资源审计（启用推动前）保存在 `Logs/WallFurnishings/push-candidates.csv`。

### 购物车与垃圾桶的玩家推动

直接走近并向物体移动即可推动，不需要按交互键，也不会拿到手中。只有侧面接触会施加水平推力，站在物体顶部或向下落不会推动；物体仍受重力和真实碰撞约束。推力按物理步处理，多碰撞体接触不会叠加成额外冲量。物体保持直立，可以绕竖直轴转向。

| 配置 | 购物车 ShoppingCart | 垃圾桶 TrashBin |
|---|---|---|
| 刚体质量 | 18kg | 7kg |
| 最大推动速度 | 1.8m/s | 1.2m/s |
| 最大推力 | 140N | 75N |
| 停止阻力系数 | 3 | 7 |
| 动/静摩擦 | 0.08 | 0.25 |
| 移动声波和听觉事件间隔 | 0.35s | 0.5s |
| 移动声波范围 | 5m | 4m |
| 怪物听觉基础范围 | 12m | 9m |

`PushableProp` 根据实际地面位移发出声波和听觉事件。速度至少 0.12m/s，累计移动至少 0.06m，并受上述时间间隔限制。静止顶墙、悬空下落和瞬移均不会触发移动反馈。移动声波使用原有 `SonarObjectEmitter` 和 `SonarPulseField`，同时发布 `GameplayNoiseKind.Movement`。视觉发声组件不可用时，AI 听觉仍独立生效。

购物车的 `PushableProp` 已打开「循环播放移动录音」并绑定 `Assets/Audio/Experiments/ShoppingCartRoll.asset`。给该资源指定可无缝循环的轮声，再点 **Bake peak envelope**。购物车实际贴地移动时开始播放并生成跟车移动的连续声波，松手后仍在滑行时会继续，速度低于门槛后默认在 0.12 秒内淡出。完成烘焙后，滚动用连续声波，怪物听觉事件仍按原间隔发送。移动音量、淡入淡出时间、3D 距离衰减、混响和声波参数在同一组件上调节。垃圾桶继续沿用随移动声波触发的单次拖擦声。

撞墙和落地复用 `CollisionSonarEmitter`，最低法向碰撞速度 0.6m/s，间隔 0.25s；基础声波 6m，每增加 1m/s 扩展 2m，最多 14m；基础听觉范围 16m，随碰撞强度比例扩展。玩家身体碰撞仍不重复触发撞击声，两个物体均关闭自动计时与快捷键发波。

滚动或拖擦录音绑定 `PushableProp.Movement Clip`。撞击录音和音量在 `CollisionSonarEmitter` 上设置，录音槽为空时沿用 `SonarCueSource`。现有未配置录音的推挤道具仍会发出声波与怪物听觉事件。录音通过 `SpatialAudioEmitter` 播放，沿用空间定位与距离衰减。

`AgentScripts/ConfigurePushableProps.cs` 配置两个原有预制体并保留 GUID，已放置的正常 Prefab 实例会继承组件。`AgentScripts/VerifyPushableProps.cs` 在 Play Mode 通过虚拟键盘驱动真实 PlayerController，验证推动、静止/停下/顶墙静音、速度与碰撞限制、落地、近远怪物听觉、缺失视觉发声时的听觉、原有道具隔离和录音输出。报告位于 `Logs/PushableProps/play.txt`。

### 家具与日用品

病房隔帘 `WardCurtain` 支持无拉伸改长度：选中关卡内实例，在“病房隔帘尺寸（保持褶皱尺度）→ 长度（米）”修改，Inspector 的 `ResizableWardCurtain` 也提供同一参数。默认 2.5m，支持 0.5–50m；按固定 0.059m 网格步长增减褶皱，末段裁齐，保持褶皱深度、帘高、管径与轮子大小。吊环按固定间距增减，两端支架平移，碰撞同步调整；各实例独立，支持撤销、保存和场景/关卡 Prefab 导出。原预制体和网格 GUID 保留。`ConfigureResizableWardCurtain` 更新原资源，`RebuildFurniture` 也保留可调结构；`VerifyWardCurtainLength.Edit` 通过 41 项检查，报告及 1.2/2.5/5.3m 对照图位于 `Logs/WardCurtainLength`。

以下预制体位于 `Assets/Prefabs`，可在地图编辑器的道具下拉列表中直接放置。根节点位于底部，统一通过 LevelProp 在编辑模式显示预览材质、在运行模式使用黑色点云材质。

| 道具 | Prefab | 拾取 / 破坏 |
|---|---|---|
| 椅子 | Chair | 静态 |
| 购物车 | ShoppingCart | 可由玩家接触推动，滚动/碰撞发波并接入怪物听觉；轮子为模型细节 |
| 图书架 | LibraryBookshelf | 静态，附带书本模型 |
| 书桌 | WritingDesk | 静态，抽屉为模型细节 |
| 电脑 | Computer | 静态，包含显示器、主机、键盘和鼠标 |
| 水杯 | PickupWaterCup | 可拾取，不打碎 |
| 台灯 | PickupDeskLamp | 可拾取，不打碎；灯泡为模型，无开关或照明玩法 |
| 冰箱 | Refrigerator | 静态 |
| 灶台 | Stove | 静态 |
| 垃圾桶 | TrashBin | 可由玩家接触推动，拖擦/碰撞发波并接入怪物听觉；保持开口桶身 |
| 咖啡杯 | PickupCoffeeMug | 可拾取，3.8m/s 撞击打碎；复用原带把手杯子模型 |
| 陶瓷花瓶 | PickupCeramicVase | 可拾取，4.5m/s 撞击打碎；66 个碎片 |
| 高脚玻璃杯 | PickupGlassMug | 保留原资源引用，可拾取，3.8m/s 撞击打碎；49 个碎片 |

拾取、投掷、碰撞声波和碎裂全部复用 PickupItem / CollisionSonarEmitter。音频素材保持留空，无合成音兜底；所有碎片不可拾取。杯子和花瓶使用位于杯身的交互锚点，避免底部贴地造成提示闪烁。

道具自定义网格按物品集中保存：原有家具与可破碎道具的 196 个网格存于 8 个 `.asset` 文件，碰撞网格及碎片网格作为子资源。闹钟使用 `BreakableProps/AlarmClock.asset`，咖啡杯使用 `BreakableProps/CoffeeMug.asset`；其他容器使用同名资源。预制体直接引用子资源，网格形状及碎片数量不变。生成工具通过编辑器工具 `PropMeshAssets.Store` 按名称复用子资源，避免再次产生独立碎片文件。

碎片统一使用 `Fragments` 物理层。`PlayerController.OnEnable` 为玩家自身碰撞体添加该层的排除掩码，`PickupItem` 生成残骸时统一设置碎片层；保存的破碎 prefab 也已配置。玩家不会被碎片顶起或挡住，碎片仍与地面、墙壁和普通物体碰撞。新玩家、玩家重新启用以及以后生成的碎片均使用同一规则；手持道具不会被误当作玩家碰撞体修改。

`AgentScripts/ConfigureHouseholdProps.cs` 生成本批模型和预制体；已有新道具不会被重建，玻璃杯转换会更新原预制体。`AgentScripts/VerifyHouseholdProps.cs` 的 Edit / Play 入口检查模型迁移、桌面吸附、拾取、撞击打碎、轻放不碎和碎片不可拾取。预览图片位于 `Logs/HouseholdProps`。

`Assets/Prefabs/PickupAlarmClock.prefab` 是可拾取的双铃闹钟，复用 TickingClock、SonarObjectEmitter 和原有拿放逻辑。`Assets/Prefabs/PickupGlassMug.prefab` 现为高脚玻璃杯，保留原资源 GUID；原开口圆柱杯和单侧把手模型已转给 `PickupCoffeeMug.prefab`。编辑时显示浅色模型，运行时沿用黑色材质和点云显影。闹钟和玻璃杯已放在 `test.unity` 的 `Editable Level/Props` 下；地图编辑器通过道具下拉列表选择。

PickupItem 新增可选的 Breakage 配置，原有球默认关闭：

| 参数 | 作用 |
|---|---|
| Breakable | 是否允许碰撞破坏 |
| Break Speed | 接触面法线方向的碰撞速度阈值；闹钟默认 7m/s，杯子默认 3.8m/s |
| Break Layers | 哪些碰撞层可以触发破坏；玩家身体碰撞不触发 |
| Broken Prefab | 完整物体破坏后的物理零件/碎片预制体 |
| Fragment Separation Speed | 碎片附加的轻微分离速度，默认 0.35m/s |
| Fragment Lifetime | 碎片保留秒数；0 表示随场景/楼层卸载，不自动清理 |
| Break Pulse Range / Break Hearing Range | 一次性破碎声波与怪物听觉范围，默认 10m / 16m |
| Break Clip / 粉碎音量 | 可选粉碎录音及播放音量 |

破坏速度复用 CollisionSonarEmitter 的接触法线计算，不以刚体总速度代替。低速落地与高速擦墙不会因总速度较大而误碎。碰撞发声和 PickupItem 共用幂等破坏入口，破坏时只发一次破碎声波，不再叠加普通碰撞声波。

`AlarmClockBroken.prefab` 包含 13 个独立物理零件，包括外壳、表盘、四段外圈、两根指针、两只铃、两只脚和按钮；指针碎片继承打碎瞬间的位置与角度。`CoffeeMugBroken.prefab` 复用原杯子的 33 个预切碎片：24 块杯壁、6 块杯底、3 段把手。高脚杯使用 `WineGlassBroken.prefab`，包含 49 个杯碗、细柄和底座碎片。碎片继承打碎时的运动并参与物理碰撞，不包含 PickupItem、InteractableObject、TickingClock 或重复发声组件，因此不能拾取，也不会在落地时连续发波。

闹钟上的 `PickupAlarm` 在首次拿起后开始倒计时，默认 `Delay After Pickup = 5` 秒。放下、扔出和再次拾取都不重置倒计时。到时暂停 `TickingClock`，按 `Ring Interval = 0.2` 秒快速发波，默认 `Ring Range = 12m`、`Hearing Range = 16m`。菜单暂停玩法时同步暂停倒计时。`Alarm Clip` 是快速响铃录音。普通滴答使用 `TickingClock` 的「滴答录音列表」，每次选中顺序中的下一条有效录音，每条音量独立。列表为空时仍使用原有 `SonarCueSource`。

打碎会停止全部闹钟音频并销毁完整物体，倒计时和快速响铃随之终止。已经发出的视觉波按原有余辉自然结束。手持物体在玩家层级停用时，会在层级切换完成后安全释放；退出 Play 时不尝试在正在关闭的层级内重设父节点。

`AgentScripts/ConfigureBreakableProps.cs` 生成模型和预制体并提供 test 场景放置入口。`AgentScripts/VerifyBreakableProps.cs` 使用真实物理碰撞验证拾取、轻放、低速/高速/擦碰/层过滤破坏、拿在手中与放下后的延时响铃、重复拿起不重置、提前摔碎取消响铃、碎片不可拾取及点云显影、普通球回归和玩家停用时持物释放。报告与模型预览在 `Logs/BreakableProps`。

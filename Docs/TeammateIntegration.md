# 队友资产整合（2026-11-14）

后续玩法调整：金币已改为物理道具，移除钱包及资源收集，HUD 只显示生命；球状闪电改为自主循环的体积点云。当前操作和验证以 [PhysicalItems.md](PhysicalItems.md)、[WorldObjects.md](WorldObjects.md) 为准。下文交易检查记录的是最初整合时的历史结果。

已将 Baiyu-Reyar-TS 交付包中的新增玩法接入当前项目。体验入口为 `Assets/Scenes/TeammateSonarHall.unity`，其中包含 1 个巡逻怪物、6 枚金币、1 个球状闪电。直接打开该场景并 Play 即可体验。

## 导入范围

- 怪物巡视、听声调查、红色扫描及闪电避让。
- 金币 prefab、钱包交易接口、生命值与球状闪电 prefab。
- 共享声波扩展为 24 个独立分组的波槽，支持怪物红色、金币白色、闪电蓝色和独立密度衰减；原白色声波接口保留。
- 金币／生命 HUD 改用当前 uGUI/TMP 字体体系，使用 SECONDERSIGN，自动参与 PlayerUiPointMask 避让；放在楼层提示下方，并随交互菜单隐藏。
- 队友配置工具限定到新演示场景，避免误改原 SonarHall；验证脚本适配当前相机晃动、测试间状态隔离及 Windows 输出路径。

## 保留内容

没有复制交付包的 ProjectSettings、Packages 或嵌入包，也没有覆盖原场景、现有 prefab、字体资产、渲染设置资产。以整合开始时的 SHA-256 基线检查，以上文件保持原样。保留此前默认隐藏调试面板、居中持物、相机晃动、准心交互、种子地图和楼层字幕逻辑，以及当前工作区原有的未提交改动。

共享 C# 和 shader 只合入新增玩法所需扩展。金币、怪物、闪电目前放在独立演示场景中，没有自动向原场景或种子地图添加对象，也没有变更构建场景列表。

## 本机验证

- C# 编译：0 错误、0 警告；实际 GPU 渲染后的 shader 消息为空。
- 演示场景：无丢失脚本，依赖 GUID 均由 AssetDatabase 解析；新增资产没有 GUID 冲突。
- VerifyWorldObjects：28 项通过（金币、交易、闪电、伤害、避让）。
- VerifyMonster：18 项通过（寻路、声音、扫描）。
- VerifyColoredSonar：29 项通过（颜色、点位、密度、范围、波槽和四色 UI 避让）。
- VerifyDangerSonar：13 项通过（红色渲染及白红重叠）。
- VerifyUiPointMask：720p/1080p 的矩形和圆形共 4 组 GPU 检查通过。
- VerifySeededMap.Plans：F0–F24 共 25 层的可重复性、规模增长和连通性检查通过。
- 实际 HUD 检查：SECONDERSIGN 字体、楼层与状态分离、调试面板默认关闭；截图位于 Logs 下。

验证完成后已退出 Play 并返回原 BlackBoxPointCloud 场景。尚未执行独立 Player 构建或多怪物压力测试。生命值耗尽目前只显示提示，沿用队友提供的行为，没有额外引入复活流程。

## 来源及本地备份

交付包：`Sound2_Transfer_20261114_110945.zip`，已通过随包 SHA-256 校验：
`70f8fb1d035620d1bdd33861fa06480d7953144a00090a085413b7f86bade596`

`Logs/TeammateIntegration_20261114/` 保存原文件备份、导入清单、开始时的文件哈希和验证截图／GPU 报告。Logs 被 Git 忽略，仅为本机工作资料。功能说明见 `MonsterGameplay.md`、`WorldObjects.md`。

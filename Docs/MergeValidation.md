# 远端合并验证（2026-11-14）

合并目标：`origin/main` 的 `5e2c946`。合并前先将现有未提交成果保存为 `05880cb`，备份分支为 `codex/backup-before-merge-20261114-1345`。

18 个冲突文件来自双方独立加入及继续修改同一套怪物、金币、闪电、渲染器和验证工具。对比后保留已包含双方基础功能的当前版本，避免恢复已移除的钱包、金币自动收集、黄色实体币面、球面闪电与旧版 HUD。保留当前菜单、白色币面波纹、四类物理道具、右键放下、持物惯性、体积闪电和 UI 点阵避让。

远端独有的 SonarHall 场景改动保留，包括怪物、巡逻路线、金币／闪电摆放和脚步参数。通过 Unity Editor 对该场景做最小兼容迁移：移除旧钱包，将 6 枚金币根节点提升 0.9m 以补偿新版中心物理枢轴，保留它们的 X/Z 与发波节奏，确保闪电自动循环。

`AgentScripts/ConfigurePhysicalItems.cs` 已覆盖 SonarHall，便于重现迁移。`AgentScripts/VerifyMergeBuild.cs` 提供两个入口：

- `Audit`：检查 Assets/Scenes 下全部 12 个场景的脚本、对象引用、金币组件和钱包残留，并检查金币／闪电 prefab。
- `Main`：构建 Windows x64 Player，入口为 BaiYu-Test，另包含 SonarHall、TeammateSonarHall、SampleScene。仅设置本次 BuildPlayer 的场景清单，不改项目原有 Build Settings。

本机验证报告写入 `Logs/MergeValidation_20261114/`，Windows 产物写入 `Builds/MergeValidation_20261114/Sound2.exe`，均不加入版本管理。构建期间 Unity 自动更新的缓存和序列化配置不纳入合并。

## 验证结果

- 全部 12 个场景通过脚本及对象引用检查；金币与闪电 prefab 无丢失脚本。
- Windows x64 非 Development 构建成功：0 错误、17 警告，耗时约 116 秒，约 166 MiB。警告来自 Steam Audio 旧 API 与 HallSlab 网格碰撞预烘焙提示，未阻止构建。
- 独立 Player 后台启动测试通过：Direct3D 12 / RTX 4060 与 Steam Audio HRTF 初始化正常，进程持续响应，未发现脚本异常。启动日志有未包含的 URP 景深／Panini 后处理 shader 提示；这次仅做启动检查，未在独立 Player 中执行完整通关。
- SonarHall Play Mode：72 个导航源，怪物与闪电导航有效，6 枚金币全部可物理拾取，没有钱包；闪电无表面 MeshRenderer。
- VerifyMonster 的 18 项巡视、听声、调查、绕墙及扫描节奏检查全部通过。
- 构建后恢复 Unity 自动改写的渲染配置、ProjectSettings、字体缓存，删除本轮自动生成的 PerformanceTestRunInfo/Settings 资源。Packages 与 ProjectSettings 均无改动。
- 检查结束后退出 Play，返回已保存的 BaiYu-Test 场景。没有推送远端。

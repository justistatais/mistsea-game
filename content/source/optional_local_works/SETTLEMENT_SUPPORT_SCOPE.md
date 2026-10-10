# R6 两聚落运行候选与接入

原件范围来自 Exploration_Level_Design_v0.1 §9.7 以及 World_Story_Bible_v0.1 §8.1 的《两个天气预报》《借来的灯》。冻结地区 learner、几何与音轨没有改；新 `settlement_support_contracts.json` v2 明确区分原件范围和新增公开的窗口、地点、序号、岗位、压线台及借灯安排。私人家庭便条没有素材、读取许可或发布入口。

三个独立实际后果为：两份地点和工作窗口不同的预报比较后，将原三件当地增补物沿本批实际配对出口交付；共享压线台在两不同当地岗位、同两件手工备用、当前 OFF/固定/DRIVE 零条件下交接维护责任；原同一借灯清理触点后重新核零，沿真实连接归还原主人，再明确原主保留或一次有限互助安排。后续借用班次尚未发生。每个完成语义一次，世界主线、余额、六塔工包和资格不变。

状态和实物采用冻结 schema2 的 warehouse/installed；有限序号不能重造，plan 含无变化实物的原子保管条件、全部读取实体、稿件 hash/revision 和实际前景占用。正文 CAS 保存、意思确认、只读预览、关闭取消与实际提交分别执行；Continue/文本/权限/配置变更拒旧确认。

Root 负责接线：

- 纳入 Core SettlementSupportModels、Application SettlementSupportService 与 `.Press`/`.Lamp`、测试 SettlementSupportCases、UI Root.SettlementSupport、Diagnostics Root.SettlementSupportClientTests，共七个 `.cs.pending`。
- 编译包原文为 `res://content/settlement_support_contracts.json`；使用 Godot.FileAccess 读取 PCK。
- 实际 spot dispatch 优先调用 `TrySettlementSupportInteraction(spot)`；未满足实际 Q601 或 S05 运输状态会 return false 保留主线入口。S01/S02 其他站位不截获。
- ClosePanel 调 `CloseSettlementPreview(title)`；Continue/slot/reset 调 `ResetSettlementSupportUi()`；保存/退出/slot/backup 调 `SaveSettlementDrafts()` 并检查 bool。
- 稿件来源类写作 task 是 R6-O01-W-WEATHER、R6-O02-W-MAINTENANCE、R6-O02-W-LAMP。档案列表可按 `ViewAsync().Acquired` 调 `OpenContractAsync(id)`，不 Discover 未访地材料。
- Program 注册 `SettlementSupportCases.Register()`；四个正常业务例 CP-SETTLEMENT-03，一个正常原生入口例 CP-SETTLEMENT-05。
- build.py 新 `settlement-support-client-test` 从 CP-SETTLEMENT-05 的 `normal-settlement-native-entry.json` 复制真实 SQLite，Root DEBUG dispatch 调 `RunSettlementSupportClientTests()`，实际报告 settlement_support_client_results.json。

文件已写好，尚未编译、执行 SQLite 或原生驱动。测试正向均复用从零 XP 正常前序至 Q603 的真实动作，不用正向 fixture。业务覆盖有限来源、地点窗口、真实遍历、取消、权限撤回、当前零能、清理导致旧零检查失效、同灯归还、CAS 和 Continue。原生 driver 覆盖实际读者、无默认选项、现场操作、正文保存、意思和取消/交付；它不是人工 IME、读屏、校准或完整人工通关。

这三个本地工作也不代表 12 个可选遭遇槽已完成，更不代表 96 个实质解谜预算已完成。R5 路牌和失物模块另行制作。

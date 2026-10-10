# FirstApproaches 生产与原生接线

玩家路径 `content/first_approaches/learner_package.json`，源版本 FIRST-APPROACHES-20261010-v1。只复制 learner JSON；作者卡、原始答例、生成器及 provenance 保留作者目录。原设计文件、Campaign/ChapterOne 原材料文本和几何源不改。

`FirstApproachService(IAdventureRepository repository, IEventLeaseStore leases, RuntimeCatalog catalog, FirstApproachPackage package, ImmutableDictionary<string,SceneGeometry> geometry)`。ProfileStore 同时作为 repository 与 leases。模型、包验证和安全站位扩展在 Core；Application 使用实际前景 lease、分支读版本、当前任务实例、物件所有权和原共享战斗引擎。

`FirstApproachRules.ExtendGeometry(package, geometry)` 仅为现有 R1-S09 加 side_gate(22,66)、outer_gate(74,38)，完整保留所有旧 ports/blocked/danger。Campaign 无这场景时不添假的场景。共享 Root 用最终完整 geometry 的扩展视图；原 ChapterOne.MoveAsync 的墙与危险约束继续有效。

`CampaignService.seat_relay_lead` 的既有隔离判定后还须 `RelayCleared(snapshot)`。`ChapterOneService` 原 R1-S09→R1-S10 通行在现行许可/供给/CONTROL来源后还须 `TowerAccessIssue(snapshot)==null`。真实已完成 Q104、原 tidegate_vessel_handover 回执与 FinalInspection/Safe 的历史档仍可使用原塔路，但不制造缺失 ENC 回执。仅这些有限 gate 和正常 helper 经 Root 明确授权修改。

服务：MaterialsAsync/OpenMaterialAsync/DiscoverAsync、PerformSideStepAsync(isolate|brace|inspect_zero_energy)、EncounterAsync、BeginAsync、PreviewActionAsync、ConfirmAsync、ClosePreview、WithdrawAsync、RetryAsync。`PreviewActionAsync` 返回共享 EncounterActionPreview。三场都是0独立奖励；不得将三个 source 或三步维护多算三个完整解谜单元。

R0原生 partial CampaignClient.FirstApproaches：Configure 后 LoadCampaignFirstApproaches；Refresh 取得_view后 RefreshCampaignFirstApproaches；MayMove 加 CampaignApproachBlocksMovement；原 station.Panel 后 AddCampaignFirstApproachControls；档案 AddCampaignFirstApproachArchive；OpenBattle 首位按 CampaignApproachSelected 转 OpenCampaignFirstApproachBattle。Continue 清 token/选择再由实际 active 恢复。R0面板仍保留原材料和中继施工按钮，支持共有战斗动作、目标、相邻位置、实际补给/装置、公开意图及预计损失。

R1 partial Root.FirstApproaches：章节服务建好后 LoadFirstApproaches(finalGeometry)；Refresh 在 CurrentEncounterAsync 前 RestoreFirstApproachSelection；新增站位走 TryFirstApproachInteraction；原 control_notice 的原来源面板附加 AddFirstApproachSourceControls，不得遮掉 Q104-R-CONTROLS；档案 AddFirstApproachArchive。Root.EncounterRoute 在所有其它 encounter discriminator 前接 FirstApproach 的 Current/Preview/Confirm/Retry/Withdraw adapters，关闭token调用 CloseFirstApproachEncounterPreview。Root.Combat 使用 FirstApproachBattleTitle，Controlled说明只授塔基通行。继续清 transient UI state，再由真实 active/defeat entity 恢复选择，不写新历史。

测试注册 `Mist.Tests.FirstApproaches.FirstApproachCases.Register()`，16个展开用例；仅静态索引可由作者脚本生成，运行由 Root 串行负责。CompleteRelay 和 CompleteSide 已接现有 Campaign/ChapterOne/Voyage 正常 helper，都是实际来源/安全导航/战斗或三工序命令。两导出 case 分别输出 normal-first-relay-native-entry 与 normal-first-tower-native-entry，带控制库、登记备份、检查点；其唯一证据文件名分别为 normal-first-relay-native-entry.json 与 normal-first-tower-native-entry.json，避免与 Life 的 entry_evidence.json 选择冲突。每次原生运行复制新目录，不能复用上次已完成分支。

DEBUG 原生 flags：CampaignClient `--first-relay-client-test` → RunFirstRelayClientTests → first_relay_client_results.json；Root `--first-side-client-test` / `--first-outer-client-test` → RunFirstTowerClientTests → first_side/first_outer_client_results.json。Root共享 boot/test分派及build脚本由Root接线。本代理不运行构建或C#测试；工程报告必须注明实际来源、运行日期、源码SHA与范围。人工语言、平衡、IME、读屏、整章游玩均未完成。

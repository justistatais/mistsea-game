# ENC-R6-06 集成

六个新 `.cs.pending` 静态 READY：Core `FieldRelayModels`；Application `FieldRelayRepairService` 和 `.Encounters`；tests `FieldRelayRepairCases`；UI `Root.FieldRelayRepair`；Diagnostics `Root.FieldRelayClientTests`。Root 统一纳入，未改冻结 R6 main service、geometry、learner、audio、shared Root 或 build.py。

玩家原说明为 `content/source/optional_local_works/field_relay_contract.json`，Root compiler 需逐字复制到 `game/content/field_relay_contract.json`、纳入 manifest 的实际 SHA，并验证上述 author card 的冻结 hash。它是独立新局部 projection，不把原 R6 learner 的同版本正文改写。UI 通过 `Godot.FileAccess` 从 `res://content/field_relay_contract.json` 读这一个合同，兼容导出 PCK，不加载 author card 或来源答案。只读原说明沿现有 `Workspace` 输入归属，附现有区域词典。

注册 `Mist.Tests.OptionalLocalWorks.FieldRelayRepairCases.Register()`。三个业务用例过滤 `CP-FIELD-RELAY-03`；一个正常 SQLite native entry 导出用例过滤 `CP-FIELD-RELAY-05`，输出目录 `field-relay-normal-native-entry-*`，说明文件 **`normal-field-relay-native-entry.json`**。实际 0XP→各章→R6 Q601/Q603 完整生产链为13700XP，Actor在原维修站位；没有新增源领取、许可、辅机或结果。所有正向测试无 fixture。此批尚未执行构建或测试。

构造 `(IAdventureRepository, RuntimeCatalog, FieldRelayContract, ChapterSixService)`，使用既有 main service 当前真实危险几何。现场API：`ViewAsync()`；`DiscoverContractAsync()`；`OpenContractAsync()`；`DecideScopeAsync("accept"/"decline")`；`SetAuxSwitchAsync("OFF"/"LIVE")`；`FitDedicatedPinAsync(actualPinSerial)`；`VerifyZeroAsync("AUX"/"REF", observedReading, expectedConfiguration)`；`RefitSameRelayAsync(actualRelaySerial)`；`TestReturnAsync(expectedConfiguration)`；`CompleteAsync()`。每个写入要求真实 Q603 收束及 R6-S06.repair 安全站位，最终封存后拒绝。

UI spot 分支在旧 `TryChapterSixInteraction` 之前加 `if(await TryFieldRelayRepairInteraction(target.Id)) return;`。独立 spot handler 先核实际 Q603 quest closed/completed、同实例的真实 main_quest_resolution；未满足时 return false 保留旧 main repair。各 load/reset 调 `ResetFieldRelayRepairUi()`。不新增几何热点、不改变走路或出口，也不自动取得材料、接受局部工作或放置设备。

共享 battle 路由须在其他区域选择器之前检查 `FieldRelayEncounterSelected`。这会识别当前真实 active/defeat 的独立 combat entity，即使 Continue 后私有字段已重置，也能重新恢复本地服务；Actor 离开 R6-S06 时清掉 selected，不能覆盖其他区或 RF 战斗。独立 `CombatId` 为 **`combat:field-relay-ENC-R6-06`**，避免冻住的 main R6 scan 把这个新 DTO 当 FrontierEncounterState 读。对应路由：

- `CurrentEncounterAsync`：取 `await FieldRelayService().EncounterAsync()`；若非null，映射共享 `new EncounterState(current.Battle,current.Entry,current.ReturnActor,current.Battle.Outcome==CombatOutcome.Controlled)`。
- Preview：`FieldRelayService().PreviewActionAsync(BattleRequest)`；Confirm：`FieldRelayService().ConfirmPreviewAsync(Guid)`。
- Withdraw：`WithdrawFieldRelayEncounterAsync()`（成功后清 selected）；Retry：`RetryFieldRelayEncounterAsync()`（恢复完整入口再新尝试）。
- ClosePanel 关闭 `行动预览` 时同时 `FieldRelayService().ClosePreview(token)`，可以先用 `_fieldRelayRepair?.ClosePreview(token)` 避免未加载服务被初始化。共享标题可标为 `外线辅助继电处置`；controlled 提示继续同机隔离、固定与复位，不用工坊仓库支架提示。暂停编辑仅回已有正文，无新增文稿要求。
- 同一物理工作实例保持稳定，每次 battle attempt 单独生成；最多两份不同实际 CON01..04，当前可用 roster tactics/relic 实际应用，不要求泰丝招募。

DEBUG flag 由 Root 接线 **`--field-relay-client-test`** → `RunFieldRelayClientTests()`。build.py 在正常入口导出后复制整个 SQLite profile，以正式启动逻辑读取，再实际运行 native driver，检查 **`field_relay_client_results.json`** 和 `MISTSEA_FIELD_RELAY_CLIENT_TESTS`。驱动通过实际说明、拒绝/接受、battle preview readonly/取消/撤回、显式 OFF/专用销/REF错误/AUX零、同机复位/新零/返路/一次回执的 UI；输入默认尚未选择。未执行前不声称通过 native 或人审。

真实结果：semantic `R6:ENC-R6-06:result` / action `chapter_six_encounter_result` / typed `FrontierEncounterResult`，method 为安全机械 `scoped_isolation_brace_zero` 或真实共享战斗 `shared_combat_auxiliary_only`。它不更新 main ChapterSixState 或门禁。最终 semantic `R6:field-relay-repair:resolution` / action `field_relay_same_unit_repair_received` / typed `FieldRelayCompletion`，同 device instance、源 fingerprint、当前配置及三份真实来源回执。世界准备和结局 consumer 不接受它为六塔事实。

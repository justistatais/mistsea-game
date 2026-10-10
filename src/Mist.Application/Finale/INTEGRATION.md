# RF 运行候选接入 — rf-author-candidate-1

源：`content/source/finale/learner_package.json`，作者答案在独立 `production_cards.author_only.md`，永不导入玩家。7 scene / 7 edge / 14 paired port / 22 bilingual original / 4 listening_script / 16 station / 4 micro-short writing；人审、语言标定、灰盒视觉和声音权利仍pending。Root统一编译、测试、bundle和共享引擎/UI接入；本分工不运行build。

## 入口、几何与注册

`FinalePackage.ParseJson(json)`；`Scenes` 是 `ChapterScene` readonly投影。`GeometrySha256("RF-S01")` = lowercase SHA256 UTF8 `StrictJson.Serialize(actualSceneGeometry)`。实际落点 `FinalePackage.LandingStand=(48,90)`，spot `RF-S01.berth`。Sourcegeometry不以manifest/槽位声明实走完成。

`new FinaleService(repository,catalog,package,actualIEndingCheckpointArchive)`。仅真实 `FinaleLaunchService` 原子 `Q-604:resolution/network_finale_departure` 后初始化；stricttyped `Core.ChapterSix.FinaleDeparture` 与 `final:actual-departure` 等同，同一Q104船、RF泊位/角色落点、R6源泊位、seal/precheckpoint/源列表/风险登记、几何hash必须匹配，Find实际protectedcheckpoint。`InitializeRegionAsync()` 无奖励/材料/ actor teleport。stateId `finale` kind `finale_state`，初始化权益 `RF:initialize:{Version}` action `finale_actual_arrival_registered`。源码已 `.cs` 自动编译，无Program runtime注册。

Root字段 `_finale: FinaleService?` 仅在actualdeparture后load初始化。`Geometry` 提供完整只读候选，`CurrentGeometryAsync()` 仅实际 `RF:{ENC}:result/finale_encounter_result` 解除对应危险。`ViewAsync()` 包括 Snapshot / Actor / State / Quests / acquired且当前可访问Materials / 当前Scene Stations / CurrentGeometry。所有世界移动用 `TraverseAsync(ImmutableArray<Point2>)` 或 `MoveAsync(Point2)`；`PortIssue(snapshot,port)`、`EnterPortAsync(id)` 实际stand、配对站位和gate读戳，兼容root共享scene rendering/port按钮。外区旅行由shared Q604锁暂停，不能从RF旁路到最终按钮。

正式认可在S01.scope门外边界接受，避免先穿未认可门才签字。`FormalHandoverAsync(bool accept)` 无默认；`ConfigureLayerIsolationAsync("ISO-L"|"ISO-R",bool)` 确认具体对象。`ExecuteStationAsync(action)` 处理 source station，begin动作分别进入独立域，`deliver_stability` 只保存真实core安全来源，不关闭QF03或发奖；正文须另走CAS预览。`safe_preparation` 声明独立遭遇边界，不改变当前机组资源。

## 来源、稿件、提交

`DiscoverAsync(id)` 只在实际material.Spot安全站位；`OpenMaterialAsync(id)` acquired-version/hash/source readonly，脚本≠播放。shared `AddRegionalAudioButton(flow,material,()=>_finale.OpenMaterialAsync(id))` 可用独立真实音轨binding，learner AudioAsset仍空。

`SaveDraftAsync(task,expectedRevision,DraftId?,body)` 使用真实Draft CAS，默认自由空稿；未知意思初始index0，不能把英文原句或作者card自动变世界选择。`ConfirmMeaningAsync(quest,ImmutableDictionary<string,string>,structuredAssistance)` 明确结构确认，assessment unassessed，无自动理解或band。`PreviewSubmissionAsync(quest)` 只读并查稿hash/worldreadstamp/实际条件；`ConfirmPreviewAsync(token)` 原子QF01..03权益。`ClosePreview` 撤本地own/consumer预览。

`SubmissionSpot("Q-F01"|"Q-F02")="RF-S04.writer"`，QF01起稿在RF-S01.writer；QF03=RF-S06.writer；QF04=RF-S07.control。Root所有关闭/退出/Save/Continue/Load/backup要await `SaveFinaleDraft()` bool，IMEpreedit或CAS失败拒绝继续，随后关闭对应UI preview。

World meaning fields（运行拒绝不正确动作，UI必须提供未知与合理干扰项）：

|任务|确认字段|
|---|---|
|QF01|route=真实formal/maintenance；scope=entry_only_not_core_safe|
|QF02|command_scope=local_combat_two_points；rook_state=alive_in_custody；machine_state=running_requires_separate_work；history=retained|
|QF03|isolation=current_layer_not_global_shutdown；core=safe_controlled_stop_restart_disabled；resources=one_attempt_no_phase_refill；policy=not_selected|

主奖严格原定义QF01 500/120/1、QF02 500/140/1、QF03 800/160/2，generic `main_quest_resolution` typed `MainQuestResolution(Quest,actualInstance,"completed")`，激活下一任务。资料/击杀无XP。QF04仍sharedconsumer一次600/200/0；QF05 zero。

## 战斗与当前安全生产者

`BattleDefinitions`；`EncounterAsync(id)` returns `FinaleEncounterState?`；`BeginEncounterAsync(id,difficulty="standard",supplies?)` 用实际Party/roster/装备/module派生freshparty，再`FinaleEncounterFactory.Attach`前置移除benchmark敌人和创建正确RF候选，不后填HP。`PreviewEncounterActionAsync(id,request)`→shared `EncounterActionPreview`；`ConfirmPreviewAsync`同token；`WithdrawEncounterAsync(id)` wholeattemptentry真实物品归还，不跨段免费补给。

ENC01 spotRF-S03.coil基础handle+来源；ENC02 RF-S04.rook真实QF01闭合+来源；ENC03 RF-S05.relay真实QF02+日志/隔离；ENC04 RF-S06.control当前layer/actualhall/三段sources。Root selected encounter按此id路由，不固定tower；读取/写作/听力面板暂停engine计划槽且不恢复资源。core三段用已接共享BattleSnapshot.Finale，旧伤害意图recalibration，relaynextRound。

完成领域：entity `RF:result:{ENC}` kind `finale_encounter_result`；semantic `RF:{ENC}:result` / action `finale_encounter_result` / typed `FinaleEncounterResult`。核心controlled≠最终政策。实际S04.audit产 `RF:rook-custody/rook_custody` 同 `RF:rook-custody/finale_rook_custody_received` typed `RookCustody(EncounterInstance,Attempt,Alive=true,CommandAuthorityReleased=true,Custodian="RF-duty-officer",SourceEncounterReceipt)`，source actualENC02结果。QF02完成后RF-S04.safe=(22,40)才允许Party整备（Root共享Safe必须站位<=2.5并核typed源+QF02 actualinstance），初始RF-S01必须核真实初始化源。Party CAS读 `finale`、custody、`RF:gate:command_released`、questQF02。

`RecordCentralSafetyAsync()` / `ExecuteStationAsync("deliver_stability")` 在真实RF-S06.writer由actualcorecontrolled +currentLayer+actualCommand生成 `central_stability_verified` typed `CentralStabilityResult`，semantic `RF:central-stability:{attempt}`，再绑定entity `final:central-safety` typed `CentralSafetyState(...SourceReceipt)`。补来源的动作显式有世界变化，`PreviewSubmissionAsync`绝不隐式生产它。完成QF03才开放RF-S07。

`VerifyFinalControlAsync()` 在RF-S07.control=(34,28)，realQF03/Safe+actualsources；专用 `final_control_access_verified` typed `FinalControlAccess(actualQF04Instance,"RF-S07",34,28,true,"")`，semantic `RF:final-control:{instance}`，随后entity `final:control-access` with真实SourceReceipt。与sharedconsumer2.5站位要求匹配。

## 终局消费与尾声

`FinalReadiness`属性为existingservice同一实例，不复制EndingEngine。`ConfirmPolicyAsync(EndingChoice,structuredAssistance)`→existing确认；`PreviewPolicyAsync()`→只读实际资格，`ConfirmPreviewAsync`可确认consumer token。Defaultchoice未知，不读取英语/字数/字幕/战斗难度/队友决定资格。sharedWorldProjection需精确Q604 typeddepartureadapter（Root持有）。

`PresentEpilogueAsync()`→`FinaleEpilogue` actualEndingSnapshotId/EndingId +双语Immediate +6RegionalCards +4compatibleCompanionNotes +Archive +NaturalMistRemains；数据来自capturedEnding.History/FrozenPreparation而不是当前回访分支。`FinaleService.EpilogueFragments.cs` 已增加六地公开实际结果片段（厨房/轮供、早降/限班、十分钟正反跑次、当时真实救援接收、原案保管、塔料与独立补给）。不展开原实体JSON或身份字段，没有来源的选择不补写，原班次许可不扩成六个月；未招募或个人03任务未完不假和解/私信。具体来信仍需要独立投递生产者，内容人审仍pending。

`AcknowledgeEpilogueAsync(bool skipPresentation)` 在RF-S07.epilogue，与真实snapshot同Id，QF05接受/闭合zero主奖，同一权益只一次；skip不用长作文不删除结局。终局前存档继续须用shared明确分支UI，不改alreadycommitted ending。

## 测试注册及真实剩余

`FinaleBattleCases.Register()`现8个 instrumented共享引擎动作用例由Root已实跑8/0：不等于完整28级平衡/正常剧情。将新增 `FinaleCases.Register()`、真实0XP `ChapterSixCases.NormalComplete`→RF流程及normal native-entry artifact（含actualcheckpointmetadata供Rootbundler导入），独立RF UI/client驱动与实际28级H/M/S/混合资源记录；没有假seed正向。所有build和执行由Root串行，源码作者不能填human/audio-rights通过。

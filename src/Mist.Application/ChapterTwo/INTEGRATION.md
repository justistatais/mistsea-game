# R2 runtime candidate integration

Status: original_unreviewed / human_pending. New C# is intentionally `.cs.pending` until the root agent freezes and compiles it. No build or test has been run by this producer. Author-only answers remain in `content/source/chapter_two/*.author_only.*`; load **only** `learner_package.json`.

## Package and arrival

`ChapterTwoPackage.ParseJson(json)` validates the exact 10 scenes, 20 reciprocal ports, material/version/stand contracts and 6.5 writing target. It never loads author answers. Constructor: `new ChapterTwoService(repository, catalog, package)`.

After the real VoyageService chart/handoff/route/berth → Travel sail → arrival flow, call `InitializeRegionAsync()`. This verifies actual Q104 handover, single matching GLIMMER, current commission, source-bound/current route and berth, and actual R1→R2 sail receipt. It requires a safe R2-S01 landing, grants zero XP, moves nobody, acquires no material and checks commit status. Bare actor relocation fails.

`ViewAsync()` returns Snapshot, Actor, State, Quests, currently acquired **and permitted** Materials, current-scene Stations, and current Geometry. `Package.Materials` is the learner source catalogue, not acquired knowledge. `Geometry` is initial geometry; use `CurrentGeometry(snapshot)` / `CurrentGeometryAsync()` for actual danger removal. Current danger removal requires independent encounter-result receipts and device configuration checks.

## Public commands

- Navigation: `MoveAsync(Point2)`, `TraverseAsync(ImmutableArray<Point2>)`, `EnterPortAsync(portId)`, `PortIssue(snapshot, port)`. Move uses actual safe segment geometry; EnterPort verifies permissions/paired stands, publishes and registers stable connection safety, then commits a real `scene_transition` / TraversedConnection. UI may pass its returned receipt to Travel.RecordWalkedRouteAsync using current geometry. It does not record a guessed route automatically.
- Sources: `DiscoverAsync(materialId)` at its actual stand and current permission; `OpenMaterialAsync(id)` requires acquired/currently permitted source. Listening items are written scripts with no audio asset; no listened claim is issued.
- Basic physical work: `ExecuteStationAsync(action)` supports package stations for landing/reception/voluntary A/B/C visits, stock transfer, isolation/drain/clamp/check, branches, connector/power/test, inner latch, kitchen and tower checks. Typed actions below are deliberately separate from buttons that merely acknowledge prose.
- Conveyor: `PerformConveyorStepAsync("open_conveyor_guard" | "release_conveyor_jam" | "secure_conveyor_guard" | "inspect_conveyor_stopped")`, in this actual manual alternative sequence after isolation. The last zero-motion inspection produces the independent ENC-R2-02 control receipt; reading the panel does not. An actual shared-engine ENC-R2-02 battle is the other implemented path. `SetLocalLoadAsync(LocalLoad.Kitchen | Wash | Alternating | Concurrent | TowerOutlet)` requires isolation and invalidates old tests. Concurrent/TowerOutlet cannot satisfy local safety.
- M12: `ConfirmM12Async("before_14:10_exact_minute_unknown", "14:40", "16:05", false, "unknown", "unknown")`. Other claims are rejected; both immutable sources are required. These are explicit player confirmations, not inferred draft meaning.
- Scoped records: `RequestRecordScopeAsync(record, "1", fields, "patrol_technician", purpose)`. TR-D1-7 purpose=verify_transfer; permitted fields: transfer_id, identity_link, dated_condition, destination_code, version_source. TS-18 purpose=verify_historical_scope; fields: signature_reference, version_source; requires completed Q202+Q203. `VerifyD1ChainAsync("TR-D1-7", "D-1_only", "unknown")` requires actual sending/receiving roots and current scoped access.
- Encounters: `BeginEncounterAsync(id, difficulty="standard", supplies=null)`, `EncounterAsync(id)`, `PreviewEncounterActionAsync(id, BattleRequest)`, `ConfirmPreviewAsync(token)`, `WithdrawEncounterAsync(id)`. Three independent IDs are ENC-R2-01/02/03; their implemented battle paths use normal shared CombatEngine transitions. ENC03 additionally needs `ChooseCoreMethodAsync("sample" | "mechanical")`, `PerformCoreStepAsync("secure_a" | "secure_b")` before battle, then method-specific `"take_sample"` / `"clear_winding"` and actual `"inspect_valve_a"` / `"inspect_valve_b"`. Choosing a method or taking a material produces no control result.
- Writing: `SaveDraftAsync(taskId, expectedRevision, DraftId?, body)`; `ConfirmMeaningAsync(questId, ImmutableDictionary<string,string>, structuredAssistance)`; `PreviewSubmissionAsync(questId)`; `ConfirmPreviewAsync(token)` / `ClosePreview(token)`. Meaning is explicit input bound to exact draft hash. Preview is read-only and immutable, checks full world/draft read stamps on confirmation, and cannot silently rebase after a change or Continue. Empty text needs explicitly selected structured assistance. No free-text understanding or language score is claimed.
- Disclosure: `SetDisclosureScopeAsync("redacted_public_summary" | "entrusted_review", publicFields, recipient, "verify_public_facts", "R2-facts-v1")`; six publicFields: public_fact, record_dates, source_versions, local_safety_boundary, historical_signature_uncertainty, mining_batch. Recipient is priory_public_board / priory_review_desk respectively. Named identity publication has no fabricated person-consent source and is explicitly unavailable.
- Mira/W04: `DecideMiraInvitationAsync(bool? join)` after Q202; null defers, false declines and does not block main line. `DecideW04InvitationAsync("accept" | "decline" | "defer")` after Q204. `RequestW04ScopeAsync(["observation_time","witness_duty","version_source"], "priory_review_desk", "verify_local_note", "W04-v1")` is an actual limited local consent at W04 stand; private addresses are rejected. Main line does not require formal Mira recruitment.
- Anchors: `RegisterCurrentReturnAnchorAsync()` only at actual qualified S01 berth / S02 June / safe Q203 S08 return-check. Travel still owns route/actual-geometric safe-return verification.

## Explicit writing confirmation fields

These values belong to the structured fallback interface; show both supported and unsupported/unknown choices rather than defaulting to accepted values. The UI must label the fallback and must never export author-only keys.

| Quest | Accepted bounded confirmation |
|---|---|
| Q-201 | scope=local_group; register_meaning=classification_not_current_presence; motive=unknown |
| Q-202 | mara_time_scope=D-1_only; mara_current_status=unknown; identity_accountability=separate_fields; comparison=different_measures |
| Q-203 | power_scope=local_only; tower_preparation=safe_not_converted; load_configuration=current LocalLoad enum string |
| Q-204 | mara_current_status=unknown; signature_scope=historical_needs_verification; public_scrutiny=preserved; mining_direction=MC-2 |
| CQ-MIRA-01 | path=limited_copy or custodian_relay; recipient=priory_review_desk; purpose=verify_local_note; version=W04-v1; address=excluded |

Writing task IDs/stands/prompts and the Q202 visual table come from Package.WritingTasks. Q202/Q204 are full tasks, Q201/Q203 are short applied tasks. The runtime validates world effects and explicit confirmations, not 150/250-word proficiency.

## Registration and limits

Root may rename the six pending files and register `Mist.Tests.ChapterTwo.ChapterTwoCases.Register()`: 11 real SQLite cases. Ten isolate branches/rejections using explicit prior-source fixtures; the final case calls the public VoyageCases.NormalChart actual zero-XP→Q104 bootstrap, actual voyage, then Q201→Q203→Q202→Q204 with no positive fixtures. No test result is claimed until root runs them.

Main reward total is fixed: 1020 XP / 320 marks / 5 parts, once. CQ-MIRA-01 is 25 XP / 20 marks / 0 parts inside the original optional budget. Q203 is parallel with Q202 after Q201; 7-level recommendation is not a hard gate. Q203 establishes only current Tower Safe, not baseline/trial/operators/transition/enhanced conversion or free engineering inventory.

Remaining: human/source/content calibration; 6.5 language scoring; audio recording/performance/rights; named-person identity publication route; O01/O02 private contract IDs/rewards/authority; native UI/client journey and encounter balance validation; full tower conversion/resources staffing; R3 onwards. Source candidate is not a finished human-reviewed chapter, and written scripts are not recorded listening.

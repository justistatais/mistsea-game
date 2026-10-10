"""Read-only source/evidence reconciliation; writes one author review, never an execution ledger."""
from pathlib import Path
import datetime, hashlib, json, re

ROOT = Path(__file__).resolve().parents[2]
def read(path):
    return json.loads((ROOT / path).read_text(encoding="utf-8-sig"))
def sha(path):
    return hashlib.sha256((ROOT / path).read_bytes()).hexdigest()
def existing(paths):
    return [p for p in dict.fromkeys(paths) if (ROOT / p).is_file()]

PLAN = read("planning/plan.json")
REGIONS = {
    "R0": dict(package="content/source/campaign/first_entry.json", author="content/source/R0/production_cards.author_only.md", provenance="content/source/R0/provenance.author_only.json", application="src/Mist.Application/Campaign", tests="tests/Mist.Tests/Campaign/CampaignCases.cs", ui="game/UI/Campaign", native="campaign_client_results.json", optional="No independent optional XP or personal node is defined for R0; temporary rescue collaboration is not recruitment."),
    "R1": dict(package="content/source/chapter_one/chapter_one.json", author="content/source/R1/production_cards.author_only.md", provenance="content/source/R1/provenance.author_only.json", application="src/Mist.Application/ChapterOne", tests="tests/Mist.Tests/ChapterOne/ChapterOneCases.cs", ui="game/UI/Shell/Root.ChapterOne.cs", native="campaign_client_results.json", optional="R1 life lodging/performance candidates and existing Lina/transport nodes have finite local contracts; they are not an inventory of every optional budget slot."),
    "R2": dict(package="content/source/chapter_two/learner_package.json", author="content/source/chapter_two/production_cards.author_only.md", provenance="content/source/chapter_two/provenance.author_only.json", application="src/Mist.Application/ChapterTwo", tests="tests/Mist.Tests/ChapterTwo/ChapterTwoCases.cs", ui="game/UI/Shell/Root.ChapterTwo.cs", native="chapter_two_client_results.json", optional="Limited W04 material-copy commission, public work proposal and side-well observation are separate optional producers; acquiring their material is not delivery or reward."),
    "R3": dict(package="content/source/chapter_three/learner_package.json", author="content/source/chapter_three/production_cards.author_only.md", provenance="content/source/chapter_three/provenance.author_only.json", application="src/Mist.Application/ChapterThree", tests="tests/Mist.Tests/ChapterThree/ChapterThreeCases.cs", ui="game/UI/Shell/Root.ChapterThree.cs", native="chapter_three_client_results.json", optional="Oren own auxiliary kit/review keeps main supply distinct; owner-approved same music-box repair has separate private consent and one physical object."),
    "R4": dict(package="content/source/chapter_four/learner_package.json", author="content/source/chapter_four/production_cards.author_only.md", provenance="content/source/chapter_four/provenance.author_only.json", application="src/Mist.Application/ChapterFour", tests="tests/Mist.Tests/ChapterFour/ChapterFourCases.cs", ui="game/UI/Shell/Root.ChapterFour.cs", native="chapter_four_client_results.json", optional="CQ-MIRA02 is a limited prior-source review. O01 course inquiry and O02 contribution card are now real finite local producers with exactly zero rewards; timetable and trial authority stay bounded."),
    "R5": dict(package="content/source/chapter_five/learner_package.json", author="content/source/chapter_five/production_cards.author_only.md", provenance="content/source/chapter_five/provenance.author_only.json", application="src/Mist.Application/ChapterFive", tests="tests/Mist.Tests/ChapterFive/ChapterFiveCases.cs", ui="game/UI/Shell/Root.ChapterFive.cs", native="chapter_five_client_results.json", optional="Mara guarded/private access and roster choice are distinct from the main rescue. Not all planned local/personal story presentation is accepted by a service-level test."),
    "R6": dict(package="content/source/chapter_six/learner_package.json", author="content/source/chapter_six/AUTHOR_CARD.md", provenance="content/source/chapter_six/provenance.author_only.json", application="src/Mist.Application/ChapterSix", tests="tests/Mist.Tests/ChapterSix/ChapterSixCases.cs", ui="game/UI/Shell/Root.ChapterSix.cs", native="chapter_six_client_results.json", optional="Tess local receipt bridge reuses actual Q603 transport, not a second reward or transport. Field-relay repair files still carry .pending and require inclusion/execution; no blanket optional completion."),
    "RF": dict(package="content/source/finale/learner_package.json", author="content/source/finale/production_cards.author_only.md", provenance="content/source/finale/provenance.author_only.json", application="src/Mist.Application/Finale", tests="tests/Mist.Tests/Finale/FinaleCases.cs", ui="game/UI/Shell/Root.Finale.cs", native="finale_client_results.json", optional="QF05 epilogue uses captured currently permitted fragments and existing receipts. It creates no mandatory new essay, optional XP or private source access; a formal all-budget optional inventory remains separate."),
}
EXTRA = {
    "R0": ["content/source/campaign/production_cards.json", "game/UI/Campaign/CampaignClient.Audio.cs", "game/UI/Campaign/CampaignClient.RegionalLearning.cs", "src/Mist.Core/Assets/CampaignSpeechProjection.cs"],
    "R1": ["src/Mist.Application/Q102/Q102Service.cs", "src/Mist.Application/Q102/Q102Service.Departure.cs", "content/source/part06-Puzzle_Materials_v0.1.json", "content/source/r1_life/learner_package.json", "src/Mist.Application/R1Life/R1LifeService.cs", "tests/Mist.Tests/R1Life/R1LifeCases.cs"],
    "R2": ["content/source/r2_life/learner_contract.json", "src/Mist.Application/R2Life/R2LifeService.cs", "tests/Mist.Tests/R2Life/R2LifeCases.cs", "src/Mist.Application/OptionalLocalWorks/SideWellObservationService.cs", "tests/Mist.Tests/OptionalLocalWorks/SideWellObservationCases.cs"],
    "R3": ["src/Mist.Application/OptionalLocalWorks/MusicBoxRepairService.cs", "tests/Mist.Tests/OptionalLocalWorks/MusicBoxRepairCases.cs"],
    "R4": ["content/source/chapter_four/optional_credit.json", "content/source/chapter_four/optional_credit.author_only.md", "content/source/chapter_four/optional_inquiry.json", "content/source/chapter_four/optional_inquiry.author_only.md", "tests/Mist.Tests/ChapterFour/ExhibitionCreditCases.cs", "tests/Mist.Tests/ChapterFour/StudyInquiryCases.cs", "game/UI/Shell/Root.ChapterFourCredit.cs", "game/UI/Shell/Root.ChapterFourStudy.cs", "game/Diagnostics/Root.ChapterFourLocalClientTests.cs.pending", "game/Diagnostics/Root.ChapterFourLocalClientTests.cs"],
    "R5": [],
    "R6": ["content/source/chapter_six/long_form.author_only.json", "src/Mist.Application/OptionalLocalWorks/TessLocalReceiptBridge.cs", "tests/Mist.Tests/OptionalLocalWorks/TessLocalReceiptBridgeCases.cs", "src/Mist.Application/OptionalLocalWorks/FieldRelayRepairService.cs.pending"],
    "RF": ["src/Mist.Core/Finale/FinaleBattleRules.cs", "src/Mist.Core/Finale/FinaleEncounterFactory.cs", "tests/Mist.Tests/Finale/FinaleBattleCases.cs", "game/Debug/FinaleBattleCandidate.cs", "content/source/finale/epilogue_fragments.author_only.md", "content/source/balance/finale_balance_candidate_v2.author_only.json", "content/source/balance/finale_balance_candidate_v3.author_only.json", "reports/content/RF/CP-RF-04.balance_candidate.md"],
}
REGIONS["R1"]["optional"] = "Lodging/performance and three actual Lina personal producers have finite scripts, current invitation, distinct correspondence/meeting permission and original once rewards; private topics are not inferred from recruitment."
REGIONS["R3"]["optional"] = "Oren auxiliary kit/review, owner-approved same music box and the named shift board have separate actual source, custody, permission and receiving contracts; shifts have two finite alternatives and zero rewards."
REGIONS["R4"]["optional"] = "CQ-MIRA02 now also requires actual source-backed same paper sealing and named desk delivery. Course inquiry and contribution card are separate real finite zero-reward producers; neither attendance nor a changed timetable is invented."
REGIONS["R5"]["optional"] = "Two local life contracts produce one remembered sign and separately checked same personal objects without private-content access. Mira03/Oren03 reuse original invitations, draft owners, fixed rewards and independent handover, with current actual response and device detail."
REGIONS["R6"]["optional"] = "Tess reuses actual Q603 transport once. Field relay has an included physical producer and actual native evidence; separate settlement packet/shared press/same borrowed lamp contracts have finite stock, current duty/permission and zero rewards. No overall optional acceptance is inferred."
EXTRA["R1"] += ["content/source/lina_personal/learner_package.json", "src/Mist.Application/LinaPersonalWorks/LinaPersonalWorkService.cs", "tests/Mist.Tests/LinaPersonalWorks/LinaPersonalWorkCases.cs", "game/UI/Shell/Root.LinaPersonalWorks.cs"]
EXTRA["R3"] += ["content/source/r3_shift_work/learner_package.json", "content/source/r3_shift_work/production_cards.author_only.json", "src/Mist.Application/R3ShiftWork/R3ShiftWorkService.cs", "tests/Mist.Tests/R3ShiftWork/R3ShiftWorkCases.cs", "game/UI/Shell/Root.R3ShiftWork.cs"]
EXTRA["R5"] += ["content/source/optional_local_works/old_city_life_contract.json", "src/Mist.Application/OptionalLocalWorks/OldCityLocalLifeService.cs", "src/Mist.Application/OptionalLocalWorks/OldCityLocalLifeService.Property.cs", "tests/Mist.Tests/OptionalLocalWorks/OldCityLocalLifeCases.cs", "game/UI/Shell/Root.OldCityLocalLife.cs"]
EXTRA["R6"] += ["src/Mist.Application/OptionalLocalWorks/FieldRelayRepairService.cs", "tests/Mist.Tests/OptionalLocalWorks/FieldRelayRepairCases.cs", "content/source/optional_local_works/settlement_support_contracts.json", "src/Mist.Application/OptionalLocalWorks/SettlementSupportService.cs", "src/Mist.Application/OptionalLocalWorks/SettlementSupportService.Lamp.cs", "src/Mist.Application/OptionalLocalWorks/SettlementSupportService.Press.cs", "tests/Mist.Tests/OptionalLocalWorks/SettlementSupportCases.cs", "game/UI/Shell/Root.SettlementSupport.cs"]
for region in ("R4", "R5"):
    EXTRA[region] += ["content/source/scoped_personal_work/learner_package.json", "content/source/scoped_personal_work/production_cards.author_only.md", "src/Mist.Core/ScopedPersonalWorks/ScopedPersonalWorkModels.cs", "src/Mist.Application/ScopedPersonalWorks/PersonalProductionRules.cs", "tests/Mist.Tests/ScopedPersonalWorks/ScopedPersonalWorkCases.cs", "game/UI/Shell/Root.ScopedPersonalWorks.cs", "game/Diagnostics/Root.ScopedPersonalClientTests.cs"]
    EXTRA[region] += [str(p.relative_to(ROOT)).replace("\\", "/") for p in (ROOT/"src/Mist.Application/ScopedPersonalWorks").glob("*.cs")]
for region in ("R1", "R2", "R3", "R4", "R5", "R6"):
    EXTRA[region] += ["content/source/optional_encounter_contracts/learner_package.json", "content/source/optional_encounter_contracts/cards.author_only.json", "src/Mist.Application/OptionalEncounterContracts/OptionalEncounterService.cs", "tests/Mist.Tests/OptionalEncounterContracts/OptionalEncounterCases.cs", "game/UI/Shell/Root.OptionalEncounterContracts.cs"]

SHARED = existing([
    "content/source/audio/regional_speech_input.json", "game/content/audio/regional_runtime_bindings.json", "game/content/audio/wave_validation.json", "game/content/audio/regional_generation.json",
    "content/source/lexicon/learner_lexicon.json", "content/source/lexicon/provenance.author_only.json", "src/Mist.Core/Learning/RegionalLexicon.cs", "src/Mist.Application/Learning/RegionalLearningService.cs", "tests/Mist.Tests/Learning/RegionalLearningCases.cs", "tests/Mist.Tests/Assets/RegionalAudioCases.cs",
    "src/Mist.Core/Combat/BattleEngine.cs", "src/Mist.Application/Inventory/PartyService.cs", "src/Mist.Application/Storage/ProfileStore.cs", "src/Mist.Application/Travel/TravelService.cs", "src/Mist.Application/RegionalVoyage/RegionalVoyageService.cs", "src/Mist.Application/Voyage/VoyageService.cs", "src/Mist.Application/Ending/EndingService.cs",
    "reports/continuous/remaining_scope_inventory_20261010_1315.json",
    "content/source/vessel_life/learner_package.json", "content/source/vessel_life/permissions.author_only.json", "content/source/vessel_life/production_cards.author_only.md", "src/Mist.Core/VesselLife/VesselLifeModels.cs", "src/Mist.Application/VesselLife/VesselLifeService.cs", "tests/Mist.Tests/VesselLife/VesselLifeCases.cs", "game/UI/Shell/Root.VesselLife.cs",
])

# Each key is the original plan wording. Support is scoped, not a task-completion claim.
SPECIFIC = {
    "熄灯先于偏航，不提前透露是谁下令": "The same ship-clock chronology and independent shore excerpt constrain order; command identity and causal intent remain unknown in acquired R0 sources.",
    "阅读耗时不杀死幸存者。": "Survivor movement/receipt changes are explicit physical commands; reading or editing has no timed loss producer. Physical rescue precedes mail pickup.",
    "A关闭/B1.2米/本船1.4米/C需当前导航灯": "Q102 original route sources retain A closed, B maximum 1.2 m versus this ferry's 1.4 m, and C subject to the current tested lamp; departure rechecks configuration and actual installed items.",
    "工坊3级、塔站5级": "Actual normal chain records workshop entry level 3 and tower entry level 5; shared progression and equipment rules, not chapter-granted experience fixtures, derive eligibility.",
    "莉娜Q104后正式邀请。": "Temporary prologue help and formal post-Q104 roster invitation are separate. The main handover does not require accepting recruitment.",
    "D-1交接只说明导师当时活着": "Typed R2 handover keeps the D-1 observation and unknown present mentor status; it never turns historical contact into current survival/location.",
    "不公开私人姓名也可核验公共事实": "Public room/cohort observations and permission-limited coded records support the main result without public private-name/address disclosure.",
    "小站不代表全塔改造。": "Current local physical safety proofs create Safe only; Baseline/Operators/Trial/Transition/conversion remain separate actual later producers.",
    "减载后仍有梯道返回": "Current load and drain proofs can expire independently; the actual independent ladder remains the return route and has safe geometry.",
    "奥伦不入队也有证据": "Original instrument, retained workshop copy and formal acceptance suffice without confession or roster recruitment; declined invitation preserves contact-level access.",
    "实际修复不抹历史": "Repaired current bypass/kit and immutable earlier acceptance facts persist separately; current success does not rewrite original raw records or responsibility.",
    "个人物资与主线供给分开。": "Finite local work kits, Oren's own auxiliary resources and actual sealed sample have different custody and proof from tower engineering stock.",
    "反例不能被删": "Original A failure, C actual safe-stopped counterexample and later stable run retain separate stored run IDs; configuration reset expires current proof without deleting historical runs.",
    "有限10分钟记录不证明全网安全": "The two actual five/ten simulated-minute observations close only bounded local comparison/experiment; no full-network safety or conversion authority is produced.",
    "Q404起公开增强准备条件。": "Actual Q404 publishes the bounded preparation list and technical handover; later enhanced producers must still supply their own permitted materials, finite resources, tests and signatures.",
    "导师第五章活着获救": "Actual contact, opening, escort through existing ports and reception establish alive-at-rescue/current receiving custody; bare permission, battle control and region entry do not rescue.",
    "玛拉/沃斯/鲁克责任分别保留": "Three independent R5 records and explicit bounded comparison preserve each responsibility; current rescue or one account does not absolve another role.",
    "私人许可不堵唯一主线依据。": "Public main custody and source paths exist separately from private/personal scopes and optional recruitment; no sole-mainline access requires disclosure or invitation.",
    "Q602与Q603不循环": "Q603 local escort/physical receipt can complete while distant Q602 baseline remains incomplete; Q604 seals only after actual prerequisite outcomes.",
    "基础保全保证两个结局": "Six actual baseline producers and launch projection feed the shared ending engine's close/preserve choices; Safe alone is insufficient and the enhanced direction is separately conditional.",
    "增强五项可见可补": "Public preparation gaps correspond to actual limited enhanced source/duty/material/isolation/communication proof producers and distinct current signatures; missing items are visible rather than silently granted.",
    "不靠现实等待施工。": "Construction/test observation is a finite explicit simulated-step command; no wall-clock countdown pretends a source, safe configuration or delivery was produced.",
    "主线28级进入": "Actual normal zero-XP chain closes Q604 at 16740 XP and grade 4, then enters RF with real main equipment/local aid and shared derived level 28. Mechanism fixtures are excluded from balance proof.",
    "鲁克制止不等于停机": "ENC-RF-02 two actual control points create custody/QF02 only. ENC-RF-03's separate three-stage equipment, verification, relay and controlled-stop sequence remains required.",
    "阶段不回满": "Shared battle atomic hooks derive 70/35 percent floors from actual maximum HP, clamp discarded overflow before down, preserve live resources, and advance only one eligible phase.",
    "QF05无新必做作文": "The epilogue produces permitted captured fragments from prior actual records; no new required writing task or English assessment controls access.",
    "英语不决定结局。": "Shared frozen preparation, actual network state and explicit policy selection determine available directions; free prose and explicitly assisted meaning have no ending-score authority.",
}
GENERIC = {
    "每个主要谜题至少有充分可得来源": ("source_supported_partial", "Author cards and learner materials bind actual safe stands, public/permitted sources, feasible solutions, hints and recovery to main nodes.", "Human solvability and all independent 96-budget units are not proved by the package, source count, method variants or controller route."),
    "英文目标6.5不提高": ("declared_target_human_pending", "The plan and candidate author packages retain Reading/Listening/Writing target 6.5; no higher difficulty is accepted by this review.", "A target label is not measured calibration: independent human 6.5 samples, language editing and comprehension remain pending."),
    "新增重大设定先变更而非偷偷写入": ("source_supported_human_pending", "Provenance and author cards distinguish fixed source facts from declared original local details; player sources withhold later/current truths until actual entitlement.", "This author review does not approve major canon changes or replace human story review. Baseline SHA comparisons apply only to listed source hashes."),
    "没有声音工具或授权则blocked": ("rendered_candidate_rights_blocked", "Local SAPI tooling and actual version/hash-bound WAV candidates exist; current binding metadata explicitly retains audition/redistribution pending.", "HV-004 redistribution/voice rights and human listening signoff are unapproved. Tool availability and a rendered PCM file do not clear authorization or acceptance."),
    "脚本不得标成音轨": ("source_supported_partial", "Learner listening_script keeps its AudioAsset empty; an independent binding and explicit player action use an actual WAV. Viewing transcript never marks listened/comprehended.", "Human listening, alignment and normal-player usability remain pending; native reader tests are not playback tests."),
    "图表来自结构化数据": ("source_supported_partial", "Writing-task tables, operational readings and stored experiment rows are data sources for table presentation; authored prose does not generate a false numeric result.", "No blanket statement that every design visual unit or publication-ready chart is delivered. Final visual assets/layout and human interpretation remain pending."),
    "不得以他章音轨冒充本章": ("source_supported_partial", "Per-material chapter ID, original version/text SHA, speech slices and actual audio SHA bind each candidate independently.", "A chapter's missing voice or scene effect remains a missing item; a different chapter or TTS duration cannot satisfy its required asset budget."),
    "不重写受控事实": ("source_supported_human_pending", "Current learner projections and author provenance retain their original versions and historic facts; alternate methods, assisted meaning and local repairs do not rewrite records.", "Human canonical/story editorial acceptance is still pending; file-hash verification alone does not establish semantic correctness."),
    "不能把close_contract变成直接完成按钮": ("runtime_supported_partial", "The normal service requires current physical device/rescue/delivery states, entitled sources, explicit proposal and a readonly CAS preview before once-only resolution.", "A controller clicking a legitimate submit after actual prerequisites is not a human usability sample; unimplemented optional consumers remain explicitly pending."),
    "关键证据无需指定加点": ("runtime_supported_partial", "Entitled material access depends on actual position/source permission, not a chosen combat stat allocation; shared legal build rules govern encounters separately.", "All viable puzzle methods and minimum configurations have not been independently sampled by human players; individual build runs must be read in their stated scope."),
    "缺通用能力回缺陷任务，不复制第二套规则": ("runtime_supported_partial", "Chapter services use the shared navigation, party/progression, battle engine, storage CAS, foreground lease and receipt model; RF supplies pure hooks rather than a second engine.", "This review does not certify absence of every duplicated rule or replace a full source/architecture review."),
    "复用物流不重奖": ("optional_supported_partial", "Main delivery instances and finite local receipts are retained; each semantic resolution settles once, and newly unbudgeted R4 local work awards zero.", "All planned optional transport/budget rows are not closed. Read each local producer's task-scoped tests; material pickup is not delivery."),
    "可选不藏必需结局条件": ("optional_supported_partial", "Optional recruitment/private copies/local life tasks are separate from main evidence and six-tower baseline; epilogue fragments do not mint ending readiness.", "All story branches and optional budget coverage still need a complete author/QA inventory and human playthrough."),
    "同一物品/人不复制": ("optional_supported_partial", "Custody transitions preserve single unique instances: survivors, ferry lamp items, GLIMMER, sealed sample, exhibition card, inquiry paper/reply and approved local objects.", "This identifies implemented producers; it does not prove every planned item/NPC expression or visual asset exists."),
    "没有本区个人线则说明不适用，不虚增任务": ("optional_supported_partial", "Author cards distinguish applicable personal nodes from N/A and do not count hint/material/repair methods as invented personal tasks.", "The 12-node companion framework/test coverage and a chapter service are not full authored, performed and human-reviewed individual story coverage."),
    "不得只靠测试关卡seed证明连续主线": ("normal_chain_supported_partial", "Actual normal helpers run zero-XP predecessor commands, source-bound sea-route producers and physical sailing/landing. Fresh native-entry profiles retain untouched local state.", "Negative/mechanism/prior-source fixtures remain separate. A copied normal profile is not independent human continuous play; final fresh full-suite and exported-client matrix still await Root evidence."),
    "字幕/完整提示不改变基本奖励": ("runtime_supported_partial", "Resolution transactions carry fixed quest rewards; reading/transcript/lookup/review/hints have no world-reward producer and explicitly assisted meaning is recorded separately.", "A full native subtitle/hint-mode cross-product is not inferred from service constants or a standard controller route."),
    "撤回不重置外部进展": ("runtime_supported_partial", "CAS/load/permission changes invalidate previews while stored original facts, receipt outcomes and physical custody history persist; local recoveries reuse the same object.", "Per-case withdrawal/retry coverage applies only to recorded cases. Every external-event and exported fault scenario is not certified."),
    "声画/材料/规则版本一致": ("version_bound_partial", "Manifest/content compiler, original source versions, text/audio hashes and actual rule releases bind candidates; reports retain their own immutable historical file provenance.", "Current source SHA is not necessarily the source of an older executable report. Require Root's fresh final build/export manifest and per-platform/native verification; art/voice review remains pending."),
    "内部通过不冒充校准": ("explicit_limit_preserved", "Engineering reports keep human_signoff null and native/controller reports state human native play, physical IME, reader accessibility and language calibration not_run/human_pending.", "No CP-*-06, HV-* or release gate is cleared here. RF real human 6–10 minute timing remains pending regardless of simulated rounds or program elapsed seconds."),
}

source_registry = {}
def source(path):
    if not (ROOT/path).is_file(): return None
    if path not in source_registry:
        source_registry[path] = dict(path=path, sha256=sha(path), pending_suffix=path.endswith(".pending"))
    return path

evidence_registry = {}
results_by_task = {}
native_by_region = {}
optional_native_by_region = {}
OPTIONAL_NATIVE_FILES = {"R1": ["r1_life_client_results.json", "lina_personal_client_results.json", "first_relay_client_results.json", "first_side_client_results.json", "first_outer_client_results.json"], "R2": ["r2_life_client_results.json"], "R3": ["musicbox_client_results.json", "r3_shift_work_client_results.json"], "R4": ["chapter_four_local_client_results.json", "scoped_personal_client_results.json"], "R5": ["old_city_local_life_client_results.json", "scoped_personal_client_results.json"], "R6": ["field_relay_client_results.json", "settlement_support_client_results.json"]}
AUXILIARY_TASKS = {"R1": ["CP-LINA-PERSONAL-03", "CP-LINA-PERSONAL-05", "CP-NPC-LIFE"], "R2": ["CP-NPC-LIFE"], "R3": ["CP-NPC-LIFE", "CP-PERSONAL-DETAIL"], "R4": ["CP-NPC-LIFE", "CP-PERSONAL-DETAIL"], "R5": ["CP-NPC-LIFE", "CP-PERSONAL-DETAIL", "CP-R5-LIFE-03", "CP-R5-LIFE-05"], "R6": ["CP-NPC-LIFE", "CP-SETTLEMENT-03", "CP-SETTLEMENT-05"]}
for names in OPTIONAL_NATIVE_FILES.values(): names.append("vessel_life_client_results.json")
builds = sorted((ROOT/"reports/build").glob("20*"))
for build in builds:
    result = build/"test_results.json"
    decoded_error=False
    if not result.is_file():
        failure=build/"failure.json"
        if failure.is_file():
            try:
                nested=read(str(failure.relative_to(ROOT)))
                doc=json.loads(nested.get("error",""))
                if isinstance(doc,dict) and isinstance(doc.get("results"),list):
                    result=failure
                    decoded_error=True
            except (ValueError,TypeError,OSError): pass
    if result.is_file():
        try:
            if not decoded_error: doc=json.loads(result.read_text(encoding="utf-8-sig"))
        except (ValueError,OSError): continue
        for i, item in enumerate(doc.get("results", [])):
            task=item.get("task", "")
            if not re.fullmatch(r"CP-R[0-6F]-0[1-5]", task) and not any(task in names for names in AUXILIARY_TASKS.values()): continue
            key=str(result.relative_to(ROOT)).replace("\\","/")+("#decoded-error/results/" if decoded_error else "#/results/")+str(i)
            evidence_registry[key]=dict(path=key.split("#")[0], json_pointer="/error" if decoded_error else "/results/"+str(i), decoded_error_json_pointer="/results/"+str(i) if decoded_error else None, sha256=sha(key.split("#")[0]), task=task, name=item.get("name"), result=item.get("result"), report_executed=doc.get("executed"), report_failed=doc.get("failed"), human_signoff=doc.get("human_signoff"), scope=doc.get("scope"), fixture_boundary="Read original case source: task tags alone do not distinguish mechanism/prior-source fixtures from true normal chains.")
            results_by_task.setdefault(task,{})[item.get("name", key)]=key
    for region, details in REGIONS.items():
        path=build/details["native"]
        if not path.is_file(): continue
        try: doc=json.loads(path.read_text(encoding="utf-8-sig"))
        except (ValueError,OSError): continue
        key=str(path.relative_to(ROOT)).replace("\\","/")
        evidence_registry[key]=dict(path=key,sha256=sha(key),failures=doc.get("failures"),checks=len(doc.get("checks",[])),source=doc.get("source"),human_native_play=doc.get("human_native_play"),physical_ime=doc.get("physical_ime"),screen_reader=doc.get("screen_reader"),language_calibration=doc.get("language_calibration"),audio_playback=doc.get("audio_playback"),scope="Native engineering driver; historical entry/UI scope must be read, not assumed full current chapter.")
        native_by_region[region]=key
    for region, names in OPTIONAL_NATIVE_FILES.items():
        for name in names:
            path=build/name
            if not path.is_file(): continue
            try: doc=json.loads(path.read_text(encoding="utf-8-sig"))
            except (ValueError,OSError): continue
            key=str(path.relative_to(ROOT)).replace("\\","/")
            evidence_registry[key]=dict(path=key,sha256=sha(key),failures=doc.get("failures"),checks=len(doc.get("checks",[])),source=doc.get("source"),human_native_play=doc.get("human_native_play"),physical_ime=doc.get("physical_ime"),screen_reader=doc.get("screen_reader"),language_calibration=doc.get("language_calibration"),scope="Independent optional-work native engineering driver; does not replace main native or all optional budget coverage.")
            optional_native_by_region.setdefault(region,{})[name]=key

bindings=read("game/content/audio/regional_runtime_bindings.json")
for region, details in REGIONS.items():
    details["source_refs"]=existing([details["package"],details["author"],details["provenance"],details["tests"]]+EXTRA[region]+[str(p.relative_to(ROOT)).replace("\\","/") for p in (ROOT/details["application"]).glob("*.cs")])
    ui=ROOT/details["ui"]
    details["source_refs"]+=([details["ui"]] if ui.is_file() else [str(p.relative_to(ROOT)).replace("\\","/") for p in ui.glob("*.cs")])
    for p in details["source_refs"]+SHARED: source(p)
    content=read(details["package"])
    details["declared_candidate_scene_count"]=len(content.get("scenes",[]))
    details["declared_material_count_in_this_package"]=len(content.get("materials",[]))
    details["candidate_count_limit"]="Package counts are source inventory, not completed independent puzzles/encounters, formal artwork, all chapter language units or human acceptance. R0 package also contains initial R1 geometry; R1 primary package is a Q103/Q104 extension with other sources in Campaign/Q102/Life."
    prefix="QP" if region=="R0" else region+"-"
    details["actual_wav_candidates"]=[dict(material=b["material_id"],material_version=b["material_version"],text_sha256=b["text_sha256"],audio_version=b["audio_version"],asset_path="game/content/"+b["audio_path"],exists=(ROOT/"game/content"/b["audio_path"]).is_file(),asset_sha256=(sha("game/content/"+b["audio_path"]) if (ROOT/"game/content"/b["audio_path"]).is_file() else None),binding_audio_sha256=b["audio_sha256"],review_status=b.get("review_status"),redistribution_rights=b.get("redistribution_rights")) for b in bindings if b["material_id"].startswith(prefix)]
    details["latest_native_evidence_ref"]=native_by_region.get(region)
    details["latest_optional_native_evidence_refs"]=list(optional_native_by_region.get(region,{}).values())
    details["auxiliary_actual_case_evidence_refs"]=[key for task in AUXILIARY_TASKS.get(region,[]) for key in results_by_task.get(task,{}).values()]
    details["listed_baseline_hash_checks"]=[]
    provenance=read(details["provenance"])
    for row in provenance.get("source_files",[]):
        path=row.get("path")
        if path and path.startswith("docs/baseline/"):
            details["listed_baseline_hash_checks"].append(dict(path=path,declared_sha256=row.get("sha256"),current_sha256=sha(path) if (ROOT/path).is_file() else None,matches=(ROOT/path).is_file() and sha(path)==row.get("sha256")))

tasks=[]
for index,task in enumerate(PLAN["tasks"]):
    matched=re.fullmatch(r"CP-(R[0-6F])-0([1-5])",task["id"])
    if not matched: continue
    region,stage=matched.groups(); info=REGIONS[region]
    observed=list(results_by_task.get(task["id"],{}).values())
    # A late test cannot retroactively approve a production card's human fields.
    scope_refs=observed or list(results_by_task.get("CP-"+region+"-03",{}).values())
    criteria=[]
    for n,text in enumerate(task.get("acceptance",[])):
        if text in SPECIFIC:
            status="source_supported_partial_engineering"
            support=SPECIFIC[text]
            limit="Source-level constrained behavior and relevant engineering case evidence only; newest formal chapter task regression, independent human story/language/experience acceptance and complete method cross-product are not certified here."
        else:
            status,support,limit=GENERIC[text]
        if stage=="04": support+=" "+info["optional"]
        if region=="R0" and stage=="04" and text=="没有本区个人线则说明不适用，不虚增任务": status="declared_not_applicable_no_invented_budget"
        criteria.append(dict(index=n,plan_ref="planning/plan.json#/tasks/"+str(index)+"/acceptance/"+str(n),criterion=text,support_status=status,current_implementation_support=support,source_refs=info["source_refs"],engineering_evidence_refs=scope_refs,native_evidence_ref=info["latest_native_evidence_ref"] if stage=="05" else None,optional_native_evidence_refs=info["latest_optional_native_evidence_refs"] if stage in ("04","05") else [],evidence_limit="Task-scoped evidence references are an index; matching a task label does not prove this individual criterion or all chapter routes. Current source is independently hashed and may postdate the evidence executable.",remaining_or_unrun=limit))
    tasks.append(dict(id=task["id"],title=task["title"],plan_task_index=index,original_deliverables=task.get("deliverables",[]),official_evidence_path=task.get("evidence_path"),official_evidence_exists=bool(task.get("evidence_path") and (ROOT/task["evidence_path"]).is_file()),author_review_only=True,completion_claim=False,criteria=criteria,remaining_deliverable_limits=(["Human story/English target calibration and feasible-source review remain unapproved."] if stage=="01" else ["Rendered WAV candidates do not approve redistribution/voice rights/audition/alignment; formal chapter visual art/layers/rights are not delivered by graybox geometry."] if stage=="02" else ["Normal UI and service producers are candidate implementations; final fresh all-tests/export/clean-machine regression remains owned by Root."] if stage=="03" else [info["optional"],"The full optional budget/personal expression inventory is not inferred from these finite producers."] if stage=="04" else ["Controller/native execution is not human continuous play, target-language calibration, physical IME, screen-reader or 6–10 minute timing.","Minimum-growth and unordered-path coverage is limited to named current cases; historical seeds/benchmarks cannot replace true normal routes."])))
    if stage in ("04", "05"):
        for criterion in criteria:
            criterion["auxiliary_actual_case_evidence_refs"] = info["auxiliary_actual_case_evidence_refs"]

report=dict(schema_version=1,review_kind="author_only_source_and_observed_evidence_mapping_not_execution_or_acceptance",generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),plan_path="planning/plan.json",plan_sha256=sha("planning/plan.json"),scope="Exactly CP-R0..RF stages 01..05, every original acceptance item; no execution.json edit and no human gate signature.",human_signoff=None,fullsuite_after_latest_integration="pending_Root_serial_execution",budgets_are_not_completion=dict(puzzles=96,main=64,optional=32,encounter_slots=44,scene_geometry_candidates_not_formal_art=True,core_audio_budget_not_actual_core_count=True),regions=REGIONS,tasks=tasks,source_registry=source_registry,evidence_registry=evidence_registry,finale_calibration_limit=dict(current_source_candidate="rf-balance-3",previous_actual_candidate="rf-balance-2",v2_report="reports/build/20261010T052306626026Z",v2_result="13 executed / 12 passed / 1 failed: pure S Core 11 rounds exceeds 8–10 engineering target; all four routes reached actual ending. Historical failure is retained.",v3_runtime_result="not_claimed_pending_Root_actual_rerun",candidate_v3_change="Core max HP only 1500→1400; Rook/guard/relay/damage/rules/strategy/pace assertions unchanged; original numeric fixtures explicit rf-balance-1.",human_6_to_10_minutes="pending; program elapsed seconds and simulated rounds are not human time"),global_remaining=["No CP-*-06 or HV-* is cleared by this mapping.","Final fresh all-source build/full-suite/current native/export and clean-machine/physical fault testing retain distinct scopes.","Language 6.5 calibration, canon/story editing, audition/alignment/voice rights, formal visual assets, physical IME/accessibility and human play remain pending.","Unbudgeted local work remains zero reward. Pending producer files and content/manifest slots do not count as finished puzzles, encounters or life quests."])
report["finale_calibration_limit"].update(
    actual_candidate="rf-balance-3",
    v3_report="reports/build/20261010T054655523070Z/test_results.json",
    v3_runtime_result="13 executed / 13 passed / 0 failed; four actual zero-XP normal routes, eight original numeric mechanism fixtures and one actual draft/load case.",
    v3_normal_rounds=dict(H=dict(rook=6,core=9),M=dict(rook=6,core=9),S=dict(rook=7,core=10),Mixed=dict(rook=6,core=10)),
    v3_limit="One deterministic DEBUG legal controller per style at standard difficulty; round goals are met in these runs only, not win-rate, human 6–10 minute timing or all variations approval.")
source("reports/content/RF/CP-RF-04.balance_candidate.md")
source("reports/continuous/optional_ui_readonly_qa.author_review.json")
assert len(tasks)==40
assert all(t["criteria"] for t in tasks)
assert all(c["criterion"] in SPECIFIC or c["criterion"] in GENERIC for t in tasks for c in t["criteria"])
OUTPUT=ROOT/"reports/continuous/chapter_acceptance_mapping.author_review.json"
OUTPUT.write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
print(json.dumps(dict(output=str(OUTPUT),tasks=len(tasks),criteria=sum(len(t["criteria"]) for t in tasks),sources=len(source_registry),evidence_refs=len(evidence_registry),human_signoff=None,fullsuite="pending"),ensure_ascii=False))

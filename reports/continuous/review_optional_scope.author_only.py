"""Source audit only. No compiler, tests, native client or execution ledger is invoked."""
from pathlib import Path
import datetime, hashlib, json

ROOT=Path(__file__).resolve().parents[2]
def read(p): return json.loads((ROOT/p).read_text(encoding="utf-8-sig"))
def ref(p, pointer=None):
    return dict(path=p, json_pointer=pointer, sha256=hashlib.sha256((ROOT/p).read_bytes()).hexdigest())
plan=read("planning/plan.json")
combat=read("content/source/part08.json")["encounters"]
seeds=read("content/source/part09.json")["banter_seeds"]
life=[]
for n,line in enumerate((ROOT/"docs/baseline/World_Story_Bible_v0.1.md").read_text(encoding="utf-8-sig").splitlines(),1):
    if 767<=n<=778 and line.startswith("| "):
        fields=[f.strip() for f in line.strip("|").split("|")]
        life.append(dict(region=fields[0],seed=fields[1],problem=fields[2],bounded_outcome_direction=fields[3],source_line=n))
producers=[
    ("R1","src/Mist.Application/R1Life/R1LifeService.cs","existing_finite_candidate",None),
    ("R1","src/Mist.Application/R1Life/R1LifeService.cs","existing_finite_candidate",None),
    ("R2","src/Mist.Application/R2Life/R2LifeService.cs","existing_finite_candidate",None),
    ("R2","src/Mist.Application/R2Life/R2LifeService.cs","existing_finite_candidate",None),
    ("R3","src/Mist.Application/OptionalLocalWorks/MusicBoxRepairService.cs","existing_finite_candidate",None),
    ("R3","src/Mist.Application/R3ShiftWork/R3ShiftWorkService.cs","included_original_candidate_actual_native_evidence","Two finite named shift alternatives, six full bilingual public materials, current availability, same board, morning/afternoon actual acknowledgement, zero rewards. Native engineering evidence is scoped; human acceptance remains pending."),
    ("R4","src/Mist.Application/ChapterFour/ExhibitionCreditService.cs","existing_finite_candidate",None),
    ("R4","src/Mist.Application/ChapterFour/StudyInquiryService.cs","existing_bounded_inquiry_candidate","Actual inquiry and desk reply exist. Alternate practice remains pending allocation; neither attendance nor a changed timetable is claimed."),
    ("R5","src/Mist.Application/OptionalLocalWorks/OldCityLocalLifeService.cs","included_original_candidate_actual_native_evidence","Both current public memories, either named option, finite same sign, mounting/check/receiving; zero rewards and no history rewrite."),
    ("R5","src/Mist.Application/OptionalLocalWorks/OldCityLocalLifeService.Property.cs","included_original_candidate_actual_native_evidence","Unique separate serials, uncertain claim, exact wrapper-only current permission, supervised same object return; private family contents never supplied or read."),
    ("R6","src/Mist.Application/OptionalLocalWorks/SettlementSupportService.cs","included_original_candidate_execution_scoped","Forecast place/stage comparison, exact extra optional stock, actual dispatch and receiving route. Read named observed reports; final current native/regression remains independently scoped."),
    ("R6","src/Mist.Application/OptionalLocalWorks/SettlementSupportService.Lamp.cs","included_original_candidate_execution_scoped","Same borrowed lamp, two named current parties, actual zero-input service/return/manual reserve. No private note or hidden ending gate; execution evidence is independently scoped."),
]
assert len(life)==len(producers)==12
for row,(region,p,status,remaining) in zip(life,producers):
    row.update(region_id=region,status=status,current_producer_ref=ref(p) if p and (ROOT/p).is_file() else None,remaining=remaining,formal_acceptance=False)
banter=[]
banter_package=read("content/source/vessel_life/learner_package.json")
banter_scripts={s["id"]:s for s in banter_package["scripts"]}
for i,seed in enumerate(seeds):
    original=seed["id"]=="BAN-LINA-OREN-1"
    banter.append(dict(id=seed["id"],actors=seed["actors"],scene=seed["scene"],premise=seed["premise"],source_ref=ref("content/source/part09.json","/banter_seeds/"+str(i)),text_status="existing_original_text" if original else "requires_new_complete_candidate_text",runtime_status="requires_current_scene_progress_actor_permission_occupancy_once_producer",original_text_ref=ref("reference/part09/materials/NPC-MAT-06_Chair_Banter.md") if original else None,private_letter_boundary="Actual received/source entitlement and each participant's current granted disclosure scope required; recruitment or co-location supplies neither." if seed["id"] in ("BAN-LINA-2","BAN-LINA-MIRA-2") else "Public local content only; no current unknown/private fact inferred.",rewards=0,mandatory=False))
    script=banter_scripts[seed["id"]]
    banter[-1].update(text_status="existing_original_text" if original else "full_original_bilingual_candidate_human_pending",runtime_status="included_current_permission_occupancy_safe_walk_once_pause_resume_producer",script_ref=ref("content/source/vessel_life/learner_package.json","/scripts/"+str(list(banter_scripts).index(seed["id"]))),script_paragraphs=len(script["paragraphs"]),producer_ref=ref("src/Mist.Application/VesselLife/VesselLifeService.cs"),native_evidence_ref=ref("reports/build/20261010T074415381779Z/vessel_life_client_results.json"),native_limit="Actual normal chair event, pause/resume/once and private-not-inferred checks; not human play or every 24-expression branch execution.")
main=[];optional=[]
for i,e in enumerate(combat):
    row=dict(id=e["id"],region=e["region"],quest=e.get("quest_id"),scene=e["scene_id"],kind=e["kind"],source_ref=ref("content/source/part08.json","/encounters/"+str(i)),completion_claim=False)
    if e["kind"]=="optional_contract_slot":
        row.update(status="requires_new_independent_safe_contract_and_complete_source",template_direction=e["template"],registered_parent_quest=None,numeric_status=e["numeric_status"],remaining=["Specific independent recipient/purpose/source/finite safe arena contract.","Actual once encounter instance/result and local acknowledgement; current geometry safety/return/occupancy.","Reward must be mapped to an existing authorized optional budget or explicitly zero; no kill reward.","Cancel/load/current-source/current-actor regression and native entry; not a respawn or template-count claim."])
        optional.append(row)
    else:
        row.update(classification="main_path_definition_including_bypass_and_exclusive_methods",runtime_coverage="Requires named current source + actual producer/result evidence; merely appearing here is not completion.")
        if e["id"] in ("ENC-R1-02","ENC-R1-03"): row["exclusive_group"]="R1 tower-base entrance; choose one, never force both."
        if e["id"]=="ENC-R6-06": row["boundary"]="Optional field-repair METHOD within Q603 main-path registry; not one of the twelve ENC-R*-O* independent optional slots."
        main.append(row)
assert len(main)==32 and len(optional)==12 and len(banter)==24
optional_package=read("content/source/optional_encounter_contracts/learner_package.json")
contract_by_id={c["id"]:c for c in optional_package["contracts"]}
assert set(contract_by_id)=={row["id"] for row in optional}
for row in optional:
    contract=contract_by_id[row["id"]]
    row.update(status="included_specific_independent_contract_candidate_execution_scoped",contract_source_ref=ref("content/source/optional_encounter_contracts/learner_package.json","/contracts/"+str(list(contract_by_id).index(row["id"]))),producer_ref=ref("src/Mist.Application/OptionalEncounterContracts/OptionalEncounterService.cs"),actual_target=contract["target"],actual_inspection=contract["inspection_action"],actual_local_custodian=contract["local_custodian"],remaining=["Named current normal-route results and local receiving evidence require per-region observed report verification.","Source cards and actual contracts are not human readability, balance or full native acceptance."])
observed_optional={}
observed_auxiliary={}
for path in sorted((ROOT/"reports/build").glob("20*/test_results.json")):
    try: report=json.loads(path.read_text(encoding="utf-8-sig"))
    except (ValueError,OSError): continue
    for index,item in enumerate(report.get("results",[])):
        name=item.get("name","")
        task=item.get("task","")
        evidence=dict(**ref(str(path.relative_to(ROOT)).replace("\\","/"),"/results/"+str(index)),task=task,name=name,result=item.get("result"),report_failed=report.get("failed"),source_currentness="Read historical executable scope; current source may postdate this report.")
        if name.startswith("normal_zero_XP_") and "both_independent_optional_contracts_actual_control_inspection_CAS_and_no_rewards" in name:
            observed_optional[task]=evidence
        if task in ("CP-NPC-LIFE","CP-PERSONAL-DETAIL","CP-R3-04","CP-R3-05","CP-R5-LIFE-03","CP-R5-LIFE-05","CP-SETTLEMENT-03","CP-SETTLEMENT-05","CP-LINA-PERSONAL-03","CP-LINA-PERSONAL-05"):
            observed_auxiliary[(task,name)]=evidence
for row in optional:
    row["observed_normal_region_result"] = observed_optional.get("CP-"+row["region"]+"-03")
    row["remaining"][0] = "Actual normal region case is indexed separately when present; one report is neither all method variants nor human/native acceptance."
hard=dict(review_kind="author_only_exact_seed_to_producer_inventory_not_execution",generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),human_signoff=None,plan_ref=ref("planning/plan.json"),mandatory_task_refs=[dict(id=t["id"],objective=t["objective"],deliverables=t["deliverables"],acceptance=t["acceptance"],plan_pointer="/tasks/"+str(i)) for i,t in enumerate(plan["tasks"]) if t["id"].startswith("CP-") and t["id"].endswith("04")],scope_decision=dict(puzzle96="A controlled authoring budget, not a quota of redundant clicks. Puzzle_System:522 requires merging redundant units. Full candidate still needs a genuine distinct-goal/source/solution/hint/recovery/runtime inventory and concrete coverage-gap production, or an explicit controlled scope revision; no 96-complete claim.",optional12="Master retains 44 and Combat says the twelve optional slots still need their own content. No approved reduction exists. Full candidate requires their independent actual production, not postponing missing content to a human gate.",life12="CP-*-04 explicitly requires regional side work and converting life seeds to scripts. Missing R3 shift/R5 two/R6 two concrete seeds are authoring/runtime work, not only human review.",banter24="Twelve individual plus twelve pair seeds. One original chair text exists; twenty-three full texts and all current-state runtime expression need production. No mandatory greeting quest or companion debt."),regional_life=life,npc_life=banter,encounters=dict(main_definitions=main,optional_slots=optional,counts=dict(main=32,optional=12,total=44),budget_is_not_completion=True),r6_current_pending_refs=[ref(p) for p in ["content/source/optional_local_works/SETTLEMENT_SUPPORT_SCOPE.md","content/source/optional_local_works/settlement_support_contracts.json","src/Mist.Core/OptionalLocalWorks/SettlementSupportModels.cs.pending"] if (ROOT/p).is_file()],source_refs=[ref(p) for p in ["docs/integration/MASTER_GDD_v0.1.md","docs/integration/Integration_Decisions_v0.1.md","docs/baseline/Puzzle_System_v0.1.md","docs/baseline/Combat_System_v0.1.md","docs/baseline/World_Story_Bible_v0.1.md","docs/baseline/NPC_Faction_System_v0.1.md","docs/baseline/Exploration_Level_Design_v0.1.md","planning/tasks/CX-078.md"]])
(ROOT/"reports/continuous/seed_to_producer_hard_scope.author_review.json").write_text(json.dumps(hard,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
hard["scope_decision"]["optional12"] = "Twelve specific independent content contracts and their shared current-state producer are now included, preserving 44 original definitions. Six named normal-region reports are indexed where present. Counts and PASS labels do not approve every variant, human balance or native play."
hard["scope_decision"]["life12"] = "All twelve original regional seeds now have named source-backed author/runtime candidates, including R3 shift, R5 sign/property and R6 forecasts/same lamp. Candidate production is distinct from scoped execution and human language acceptance."
hard["scope_decision"]["banter24"] = "One original chair script is retained and twenty-three full bilingual originals are included with current permission, occupancy, safe NPC movement, once, pause/resume and zero reward. A native chair route and permission-negative checks are observed; all 24 current-expression branches and human presentation are not accepted by that route."
hard["observed_auxiliary_case_evidence"] = list(observed_auxiliary.values())
hard["r6_current_pending_refs"] = [ref(p) for p in ["content/source/optional_local_works/SETTLEMENT_SUPPORT_SCOPE.md","content/source/optional_local_works/settlement_support_contracts.json","src/Mist.Core/OptionalLocalWorks/SettlementSupportModels.cs","src/Mist.Application/OptionalLocalWorks/FieldRelayRepairService.cs"] if (ROOT/p).is_file()]
hard["personal_detail_current_sources"] = [ref(p) for p in ["content/source/lina_personal/learner_package.json","src/Mist.Application/LinaPersonalWorks/LinaPersonalWorkService.cs","content/source/scoped_personal_work/learner_package.json","src/Mist.Application/ScopedPersonalWorks/PersonalProductionRules.cs","tests/Mist.Tests/ScopedPersonalWorks/ScopedPersonalWorkCases.cs"]]
hard["final_current_fullsuite_and_export"] = "pending Root serial execution; no execution.json change or human acceptance"
(ROOT/"reports/continuous/seed_to_producer_hard_scope.author_review.json").write_text(json.dumps(hard,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
qa=dict(review_kind="readonly_static_ui_review_and_owned_campaign_repair_not_native_execution",generated_utc=hard["generated_utc"],human_signoff=None,findings=[
    dict(id="R2-SIDEWELL-ARCHIVE-DISPATCH",severity="P1",status="root_source_fix_observed_native_pending",previous_issue="Public acquired seed/observation material total archive routed into a second generic editor for the same task, then generic meaning/preview rejected unregistered optional quest. Two cached draft owners could diverge CAS heads.",affected=[ref("game/UI/Shell/Root.ChapterTwo.cs"),ref("game/UI/Shell/Root.ChapterThree.cs")],fix_boundary="Dispatch to the existing optional writer/meaning/preview; retain current source and physical submission permission."),
    dict(id="R1-R2-DRAFT-ACK-BOUNDARY",severity="P1",status="root_source_fix_observed_native_fault_pending",previous_issue="A saved prior generation could return true after newer edit; draft close lacked awaited save/IME boundary; permission withdrawal or pause could strand dirty drafts.",affected=[ref("game/UI/Shell/Root.R1Life.cs"),ref("game/UI/Shell/Root.R2Life.cs"),ref("src/Mist.Application/Drafts/DraftEditor.cs")],observed_fix="Single in-flight SaveTask, all editors IsSaved and current IME check, awaited writer back/export, save before pause/withdraw, global Escape awaits all adventure drafts."),
    dict(id="R0-ALL-CACHED-DRAFT-BOUNDARY",severity="P1",status="owned_source_fix_frozen_root_build_native_pending",previous_issue="Campaign Continue saved only active editor before clearing all cached drafts; automatic Q102 handoff also lacked all-cache drain. Mouse writer Back discarded IME preedit after rejected fire-and-forget save.",affected=[ref("game/UI/Campaign/CampaignClient.cs")],observed_fix="Single-flight SaveAllDrafts drains all cached draft CAS results, re-syncs current editor and requires !Composing + All(IsSaved). Continue/handoff/Back/Escape use it, only successful Continue clears caches; close callback no longer launches stale-load save."),
],withdrawn_findings=[dict(id="FIRST-APPROACH-MATERIAL-ENTRY",status="withdrawn_false_alarm",reason="Both R1 approach source spots are actually R1-S09.control_notice, not side_gate/outer_gate. Existing Root.OpenChapterMaterial adds source controls at material.Spot. No missing-source conclusion.",refs=[ref("content/source/first_approaches/learner_package.json"),ref("game/UI/Shell/Root.ChapterOne.cs")])],reviewed_safeguards=["Root close/manual/backup/Continue/Load await all adventure draft owners before clearing/resetting state.","Current source permissions are rechecked by material APIs and dictionary callbacks; private MusicBox material is intentionally not exposed through generic main archive.","R4 two local writers share the main R4 editor cache and dispatch exact producer meaning/preview; native local report has 62 checks, zero failures.","Main R6/Tess personal nodes route to their actual existing chapter closure and bridge records prior receipts without repeat transport or reward.","Current combat/event/transport and occupancy authority remain shared; source acquisition is not physical completion."],native_evidence=[ref("reports/build/20261010T054957141407Z/chapter_four_local_client_results.json")],remaining=["Campaign repair and Root latest dispatch/save patches require Root serial build and native/fault tests.","Physical IME, screen reader, real user input interruption and actual disk fault scenarios not executed by this static audit.","R6 field relay .pending integration and unmade regional/optional budget contracts are production gaps recorded separately.","No blanket absence-of-bugs, full optional completion, human language/audio or final export acceptance claimed."])
(ROOT/"reports/continuous/optional_ui_readonly_qa.author_review.json").write_text(json.dumps(qa,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
qa["native_evidence"] += [ref(p) for p in ["reports/build/20261010T074415381779Z/vessel_life_client_results.json","reports/build/20261010T075112403838Z/r3_shift_work_client_results.json","reports/build/20261010T074519262745Z/old_city_local_life_client_results.json"]]
qa["remaining"] = ["Current final all-source build/regression/export remains Root-owned; prior reports certify only their actual named drivers.","Physical IME, screen reader, real user interruption and actual disk faults are not certified by this static author audit.","Full 24 expression branch coverage, all encounter alternatives and human language/audio review are not inferred from source inventory or native samples.","No blanket absence-of-bugs, human acceptance or execution ledger update is claimed."]
(ROOT/"reports/continuous/optional_ui_readonly_qa.author_review.json").write_text(json.dumps(qa,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
print(json.dumps(dict(life_seeds=len(life),npc_life_seeds=len(banter),main_encounters=len(main),optional_slots=len(optional),human_signoff=None)))

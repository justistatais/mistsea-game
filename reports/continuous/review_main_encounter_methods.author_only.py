"""Read-only per-slot review; writes an author review JSON, never a task ledger.

This does not run the game, tests, build, report finisher or import pipeline.
Historical evidence remains historical. A named entry case is not a completed
method case. R1 outer completion can only be supplemented by its actual native
profile, with that distinction retained in the output.
"""
from __future__ import annotations
import argparse, hashlib, importlib.util, json, os, sqlite3, sys
from contextlib import closing
from datetime import datetime, timezone
from pathlib import Path

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]

def native_path(p):
    p = Path(p).resolve()
    if os.name == 'nt' and not str(p).startswith('\\\\?\\'):
        return Path('\\\\?\\UNC\\' + str(p)[2:] if str(p).startswith('\\\\') else '\\\\?\\' + str(p))
    return p

def read(p): return json.loads(native_path(p).read_text(encoding='utf-8-sig'))
def sha(p): return hashlib.sha256(native_path(p).read_bytes()).hexdigest()
def rel(p): return Path(p).resolve().relative_to(ROOT).as_posix()
def info(p): return dict(path=rel(p), sha256=sha(p), bytes=native_path(p).stat().st_size)
def need(v, message):
    if not v: raise ValueError(message)

def module(path, name):
    spec = importlib.util.spec_from_file_location(name, ROOT / path)
    value = importlib.util.module_from_spec(spec); spec.loader.exec_module(value)
    return value

URI = module('tools/ci/sqlite_evidence.py', '_method_readonly_uri').readonly_uri

def profile(control, case):
    with closing(sqlite3.connect(URI(control), uri=True)) as db:
        pointers = db.execute('SELECT current_path FROM recovery_pointers').fetchall()
    need(len(pointers) == 1, 'Expected one current recovery pointer')
    active = (control.parent / pointers[0][0]).resolve()
    need(active.is_relative_to(control.parent.resolve()) and native_path(active).is_file(), 'Current target missing/outside profile')
    with closing(sqlite3.connect(URI(active), uri=True)) as db:
        branch = db.execute("SELECT value FROM metadata WHERE key='active_branch'").fetchone()
        need(branch and branch[0], 'Missing current branch')
        columns = [r[1] for r in db.execute('PRAGMA table_info(receipts)')]
        need({'branch_id', 'semantic_key', 'action_type', 'receipt_id', 'commit_sequence', 'effect_hash', 'result_json'} <= set(columns), 'Receipt provenance schema missing')
        all_receipts = [dict(zip(columns, row)) for row in db.execute('SELECT * FROM receipts')]
        fixtures = [r for r in all_receipts if r['semantic_key'].startswith(('fixture:', 'internal:')) or r['action_type'] in {'explicit_engineering_fixture', 'internal_slice_initialization'}]
        receipts = [r for r in all_receipts if r['branch_id'] == branch[0]]
        created = [r for r in receipts if r['action_type'] == 'normal_campaign_created' and r['semantic_key'].startswith('campaign:new:')]
        normal = len(created) == 1 and not fixtures
        if normal:
            creation = json.loads(created[0]['result_json'])
            normal = type(creation.get('experience_awarded')) is int and creation['experience_awarded'] == 0
        entities = [dict(entity_id=r[0], kind=r[1], value=json.loads(r[2])) for r in db.execute('SELECT entity_id,kind,payload_json FROM entities WHERE branch_id=?', (branch[0],))]
        xp = db.execute("SELECT quantity FROM balances WHERE branch_id=? AND account='experience'", (branch[0],)).fetchone()
        integrity = db.execute('PRAGMA integrity_check').fetchone()[0]
    return dict(case=case, control=info(control), active_database=info(active), branch=branch[0],
                current_target_within_profile=True, integrity=integrity, normal_zero_XP_no_fixture=normal,
                fixture_receipts=len(fixtures), all_history_receipt_count=len(all_receipts), experience=xp[0] if xp else None,
                creation_receipt=receipt_info(created[0]) if len(created) == 1 else None,
                receipts=receipts, entities=entities, profile_path=rel(control.parent))

def receipt_info(r):
    return {**{k:r[k] for k in ['semantic_key', 'action_type', 'receipt_id', 'commit_sequence', 'effect_hash']}, 'result':json.loads(r['result_json'])}

def public_profile(p):
    return {k:v for k,v in p.items() if k not in {'receipts','entities'}}

def child_proof(suite, case):
    child = suite / 'case_runs' / f"{case['index']:04d}"
    result = read(child / 'test_results.json')
    need(result['executed'] == 1 and result['failed'] == 0 and result['results'] == [dict(task=case['task'],name=case['name'],result='passed')], 'Selected actual child did not execute exactly this case once')
    log = child / 'command.log'; header = json.loads(read_text(log).splitlines()[0])
    need(header['exit_code'] == 0 and header['argv'][-1] == f"--case-index={case['index']}" and Path(header['argv'][1]).resolve() == ROOT/'tests/Mist.Tests/bin/Debug/net10.0/Mist.Tests.dll', 'Selected actual child argv/exit mismatch')
    return dict(result=info(child/'test_results.json'), actual_command=info(log), actual_argv=header['argv'], executed=1, failed=0)

def current_binaries():
    files=list((ROOT/'game/.godot/mono/temp/bin/Debug').rglob('*.dll'))+list((ROOT/'tests/Mist.Tests/bin/Debug/net10.0').glob('*.dll'))
    return [dict(path=rel(p),sha256=sha(p)) for p in sorted(files)]

def suite_commands(suite):
    commands=read(suite/'commands.json');wrapper=read(suite/'result.json')
    need(wrapper['action']=='test' and wrapper['result']=='verified' and wrapper['commands']==commands,'Full suite wrapper is not verified')
    need(not (suite/'reused_compilation.json').exists(),'The reviewed suite reused compilation')
    for command in commands:
        header=json.loads(read_text(ROOT/command['output_path']).splitlines()[0])
        need(header['argv']==command['argv'] and header['exit_code']==command['exit_code'] and command['cwd']==str(ROOT),'Actual suite command/log mismatch')
        need(command['exit_code']==0 or command.get('expected_nonzero_proven') is True,'Unexpected unsuccessful suite command')
    need(any('build' in c['argv'] and c['exit_code']==0 for c in commands),'No successful fresh build command')
    full=[c for c in commands if len(c['argv'])==4 and c['argv'][1]==str(ROOT/'tools/ci/full_suite.py')]
    need(len(full)==1 and full[0]['exit_code']==0 and full[0]['argv'][3]==str(ROOT/'tests/Mist.Tests/bin/Debug/net10.0/Mist.Tests.dll'),'Actual unfiltered full-suite command missing')
    probes=[c for c in commands if c['argv'][-1]=='--fail-probe']
    need(len(probes)==1 and probes[0]['exit_code']==7 and probes[0].get('expected_nonzero_proven') is True and 'intentional test runner failure' in read_text(ROOT/probes[0]['output_path']),'Actual runner failure probe missing')
    return dict(wrapper=info(suite/'result.json'),commands=info(suite/'commands.json'),actual_unfiltered_full_suite=True,actual_successful_fresh_build=True,actual_exit7_failure_probe=True)

def read_text(p): return native_path(p).read_text(encoding='utf-8-sig')

def source(file, symbols, note):
    text=read_text(ROOT/file)
    need(all(symbol in text for symbol in symbols), 'Declared producer symbol missing in '+file)
    return {**info(ROOT/file), 'symbols':symbols, 'contract':note}

def declarations():
    base='src/Mist.Application/'
    entries = {
      'ENC-R0-01':(244,base+'Campaign/CampaignService.cs',['BeginShoreEncounterAsync','PreviewShoreActionAsync','ConfirmShoreActionAsync'],'Actual controlled shore encounter, temporary rescue cooperation, no formal recruitment, zero action XP.'),
      'ENC-R0-02':(526,base+'FirstApproaches/FirstApproachService.cs',['BeginAsync','PreviewActionAsync','ConfirmAsync'],'Actual relay defence at 140 XP; Q-P03 gate requires same current quest/source-bound result.'),
      'ENC-R1-01':(244,base+'Q102/Q102Service.cs',['WorkshopModeAsync','PortIssue','PickBracketAsync'],'Current physical isolated workshop bypass; no claim that the maintenance machine was fought.'),
      'ENC-R1-02':(244,base+'FirstApproaches/FirstApproachService.cs',['PerformSideStepAsync','Accepted'],'Actual isolation, brace and zero-energy side result; mutually exclusive with outer result.'),
      'ENC-R1-03':(None,base+'FirstApproaches/FirstApproachService.cs',['BeginAsync','PreviewActionAsync','ConfirmAsync','Accepted'],'Actual full shared combat outer result; C# completed positive cases have prior fixtures, actual normal native completion is separate.'),
      'ENC-R1-04':(244,base+'ChapterOne/ChapterOneService.Tidegate.cs',['BeginTowerAsync','PreviewTowerActionAsync','VerifyTowerAfterControlAsync'],'Current tower encounter, two valves/isolation and controlled state produce matching TowerSafety, independently inspected before ship handover.'),
    }
    for n in range(1,4):
        entries[f'ENC-R2-{n:02d}']=(352,base+'ChapterTwo/ChapterTwoService.Encounters.cs' if n!=2 else base+'ChapterTwo/ChapterTwoService.Stations.cs', ['BeginEncounterAsync','PreviewEncounterActionAsync'] if n==1 else ['PerformConveyorStepAsync'] if n==2 else ['ChooseCoreMethodAsync','PerformCoreStepAsync'], ['Current root-path threat control under actual inspection scope.','Current conveyor guard, jam release and zero-motion manual result.','Mechanical method is executed with actual controlled core, fixed supports, winding clearance and current valve inspections; selecting a method alone is insufficient.'][n-1])
    for n in range(1,6):
        entries[f'ENC-R3-{n:02d}']=(369,base+'ChapterThree/ChapterThreeService.Encounters.cs', ['PerformInstrumentStepAsync'] if n==3 else ['BeginEncounterAsync','PreviewEncounterActionAsync'], {1:'Actual rail clearance, independent of drainage ladder route.',2:'Actual pump entry threat control under current mine route scope.',3:'Actual instrument isolation, brace and zero-motion result.',4:'Actual rescue return escort control, followed by physical delivery of three people.',5:'Current feed stop and braced arm remain required independently of mining boss control.'}[n])
    for n in range(1,5):
        entries[f'ENC-R4-{n:02d}']=(386,base+'ChapterFour/ChapterFourService.Stations.cs' if n in {2,4} else base+'ChapterFour/ChapterFourService.Encounters.cs', ['PerformReserveStepAsync'] if n==2 else ['PrepareOutputStepAsync'] if n==4 else ['BeginEncounterAsync','PreviewEncounterActionAsync'], {1:'Actual residual threat control under lawful old laboratory entry.',2:'Isolated manual reserve opening, clearance, securing and inspection.',3:'Actual calibration defence before current island experiment work.',4:'Threat control alone is insufficient; current isolate/select_TEST/coupler/guard/inspection creates result; trial observations remain independent.'}[n])
    for n in range(1,5):
        entries[f'ENC-R5-{n:02d}']=(553 if n==2 else 408,base+'ChapterFive/ChapterFiveService.Stations.cs' if n<3 else base+'ChapterFive/ChapterFiveService.Encounters.cs', ['ExecuteStationAsync'] if n<3 else ['BeginEncounterAsync','PreviewEncounterActionAsync'], {1:'Actual current credential, map layers and independent checkpoint passage check.',2:'Actual tunnel isolation, brace, obstacle clearance and zero motion; normal Optional R5 predecessor uses this real alternative route.',3:'Actual nonlethal watcher-group control, with mentor rescue requiring separate physical steps.',4:'Actual nonlethal archival relay control under joint permission with real isolated and secured rack.'}[n])
    for n in range(1,7):
        entries[f'ENC-R6-{n:02d}']=(539 if n==6 else 425,base+'OptionalLocalWorks/FieldRelayRepairService.cs' if n==6 else base+'ChapterSix/ChapterSixService.Encounters.cs', ['SetAuxSwitchAsync','FitDedicatedPinAsync','VerifyZeroAsync'] if n==6 else ['BeginEncounterAsync','PreviewEncounterActionAsync'], {1:'Actual pass-wind control under current briefing and pass material.',2:'Actual measurement station defence; pass result and instrument source remain required.',3:'First cargo escort control requires actual current cargo stage and public escort source.',4:'Second cargo escort control requires actual first-stage cargo and current outer segment.',5:'Actual field device control and protected cargo remain required before ridge platform boss.',6:'This method slot is inside canonical 32: same scoped AUX OFF, dedicated pin and current zero; auxiliary-only combat alternative is also actually exercised. No tower/cargo authority or XP granted.'}[n])
    for n in range(1,5):
        entries[f'ENC-RF-{n:02d}']=(432 if n==1 else 429,base+'Finale/FinaleService.Encounters.cs', ['BeginEncounterAsync','PreviewEncounterActionAsync'], {1:'Actual shaft combat in Mixed normal finale; H/M/S bypasses do not count as this combat execution.',2:'Actual Rook two-point command chain; both sides must be closed for the controlled result.',3:'Actual relay isolation group under current layer/source permission.',4:'Actual machine phases, current verified configuration/stop approval and stopped real relays; progress zero alone cannot produce result.'}[n])
    return entries

def canonical():
    rows=[]
    for line in read_text(ROOT/'docs/baseline/Combat_System_v0.1.md').splitlines():
        if line.startswith('| ENC-'):
            cells=[c.strip() for c in line.strip('|').split('|')]
            if '-O' not in cells[0]: rows.append(dict(id=cells[0],parent_task=cells[1],scene=cells[2],name=cells[3],kind=cells[4],reference_entry=cells[5]))
    need(len(rows)==32 and len({r['id'] for r in rows})==32,'Canonical baseline does not have exactly 32 unique main-design slots')
    return rows

def result_proof(p, encounter):
    result=[r for r in p['receipts'] if json.loads(r['result_json']).get('encounter')==encounter and json.loads(r['result_json']).get('completed') is True and 'method' in json.loads(r['result_json'])]
    if encounter=='ENC-R0-01':
        result=[r for r in p['receipts'] if r['action_type']=='shore_combat_action' and json.loads(r['result_json']).get('outcome')=='controlled']
        state=next(e for e in p['entities'] if e['entity_id']=='combat:shore')['value']
        need(state['battle']['outcome']=='controlled' and json.loads(result[0]['result_json'])['experience_awarded']==0,'Shore not actually controlled at zero action XP')
        return dict(method='actual_shared_shore_combat',receipt=receipt_info(result[0]),state=dict(entity='combat:shore',instance=state['battle']['instance'],attempt=state['battle']['attempt'],outcome='controlled'))
    if encounter=='ENC-R1-01':
        state=next(e for e in p['entities'] if e['entity_id']=='workshop')['value']
        need(state['mode']=='isolated' and state['front_safe'] is True and state['side_open'] is True,'Physical workshop bypass is not currently safe')
        actions=[r for r in p['receipts'] if r['action_type'] in {'workshop_mode','open_inner_latch','pick_bracket'}]
        need({r['action_type'] for r in actions}=={'workshop_mode','open_inner_latch','pick_bracket'},'Workshop bypass lacks actual control and physical access receipts')
        return dict(method='actual_isolated_workshop_bypass',actual_bypass_not_combat=True,state=dict(entity='workshop',value=state),receipts=[receipt_info(r) for r in actions],boundary='Canonical R1-S05 front boundary leads into R1-S06; no invented ENC-R1-01 typed battle result')
    if encounter=='ENC-R1-04':
        result=[r for r in p['receipts'] if r['action_type']=='tower_safety_verified']
        state=next(e for e in p['entities'] if e['entity_id']=='combat:tower')['value']
        need(state['battle']['outcome']=='controlled' and state['result_applied'] is True,'Tower actual control/result state missing')
        value=json.loads(result[0]['result_json']);need(value['safe'] is True and value['source_work']==f"encounter:{state['battle']['instance']}:attempt:{state['battle']['attempt']}",'Tower safety receipt does not bind this current actual attempt')
        return dict(method='actual_shared_tower_combat_and_local_isolation',receipt=receipt_info(result[0]),state=dict(entity='combat:tower',instance=state['battle']['instance'],attempt=state['battle']['attempt'],outcome='controlled',result_applied=True))
    need(len(result)==1,'Selected normal profile lacks exactly one actual completed result for '+encounter)
    obj=json.loads(result[0]['result_json']); matching=[e for e in p['entities'] if e['value']==obj]
    need(matching,'Receipt typed result has no identical current-branch entity')
    return dict(method=obj['method'],receipt=receipt_info(result[0]),typed_current_entity=matching[0])

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('suite');parser.add_argument('--outer-native',required=True)
    parser.add_argument('--output',default='reports/continuous/main_encounter_methods_20261010.author_review.json')
    parser.add_argument('--require-final-fresh',action='store_true',help='Refuse unless full-suite and actual outer native inputs/DLLs equal the current tree and each other; remains author review, no ledger')
    args=parser.parse_args();suite=(ROOT/args.suite).resolve();native=(ROOT/args.outer_native).resolve()
    inventory=read(suite/'full_suite_inventory.json')['cases'];aggregate=read(suite/'test_results.json')
    need(aggregate['executed']==len(inventory)==578 and aggregate['failed']==0,'Only the completed full 578-case suite is reviewable')
    need([(c['task'],c['name']) for c in inventory]==[(c['task'],c['name']) for c in aggregate['results']] and all(c['result']=='passed' for c in aggregate['results']),'Ordered registry/results do not match exactly')
    commands=suite_commands(suite)
    for case in inventory: child_proof(suite,case)
    current=module('tools/ci/build_inputs.py','_method_current_inputs').current_inputs()
    suite_inputs=read(suite/'run_inputs.json')['inputs'];need(read(suite/'build_inputs.json')['inputs']==suite_inputs,'Fresh suite build/run capture differ')
    old={x['path']:x['sha256'] for x in suite_inputs};now={x['path']:x['sha256'] for x in current}
    changed=[p for p in sorted(set(old)|set(now)) if old.get(p)!=now.get(p)]
    binaries=current_binaries();suite_binary_match=read(suite/'compiled_artifacts.json')['binaries']==binaries
    if args.require_final_fresh: need(not changed and suite_binary_match,'Final fresh review requires complete current source/input and sorted DLL matches')
    profiles=[];errors=[]
    for control in sorted((suite/'case_runs').rglob('control.sqlite')):
        case=inventory[int(control.relative_to(suite/'case_runs').parts[0])]
        try: profiles.append(profile(control,case))
        except Exception as exc: errors.append(dict(path=rel(control),error=type(exc).__name__+': '+str(exc)))
    records=[];decl=declarations()
    for row in canonical():
        encounter=row['id'];index,file,symbols,note=decl[encounter]
        entry={**row,'optional':False,'producer':source(file,symbols,note),'methods':[],'status':'pending'}
        entry['producer']['matches_suite_input']=old.get(file)==entry['producer']['sha256']
        need(entry['producer']['matches_suite_input'],'Actual producer differs from the reviewed suite capture')
        if index is not None:
            candidates=[p for p in profiles if p['case']['index']==index and p['normal_zero_XP_no_fixture'] and p['integrity']=='ok']
            proofs=[]
            for p in candidates:
                try: proofs.append((p,result_proof(p,encounter)))
                except (ValueError,KeyError,StopIteration): continue
            need(proofs,'Declared selected case has no real completed normal proof for '+encounter)
            p,actual=proofs[0];case=p['case']
            entry['methods']=[dict(task=case['task'],case_name=case['name'],case_index=index,method=actual['method'],evidence_class='actual_normal_CSharp_completed_method',actual=actual,profile=public_profile(p),child=child_proof(suite,case))]
            if encounter=='ENC-R6-06':
                p=next(p for p in profiles if p['case']['index']==540 and p['normal_zero_XP_no_fixture']);actual=result_proof(p,encounter);case=p['case']
                entry['methods'].append(dict(task=case['task'],case_name=case['name'],case_index=540,method=actual['method'],evidence_class='actual_normal_CSharp_completed_method',actual=actual,profile=public_profile(p),child=child_proof(suite,case)))
            entry['status']='historical_normal_method_verified_rebind_pending' if changed else 'actual_normal_method_verified_candidate'
        else:
            p=profile(native/'client_profile/control.sqlite',inventory[523]);need(p['normal_zero_XP_no_fixture'] and p['integrity']=='ok','Native outer did not retain actual normal creation/history')
            actual=result_proof(p,encounter);data=read(native/'first_outer_client_results.json');need(data['failures']==0 and data['route']==encounter and all(c['passed'] for c in data['checks']),'Actual outer native checks did not pass')
            header=json.loads(read_text(native/'client_tests.log').splitlines()[0]);need(header['exit_code']==0 and header['argv'][-1]=='--first-outer-client-test' and '--headless' in header['argv'],'Actual outer native command missing')
            source_copy=read(native/'client_profile_source.json');need(source_copy['scope'].startswith('copied untouched actual normal-route') and source_copy['suite_dir']==rel(suite) and source_copy['selected_case']==inventory[523] and source_copy['fixture_receipts']==0,'Actual native input is not the exact normal entry case copy')
            source_db=ROOT/source_copy['source_active_database'];need(sha(source_db)==source_copy['source_active_database_sha256'],'Original selected source DB changed')
            source_proof=ROOT/source_copy['source'];need(source_proof.is_relative_to(suite/'case_runs'/'0523'/'testdata') and source_proof.name=='normal-first-tower-native-entry.json','Incorrect source proof path/name')
            need(len(list((suite/'case_runs'/'0523'/'testdata').rglob(source_proof.name)))==1 and sha(source_proof)==source_copy['proof_sha256'],'Original actual entry proof is ambiguous or changed')
            source_control=source_db.parent.parent/'control.sqlite';original=profile(source_control,inventory[523]);need(original['active_database']['path']==rel(source_db) and original['branch']==p['branch'] and original['normal_zero_XP_no_fixture'] and original['creation_receipt']==p['creation_receipt'],'Original source pointer/current branch/normal creation differs from actual native profile')
            # native_source_cache.py defines suite_inputs_sha256 as build_inputs.json;
            # complete build/run inputs are separately required to agree above.
            for key,file in [('suite_results_sha256',suite/'test_results.json'),('suite_inputs_sha256',suite/'build_inputs.json'),('suite_inventory_sha256',suite/'full_suite_inventory.json'),('compiled_artifacts_sha256',suite/'compiled_artifacts.json'),('case_results_sha256',suite/'case_runs/0523/test_results.json'),('case_command_sha256',suite/'case_runs/0523/command.log')]:
                need(source_copy[key]==sha(file),'Actual native source capture hash mismatch: '+key)
            copied=source_copy['copied_files'];original_root=source_control.parent
            actual_files=sorted(rel(f) for f in original_root.rglob('*') if native_path(f).is_file())
            need(actual_files==sorted(x['source'] for x in copied),'Native full-copy inventory omits or adds original files')
            for file in copied:
                original_file=ROOT/file['source'];need(not native_path(original_file).is_symlink() and sha(original_file)==file['sha256']==file['copied_before_native_sha256'],'Original/copy-before-native file SHA mismatch')
            native_inputs=read(native/'run_inputs.json')['inputs'];native_binary_match=read(native/'compiled_artifacts.json')['binaries']==binaries
            native_fresh=not changed and suite_binary_match and native_inputs==current==suite_inputs and native_binary_match
            if args.require_final_fresh: need(native_fresh,'Final fresh review requires an outer native run sourced from this exact current fresh suite and DLL set')
            entry['available_native_method']=dict(method=actual['method'],actual=actual,profile=public_profile(p),report=info(native/'first_outer_client_results.json'),actual_command=info(native/'client_tests.log'),source_copy=info(native/'client_profile_source.json'),entry_case=inventory[523],entry_child=child_proof(suite,inventory[523]),case_role='CSharp proves untouched entry only; native independently executes complete outer method',native_inputs_match_current=native_inputs==current,native_inputs_match_reviewed_suite=native_inputs==suite_inputs,native_DLLs_match_current=native_binary_match,original_full_profile_copy_files=len(copied),source_files_and_before_native_SHA_exact=True,final_fresh_rebound=native_fresh,human_play='not_run')
            entry['status']='actual_normal_native_completed_method_candidate_not_CSharp_completion' if native_fresh else 'historical_actual_normal_native_completed_method_requires_final_fresh_native_rebind'
            entry['gap']='No normal CSharp completed outer case in this 578 suite. Do not map fixture cases533/535/536/537 or entry-only523 to a completed normal CSharp method. '+('Separate actual normal native completion is now rebound to this exact current fresh suite, complete inputs and DLLs; this remains native engineering evidence, not a completed normal CSharp case or human/96-budget acceptance.' if native_fresh else 'Separate actual native result exists; final fresh source/input/native rebind remains.')
        records.append(entry)
    output=dict(scope='Reports-only author review of canonical 32 encounter design slots and actual methods; no task ledger, build, game or tests run by this audit',created_at=datetime.now(timezone.utc).isoformat(),suite=dict(path=rel(suite),inventory=info(suite/'full_suite_inventory.json'),aggregate=info(suite/'test_results.json'),ordered_exact_cases=578,ordered_actual_children_1case_0fail=578,failures=0,current_complete_input_match=not changed,current_sorted_DLL_match=suite_binary_match,compiled_artifacts=info(suite/'compiled_artifacts.json'),changed_inputs=changed,commands=commands),canonical_definition_slots=32,normal_CSharp_completed_slots=31,additional_actual_normal_native_completed_slots=1,independent_method_family_count='not_claimed',semantic_96_budget='pending_separate_semantic_goal_review',optional_12_encounters='not_counted_in_this32',status='author_review_pending_human_gates' if args.require_final_fresh else 'author_review_pending_final_fresh_rebind_and_human_gates',final_fresh_rebind_required=args.require_final_fresh,legacy_method_report_ready=False,legacy_checker_boundary='Current release checker validates only producer SHA and known passed case name. This report deliberately leaves outer methods empty; it must not turn an entry-only or fixture case into a completed normal method.',profile_scan=dict(controls=len(profiles)+len(errors),readable_profiles=len(profiles),normal_zero_XP_no_fixture_profiles=sum(p['normal_zero_XP_no_fixture'] for p in profiles),errors=errors,readonly_sqlite=True,extended_windows_filesystem_paths=True),encounters=records,human=dict(language_band_6_5='pending',balance='pending',readability='pending',audio_audition_and_rights='pending',full_human_play='not_run',formal_release='blocked'))
    destination=(ROOT/args.output).resolve();need(destination.is_relative_to(ROOT/'reports/continuous'),'Only reports/continuous output is permitted');need(not destination.exists(),'Preserve prior author reviews; choose a new output file')
    destination.write_text(json.dumps(output,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(output=rel(destination),main_slots=32,CSharp_normal_completed=31,actual_native_normal_completed=1,current_complete_inputs_match=not changed,changed_inputs=changed,profile_errors=len(errors),legacy_ready=False),ensure_ascii=True))

if __name__=='__main__': main()

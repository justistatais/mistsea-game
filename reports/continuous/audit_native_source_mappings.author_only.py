"""Read-only diagnosis of every native source mapping.

This intentionally does not import build.py, native_source_cache.py or any runner.
It reads original isolated child results and profiles, using only mode=ro SQLite.
The default historical diagnosis never qualifies a suite. --current-fresh checks
current inputs, fresh commands and DLLs before source-cache qualification only.
The JSON output is an audit artifact, not a task report or execution ledger update.
"""
from __future__ import annotations
import argparse,ast,hashlib,importlib.util,json,sqlite3,sys
from contextlib import closing
from datetime import datetime,timezone
from pathlib import Path

sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[2]
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def relative(p):return p.resolve().relative_to(ROOT).as_posix()
def info(p):return {'path':relative(p),'sha256':sha(p),'bytes':p.stat().st_size}
def need(value,message):
    if not value:raise ValueError(message)
def readonly(p):
    spec=importlib.util.spec_from_file_location('_audit_sqlite_readonly',ROOT/'tools/ci/sqlite_evidence.py')
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    return module.readonly_uri(p)
def current_inputs():
    spec=importlib.util.spec_from_file_location('_audit_current_build_inputs',ROOT/'tools/ci/build_inputs.py')
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    return module.current_inputs()
def current_binaries():
    files=list((ROOT/'game/.godot/mono/temp/bin/Debug').rglob('*.dll'))+list((ROOT/'tests/Mist.Tests/bin/Debug/net10.0').glob('*.dll'))
    return [{'path':relative(p),'sha256':sha(p)} for p in sorted(files)]
def fresh_suite_proof(suite):
    inputs=current_inputs();build=read(suite/'build_inputs.json');run=read(suite/'run_inputs.json')
    need(build['inputs']==inputs and run['inputs']==inputs,'Complete current input set differs from fresh suite build/run captures')
    need(not (suite/'reused_compilation.json').exists(),'Source suite reused compilation instead of a fresh build')
    binaries=current_binaries();need(read(suite/'compiled_artifacts.json')['binaries']==binaries,'Current sorted game/test DLL fingerprint set differs from fresh suite')
    need({'MistSea.dll','Mist.Core.dll','Mist.Application.dll','Mist.Tests.dll'}<={Path(r['path']).name for r in binaries},'Fresh compiled proof is incomplete')
    commands=read(suite/'commands.json');result=read(suite/'result.json')
    need(result['action']=='test' and result['result']=='verified' and result['commands']==commands,'Fresh full-suite wrapper result/command inventory is not verified')
    for command in commands:
        log=ROOT/command['output_path'];text=log.read_text(encoding='utf-8-sig');header=json.loads(text.splitlines()[0])
        need(header['argv']==command['argv'] and header['exit_code']==command['exit_code'] and command['cwd']==str(ROOT),'Actual fresh command/log mismatch')
        need(command['exit_code']==0 or command.get('expected_nonzero_proven') is True,'Unexpected failed fresh command')
    need(any('build' in c['argv'] and c['exit_code']==0 for c in commands),'No actual successful fresh compilation')
    full=[c for c in commands if len(c['argv'])==4 and c['argv'][1]==str(ROOT/'tools/ci/full_suite.py')]
    need(len(full)==1 and full[0]['exit_code']==0 and full[0]['argv'][3]==str(ROOT/'tests/Mist.Tests/bin/Debug/net10.0/Mist.Tests.dll'),'Missing exact successful unfiltered actual full-suite command')
    probes=[c for c in commands if c['argv'][-1]=='--fail-probe']
    need(len(probes)==1 and probes[0]['exit_code']==7 and probes[0].get('expected_nonzero_proven') is True and 'intentional test runner failure' in (ROOT/probes[0]['output_path']).read_text(encoding='utf-8-sig'),'Failure probe lacks actual expected exit7/marker proof')
    return {'build_inputs':info(suite/'build_inputs.json'),'run_inputs':info(suite/'run_inputs.json'),
      'current_input_count':len(inputs),'current_inputs':inputs,'compiled_artifacts':info(suite/'compiled_artifacts.json'),
      'current_sorted_binaries':binaries,'commands':info(suite/'commands.json'),'verified_wrapper':info(suite/'result.json'),
      'failure_probe':info(ROOT/probes[0]['output_path']),'scope':'Fresh exact-current full-suite evidence; no native or human acceptance claim'}
def mappings():
    tree=ast.parse((ROOT/'tools/ci/build.py').read_text(encoding='utf-8-sig'));found={}
    for node in ast.walk(tree):
        if not isinstance(node,ast.Assign) or len(node.targets)!=1 or not isinstance(node.targets[0],ast.Name):continue
        name=node.targets[0].id
        if name not in {'source_task','source_marker','source_name'}:continue
        value=node.value
        if isinstance(value,ast.Call) and isinstance(value.func,ast.Attribute) and value.func.attr=='get':
            found[name]=(ast.literal_eval(value.func.value),ast.literal_eval(value.args[1]) if len(value.args)>1 else None)
        elif isinstance(value,ast.Subscript):found[name]=(ast.literal_eval(value.value),None)
    need(set(found)=={'source_task','source_marker','source_name'},'Cannot extract exact frozen build source maps')
    return [{ 'action':action,'source_task':found['source_task'][0].get(action,found['source_task'][1]),
              'source_marker':found['source_marker'][0].get(action,found['source_marker'][1]),'source_name':proof}
            for action,proof in found['source_name'][0].items()]
def receipt_objects(value):
    if isinstance(value,dict):
        for k,v in value.items():
            if k.replace('_','').lower() in {'receipts','actualreceipts'}:
                need(isinstance(v,list) and all(isinstance(r,dict) for r in v),'Declared receipt history is malformed');yield from v
            else:yield from receipt_objects(v)
    elif isinstance(value,list):
        for item in value:yield from receipt_objects(item)
def profile_proof(proof,child):
    profile=proof.parent
    while not (profile/'control.sqlite').is_file():
        profile=profile.parent;need(profile.is_relative_to(child/'testdata'),'Proof has no own profile control.sqlite')
    files=sorted(p for p in profile.rglob('*') if p.is_file())
    need(not profile.is_symlink() and all(not p.is_symlink() for p in profile.rglob('*')),'Profile contains symlinks')
    with closing(sqlite3.connect(readonly(profile/'control.sqlite'),uri=True)) as db:
        pointers=db.execute('SELECT current_path FROM recovery_pointers').fetchall()
    need(len(pointers)==1,'Control must contain exactly one current database pointer')
    active=(profile/pointers[0][0]).resolve();need(active.is_relative_to(profile.resolve()) and active.is_file(),'Current recovery generation missing or outside copied profile')
    need(active in [p.resolve() for p in files],'Full profile copy inventory omits current active database')
    with closing(sqlite3.connect(readonly(active),uri=True)) as db:
        need(db.execute('PRAGMA integrity_check').fetchone()[0]=='ok','Current active database integrity failed')
        columns=[r[1] for r in db.execute('PRAGMA table_info(receipts)')]
        need({'branch_id','semantic_key','action_type','receipt_id','commit_sequence','effect_hash','result_json'}<=set(columns),'Current receipt schema lacks cache provenance columns')
        branch=db.execute("SELECT value FROM metadata WHERE key='active_branch'").fetchone();need(branch and branch[0],'Current active branch missing')
        rows=[dict(zip(columns,row)) for row in db.execute('SELECT * FROM receipts')]
        fixtures=[r for r in rows if str(r.get('semantic_key','')).startswith(('fixture:','internal:')) or r.get('action_type') in {'explicit_engineering_fixture','internal_slice_initialization'}]
        need(rows and not fixtures,'Source profile has positive fixture world receipt history')
        current=[r for r in rows if r['branch_id']==branch[0]]
        created=[r for r in current if r['action_type']=='normal_campaign_created']
        need(len(created)==1 and created[0]['semantic_key'].startswith('campaign:new:'),'Current branch lacks exactly one actual normal campaign creation')
        creation=json.loads(created[0]['result_json']);need(type(creation.get('experience_awarded')) is int and creation['experience_awarded']==0,'Normal creation is not exact integer zero-XP')
        xp=db.execute("SELECT quantity FROM balances WHERE branch_id=? AND account='experience'",(branch[0],)).fetchone()
    declared=list(receipt_objects(read(proof)));lookup={r['semantic_key']:r for r in current};mismatches=[]
    for record in declared:
        normalized={k.replace('_','').lower():v for k,v in record.items()};key=normalized.get('semantickey');actual=lookup.get(key)
        if actual is None:mismatches.append({'semantic_key':key,'field':'current_branch_receipt_absent'});continue
        for name,field in [('actiontype','action_type'),('receiptid','receipt_id'),('id','receipt_id'),('branchid','branch_id'),('commitsequence','commit_sequence'),('sequence','commit_sequence'),('effecthash','effect_hash'),('resultjson','result_json')]:
            if name in normalized and normalized[name]!=actual[field]:mismatches.append({'semantic_key':key,'field':name})
        for name in ['result','resultjson']:
            if name in normalized:
                expected=normalized[name];expected=json.loads(expected) if isinstance(expected,str) else expected
                if expected!=json.loads(actual['result_json']):mismatches.append({'semantic_key':key,'field':name})
    need(not mismatches,'Entry declared history differs from current branch: '+str(mismatches[:3]))
    return {'proof':info(proof),'profile_root':relative(profile),'control':info(profile/'control.sqlite'),
            'current_path':pointers[0][0],'actual_active_database':info(active),'current_branch':branch[0],
            'actual_current_receipt_count':len(current),'all_branch_receipt_count':len(rows),'fixture_receipts':len(fixtures),
            'actual_creation':{'semantic_key':created[0]['semantic_key'],'action_type':created[0]['action_type'],'experience_awarded':creation['experience_awarded']},
            'actual_current_experience':xp[0] if xp else 0,'declared_json_receipt_count':len(declared),'declared_receipt_mismatches':mismatches,
            'summary_only_json_scope':'JSON is only an entry summary; actual receipt history is read from control current database' if not declared else None,
            'complete_source_profile_file_inventory':[{'path':p.relative_to(profile).as_posix(),'sha256':sha(p),'bytes':p.stat().st_size} for p in files],
            'profile_full_copy_contract':'Static production uses copytree(profile, client_profile), preserving every relative generation/control/checkpoint file and verifying every copied SHA; no copy executed by this read-only audit.'}
def audit(suite,current_fresh=False):
    fresh=fresh_suite_proof(suite) if current_fresh else None
    inventory=read(suite/'full_suite_inventory.json')['cases'];aggregate=read(suite/'test_results.json')
    need([c['index'] for c in inventory]==list(range(len(inventory))),'Compiled registry sequence malformed')
    format_differences=[]
    for i,(case,result) in enumerate(zip(inventory,aggregate['results'])):
        if (case['task'],case['name'],'passed')!=(result['task'],result['name'],result['result']):
            format_differences.append({'index':i,'inventory':case,'aggregate':result})
    all_children=[]
    for case in inventory:
        child=suite/'case_runs'/f"{case['index']:04d}";result=read(child/'test_results.json');expected={'task':case['task'],'name':case['name'],'result':'passed'}
        need(result['executed']==1 and result['failed']==0 and result['results']==[expected],'Original actual child disagrees with compiled registry: '+str(case['index']))
        command=json.loads((child/'command.log').read_text(encoding='utf-8-sig').splitlines()[0])
        need(command['exit_code']==0 and command['cwd']==str(ROOT) and command['argv'][1]==str(ROOT/'tests/Mist.Tests/bin/Debug/net10.0/Mist.Tests.dll')
             and command['argv'][2:]==['--case-index='+str(case['index'])],'Original child actual argv differs: '+str(case['index']))
        all_children.append({'index':case['index'],'result':info(child/'test_results.json'),'command':info(child/'command.log')})
    reports=[];cache={}
    for mapping in mappings():
        entry={**mapping,'source_mapping_diagnosis':'blocked','errors':[]}
        try:
            eligible=[c for c in inventory if c['task']==mapping['source_task'] and (mapping['source_marker'] is None or mapping['source_marker'] in c['name'])]
            entry['eligible_cases']=eligible
            if mapping['source_marker'] is not None:need(len(eligible)==1,'Marker does not select exactly one case of source_task')
            matches=[]
            for case in eligible:
                child=suite/'case_runs'/f"{case['index']:04d}"
                for proof in (child/'testdata').rglob(mapping['source_name']):matches.append((case,child,proof))
            entry['proof_match_count']=len(matches);need(len(matches)==1,'Exact proof filename does not select exactly one eligible actual child profile')
            case,child,proof=matches[0];entry['selected_original_child']=case
            if str(proof) not in cache:cache[str(proof)]=profile_proof(proof,child)
            entry['actual_source_profile']=cache[str(proof)];entry['source_mapping_diagnosis']='shape_compatible'
        except (ValueError,KeyError,OSError,sqlite3.DatabaseError) as error:entry['errors'].append(str(error))
        reports.append(entry)
    blockers=[{'action':r['action'],'errors':r['errors']} for r in reports if r['errors']]
    if current_fresh:
        need(not format_differences and aggregate['executed']==len(inventory)==len(aggregate['results'])==578 and aggregate['failed']==0,
             'Current final suite must contain 578 exact ordered registry/aggregate passes without format differences')
        need(aggregate['scope']=='every registered C# case executed exactly once in isolated child profiles','Source is not the actual isolated complete suite')
        need(not blockers and len(reports)==27,'At least one of the 27 exact source mapping contracts is blocked')
        need(fresh['current_inputs']==current_inputs() and fresh['current_sorted_binaries']==current_binaries(),'Inputs/DLLs changed during the read-only audit')
    return {'schema_version':1,'generated_utc':datetime.now(timezone.utc).isoformat(),'suite':relative(suite),'suite_qualified':current_fresh,
       'qualification_scope':'Actual current fresh full-suite source-cache compatibility only; does not qualify any native action, human gate or formal release' if current_fresh else 'Historical format diagnosis only; no qualification',
       'fresh_exact_current_suite_proof':fresh,
       'scope':'Read-only exact-current full-suite and all source-profile mapping audit' if current_fresh else 'Read-only format/source-shape diagnosis of previous suite only. Aggregate Chinese name encoding corruption is preserved and never waived for cache qualification. Current CI inputs also differ, so a new final suite remains required.',
       'aggregate':info(suite/'test_results.json'),'inventory':info(suite/'full_suite_inventory.json'),'registered':len(inventory),
       'aggregate_executed':aggregate['executed'],'aggregate_failed':aggregate['failed'],'aggregate_format_differences':format_differences,
       'original_child_count_exactly_matched':len(all_children),'original_child_proofs':all_children,
       'audited_current_sources':[info(ROOT/p) for p in ['tools/ci/build.py','tools/ci/native_source_cache.py','tools/ci/sqlite_evidence.py','reports/continuous/run_native_sequence.py']],
       'profile_source_mapping_count':len(reports),'non_profile_native_actions':['client-test','campaign-client-test'],
       'source_mapping_results':reports,'other_source_shape_blockers':blockers,
       'no_native_or_human_qualification':True,'no_suite_qualification':not current_fresh,'native_or_cache_executed':False,'copied_profiles':False,'ci_source_or_ledger_mutated':False}
def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--suite',required=True);parser.add_argument('--output',required=True);parser.add_argument('--current-fresh',action='store_true');args=parser.parse_args()
    suite=(ROOT/args.suite).resolve();need(suite.is_relative_to(ROOT/'reports/build'),'Suite outside build reports')
    out=(ROOT/args.output).resolve();need(out.is_relative_to(ROOT/'reports/continuous'),'Audit output outside reports-only scope')
    result=audit(suite,args.current_fresh);out.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    print(json.dumps({'audit':relative(out),'suite_qualified':result['suite_qualified'],'qualification_scope':result['qualification_scope'],'registered':result['registered'],'aggregate_name_differences':len(result['aggregate_format_differences']),
      'actual_original_children':result['original_child_count_exactly_matched'],'source_mappings':result['profile_source_mapping_count'],'blockers':result['other_source_shape_blockers']},ensure_ascii=True))
if __name__=='__main__':main()

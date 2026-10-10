"""Validate frozen real evidence and prepare 40 partial chapter reports.

Default is a read-only audit. --write-reports invokes the existing task_report.finish
API; only that API may update execution.json. No human/CP06/HV gate is signed.

Example, after Root finishes the current tree (paths are supplied, never guessed):
  python reports/continuous/finish_chapter_candidates.py --suite-dir reports/build/FINAL \
    --compiled-build-dir reports/build/FRESH --package dist/CANDIDATE.zip \
    --native-root reports/build --output reports/continuous/chapter_final_preflight.json
Add --write-reports only after examining that preflight. An optional --native path
adds a specifically selected result. Inputs are revalidated immediately before API calls.
"""
from __future__ import annotations
import argparse
from contextlib import closing
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import shutil
import sqlite3
import sys
import zipfile

ROOT=Path(__file__).resolve().parents[2]
REGIONS=('R0','R1','R2','R3','R4','R5','R6','RF')
TASK_IDS={f'CP-{region}-{step:02d}' for region in REGIONS for step in range(1,6)}
sys.dont_write_bytecode=True

class EvidenceError(RuntimeError):pass

def require(condition,message):
    if not condition:raise EvidenceError(message)

def inside(value,base=ROOT):
    p=Path(value);p=p if p.is_absolute() else ROOT/p;p=p.resolve()
    require(p.is_relative_to(base.resolve()),'Path outside allowed evidence workspace: '+str(p))
    return p

def rel(p):return inside(p).relative_to(ROOT).as_posix()
def sha(p):return hashlib.sha256(inside(p).read_bytes()).hexdigest()
def read(p):return json.loads(inside(p).read_text(encoding='utf-8-sig'))
def info(p):
    p=inside(p);require(p.is_file(),'Missing evidence file: '+rel(p))
    return {'path':rel(p),'sha256':sha(p),'bytes':p.stat().st_size}

def current_inputs():
    spec=importlib.util.spec_from_file_location('_chapter_readonly_build_inputs',ROOT/'tools/ci/build_inputs.py')
    require(spec is not None and spec.loader is not None,'Cannot load existing read-only input inventory')
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    return module.current_inputs()

def readonly_uri(path):
    spec=importlib.util.spec_from_file_location('_chapter_readonly_sqlite_evidence',ROOT/'tools/ci/sqlite_evidence.py')
    require(spec is not None and spec.loader is not None,'Cannot load existing read-only SQLite evidence URI helper')
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    return module.readonly_uri(inside(path))

def records_map(records):
    require(isinstance(records,list) and records,'Input manifest must contain nonempty inputs')
    result={}
    for record in records:
        require(set(record)>={'path','sha256','bytes'},'Invalid input record')
        p=inside(record['path']);require(p.is_file(),'Input no longer exists: '+record['path'])
        require(record['path'] not in result,'Duplicate input record: '+record['path'])
        require(sha(p)==record['sha256'],'Changed frozen input: '+record['path'])
        require(p.stat().st_size==record['bytes'],'Frozen input byte count differs: '+record['path'])
        result[record['path']]=record['sha256']
    return result

def code_input(path):
    return Path(path).suffix in {'.cs','.csproj','.sql'} or Path(path).name in {
      'packages.lock.json','global.json','Directory.Build.props','Directory.Build.targets','MistSea.sln','project.godot','export_presets.cfg'}

def commands(directory):
    p=directory/'commands.json';values=read(p)
    require(isinstance(values,list) and values,'Missing actual command log: '+rel(p))
    for c in values:
        require(isinstance(c.get('argv'),list) and c['argv'],'Command missing actual argv')
        code=c.get('exit_code');expected=c.get('expected_exit_code',c.get('expected_code'))
        allowed=code==0 or c.get('expected_nonzero_proven') is True or (code==7 and expected==7 and '--fail-probe' in c['argv'])
        require(allowed,'Unsuccessful command in selected evidence: '+str(c['argv'])+' exit='+str(code))
        require(inside(c['output_path']).is_file(),'Actual command stdout/log missing: '+str(c['output_path']))
    return values

def validate_compilation(directory,now):
    require(not (directory/'reused_compilation.json').exists(),'Compilation proof must be a fresh build, not reused artifacts')
    frozen=records_map([r for r in read(directory/'build_inputs.json')['inputs'] if code_input(r['path'])])
    require(any('build' in c['argv'] and c.get('exit_code')==0 for c in commands(directory)),'No actual successful compilation command')
    expected={k:v for k,v in now.items() if code_input(k)}
    observed={k:v for k,v in frozen.items() if code_input(k)}
    require(expected==observed,'Fresh compilation does not cover this exact current code/project/SQL input set')
    return {'inputs':info(directory/'build_inputs.json'),'commands':info(directory/'commands.json'),'current_code_input_count':len(expected)}

def validate_reuse(directory):
    if not (directory/'reused_compilation.json').exists():return None
    value=read(directory/'reused_compilation.json');binaries=value.get('binaries',[])
    require(binaries,'Reused run lacks concrete binary SHA records')
    # A native run can include both game and test copies; compare every recorded actual binary.
    for b in binaries:require(sha(b['path'])==b['sha256'],'Selected run uses a different binary: '+b['path'])
    require({'Mist.Core.dll','Mist.Application.dll','MistSea.dll','Mist.Tests.dll'} <= {Path(b['path']).name for b in binaries},'Incomplete reused compiled application/native/test proof')
    return info(directory/'reused_compilation.json')


def validate_compiled_artifacts(directory):
    path=directory/'compiled_artifacts.json';recorded=read(path)['binaries']
    files=list((ROOT/'game/.godot/mono/temp/bin/Debug').rglob('*.dll'))+list((ROOT/'tests/Mist.Tests/bin/Debug/net10.0').glob('*.dll'))
    current=[{'path':rel(p),'sha256':sha(p)} for p in sorted(files)]
    require(recorded==current,'Actual sorted game/test binary set differs from this frozen run')
    require({'Mist.Core.dll','Mist.Application.dll','MistSea.dll','Mist.Tests.dll'}<={Path(r['path']).name for r in recorded},'Compiled artifact proof is incomplete')
    return info(path)

def fixture_receipt(key,action):
    return key.startswith(('fixture:','internal:')) or action in {'explicit_engineering_fixture','internal_slice_initialization'}

def sqlite_normal_proof(case_dir,required_keys):
    candidates=[];databases=set(case_dir.rglob('profile.sqlite'))
    # Recovery may legitimately point at a newer managed generation. Never assume
    # an obsolete profile.sqlite is the current normal result after save/load.
    for control in case_dir.rglob('control.sqlite'):
        try:
            with closing(sqlite3.connect(readonly_uri(control),uri=True)) as db:
                for row in db.execute('SELECT current_path FROM recovery_pointers'):
                    p=(control.parent/row[0]).resolve()
                    if p.is_relative_to(control.parent.resolve()) and p.is_file():databases.add(p)
        except sqlite3.DatabaseError:continue
    for p in sorted(databases):
        try:
            with closing(sqlite3.connect(readonly_uri(p),uri=True)) as db:
                receipts=db.execute('SELECT branch_id,semantic_key,action_type,receipt_id,commit_sequence FROM receipts').fetchall()
                by_branch={}
                for row in receipts:by_branch.setdefault(row[0],[]).append(row)
                for branch,rows in by_branch.items():
                    keys={r[1] for r in rows}
                    if not set(required_keys)<=keys or any(fixture_receipt(r[1],r[2]) for r in rows):continue
                    candidates.append({'profile':info(p),'branch':branch,'receipt_count':len(rows),'positive_fixture_receipts':0,
                      'required_actual_receipts':[{'semantic_key':r[1],'action_type':r[2],'receipt_id':r[3],'sequence':r[4]} for r in rows if r[1] in required_keys]})
        except sqlite3.DatabaseError:continue
    require(candidates,'No actual normal SQLite branch with required producer receipts in '+rel(case_dir)+': '+str(required_keys))
    return candidates

def validate_full_suite(directory):
    suite=read(directory/'test_results.json');inventory=read(directory/'full_suite_inventory.json')['cases']
    require(suite.get('failed')==0 and suite.get('executed',0)>0,'Final suite must contain actual execution with zero failures')
    require(suite.get('scope')=='every registered C# case executed exactly once in isolated child profiles','Select final isolated full-suite report, not a scoped test run')
    require(len(inventory)==suite['executed']==len(suite['results']),'Full-suite count differs from compiled registry')
    require([c['index'] for c in inventory]==list(range(len(inventory))),'Compiled registry is incomplete or duplicated')
    actual={}
    for c,r in zip(inventory,suite['results']):
        require(r['task']==c['task'] and r['name']==c['name'] and r['result']=='passed','Result differs from exact compiled registry case')
        require((r['task'],r['name']) not in actual,'Duplicate named compiled test case')
        child=directory/'case_runs'/f"{c['index']:04d}";record=read(child/'test_results.json')
        require(record.get('executed')==1 and record.get('failed')==0 and record['results']==[r],'Missing actual isolated child result')
        command=(child/'command.log').read_text(encoding='utf-8-sig').splitlines()[0]
        observed=json.loads(command)
        require(observed.get('exit_code')==0 and observed.get('cwd')==str(ROOT),'Actual isolated case command failed or used another workspace')
        require(len(observed.get('argv',[]))>=3 and Path(observed['argv'][1]).resolve()==ROOT/'tests/Mist.Tests/bin/Debug/net10.0/Mist.Tests.dll' and observed['argv'][2:]==['--case-index='+str(c['index'])],
                'Actual isolated command does not execute this exact compiled registry case')
        actual[(r['task'],r['name'])]={'result':r,'index':c['index'],'report':info(child/'test_results.json'),
                                     'actual_command':info(child/'command.log'),'case_dir':rel(child)}
    commands(directory);validate_reuse(directory);validate_compiled_artifacts(directory)
    result=read(directory/'result.json')
    require(result.get('action')=='test' and result.get('result')=='verified','Full-suite build result is not verified')
    required=[
      ('Campaign/CampaignCases.cs','Normal campaign hands the same actor and unique ferry devices to the existing physical Q102 transaction',['Q-P01:resolution','Q-P02:resolution','Q-P03:resolution','Q-101:resolution']),
      ('ChapterOne/ChapterOneCases.cs','Normal zero-XP rescue, commission, ferry, finding and tower handover persist continuously without fixtures',['Q-104:resolution']),
      ('ChapterTwo/ChapterTwoCases.cs','Continuous actual normal zero-XP voyage reaches all four R2 tasks without positive fixtures',['Q-204:resolution']),
      ('ChapterThree/ChapterThreeCases.cs','Continuous normal zero-XP through actual R2-R3 voyage and all four mining tasks uses no positive fixture',['Q-304:resolution']),
      ('ChapterFour/ChapterFourCases.cs','Continuous normal zero-XP voyage reaches all R4 tasks with no positive fixtures or inferred current mentor status',['Q-404:resolution']),
      ('ChapterFive/ChapterFiveCases.cs','Actual zero-XP mainline completes R5 with one vessel, one original case, alive mentor and bounded R6 commission',['Q-504:resolution']),
      ('ChapterSix/ChapterSixCases.cs','Normal 0 XP through six onsite baseline producers seals real checkpoint and launches one vessel atomically to RF',['Q-604:resolution']),
    ]
    for style,choice in [('H','Central'),('M','Island'),('S','Distributed'),('Mixed','Central')]:required.append(('Finale/FinaleCases.cs',f'Actual zero-XP finale {style} keeps 28-level resources and closes {choice} once',['Q-F04:resolution']))
    if (ROOT/'content/source/optional_encounter_contracts/learner_package.json').exists():
        for region in REGIONS[1:-1]:required.append(('OptionalEncounterContracts/OptionalEncounterCases.cs',f'normal_zero_XP_{region}_both_independent_optional_contracts_actual_control_inspection_CAS_and_no_rewards',[f'optional-contract:ENC-{region}-O01:result',f'optional-contract:ENC-{region}-O02:result']))
    production=[]
    for source,name,keys in required:
        found=[a for (task,n),a in actual.items() if n==name];require(len(found)==1,'Required actual production case absent from final suite: '+name)
        a=found[0];production.append({'name':name,'test_source':info(ROOT/'tests/Mist.Tests'/source),**a,
          'sqlite_actual_result_proofs':sqlite_normal_proof(inside(a['case_dir']),keys),'language_input':'explicit engineering controller; not a human/free-English trial'})
    return suite,actual,production

def receipt_objects(value):
    if isinstance(value,dict):
        for k,v in value.items():
            if re.sub('_','',k).lower() in {'receipts','actualreceipts'}:
                require(isinstance(v,list) and all(isinstance(r,dict) for r in v),'Entry receipt history must be an array of actual receipt objects')
                yield from v
            else:yield from receipt_objects(v)
    elif isinstance(value,list):
        for v in value:yield from receipt_objects(v)

def normalize_receipt(r):
    fields={re.sub('_','',k).lower():v for k,v in r.items()}
    return str(fields.get('semantickey','')),str(fields.get('actiontype',''))

def validate_native_suite_source(source,entry,suite_context):
    """Validate a copy of one completed, isolated normal entry, without replaying it."""
    require(suite_context is not None,'Full-suite native source requires the selected final suite context')
    directory=inside(source['suite_dir'])
    require(directory==suite_context['directory'],'Native source is not the selected final full suite')
    require(source['suite_test_results_sha256']==sha(directory/'test_results.json'),'Native source suite summary changed')
    require(source['suite_inventory_sha256']==sha(directory/'full_suite_inventory.json'),'Native source compiled registry changed')
    selected=source['selected_case'];require(set(selected)>={'index','task','name'},'Native source lacks exact selected compiled case')
    actual=suite_context['actual'].get((selected['task'],selected['name']))
    require(actual is not None and actual['index']==selected['index'],'Native source task/name/index differs from the passed isolated case')
    child=inside(actual['case_dir']);testdata=child/'testdata'
    require(entry.is_relative_to(testdata),'Native entry proof belongs to another case profile')
    matches=list(testdata.rglob(entry.name))
    require(matches==[entry],'Selected isolated case must contain exactly this one untouched entry proof')
    require(source['source_sha256']==sha(entry),'Copied normal-entry source proof changed')
    profile_root=entry.parent
    while not (profile_root/'control.sqlite').is_file():
        require(profile_root!=testdata,'Normal entry has no matching actual profile control database')
        profile_root=profile_root.parent
    require(not profile_root.is_symlink() and all(not p.is_symlink() for p in profile_root.rglob('*')),'Native entry includes a symbolic link')
    with closing(sqlite3.connect(readonly_uri(profile_root/'control.sqlite'),uri=True)) as control:
        pointers=control.execute('SELECT current_path FROM recovery_pointers').fetchall()
    require(len(pointers)==1,'Native entry has ambiguous active world database pointers')
    active=(profile_root/pointers[0][0]).resolve()
    require(active.is_relative_to(profile_root.resolve()) and active.is_file(),'Normal entry active database pointer is unsafe or missing')
    require(source['source_active_database']==rel(active) and source['source_active_database_sha256']==sha(active),'Native source active-generation fingerprint differs')
    with closing(sqlite3.connect(readonly_uri(active),uri=True)) as db:
        require(db.execute('PRAGMA integrity_check').fetchone()[0]=='ok','Native source active database integrity failed')
        columns=[r[1] for r in db.execute('PRAGMA table_info(receipts)')]
        require({'branch_id','semantic_key','action_type','receipt_id','commit_sequence','effect_hash','result_json'}<=set(columns),
                'Native active receipt schema lacks actual command provenance')
        receipts=[dict(zip(columns,r)) for r in db.execute('SELECT * FROM receipts ORDER BY branch_id,commit_sequence,semantic_key,receipt_id')]
        branch=db.execute("SELECT value FROM metadata WHERE key='active_branch'").fetchone()
    require(branch and isinstance(branch[0],str) and branch[0],'Native active database lacks its current branch')
    require(receipts and all(isinstance(r['semantic_key'],str) and r['semantic_key'] and isinstance(r['action_type'],str) and r['action_type']
                and isinstance(r['receipt_id'],str) and r['receipt_id'] and isinstance(r['branch_id'],str) and r['branch_id']
                and isinstance(r['commit_sequence'],int) and r['commit_sequence']>0
                and isinstance(r['effect_hash'],str) and r['effect_hash'] and isinstance(r['result_json'],str)
                and not fixture_receipt(r['semantic_key'],r['action_type']) for r in receipts),
            'Native active source contains fixture or malformed actual receipt history')
    current_receipts=[r for r in receipts if r['branch_id']==branch[0]]
    require(current_receipts,'Native active branch has no actual receipt history')
    created=[r for r in current_receipts if r['action_type']=='normal_campaign_created']
    require(len(created)==1 and created[0]['semantic_key'].startswith('campaign:new:'),
            'Native active source must have one actual normal campaign creation on the current branch')
    creation_result=json.loads(created[0]['result_json'])
    require(isinstance(creation_result,dict) and type(creation_result.get('experience_awarded')) is int and creation_result['experience_awarded']==0,
            'Native actual normal campaign creation must award zero XP')
    # Some entry files deliberately contain only sequence/XP/scope.  They are
    # summaries of this exact untouched database, never substitute receipt logs.
    entry_receipts=list(receipt_objects(read(entry)))
    actual_by_key={r['semantic_key']:r for r in current_receipts}
    require(len(actual_by_key)==len(current_receipts),'Duplicate actual semantic keys on the native active branch')
    for receipt in entry_receipts:
        key,action=normalize_receipt(receipt)
        require(key and action and not fixture_receipt(key,action),'Native entry includes fixture or malformed receipts')
        actual_receipt=actual_by_key.get(key)
        require(actual_receipt is not None and actual_receipt['action_type']==action,'Native entry receipt differs from the actual current database')
        fields={re.sub('_','',k).lower():v for k,v in receipt.items()}
        for field,column in [('receiptid','receipt_id'),('branchid','branch_id'),('sequence','commit_sequence'),
                             ('commitsequence','commit_sequence'),('effecthash','effect_hash'),('resultjson','result_json')]:
            if field in fields:require(fields[field]==actual_receipt[column],'Native entry receipt '+field+' differs from the actual current database')
    history={'source':'validated control current_path active SQLite receipts table','active_branch':branch[0],
      'actual_receipt_count':len(receipts),'active_branch_receipt_count':len(current_receipts),'positive_fixture_receipts':0,
      'receipt_columns':columns,'ordered_receipts_sha256':hashlib.sha256(json.dumps(receipts,ensure_ascii=False,sort_keys=True,separators=(',',':')).encode('utf-8')).hexdigest(),
      'normal_campaign_creation':{k:created[0][k] for k in ['branch_id','semantic_key','action_type','receipt_id','commit_sequence']},
      'normal_creation_experience_awarded':creation_result['experience_awarded'],
      'entry_json_receipt_count':len(entry_receipts),'entry_json_role':'receipt history cross-checked against active SQLite' if entry_receipts else 'summary only; no receipt history claimed'}
    require(source['suite_inputs_sha256']==sha(directory/'build_inputs.json') and source['compiled_artifacts_sha256']==sha(directory/'compiled_artifacts.json'),'Native suite input/binary manifests changed')
    require(source['case_results_sha256']==sha(child/'test_results.json') and source['case_command_sha256']==sha(child/'command.log'),'Selected actual case evidence changed')
    compiled=validate_compiled_artifacts(directory)
    files=source.get('copied_files',[]);require(files,'Native suite reuse must record actual before-launch copied file fingerprints')
    seen=set();proof=[]
    for record in files:
        original=inside(record['source']);require(original.is_relative_to(profile_root),'Copied file escaped the selected normal entry profile')
        require(rel(original) not in seen,'Duplicate copied-source file record');seen.add(rel(original))
        require(record['sha256']==sha(original),'Untouched suite profile/source bytes changed after copy')
        require(record['copied_before_native_sha256']==record['sha256'],'Before-native copied bytes differ from the actual suite source')
        # Native gameplay legitimately modifies its live copy.  If a permanent
        # pre-launch copy was retained, independently recheck those actual bytes.
        retained=record.get('frozen_copy')
        if retained:require(sha(retained)==record['sha256'],'Retained pre-native copy fingerprint differs')
        proof.append({**info(original),'copied_before_native_sha256':record['copied_before_native_sha256'],
                      'frozen_copy':info(retained) if retained else None})
    require({rel(active),rel(profile_root/'control.sqlite')}<=seen,'Both actual active world and control databases must be copied')
    require(seen=={rel(p) for p in profile_root.rglob('*') if p.is_file()},'Copied-file inventory differs from the complete actual entry profile')
    return {'source_mode':'final_full_suite_case_copy','selected_case':selected,
      'suite':info(directory/'test_results.json'),'registry':info(directory/'full_suite_inventory.json'),
      'exact_isolated_case':actual['report'],'actual_case_command':actual['actual_command'],'suite_current_inputs':suite_context['inputs'],
      'compiled_artifacts':compiled,'active_world_database':info(active),'actual_receipt_history':history,'copied_files':proof,
      'scope':'Actual earlier producer commands executed once in the final suite; native uses their untouched entry copy. No fixture or second normal-route execution is inferred.'}


def validate_native(path,now,native_input_index,suite_context):
    data=read(path);require(data.get('failures')==0 and data.get('checks'),'Native candidate has no genuine passing checks')
    require(all(c.get('passed') is True for c in data['checks']),'Native candidate contains failed/pending checks')
    directory=path.parent;actual_commands=commands(directory);run=read(directory/'result.json')
    require(run.get('result')=='verified' and isinstance(run.get('action'),str) and run['action'].endswith('client-test'),
            'Native build wrapper did not verify this actual client probe')
    action=run['action'];filename={'r3-shift-client-test':'r3_shift_work_client_results.json'}.get(action,action.replace('-','_').removesuffix('_test')+'_results.json')
    require(path.name==filename,'Native result filename differs from its actual verified action')
    require(any(c.get('exit_code')==0 and c.get('cwd')==str(ROOT) and '--headless' in c['argv'] and '--path' in c['argv']
                and str(ROOT/'game') in c['argv'] and c['argv'][-2:]==['--','--'+action] for c in actual_commands),
            'No actual successful Godot command executed this exact native probe')
    reuse=validate_reuse(directory)
    compiled=validate_compiled_artifacts(directory) if (directory/'compiled_artifacts.json').exists() else None
    if reuse is None:validate_compilation(directory,now)
    # Binary equality does not establish which dynamic JSON/WAV inputs a reused native
    # run read. Require its real input capture too; older runs remain historical only.
    if (directory/'run_inputs.json').exists():
        runtime_inputs=records_map(read(directory/'run_inputs.json')['inputs'])
        runtime_capture=info(directory/'run_inputs.json')
    elif (directory/'build_inputs.json').exists():
        runtime_inputs=records_map(read(directory/'build_inputs.json')['inputs'])
        runtime_capture=info(directory/'build_inputs.json')
    else:
        selected=native_input_index.get(rel(path))
        require(selected is not None and selected.get('result_sha256')==sha(path),'Native reuse lacks exact runtime/source input capture tied to this result')
        runtime_inputs=records_map(selected['inputs'])
        runtime_capture={'external_result_sha256':selected['result_sha256'],'input_count':len(runtime_inputs)}
    require(runtime_inputs==now,'Native input capture differs from the current complete input set')
    profile_source=directory/'client_profile_source.json'
    route=None
    if profile_source.exists():
        source=read(profile_source);require(source.get('fixture') is False,'Native profile is a positive fixture')
        require(source.get('source_mode') in {None,'final_full_suite_case_copy'},'Native profile uses an unsupported source proof mode')
        entry=inside(source['source'])
        route={'profile_source':info(profile_source),'actual_entry':info(entry),'positive_fixture_receipts':0}
        if source.get('source_mode')=='final_full_suite_case_copy':
            copy_proof=validate_native_suite_source(source,entry,suite_context)
            route['full_suite_copy_proof']=copy_proof
            route['actual_receipt_count']=copy_proof['actual_receipt_history']['actual_receipt_count']
            route['receipt_history_source']='validated actual active SQLite, not entry JSON summary'
        else:
            receipts=list(receipt_objects(read(entry)))
            require(receipts,'Native normal-entry proof lacks actual receipt history')
            require(all(all(normalize_receipt(r)) and not fixture_receipt(*normalize_receipt(r)) for r in receipts),'Native entry includes fixture or malformed receipts')
            route['actual_receipt_count']=len(receipts)
            route['receipt_history_source']='original entry JSON receipt history; no full-suite database substitution'
    else:
        # The older fresh-new-game campaign probe only tests editor/UI boundaries.
        # It is useful native evidence but has no complete chapter normal-result claim.
        require(path.name=='campaign_client_results.json','Native run missing actual source-profile proof')
        route={'normal_completion_claim':False,'scope':'fresh campaign native UI/editor boundary probe; no chapter completion proof'}
    return {'result':info(path),'checks':data['checks'],'route':route,'compilation_reuse':reuse,'runtime_input_capture':runtime_capture,
      'actual_commands_manifest':info(directory/'commands.json'),'compiled_artifacts':compiled,
      'scope':data.get('source',data.get('scope','actual native control checks')),'human_native_play':'not_run','language_calibration':'human_pending'}

def select_natives(root,explicit,now,native_input_index,suite_context):
    paths=list(root.glob('*/*client_results.json')) if root is not None else []
    paths.extend(inside(p) for p in explicit)
    accepted={};rejected=[]
    for path in sorted(set(paths),key=lambda p:p.stat().st_mtime,reverse=True):
        if path.name in accepted:continue
        try:accepted[path.name]=validate_native(path,now,native_input_index,suite_context)
        except (EvidenceError,KeyError,ValueError,OSError,sqlite3.DatabaseError) as e:rejected.append({'path':rel(path),'reason':str(e)})
    return accepted,rejected

def package_manifest(path):
    path=inside(path);entries=[]
    banned=lambda n:any('author_only' in part or part in {'reference','planning','reports','testdata'} for part in Path(n).parts) or n.endswith(('.sqlite','.sqlite-wal','.sqlite-shm'))
    if path.is_dir():
        for p in sorted(path.rglob('*')):
            if p.is_file():
                n=p.relative_to(path).as_posix();require(not banned(n),'Player package includes author/private/test input: '+n);entries.append({'path':n,'sha256':sha(p),'bytes':p.stat().st_size})
        artifact={'path':rel(path),'kind':'directory_candidate','file_count':len(entries)}
    else:
        require(path.suffix.lower()=='.zip','Package must be actual exported candidate directory or ZIP')
        with zipfile.ZipFile(path) as archive:
            names=set()
            for member in archive.infolist():
                if member.is_dir():continue
                n=member.filename;require(not n.startswith(('/','\\')) and '..' not in Path(n).parts and ':' not in n,'Unsafe package member')
                require(n not in names,'Duplicate package member: '+n);names.add(n);require(not banned(n),'Player package includes author/private/test input: '+n)
                body=archive.read(member);entries.append({'path':n,'sha256':hashlib.sha256(body).hexdigest(),'bytes':len(body)})
        artifact={**info(path),'kind':'zip_candidate'}
    require(entries,'Package candidate is empty')
    require(any(Path(e['path']).suffix.lower()=='.exe' and e['bytes']>100000 for e in entries),'Candidate package has no actual Windows executable')
    return {'artifact':artifact,'files':entries,'formal_release_approval':False,'audio_rights_human_review':'pending','clean_machine':'not_inferred'}

def mapping_and_plan(path):
    mapping=read(path);plan=read(ROOT/'planning/plan.json');tasks={t['id']:t for t in plan['tasks']}
    require(mapping.get('plan_sha256')==sha(ROOT/'planning/plan.json'),'Author mapping belongs to a different immutable plan')
    require(len(mapping['tasks'])==40 and {t['id'] for t in mapping['tasks']}==TASK_IDS,'Mapping must cover exactly 40 CP01..05 tasks')
    total=0
    for mapped in mapping['tasks']:
        task=tasks[mapped['id']];criteria=mapped['criteria'];total+=len(criteria)
        require(len(criteria)==len(task['acceptance']),'Acceptance item count changed: '+task['id'])
        require([c['index'] for c in criteria]==list(range(len(criteria))),'Criterion index missing/duplicated')
        require([c['criterion'] for c in criteria]==task['acceptance'],'Original criterion text differs: '+task['id'])
        require(task['kind'] in {'content_authoring','content_production','chapter_integration','content_integration','chapter_qa'},'Not an allowed partial content task')
    require(total==214,'Expected exact 214 original acceptance criteria, got '+str(total))
    return mapping,tasks

def criterion_evidence(c,mapping,actual,natives):
    sources=[];missing=[]
    for relative in sorted(set(c['source_refs'])):
        if (ROOT/relative).is_file():
            entry=info(relative);old=mapping['source_registry'].get(relative,{})
            entry['author_mapping_sha256']=old.get('sha256');entry['unchanged_since_author_mapping']=old.get('sha256')==entry['sha256'];sources.append(entry)
        else:missing.append(relative)
    matched=[]
    for reference in c['engineering_evidence_refs']:
        old=mapping['evidence_registry'].get(reference,{})
        a=actual.get((old.get('task'),old.get('name')))
        if a:matched.append({k:v for k,v in a.items() if k!='case_dir'})
    native=[]
    for reference in [c.get('native_evidence_ref'),*c.get('optional_native_evidence_refs',[])]:
        if reference and Path(reference.split('#')[0]).name in natives:native.append(natives[Path(reference.split('#')[0]).name])
    return {'status':'pending','criterion':c['criterion'],'index':c['index'],
      'evidence':{'source_snapshots':sources,'missing_sources':missing,'actual_current_named_tests':matched,'actual_current_native':native,
        'author_implementation_statement':c['current_implementation_support'],'statement_scope':'retained author-review claim; no automatic semantic confirmation',
        'mapping_support_status':c['support_status']},
      'note':c['remaining_or_unrun']+' No full-suite pass alone clears this individual criterion. Full source/canon/language/experience judgement and any unimplemented goal remain explicit.'}

def preflight(args):
    suite_dir=inside(args.suite_dir);compiled_dir=inside(args.compiled_build_dir);mapping,tasks=mapping_and_plan(inside(args.mapping))
    inputs=current_inputs();now={x['path']:x['sha256'] for x in inputs};compilation=validate_compilation(compiled_dir,now)
    suite,actual,production=validate_full_suite(suite_dir)
    # A final suite run must record the exact current complete inputs, including newly compiled player content.
    suite_inputs=suite_dir/'run_inputs.json'
    if not suite_inputs.exists():suite_inputs=suite_dir/'build_inputs.json'
    if not suite_inputs.exists():
        require(args.suite_inputs,'Reused full suite requires --suite-inputs from its actual frozen run')
        suite_inputs=inside(args.suite_inputs);value=read(suite_inputs)
        require(value.get('test_results_sha256')==sha(suite_dir/'test_results.json'),'External suite input proof is not tied to these exact results')
    frozen=records_map(read(suite_inputs)['inputs']);require(frozen==now,'Full suite frozen inputs differ from the current complete runtime/source/player input set')
    native_index={}
    if args.native_inputs_index:
        entries=read(args.native_inputs_index)['runs']
        native_index={e['result_path']:e for e in entries}
        require(len(native_index)==len(entries),'Duplicate native runtime input capture')
    suite_context={'directory':suite_dir,'actual':actual,'inputs':info(suite_inputs)}
    natives,rejected=select_natives(inside(args.native_root) if args.native_root else None,args.native,now,native_index,suite_context)
    package=package_manifest(inside(args.package));state=read(ROOT/'planning/state/execution.json');prepared=[];skipped=[]
    sys.path.insert(0,str(ROOT/'tools'))
    from plan_lib import schedule
    require(not schedule(ROOT,read(ROOT/'planning/plan.json'),state)['errors'],'Existing task schedule/evidence errors must be resolved before report writes')
    goals=read(args.goal_inventory)
    require(goals.get('scope','').startswith('author-only semantic'),'Goal inventory must be an author-only source/producer map')
    regional_sources={r:set() for r in REGIONS}
    for g in goals['main_goals']+goals['optional_goals']:
        records=[g['public_source'],*g.get('supplementary_public_sources',[]),*g['production_files'],*g['named_cases']]
        for record in records:
            p=ROOT/record['path']
            if not p.exists() and p.name.endswith('.pending'):p=Path(str(p)[:-8])
            if p.is_file():regional_sources[g['region']].add(rel(p))
    for mapped in mapping['tasks']:
        task=tasks[mapped['id']];old_report=ROOT/task['evidence_path'];old=read(old_report) if old_report.is_file() else {}
        if state['tasks'][task['id']]['status']=='accepted' or old.get('result')=='accepted':skipped.append(task['id']);continue
        acceptance=[criterion_evidence(c,mapping,actual,natives) for c in mapped['criteria']]
        region=task['id'].split('-')[1]
        prepared.append({'task_id':task['id'],'acceptance':acceptance,'source_refs':sorted(set(p for c in mapped['criteria'] for p in c['source_refs'] if (ROOT/p).is_file())|regional_sources[region]|{rel(inside(args.goal_inventory))}),
          'supplemental_current_goal_sources':[info(p) for p in sorted(regional_sources[region])],
          'official_evidence_path':task['evidence_path'],'result':'implemented','human_signoff':None,'content_candidate':True})
    return {'schema_version':1,'generated_utc':datetime.now(timezone.utc).isoformat(),'scope':'40 chapter content candidates; exact 214 original criteria, no human gate',
      'suite':info(suite_dir/'test_results.json'),'suite_registry':info(suite_dir/'full_suite_inventory.json'),'suite_inputs':info(suite_inputs),
      'compilation':compilation,'current_inputs':inputs,'required_actual_production':production,'selected_latest_current_native':natives,'rejected_native_candidates':rejected,
      'package':package,'mapping':info(args.mapping),'plan':info('planning/plan.json'),'goal_inventory':info(args.goal_inventory),
      'goal_inventory_limit':'64 main author semantic groups and full optional inventory are proposals with merge cautions; they do not certify 96 independent units',
      'prepared_tasks':prepared,'accepted_historical_tasks_skipped':skipped,
      'not_run':['Human canon/story/language 6.5/experience acceptance; no CP06/HV signatures','Audio audition/alignment/voice redistribution rights','Physical human IME/accessibility, clean second machine, physical power loss',
        '96 semantic independent puzzle completion is not inferred from 9 references, 32 task families, 44 encounter slots or all-suite success','Formal release approval'],
      'criterion_status_policy':'All 214 retain pending full acceptance with concrete observed engineering/source support; not an all-passed gate','execution_json_written_by_this_script':False}

def recheck_artifact_records(value):
    if isinstance(value,dict):
        if 'path' in value and 'sha256' in value:
            require(sha(value['path'])==value['sha256'],'Inspected actual evidence changed after preflight: '+value['path'])
        for child in value.values():recheck_artifact_records(child)
    elif isinstance(value,list):
        for child in value:recheck_artifact_records(child)


def write_reports(args,audit):
    # Re-read all inspected inputs and artifacts; no report/state mutation precedes this check.
    require(records_map(audit['current_inputs'])=={x['path']:x['sha256'] for x in current_inputs()},'Inputs changed after preflight')
    for record in [audit['suite'],audit['suite_registry'],audit['suite_inputs'],audit['mapping'],audit['plan'],audit['goal_inventory']]:require(sha(record['path'])==record['sha256'],'Evidence changed after preflight')
    require(package_manifest(inside(args.package))==audit['package'],'Player package changed after preflight')
    for task in audit['prepared_tasks']:
        for criterion in task['acceptance']:
            for record in criterion['evidence']['source_snapshots']:require(sha(record['path'])==record['sha256'],'Criterion source changed after preflight')
        for record in task['supplemental_current_goal_sources']:require(sha(record['path'])==record['sha256'],'Goal producer/source changed after preflight')
    recheck_artifact_records(audit['compilation'])
    recheck_artifact_records(audit['required_actual_production'])
    for native in audit['selected_latest_current_native'].values():
        recheck_artifact_records(native)
        validate_reuse(inside(native['result']['path']).parent)
    digest=hashlib.sha256(json.dumps({k:audit[k] for k in ['suite','suite_inputs','package','mapping','current_inputs','selected_latest_current_native','prepared_tasks']},sort_keys=True).encode()).hexdigest()[:12]
    stage=ROOT/'reports/continuous/cps'/digest;stage.mkdir(parents=True,exist_ok=True)
    frozen_audit=stage/'audit.json';frozen_audit.write_text(json.dumps(audit,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    pool={};origins=[]
    required={p for task in audit['prepared_tasks'] for p in task['source_refs']}
    required.update({rel(Path(__file__)),audit['mapping']['path'],audit['suite']['path'],audit['suite_registry']['path'],audit['suite_inputs']['path']})
    for number,origin in enumerate(sorted(required),1):
        source=inside(origin);suffix=source.suffix if len(source.suffix)<=12 else '.dat';dest=stage/f'f{number:04d}{suffix}'
        require(len(str(ROOT/'reports/execution/CP-RF-05/evidence/20261010T000000000000Z/sources'/dest.relative_to(ROOT)))<240,'Task snapshot would exceed conservative Windows path limit')
        shutil.copyfile(source,dest);pool[origin]=rel(dest);origins.append({**info(source),'short_snapshot_alias':rel(dest),'alias_sha256':sha(dest)})
    manifest=stage/'origins.json';manifest.write_text(json.dumps({'scope':'exact original bytes copied to short task_report source aliases','files':origins},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    # Existing task_report.finish owns state/report writes and its actual per-task Git/source snapshot.
    sys.path.insert(0,str(ROOT/'reports/continuous'))
    from task_report import finish
    actual_commands=commands(inside(args.suite_dir));outputs=[]
    for task in audit['prepared_tasks']:
        state=read(ROOT/'planning/state/execution.json')
        if state['tasks'][task['task_id']]['status']=='accepted':continue
        existing=ROOT/task['official_evidence_path']
        if existing.exists():
            old=read(existing);manifest_path=old.get('working_tree_manifest')
            if manifest_path and (ROOT/manifest_path).is_file():
                old_manifest=read(manifest_path)
                if old_manifest.get('interface_schema_impact',{}).get('candidate_invocation_fingerprint')==digest:
                    for artifact in old.get('artifacts',[]):require(sha(artifact['path'])==artifact['sha256'],'Prior identical partial report artifact changed')
                    outputs.append({'task_id':task['task_id'],'result':old['result'],'evidence':task['official_evidence_path'],'identical_candidate_preserved':True});continue
        sources=[pool[p] for p in task['source_refs']]+[pool[rel(Path(__file__))],pool[audit['mapping']['path']],rel(manifest),rel(frozen_audit)]
        report=finish(task['task_id'],sources,actual_commands,task['acceptance'],audit['not_run'],
          {'scope':'actual final full-suite and required normal producer proofs; individual criterion acceptance remains pending','executed':read(audit['suite']['path'])['executed'],
           'failed':0,'sqlite_version':read(audit['suite']['path']).get('sqlite_version'),'suite':audit['suite'],'production':audit['required_actual_production'],
           'native':audit['selected_latest_current_native'],'package':audit['package']['artifact'],'human_signoff':None},
          changes={'data_migration_needed':False,'short_source_snapshot_origin_manifest':rel(manifest),'source_aliases_preserve_original_sha256':True,
             'candidate_invocation_fingerprint':digest,'no_new_acceptance_or_human_signature':True},partial=True,content_candidate=True)
        outputs.append({'task_id':task['task_id'],'result':report['result'],'evidence':task['official_evidence_path']})
    return outputs

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--suite-dir',required=True);p.add_argument('--compiled-build-dir',required=True)
    p.add_argument('--suite-inputs');p.add_argument('--package',required=True);p.add_argument('--native-root');p.add_argument('--native',action='append',default=[])
    p.add_argument('--native-inputs-index',help='Actual reused-run input captures: {runs:[{result_path,result_sha256,inputs:[{path,sha256,bytes}]}]}')
    p.add_argument('--goal-inventory',default='reports/continuous/semantic_goal_inventory_20261010.author_review.json')
    p.add_argument('--mapping',default='reports/continuous/chapter_acceptance_mapping.author_review.json');p.add_argument('--output',default='reports/continuous/chapter_final_preflight.json')
    p.add_argument('--write-reports',action='store_true');a=p.parse_args()
    try:
        audit=preflight(a)
        if a.write_reports:audit['task_report_api_results']=write_reports(a,audit)
        output=inside(a.output,ROOT/'reports/continuous');output.parent.mkdir(parents=True,exist_ok=True)
        output.write_text(json.dumps(audit,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
        print(json.dumps({'preflight':rel(output),'prepared':len(audit['prepared_tasks']),'criteria':sum(len(t['acceptance']) for t in audit['prepared_tasks']),
          'accepted_history_skipped':audit['accepted_historical_tasks_skipped'],'reports_written':a.write_reports,'human_signoff':None},ensure_ascii=False));return 0
    except (EvidenceError,ValueError,KeyError,OSError,sqlite3.DatabaseError) as e:
        print(json.dumps({'result':'blocked_before_acceptance','error':str(e),'human_signoff':None},ensure_ascii=False),file=sys.stderr);return 2

if __name__=='__main__':sys.exit(main())

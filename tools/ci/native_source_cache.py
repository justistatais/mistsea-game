"""Copy an untouched actual-route entry produced by the exact current full suite."""
import hashlib,json,shutil,sqlite3
from contextlib import closing
from pathlib import Path
from sqlite_evidence import readonly_uri
ROOT=Path(__file__).resolve().parents[2]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def copy_current_suite_entry(suite_path,out,task,marker,proof_name,inputs,binaries):
 suite=Path(suite_path).resolve();assert suite.is_relative_to((ROOT/'reports/build').resolve())
 def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
 assert read(suite/'build_inputs.json')['inputs']==inputs,'Native source suite inputs differ from current inputs.'
 assert read(suite/'compiled_artifacts.json')['binaries']==binaries,'Native source suite compilation differs from current binaries.'
 commands=read(suite/'commands.json')
 assert not (suite/'reused_compilation.json').exists() and any('build' in c['argv'] and c['exit_code']==0 for c in commands),'Source full suite must actually compile current inputs.'
 assert any(c['argv'][1].endswith('full_suite.py') and c['exit_code']==0 for c in commands),'Source must be an unfiltered full suite.'
 assert all(c['exit_code']==0 or c.get('expected_nonzero_proven') for c in commands)
 assert read(suite/'result.json')['action']=='test' and read(suite/'result.json')['result']=='verified'
 results=read(suite/'test_results.json');inventory=read(suite/'full_suite_inventory.json')['cases']
 assert results['executed']==len(inventory)>0 and results['failed']==0
 assert [c['index'] for c in inventory]==list(range(len(inventory)))
 assert [(c['task'],c['name'],'passed') for c in inventory]==[(c['task'],c['name'],c['result']) for c in results['results']]
 for b in binaries:
  current=ROOT/b['path'];assert current.is_file() and sha(current)==b['sha256']
 eligible=[c for c in inventory if c['task']==task and (marker is None or marker in c['name'])]
 if marker is not None:assert len(eligible)==1,'Actual source marker is not unique in the full suite.'
 matches=[]
 for c in eligible:
  child=suite/'case_runs'/f"{c['index']:04d}"
  result=read(child/'test_results.json');assert result['executed']==1 and result['failed']==0
  assert result['results'][0]['task']==c['task'] and result['results'][0]['name']==c['name']
  assert result['results'][0]['result']=='passed'
  actual=json.loads((child/'command.log').read_text(encoding='utf8').splitlines()[0])
  assert actual['exit_code']==0 and actual['cwd']==str(ROOT) and actual['argv'][1]==str(ROOT/'tests/Mist.Tests/bin/Debug/net10.0/Mist.Tests.dll') and actual['argv'][2:]==['--case-index='+str(c['index'])]
  for proof in (child/'testdata').rglob(proof_name):matches.append((c,child,proof,result))
 assert len(matches)==1,'Untouched actual normal entry proof must be unique.'
 case,child,proof,result=matches[0];profile=proof.parent
 while not (profile/'control.sqlite').is_file():
  profile=profile.parent;assert profile.is_relative_to(child/'testdata'),'Actual entry has no profile control SQLite.'
 assert not profile.is_symlink() and all(not p.is_symlink() for p in profile.rglob('*'))
 with closing(sqlite3.connect(readonly_uri(profile/'control.sqlite'),uri=True)) as control:
  pointers=control.execute('SELECT current_path FROM recovery_pointers').fetchall()
 assert len(pointers)==1
 active=(profile/pointers[0][0]).resolve();assert active.is_relative_to(profile.resolve()) and active.is_file()
 with closing(sqlite3.connect(readonly_uri(active),uri=True)) as db:
  assert db.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
  columns=[r[1] for r in db.execute('PRAGMA table_info(receipts)')]
  assert {'branch_id','semantic_key','action_type','receipt_id','commit_sequence','effect_hash','result_json'}<=set(columns),'Actual receipt schema does not expose provenance.'
  branch=db.execute("SELECT value FROM metadata WHERE key='active_branch'").fetchone();assert branch and branch[0],'Missing actual active branch.'
  rows=[dict(zip(columns,r)) for r in db.execute('SELECT * FROM receipts')]
  assert rows and all(not str(r.get('semantic_key','')).startswith(('fixture:','internal:')) and r.get('action_type') not in {'explicit_engineering_fixture','internal_slice_initialization'} for r in rows),'Native input has fixture history.'
  created=[r for r in rows if r['branch_id']==branch[0] and r['action_type']=='normal_campaign_created'];assert len(created)==1 and created[0]['semantic_key'].startswith('campaign:new:'),'Native input must start with one actual normal campaign creation.'
  xp=json.loads(created[0]['result_json'])['experience_awarded'];assert type(xp) is int and xp==0,'Actual campaign creation must award zero XP.'
 shutil.copytree(profile,out/'client_profile')
 assert all(sha(p)==sha(out/'client_profile'/p.relative_to(profile)) for p in profile.rglob('*') if p.is_file()),'Profile copy changed actual source bytes.'
 (out/'test_results.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
 audit={'scope':'copied untouched actual normal-route profile from exact current fresh unfiltered full-suite case; no fixture or seeded entry',
        'source_mode':'final_full_suite_case_copy','suite_dir':suite.relative_to(ROOT).as_posix(),'selected_case':case,
        'suite_test_results_sha256':sha(suite/'test_results.json'),'suite_inventory_sha256':sha(suite/'full_suite_inventory.json'),
        'source_sha256':sha(proof),
        'source_active_database':active.relative_to(ROOT).as_posix(),'source_active_database_sha256':sha(active),'fixture_receipts':0,
        'suite':suite.relative_to(ROOT).as_posix(),'source':proof.relative_to(ROOT).as_posix(),'fixture':False,'case':case,
        'suite_results_sha256':sha(suite/'test_results.json'),'suite_inputs_sha256':sha(suite/'build_inputs.json'),
        'compiled_artifacts_sha256':sha(suite/'compiled_artifacts.json'),'case_results_sha256':sha(child/'test_results.json'),
        'case_command_sha256':sha(child/'command.log'),'proof_sha256':sha(proof),
        'copied_profile_files':[{'path':p.relative_to(profile).as_posix(),'sha256':sha(p)} for p in sorted(profile.rglob('*')) if p.is_file()],
        'copied_files':[{'source':p.relative_to(ROOT).as_posix(),'sha256':sha(p),'copied_before_native_sha256':sha(out/'client_profile'/p.relative_to(profile))} for p in sorted(profile.rglob('*')) if p.is_file()]}
 (out/'client_profile_source.json').write_text(json.dumps(audit,ensure_ascii=False,indent=2),encoding='utf8')
 return proof

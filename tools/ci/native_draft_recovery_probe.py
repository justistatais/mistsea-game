"""Launch only a disposable actual route copy; hold a real SQLite writer lock for native UX checks."""
from datetime import datetime,timezone
import argparse,hashlib,json,os,shutil,sqlite3,subprocess,time
from build_inputs import current_inputs
from sqlite_evidence import readonly_uri
from pathlib import Path
R=Path(__file__).resolve().parents[2]
def write(p,data):p.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
def bounded(path):
 p=Path(path).resolve();base=(R/'reports').resolve();assert p.is_relative_to(base),'Only disposable report profiles are permitted';return p
def main():
 parser=argparse.ArgumentParser();parser.add_argument('action',choices=['launch','reopen','lock','release','audit']);parser.add_argument('path',nargs='?');parser.add_argument('--package',action='store_true');args=parser.parse_args()
 if args.action=='launch':
  source=bounded(args.path);assert (source/'entry_evidence.json').is_file() and (source/'control.sqlite').is_file()
  proof=json.loads((source/'entry_evidence.json').read_text(encoding='utf8'));assert proof['fixture_history'] is False and proof['experience']==340
  out=R/'reports/execution/CX-076'/('native-ux-'+datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ'));out.mkdir(parents=True);shutil.copytree(source,out/'profile')
  write(out/'run_inputs.json',{'scope':'actual current source and bundled player inputs before owned native draft recovery probe','inputs':current_inputs()})
  installed=json.loads((R/'toolchain/installed.windows-x64.json').read_text(encoding='utf8'));env=os.environ.copy();env['MISTSEA_PROFILE_ROOT']=str(out/'profile')
  if args.package:
   package=json.loads((R/'artifacts/windows/latest.json').read_text(encoding='utf8'));assert package['result']=='internal_candidate_verified';exe=R/package['package']/'MistSea-internal.exe';argv=[str(exe)]
  else:
   env.update(DOTNET_ROOT=installed['dotnet_root'],DOTNET_ROOT_X64=installed['dotnet_root']);env['PATH']=installed['dotnet_root']+os.pathsep+env.get('PATH','');argv=[installed['godot'],'--path',str(R/'game')]
  process=subprocess.Popen(argv,cwd=R,env=env,stdout=(out/'native_stdout.log').open('wb'),stderr=(out/'native_stderr.log').open('wb'),creationflags=subprocess.CREATE_NO_WINDOW)
  record={'pid':process.pid,'argv':argv,'executable_sha256':hashlib.sha256(Path(argv[0]).read_bytes()).hexdigest(),'profile':str(out/'profile'),'actual_source':str(source),'source_proof':proof,'executable_kind':'exported_release' if args.package else 'debug_editor_runtime','scope':'actual native draft save failure/export/retry on a disposable real 340XP entry; no human IME or physical power-loss claim','human_signoff':None};write(out/'process.json',record);print(json.dumps({'report':str(out),'pid':process.pid},ensure_ascii=False),flush=True);return
 out=bounded(args.path);record=json.loads((out/'process.json').read_text(encoding='utf8'));profile=bounded(record['profile']);assert profile.parent==out
 if args.action=='reopen':
  assert record['executable_kind']=='exported_release','This probe must reopen the same exported release.'
  import ctypes
  kernel=ctypes.WinDLL('kernel32',use_last_error=True);kernel.OpenProcess.restype=ctypes.c_void_p;kernel.WaitForSingleObject.argtypes=[ctypes.c_void_p,ctypes.c_uint32];kernel.CloseHandle.argtypes=[ctypes.c_void_p]
  handle=kernel.OpenProcess(0x00100000,False,record['pid'])
  if handle:
   try:assert kernel.WaitForSingleObject(handle,0)==0,'Close the original disposable native window before reopening.'
   finally:kernel.CloseHandle(handle)
  assert hashlib.sha256(Path(record['argv'][0]).read_bytes()).hexdigest()==record['executable_sha256'],'Same exported executable required.'
  env=os.environ.copy();env['MISTSEA_PROFILE_ROOT']=str(profile)
  process=subprocess.Popen(record['argv'],cwd=R,env=env,stdout=(out/'reopen_stdout.log').open('wb'),stderr=(out/'reopen_stderr.log').open('wb'),creationflags=subprocess.CREATE_NO_WINDOW)
  write(out/'reopened_process.json',{'pid':process.pid,'argv':record['argv'],'profile':str(profile),'same_exported_executable_sha256':record['executable_sha256'],'original_process_closed':True,'human_signoff':None});print(json.dumps({'report':str(out),'reopened_pid':process.pid},ensure_ascii=False));return
 if args.action=='release':
  current=json.loads((out/'current_lock.json').read_text(encoding='utf8')) if (out/'current_lock.json').is_file() else {'directory':str(out)}
  (bounded(current['directory'])/'release_lock').write_text('release',encoding='utf8');print('Isolated SQLite writer lock release requested');return
 control=sqlite3.connect(readonly_uri(profile/'control.sqlite'),uri=True);pointer=control.execute('SELECT current_path FROM recovery_pointers').fetchall();control.close();assert len(pointer)==1
 active=bounded(profile/pointer[0][0]);assert active.is_relative_to(profile) and active.is_file()
 if args.action=='lock':
  attempt=out/'lock_attempts'/datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ');attempt.mkdir(parents=True)
  db=sqlite3.connect(active,timeout=1);db.execute('BEGIN IMMEDIATE');write(attempt/'lock_acquired.json',{'actual_sqlite_lock':'BEGIN IMMEDIATE','database':str(active),'utc':datetime.now(timezone.utc).isoformat(),'limit_seconds':600});write(out/'current_lock.json',{'directory':str(attempt),'actual_database':str(active)});print('ACTUAL_ISOLATED_SQLITE_WRITER_LOCK_ACQUIRED '+str(active),flush=True)
  try:
   end=time.monotonic()+600
   while not (attempt/'release_lock').exists() and time.monotonic()<end:time.sleep(.2)
  finally:db.rollback();db.close();write(attempt/'lock_released.json',{'rolled_back_without_mutation':True,'utc':datetime.now(timezone.utc).isoformat()});print('ACTUAL_ISOLATED_SQLITE_WRITER_LOCK_RELEASED',flush=True)
  return
 db=sqlite3.connect(readonly_uri(active),uri=True);rows=db.execute("SELECT name FROM sqlite_master WHERE type='table'").fetchall()
 data={'scope':'read-only actual isolated database after native interaction','active_database':str(active),'tables':[r[0] for r in rows],'integrity':db.execute('PRAGMA integrity_check').fetchone()[0],'human_signoff':None}
 source=bounded(record['actual_source']);source_control=sqlite3.connect(readonly_uri(source/'control.sqlite'),uri=True);source_pointer=source_control.execute('SELECT current_path FROM recovery_pointers').fetchone();source_control.close();source_active=bounded(source/source_pointer[0]);baseline=sqlite3.connect(readonly_uri(source_active),uri=True)
 def digest(connection,table):
  values=sorted(connection.execute('SELECT * FROM '+table).fetchall(),key=repr);return {'rows':len(values),'sha256':hashlib.sha256(json.dumps(values,ensure_ascii=False,separators=(',',':')).encode('utf8')).hexdigest()}
 data['world_comparison']={table:{'before':digest(baseline,table),'after':digest(db,table)} for table in ['entities','balances','items','receipts']};baseline.close();data['world_and_rewards_unchanged']=all(v['before']==v['after'] for v in data['world_comparison'].values())
 if (out/'retry_ui.json').is_file():
  expected=json.loads((out/'retry_ui.json').read_text(encoding='utf8'))['document'];heads=db.execute('SELECT h.task_id,h.revision,r.body,r.body_hash FROM draft_heads h JOIN draft_revisions r ON h.draft_id=r.draft_id AND h.revision=r.revision WHERE r.deleted=0 AND r.body=?',(expected,)).fetchall();data['saved_body_exact_after_process_exit']=len(heads)==1 and heads[0][3]==hashlib.sha256(expected.encode('utf8')).hexdigest();data['matching_saved_heads']=[{'task':r[0],'revision':r[1],'body_sha256':r[3]} for r in heads]
  if (out/'reopen_ui.json').is_file() and (out/'reopened_process.json').is_file():
   opened=json.loads((out/'reopen_ui.json').read_text(encoding='utf8'));data['actual_exported_ui_reopened_exact_body']=record['executable_kind']=='exported_release' and opened['document']==expected and data['saved_body_exact_after_process_exit']
 write(out/'native_database_audit.json',data);db.close();assert data['integrity']=='ok';print(json.dumps({'audit':str(out/'native_database_audit.json'),'world_and_rewards_unchanged':data['world_and_rewards_unchanged'],'saved_body_exact_after_process_exit':data.get('saved_body_exact_after_process_exit')},ensure_ascii=False))
if __name__=='__main__':main()


"""Serial actual Godot probes using untouched entries from a verified current full suite."""
from datetime import datetime,timezone
from pathlib import Path
import argparse,json,os,subprocess,sys
R=Path(__file__).resolve().parents[2]
DEFAULT=['client-test','campaign-client-test','vessel-client-test','chapter-two-client-test','chapter-three-client-test','chapter-four-client-test','chapter-five-client-test','chapter-six-client-test','finale-client-test','r1-life-client-test','musicbox-client-test','r2-life-client-test','chapter-four-local-client-test','first-relay-client-test','first-side-client-test','first-outer-client-test','field-relay-client-test','vessel-life-client-test','settlement-support-client-test','lina-personal-client-test','r3-shift-client-test','old-city-local-life-client-test','scoped-personal-client-test']+[f'optional-r{n}-client-test' for n in range(1,7)]
p=argparse.ArgumentParser();p.add_argument('--suite',required=True);p.add_argument('actions',nargs='*');a=p.parse_args()
suite=Path(a.suite);suite=suite if suite.is_absolute() else R/suite;assert suite.is_relative_to(R/'reports/build')
out=R/'reports/continuous'/('native_sequence_'+datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ'));out.mkdir()
env=os.environ.copy();env['MISTSEA_REUSE_COMPILED']='1';env['MISTSEA_NATIVE_SOURCE_SUITE']=str(suite)
records=[]
for action in a.actions or DEFAULT:
 assert action in DEFAULT
 print('BEGIN '+action,flush=True);argv=[sys.executable,str(R/'tools/ci/build.py'),action]
 r=subprocess.run(argv,cwd=R,env=env,capture_output=True);stdout=r.stdout.decode('utf8',errors='replace');stderr=r.stderr.decode('utf8',errors='replace');(out/(action+'.log')).write_text(json.dumps({'argv':argv,'exit_code':r.returncode})+'\n[stdout]\n'+stdout+'\n[stderr]\n'+stderr,encoding='utf8')
 entry={'action':action,'exit_code':r.returncode}
 if r.returncode==0:
  entry.update(json.loads(stdout.strip().splitlines()[-1]))
 else:entry['error_tail']=(stdout+stderr)[-6000:]
 records.append(entry);(out/'progress.json').write_text(json.dumps({'scope':'actual serial native probes; no human acceptance','suite':suite.relative_to(R).as_posix(),'runs':records},ensure_ascii=False,indent=2),encoding='utf8');print(json.dumps(entry,ensure_ascii=False),flush=True)
 if r.returncode:sys.exit(r.returncode)
print(json.dumps({'result':'all_selected_native_runs_verified','report':out.relative_to(R).as_posix(),'runs':len(records),'human_play':'not_run'}),flush=True)

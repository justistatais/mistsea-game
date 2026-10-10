"""Actual serial diagnostics on frozen compiled inputs; retain every failure."""
from datetime import datetime, timezone
from pathlib import Path
import argparse,json,os,subprocess,sys
R=Path(__file__).resolve().parents[2]
p=argparse.ArgumentParser();p.add_argument('--suite',required=True);p.add_argument('actions',nargs='*');a=p.parse_args()
suite=(R/a.suite).resolve();assert suite.is_relative_to(R/'reports/build')
actions=['chapter-three-client-test','chapter-four-client-test','chapter-five-client-test','chapter-six-client-test','finale-client-test','r1-life-client-test','musicbox-client-test','r2-life-client-test','chapter-four-local-client-test','first-relay-client-test','first-side-client-test','first-outer-client-test','field-relay-client-test','vessel-life-client-test','settlement-support-client-test','lina-personal-client-test','r3-shift-client-test','old-city-local-life-client-test','scoped-personal-client-test']+[f'optional-r{n}-client-test' for n in range(1,7)]
out=R/'reports/continuous'/('native_diagnostic_sweep_'+datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ'));out.mkdir()
env=os.environ.copy();env.update(MISTSEA_REUSE_COMPILED='1',MISTSEA_NATIVE_SOURCE_SUITE=str(suite),PYTHONUTF8='1',PYTHONIOENCODING='utf-8')
records=[]
for action in a.actions or actions:
 assert action in actions
 print('BEGIN '+action,flush=True)
 argv=[sys.executable,'-X','utf8',str(R/'tools/ci/build.py'),action]
 run=subprocess.run(argv,cwd=R,env=env,capture_output=True)
 stdout=run.stdout.decode('utf8',errors='replace');stderr=run.stderr.decode('utf8',errors='replace')
 (out/(action+'.log')).write_text(json.dumps({'argv':argv,'exit_code':run.returncode})+'\n[stdout]\n'+stdout+'\n[stderr]\n'+stderr,encoding='utf8')
 item={'action':action,'exit_code':run.returncode}
 if run.returncode==0:item.update(json.loads(stdout.strip().splitlines()[-1]))
 else:item['error_tail']=(stdout+stderr)[-3000:]
 records.append(item)
 (out/'progress.json').write_text(json.dumps({'scope':'actual diagnostic sweep; failures preserved; no final qualification or human signoff','suite':suite.relative_to(R).as_posix(),'runs':records},ensure_ascii=False,indent=2),encoding='utf8')
 print(json.dumps({k:v for k,v in item.items() if k!='error_tail'},ensure_ascii=False),flush=True)
print(json.dumps({'report':out.relative_to(R).as_posix(),'runs':len(records),'failed':sum(r['exit_code']!=0 for r in records),'scope':'diagnostics only'}),flush=True)

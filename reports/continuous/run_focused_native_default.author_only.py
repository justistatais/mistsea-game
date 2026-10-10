"""Rebuild then verify the three corrected actual UI drivers with fresh normal producers."""
from pathlib import Path
from datetime import datetime,timezone
import json,os,subprocess,sys
R=Path(__file__).resolve().parents[2]
out=R/'reports/continuous'/('focused_native_driver_'+datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ'));out.mkdir()
env=os.environ.copy();env.update(PYTHONUTF8='1',PYTHONIOENCODING='utf-8')
env.pop('MISTSEA_NATIVE_SOURCE_SUITE',None);env.pop('MISTSEA_PARALLEL_FULL_SUITE',None)
records=[]
for index,action in enumerate(['chapter-two-client-test','chapter-three-client-test','chapter-six-client-test']):
 env['MISTSEA_REUSE_COMPILED']='0' if index==0 else '1'
 print('BEGIN '+action,flush=True);argv=[sys.executable,'-X','utf8',str(R/'tools/ci/build.py'),action]
 run=subprocess.run(argv,cwd=R,env=env,capture_output=True)
 stdout=run.stdout.decode('utf8',errors='replace');stderr=run.stderr.decode('utf8',errors='replace')
 (out/(action+'.log')).write_text(json.dumps({'argv':argv,'exit_code':run.returncode})+'\n[stdout]\n'+stdout+'\n[stderr]\n'+stderr,encoding='utf8')
 item={'action':action,'exit_code':run.returncode}
 if run.returncode==0:item.update(json.loads(stdout.strip().splitlines()[-1]))
 else:item['error_tail']=(stdout+stderr)[-6000:]
 records.append(item);(out/'progress.json').write_text(json.dumps({'scope':'actual focused correction verification; default fresh normal producers; not final full qualification','runs':records},ensure_ascii=False,indent=2),encoding='utf8')
 print(json.dumps(item,ensure_ascii=False),flush=True)
 if run.returncode:sys.exit(run.returncode)
print(json.dumps({'result':'focused_driver_verified','report':out.relative_to(R).as_posix(),'runs':len(records),'human_signoff':None}),flush=True)

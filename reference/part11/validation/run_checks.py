from __future__ import annotations
import io,json,sys,time,unittest,argparse
from pathlib import Path
from datetime import datetime,timezone
ROOT=Path(__file__).resolve().parents[1]
ap=argparse.ArgumentParser();ap.add_argument('--output-dir',type=Path);args=ap.parse_args()
OUT=args.output_dir or ROOT/'validation';OUT.mkdir(parents=True,exist_ok=True)
class Result(unittest.TextTestResult):
    def __init__(self,*a,**k):super().__init__(*a,**k);self.rows=[]
    def addSuccess(self,t):super().addSuccess(t);self.rows.append({'id':t.id(),'result':'pass'})
    def addFailure(self,t,e):super().addFailure(t,e);self.rows.append({'id':t.id(),'result':'fail'})
    def addError(self,t,e):super().addError(t,e);self.rows.append({'id':t.id(),'result':'error'})
suite=unittest.defaultTestLoader.discover(str(ROOT/'validation'),pattern='test_pipeline.py')
buf=io.StringIO();start=time.monotonic()
r=unittest.TextTestRunner(stream=buf,verbosity=2,resultclass=Result).run(suite)
report={'run_at_utc':datetime.now(timezone.utc).isoformat(),'tests_run':r.testsRun,'failures':len(r.failures),'errors':len(r.errors),'skipped':len(r.skipped),'elapsed_seconds':round(time.monotonic()-start,4),'results':r.rows,
        'scope':'Synthetic finite contract tests + source inventory. No model/TTS/network calls; no real human review, player pilot, provider safety or production signing tested.',
        'limitations':['JSON shape and citations do not prove semantic correctness','Synthetic approvals/audio are NOT real review records or tracks','Runtime policy registries are trusted fixtures; production authentication and persistence absent','No game-engine integration or filesystem crash recovery tested']}
(OUT/'validation_report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(OUT/'test_log.txt').write_text(buf.getvalue(),encoding='utf-8')
print(buf.getvalue());print(json.dumps({k:v for k,v in report.items() if k!='results'},ensure_ascii=False,indent=2))
sys.exit(0 if r.wasSuccessful() else 1)

from __future__ import annotations
import io,json,platform,sqlite3,sys,unittest
from datetime import datetime,timezone
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'reference'))
class RecordingResult(unittest.TextTestResult):
    def __init__(self,*a,**kw):super().__init__(*a,**kw);self.cases=[]
    def addSuccess(self,test):super().addSuccess(test);self.cases.append({'id':test.id(),'status':'passed'})
    def addFailure(self,test,err):super().addFailure(test,err);self.cases.append({'id':test.id(),'status':'failed'})
    def addError(self,test,err):super().addError(test,err);self.cases.append({'id':test.id(),'status':'error'})
if __name__=='__main__':
    log=io.StringIO();suite=unittest.defaultTestLoader.discover(str(ROOT/'reference'),pattern='test_reference.py')
    result=unittest.TextTestRunner(stream=log,verbosity=2,resultclass=RecordingResult).run(suite)
    out=ROOT/'validation';out.mkdir(exist_ok=True)
    report={'executed_at_utc':datetime.now(timezone.utc).isoformat(),'python':platform.python_version(),'sqlite':sqlite3.sqlite_version,'platform':platform.platform(),
            'tests_run':result.testsRun,'failures':len(result.failures),'errors':len(result.errors),'skipped':len(result.skipped),'process_termination_hook_scenarios':5,
            'real_files':True,'reference_language':'Python','journal_mode':'DELETE','synchronous':'FULL',
            'not_tested':['Godot','C# compilation','Windows export','physical power loss','real IME','screen reader','provider request','full combat','IELTS calibration','publisher signature verification','managed backup purge / secure physical deletion'],
            'cases':result.cases}
    (out/'run.log').write_text(log.getvalue(),encoding='utf-8')
    (out/'validation_report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(log.getvalue());sys.exit(0 if result.wasSuccessful() else 1)

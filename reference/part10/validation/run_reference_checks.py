from pathlib import Path
import io, json, platform, sys, unittest
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'validation'))
suite=unittest.defaultTestLoader.discover(str(ROOT/'validation'),pattern='test_ui_reference.py')
def flatten(s):
    for item in s:
        if isinstance(item,unittest.TestSuite):yield from flatten(item)
        else:yield item.id()
ids=list(flatten(suite));stream=io.StringIO()
result=unittest.TextTestRunner(stream=stream,verbosity=2).run(suite)
(ROOT/'validation/unit_test_log.txt').write_text(stream.getvalue())
failed={t.id():trace for t,trace in result.failures+result.errors}
report={'python':platform.python_version(),'scope':'in-memory bounded UI contracts, not an engine or durable filesystem','run':result.testsRun,'passed':result.testsRun-len(result.failures)-len(result.errors)-len(result.skipped),'failures':len(result.failures),'errors':len(result.errors),'skipped':len(result.skipped),'cases':[{'id':i,'status':'failed' if i in failed else 'passed',**({'traceback':failed[i]} if i in failed else {})} for i in ids]}
(ROOT/'validation/reference_report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
print(f"Reference checks: {report['run']}, passed {report['passed']}, failures {report['failures']}, errors {report['errors']}")
sys.exit(not result.wasSuccessful())

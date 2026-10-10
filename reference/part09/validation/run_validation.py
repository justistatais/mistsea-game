from pathlib import Path
import sys,io,json,unittest,platform
from datetime import datetime,timezone
P=Path(__file__).resolve().parent
sys.path.insert(0,str(P))
suite=unittest.defaultTestLoader.discover(str(P),pattern='test_npc.py')
stream=io.StringIO();result=unittest.TextTestRunner(stream=stream,verbosity=2).run(suite)
log=stream.getvalue();(P/'run_log.txt').write_text(log,encoding='utf-8')
report={'part':9,'version':'0.1','run_at_utc':datetime.now(timezone.utc).isoformat(),
        'python':platform.python_version(),'tests_run':result.testsRun,
        'failures':len(result.failures),'errors':len(result.errors),'skipped':len(result.skipped),
        'finite_cases':{'companion_stance_and_recruitment_combinations':1296,'enhanced_readiness_combinations':32},
        'scope':'in-memory reference contracts and metadata; not game, LLM, authentication or crash recovery',
        'limitations':['No actual generated-dialogue safety test','No IELTS 6.5 human calibration','No actual audio','No full 12-level gameplay','No live scene navigation or real save-file crash test']}
(P/'validation_report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(log);print(json.dumps(report,ensure_ascii=False,indent=2))
sys.exit(0 if result.wasSuccessful() else 1)

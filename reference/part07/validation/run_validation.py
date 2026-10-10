from pathlib import Path
import io,json,sys,time,unittest
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'validation'))
from rpg_reference import REF,derived,level_from_xp,threshold
start=time.monotonic()
suite=unittest.defaultTestLoader.discover(str(ROOT/'validation'),pattern='test_*.py')
stream=io.StringIO();result=unittest.TextTestRunner(stream=stream,verbosity=2).run(suite)
text=stream.getvalue();(ROOT/'validation'/'run_log.txt').write_text(text)
# Exact derivative values, not damage/battle win claims.
records=[]
for b in REF['builds']:
 out=derived(b['level'],b['stats'],4 if b['level']==30 else 0,b['weapon'],b['armor'])
 records.append({'build':b['id'],'level':b['level'],'hp':out['hp'],'focus':out['focus'],'initiative':out['initiative'],'A':float(out['A']),'T':float(out['T']),'armor_bp':out['armor_bp'],'scope':'before conditional passive, relic and module effects'})
report={'tests_run':result.testsRun,'failures':len(result.failures),'errors':len(result.errors),'skipped':len(result.skipped),'success':result.wasSuccessful(),'elapsed_seconds':round(time.monotonic()-start,3),'counts':{'talents':len(REF['skills']),'core_equipment':len(REF['items']),'module_definitions':len(REF['modules']),'main_reward_rows':len(REF['main_rewards'])},'main_only_levels':[{'chapter':c['title'],'cumulative_xp':c['cumulative_main_xp'],'level':level_from_xp(c['cumulative_main_xp'])} for c in REF['chapter_budgets']],'build_statistics':records,'scope_limits':REF['validation_limits'],'note':'Tests of declared constants are contract checks, not engine behavior validation. No claim that every listed talent proc has been executed.'}
(ROOT/'validation'/'validation_report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
print(text[-1800:]);print(json.dumps({k:report[k] for k in ['tests_run','failures','errors','success','counts']},ensure_ascii=False,indent=2))
if not result.wasSuccessful():raise SystemExit(1)

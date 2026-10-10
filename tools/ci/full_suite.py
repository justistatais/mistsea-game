"""Execute every registered C# case once, in separate disposable profile directories."""
from concurrent.futures import ThreadPoolExecutor,as_completed
import json,os,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
def main():
 dotnet,dll=sys.argv[1:3];out=Path(os.environ['MISTSEA_TEST_OUTPUT']).parent
 workers=int(os.environ.get('MISTSEA_PARALLEL_FULL_SUITE','3'));assert 1<=workers<=4
 inventory=subprocess.run([dotnet,dll,'--list-cases'],cwd=ROOT,env=os.environ.copy(),capture_output=True,timeout=60)
 assert inventory.returncode==0,inventory.stderr.decode('utf8',errors='replace')
 cases=json.loads(inventory.stdout.decode('utf8'))['cases'];assert cases and [c['index'] for c in cases]==list(range(len(cases)))
 (out/'full_suite_inventory.json').write_text(json.dumps({'scope':'actual compiled test registry; not acceptance','cases':cases},ensure_ascii=False,indent=2),encoding='utf8')
 def run(case):
  child=out/'case_runs'/f"{case['index']:04d}";child.mkdir(parents=True)
  env=os.environ.copy();env['MISTSEA_TEST_OUTPUT']=str(child/'testdata')
  argv=[dotnet,dll,'--case-index='+str(case['index'])]
  try:
   process=subprocess.run(argv,cwd=ROOT,env=env,capture_output=True,timeout=1800)
   stdout=process.stdout.decode('utf8',errors='replace');stderr=process.stderr.decode('utf8',errors='replace')
   record={'argv':argv,'cwd':str(ROOT),'exit_code':process.returncode,'scope':'one exact compiled registry case with its own disposable profiles'}
   (child/'command.log').write_text(json.dumps(record,ensure_ascii=False)+'\n[stdout]\n'+stdout+'\n[stderr]\n'+stderr,encoding='utf8')
   assert 'ERROR:' not in stderr,stderr
   parsed=json.loads(stdout);assert parsed['executed']==1 and len(parsed['results'])==1
   result=parsed['results'][0];assert result['task']==case['task'] and result['name']==case['name']
   assert process.returncode==(0 if result['result']=='passed' else 1)
   (child/'test_results.json').write_text(json.dumps(parsed,ensure_ascii=False,indent=2),encoding='utf8')
   return case['index'],result,parsed
  except Exception as error:
   return case['index'],{'task':case['task'],'name':case['name'],'result':'failed','detail':str(error)},{}
 results={};facts={};failed=0
 with ThreadPoolExecutor(max_workers=workers) as executor:
  jobs=[executor.submit(run,c) for c in cases]
  for job in as_completed(jobs):
   index,result,parsed=job.result();results[index]=result
   if result['result']!='passed':failed+=1
   if parsed:
    runtime={k:parsed[k] for k in ['sqlite_version','dotnet_runtime','process_architecture']}
    if facts and runtime!=facts:raise RuntimeError('Independent child runtimes disagree.')
    facts=runtime
   (out/'full_suite_progress.json').write_text(json.dumps({'registered':len(cases),'completed':len(results),'failed':failed,'workers':workers},ensure_ascii=False),encoding='utf8')
 assert len(results)==len(cases) and set(results)==set(range(len(cases)))
 data={'scope':'every registered C# case executed exactly once in isolated child profiles','executed':len(results),'failed':failed,'results':[results[i] for i in range(len(cases))],**facts,'human_signoff':None,'parallel_workers':workers,'case_inventory':'full_suite_inventory.json','case_reports':'case_runs'}
 # The parent consumes this protocol as UTF-8 even on a legacy Windows console.
 print(json.dumps(data,ensure_ascii=True,indent=2));return 0 if failed==0 else 1
if __name__=='__main__':sys.exit(main())

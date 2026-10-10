"""Run named current compiled cases; never infer a full suite from a filtered run."""
from concurrent.futures import ThreadPoolExecutor,as_completed
from pathlib import Path
import argparse,hashlib,json,os,subprocess,sys
R=Path(__file__).resolve().parents[2]
p=argparse.ArgumentParser();p.add_argument('marker');p.add_argument('--workers',type=int,default=3);a=p.parse_args();assert 1<=a.workers<=3
installed=json.loads((R/'toolchain/installed.windows-x64.json').read_text(encoding='utf8'));dll=R/'tests/Mist.Tests/bin/Debug/net10.0/Mist.Tests.dll';before=hashlib.sha256(dll.read_bytes()).hexdigest()
raw=subprocess.run([installed['dotnet'],str(dll),'--list-cases'],cwd=R,capture_output=True,check=True);cases=[c for c in json.loads(raw.stdout)['cases'] if a.marker in c['name']];assert cases
def run(c):
 env=os.environ.copy();env['MISTSEA_REUSE_COMPILED']='1'
 command=[sys.executable,str(R/'tools/ci/build.py'),'test','--task='+c['task'],'--case-index='+str(c['index'])]
 result=subprocess.run(command,cwd=R,env=env,capture_output=True)
 print(json.dumps({'selected_case':c,'exit_code':result.returncode,'output':result.stdout.decode('utf8',errors='replace')[-2000:],'error':result.stderr.decode('utf8',errors='replace')[-6000:]},ensure_ascii=False),flush=True)
 assert hashlib.sha256(dll.read_bytes()).hexdigest()==before,'Compiled selector changed during execution.'
 return result.returncode
with ThreadPoolExecutor(max_workers=a.workers) as executor:codes=[f.result() for f in as_completed([executor.submit(run,c) for c in cases])]
sys.exit(1 if any(codes) else 0)

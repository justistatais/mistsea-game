"""Capture current inputs before the actual native performance process."""
from datetime import datetime,timezone
from pathlib import Path
import argparse,hashlib,importlib.util,json,os,subprocess,sys
R=Path(__file__).resolve().parents[2]
p=argparse.ArgumentParser();p.add_argument('--suite',required=True);a=p.parse_args()
suite=Path(a.suite);suite=suite if suite.is_absolute() else R/suite
def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
spec=importlib.util.spec_from_file_location('_current_input_capture',R/'tools/ci/build_inputs.py');module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
inputs=module.current_inputs();assert inputs==read(suite/'build_inputs.json')['inputs']
result=read(suite/'result.json');tests=read(suite/'test_results.json')
assert result['result']=='verified' and result['action']=='test' and tests['failed']==0
assert tests['executed']==len(read(suite/'full_suite_inventory.json')['cases'])
paths=list((R/'game/.godot/mono/temp/bin/Debug').rglob('*.dll'))+list((R/'tests/Mist.Tests/bin/Debug/net10.0').glob('*.dll'))
binaries=[{'path':f.relative_to(R).as_posix(),'sha256':hashlib.sha256(f.read_bytes()).hexdigest()} for f in sorted(paths)]
assert binaries==read(suite/'compiled_artifacts.json')['binaries']
installed=read(R/'toolchain/installed.windows-x64.json')
capture=R/'reports/continuous'/('performance_capture_'+datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')+'.json')
payload={'captured_before_native_start':True,'captured_utc':datetime.now(timezone.utc).isoformat(),'inputs':inputs,'binaries':binaries,'performance_command':{'argv':[installed['godot'],'--path',str(R/'game'),'--','--performance-candidate'],'cwd':str(R)},'human_signoff':None}
hardware_script="""[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$perfOs = Get-CimInstance Win32_OperatingSystem
$perfComputer = Get-CimInstance Win32_ComputerSystem
$perfCpu = @(Get-CimInstance Win32_Processor | Select-Object -ExpandProperty Name)
$perfGpu = @(Get-CimInstance Win32_VideoController | Select-Object Name, DriverVersion)
$perfStorage = @(Get-CimInstance Win32_DiskDrive | Select-Object Model, InterfaceType, Size)
@{ os = $perfOs.Caption + ' ' + $perfOs.Version + ' build ' + $perfOs.BuildNumber; cpu = $perfCpu -join '; '; gpu = $perfGpu; memory_mib = [Math]::Round($perfComputer.TotalPhysicalMemory / 1MB); storage = $perfStorage } | ConvertTo-Json -Depth 4 -Compress
"""
try:
 hardware=subprocess.run([str(Path(os.environ['SystemRoot'])/'System32/WindowsPowerShell/v1.0/powershell.exe'),'-NoProfile','-NonInteractive','-Command',hardware_script],capture_output=True,timeout=45)
 assert hardware.returncode==0,'Actual read-only hardware observation failed'
 payload['hardware']=json.loads(hardware.stdout.decode('utf8').lstrip('\ufeff'))
 payload['hardware_observation']={'source':'Actual read-only Windows CIM immediately before native process','captured_utc':datetime.now(timezone.utc).isoformat(),'human_certification':None}
except (OSError,KeyError,ValueError,AssertionError,subprocess.TimeoutExpired) as error:
 payload['hardware_observation']={'result':'unavailable','reason':str(error),'human_certification':None}
capture.write_text(json.dumps(payload,ensure_ascii=False,indent=2),encoding='utf8')
native=subprocess.run([sys.executable,'-X','utf8',str(R/'tools/ci/native_performance.py')],cwd=R,capture_output=True)
stdout=native.stdout.decode('utf8',errors='replace');stderr=native.stderr.decode('utf8',errors='replace')
assert native.returncode==0,(stdout+stderr)[-6000:]
data=json.loads(stdout.strip().splitlines()[-1]);data['pre_native_capture']=capture.relative_to(R).as_posix()
print(json.dumps(data,ensure_ascii=False))

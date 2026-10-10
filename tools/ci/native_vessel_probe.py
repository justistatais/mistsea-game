"""Open only an isolated copy of the actual command-route profile for native UI observation."""
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT=Path(__file__).resolve().parents[2]
source=ROOT / sys.argv[1]
proof=json.loads((source / "vessel_client_results.json").read_text(encoding="utf8"))
assert proof["failures"] == 0 and (source / "client_profile/control.sqlite").is_file()
stamp=datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
out=ROOT / "reports/execution/CX-060" / stamp
out.mkdir(parents=True)
profile=out / "native_profile"
shutil.copytree(source / "client_profile",profile)
installed=json.loads((ROOT / "toolchain/installed.windows-x64.json").read_text(encoding="utf8"))
env=os.environ.copy()
env.update(DOTNET_ROOT=installed["dotnet_root"],DOTNET_ROOT_X64=installed["dotnet_root"],NUGET_PACKAGES=installed["nuget_packages"],MISTSEA_PROFILE_ROOT=str(profile))
env["PATH"]=installed["dotnet_root"]+os.pathsep+env.get("PATH","")
args=[installed["godot"],"--path",str(ROOT / "game")]
process=subprocess.Popen(args,cwd=ROOT,env=env,stdout=(out / "native_stdout.log").open("wb"),stderr=(out / "native_stderr.log").open("wb"),creationflags=subprocess.CREATE_NO_WINDOW)
record={"pid":process.pid,"source":source.relative_to(ROOT).as_posix(),"evidence":str(out),"argv":args,"scope":"native observation of actual normal Q104 command route; isolated copy; no human acceptance claim"}
(out / "native_process.json").write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding="utf8")
print(json.dumps(record,ensure_ascii=False))

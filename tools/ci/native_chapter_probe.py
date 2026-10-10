"""Open an isolated native window from an actually verified chapter route profile."""
from datetime import datetime, timezone
import json
from pathlib import Path
import shutil
import subprocess
import sys

ROOT=Path(__file__).resolve().parents[2]
region=sys.argv[1]
assert region in {"R2","R3","R4","R5","R6","RF"}
action={"R2":"chapter-two-client-test","R3":"chapter-three-client-test","R4":"chapter-four-client-test","R5":"chapter-five-client-test","R6":"chapter-six-client-test","RF":"finale-client-test"}[region]
runs=[]
for path in (ROOT/"reports/build").glob("*/result.json"):
    data=json.loads(path.read_text(encoding="utf8"))
    if data.get("action")==action and data.get("result")=="verified":runs.append(path.parent)
assert runs,"Complete the actual chapter native-client test before opening its route profile."
source=max(runs,key=lambda p:p.name)
metadata=json.loads((source/"client_profile_source.json").read_text(encoding="utf8"))
assert metadata["fixture"] is False
profile=source/"client_profile"
assert (profile/"control.sqlite").is_file()
out=ROOT/"reports/native"/(datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")+"-"+region)
out.mkdir(parents=True)
shutil.copytree(profile,out/"profile")
installed=json.loads((ROOT/"toolchain/installed.windows-x64.json").read_text(encoding="utf8"))
import os
env=os.environ.copy()
env.update(DOTNET_ROOT=installed["dotnet_root"],DOTNET_ROOT_X64=installed["dotnet_root"],MISTSEA_PROFILE_ROOT=str(out/"profile"))
env["PATH"]=installed["dotnet_root"]+os.pathsep+env["PATH"]
args=[installed["godot"],"--path",str(ROOT/"game")]
process=subprocess.Popen(args,cwd=ROOT,env=env,stdout=(out/"stdout.log").open("wb"),stderr=(out/"stderr.log").open("wb"),creationflags=subprocess.CREATE_NO_WINDOW)
record={"pid":process.pid,"argv":args,"evidence":out.relative_to(ROOT).as_posix(),"source_verified_run":source.relative_to(ROOT).as_posix(),"source_entry":metadata,"scope":"isolated actual completed chapter profile for native window inspection; no human playthrough, language or audition approval", "human_signoff":None}
(out/"process.json").write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding="utf8")
print(json.dumps(record,ensure_ascii=False))

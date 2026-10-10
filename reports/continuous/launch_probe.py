from pathlib import Path
import json
import subprocess
import sys
from datetime import datetime, timezone
from build_probe import ROOT, ENV, installed

task, folder = sys.argv[1:3]
evidence = ROOT / "reports/execution" / task / datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
evidence.mkdir(parents=True)
env = ENV.copy()
if folder == "game": env["MISTSEA_PROFILE_ROOT"] = str(evidence / "native_test_profile")
args=[installed["godot"],"--path",str(ROOT / folder)]
if len(sys.argv)>3: args.extend(["--",*sys.argv[3:]])
process = subprocess.Popen(args, cwd=ROOT, env=env,
                           stdout=(evidence / "native_window_stdout.log").open("wb"),
                           stderr=(evidence / "native_window_stderr.log").open("wb"),
                           creationflags=subprocess.CREATE_NO_WINDOW)
record = {"pid": process.pid, "project": folder, "evidence": str(evidence), "executable": installed["godot"],"argv":args,"scope":"internal isolated native UI candidate"}
(evidence / "native_process.json").write_text(json.dumps(record), encoding="utf8")
print(json.dumps(record))

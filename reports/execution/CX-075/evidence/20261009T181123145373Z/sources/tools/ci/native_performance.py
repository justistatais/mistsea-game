"""Observe actual native frame cadence without treating a headless script as game FPS."""
from datetime import datetime,timezone
import json,os
from pathlib import Path
import subprocess
ROOT=Path(__file__).resolve().parents[2]
def main():
    installed=json.loads((ROOT / "toolchain/installed.windows-x64.json").read_text(encoding="utf8"))
    out=ROOT / "reports/execution/CX-075" / datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ");out.mkdir(parents=True)
    env=os.environ.copy();env.update(DOTNET_ROOT=installed["dotnet_root"],DOTNET_ROOT_X64=installed["dotnet_root"],NUGET_PACKAGES=installed["nuget_packages"],MISTSEA_PROFILE_ROOT=str(out / "isolated_profile"),MISTSEA_CLIENT_TEST_OUTPUT=str(out),MISTSEA_PERFORMANCE_PROFILE="1")
    env["PATH"]=installed["dotnet_root"]+os.pathsep+env.get("PATH","")
    argv=[installed["godot"],"--path",str(ROOT / "game"),"--","--performance-candidate"]
    run=subprocess.run(argv,cwd=ROOT,env=env,capture_output=True,timeout=60,creationflags=subprocess.CREATE_NO_WINDOW)
    stdout=run.stdout.decode("utf8",errors="replace");stderr=run.stderr.decode("utf8",errors="replace");log=out / "native_runtime.log";log.write_text(json.dumps({"argv":argv,"exit_code":run.returncode})+"\n[stdout]\n"+stdout+"\n[stderr]\n"+stderr,encoding="utf8")
    command={"argv":argv,"cwd":str(ROOT),"exit_code":run.returncode,"output_path":log.relative_to(ROOT).as_posix()};(out / "commands.json").write_text(json.dumps([command],ensure_ascii=False,indent=2),encoding="utf8")
    assert run.returncode==0 and "MISTSEA_NATIVE_PERFORMANCE frames=" in stdout and "ERROR:" not in stderr,log
    data=json.loads((out / "native_performance.json").read_text(encoding="utf8"));assert data["renderer"]!="headless" and data["retained_fixture_heads"]==10 and data["experience"]==0
    print(json.dumps({"result":"native_candidate_measurement","path":out.relative_to(ROOT).as_posix(),"frames":data["frames"],"observed_frame_cadence_fps":data["observed_frame_cadence_fps"],"scope":data["scope"]},ensure_ascii=False))
if __name__=="__main__":main()

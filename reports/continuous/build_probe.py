"""Run and retain actual Windows Godot probe commands, without certifying human checks."""
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
installed = json.loads((ROOT / "toolchain/installed.windows-x64.json").read_text(encoding="utf8"))
ENV = os.environ.copy()
ENV.update(DOTNET_ROOT=installed["dotnet_root"], DOTNET_ROOT_X64=installed["dotnet_root"],
           DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", NUGET_PACKAGES=installed["nuget_packages"])
ENV["PATH"] = installed["dotnet_root"] + os.pathsep + ENV["PATH"]


def main():
    task, relative, name, flag = sys.argv[1:5]
    folder = ROOT / relative
    project = folder / (name + ".csproj")
    out = ROOT / "reports/execution" / task / datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    out.mkdir(parents=True)
    ENV["MISTSEA_PROBE_OUTPUT"] = str(out)
    commands = []

    def run(label, args):
        print("RUN " + label, flush=True)
        r = subprocess.run(args, cwd=ROOT, env=ENV, capture_output=True, timeout=300)
        stdout, stderr = r.stdout.decode("utf8", errors="replace"), r.stderr.decode("utf8", errors="replace")
        path = out / (label + ".log")
        path.write_text(json.dumps({"argv": args, "cwd": str(ROOT), "exit_code": r.returncode}) + "\n[stdout]\n" + stdout + "\n[stderr]\n" + stderr, encoding="utf8")
        commands.append({"argv": args, "cwd": str(ROOT), "exit_code": r.returncode, "output_path": path.relative_to(ROOT).as_posix()})
        (out / "commands.json").write_text(json.dumps(commands, ensure_ascii=False, indent=2), encoding="utf8")
        print("EXIT " + str(r.returncode), flush=True)
        if r.returncode or "ERROR:" in stderr:
            raise RuntimeError(stdout + "\n" + stderr)
        return stdout

    try:
        dotnet, godot = installed["dotnet"], installed["godot"]
        solution = folder / (name + ".sln")
        if not solution.exists():
            run("solution", [dotnet, "new", "sln", "--format", "sln", "--name", name, "--output", str(folder)])
            run("solution_add", [dotnet, "sln", str(solution), "add", str(project)])
        run("restore", [dotnet, "restore", str(project), "--configfile", str(ROOT / "spikes/toolchain/NuGet.Config")])
        run("build", [dotnet, "build", str(project), "--no-restore", "-c", "Debug"])
        run("import", [godot, "--headless", "--editor", "--path", str(folder), "--quit-after", "2"])
        stdout = run("runtime", [godot, "--headless", "--path", str(folder), "--", flag])
        result = {"task_id": task, "project": relative, "scope": "actual Windows engine process with automated checks", "commands": commands, "runtime_output": stdout}
        (out / "result.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf8")
        (ROOT / "reports/execution" / task / "latest.json").write_text(json.dumps({"path": out.relative_to(ROOT).as_posix(), "result": "verified"}), encoding="utf8")
        print(json.dumps({"result": "verified", "path": out.relative_to(ROOT).as_posix(), "runtime": stdout}, ensure_ascii=False), flush=True)
    except Exception as error:
        (out / "failure.json").write_text(json.dumps({"error": str(error), "commands": commands}, ensure_ascii=False, indent=2), encoding="utf8")
        raise


if __name__ == "__main__":
    main()

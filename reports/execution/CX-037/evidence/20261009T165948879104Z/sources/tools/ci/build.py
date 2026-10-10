"""Portable build/test entry: python tools/ci/build.py [configure|build|test|smoke]."""
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
PROJECTS = ["src/Mist.Core/Mist.Core.csproj", "src/Mist.Application/Mist.Application.csproj",
            "src/Mist.Infrastructure/Mist.Infrastructure.csproj", "game/MistSea.csproj",
            "tools/Mist.ContentCli/Mist.ContentCli.csproj", "tests/Mist.Tests/Mist.Tests.csproj",
            "services/Mist.Assist/Mist.Assist.csproj"]


def main():
    action = sys.argv[1] if len(sys.argv) > 1 else "test"
    assert action in {"configure", "build", "test", "smoke", "content", "client-test", "campaign-client-test"}, "Unknown build action."
    manifest_path = ROOT / "toolchain/installed.windows-x64.json"
    installed = json.loads(manifest_path.read_text(encoding="utf8")) if manifest_path.exists() else {}
    env = os.environ.copy()
    dotnet = shutil.which("dotnet")
    if installed and Path(installed["dotnet"]).exists():
        dotnet = installed["dotnet"]
        env.update(DOTNET_ROOT=installed["dotnet_root"], DOTNET_ROOT_X64=installed["dotnet_root"], NUGET_PACKAGES=installed["nuget_packages"])
        env["PATH"] = installed["dotnet_root"] + os.pathsep + env["PATH"]
    if not dotnet: raise RuntimeError("Install the SDK specified by global.json first.")
    env.update(DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    out = ROOT / "reports/build" / datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    out.mkdir(parents=True)
    env["MISTSEA_TEST_OUTPUT"] = str(out / "testdata")
    env["MISTSEA_PROFILE_ROOT"] = str(out / "client_profile")
    env["MISTSEA_CLIENT_TEST_OUTPUT"] = str(out)
    records = []

    def run(label, argv, expected_code=None, expected_marker="intentional test runner failure"):
        print("RUN " + label, flush=True)
        r = subprocess.run(argv, cwd=ROOT, env=env, capture_output=True, timeout=600)
        stdout, stderr = r.stdout.decode("utf8", errors="replace"), r.stderr.decode("utf8", errors="replace")
        log = out / (label + ".log")
        log.write_text(json.dumps({"argv": argv, "cwd": str(ROOT), "exit_code": r.returncode}) + "\n[stdout]\n" + stdout + "\n[stderr]\n" + stderr, encoding="utf8")
        record = {"argv": argv, "cwd": str(ROOT), "exit_code": r.returncode, "output_path": log.relative_to(ROOT).as_posix()}
        if expected_code is not None:
            record.update(expected_nonzero_proven=r.returncode == expected_code and expected_marker in stdout + stderr,
                          expected_failure_reason="Expected refusal or explicit failure probe: " + expected_marker)
        records.append(record)
        (out / "commands.json").write_text(json.dumps(records, ensure_ascii=False, indent=2), encoding="utf8")
        print("EXIT " + str(r.returncode), flush=True)
        if expected_code is not None:
            if not record["expected_nonzero_proven"]: raise RuntimeError(stdout + stderr)
        elif r.returncode != 0 or "ERROR:" in stderr: raise RuntimeError(stdout + stderr)
        return stdout

    try:
        version = run("sdk_version", [dotnet, "--version"]).strip()
        if version != "10.0.401": raise RuntimeError("SDK differs from global.json: " + version)
        if not (ROOT / "MistSea.sln").exists():
            run("solution_create", [dotnet, "new", "sln", "--format", "sln", "--name", "MistSea", "--output", str(ROOT)])
            run("solution_projects", [dotnet, "sln", str(ROOT / "MistSea.sln"), "add", *[str(ROOT / p) for p in PROJECTS]])
        if not (ROOT / "game/MistSea.sln").exists():
            run("game_solution", [dotnet, "new", "sln", "--format", "sln", "--name", "MistSea", "--output", str(ROOT / "game")])
            run("game_solution_project", [dotnet, "sln", str(ROOT / "game/MistSea.sln"), "add", str(ROOT / "game/MistSea.csproj")])
        restore = [dotnet, "restore", str(ROOT / "MistSea.sln"), "--configfile", str(ROOT / "spikes/toolchain/NuGet.Config")]
        if action != "configure": restore.append("--locked-mode")
        run("restore", restore)
        run("build", [dotnet, "build", str(ROOT / "MistSea.sln"), "-c", "Debug", "--no-restore"])
        if action in {"test", "configure"}:
            tests = run("tests", [dotnet, str(ROOT / "tests/Mist.Tests/bin/Debug/net10.0/Mist.Tests.dll"), *sys.argv[2:]])
            parsed = json.loads(tests)
            if parsed["executed"] == 0 or parsed["failed"]: raise RuntimeError("No passing executed tests.")
            (out / "test_results.json").write_text(json.dumps(parsed, ensure_ascii=False, indent=2), encoding="utf8")
            run("runner_failure_probe", [dotnet, str(ROOT / "tests/Mist.Tests/bin/Debug/net10.0/Mist.Tests.dll"), "--fail-probe"], expected_code=7)
        if action in {"smoke", "client-test", "campaign-client-test"}:
            godot = installed.get("godot") or shutil.which("godot")
            if not godot: raise RuntimeError("Matching Godot .NET editor is required for smoke action.")
            run("godot_import", [godot, "--headless", "--editor", "--path", str(ROOT / "game"), "--quit-after", "2"])
            output = run("client_smoke" if action == "smoke" else "client_tests", [godot, "--headless", "--path", str(ROOT / "game"), "--", "--"+action])
            if "MISTSEA_CLIENT_BOOT" not in output: raise RuntimeError("Managed client boot marker absent.")
        if action == "content":
            compiler = str(ROOT / "tools/Mist.ContentCli/bin/Debug/net10.0/Mist.ContentCli.dll")
            run("content_internal", [dotnet, compiler, "compile", str(ROOT), str(ROOT / "game/content")])
            run("content_formal_refusal", [dotnet, compiler, "compile", str(ROOT), str(ROOT / "game/content"), "--formal"], expected_code=3, expected_marker="formal_release_blocked")
        (out / "result.json").write_text(json.dumps({"action": action, "result": "verified", "sdk": version, "commands": records,
            "human_tests": "not_run", "product_coverage_claim": "only named executed test cases"}, ensure_ascii=False, indent=2), encoding="utf8")
        (ROOT / "reports/build/latest.json").write_text(json.dumps({"path": out.relative_to(ROOT).as_posix(), "action": action, "result": "verified"}), encoding="utf8")
        print(json.dumps({"action": action, "result": "verified", "path": out.relative_to(ROOT).as_posix()}), flush=True)
    except Exception as error:
        (out / "failure.json").write_text(json.dumps({"error": str(error), "commands": records}, ensure_ascii=False, indent=2), encoding="utf8")
        raise


if __name__ == "__main__": main()

"""Real CX-002 version, build, native SQLite, audit and export probes."""
from __future__ import annotations

from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
INSTALLED = json.loads((ROOT / "toolchain/installed.windows-x64.json").read_text(encoding="utf-8"))
OUT = ROOT / "reports/execution/CX-002/runs" / datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
OUT.mkdir(parents=True)
COMMANDS = []
ENV = os.environ.copy()
ENV.update({
    "DOTNET_ROOT": INSTALLED["dotnet_root"], "DOTNET_ROOT_X64": INSTALLED["dotnet_root"],
    "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1",
    "DOTNET_GENERATE_ASPNET_CERTIFICATE": "false",
    "DOTNET_CLI_HOME": str(Path(INSTALLED["base"]) / "dotnet-home"),
    "NUGET_PACKAGES": INSTALLED["nuget_packages"],
    "PATH": INSTALLED["dotnet_root"] + os.pathsep + os.environ["PATH"],
})
DOTNET = INSTALLED["dotnet"]
GODOT = INSTALLED["godot"]
SQLITE_PROJECT = ROOT / "spikes/toolchain/SqliteProbe/SqliteProbe.csproj"
GODOT_PROJECT = ROOT / "spikes/toolchain/GodotSmoke/GodotSmoke.csproj"
ONLINE_CONFIG = ROOT / "spikes/toolchain/NuGet.Config"
OFFLINE_CONFIG = ROOT / "toolchain/NuGet.offline.config"


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def run(name, argv, *, env=ENV, expected_failure=False, timeout=180):
    print("RUN " + name, flush=True)
    result = subprocess.run(argv, cwd=ROOT, env=env, capture_output=True, timeout=timeout)
    stdout = result.stdout.decode("utf-8", errors="replace")
    stderr = result.stderr.decode("utf-8", errors="replace")
    log = OUT / (name + ".log")
    log.write_text(json.dumps({"argv": argv, "cwd": str(ROOT), "exit_code": result.returncode}) +
                   "\n[stdout]\n" + stdout + "\n[stderr]\n" + stderr, encoding="utf-8")
    record = {"argv": argv, "cwd": str(ROOT), "exit_code": result.returncode,
              "output_path": log.relative_to(ROOT).as_posix()}
    if expected_failure:
        record["expected_nonzero_proven"] = result.returncode != 0 and "NU1100" in stdout + stderr
        record["expected_failure_reason"] = "Empty package cache and an explicitly empty source list must report NU1100."
    COMMANDS.append(record)
    write_json(OUT / "commands.json", COMMANDS)
    print(f"RESULT {name}: {result.returncode}", flush=True)
    if expected_failure:
        if not record["expected_nonzero_proven"]:
            raise RuntimeError(f"Unexpected offline failure result: {stdout}\n{stderr}")
    elif result.returncode != 0:
        raise RuntimeError(f"{name} failed: {stdout}\n{stderr}")
    if name == "windows_export" and "ERROR:" in stderr:
        raise RuntimeError(f"Godot reported an export error despite exit code zero: {stderr}")
    return stdout, stderr


def audit(project, label):
    stdout, _ = run(label, [DOTNET, "list", str(project), "package", "--vulnerable", "--include-transitive", "--format", "json"])
    report = json.loads(stdout[stdout.index("{"):])
    write_json(OUT / (label + ".json"), report)
    findings = []
    for item in report.get("projects", []):
        for framework in item.get("frameworks", []):
            for key in ["topLevelPackages", "transitivePackages"]:
                findings.extend(p for p in framework.get(key, []) if p.get("vulnerabilities"))
    if findings:
        raise RuntimeError(f"Known vulnerable dependencies found: {findings}")
    return {"known_vulnerable_packages": len(findings), "report": (OUT / (label + ".json")).relative_to(ROOT).as_posix()}


def export_probe():
    solution = GODOT_PROJECT.with_suffix(".sln")
    if not solution.exists():
        run("godot_solution_create", [DOTNET, "new", "sln", "--format", "sln", "--name", "GodotSmoke", "--output", str(GODOT_PROJECT.parent)])
        run("godot_solution_add", [DOTNET, "sln", str(solution), "add", str(GODOT_PROJECT)])
    export = OUT / "export"
    export.mkdir()
    executable = export / "MistSeaToolchainSmoke.exe"
    run("windows_export", [GODOT, "--headless", "--path", str(GODOT_PROJECT.parent),
                            "--export-release", "Windows Desktop", str(executable)], timeout=300)
    assert executable.is_file()
    assert list(export.rglob("coreclr.dll")), "The exported program must include its .NET runtime."
    clean_environment = os.environ.copy()
    for key in ["DOTNET_ROOT", "DOTNET_ROOT_X64", "NUGET_PACKAGES", "DOTNET_CLI_HOME"]:
        clean_environment.pop(key, None)
    exported, _ = run("windows_export_runtime", [str(executable), "--headless", "--quit-after", "20"], env=clean_environment)
    assert "MISTSEA_CX002_CSHARP_OK" in exported, exported
    return executable


def main():
    version, _ = run("dotnet_version", [DOTNET, "--version"])
    assert version.strip() == "10.0.401", version
    run("dotnet_info", [DOTNET, "--info"])
    engine, _ = run("godot_version", [GODOT, "--version"])
    assert engine.strip().startswith("4.7.2.stable.mono"), engine
    run("sqlite_restore", [DOTNET, "restore", str(SQLITE_PROJECT), "--configfile", str(ONLINE_CONFIG), "--force-evaluate"])
    run("sqlite_build", [DOTNET, "build", str(SQLITE_PROJECT), "-c", "Release", "--no-restore"])
    probe_dll = SQLITE_PROJECT.parent / "bin/Release/net10.0/win-x64/SqliteProbe.dll"
    sqlite_output, _ = run("sqlite_native_probe", [DOTNET, str(probe_dll), str(OUT / "sqlite")])
    sqlite = json.loads(sqlite_output[sqlite_output.index("{"):])
    write_json(OUT / "sqlite_result.json", sqlite)
    assert sqlite["process_architecture"] == "X64"
    sqlite_audit = audit(SQLITE_PROJECT, "sqlite_vulnerability_audit")
    run("godot_restore", [DOTNET, "restore", str(GODOT_PROJECT), "--configfile", str(ONLINE_CONFIG), "--force-evaluate"])
    run("godot_build", [DOTNET, "build", str(GODOT_PROJECT), "-c", "Debug", "--no-restore"])
    run("godot_import", [GODOT, "--headless", "--editor", "--path", str(GODOT_PROJECT.parent), "--quit-after", "2"])
    runtime, _ = run("godot_csharp_runtime", [GODOT, "--headless", "--path", str(GODOT_PROJECT.parent), "--quit-after", "20"])
    assert "MISTSEA_CX002_CSHARP_OK" in runtime, runtime
    godot_audit = audit(GODOT_PROJECT, "godot_vulnerability_audit")
    for project, label in [(SQLITE_PROJECT, "sqlite_offline_cached"), (GODOT_PROJECT, "godot_offline_cached")]:
        run(label, [DOTNET, "restore", str(project), "--configfile", str(OFFLINE_CONFIG), "--locked-mode", "-p:NuGetAudit=false"])
    cold = ENV.copy()
    cold["NUGET_PACKAGES"] = str(OUT / "empty_package_cache")
    # Redirect generated assets, so the intentional failure cannot replace usable build metadata.
    run("sqlite_offline_empty", [DOTNET, "restore", str(SQLITE_PROJECT), "--configfile", str(OFFLINE_CONFIG),
                                "--locked-mode", "--force", "--no-cache", "-p:NuGetAudit=false",
                                "-p:MSBuildProjectExtensionsPath=" + str(OUT / "offline_obj") + os.sep],
        env=cold, expected_failure=True)
    executable = export_probe()
    locks = []
    for project in [SQLITE_PROJECT, GODOT_PROJECT]:
        path = project.parent / "packages.lock.json"
        lock = json.loads(path.read_text(encoding="utf-8"))
        locks.append({"path": path.relative_to(ROOT).as_posix(),
                      "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "contents": lock})
    provider = locks[0]["contents"]["dependencies"]["net10.0"]["Microsoft.Data.Sqlite"]
    assert provider["resolved"] == "10.0.12", provider
    write_json(OUT / "dependency_locks.json", locks)
    summary = {
        "scope": "CX-002 toolchain probes, not full game or clean-machine human acceptance",
        "dotnet_sdk": version.strip(), "godot": engine.strip(), "sqlite": sqlite,
        "audits": {"sqlite": sqlite_audit, "godot": godot_audit},
        "checks": {"exact_sdk": True, "matching_dotnet_editor": True, "sqlite_native_x64": True,
                   "sqlite_commit_rollback_backup": True, "csharp_build_and_runtime": True,
                   "dependency_locks_generated": True, "vulnerability_audits_run": True,
                   "cached_offline_restore": True, "empty_cache_offline_failure_explained": True,
                   "matching_windows_export": True, "export_runs_with_sdk_environment_removed": True},
        "export": str(executable), "commands": COMMANDS,
        "not_run": ["different clean Windows machine", "native IME", "screen reader", "production persistence tests", "audio listening", "human language calibration"],
    }
    write_json(OUT / "summary.json", summary)
    write_json(ROOT / "reports/execution/CX-002/latest_run.json", {"run_path": OUT.relative_to(ROOT).as_posix(), "result": "verified"})
    print(json.dumps({"verified": True, "run_path": OUT.relative_to(ROOT).as_posix(), "checks": summary["checks"]}, indent=2), flush=True)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        write_json(OUT / "failure.json", {"error": str(error), "commands": COMMANDS})
        write_json(ROOT / "reports/execution/CX-002/latest_run.json", {"run_path": OUT.relative_to(ROOT).as_posix(), "result": "failed"})
        raise

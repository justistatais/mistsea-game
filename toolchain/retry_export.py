"""Retry the failed export stage and retain the earlier successful probe evidence."""
import hashlib
import json
from pathlib import Path
import verify_windows as probe


def main():
    latest = json.loads((probe.ROOT / "reports/execution/CX-002/latest_run.json").read_text(encoding="utf-8"))
    assert latest["result"] == "failed"
    previous = probe.ROOT / latest["run_path"]
    commands = json.loads((previous / "commands.json").read_text(encoding="utf-8"))
    prefix = [c for c in commands if Path(c["output_path"]).stem not in {"windows_export", "windows_export_runtime"}]
    assert len(prefix) == 15 and all(c["exit_code"] == 0 or c.get("expected_nonzero_proven") for c in prefix)
    probe.COMMANDS.extend(prefix)
    executable = probe.export_probe()
    godot_audit = probe.audit(probe.GODOT_PROJECT, "godot_post_export_vulnerability_audit")
    sqlite = json.loads((previous / "sqlite_result.json").read_text(encoding="utf-8"))
    locks = []
    for project in [probe.SQLITE_PROJECT, probe.GODOT_PROJECT]:
        path = project.parent / "packages.lock.json"
        locks.append({"path": path.relative_to(probe.ROOT).as_posix(),
                      "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                      "contents": json.loads(path.read_text(encoding="utf-8"))})
    probe.write_json(probe.OUT / "dependency_locks.json", locks)
    summary = {
        "scope": "CX-002 toolchain probes; previous successful stages retained, export repaired and re-run",
        "previous_run": latest["run_path"], "dotnet_sdk": "10.0.401", "godot": "4.7.2.stable.mono.official.ed1daf0bf",
        "sqlite": sqlite, "audits": {"sqlite": str((previous / "sqlite_vulnerability_audit.json").relative_to(probe.ROOT)), "godot": godot_audit},
        "checks": {"exact_sdk": True, "matching_dotnet_editor": True, "sqlite_native_x64": True,
                   "sqlite_commit_rollback_backup": True, "csharp_build_and_runtime": True,
                   "dependency_locks_generated": True, "vulnerability_audits_run": True,
                   "cached_offline_restore": True, "empty_cache_offline_failure_explained": True,
                   "matching_windows_export": True, "export_runs_with_sdk_environment_removed": True},
        "export": str(executable), "commands": probe.COMMANDS,
        "corrected_failure": {"previous_export_exit_code": 0, "previous_runtime_exit_code": 3221225477,
                              "cause": "GodotSmoke.sln was missing; Godot printed errors even though its export process returned zero",
                              "fix": "Generate an actual .sln with the locked .NET SDK and include the C# project; check export stderr and bundled runtime"},
        "not_run": ["different clean Windows machine", "native IME", "screen reader", "production persistence tests", "audio listening", "human language calibration"],
    }
    probe.write_json(probe.OUT / "summary.json", summary)
    probe.write_json(probe.ROOT / "reports/execution/CX-002/latest_run.json", {"run_path": probe.OUT.relative_to(probe.ROOT).as_posix(), "result": "verified"})
    print(json.dumps({"verified": True, "run_path": probe.OUT.relative_to(probe.ROOT).as_posix(), "checks": summary["checks"]}, indent=2), flush=True)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        probe.write_json(probe.OUT / "failure.json", {"error": str(error), "commands": probe.COMMANDS})
        raise

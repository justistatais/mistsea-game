from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
from build_probe import ROOT, ENV, installed


def main():
    task, relative, name, flag = sys.argv[1:5]
    out = ROOT / "reports/execution" / task / datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    out.mkdir(parents=True)
    export = out / "export"
    export.mkdir()
    executable = export / (name + ".exe")
    commands = []

    def run(label, argv, environment, expected_missing=False, expected_corrupt=False):
        print("RUN " + label, flush=True)
        r = subprocess.run(argv, cwd=ROOT, env=environment, capture_output=True, timeout=300)
        stdout, stderr = r.stdout.decode("utf8", errors="replace"), r.stderr.decode("utf8", errors="replace")
        log = out / (label + ".log")
        log.write_text(json.dumps({"argv": argv, "cwd": str(ROOT), "exit_code": r.returncode}) + "\n[stdout]\n" + stdout + "\n[stderr]\n" + stderr, encoding="utf8")
        record = {"argv": argv, "cwd": str(ROOT), "exit_code": r.returncode, "output_path": log.relative_to(ROOT).as_posix()}
        if expected_missing:
            record.update(expected_nonzero_proven=r.returncode == 3 and "MISTSEA_STORAGE_ERROR" in stderr and "e_sqlite3" in stderr,
                          expected_failure_reason="Export copy intentionally omits e_sqlite3.dll; explicit native-library diagnostic is required.")
        if expected_corrupt:
            record.update(expected_nonzero_proven=r.returncode == 4 and "MISTSEA_CANDIDATE_READ_ERROR" in stderr,
                          expected_failure_reason="Existing database contains invalid bytes; must diagnose without resetting it.")
        commands.append(record)
        (out / "commands.json").write_text(json.dumps(commands, ensure_ascii=False, indent=2), encoding="utf8")
        print("EXIT " + str(r.returncode), flush=True)
        if expected_missing or expected_corrupt:
            assert record["expected_nonzero_proven"], stdout + stderr
        else:
            assert r.returncode == 0 and "ERROR:" not in stderr, stdout + stderr
        return stdout

    env = ENV.copy()
    run("export", [installed["godot"], "--headless", "--path", str(ROOT / relative), "--export-release", "Windows Desktop", str(executable)], env)
    assert list(export.rglob("coreclr.dll"))
    clean = ENV.copy()
    for key in ["DOTNET_ROOT", "DOTNET_ROOT_X64", "NUGET_PACKAGES", "DOTNET_CLI_HOME"]: clean.pop(key, None)
    clean["PATH"] = str(Path(installed["dotnet_root"]).parent.parent.parent) + ";C:\\Windows\\system32;C:\\Windows"
    clean["MISTSEA_PROBE_OUTPUT"] = str(out)
    stdout = run("export_runtime", [str(executable), "--headless", "--", flag], clean)
    if flag == "--candidate-save":
        run("reopen_saved_draft", [str(executable), "--headless", "--", "--candidate-read"], clean)
        damaged = out / "corrupt_database"
        damaged.mkdir()
        corrupt_file = damaged / "draft.sqlite"
        corrupt_file.write_bytes(b"intentional-invalid-SQLite-fixture")
        before_hash = hashlib.sha256(corrupt_file.read_bytes()).hexdigest()
        corrupt_env = clean.copy()
        corrupt_env["MISTSEA_PROBE_OUTPUT"] = str(damaged)
        run("corrupt_database_diagnostic", [str(executable), "--headless", "--", "--candidate-read"], corrupt_env, expected_corrupt=True)
        assert hashlib.sha256(corrupt_file.read_bytes()).hexdigest() == before_hash
    if flag == "--storage-test":
        broken = out / "missing_native"
        shutil.copytree(export, broken, ignore=lambda directory, names: [n for n in names if n.lower() == "e_sqlite3.dll"])
        assert not list(broken.rglob("e_sqlite3.dll"))
        run("missing_native_diagnostic", [str(broken / executable.name), "--headless", "--", "--native-diagnostic"], clean, True)
    files = [{"path": p.relative_to(ROOT).as_posix(), "bytes": p.stat().st_size,
              "sha256": hashlib.sha256(p.read_bytes()).hexdigest()} for p in sorted(export.rglob("*")) if p.is_file()]
    result = {"task_id": task, "result": "verified", "commands": commands, "files": files,
              "native_files": [f for f in files if f["path"].endswith("e_sqlite3.dll")],
              "runtime_bundled": True, "sdk_environment_removed": True, "runtime_output": stdout,
              "different_clean_windows_machine": "not_run"}
    (out / "export_result.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf8")
    (ROOT / "reports/execution" / task / "latest_export.json").write_text(json.dumps({"path": out.relative_to(ROOT).as_posix(), "result": "verified"}), encoding="utf8")
    print(json.dumps({"path": out.relative_to(ROOT).as_posix(), "result": "verified", "native_files": result["native_files"]}, ensure_ascii=False), flush=True)


if __name__ == "__main__": main()

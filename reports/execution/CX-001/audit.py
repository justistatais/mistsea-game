"""Capture CX-001 evidence only; never installs tools or implements game tasks.

Run initial before writing a task report or changing execution state. Run final
after the engineering review. Logs record real subprocess outputs and exit codes.
"""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import sqlite3
import subprocess
import sys
from datetime import datetime, timedelta, timezone
import zipfile

ROOT = Path(__file__).resolve().parents[3]
OUT = Path(__file__).resolve().parent
SHANGHAI = timezone(timedelta(hours=8))


def stamp():
    return datetime.now(SHANGHAI).isoformat(timespec="seconds")


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def command(argv, name, records, cwd=ROOT):
    env = os.environ.copy()
    env.update(PYTHONDONTWRITEBYTECODE="1", PYTHONIOENCODING="utf-8")
    started = stamp()
    completed = subprocess.run(argv, cwd=cwd, env=env, capture_output=True, timeout=45)
    stdout = completed.stdout.decode("utf-8", errors="replace")
    stderr = completed.stderr.decode("utf-8", errors="replace")
    log = OUT / (name + ".log")
    log.write_text(
        json.dumps({"argv": argv, "cwd": str(cwd), "started_at": started,
                    "exit_code": completed.returncode}, ensure_ascii=False) +
        "\n[stdout]\n" + stdout + "\n[stderr]\n" + stderr,
        encoding="utf-8",
    )
    records.append({"argv": argv, "cwd": str(cwd), "exit_code": completed.returncode,
                    "output_path": log.relative_to(ROOT).as_posix(), "started_at": started})
    return completed, stdout, stderr


def repository(records, stage):
    git = shutil.which("git")
    if not git:
        return {"available": False, "files": []}
    proc, stdout, _ = command([git, "rev-parse", "--show-toplevel"], stage + "_git_root", records)
    if proc.returncode:
        return {"available": False, "files": []}
    parent = Path(stdout.strip())
    _, commit, _ = command([git, "rev-parse", "HEAD"], stage + "_git_head", records, parent)
    _, status, _ = command([git, "status", "--porcelain=v1", "--untracked-files=normal"],
                           stage + "_git_status", records, parent)
    proc, _, _ = command([git, "ls-files", "-z"], stage + "_git_files", records, parent)
    names = [s.decode("utf-8") for s in proc.stdout.split(b"\0") if s]
    files = [{"path": name, "sha256": digest(parent / name) if (parent / name).is_file() else None}
             for name in names]
    return {"available": True, "root": str(parent), "head": commit.strip(),
            "status": status.splitlines(), "tracked_file_count": len(files), "files": files,
            "project_root": str(ROOT), "child_has_own_git": (ROOT / ".git").exists(),
            "parent_top_level": sorted(p.name for p in parent.iterdir()),
            "ignored_files_content_read": False}


def inventory():
    source = read_json(ROOT / "planning/source_inventory.json")
    collection = read_json(ROOT / "COLLECTION_MANIFEST.json")
    rows = []
    for name, expected in collection["entries"].items():
        path = ROOT / name
        actual = digest(path) if path.is_file() else None
        rows.append({"path": name, "expected_sha256": expected["sha256"],
                     "actual_sha256": actual, "bytes": path.stat().st_size if path.is_file() else None,
                     "matched": actual == expected["sha256"] and path.stat().st_size == expected["size"]})
    docs = []
    for doc in source["documents"]:
        path = ROOT / doc["path"]
        headers = path.read_text(encoding="utf-8").splitlines()[:18]
        version = next((line for line in headers if "版本" in line), None)
        docs.append({**doc, "observed_version_header": version,
                     "verified_sha256": digest(path), "matched": digest(path) == doc["sha256"]})
    archives = []
    for archive in source["archives"]:
        path = ROOT / "deliverables/original_part_packages" / archive["original_archive"]
        members = [{"path": member["path"], "expected_sha256": member["sha256"],
                    "actual_sha256": digest(ROOT / member["path"])} for member in archive["members"]]
        with zipfile.ZipFile(path) as zipped:
            corrupt = zipped.testzip()
            entry_count = len(zipped.infolist())
        archives.append({"part": archive["part"], "version": "v0.1",
                         "archive": path.relative_to(ROOT).as_posix(),
                         "expected_sha256": archive["archive_sha256"], "actual_sha256": digest(path),
                         "archive_hash_matched": digest(path) == archive["archive_sha256"],
                         "crc_error": corrupt, "zip_entries": entry_count,
                         "reference_root": archive["extracted_root"], "reference_count": len(members),
                         "reference_members": members,
                         "all_reference_hashes_matched": all(m["expected_sha256"] == m["actual_sha256"] for m in members)})
    outer = Path("D:/Downloads/MistSea_Full_Design_and_Codex_v0.1.zip")
    with zipfile.ZipFile(outer) as zipped:
        outer_crc = zipped.testzip()
        outer_count = len(zipped.infolist())
    files = [p for p in ROOT.rglob("*") if p.is_file()]
    return {"captured_at": stamp(), "baseline": "MASTER-GDD-0.1", "documents": docs,
            "archives": archives, "collection_entries": rows,
            "outer_archive": {"path": str(outer), "bytes": outer.stat().st_size,
                              "sha256": digest(outer), "crc_error": outer_crc, "entries": outer_count},
            "assets": {"audio_files": [p.relative_to(ROOT).as_posix() for p in files if p.suffix.lower() in
                                        {".wav", ".mp3", ".ogg", ".flac", ".m4a", ".aac", ".opus", ".aiff", ".wma"}],
                       "font_files": [p.relative_to(ROOT).as_posix() for p in files if p.suffix.lower() in
                                       {".ttf", ".otf", ".woff", ".woff2"}],
                       "godot_projects": [p.relative_to(ROOT).as_posix() for p in files if p.name == "project.godot"],
                       "csharp_projects": [p.relative_to(ROOT).as_posix() for p in files if p.suffix == ".csproj"]}}


def initial():
    target = OUT / "initial_audit.json"
    if target.exists() or (ROOT / "reports/execution/CX-001.json").exists():
        raise SystemExit("Initial evidence already exists; refusing to overwrite it.")
    records = []
    state = read_json(ROOT / "planning/state/execution.json")
    write_json(OUT / "state_before.json", state)
    repo = repository(records, "before")
    write_json(OUT / "repository_before.json", repo)
    # Bootstrap tests intentionally require an untouched task ledger and no top-level task report.
    results = {}
    for name, args in [
        ("verify_handoff_initial", ["tools/verify_handoff.py"]),
        ("planning_tests_initial", ["-m", "unittest", "discover", "-s", "tools/tests", "-v"]),
        ("plan_status_initial", ["tools/plan.py", "status"]),
        ("plan_next_initial", ["tools/plan.py", "next", "--limit", "10"]),
        ("plan_show_initial", ["tools/plan.py", "show", "CX-001"]),
    ]:
        proc, stdout, stderr = command([sys.executable, "-B", *args], name, records)
        results[name] = {"exit_code": proc.returncode, "stdout": stdout, "stderr": stderr}
    inv = inventory()
    write_json(OUT / "source_inventory_verified.json", inv)
    environment = {"captured_at": stamp(), "os": platform.platform(), "os_architecture": platform.machine(),
                   "python": {"executable": sys.executable, "version": platform.python_version(),
                              "architecture": platform.architecture()[0], "sqlite_reference_version": sqlite3.sqlite_version},
                   "commands_on_path": {name: shutil.which(name) for name in
                                        ["python", "py", "git", "rg", "dotnet", "godot", "godot4", "node", "pnpm"]},
                   "godot": {"status": "not_found_on_PATH", "global_installation_audit": "not_performed"},
                   "dotnet_hosts": [], "sqlite_native_csharp": "not_verified",
                   "candidate_toolchain": read_json(ROOT / "reference/part12/data/toolchain_selection.json")}
    hosts = [shutil.which("dotnet"), "C:/Program Files/dotnet/dotnet.exe", "C:/Program Files (x86)/dotnet/dotnet.exe"]
    seen = set()
    for executable in hosts:
        if not executable or not Path(executable).is_file():
            continue
        key = str(Path(executable).resolve()).lower()
        if key in seen:
            continue
        seen.add(key)
        host = {"executable": executable}
        for flag, label in [("--info", "info"), ("--list-sdks", "sdks"), ("--list-runtimes", "runtimes")]:
            proc, stdout, stderr = command([executable, flag], "dotnet_" + str(len(seen)) + "_" + label, records)
            host[label] = {"exit_code": proc.returncode, "stdout": stdout, "stderr": stderr}
        environment["dotnet_hosts"].append(host)
    for name in ["git", "node", "pnpm"]:
        executable = shutil.which(name)
        if executable:
            proc, stdout, stderr = command([executable, "--version"], name + "_version", records)
            environment[name] = {"executable": executable, "exit_code": proc.returncode, "version": stdout.strip(), "stderr": stderr}
    write_json(OUT / "environment.json", environment)
    verify = json.loads(results["verify_handoff_initial"]["stdout"])
    tests = re.search(r"Ran (\d+) tests? in", results["planning_tests_initial"]["stderr"])
    checks = {
        "all_commands_succeeded": all(c["exit_code"] == 0 for c in records),
        "all_136_tasks_initially_not_started": len(state["tasks"]) == 136 and all(
            value["status"] == "not_started" and value["evidence"] is None for value in state["tasks"].values()),
        "cx001_first_ready": results["plan_next_initial"]["stdout"].splitlines()[0].startswith("CX-001 |"),
        "all_collection_entries_matched": all(r["matched"] for r in inv["collection_entries"]),
        "all_12_documents_matched": len(inv["documents"]) == 12 and all(r["matched"] for r in inv["documents"]),
        "all_10_archives_matched": len(inv["archives"]) == 10 and all(
            a["archive_hash_matched"] and a["crc_error"] is None and a["all_reference_hashes_matched"] for a in inv["archives"]),
        "outer_zip_crc_passed": inv["outer_archive"]["crc_error"] is None,
        "handoff_passed": verify["failed"] == 0,
        "planning_tests_passed": results["planning_tests_initial"]["exit_code"] == 0 and tests is not None,
    }
    summary = {"captured_at": stamp(), "phase": "initial_before_ledger_change", "checks": checks,
               "commands": records, "handoff": verify, "planning_test_count": int(tests.group(1)) if tests else None,
               "collection_count": len(inv["collection_entries"]), "source_reference_count": sum(a["reference_count"] for a in inv["archives"])}
    write_json(target, summary)
    print(json.dumps({k: v for k, v in summary.items() if k not in {"commands", "handoff"}}, ensure_ascii=False, indent=2))
    return 0 if all(checks.values()) else 1


def final():
    records = []
    before = read_json(OUT / "repository_before.json")
    after = repository(records, "after")
    write_json(OUT / "repository_after.json", after)
    initial_inv = read_json(OUT / "source_inventory_verified.json")
    immutable = [r for r in initial_inv["collection_entries"] if r["path"] != "planning/state/execution.json"]
    mismatches = [r["path"] for r in immutable if digest(ROOT / r["path"]) != r["expected_sha256"]]
    for name, args in [
        ("verify_handoff_final", ["tools/verify_handoff.py"]),
        ("plan_status_final", ["tools/plan.py", "status"]),
        ("plan_next_final", ["tools/plan.py", "next", "--limit", "10"]),
    ]:
        command([sys.executable, "-B", *args], name, records)
    sys.path.insert(0, str(ROOT / "tools"))
    from plan_lib import evidence_errors, schedule
    plan = read_json(ROOT / "planning/plan.json")
    state = read_json(ROOT / "planning/state/execution.json")
    old_state = read_json(OUT / "state_before.json")
    report = read_json(ROOT / "reports/execution/CX-001.json")
    task = next(t for t in plan["tasks"] if t["id"] == "CX-001")
    errors = evidence_errors(ROOT, task, report)
    scheduled = schedule(ROOT, plan, state)
    checks = {
        "parent_git_head_unchanged": before["head"] == after["head"],
        "all_parent_tracked_files_unchanged": before["files"] == after["files"],
        "all_immutable_delivered_files_unchanged": not mismatches,
        "only_cx001_ledger_entry_changed": all(state["tasks"][i] == v for i, v in old_state["tasks"].items() if i != "CX-001"),
        "cx001_evidence_valid": not errors,
        "scheduler_has_no_errors": not scheduled["errors"],
        "cx001_accepted": "CX-001" in scheduled["done"],
        "cx002_first_ready": bool(scheduled["ready"]) and scheduled["ready"][0] == "CX-002",
        "all_gates_still_closed": all(g["id"] not in scheduled["done"] for g in plan["gates"]),
        "all_commands_succeeded": all(c["exit_code"] == 0 for c in records),
    }
    result = {"captured_at": stamp(), "phase": "after_engineering_review", "checks": checks,
              "commands": records, "evidence_errors": errors, "schedule": scheduled,
              "immutable_files_checked": len(immutable), "immutable_mismatches": mismatches}
    write_json(OUT / "final_audit.json", result)
    print(json.dumps({k: v for k, v in result.items() if k not in {"commands", "schedule"}}, ensure_ascii=False, indent=2))
    return 0 if all(checks.values()) else 1


if __name__ == "__main__":
    if sys.argv[1:] == ["initial"]:
        raise SystemExit(initial())
    if sys.argv[1:] == ["final"]:
        raise SystemExit(final())
    raise SystemExit("Usage: python -B reports/execution/CX-001/audit.py initial|final")

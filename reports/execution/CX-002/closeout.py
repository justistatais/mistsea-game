"""Freeze measured CX-002 evidence, then update only its execution-ledger entry."""
from __future__ import annotations

import base64
from datetime import datetime
import hashlib
import json
from pathlib import Path
import platform
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]
EVIDENCE = ROOT / "reports/execution/CX-002"
sys.path.insert(0, str(ROOT / "tools"))
from plan_lib import evidence_errors, load_json, schedule


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def digest(path, algorithm="sha256"):
    h = hashlib.new(algorithm)
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def artifact(path):
    return {"path": path.relative_to(ROOT).as_posix(), "sha256": digest(path), "bytes": path.stat().st_size}


def check_reports():
    plan = load_json(ROOT / "planning/plan.json")
    state = load_json(ROOT / "planning/state/execution.json")
    by_id = {t["id"]: t for t in plan["tasks"]}
    errors = {ident: evidence_errors(ROOT, by_id[ident], load_json(ROOT / by_id[ident]["evidence_path"]))
              for ident in ["CX-001", "CX-002"]}
    scheduled = schedule(ROOT, plan, state)
    assert not any(errors.values()), errors
    assert not scheduled["errors"], scheduled["errors"]
    assert scheduled["done"] == ["CX-001", "CX-002"], scheduled["done"]
    assert scheduled["ready"][0] == "CX-003", scheduled["ready"]
    assert all(g["id"] not in scheduled["done"] for g in plan["gates"])
    trace = load_json(ROOT / "planning/acceptance_traceability.json")
    assert len(trace["requirements"]) == 478
    assert all(r["execution_status"] == "not_run_in_production" for r in trace["requirements"])
    return {"evidence_errors": errors, "completed_tasks": scheduled["done"],
            "next_engineering_task": "CX-003", "open_gates": 0,
            "product_requirements_not_run": 478, "scheduler_errors": scheduled["errors"]}


def main():
    if "--check" in sys.argv:
        print(json.dumps(check_reports(), ensure_ascii=False, indent=2))
        return
    now = datetime.now().astimezone().isoformat()
    plan = load_json(ROOT / "planning/plan.json")
    task = next(t for t in plan["tasks"] if t["id"] == "CX-002")
    state = load_json(ROOT / "planning/state/execution.json")
    assert state["tasks"]["CX-002"]["status"] == "in_progress"
    latest = load_json(EVIDENCE / "latest_run.json")
    assert latest["result"] == "verified"
    run_path = ROOT / latest["run_path"]
    summary = load_json(run_path / "summary.json")
    write(EVIDENCE / "verified_run.json", latest)
    first = ROOT / summary["previous_run"]
    installed = load_json(ROOT / "toolchain/installed.windows-x64.json")
    commands = list(summary["commands"])
    assert all(c["exit_code"] == 0 or c.get("expected_nonzero_proven") for c in commands)
    assert "10.0.401" in (first / "dotnet_version.log").read_text(encoding="utf8")
    assert "4.7.2.stable.mono.official.ed1daf0bf" in (first / "godot_version.log").read_text(encoding="utf8")
    runtime_log = (run_path / "windows_export_runtime.log").read_text(encoding="utf8")
    assert "MISTSEA_CX002_CSHARP_OK 4.7.2-stable (official) .NET 10.0.12 X64" in runtime_log
    assert "ERROR:" not in (run_path / "windows_export.log").read_text(encoding="utf8")
    assert summary["sqlite"]["rollback_verified"] and summary["sqlite"]["commit_verified"]
    assert summary["sqlite"]["backup_verified"] and summary["sqlite"]["integrity_check"] == "ok"
    assert digest(Path(installed["dotnet"])) == installed["binary_hashes"]["dotnet_sha256"]
    assert digest(Path(installed["godot"])) == installed["binary_hashes"]["godot_sha256"]
    assert all(digest(Path(t["path"])) == t["sha256"] for t in installed["windows_templates"])
    assert all(digest(Path(t["path"])) == t["sha256"] for t in summary["sqlite"]["native_files"])
    export_dir = Path(summary["export"]).parent
    assert list(export_dir.rglob("coreclr.dll"))

    locked_packages, package_records = {}, []
    lock_files = [ROOT / f"spikes/toolchain/{name}/packages.lock.json" for name in ["SqliteProbe", "GodotSmoke"]]
    for lock_file in lock_files:
        for framework, dependencies in load_json(lock_file)["dependencies"].items():
            for name, record in dependencies.items():
                key = (name, record["resolved"])
                assert key not in locked_packages or locked_packages[key] == record["contentHash"]
                locked_packages[key] = record["contentHash"]
    sdk_metadata = load_json(Path(installed["nuget_packages"]) / "godot.net.sdk/4.7.2/.nupkg.metadata")
    locked_packages[("Godot.NET.Sdk", "4.7.2")] = sdk_metadata["contentHash"]
    for (name, version), expected in sorted(locked_packages.items()):
        folder = Path(installed["nuget_packages"]) / name.lower() / version
        nupkg = folder / f"{name.lower()}.{version}.nupkg"
        actual = base64.b64encode(bytes.fromhex(digest(nupkg, "sha512"))).decode()
        metadata = load_json(folder / ".nupkg.metadata")
        # NuGet contentHash omits the signature context for signed packages;
        # the .nupkg.sha512 file instead hashes the complete downloaded archive.
        assert actual == nupkg.with_suffix(".nupkg.sha512").read_text().strip(), name
        assert expected == metadata["contentHash"], name
        assert metadata["source"] == "https://api.nuget.org/v3/index.json"
        package_records.append({"name": name, "version": version, "source": metadata["source"],
                                "url": f"https://api.nuget.org/v3-flatcontainer/{name.lower()}/{version}/{nupkg.name}",
                                "sha256": digest(nupkg), "archive_sha512_base64": actual,
                                "nuget_content_hash": expected,
                                "bytes": nupkg.stat().st_size, "local_archive": str(nupkg),
                                "role": "MSBuild SDK" if name == "Godot.NET.Sdk" else "restored dependency"})
    assert all(v == "4.7.2" for (n, v) in locked_packages if n.startswith("Godot"))
    assert ("Microsoft.Data.Sqlite", "10.0.12") in locked_packages
    for audit_path in [first / "sqlite_vulnerability_audit.json", run_path / "godot_post_export_vulnerability_audit.json"]:
        audit = load_json(audit_path)
        assert audit["sources"] == ["https://api.nuget.org/v3/index.json"]
        assert audit["projects"]
        for project in audit["projects"]:
            for framework in project.get("frameworks", []):
                for group in ["topLevelPackages", "transitivePackages"]:
                    assert all(not p.get("vulnerabilities") for p in framework.get(group, []))
    write(ROOT / "toolchain/toolchain.lock.json", {
        "schema_version": "0.1", "task_id": "CX-002", "verified_at": now,
        "baseline": load_json(ROOT / "planning/toolchain_baseline.json"),
        "measured": {"godot_build": summary["godot"], "dotnet_sdk": summary["dotnet_sdk"],
                     "framework": summary["sqlite"]["framework"], "sqlite_native": summary["sqlite"]["sqlite_version"],
                     "architecture": "X64"},
        "downloads": load_json(EVIDENCE / "download_verification.json"),
        "binary_hashes": installed["binary_hashes"], "windows_templates": installed["windows_templates"],
        "nuget_packages": package_records, "dependency_locks": [artifact(p) for p in lock_files],
        "sdk_lock": artifact(ROOT / "global.json"), "central_package_versions": artifact(ROOT / "Directory.Packages.props"),
        "exported_files": [artifact(p) for p in sorted(export_dir.rglob("*")) if p.is_file()],
        "offline_policy": "Locked restore requires pre-populated package cache; network audit recorded separately.",
        "provenance": "Download hashes checked against official release metadata by install_windows.py; complete package archives checked against cache .sha512 files, NuGet contentHash checked against restored locks and metadata. Locked restore logs provide NuGet content validation.",
        "nuget_hash_semantics_source": "https://github.com/NuGet/Home/wiki/Nupkg-Metadata-File"})

    def run(name, argv):
        result = subprocess.run(argv, cwd=ROOT, capture_output=True)
        stdout, stderr = result.stdout.decode("utf8", errors="replace"), result.stderr.decode("utf8", errors="replace")
        log = EVIDENCE / (name + ".log")
        log.write_text(json.dumps({"argv": argv, "cwd": str(ROOT), "exit_code": result.returncode}) +
                       "\n[stdout]\n" + stdout + "\n[stderr]\n" + stderr, encoding="utf8")
        commands.append({"argv": argv, "cwd": str(ROOT), "exit_code": result.returncode, "output_path": log.relative_to(ROOT).as_posix()})
        assert result.returncode == 0, stderr
        return stdout

    git = shutil.which("git")
    assert git
    parent_before = load_json(ROOT / "reports/execution/CX-001/repository_before.json")
    parent = Path(parent_before["root"])
    head = run("git_head", [git, "rev-parse", "HEAD"]).strip()
    assert head == parent_before["head"]
    run("git_status", [git, "status", "--porcelain=v1", "--untracked-files=normal"])
    assert all(digest(parent / f["path"]) == f["sha256"] for f in parent_before["files"])
    collection = load_json(ROOT / "COLLECTION_MANIFEST.json")["entries"]
    immutable = {p: m for p, m in collection.items() if p != "planning/state/execution.json"}
    assert all(digest(ROOT / p) == m["sha256"] for p, m in immutable.items())
    handoff = json.loads(run("verify_handoff", [sys.executable, "-B", "-X", "utf8", "tools/verify_handoff.py"]))
    assert handoff["passed"] == 15 and handoff["failed"] == 0
    write(EVIDENCE / "preservation.json", {"parent_head": head, "parent_tracked_files_unchanged": len(parent_before["files"]),
                                          "immutable_collection_files_unchanged": len(immutable), "handoff": handoff})
    write(EVIDENCE / "offline_and_platform.json", {
        "platform": platform.platform(), "architecture": platform.machine(), "verified": "Windows x86_64 on this machine",
        "offline_cached": [c for c in commands if Path(c["output_path"]).stem.endswith("offline_cached")],
        "offline_empty_cache": [c for c in commands if Path(c["output_path"]).stem.endswith("offline_empty")],
        "failure_explanation": "No package source and no cache: NuGet reports NU1100; pre-populate the cache by an online restore before offline use.",
        "not_run": summary["not_run"] + ["Linux/macOS/mobile builds; outside the Windows baseline"],
        "clean_machine_limit": "SDK environment removed and bundled runtime verified locally; a separate clean Windows machine has not been tested."})
    reviewer = "Codex /root（工具链工程自检，非真人签核）"
    review = {"required_role": task.get("owner", "Codex工程实现"), "reviewer": reviewer, "decision": "accepted",
              "is_synthetic": False, "evidence": ["reports/execution/CX-002/engineering_review.json", latest["run_path"] + "/summary.json"]}
    write(EVIDENCE / "engineering_review.json", {"task_id": "CX-002", "reviewed_at": now, "reviewer": reviewer,
        "decision": "accepted", "scope": "Exact toolchain, source/hash records, actual dependency locks, online package audits, native SQLite, C# runtime, Windows export and documented offline failure.",
        "checked": {"actual_command_logs": len(summary["commands"]), "package_archives_verified": len(package_records),
                    "editor_and_template_hashes_match_install": True, "parent_files_and_frozen_sources_preserved": True},
        "corrected_failure": summary["corrected_failure"], "human_signoff": None,
        "limits": summary["not_run"], "gate_or_product_acceptance": "not granted"})
    prospective = json.loads(json.dumps(state))
    prospective["tasks"]["CX-002"] = {"status": "accepted", "evidence": "reports/execution/CX-002.json",
        "review": {"reviewer": reviewer, "decision": "accepted", "reviewed_at": now}}
    prospective["note"] = "CX-001和CX-002已按真实工程证据接受；其他任务未开始，所有Gate仍关闭。"
    original = load_json(EVIDENCE / "state_before.json")
    assert all(prospective["tasks"][k] == v for k, v in original["tasks"].items() if k != "CX-002")
    write(EVIDENCE / "state_after.json", prospective)
    write(EVIDENCE / "working_tree.json", {"parent_git_head": head, "mistsea_has_own_git": (ROOT / ".git").exists(),
        "change_scope": load_json(EVIDENCE / "scope_notes.json"), "parent_tracked_files_unchanged": len(parent_before["files"]),
        "new_source_files": [artifact(p) for p in sorted(ROOT.glob("toolchain/*")) if p.is_file()],
        "ledger_snapshot": artifact(EVIDENCE / "state_after.json"),
        "source_commit_meaning": "Existing parent repository snapshot; no MistSea implementation commit was created."})
    report = load_json(ROOT / "planning/templates/Task_Report.template.json")
    report.update({"task_id": "CX-002", "result": "accepted", "source_commit": head,
        "working_tree_manifest": "reports/execution/CX-002/working_tree.json", "build_id": digest(Path(summary["export"])),
        "executed_at": now, "baseline": "MASTER-GDD-0.1", "commands": commands,
        "environment": {"os": platform.platform(), "architecture": platform.machine(), "godot": summary["godot"],
                        "dotnet": {"sdk": summary["dotnet_sdk"], "framework": summary["sqlite"]["framework"], "path": installed["dotnet"]},
                        "sqlite_native": summary["sqlite"]},
        "acceptance_results": [{"requirement": a, "status": "passed", "evidence": "toolchain/toolchain.lock.json", "detail": d}
            for a, d in zip(task["acceptance"], ["实际版本与候选基线一致；保留原基线文件。", "Godot编辑器、模板、MSBuild SDK及GodotSharp同为4.7.2；SQLite单独锁定。", "实际安装、编译、运行与导出日志和哈希已保存。", "按指定版本安装，未追逐最新版或变更基线。"] )],
        "not_run": summary["not_run"] + ["其余134张任务卡", "完整TECH-AT-01产品验收与G0依赖", "原始计划测试含接管初始状态断言；本轮采用当前账本的证据、调度和冻结来源检查"],
        "blocked_reasons": [], "review": review, "test_results": summary["checks"],
        "failed_attempts": [{"run_path": summary["previous_run"], **summary["corrected_failure"],
                             "logs": [summary["previous_run"] + "/windows_export.log", summary["previous_run"] + "/windows_export_runtime.log"],
                             "resolved_by": latest["run_path"]}],
        "notes": "accepted仅对应CX-002工具链工程自检。修复前的真实导出失败单独保留，验收命令列使用已成功阶段与修复后的复测；空缓存离线缺包为有日志支持的预期失败。干净Windows、真人、Gate和产品验收未签核。所有项目变更位于新游戏子文件夹。"})

    def refresh_artifacts():
        paths = {p for p in EVIDENCE.rglob("*") if p.is_file() and "offline_obj" not in p.parts and "__pycache__" not in p.parts}
        paths.discard(EVIDENCE / "closeout_console.log")
        paths.discard(EVIDENCE / "latest_run.json")
        paths.update(p for p in (ROOT / "toolchain").glob("*") if p.is_file())
        paths.update(ROOT / p for p in ["global.json", "Directory.Packages.props", "spikes/toolchain/NuGet.Config"])
        for name in ["GodotSmoke", "SqliteProbe"]:
            paths.update(p for p in (ROOT / "spikes/toolchain" / name).glob("*") if p.is_file())
        report["artifacts"] = [artifact(p) for p in sorted(paths)]
        report["commands"] = commands
        write(ROOT / "reports/execution/CX-002.json", report)

    refresh_artifacts()
    assert not evidence_errors(ROOT, task, report)
    (ROOT / "planning/state/execution.json").write_bytes((EVIDENCE / "state_after.json").read_bytes())
    run("plan_status", [sys.executable, "-B", "-X", "utf8", "tools/plan.py", "status"])
    run("plan_next", [sys.executable, "-B", "-X", "utf8", "tools/plan.py", "next", "--kind", "engineering", "--limit", "1"])
    final_checks = check_reports()
    write(EVIDENCE / "final_checks.json", final_checks)
    refresh_artifacts()
    print(json.dumps(check_reports(), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

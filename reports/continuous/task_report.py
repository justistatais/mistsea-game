"""Freeze engineering task evidence. Human or unmet-gate acceptance is never inferred."""
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import platform
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools"))
from plan_lib import evidence_errors, load_json, schedule


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf8")


def artifact(path, origin=None):
    value = {"path": path.relative_to(ROOT).as_posix(), "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "bytes": path.stat().st_size}
    if origin is not None: value["source_path_at_execution"] = origin
    return value


def finish(task_id, sources, commands, acceptance, not_run, test_results, changes=None, *, partial=False, content_candidate=False):
    plan = load_json(ROOT / "planning/plan.json")
    task = next(t for t in plan["tasks"] if t["id"] == task_id)
    if content_candidate:
        assert partial, "Content candidates retain editorial/calibration dependencies and cannot self-accept."
        assert task["kind"] in {"content_authoring", "content_production", "chapter_integration", "content_integration", "chapter_qa"}, "Human review tasks cannot be signed by this helper."
    else:
        assert task["kind"] == "engineering", "Human tasks cannot be signed by this helper."
    assert len(acceptance) == len(task["acceptance"])
    assert all(c["exit_code"] == 0 or c.get("expected_nonzero_proven") for c in commands)
    assert partial or all(a["status"] == "passed" for a in acceptance), "Incomplete acceptance must stay implemented/blocked."
    assert all(a["status"] in {"passed", "pending", "not_run", "failed"} for a in acceptance)
    state = load_json(ROOT / "planning/state/execution.json")
    assert state["tasks"][task_id]["status"] != "accepted", "Do not rewrite accepted historical evidence."
    done = schedule(ROOT, plan, state)
    assert not done["errors"], done["errors"]
    pending = [dep for dep in task["depends_on"] if dep not in done["done"]]
    status = "implemented" if partial else "verified" if pending else "accepted"
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    out = ROOT / "reports/execution" / task_id / "evidence" / stamp
    out.mkdir(parents=True)
    artifacts = []
    for origin in sorted(set(sources)):
        path = ROOT / origin
        assert path.is_file(), origin
        target = out / "sources" / origin
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, target)
        artifacts.append(artifact(target, origin))
    for command in commands:
        path = ROOT / command["output_path"]
        assert path.is_file(), command
        artifacts.append(artifact(path))
    git = shutil.which("git")
    r = subprocess.run([git, "rev-parse", "HEAD"], cwd=ROOT, capture_output=True, check=True)
    (out / "git_head.log").write_bytes(r.stdout)
    head = r.stdout.decode().strip()
    artifacts.append(artifact(out / "git_head.log"))
    write(out / "state_before.json", state)
    reviewer = "Codex /root（工程自检，无真人签核）"
    review = {"required_role": task["owner_role"], "reviewer": reviewer, "decision": "partial" if partial else "accepted",
              "is_synthetic": False, "evidence": [(out / "engineering_review.json").relative_to(ROOT).as_posix()]}
    write(out / "engineering_review.json", {"task_id": task_id, "scope": task["objective"], "reviewer": reviewer,
        "decision": "partial_implementation" if partial else "engineering_checks_passed", "acceptance": acceptance, "test_results": test_results,
        "human_signoff": None, "pending_dependencies": pending, "not_run": not_run})
    state["tasks"][task_id] = {"status": status, "evidence": task["evidence_path"],
                               "review": {"reviewer": reviewer, "decision": "partial_implementation" if partial else "accepted" if not pending else "engineering_verified_pending_dependencies"}}
    state["note"] = "用户已授权连续开发；工程证据逐项记录，人工与发布Gate不代签。verified候选按reports/continuous/Scope_20261009.md继续开发。"
    write(out / "state_after.json", state)
    for filename in ["state_before.json", "state_after.json", "engineering_review.json"]: artifacts.append(artifact(out / filename))
    manifest = {"task_id": task_id, "parent_commit": head, "source_files": [a for a in artifacts if "source_path_at_execution" in a],
                "interface_schema_impact": changes or {"data_migration_needed": False}, "snapshot_reason": "Preserve actual source bytes while later authorized tasks evolve the working tree."}
    write(out / "working_tree.json", manifest)
    artifacts.append(artifact(out / "working_tree.json"))
    report = load_json(ROOT / "planning/templates/Task_Report.template.json")
    report.update(task_id=task_id, result=status, source_commit=head, working_tree_manifest=(out / "working_tree.json").relative_to(ROOT).as_posix(),
                  build_id=stamp, artifacts=artifacts, commands=commands, acceptance_results=acceptance,
                  not_run=not_run, blocked_reasons=[], review=review, test_results=test_results,
                  pending_dependencies=pending, executed_at=datetime.now(timezone.utc).isoformat(),
                  environment={"os": platform.platform(), "architecture": platform.machine(), "godot": "4.7.2.stable.mono.official.ed1daf0bf", "dotnet": "10.0.401 / net10.0", "sqlite_native": test_results.get("sqlite_version", "not_used_by_this_task")},
                  notes="真实工程检查与产物快照；尚缺任务验收项记implemented，全部工程项通过但缺依赖记verified。未声明任何真人或正式发布验收。source_commit定位既有父仓库，当前MistSea源码由快照标识。")
    write(ROOT / task["evidence_path"], report)
    if status == "accepted": assert not evidence_errors(ROOT, task, report)
    (ROOT / "planning/state/execution.json").write_bytes((out / "state_after.json").read_bytes())
    check = schedule(ROOT, plan, state)
    assert not check["errors"], check["errors"]
    print(json.dumps({"task_id": task_id, "status": status, "pending_dependencies": pending, "accepted_task_count": len(check["done"])}, ensure_ascii=False))
    return report

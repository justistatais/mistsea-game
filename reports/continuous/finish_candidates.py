"""Freeze named executed candidates, retaining incomplete acceptance and human gates."""
import json
import sys
from pathlib import Path
from task_report import ROOT, finish


# These rows have been reviewed against both their implementation and named tests.
# UI, later chapter production and human tests are deliberately not inferred.
ENGINEERING_COMPLETE = {25, 26, 27, 30, 33, 35, 36, 37, 38, 41, 42, 43, 44,
                        45, 46, 47, 48, 50, 52, 53, 54, 55, 56, 57, 58, 59,
                        62, 63, 68, 72}
GAPS = {
    23: "Native IME and actual assistive-technology input ownership matrix still requires execution.",
    24: "Camera, crowd behavior, dangerous-area confirmation and full native viewport matrix are incomplete.",
    28: "General lifecycle rules exist; later actual chapter producers and derived readiness are incomplete.",
    29: "Acquired-source projection and text reader exist; actual screen-reader session is pending.",
    31: "Real CAS and delayed ACK cases pass; real human IME composition remains untested.",
    34: "Exclusive NPC leases and explicit promises exist; full normal-campaign dialogue matrix is incomplete.",
    39: "Six entry points and branching slots exist; full normal-campaign loading and HUD matrix is incomplete.",
    40: "Frozen XP, attributes and exact thresholds pass; actual level-28 final chapter is not produced.",
    49: "Actual level-5 benchmark and SQLite tower candidate exist; actual human player validation is absent.",
    51: "Native battle and Unicode input evidence exists; final animation/read-screen and current tower UI matrix are incomplete.",
    60: "Actual route proof and safe geometry rules exist; later region producers, three native ship rooms and sailing UI are incomplete.",
    61: "Read-only work and agreement views exist; the complete preparation/transaction preview UI is incomplete.",
    64: "Headless entry/focus/font checks exist; the full 24-state native accessibility matrix is not executed.",
    65: "Normal prologue/first chapter candidate and actual service cases exist; full native continuous player route and human G3 are pending.",
    67: "Disabled gateway and read-only authorization ports exist; production authorization projection and optional native help UI are not connected.",
}


def main():
    if len(sys.argv) != 4:
        raise SystemExit("Usage: finish_candidates.py <passing-tests-report-dir> <client-report-dir> <content-report-dir>")
    folders = [ROOT / value for value in sys.argv[1:]]
    suite = json.loads((folders[0] / "test_results.json").read_text(encoding="utf8"))
    assert suite["executed"] > 0 and suite["failed"] == 0
    commands = []
    for folder in folders:
        result = json.loads((folder / "result.json").read_text(encoding="utf8"))
        assert result["result"] == "verified"
        commands += json.loads((folder / "commands.json").read_text(encoding="utf8"))
    plan = json.loads((ROOT / "planning/plan.json").read_text(encoding="utf8"))
    sources = []
    for base in ["src", "game", "tests/Mist.Tests", "tools/Mist.ContentCli", "schemas/sql", "docs/contracts"]:
        for path in (ROOT / base).rglob("*"):
            if path.is_file() and path.suffix in {".cs", ".csproj", ".sql", ".md"} and not any(part in {"bin", "obj", ".godot", "ReleaseReadiness"} for part in path.parts):
                sources.append(path.relative_to(ROOT).as_posix())
    sources += ["MistSea.sln", "global.json", "Directory.Build.props", "Directory.Packages.props", "tools/ci/build.py", "reports/continuous/Scope_20261009.md"]
    sources += ["content/source/campaign/first_entry.json", "content/source/campaign/production_cards.json", "content/source/chapter_one/chapter_one.json",
                "game/content/campaign/first_entry.json", "game/content/chapter_one/chapter_one.json", "game/content/runtime_catalog.json", "game/content/manifest.json", "game/content/compile_result.json", "content/manifests/material_history.json"]
    for folder in folders:
        sources += [p.relative_to(ROOT).as_posix() for p in folder.glob("*.json") if p.name != "commands.json"]
    for task in plan["tasks"]:
        if not task["id"].startswith("CX-"):
            continue
        number = int(task["id"][3:])
        if task["kind"] != "engineering" or not (23 <= number <= 65 or number in {67, 68, 72}):
            continue
        selected = [case for case in suite["results"] if case["task"] == task["id"]]
        assert selected or number in {28, 31, 39, 51, 61, 64}, "Do not record an untested candidate: " + task["id"]
        complete = number in ENGINEERING_COMPLETE
        evidence = {"named_cases": selected, "test_report": (folders[0] / "test_results.json").relative_to(ROOT).as_posix(),
                    "client_checks": "Only the exact checks in the supplied client result files are claimed.",
                    "review_scope": "Current C# rules and named executed runtime/SQLite cases; future content and human performance are excluded."}
        acceptance = [{"criterion": criterion, "status": "passed" if complete else "pending",
                       "evidence": evidence if complete else {"implemented_evidence": evidence, "remaining": GAPS.get(number, "Incomplete production or native acceptance matrix; candidate only.")}}
                      for criterion in task["acceptance"]]
        not_run = ["Human HV gates, editorial/listening review, actual IELTS-style target calibration, rights approval and final publisher signature remain pending.",
                   "No real online supplier, final chapter player run, clean-machine qualification or physical power-loss experiment is claimed."]
        if not complete:
            not_run.append(GAPS.get(number, "Acceptance remains incomplete."))
        finish(task["id"], sources, commands, acceptance, not_run,
               {"task_cases": selected, "suite_executed": suite["executed"], "suite_failed": suite["failed"],
                "sqlite_version": suite["sqlite_version"], "dotnet_runtime": suite["dotnet_runtime"],
                "human_signoff": None},
               {"storage_schema": 2, "source_scope": "Frozen internal candidate; no formal release claim."}, partial=not complete)


if __name__ == "__main__":
    main()

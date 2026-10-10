"""Freeze executed extension evidence; unavailable production and human checks stay pending."""
import json
from task_report import ROOT,finish

RUNS={
    "CX-066":"reports/build/20261009T174133016254Z",
    "CX-070":"reports/build/20261009T174428031315Z",
    "CX-071":"reports/build/20261009T174534653251Z",
}
PERFORMANCE="reports/execution/CX-075/20261009T180052059860Z"
REVIEW="reports/execution/CX-071/20261009T175238595796Z"

def files(*bases):
    return [p.relative_to(ROOT).as_posix() for base in bases for p in (ROOT/base).rglob("*") if p.is_file() and p.suffix in {".cs",".py",".md"} and not {"obj","bin"}.intersection(p.parts)]

def main():
    plan=json.loads((ROOT/"planning/plan.json").read_text(encoding="utf8"))
    for task_id,run in RUNS.items():
        test=json.loads((ROOT/run/"test_results.json").read_text(encoding="utf8"));assert test["executed"]>0 and test["failed"]==0
        commands=json.loads((ROOT/run/"commands.json").read_text(encoding="utf8"))
        selected=[r for r in test["results"] if r["task"]==task_id];assert selected
        if task_id=="CX-066":
            source=files("src/Mist.Core/Assets","src/Mist.Application/Assets","tests/Mist.Tests/Assets")
            pending=["Actual replacement speech track without spoken role labels and human listening/rights review","Complete 360+360 lexicon, subtitle production and five-channel native volume/localization UI"]
        else:
            source=files("src/Mist.Core/ReviewCalibration","src/Mist.Application/ReviewCalibration","tests/Mist.Tests/ReviewCalibration","tools/Mist.ReviewCalibration")
            pending=["Native review/practice entry and actual reviewed full offline material routes","Human target-band calibration, unfamiliar tests and recorded listening tracks"]
        if task_id=="CX-071":
            commands+=json.loads((ROOT/REVIEW/"commands.json").read_text(encoding="utf8"))
            source+=[REVIEW+"/audit_result.json","tools/ci/review_audit.py"]
        source+=[run+"/test_results.json","reports/continuous/Scope_20261009.md","reports/continuous/finish_runtime_extensions.py"]
        task=next(t for t in plan["tasks"] if t["id"]==task_id)
        acceptance=[{"criterion":criterion,"status":"pending","evidence":{"executed_named_cases":selected,"remaining":pending}} for criterion in task["acceptance"]]
        finish(task_id,source,commands,acceptance,pending,{**test,"cli_scope":"source/protocol audit only; no real participant, band, external provider or human signature"},
               {"storage_schema":2,"new_modules":"read-only production/observation rules and exact original-source projection","human_approvals":"not granted"},partial=True)
    data=json.loads((ROOT/PERFORMANCE/"native_performance.json").read_text(encoding="utf8"));assert data["frames"]>60 and data["retained_fixture_heads"]==10 and data["experience"]==0
    commands=json.loads((ROOT/PERFORMANCE/"commands.json").read_text(encoding="utf8"))
    source=[PERFORMANCE+"/native_performance.json","tools/ci/native_performance.py","game/Diagnostics/Root.PerformanceCandidate.cs","game/Diagnostics/CampaignClient.PerformanceCandidate.cs","game/Root.cs","game/MistSea.csproj","reports/continuous/Scope_20261009.md","reports/continuous/finish_runtime_extensions.py"]
    task=next(t for t in plan["tasks"] if t["id"]=="CX-075")
    acceptance=[{"criterion":criterion,"status":"passed" if index==0 else "pending","evidence":{"native_candidate":PERFORMANCE,"remaining":data["not_run"]}} for index,criterion in enumerate(task["acceptance"])]
    finish("CX-075",source,commands,acceptance,data["not_run"],data,{"runtime":"native Windows Debug candidate, first scene plus real 9000-word editor; twenty explicit fixture draft saves","release_configuration":"Diagnostics compile-excluded"},partial=True)

if __name__=="__main__":main()

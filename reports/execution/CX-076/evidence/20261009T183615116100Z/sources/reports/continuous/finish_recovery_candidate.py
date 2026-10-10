"""Freeze the actual isolated Windows recovery matrix, retaining its physical/release limits."""
import hashlib
import json
from task_report import ROOT, finish

RUN = "reports/build/20261009T182406252977Z"

def main():
    result=json.loads((ROOT/RUN/"test_results.json").read_text(encoding="utf8"))
    cases=[case for case in result["results"] if case["task"]=="CX-076"]
    assert len(cases)==14 and all(case["result"]=="passed" for case in cases)
    commands=json.loads((ROOT/RUN/"commands.json").read_text(encoding="utf8"))
    not_run=["Physical power interruption (child process kill is recorded separately)",
             "Physical system disk-full (the actual SQLite max-page quota failure is a narrower isolated test)",
             "Complete matrix through the exported executable and native failure UX",
             "A different clean Windows machine and human recovery/IME/screen-reader validation"]
    task=next(t for t in json.loads((ROOT/"planning/plan.json").read_text(encoding="utf8"))["tasks"] if t["id"]=="CX-076")
    evidence={"named_actual_cases":cases,"scope":"isolated Windows SQLite service/process/DACL matrix; no user profile mutation","not_run":not_run}
    acceptance=[{"criterion":criterion,"status":"passed" if index in (0,1,2,3) else "pending","evidence":evidence} for index,criterion in enumerate(task["acceptance"])]
    source=["tests/Mist.Tests/WindowsRecovery/WindowsRecoveryCases.cs","tools/recovery/AuditRecoverySource.ps1","tools/recovery/README.md",RUN+"/test_results.json","reports/continuous/finish_recovery_candidate.py","reports/continuous/Scope_20261009.md"]
    # Keep the actual evidence values with hashes; avoid exceeding Windows' path limit
    # when the report helper freezes a second, deeply nested source snapshot.
    details=[{"path":p.relative_to(ROOT).as_posix(),"sha256":hashlib.sha256(p.read_bytes()).hexdigest(),"evidence":json.loads(p.read_text(encoding="utf8"))}
             for p in (ROOT/RUN/"testdata").rglob("*.evidence.json") if "windows-recovery-" in str(p)]
    summary=ROOT/"reports/execution/CX-076/windows_matrix_values.json"
    summary.write_text(json.dumps({"run":RUN,"evidence":details,"scope":evidence},ensure_ascii=False,indent=2)+"\n",encoding="utf8")
    source.append(summary.relative_to(ROOT).as_posix())
    finish("CX-076",source,commands,acceptance,not_run,{"executed":14,"failed":0,"results":cases},
           {"storage_schema":2,"shared_schema_changed":False,"input_scope":"disposable isolated test profiles only","release_candidate_matrix":"pending"},partial=True)

if __name__=="__main__":main()

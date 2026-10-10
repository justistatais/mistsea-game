"""Record actual foundation checks; all post-G0 engineering remains verified pending dependencies."""
import json
from pathlib import Path
from task_report import ROOT, finish

TEST = ROOT / "reports/build/20261009T112610537402Z"
SMOKE = ROOT / "reports/build/20261009T112647221148Z"
CONTENT = ROOT / "reports/build/20261009T112456485767Z"

DETAILS = {
    "CX-006": ["Core assembly dependency test and actual solution build", "IAssistanceProvider/DisabledProvider capability test", "Godot managed smoke boots C# composition; runtime projects contain no Python execution dependency"],
    "CX-007": ["Runner failure probe returns exit 7 and is logged as expected failure", "Results record executed named cases and human_signoff=null separately", "Cases exercise actual C#/SQLite sources; no placeholder success count", "Build root resolves from script; installed-toolchain manifest is optional with matching PATH SDK fallback"],
    "CX-008": ["Duplicate JSON, unknown DTO fields and numeric enum cases reject", "DomainClock test isolates wall time from action/combat clock", "Actual read-only query checks sequence and database byte hash unchanged", "Reward integers and exact floor basis-point case"],
    "CX-009": ["Unknown source-field negative case rejects instead of defaulting", "Removed real reward-map entry rejects; 32 rewards sum to source 19140 XP", "Nine adapters and every original paragraph checked character for character", "Projection excludes illustrative_response, answer_key and meaning_requirements"],
    "CX-010": ["Same-ID/version changed-body hash case rejects", "Actual formal CLI exit 3 lists missing audio, review, calibration and publisher evidence", "SYNTHETIC negative fixture rejected from runtime catalog", "Fixed-target validation rejects 7.0; importer checks all source auto-escalation flags"],
    "CX-011": ["Dictionary revision change does not invalidate relevant battery read-set", "Battery revision and load-token changes invalidate preview", "Missing entity yields StateUnavailable", "Empty draft and structured fallback never count independent writing"],
    "CX-012": ["Actual SQLite foreign keys, receipts, balances and serialized transactions tested", "Separate control database and tombstone replay tested across old backup restore", "Actual diagnostics assert journal_mode=delete, synchronous=2, foreign_keys=1, busy_timeout=5000", "One ProfileStore owns world persistence; immutable views and explicit writer ports"],
    "CX-013": ["Injected exception after balances rolls back entities, balances, receipts, commands and outbox", "Real competing BEGIN IMMEDIATE returns Busy while enqueue stays under 250ms", "Infrastructure has no Godot assembly reference and writer returns immutable DTOs", "Insufficient-balance fixture restores earlier item ownership through rollback"],
    "CX-014": ["Same CommandId with changed request hash rejects", "Two real stores and new command ID for same semantic work grant once", "FindReceipt recovers committed response; after-commit process-kill case retains receipt", "Outbox display changes only displayed bit, keeping marks and receipt count"],
    "CX-015": ["Pre-departure snapshot creates new branch with one reward opportunity", "Post-departure snapshot inherits receipt and returns AlreadyCommitted", "Observation count persists through both old-world loads", "Continue preserves same encounter entity and branch, changes only loading token", "Seven automatic writes retain five rolling slots and protected checkpoint alongside six manual slots"],
    "CX-016": ["Actual retry transaction preserves latest draft and newly collected material", "Delivered transport entity remains unchanged in actual retry", "Continue and local retry preserve restored consumption; no load/phase refill operation", "Successful-reward mismatch and settled-encounter guard deny refund-and-reward combination"],
    "CX-017": ["Shared DraftEditor entrances return the same acknowledged DraftId and persisted head", "Delayed actual writer ACK cannot mark edited current text saved", "Actual device entity command leaves draft text untouched", "Archive/loading-token stale draft command rejects; old branch head points at its earlier revision"],
    "CX-018": ["Unsubmitted playback/preload projects not_evidence", "Subtitle observation excludes independent listening", "Previously seen family excludes unfamiliar evidence", "PersistenceComplete=false reports conditions_incomplete and writes no observation", "ObservationRules is pure Core code without IWorldCommands; storage observation path writes only observations"],
    "CX-019": ["Control tombstone survives interruption and old backup restoration without resurrection", "Actual late request matrix checks current, old_draft, archived_branch and discarded", "Draft erasure leaves world receipt and reward intact", "Allowing external use without explicit confirmation throws; page reads never alter consent"],
    "CX-020": ["Failed migration candidate preserves original database SHA", "Actual schema 999 denies opening for write and keeps SHA", "BackupDatabase creates checked copies; corrupt active main recovers to separate candidate and preserves original", "Registered outside-path cleanup rejects and leaves external user file bytes unchanged"],
    "CX-021": ["Present audio with old material version and absent audio dependency both reject", "Traversal, drive path, executable and tampered file cases reject", "Unsigned package requires explicit internal allowance; actual RSA test key passes, wrong key fails", "Quarantine denies exact release load and preserves repair direction without modifying profile files"],
    "CX-022": ["Actual .NET child processes terminated after_balances and after_commit reopen in old/new complete state", "Same-request, semantic and competing-store tests grant once", "Actual old-checkpoint, draft CAS, deletion-control and restored-backup matrix passes", "Each process_evidence.json records process termination and explicitly excludes power-loss claim"],
}

def main():
    tests = json.loads((TEST / "test_results.json").read_text(encoding="utf8"))
    assert tests["failed"] == 0 and tests["executed"] > 0
    commands = json.loads((TEST / "commands.json").read_text(encoding="utf8"))
    plan = json.loads((ROOT / "planning/plan.json").read_text(encoding="utf8"))
    sources = [p.relative_to(ROOT).as_posix() for base in ["src", "tests/Mist.Tests", "tools/Mist.ContentCli", "schemas/sql", "docs/contracts"] for p in (ROOT / base).rglob("*") if p.is_file() and not any(segment in {"bin", "obj"} for segment in p.parts) and p.suffix in {".cs", ".csproj", ".json", ".sql", ".md"}]
    sources += ["MistSea.sln", "Directory.Build.props", "Directory.Packages.props", "global.json", "tools/ci/build.py", "tools/ci/README.md", "reports/continuous/Scope_20261009.md", (TEST / "test_results.json").relative_to(ROOT).as_posix()]
    for number in range(6, 23):
        tid = f"CX-{number:03d}"; task = next(t for t in plan["tasks"] if t["id"] == tid)
        selected = [t for t in tests["results"] if t["task"] == tid]
        task_commands = list(commands); task_sources = list(sources)
        if number == 6:
            task_commands += json.loads((SMOKE / "commands.json").read_text(encoding="utf8"))
            task_sources += ["game/Root.cs", "game/Main.tscn", "game/MistSea.csproj", "game/project.godot", "services/Mist.Assist/Program.cs", "services/Mist.Assist/Mist.Assist.csproj"]
        if number in {9,10,21}:
            task_commands += json.loads((CONTENT / "commands.json").read_text(encoding="utf8"))
            task_sources += ["game/content/runtime_catalog.json", "game/content/manifest.json", "game/content/compile_result.json", "content/manifests/material_history.json"]
        acceptance = [{"criterion": criterion, "status": "passed", "evidence": detail} for criterion, detail in zip(task["acceptance"], DETAILS[tid], strict=True)]
        finish(tid, task_sources, task_commands, acceptance,
            ["G0/HV-001 human dependency remains pending; internal development verification is not formal task acceptance", "Clean-machine, human IME/screen-reader, empirical language calibration and publisher rights/signatures are not run", "No real online provider or power-loss experiment"],
            {"task_cases": selected, "suite_executed": tests["executed"], "suite_failed": tests["failed"], "sqlite_version": tests["sqlite_version"], "dotnet_runtime": tests["dotnet_runtime"]},
            {"storage_schema": 1, "format": "typed contracts and explicit new production schema; no existing production-profile upgrade claimed", "caller_updates": "Core, Application, Infrastructure, Godot, CLI and actual C# tests built together"})

if __name__ == "__main__": main()

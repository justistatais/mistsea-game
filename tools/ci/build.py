"""Portable build/test entry: python tools/ci/build.py [configure|build|test|smoke]."""
from datetime import datetime, timezone
import json
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import sys
from build_inputs import current_inputs

ROOT = Path(__file__).resolve().parents[2]
PROJECTS = ["src/Mist.Core/Mist.Core.csproj", "src/Mist.Application/Mist.Application.csproj",
            "src/Mist.Infrastructure/Mist.Infrastructure.csproj", "game/MistSea.csproj",
            "tools/Mist.ContentCli/Mist.ContentCli.csproj", "tests/Mist.Tests/Mist.Tests.csproj",
            "services/Mist.Assist/Mist.Assist.csproj", "tools/Mist.ReviewCalibration/Mist.ReviewCalibration.csproj"]


def main():
    action = sys.argv[1] if len(sys.argv) > 1 else "test"
    assert action in {"configure", "build", "test", "smoke", "content", "client-test", "campaign-client-test", "vessel-client-test", "chapter-two-client-test", "chapter-three-client-test", "chapter-four-client-test","chapter-five-client-test","chapter-six-client-test","finale-client-test","r1-life-client-test","musicbox-client-test","r2-life-client-test","chapter-four-local-client-test","first-relay-client-test","first-side-client-test","first-outer-client-test","field-relay-client-test","vessel-life-client-test","settlement-support-client-test","optional-r1-client-test","optional-r2-client-test","optional-r3-client-test","optional-r4-client-test","optional-r5-client-test","optional-r6-client-test","lina-personal-client-test","r3-shift-client-test","old-city-local-life-client-test","scoped-personal-client-test"}, "Unknown build action."
    manifest_path = ROOT / "toolchain/installed.windows-x64.json"
    installed = json.loads(manifest_path.read_text(encoding="utf8")) if manifest_path.exists() else {}
    env = os.environ.copy()
    dotnet = shutil.which("dotnet")
    if installed and Path(installed["dotnet"]).exists():
        dotnet = installed["dotnet"]
        env.update(DOTNET_ROOT=installed["dotnet_root"], DOTNET_ROOT_X64=installed["dotnet_root"], NUGET_PACKAGES=installed["nuget_packages"])
        env["PATH"] = installed["dotnet_root"] + os.pathsep + env["PATH"]
    if not dotnet: raise RuntimeError("Install the SDK specified by global.json first.")
    env.update(DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", PYTHONUTF8="1", PYTHONIOENCODING="utf-8")
    out = ROOT / "reports/build" / datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    # Windows clock resolution can give simultaneous runs the same microsecond stamp.
    report_base=out
    for collision in range(1000):
        out=report_base if collision==0 else report_base.with_name(report_base.name+"-"+str(collision))
        try:
            out.mkdir(parents=True)
            break
        except FileExistsError:
            continue
    else:
        raise RuntimeError("Unable to create an isolated build report directory.")
    env["MISTSEA_TEST_OUTPUT"] = str(out / "testdata")
    env["MISTSEA_PROFILE_ROOT"] = str(out / "client_profile")
    env["MISTSEA_CLIENT_TEST_OUTPUT"] = str(out)
    records = []
    (out / "run_inputs.json").write_text(json.dumps({"scope":"actual current source and bundled player inputs before this run","inputs":current_inputs()},ensure_ascii=False,indent=2),encoding="utf8")

    def compiled_binaries():
        binaries=list((ROOT / "game/.godot/mono/temp/bin/Debug").rglob("*.dll"))+list((ROOT / "tests/Mist.Tests/bin/Debug/net10.0").glob("*.dll"))
        return [{"path":p.relative_to(ROOT).as_posix(),"sha256":hashlib.sha256(p.read_bytes()).hexdigest()} for p in sorted(binaries)]

    def run(label, argv, expected_code=None, expected_marker="intentional test runner failure"):
        print("RUN " + label, flush=True)
        r = subprocess.run(argv, cwd=ROOT, env=env, capture_output=True, timeout=3600 if label in {"tests","actual_vessel_route_source"} else 600)
        stdout, stderr = r.stdout.decode("utf8", errors="replace"), r.stderr.decode("utf8", errors="replace")
        log = out / (label + ".log")
        log.write_text(json.dumps({"argv": argv, "cwd": str(ROOT), "exit_code": r.returncode}) + "\n[stdout]\n" + stdout + "\n[stderr]\n" + stderr, encoding="utf8")
        record = {"argv": argv, "cwd": str(ROOT), "exit_code": r.returncode, "output_path": log.relative_to(ROOT).as_posix()}
        if expected_code is not None:
            record.update(expected_nonzero_proven=r.returncode == expected_code and expected_marker in stdout + stderr,
                          expected_failure_reason="Expected refusal or explicit failure probe: " + expected_marker)
        records.append(record)
        (out / "commands.json").write_text(json.dumps(records, ensure_ascii=False, indent=2), encoding="utf8")
        print("EXIT " + str(r.returncode), flush=True)
        if expected_code is not None:
            if not record["expected_nonzero_proven"]: raise RuntimeError(stdout + stderr)
        elif r.returncode != 0 or "ERROR:" in stderr: raise RuntimeError("Command failed: "+label+"; full output: "+str(log)+"\n"+(stdout+stderr)[-8000:])
        return stdout

    try:
        version = run("sdk_version", [dotnet, "--version"]).strip()
        if version != "10.0.401": raise RuntimeError("SDK differs from global.json: " + version)
        if not (ROOT / "MistSea.sln").exists():
            run("solution_create", [dotnet, "new", "sln", "--format", "sln", "--name", "MistSea", "--output", str(ROOT)])
            run("solution_projects", [dotnet, "sln", str(ROOT / "MistSea.sln"), "add", *[str(ROOT / p) for p in PROJECTS]])
        if not (ROOT / "game/MistSea.sln").exists():
            run("game_solution", [dotnet, "new", "sln", "--format", "sln", "--name", "MistSea", "--output", str(ROOT / "game")])
            run("game_solution_project", [dotnet, "sln", str(ROOT / "game/MistSea.sln"), "add", str(ROOT / "game/MistSea.csproj")])
        if "Mist.ReviewCalibration" not in (ROOT / "MistSea.sln").read_text(encoding="utf-8-sig"):
            run("review_tool_solution", [dotnet, "sln", str(ROOT / "MistSea.sln"), "add", str(ROOT / "tools/Mist.ReviewCalibration/Mist.ReviewCalibration.csproj")])
        restore = [dotnet, "restore", str(ROOT / "MistSea.sln"), "--configfile", str(ROOT / "spikes/toolchain/NuGet.Config")]
        if action != "configure": restore.append("--locked-mode")
        if os.environ.get("MISTSEA_REUSE_COMPILED") == "1":
            if action not in {"test","content","client-test","campaign-client-test","vessel-client-test","chapter-two-client-test","chapter-three-client-test","chapter-four-client-test","chapter-five-client-test","chapter-six-client-test","finale-client-test","r1-life-client-test","musicbox-client-test","r2-life-client-test","chapter-four-local-client-test","first-relay-client-test","first-side-client-test","first-outer-client-test","field-relay-client-test","vessel-life-client-test","settlement-support-client-test","optional-r1-client-test","optional-r2-client-test","optional-r3-client-test","optional-r4-client-test","optional-r5-client-test","optional-r6-client-test","lina-personal-client-test","r3-shift-client-test","old-city-local-life-client-test","scoped-personal-client-test"}:raise RuntimeError("Saved compilation reuse is restricted to isolated compiled tests, content compilation and named native probes.")
            binaries=list((ROOT / "game/.godot/mono/temp/bin/Debug").rglob("*.dll"))+list((ROOT / "tests/Mist.Tests/bin/Debug/net10.0").glob("*.dll"))
            if not any(p.name=="MistSea.dll" for p in binaries) or not any(p.name=="Mist.Tests.dll" for p in binaries):raise RuntimeError("A saved Debug compilation is required.")
            (out / "reused_compilation.json").write_text(json.dumps({"scope":"existing compiled artifacts; this run does not claim to compile current edited sources","binaries":[{"path":p.relative_to(ROOT).as_posix(),"sha256":hashlib.sha256(p.read_bytes()).hexdigest()} for p in binaries]},indent=2),encoding="utf8")
        else:
            (out / "build_inputs.json").write_text(json.dumps({"scope":"actual source and bundled player inputs before compilation","inputs":current_inputs()},ensure_ascii=False,indent=2),encoding="utf8")
            run("restore", restore)
            run("build", [dotnet, "build", str(ROOT / "MistSea.sln"), "-c", "Debug", "--no-restore"])
        (out / "compiled_artifacts.json").write_text(json.dumps({"scope":"actual binary SHA before execution","binaries":compiled_binaries()},indent=2),encoding="utf8")
        if action in {"test", "configure"}:
            dll=str(ROOT / "tests/Mist.Tests/bin/Debug/net10.0/Mist.Tests.dll")
            argv=[dotnet,dll,*sys.argv[2:]]
            if os.environ.get("MISTSEA_PARALLEL_FULL_SUITE") and not sys.argv[2:]:argv=[sys.executable,str(ROOT / "tools/ci/full_suite.py"),dotnet,dll]
            tests = run("tests",argv)
            parsed = json.loads(tests)
            if parsed["executed"] == 0 or parsed["failed"]: raise RuntimeError("No passing executed tests.")
            (out / "test_results.json").write_text(json.dumps(parsed, ensure_ascii=False, indent=2), encoding="utf8")
            run("runner_failure_probe", [dotnet, str(ROOT / "tests/Mist.Tests/bin/Debug/net10.0/Mist.Tests.dll"), "--fail-probe"], expected_code=7)
        if action in {"vessel-client-test","chapter-two-client-test","chapter-three-client-test","chapter-four-client-test","chapter-five-client-test","chapter-six-client-test","finale-client-test","r1-life-client-test","musicbox-client-test","r2-life-client-test","chapter-four-local-client-test","first-relay-client-test","first-side-client-test","first-outer-client-test","field-relay-client-test","vessel-life-client-test","settlement-support-client-test","optional-r1-client-test","optional-r2-client-test","optional-r3-client-test","optional-r4-client-test","optional-r5-client-test","optional-r6-client-test","lina-personal-client-test","r3-shift-client-test","old-city-local-life-client-test","scoped-personal-client-test"}:
            source_task={"chapter-three-client-test":"CP-R3-05","chapter-four-client-test":"CP-R4-05","chapter-five-client-test":"CP-R5-05","chapter-six-client-test":"CP-R6-05","finale-client-test":"CP-RF-05","r1-life-client-test":"CP-R1-03","r2-life-client-test":"CP-R2-03","chapter-four-local-client-test":"CP-R4-05","first-relay-client-test":"CP-R0-03","first-side-client-test":"CP-R1-03","first-outer-client-test":"CP-R1-03","field-relay-client-test":"CP-FIELD-RELAY-05","musicbox-client-test":"CP-OPTIONAL-MUSICBOX-05","vessel-life-client-test":"CP-NPC-LIFE","settlement-support-client-test":"CP-SETTLEMENT-05","optional-r1-client-test":"CP-R1-03","optional-r2-client-test":"CP-R2-03","optional-r3-client-test":"CP-R3-03","optional-r4-client-test":"CP-R4-03","optional-r5-client-test":"CP-R5-03","optional-r6-client-test":"CP-R6-03","lina-personal-client-test":"CP-LINA-PERSONAL-05","r3-shift-client-test":"CP-R3-05","old-city-local-life-client-test":"CP-R5-LIFE-05","scoped-personal-client-test":"CP-PERSONAL-DETAIL"}.get(action,"CX-060")
            source_argv=[dotnet,str(ROOT / "tests/Mist.Tests/bin/Debug/net10.0/Mist.Tests.dll"),"--task="+source_task]
            source_marker={"first-relay-client-test":"actual_zero_XP_relay_native_entry_","first-side-client-test":"actual_zero_XP_tower_base_native_entry_","first-outer-client-test":"actual_zero_XP_tower_base_native_entry_","r1-life-client-test":"actual_zero_XP_R1_native_entry_","r2-life-client-test":"actual_zero_XP_R2_optional_entry_","finale-client-test":"leaves untouched RF native entry","vessel-life-client-test":"Normal native-entry profile","settlement-support-client-test":None,"optional-r1-client-test":"normal_zero_XP_R1_both_independent_optional_contracts","optional-r2-client-test":"normal_zero_XP_R2_both_independent_optional_contracts","optional-r3-client-test":"normal_zero_XP_R3_both_independent_optional_contracts","optional-r4-client-test":"normal_zero_XP_R4_both_independent_optional_contracts","optional-r5-client-test":"normal_zero_XP_R5_both_independent_optional_contracts","optional-r6-client-test":"normal_zero_XP_R6_both_independent_optional_contracts","lina-personal-client-test":"actual_zero_XP_Lina_native_entry","r3-shift-client-test":"Shift native entry has actual zero-XP","old-city-local-life-client-test":None,"scoped-personal-client-test":"Normal source-backed prior nodes produce a native entry"}.get(action)
            if source_marker is not None:
                inventory=json.loads(run("compiled_source_case_inventory",[dotnet,str(ROOT / "tests/Mist.Tests/bin/Debug/net10.0/Mist.Tests.dll"),"--list-cases"]))
                selected=[c for c in inventory["cases"] if c["task"]==source_task and source_marker in c["name"]]
                if len(selected)!=1:raise RuntimeError("Expected one actual native source producer: "+source_marker)
                source_argv.append("--case-index="+str(selected[0]["index"]))
                (out / "selected_actual_source_case.json").write_text(json.dumps(selected[0],ensure_ascii=False,indent=2),encoding="utf8")
            source_name={"vessel-client-test":"actual-vessel-interior.json","chapter-two-client-test":"normal-r1-r2-voyage.json","chapter-three-client-test":"normal-r3-native-entry.json","chapter-four-client-test":"normal-r4-native-entry.json","chapter-five-client-test":"normal-r5-native-entry.json","chapter-six-client-test":"normal-r6-native-entry.json","finale-client-test":"normal-rf-native-entry.json","r1-life-client-test":"entry_evidence.json","r2-life-client-test":"entry_evidence.json","chapter-four-local-client-test":"normal-r4-native-entry.json","first-relay-client-test":"normal-first-relay-native-entry.json","first-side-client-test":"normal-first-tower-native-entry.json","first-outer-client-test":"normal-first-tower-native-entry.json","field-relay-client-test":"normal-field-relay-native-entry.json","musicbox-client-test":"normal-musicbox-native-entry.json","vessel-life-client-test":"normal-vessel-life-native-entry.json","settlement-support-client-test":"normal-settlement-native-entry.json","optional-r1-client-test":"normal-optional-r1-native-entry.json","optional-r2-client-test":"normal-optional-r2-native-entry.json","optional-r3-client-test":"normal-optional-r3-native-entry.json","optional-r4-client-test":"normal-optional-r4-native-entry.json","optional-r5-client-test":"normal-optional-r5-native-entry.json","optional-r6-client-test":"normal-optional-r6-native-entry.json","lina-personal-client-test":"normal-lina-personal-native-entry.json","r3-shift-client-test":"normal-r3-shift-native-entry.json","old-city-local-life-client-test":"normal-r5-local-life-native-entry.json","scoped-personal-client-test":"normal-scoped-personal-native-entry.json"}[action]
            if os.environ.get("MISTSEA_NATIVE_SOURCE_SUITE"):
                from native_source_cache import copy_current_suite_entry
                copy_current_suite_entry(os.environ["MISTSEA_NATIVE_SOURCE_SUITE"],out,source_task,source_marker,source_name,current_inputs(),compiled_binaries())
            else:
                route_output=run("actual_vessel_route_source",source_argv)
                route_result=json.loads(route_output)
                assert route_result["executed"] > 0 and route_result["failed"] == 0
                (out / "test_results.json").write_text(json.dumps(route_result,ensure_ascii=False,indent=2),encoding="utf8")
                proofs=list((out / "testdata").rglob(source_name))
                if len(proofs)!=1:raise RuntimeError("Actual native entry proof must be unique: "+source_name)
                source_proof=proofs[0]
                profile_source=source_proof.parent
                while not (profile_source / "control.sqlite").is_file():
                    profile_source=profile_source.parent
                    if profile_source == out / "testdata": raise RuntimeError("Actual route proof has no matching profile control database.")
                shutil.copytree(profile_source,out / "client_profile")
                (out / "client_profile_source.json").write_text(json.dumps({"source": source_proof.relative_to(ROOT).as_posix(),"fixture":False,"scope":"actual zero-XP command route through all prior main chapters and the selected native entry voyage; engineering input, not a human playthrough"},ensure_ascii=False,indent=2),encoding="utf8")
        if action in {"smoke", "client-test", "campaign-client-test", "vessel-client-test","chapter-two-client-test","chapter-three-client-test","chapter-four-client-test","chapter-five-client-test","chapter-six-client-test","finale-client-test","r1-life-client-test","musicbox-client-test","r2-life-client-test","chapter-four-local-client-test","first-relay-client-test","first-side-client-test","first-outer-client-test","field-relay-client-test","vessel-life-client-test","settlement-support-client-test","optional-r1-client-test","optional-r2-client-test","optional-r3-client-test","optional-r4-client-test","optional-r5-client-test","optional-r6-client-test","lina-personal-client-test","r3-shift-client-test","old-city-local-life-client-test","scoped-personal-client-test"}:
            godot = installed.get("godot") or shutil.which("godot")
            if not godot: raise RuntimeError("Matching Godot .NET editor is required for smoke action.")
            run("godot_import", [godot, "--headless", "--editor", "--path", str(ROOT / "game"), "--quit-after", "2"])
            output = run("client_smoke" if action == "smoke" else "client_tests", [godot, "--headless", "--path", str(ROOT / "game"), "--", "--"+action])
            if "MISTSEA_CLIENT_BOOT" not in output: raise RuntimeError("Managed client boot marker absent.")
            if action != "smoke":
                filename={"client-test":"client_results.json","campaign-client-test":"campaign_client_results.json","vessel-client-test":"vessel_client_results.json","chapter-two-client-test":"chapter_two_client_results.json","chapter-three-client-test":"chapter_three_client_results.json","chapter-four-client-test":"chapter_four_client_results.json","chapter-five-client-test":"chapter_five_client_results.json","chapter-six-client-test":"chapter_six_client_results.json","finale-client-test":"finale_client_results.json","r1-life-client-test":"r1_life_client_results.json","r2-life-client-test":"r2_life_client_results.json","chapter-four-local-client-test":"chapter_four_local_client_results.json","first-relay-client-test":"first_relay_client_results.json","first-side-client-test":"first_side_client_results.json","first-outer-client-test":"first_outer_client_results.json","field-relay-client-test":"field_relay_client_results.json","musicbox-client-test":"musicbox_client_results.json","vessel-life-client-test":"vessel_life_client_results.json","settlement-support-client-test":"settlement_support_client_results.json","optional-r1-client-test":"optional_r1_client_results.json","optional-r2-client-test":"optional_r2_client_results.json","optional-r3-client-test":"optional_r3_client_results.json","optional-r4-client-test":"optional_r4_client_results.json","optional-r5-client-test":"optional_r5_client_results.json","optional-r6-client-test":"optional_r6_client_results.json","lina-personal-client-test":"lina_personal_client_results.json","r3-shift-client-test":"r3_shift_work_client_results.json","old-city-local-life-client-test":"old_city_local_life_client_results.json","scoped-personal-client-test":"scoped_personal_client_results.json"}[action]
                check_result=json.loads((out / filename).read_text(encoding="utf8"))
                if not check_result["checks"] or check_result["failures"]: raise RuntimeError("Client checks missing or failed: " + filename)
        if action == "content":
            compiler = str(ROOT / "tools/Mist.ContentCli/bin/Debug/net10.0/Mist.ContentCli.dll")
            run("content_internal", [dotnet, compiler, "compile", str(ROOT), str(ROOT / "game/content")])
            run("content_formal_refusal", [dotnet, compiler, "compile", str(ROOT), str(ROOT / "game/content"), "--formal"], expected_code=3, expected_marker="formal_release_blocked")
        (out / "result.json").write_text(json.dumps({"action": action, "result": "verified", "sdk": version, "commands": records,
            "human_tests": "not_run", "product_coverage_claim": "only named executed test cases"}, ensure_ascii=False, indent=2), encoding="utf8")
        (ROOT / "reports/build/latest.json").write_text(json.dumps({"path": out.relative_to(ROOT).as_posix(), "action": action, "result": "verified"}), encoding="utf8")
        print(json.dumps({"action": action, "result": "verified", "path": out.relative_to(ROOT).as_posix()}), flush=True)
    except Exception as error:
        (out / "failure.json").write_text(json.dumps({"error": str(error), "commands": records}, ensure_ascii=False, indent=2), encoding="utf8")
        raise


if __name__ == "__main__": main()

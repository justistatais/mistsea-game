"""Export an internal Windows candidate and exercise its bundled runtime."""
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import urllib.request
import xml.etree.ElementTree as ET
import zipfile
from build_inputs import current_inputs

ROOT = Path(__file__).resolve().parents[2]


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf8")


def main():
    installed = json.loads((ROOT / "toolchain/installed.windows-x64.json").read_text(encoding="utf8"))
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    report = ROOT / "reports/execution/CX-077" / stamp
    package = ROOT / "artifacts/windows" / ("MistSea-internal-" + stamp)
    report.mkdir(parents=True)
    package.mkdir(parents=True)
    env = os.environ.copy()
    env.update(DOTNET_ROOT=installed["dotnet_root"], DOTNET_ROOT_X64=installed["dotnet_root"], NUGET_PACKAGES=installed["nuget_packages"])
    env["PATH"] = installed["dotnet_root"] + os.pathsep + env.get("PATH", "")
    # Idle MSBuild nodes inherit the export console's output handles on Windows.
    env.update(MSBUILDDISABLENODEREUSE="1", DOTNET_CLI_USE_MSBUILD_SERVER="0")
    commands = []

    def run(name, argv, environment, expected=0, marker=None):
        print("RUN " + name, flush=True)
        result = subprocess.run(argv, cwd=ROOT, env=environment, capture_output=True, timeout=600)
        stdout, stderr = result.stdout.decode("utf8", errors="replace"), result.stderr.decode("utf8", errors="replace")
        log = report / (name + ".log")
        log.write_text(json.dumps({"argv": argv, "cwd": str(ROOT), "exit_code": result.returncode}) + "\n[stdout]\n" + stdout + "\n[stderr]\n" + stderr, encoding="utf8")
        record = {"argv": argv, "cwd": str(ROOT), "exit_code": result.returncode, "output_path": log.relative_to(ROOT).as_posix()}
        valid = result.returncode == expected and (marker is None or marker in stdout + stderr)
        if expected:
            record.update(expected_nonzero_proven=valid, expected_failure_reason=marker)
        commands.append(record)
        write(report / "commands.json", commands)
        print("EXIT " + str(result.returncode), flush=True)
        if not valid or expected == 0 and "ERROR:" in stderr:
            raise RuntimeError("Inspect actual command failure: " + str(log))
        return stdout

    try:
        godot = installed["godot"]
        executable = package / "MistSea-internal.exe"
        debug_lock_hashes={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in [ROOT / "game/packages.lock.json",ROOT / "src/Mist.Core/packages.lock.json",ROOT / "src/Mist.Application/packages.lock.json",ROOT / "src/Mist.Infrastructure/packages.lock.json"]}
        run("godot_import", [godot, "--headless", "--editor", "--path", str(ROOT / "game"), "--quit-after", "2"], env)
        # Playback and the release probe read the authored WAV bytes, not imported
        # .sample resources. Keep File preserves those exact bytes in the PCK.
        # Import metadata is generated cache: restore the editor's settings after
        # export so development ResourceLoader checks retain their normal imports.
        wave_imports = [(p.with_suffix(p.suffix + ".import"), p) for p in sorted((ROOT / "game/content/audio").rglob("*.wav"))]
        original_imports = {p: p.read_bytes() if p.is_file() else None for p, _ in wave_imports}
        write(report / "raw_wave_export.json", {"mode": "keep_file_during_export", "assets": [{"path": p.relative_to(ROOT).as_posix(), "bytes": p.stat().st_size, "sha256": hashlib.sha256(p.read_bytes()).hexdigest()} for _, p in wave_imports]})
        try:
            for metadata, _ in wave_imports:
                metadata.write_text('[remap]\n\nimporter="keep"\n', encoding="utf8")
            run("windows_export", [godot, "--headless", "--path", str(ROOT / "game"), "--export-release", "Windows Desktop", str(executable)], env)
        finally:
            for metadata, original in original_imports.items():
                if original is None:
                    metadata.unlink(missing_ok=True)
                else:
                    metadata.write_bytes(original)
        assert all(hashlib.sha256((ROOT / path).read_bytes()).hexdigest()==before for path,before in debug_lock_hashes.items()), "Export changed a Debug dependency lock."
        assert executable.is_file() and list(package.rglob("coreclr.dll")) and list(package.rglob("e_sqlite3.dll"))
        assert not any(p.suffix.lower() in {".cs", ".csproj", ".sln", ".py", ".sqlite", ".pdb"} for p in package.rglob("*") if p.is_file())
        clean = env.copy()
        for key in ["DOTNET_ROOT", "DOTNET_ROOT_X64", "NUGET_PACKAGES", "DOTNET_CLI_HOME", "PYTHONPATH", "PYTHONHOME"]:
            clean.pop(key, None)
        clean["PATH"] = r"C:\Windows\system32;C:\Windows"
        clean["MISTSEA_PROFILE_ROOT"] = str(report / "probe_profile")
        first = run("bundled_runtime_new_game", [str(executable), "--headless", "--", "--release-probe"], clean, marker="MISTSEA_RELEASE_PROBE")
        second = run("bundled_runtime_reopen", [str(executable), "--headless", "--", "--release-probe"], clean, marker="MISTSEA_RELEASE_PROBE")
        def probe(output):
            return json.loads(next(line.split("MISTSEA_RELEASE_PROBE ", 1)[1] for line in output.splitlines() if line.startswith("MISTSEA_RELEASE_PROBE ")))
        before, after = probe(first), probe(second)
        assert before == after and before["debug_driver_absent"] and before["experience"] == 0
        corrupt = report / "corrupt_profile"
        corrupt.mkdir()
        damaged = corrupt / "profile.sqlite"
        damaged.write_bytes(b"deliberately-invalid-isolated-SQLite-fixture")
        original_hash = hashlib.sha256(damaged.read_bytes()).hexdigest()
        corrupt_env = clean.copy()
        corrupt_env["MISTSEA_PROFILE_ROOT"] = str(corrupt)
        run("corrupt_profile_preserved", [str(executable), "--headless", "--", "--release-probe"], corrupt_env, expected=5, marker="MISTSEA_BOOT_ERROR")
        assert hashlib.sha256(damaged.read_bytes()).hexdigest() == original_hash
        licenses = package / "LICENSES"
        licenses.mkdir()
        godot_commit = "ed1daf0bf001b61586d9930840f2f1394092c079"
        for name in ["LICENSE.txt", "COPYRIGHT.txt", "AUTHORS.md"]:
            url = "https://raw.githubusercontent.com/godotengine/godot/" + godot_commit + "/" + name
            body = urllib.request.urlopen(url, timeout=60).read()
            assert body
            (licenses / ("Godot-" + name)).write_bytes(body)
        runtime_license = Path(installed["nuget_packages"]) / "microsoft.netcore.app.runtime.win-x64/10.0.12/LICENSE.TXT"
        shutil.copyfile(runtime_license, licenses / "dotnet-runtime-LICENSE.txt")
        packages = []
        export_lock=ROOT / "game/packages.ExportRelease.win-x64.lock.json"
        if not export_lock.is_file(): raise RuntimeError("Exact export dependency lock was not created: " + str(export_lock))
        lock = json.loads(export_lock.read_text(encoding="utf8"))
        ns = {"n": "http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd"}
        for section in lock["dependencies"].values():
            for identity, item in section.items():
                if "resolved" not in item:
                    continue
                nuspec = Path(installed["nuget_packages"]) / identity.lower() / item["resolved"] / (identity.lower() + ".nuspec")
                metadata = next(node for node in ET.parse(nuspec).getroot().iter() if node.tag.split("}")[-1] == "metadata")
                def value(key):
                    node = next((node for node in metadata if node.tag.split("}")[-1] == key), None)
                    return node.text if node is not None else None
                packages.append({"id": identity, "version": item["resolved"], "license_metadata": value("license"), "project_url": value("projectUrl"), "nuspec_sha256": hashlib.sha256(nuspec.read_bytes()).hexdigest(), "legal_review": "pending"})
        rights = {"status": "internal_candidate_only", "package_license_metadata": packages,
                  "godot_license_source": "https://godotengine.org/license/", "godot_source_commit": godot_commit,
                  "original_material_review": "pending", "SAPI_generated_audio_redistribution_review": "pending",
                  "system_font_binaries_bundled": False, "human_editorial_and_6_5_calibration": "pending", "publisher_signature": None}
        write(package / "RIGHTS_AND_DEPENDENCIES.json", rights)
        shutil.copyfile(ROOT / "tools/ci/windows_README_中文.txt", package / "README_中文.txt")
        verification = {"packaged_runtime_checks":before,"named_checks":[],"human_signoff":None}
        latest_by_action = {}
        latest_full_suite = None
        for path in sorted((ROOT / "reports/build").glob("*/result.json")):
            entry = json.loads(path.read_text(encoding="utf8"))
            if entry.get("result")=="verified":
                latest_by_action[entry["action"]]=path.parent
                if entry["action"]=="test":
                    argv=next((c["argv"] for c in entry.get("commands",[]) if c["output_path"].endswith("tests.log")),[])
                    if argv and not any(a.startswith(("--task=","--case-index=")) for a in argv):latest_full_suite=path.parent
        if latest_full_suite is None:raise RuntimeError("A verified complete test suite is required before packaging.")
        full_inputs=latest_full_suite / "build_inputs.json"
        if not full_inputs.is_file() or json.loads(full_inputs.read_text(encoding="utf8"))["inputs"]!=current_inputs():raise RuntimeError("The complete suite must compile the exact current sources and bundled player content before packaging.")
        verification["complete_suite_inputs_sha256"]=hashlib.sha256(full_inputs.read_bytes()).hexdigest()
        latest_by_action["full_test_suite"]=latest_full_suite
        for action, directory in latest_by_action.items():
            if action!="full_test_suite":
                run_inputs=directory/"run_inputs.json"
                if not run_inputs.is_file() or json.loads(run_inputs.read_text(encoding="utf8"))["inputs"]!=current_inputs():continue
            for filename in ["test_results.json","client_results.json","campaign_client_results.json","vessel_client_results.json","chapter_two_client_results.json","chapter_three_client_results.json","chapter_four_client_results.json","chapter_five_client_results.json","chapter_six_client_results.json","finale_client_results.json","r1_life_client_results.json","r2_life_client_results.json","musicbox_client_results.json","chapter_four_local_client_results.json","first_relay_client_results.json","first_side_client_results.json","first_outer_client_results.json","field_relay_client_results.json","vessel_life_client_results.json","settlement_support_client_results.json","lina_personal_client_results.json","r3_shift_work_client_results.json","old_city_local_life_client_results.json","scoped_personal_client_results.json"]+[f"optional_r{n}_client_results.json" for n in range(1,7)]:
                candidate=directory / filename
                if candidate.is_file():verification["named_checks"].append({"action":action,"report":candidate.relative_to(ROOT).as_posix(),"data":json.loads(candidate.read_text(encoding="utf8"))})
        write(package / "VERIFICATION.json", verification)
        files = [{"path": p.relative_to(package).as_posix(), "bytes": p.stat().st_size, "sha256": hashlib.sha256(p.read_bytes()).hexdigest()} for p in sorted(package.rglob("*")) if p.is_file()]
        write(package / "PACKAGE_MANIFEST.json", {"version": "0.1-internal", "build": stamp, "signature": None, "files": files})
        archive = package.with_suffix(".zip")
        with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as zipped:
            for path in sorted(package.rglob("*")):
                if path.is_file():
                    zipped.write(path, package.name + "/" + path.relative_to(package).as_posix())
        result = {"result": "internal_candidate_verified", "package": package.relative_to(ROOT).as_posix(), "zip": archive.relative_to(ROOT).as_posix(), "zip_sha256": hashlib.sha256(archive.read_bytes()).hexdigest(), "bundled_runtime_new_and_reopen": before, "corrupt_profile_preserved": True, "debug_dependency_locks_preserved":True,"export_lock":export_lock.relative_to(ROOT).as_posix(),"commands": commands, "formal_release": "blocked", "different_clean_windows_machine": "not_run", "human_signoff": None}
        write(report / "package_result.json", result)
        write(ROOT / "artifacts/windows/latest.json", result)
        print(json.dumps(result, ensure_ascii=False), flush=True)
    except Exception as error:
        write(report / "failure.json", {"error": str(error), "commands": commands, "incomplete_package": package.relative_to(ROOT).as_posix()})
        raise


if __name__ == "__main__":
    main()

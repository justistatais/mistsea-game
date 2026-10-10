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
        run("godot_import", [godot, "--headless", "--editor", "--path", str(ROOT / "game"), "--quit-after", "2"], env)
        run("windows_export", [godot, "--headless", "--path", str(ROOT / "game"), "--export-release", "Windows Desktop", str(executable)], env)
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
        lock = json.loads((ROOT / "game/packages.lock.json").read_text(encoding="utf8"))
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
        (package / "README_中文.txt").write_text("雾海档案：第七座灯塔 · 内部开发候选\n\n解压完整文件夹，双击 MistSea-internal.exe。运行包自带 .NET 运行库，不要求安装 SDK 或 Python。\nWASD/方向键移动；选中物件后走近，E/Enter交互。六个入口为地图、任务、档案、队伍、学习、系统。\n默认从岸边救援的正常零经验新档开始；序章与第一章已接入。后续章节、美术、音轨、人审和正式发行尚未完成。在线供应商关闭。\n保存、确认意思和执行分别进行；关闭窗口前会等待正文保存。请保持整个目录完整。默认存档由Godot用户数据目录管理。\n字体100%–200%可调整。实际读屏与人工输入法验收仍待完成。\n此包仅供本地内部验证，无发布者签名，无IELTS认证或通关得分承诺。另一台干净Windows设备尚未验收。\nGodot版权与许可证：https://godotengine.org/license/；其他声明见LICENSES及RIGHTS_AND_DEPENDENCIES.json。\n", encoding="utf8")
        files = [{"path": p.relative_to(package).as_posix(), "bytes": p.stat().st_size, "sha256": hashlib.sha256(p.read_bytes()).hexdigest()} for p in sorted(package.rglob("*")) if p.is_file()]
        write(package / "PACKAGE_MANIFEST.json", {"version": "0.1-internal", "build": stamp, "signature": None, "files": files})
        archive = package.with_suffix(".zip")
        with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as zipped:
            for path in sorted(package.rglob("*")):
                if path.is_file():
                    zipped.write(path, package.name + "/" + path.relative_to(package).as_posix())
        result = {"result": "internal_candidate_verified", "package": package.relative_to(ROOT).as_posix(), "zip": archive.relative_to(ROOT).as_posix(), "zip_sha256": hashlib.sha256(archive.read_bytes()).hexdigest(), "bundled_runtime_new_and_reopen": before, "corrupt_profile_preserved": True, "commands": commands, "formal_release": "blocked", "different_clean_windows_machine": "not_run", "human_signoff": None}
        write(report / "package_result.json", result)
        write(ROOT / "artifacts/windows/latest.json", result)
        print(json.dumps(result, ensure_ascii=False), flush=True)
    except Exception as error:
        write(report / "failure.json", {"error": str(error), "commands": commands, "incomplete_package": package.relative_to(ROOT).as_posix()})
        raise


if __name__ == "__main__":
    main()

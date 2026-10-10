"""Install the exact CX-002 toolchain from primary sources into user directories."""
from __future__ import annotations

from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import sys
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
REPORT = ROOT / "reports/execution/CX-002"
BASE = Path(os.environ["LOCALAPPDATA"]) / "MistSeaToolchain"
CACHE = BASE / "downloads"


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def hash_file(path, algorithm="sha256"):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, algorithm).hexdigest()


def get_json(url):
    request = urllib.request.Request(url, headers={"User-Agent": "MistSea-Toolchain-Audit"})
    with urllib.request.urlopen(request, timeout=45) as response:
        return json.load(response)


def download(asset):
    path = CACHE / asset["name"]
    algorithm, expected = asset["algorithm"], asset["expected_hash"]
    if path.is_file() and hash_file(path, algorithm) == expected:
        return {**asset, "path": str(path), "reused": True, "sha256": hash_file(path), "bytes": path.stat().st_size}
    partial = path.with_suffix(path.suffix + ".partial")
    request = urllib.request.Request(asset["url"], headers={"User-Agent": "MistSea-Toolchain-Audit"})
    with urllib.request.urlopen(request, timeout=60) as response, partial.open("wb") as output:
        downloaded = 0
        reported = 0
        while block := response.read(4 * 1024 * 1024):
            output.write(block)
            downloaded += len(block)
            if downloaded - reported >= 64 * 1024 * 1024:
                print(json.dumps({"download": asset["name"], "MiB": downloaded // (1024 * 1024)}), flush=True)
                reported = downloaded
    actual = hash_file(partial, algorithm)
    if actual != expected:
        raise RuntimeError(f"Official checksum mismatch: {asset['name']}: {actual}")
    if path.exists():
        raise RuntimeError(f"Existing download has a different checksum; retained: {path}")
    partial.replace(path)
    result = {**asset, "path": str(path), "reused": False, "sha256": hash_file(path), "bytes": path.stat().st_size}
    print(json.dumps({"downloaded_and_verified": asset["name"], "bytes": result["bytes"]}), flush=True)
    return result


def extract(path, destination, strip_prefix=""):
    if destination.exists():
        raise RuntimeError(f"Refusing to overwrite an existing installation: {destination}")
    destination.mkdir(parents=True)
    seen = set()
    count = 0
    with zipfile.ZipFile(path) as archive:
        for entry in archive.infolist():
            name = entry.filename
            if strip_prefix:
                if not name.startswith(strip_prefix):
                    raise RuntimeError(f"Unexpected template entry: {name}")
                name = name[len(strip_prefix):]
            if not name:
                continue
            relative = PurePosixPath(name)
            if relative.is_absolute() or ".." in relative.parts or ":" in name or "\\" in name:
                raise RuntimeError(f"Unsafe archive entry: {name}")
            if (entry.external_attr >> 16) & 0o170000 == 0o120000:
                raise RuntimeError(f"Archive symlink rejected: {name}")
            key = name.rstrip("/").lower()
            if key in seen:
                raise RuntimeError(f"Duplicate archive path: {name}")
            seen.add(key)
            target = destination.joinpath(*relative.parts)
            if entry.is_dir():
                target.mkdir(parents=True, exist_ok=True)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(entry) as source, target.open("wb") as output:
                    shutil.copyfileobj(source, output)
                count += 1
    return count


def main():
    if os.name != "nt" or sys.maxsize < 2**32:
        raise RuntimeError("CX-002 installation requires 64-bit Windows Python.")
    CACHE.mkdir(parents=True, exist_ok=True)
    REPORT.mkdir(parents=True, exist_ok=True)
    if shutil.disk_usage(BASE).free < 8 * 1024**3:
        raise RuntimeError("At least 8 GiB free space is required for SDK/templates and retained downloads.")
    godot_url = "https://api.github.com/repos/godotengine/godot-builds/releases/tags/4.7.2-stable"
    dotnet_url = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json"
    release = get_json(godot_url)
    dotnet = get_json(dotnet_url)
    assert release["tag_name"] == "4.7.2-stable" and not release["prerelease"]
    sdk = next(s for r in dotnet["releases"] for s in r.get("sdks", []) if s["version"] == "10.0.401")
    sdk_file = next(f for f in sdk["files"] if f["name"] == "dotnet-sdk-win-x64.zip")
    write_json(REPORT / "godot_official_release.json", release)
    write_json(REPORT / "dotnet_official_release_metadata.json", dotnet)
    assets = [{"name": "dotnet-sdk-10.0.401-win-x64.zip", "url": sdk_file["url"],
               "algorithm": "sha512", "expected_hash": sdk_file["hash"].lower(), "metadata_source": dotnet_url}]
    for name in ["Godot_v4.7.2-stable_mono_win64.zip", "Godot_v4.7.2-stable_mono_export_templates.tpz"]:
        asset = next(a for a in release["assets"] if a["name"] == name)
        algorithm, expected = asset["digest"].split(":", 1)
        assert algorithm == "sha256"
        assets.append({"name": name, "url": asset["browser_download_url"], "algorithm": algorithm,
                       "expected_hash": expected, "expected_bytes": asset["size"], "metadata_source": godot_url})
    write_json(REPORT / "download_sources.json", {"assets": assets, "user_authorization": "2026-10-09 用户明确允许自行安装工具"})
    print("Downloading the exact SDK, Godot .NET editor and matching export templates.", flush=True)
    with ThreadPoolExecutor(max_workers=3) as pool:
        downloads = list(pool.map(download, assets))
    write_json(REPORT / "download_verification.json", downloads)
    sdk_root = BASE / "dotnet/10.0.401"
    editor_root = BASE / "godot/4.7.2-mono"
    templates_root = Path(os.environ["APPDATA"]) / "Godot/export_templates/4.7.2.stable.mono"
    counts = {
        "sdk_files": extract(Path(downloads[0]["path"]), sdk_root),
        "editor_files": extract(Path(downloads[1]["path"]), editor_root),
        "template_files": extract(Path(downloads[2]["path"]), templates_root, "templates/"),
    }
    godot = next(editor_root.rglob("Godot_v4.7.2-stable_mono_win64_console.exe"))
    sdk_exe = sdk_root / "dotnet.exe"
    assert sdk_exe.is_file()
    windows_templates = [p for p in templates_root.rglob("*") if p.is_file() and "windows" in p.name.lower() and "x86_64" in p.name.lower()]
    if not windows_templates:
        raise RuntimeError("Windows x86_64 templates were not found in the official archive.")
    installed = {
        "installed_at_utc": datetime.now(timezone.utc).isoformat(), "base": str(BASE),
        "dotnet": str(sdk_exe), "dotnet_root": str(sdk_root), "godot": str(godot),
        "export_templates": str(templates_root), "nuget_packages": str(BASE / "nuget/packages"),
        "versions": {"godot": "4.7.2", "godot_net_sdk": "4.7.2", "dotnet_sdk": "10.0.401", "sqlite_provider": "10.0.12"},
        "windows_templates": [{"path": str(p), "sha256": hash_file(p)} for p in windows_templates],
        "binary_hashes": {"dotnet_sha256": hash_file(sdk_exe), "godot_sha256": hash_file(godot)},
        "extracted_counts": counts, "system_path_modified": False, "old_system_dotnet_modified": False,
    }
    write_json(ROOT / "toolchain/installed.windows-x64.json", installed)
    print(json.dumps({"installed": True, "versions": installed["versions"], "counts": counts}, indent=2), flush=True)


if __name__ == "__main__":
    main()

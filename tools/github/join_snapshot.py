"""Verify and join MistSea snapshot parts without overwriting different files."""
from pathlib import Path, PurePosixPath
import argparse
import hashlib
import json
import os
import re
import sys
import tempfile

BLOCK_BYTES = 4 * 1024 * 1024
UNSAFE_NAME = re.compile(r'[<>:"/\\|?*\x00-\x1f]')
RESERVED = {"CON", "PRN", "AUX", "NUL"} | {
    prefix + str(number) for prefix in ("COM", "LPT") for number in range(1, 10)
}


def native(path):
    path = Path(path).absolute()
    if os.name == "nt" and not str(path).startswith("\\\\?\\"):
        text = str(path)
        return Path("\\\\?\\UNC\\" + text[2:] if text.startswith("\\\\") else "\\\\?\\" + text)
    return path


def safe_name(value):
    if (
        not isinstance(value, str)
        or not value
        or value in {".", ".."}
        or len(value) > 255
        or value.rstrip(" .") != value
        or UNSAFE_NAME.search(value)
        or value.split(".", 1)[0].upper() in RESERVED
    ):
        raise ValueError("Unsafe asset filename")
    return value


def expected_size(value, label):
    if type(value) is not int or value <= 0:
        raise ValueError("Invalid positive byte count: " + label)
    return value


def expected_digest(value, label):
    if not isinstance(value, str) or not re.fullmatch(r"[0-9a-fA-F]{64}", value):
        raise ValueError("Invalid SHA-256: " + label)
    return value.lower()


def file_digest(path):
    value = hashlib.sha256()
    size = 0
    with path.open("rb") as stream:
        while block := stream.read(BLOCK_BYTES):
            value.update(block)
            size += len(block)
    return size, value.hexdigest()


def existing_output(output, size, sha):
    if output.exists() or output.is_symlink():
        if not output.is_file():
            raise ValueError("Output already exists and is not a regular file")
        if file_digest(output) != (size, sha):
            raise ValueError("Output contains different bytes; choose a new --output")
        return True
    return False


def verified_result(output, size, sha, part_count, result):
    return {
        "result": result,
        "output": str(output),
        "bytes": size,
        "sha256": sha,
        "parts": part_count,
    }


def join_snapshot(directory, manifest_path=None, output_path=None):
    directory = native(directory).resolve()
    if not directory.is_dir():
        raise ValueError("Part directory does not exist")
    manifest_path = native(manifest_path).resolve() if manifest_path else directory / "GITHUB_UPLOAD_MANIFEST.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    archive = manifest["archive"]
    raw_archive_path = archive["path"]
    if not isinstance(raw_archive_path, str) or not raw_archive_path:
        raise ValueError("Missing archive path")
    # The preparation manifest can contain an absolute Windows or Unix path.
    archive_name = safe_name(PurePosixPath(raw_archive_path.replace("\\", "/")).name)
    size = expected_size(archive["bytes"], "archive")
    sha = expected_digest(archive["sha256"], "archive")
    prefix = archive_name + ".part"
    part_expression = re.compile(re.escape(prefix) + r"([0-9]{3})")
    assets = manifest["release_assets"]
    if not isinstance(assets, list):
        raise ValueError("release_assets must be a list")
    parts = {}
    asset_names = set()
    for asset in assets:
        name = safe_name(asset["name"])
        if name in asset_names:
            raise ValueError("Duplicate release asset name")
        asset_names.add(name)
        if not name.startswith(prefix):
            continue
        match = part_expression.fullmatch(name)
        if match is None:
            raise ValueError("Invalid part number: " + name)
        number = int(match.group(1))
        if number == 0 or number in parts:
            raise ValueError("Duplicate or zero part number")
        parts[number] = {
            "name": name,
            "bytes": expected_size(asset["bytes"], name),
            "sha256": expected_digest(asset["sha256"], name),
        }
    if not parts or sorted(parts) != list(range(1, len(parts) + 1)):
        raise ValueError("Snapshot parts must be consecutive from 001")
    if sum(part["bytes"] for part in parts.values()) != size:
        raise ValueError("Part byte totals differ from the archive byte count")

    output = native(output_path).resolve() if output_path else directory / archive_name
    forbidden = {manifest_path.resolve()}
    for part in parts.values():
        candidate = (directory / part["name"]).resolve()
        if not candidate.is_relative_to(directory):
            raise ValueError("A part path escapes the directory")
        part["path"] = candidate
        forbidden.add(candidate)
    if output.resolve() in forbidden:
        raise ValueError("Output collides with a manifest or part file")
    if existing_output(output, size, sha):
        return verified_result(output, size, sha, len(parts), "verified")
    for part in parts.values():
        if not part["path"].is_file():
            raise ValueError("Missing part: " + part["name"])
        if part["path"].stat().st_size != part["bytes"]:
            raise ValueError("Part byte count differs: " + part["name"])

    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(
            dir=output.parent, prefix="." + output.name + ".join-", suffix=".tmp", delete=False
        ) as target:
            temporary = Path(target.name)
            whole_hash = hashlib.sha256()
            whole_size = 0
            for number in sorted(parts):
                part = parts[number]
                part_hash = hashlib.sha256()
                part_size = 0
                with part["path"].open("rb") as stream:
                    while block := stream.read(BLOCK_BYTES):
                        target.write(block)
                        part_hash.update(block)
                        whole_hash.update(block)
                        part_size += len(block)
                        whole_size += len(block)
                if part_size != part["bytes"] or part_hash.hexdigest() != part["sha256"]:
                    raise ValueError("Part SHA-256 or byte verification failed: " + part["name"])
                print(json.dumps({"verified_part": number, "parts": len(parts), "bytes": part_size}), flush=True)
            if whole_size != size or whole_hash.hexdigest() != sha:
                raise ValueError("Complete archive SHA-256 or byte verification failed")
            target.flush()
            os.fsync(target.fileno())
        try:
            if os.name == "nt":
                # Windows rename refuses an existing destination.
                os.rename(temporary, output)
            else:
                # POSIX rename replaces existing files. Link+unlink gives an
                # atomic completed file without overwriting a concurrent file.
                os.link(temporary, output)
                temporary.unlink()
        except FileExistsError:
            if existing_output(output, size, sha):
                return verified_result(output, size, sha, len(parts), "verified")
            raise
        return verified_result(output, size, sha, len(parts), "joined")
    finally:
        # Only this tool's uniquely-created temporary file is removed.
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--manifest", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    try:
        result = join_snapshot(args.directory, args.manifest, args.output)
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(json.dumps({"result": "failed", "error": str(error)}), file=sys.stderr)
        return 1
    print(json.dumps(result), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())


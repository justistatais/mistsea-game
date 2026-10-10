"""Restore or verify every original game file from the private full snapshot."""
from pathlib import Path, PurePosixPath
import argparse, hashlib, json, os, shutil, tempfile, zipfile

def native(path):
    path = Path(path).absolute()
    if os.name == 'nt' and not str(path).startswith('\\\\?\\'):
        text = str(path)
        return Path('\\\\?\\UNC\\' + text[2:] if text.startswith('\\\\') else '\\\\?\\' + text)
    return path

def bounded(root, relative):
    p = PurePosixPath(relative)
    if p.is_absolute() or not p.parts or any(x in {'.', '..'} for x in p.parts) or any(':' in x or '\\' in x for x in p.parts):
        raise ValueError('Unsafe snapshot path: ' + relative)
    target = (root / relative).resolve()
    if not target.is_relative_to(root): raise ValueError('Path escapes destination: ' + relative)
    return target

def digest(path):
    value = hashlib.sha256()
    with path.open('rb') as stream:
        while block := stream.read(4 * 1024 * 1024): value.update(block)
    return value.hexdigest()

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', type=Path)
    parser.add_argument('--destination', type=Path)
    parser.add_argument('--verify-only', action='store_true')
    args = parser.parse_args()
    if not args.verify_only and args.destination is None: parser.error('--destination is required for restoration')
    root = native(args.destination).resolve() if args.destination else None
    with zipfile.ZipFile(args.archive) as pack:
        names = pack.namelist()
        if len(names) != len(set(names)): raise ValueError('Duplicate archive entries')
        manifest = json.loads(pack.read('manifest.json'))
        assert manifest['format'] == 'mistsea-content-addressed-full-snapshot-v1'
        entries = manifest['files']; assert len(entries) == manifest['file_count']
        assert len({e['path'] for e in entries}) == len(entries)
        blobs = {}
        for e in entries:
            bounded(root or Path.cwd().resolve(), e['path'])
            if len(e['sha256']) != 64 or any(c not in '0123456789abcdef' for c in e['sha256']): raise ValueError('Invalid digest')
            if e['sha256'] in blobs and blobs[e['sha256']] != e['bytes']: raise ValueError('Inconsistent blob length')
            blobs[e['sha256']] = e['bytes']
        assert sum(e['bytes'] for e in entries) == manifest['total_bytes']
        assert len(blobs) == manifest['unique_blobs'] and sum(blobs.values()) == manifest['unique_blob_bytes']
        for directory in manifest['directories']: bounded(root or Path.cwd().resolve(), directory)
        assert set(names) == {'manifest.json', *('blobs/' + d for d in blobs)}
        for index, (expected, size) in enumerate(blobs.items()):
            value = hashlib.sha256(); actual = 0
            with pack.open('blobs/' + expected) as stream:
                while block := stream.read(4 * 1024 * 1024): value.update(block); actual += len(block)
            if actual != size or value.hexdigest() != expected: raise ValueError('Blob verification failed: ' + expected)
            if index % 500 == 0: print(json.dumps({'verified_unique_blobs': index + 1, 'total': len(blobs)}), flush=True)
        if root is not None and not args.verify_only:
            # Validate all existing destinations before writing any recovered file.
            for e in entries:
                target = bounded(root, e['path'])
                if target.exists() and (not target.is_file() or target.stat().st_size != e['bytes'] or digest(target) != e['sha256']):
                    raise ValueError('Destination contains a different existing file; choose a fresh directory: ' + e['path'])
            root.mkdir(parents=True, exist_ok=True)
            for directory in manifest['directories']: bounded(root, directory).mkdir(parents=True, exist_ok=True)
            for index, e in enumerate(entries):
                target = bounded(root, e['path']); target.parent.mkdir(parents=True, exist_ok=True)
                if target.exists(): continue
                with tempfile.NamedTemporaryFile(dir=target.parent, delete=False) as tmp:
                    temporary = Path(tmp.name)
                    with pack.open('blobs/' + e['sha256']) as stream: shutil.copyfileobj(stream, tmp, 4 * 1024 * 1024)
                if digest(temporary) != e['sha256']: raise ValueError('Recovered temporary file does not match')
                temporary.replace(target)
                os.utime(target, ns=(e['mtime_ns'], e['mtime_ns']))
                if index % 1000 == 0: print(json.dumps({'restored_files': index + 1, 'total': len(entries)}), flush=True)
    print(json.dumps({'result': 'verified' if args.verify_only else 'restored', 'original_files': len(entries), 'original_bytes': manifest['total_bytes'], 'unique_blobs': len(blobs), 'destination': str(root) if root else None}))

if __name__ == '__main__': main()

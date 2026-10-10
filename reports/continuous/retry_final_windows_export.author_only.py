"""Resume a failed export after verified native/performance phases, preserving all failures."""
from datetime import datetime, timezone
from pathlib import Path
import argparse, hashlib, importlib.util, json, os, subprocess, sys
ROOT = Path(__file__).resolve().parents[2]
def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))
def write(path, data): path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf8')
def main():
    parser = argparse.ArgumentParser(); parser.add_argument('--prior-pipeline', required=True); args = parser.parse_args()
    prior = (ROOT / args.prior_pipeline).resolve(); assert prior.is_relative_to(ROOT / 'reports/continuous')
    previous = read(prior / 'progress.json'); suite = ROOT / previous['suite']
    assert [p['phase'] for p in previous['runs'][:2]] == ['all_29_native', 'native_performance']
    assert all(p['exit_code'] == 0 for p in previous['runs'][:2])
    spec = importlib.util.spec_from_file_location('_resumed_current_inputs', ROOT / 'tools/ci/build_inputs.py')
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    assert read(suite / 'build_inputs.json')['inputs'] == module.current_inputs()
    paths = list((ROOT / 'game/.godot/mono/temp/bin/Debug').rglob('*.dll')) + list((ROOT / 'tests/Mist.Tests/bin/Debug/net10.0').glob('*.dll'))
    assert read(suite / 'compiled_artifacts.json')['binaries'] == [{'path': p.relative_to(ROOT).as_posix(), 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()} for p in sorted(paths)]
    out = ROOT / 'reports/continuous' / ('delivery_pipeline_resumed_' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')); out.mkdir()
    records = previous['runs'][:2]
    payload = dict(suite=previous['suite'], runs=records, original_pipeline=prior.relative_to(ROOT).as_posix(), scope='Actual export retry; first two verified phases are explicitly retained from the same current complete inputs and Debug DLLs, not rerun')
    write(out / 'progress.json', payload)
    argv = [sys.executable, '-X', 'utf8', str(ROOT / 'tools/ci/package_windows.py')]
    env = os.environ.copy(); env.update(PYTHONUTF8='1', PYTHONIOENCODING='utf-8')
    with (out / 'windows_package.log').open('w', encoding='utf8') as log:
        process = subprocess.Popen(argv, cwd=ROOT, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding='utf8', errors='replace')
        lines = []
        for line in process.stdout:
            log.write(line); log.flush(); lines.append(line); print(line.rstrip('\r\n'), flush=True)
        code = process.wait()
    entry = dict(phase='windows_package', argv=argv, exit_code=code, log=(out / 'windows_package.log').relative_to(ROOT).as_posix())
    if code == 0: entry['actual_result'] = json.loads(lines[-1])
    records.append(entry); write(out / 'progress.json', payload)
    assert code == 0, 'Actual retried package failed; preserved: ' + str(out)
    print(json.dumps(dict(result='verified_phases_with_actual_export_retry', report=out.relative_to(ROOT).as_posix(), original_pipeline=payload['original_pipeline'], human_signoff=None), ensure_ascii=False))
if __name__ == '__main__': main()

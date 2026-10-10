"""Serial final verification after a specified actual complete suite succeeds."""
from datetime import datetime, timezone
from pathlib import Path
import argparse
import json
import os
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument('--suite', required=True)
args = parser.parse_args()
suite = Path(args.suite)
suite = suite if suite.is_absolute() else ROOT / suite
assert suite.is_relative_to(ROOT / 'reports/build')
out = ROOT / 'reports/continuous' / ('delivery_pipeline_' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ'))
out.mkdir()
records = []
def save():
    (out / 'progress.json').write_text(json.dumps({'suite': suite.relative_to(ROOT).as_posix(), 'runs': records, 'scope': 'Actual serial engineering verification; no human or formal acceptance'}, ensure_ascii=False, indent=2), encoding='utf8')

save()
print('WAITING_FOR_EXPLICIT_FINAL_SUITE ' + str(suite), flush=True)
limit = time.monotonic() + 2700
while not (suite / 'result.json').is_file():
    assert not (suite / 'failure.json').is_file(), 'Selected final full suite failed; stop downstream actions.'
    assert time.monotonic() < limit, 'Selected suite did not finish within bounded wait.'
    time.sleep(2)
result = json.loads((suite / 'result.json').read_text(encoding='utf8'))
tests = json.loads((suite / 'test_results.json').read_text(encoding='utf8'))
inventory = json.loads((suite / 'full_suite_inventory.json').read_text(encoding='utf8'))
assert result['action'] == 'test' and result['result'] == 'verified'
assert tests['executed'] == len(inventory['cases']) == 578 and tests['failed'] == 0
assert [(x['task'], x['name']) for x in tests['results']] == [(x['task'], x['name']) for x in inventory['cases']]
print('SELECTED_FULL_SUITE_VERIFIED 578 0', flush=True)
env = os.environ.copy()
env.update(PYTHONUTF8='1', PYTHONIOENCODING='utf-8')
steps = [
    ('all_29_native', [sys.executable, '-X', 'utf8', str(ROOT / 'reports/continuous/run_native_sequence.py'), '--suite', str(suite)]),
    ('native_performance', [sys.executable, '-X', 'utf8', str(ROOT / 'reports/continuous/run_current_performance.author_only.py'), '--suite', str(suite)]),
    ('windows_package', [sys.executable, '-X', 'utf8', str(ROOT / 'tools/ci/package_windows.py')]),
]
for name, argv in steps:
    print('BEGIN_FINAL_PHASE ' + name, flush=True)
    with (out / (name + '.log')).open('w', encoding='utf8') as log:
        log.write(json.dumps({'argv': argv, 'cwd': str(ROOT)}, ensure_ascii=False) + '\n')
        process = subprocess.Popen(argv, cwd=ROOT, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding='utf8', errors='replace')
        lines = []
        for line in process.stdout:
            log.write(line)
            log.flush()
            lines.append(line)
            print(line.rstrip('\r\n'), flush=True)
        code = process.wait()
    record = {'phase': name, 'argv': argv, 'exit_code': code, 'log': (out / (name + '.log')).relative_to(ROOT).as_posix()}
    if code == 0:
        record['actual_result'] = json.loads(lines[-1])
    records.append(record)
    save()
    assert code == 0, 'Actual final phase failed: ' + name
print(json.dumps({'result': 'serial_final_engineering_phases_verified', 'report': out.relative_to(ROOT).as_posix(), 'phases': len(records), 'human_signoff': None}), flush=True)

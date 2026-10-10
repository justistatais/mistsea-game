"""Bind explicit completed serial phases and exported UX before final report writes."""
from datetime import datetime, timezone
from pathlib import Path
import argparse, hashlib, importlib, json, os, subprocess, sys

ROOT = Path(__file__).resolve().parents[2]

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def rel(path):
    return path.resolve().relative_to(ROOT).as_posix()

def inside(value):
    path = Path(value)
    path = (path if path.is_absolute() else ROOT / path).resolve()
    assert path.is_relative_to(ROOT)
    return path

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pipeline', required=True)
    parser.add_argument('--method-report', required=True)
    parser.add_argument('--native-ux', required=True)
    parser.add_argument('--write-reports', action='store_true')
    args = parser.parse_args()
    pipeline = read(inside(args.pipeline) / 'progress.json')
    suite = inside(pipeline['suite'])
    assert [p['phase'] for p in pipeline['runs']] == ['all_29_native', 'native_performance', 'windows_package']
    assert all(p['exit_code'] == 0 for p in pipeline['runs'])
    phases = {p['phase']: p['actual_result'] for p in pipeline['runs']}
    sequence = read(inside(phases['all_29_native']['report']) / 'progress.json')
    assert sequence['suite'] == rel(suite) and len(sequence['runs']) == 29
    expected_actions = ['client-test', 'campaign-client-test', 'vessel-client-test', 'chapter-two-client-test', 'chapter-three-client-test', 'chapter-four-client-test', 'chapter-five-client-test', 'chapter-six-client-test', 'finale-client-test', 'r1-life-client-test', 'musicbox-client-test', 'r2-life-client-test', 'chapter-four-local-client-test', 'first-relay-client-test', 'first-side-client-test', 'first-outer-client-test', 'field-relay-client-test', 'vessel-life-client-test', 'settlement-support-client-test', 'lina-personal-client-test', 'r3-shift-client-test', 'old-city-local-life-client-test', 'scoped-personal-client-test'] + [f'optional-r{n}-client-test' for n in range(1, 7)]
    assert [r['action'] for r in sequence['runs']] == expected_actions
    native = []
    for run in sequence['runs']:
        assert run['exit_code'] == 0 and run['result'] == 'verified'
        directory = inside(run['path'])
        files = list(directory.glob('*client_results.json'))
        assert len(files) == 1, 'Ambiguous native report: ' + rel(directory)
        data = read(files[0])
        assert data['failures'] == 0 and data['checks'] and all(c['passed'] for c in data['checks'])
        native.append((run['action'], files[0]))
    package_result = read(ROOT / 'artifacts/windows/latest.json')
    assert package_result == phases['windows_package']
    package = inside(package_result['package'])
    verification = read(package / 'VERIFICATION.json')
    assert verification['complete_suite_inputs_sha256'] == hashlib.sha256((suite / 'build_inputs.json').read_bytes()).hexdigest()
    performance = phases['native_performance']
    perf_dir = inside(performance['path'])
    capture = inside(performance['pre_native_capture'])
    method = inside(args.method_report)
    method_data = read(method)
    assert method_data['suite']['path'] == rel(suite)
    assert method_data['final_fresh_rebind_required'] is True
    assert method_data['suite']['current_complete_input_match'] is True
    assert method_data['suite']['current_sorted_DLL_match'] is True
    assert method_data['profile_scan']['errors'] == []
    ux = inside(args.native_ux)
    sys.path.insert(0, str(ROOT / 'reports/continuous'))
    release = importlib.import_module('finish_release_candidates')
    recovery = importlib.import_module('finish_recovery_candidate')
    evidence = release.Evidence()
    selected, suite_data, _, suite_hash = release.read_suite(str(suite), evidence)
    result, manifest, _, packaged, _ = release.read_package('artifacts/windows/latest.json', evidence, suite_hash)
    actual_ux = recovery.native_recovery(ux, result, manifest, evidence, packaged, selected)
    assert actual_ux['recovery_ux_check'] == 'passed', json.dumps(actual_ux, ensure_ascii=False)
    out = ROOT / 'reports/continuous' / ('delivery_reports_' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ'))
    out.mkdir()
    metadata_before = ROOT / 'reports/continuous/wave_import_metadata_before_final_export_20261010.actual.json'
    before = read(metadata_before)
    assert before['suite'] == rel(suite)
    for entry in before['files']:
        path = inside(entry['path'])
        assert path.stat().st_size == entry['bytes']
        assert hashlib.sha256(path.read_bytes()).hexdigest() == entry['sha256']
    assert len(before['files']) == 50
    (out / 'wave_import_metadata_restored.actual.json').write_text(json.dumps(dict(observed_utc=datetime.now(timezone.utc).isoformat(), before_capture=rel(metadata_before), before_capture_sha256=hashlib.sha256(metadata_before.read_bytes()).hexdigest(), actual_metadata_files_unchanged=50, scope='Actual generated metadata bytes before final export and after final exported UX; no inference about human audio review', human_signoff=None), ensure_ascii=False, indent=2) + '\n', encoding='utf8')
    env = os.environ.copy()
    env.update(PYTHONUTF8='1', PYTHONIOENCODING='utf-8')
    records = []
    def execute(name, script, arguments):
        argv = [sys.executable, '-X', 'utf8', str(ROOT / 'reports/continuous' / script), *arguments]
        process = subprocess.run(argv, cwd=ROOT, env=env, capture_output=True, timeout=600)
        stdout, stderr = process.stdout.decode('utf8', errors='replace'), process.stderr.decode('utf8', errors='replace')
        record = dict(stage=name, argv=argv, cwd=str(ROOT), exit_code=process.returncode, log=rel(out / (name + '.log')))
        (out / (name + '.log')).write_text(json.dumps(record, ensure_ascii=False) + '\n[stdout]\n' + stdout + '\n[stderr]\n' + stderr, encoding='utf8')
        records.append(record)
        (out / 'progress.json').write_text(json.dumps(dict(suite=rel(suite), commands=records, human_signoff=None), ensure_ascii=False, indent=2), encoding='utf8')
        assert process.returncode == 0, name + ': ' + (stdout + stderr)[-6000:]
        print('VERIFIED ' + name, flush=True)
    chapter_args = ['--suite-dir', str(suite), '--compiled-build-dir', str(suite), '--package', str(package), '--output', str(out / 'chapter_preflight.json')]
    for _, report in native:
        chapter_args += ['--native', str(report)]
    runtime_args = ['--suite-dir', str(suite), '--compiled-build-dir', str(suite), '--performance-dir', str(perf_dir), '--performance-capture', str(capture), '--output', str(out / 'runtime_preflight.json')]
    for action, report in native:
        if action in {'chapter-two-client-test', 'chapter-five-client-test'}:
            runtime_args += ['--native', str(report)]
    execute('chapter_readonly', 'finish_chapter_candidates.py', chapter_args)
    chapter = read(out / 'chapter_preflight.json')
    assert len(chapter['prepared_tasks']) == 40
    assert len(chapter['selected_latest_current_native']) == 28, 'Expected boundary campaign plus 27 normal-source native candidates; generic editor client excluded'
    execute('runtime_readonly', 'finish_current_runtime_candidates.py', runtime_args)
    runtime = read(out / 'runtime_preflight.json')
    assert {t['task_id'] for t in runtime['prepared_tasks']} == {'CX-066', 'CX-070', 'CX-071', 'CX-075'}
    if args.write_reports:
        execute('chapter_write', 'finish_chapter_candidates.py', chapter_args + ['--write-reports'])
        release_args = [str(suite), '--package', 'artifacts/windows/latest.json', '--method-report', str(method)]
        for _, report in native:
            release_args += ['--native-report', str(report)]
        execute('release_write', 'finish_release_candidates.py', release_args)
        execute('runtime_write', 'finish_current_runtime_candidates.py', runtime_args + ['--write-reports'])
        execute('recovery_write', 'finish_recovery_candidate.py', [str(suite), '--package', 'artifacts/windows/latest.json', '--native-ux', str(ux)])
    summary = dict(result='actual_candidate_reports_written' if args.write_reports else 'actual_candidate_readonly_preflight_verified', suite=rel(suite), directory=rel(out), package=rel(package), zip=package_result['zip'], zip_sha256=package_result['zip_sha256'], recovery=actual_ux, actual_native_runs=29, human_signoff=None, formal_release='pending_human_and_production_gates', commands=records)
    (out / 'result.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2) + '\n', encoding='utf8')
    print(json.dumps(dict(result=summary['result'], report=rel(out), suite=rel(suite), native=29, recovery=actual_ux['recovery_ux_check'], human_signoff=None), ensure_ascii=False))

if __name__ == '__main__':
    main()

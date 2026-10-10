"""Freeze selected Windows recovery evidence without inferring physical/human acceptance.
Usage: finish_recovery_candidate.py reports/build/<FINAL-SUITE>
  --package artifacts/windows/latest.json --native-ux <actual-native-directory>
Native exported EXE evidence needs process.json, failure_ui.json, export_ui.json,
retry_ui.json, native_database_audit.json, reopened_process.json and reopen_ui.json.
The reopened process must be a different PID of the same verified packaged EXE;
reopen_ui must explicitly attest reopened=true and match the exact saved document.
"""
import argparse
from contextlib import closing
import hashlib
import json
from pathlib import Path
import sqlite3
from finish_release_candidates import Evidence, case_data_files, checked_inputs, digest, normal_receipts, path_at, read_package, read_suite, readonly_evidence_uri
from task_report import ROOT, finish


def recovery_profile(profile, body, evidence):
    """Inspect the closed actual pointer-selected SQLite, never an obsolete profile.sqlite."""
    profile = path_at(profile)
    control = profile / 'control.sqlite'
    evidence.file(control)
    if any(Path(str(control) + suffix).exists() for suffix in ('-wal', '-shm')):
        raise ValueError('Close the recovery window and SQLite writer before final evidence inspection')
    with closing(sqlite3.connect(readonly_evidence_uri(control, immutable=True), uri=True)) as db:
        if db.execute('PRAGMA integrity_check').fetchone()[0] != 'ok':
            raise ValueError('Actual recovery control SQLite is not intact')
        pointers = db.execute('SELECT current_path FROM recovery_pointers').fetchall()
    if len(pointers) != 1:
        raise ValueError('Actual recovery control SQLite must identify one current database')
    active = path_at(pointers[0][0], profile)
    if not active.is_relative_to(profile) or not active.is_file() or any(Path(str(active) + suffix).exists() for suffix in ('-wal', '-shm')):
        raise ValueError('Actual current recovery database is not a closed profile-contained snapshot')
    evidence.file(active)
    with closing(sqlite3.connect(readonly_evidence_uri(active, immutable=True), uri=True)) as db:
        if db.execute('PRAGMA integrity_check').fetchone()[0] != 'ok':
            raise ValueError('Actual pointer-selected recovery SQLite is not intact')
        normal_receipts(db)
        world = {}
        for table in ('entities', 'balances', 'items', 'receipts'):
            rows = sorted(db.execute('SELECT * FROM ' + table).fetchall(), key=repr)
            world[table] = dict(rows=len(rows), sha256=hashlib.sha256(json.dumps(rows, ensure_ascii=False, separators=(',', ':')).encode('utf8')).hexdigest())
        heads = db.execute('SELECT h.task_id,h.revision,r.body,r.body_hash FROM draft_heads h JOIN draft_revisions r ON h.draft_id=r.draft_id AND h.revision=r.revision WHERE r.deleted=0 AND r.body=?', (body,)).fetchall() if isinstance(body, str) else []
    return dict(active_database=active.relative_to(ROOT).as_posix(), world=world,
                heads=[dict(task=r[0], revision=r[1], body_sha256=r[3]) for r in heads],
                saved_body_exact=len(heads) == 1 and heads[0][3] == hashlib.sha256(body.encode('utf8')).hexdigest())


def recovery_source_binding(directory, process, audit, body, suite_directory, evidence):
    source = path_at(process['actual_source']); profile = path_at(process['profile'])
    if profile != directory / 'profile':
        raise ValueError('Recovery process does not use its owned disposable profile directory')
    cases_root = suite_directory / 'case_runs'
    if not source.is_relative_to(cases_root):
        raise ValueError('Recovery entry is not an untouched source from the selected current full suite')
    relative = source.relative_to(cases_root)
    if len(relative.parts) < 3 or relative.parts[1] != 'testdata' or not relative.parts[0].isdigit():
        raise ValueError('Recovery source is outside an isolated actual full-suite case profile')
    index = int(relative.parts[0]); inventory = evidence.read(suite_directory / 'full_suite_inventory.json')['cases']
    if index >= len(inventory) or relative.parts[0] != f'{index:04d}' or inventory[index]['task'] != 'CP-R1-03' or inventory[index]['name'] != 'actual_zero_XP_R1_native_entry_preserves_unaccepted_life_work_and_real_340XP_receipts':
        raise ValueError('Recovery source is not the actual untouched normal 340XP R1 entry producer')
    proof = evidence.read(source / 'entry_evidence.json')
    if proof != process.get('source_proof') or proof.get('fixture_history') is not False or proof.get('experience') != 340:
        raise ValueError('Actual recovery entry summary differs from its source producer')
    before = recovery_profile(source, body, evidence); after = recovery_profile(profile, body, evidence)
    comparisons = {table: dict(before=before['world'][table], after=after['world'][table]) for table in before['world']}
    if before['world'] != after['world'] or audit.get('world_comparison') != comparisons or path_at(audit['active_database']).relative_to(ROOT).as_posix() != after['active_database'] or not after['saved_body_exact'] or audit.get('matching_saved_heads') != after['heads']:
        raise ValueError('Actual current recovery SQLite does not match the saved body and unchanged world audit')
    return dict(status='passed', source_case=inventory[index], source_profile=source.relative_to(ROOT).as_posix(),
                active_database=after['active_database'], exact_saved_heads=after['heads'], zero_xp_normal_creation=True,
                scope='Actual full-suite entry summary plus pointer-selected source/current SQLite; no summary receipts required')


def native_recovery(directory, package, manifest, evidence, package_data, suite_directory):
    directory = path_at(directory)
    names = ('process', 'failure_ui', 'export_ui', 'retry_ui', 'native_database_audit', 'reopen_process', 'reopen_ui')
    data = {}; missing = []
    for name in names:
        p = directory / (name + '.json')
        if name == 'reopen_process' and (directory / 'reopened_process.json').is_file():
            p = directory / 'reopened_process.json'
        if p.is_file():
            data[name] = evidence.read(p)
        else:
            missing.append(name + '.json')
    files = {a['path']: a for a in manifest['files']}
    packaged_directory = path_at(package['package'])

    def exported_process(record, inherited_export_kind=None):
        if record.get('executable_kind', inherited_export_kind) not in {'exported_release', 'exported_executable'} or not record.get('pid') or not record.get('argv'):
            return False
        exe = Path(record['argv'][0]).resolve()
        if not exe.is_relative_to(packaged_directory) or not exe.is_file():
            return False
        entry = files.get(exe.relative_to(packaged_directory).as_posix())
        return entry is not None and exe.suffix.lower() == '.exe' and digest(exe) == entry['sha256'] and exe.stat().st_size == entry['bytes']

    process = data.get('process', {}); reopened = data.get('reopen_process', {})
    failure = data.get('failure_ui', {}); export = data.get('export_ui', {}); retry = data.get('retry_ui', {})
    audit = data.get('native_database_audit', {}); reopen = data.get('reopen_ui', {})
    body = failure.get('document')
    actual_body = isinstance(body, str) and bool(body)
    export_file_ok = False
    if actual_body and export.get('export_path'):
        # The user-facing export can be outside ROOT. Read its bytes only, retain its
        # hash in the short summary instead of handing an outside path to task_report.
        p = Path(export['export_path']).resolve()
        if p.is_file():
            export_file_ok = p.read_text(encoding='utf-8-sig') == body
            export['verified_export_file'] = dict(path=str(p), sha256=digest(p), bytes=p.stat().st_size)
    run_inputs = dict(status='package_bound', scope='Actual exported EXE hash and selected suite package binding; no Debug source cache inferred')
    if (directory / 'run_inputs.json').is_file():
        try:
            checked_inputs(directory, evidence); run_inputs = dict(status='passed')
        except (OSError, ValueError, KeyError, TypeError) as error:
            run_inputs = dict(status='pending', reason=str(error))
    exported = exported_process(process) and run_inputs['status'] != 'pending' and package_data['status'] == 'passed' and package_data['matches_selected_suite_inputs']
    try:
        source_binding = recovery_source_binding(directory, process, audit, body, suite_directory, evidence)
    except (OSError, ValueError, KeyError, TypeError, IndexError, sqlite3.Error) as error:
        source_binding = dict(status='pending', reason=str(error))
    saved = (exported and actual_body and failure.get('failure_visible') is True
             and export.get('exact_visible_document') is True and export.get('export_body') == body and export_file_ok
             and retry.get('body_unchanged') is True and retry.get('save_ack_visible') is True and retry.get('document') == body
             and audit.get('integrity') == 'ok' and audit.get('world_and_rewards_unchanged') is True
             and audit.get('saved_body_exact_after_process_exit') is True and source_binding['status'] == 'passed')
    initial_exe = Path(process['argv'][0]).resolve() if process.get('argv') else None
    real_exe_hash = digest(initial_exe) if initial_exe is not None and initial_exe.is_file() else None
    reopen_ok = (saved and exported_process(reopened, process.get('executable_kind')) and reopened.get('pid') != process.get('pid')
                 and Path(reopened['argv'][0]).resolve() == Path(process['argv'][0]).resolve()
                 and real_exe_hash is not None and process.get('executable_sha256') == real_exe_hash
                 and reopened.get('same_exported_executable_sha256') == real_exe_hash
                 and path_at(reopened.get('profile', '')) == path_at(process['profile'])
                 and reopened.get('original_process_closed') is True
                 and audit.get('actual_exported_ui_reopened_exact_body') is True
                 and reopen.get('reopened') is True and reopen.get('document') == body
                 and reopen.get('exact_visible_document') is True
                 and isinstance(reopen.get('tree'), str) and bool(reopen['tree'].strip()))
    return dict(directory=directory.relative_to(ROOT).as_posix(), missing=missing,
                run_input_binding=run_inputs, actual_source_sqlite_binding=source_binding, executable_kind=process.get('executable_kind'), initial_process=process.get('pid'), reopened_process=reopened.get('pid'),
                exported_exe_save_failure_export_retry='passed' if saved else 'pending',
                exported_exe_reopen_exact_document='passed' if reopen_ok else 'pending',
                recovery_ux_check='passed' if saved and reopen_ok else 'pending',
                document_sha256=__import__('hashlib').sha256(body.encode('utf8')).hexdigest() if actual_body else None,
                export_file=export.get('verified_export_file'),
                actual_database_audit={k: audit.get(k) for k in ('integrity', 'world_and_rewards_unchanged', 'saved_body_exact_after_process_exit', 'world_comparison', 'matching_saved_heads')},
                scope='Only actual named lock/save/export/retry/reopen checks; not the whole exported recovery fault matrix.',
                physical_power='not_run', physical_disk_full='not_run', physical_ime='not_run', screen_reader='not_run', clean_machine='not_run', human_signoff=None)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('suite', help='Final current-input unfiltered C# suite')
    parser.add_argument('--package', default='artifacts/windows/latest.json')
    parser.add_argument('--native-ux', action='append', default=[], help='Explicit actual UX evidence directory; never auto-select latest')
    args = parser.parse_args(); evidence = Evidence()
    directory, result, commands, suite_hash = read_suite(args.suite, evidence)
    cases = [c for c in result['results'] if c['task'] == 'CX-076']
    if not cases:
        raise ValueError('Selected suite contains no Windows recovery cases')
    package, manifest, verification, package_data, package_commands = read_package(args.package, evidence, suite_hash)
    native = [native_recovery(p, package, manifest, evidence, package_data, directory) for p in args.native_ux]
    # Individual isolated failures retain actual file hashes; their snapshot bodies
    # are not copied into a second deep source tree or mistaken for a native UI run.
    isolated = []
    for p in case_data_files(directory, '*.evidence.json'):
        if 'windows-recovery-' not in str(p):
            continue
        data = evidence.read(p)
        isolated.append(dict(source=p.relative_to(ROOT).as_posix(), values={k: v for k, v in data.items() if k in
                            {'scope', 'scenario', 'phase', 'integrity', 'exit_code', 'result', 'original_preserved', 'physical_power', 'human_signoff'}}))
    not_run = ['Physical power interruption; actual child termination remains a separate isolated check',
               'Physical system disk-full; SQLite max-page quota is a narrower actual service failure',
               'Different clean Windows machine, physical IME, screen reader and human recovery validation',
               'Complete exported native UX matrix for all fault types; named lock/save/export/retry checks do not cover every type']
    if not any(n['recovery_ux_check'] == 'passed' for n in native) or package_data['status'] != 'passed' or not package_data['matches_selected_suite_inputs']:
        not_run.append('Current verified exported EXE native failure/export/retry plus reopened exact document has not been jointly proven')
    facts = dict(suite=directory.relative_to(ROOT).as_posix(), suite_executed=result['executed'], suite_failed=result['failed'],
                 named_actual_cases=cases, isolated_case_evidence=isolated, package=package_data, native_recovery=native,
                 scope='Actual disposable Windows SQLite/process/DACL evidence; exported UX only when its real process and reopen reports pass', not_run=not_run, human_signoff=None)
    summary = evidence.freeze(facts, 'Windows recovery evidence: short summary plus original full SHA256/byte values')
    task = next(t for t in json.loads((ROOT / 'planning/plan.json').read_text(encoding='utf8'))['tasks'] if t['id'] == 'CX-076')
    names = [c['name'].lower() for c in cases]
    supported = {0: any('migration' in n and ('preserves' in n or 'preserving' in n or 'without promoting original' in n) for n in names),
                 1: any('deletion' in n or 'deletions' in n for n in names) and any('missing newer draft' in n for n in names),
                 2: any('child termination before commit' in n for n in names) and any('child termination after commit' in n for n in names), 3: True}
    acceptance = [dict(criterion=c, status='passed' if supported.get(i, False) else 'pending', evidence=dict(named_cases=cases, summary=summary, limitations=not_run)) for i, c in enumerate(task['acceptance'])]
    sources = ['reports/continuous/finish_recovery_candidate.py', 'reports/continuous/finish_release_candidates.py', 'planning/plan.json',
               'tests/Mist.Tests/WindowsRecovery/WindowsRecoveryCases.cs', 'tools/recovery/AuditRecoverySource.ps1', 'tools/recovery/README.md', summary]
    finish('CX-076', sources, commands + package_commands, acceptance, not_run,
           dict(executed=len(cases), failed=0, results=cases, sqlite_version=result.get('sqlite_version'), dotnet_runtime=result.get('dotnet_runtime'), native_ux=native),
           dict(storage_schema=2, shared_schema_changed=False, input_scope='disposable isolated test profiles', release_candidate_matrix='partial'), partial=True)


if __name__ == '__main__':
    main()

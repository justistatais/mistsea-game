"""Freeze explicitly selected engineering evidence; human/release gates remain pending.
Usage: finish_release_candidates.py reports/build/<FULL-SUITE> --package <latest.json>
  --native-report <actual-client-results.json> [--native-report ...]
  [--method-report <per-slot-method-evidence.json>]
Execution writes task reports and execution state; importing performs no IO.
"""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import re
import sqlite3
import wave
from task_report import ROOT, finish

CHAPTERS = ['campaign/first_entry.json', 'chapter_one/chapter_one.json'] + [
    f'chapter_{n}/learner_package.json' for n in ('two', 'three', 'four', 'five', 'six')
] + ['finale/learner_package.json']
VOYAGES = [f'voyage/r{n}_r{n+1}_voyage.json' for n in range(1, 6)]
MAIN_QUESTS = [f'Q-P{n:02}' for n in range(1, 4)] + [
    f'Q-{r}{n:02}' for r in range(1, 7) for n in range(1, 5)
] + [f'Q-F{n:02}' for n in range(1, 6)]


def path_at(value, base=ROOT):
    p = (base / value).resolve()
    if not p.is_relative_to(ROOT.resolve()):
        raise ValueError(f'Evidence path outside workspace: {value}')
    return p


def digest(p):
    h = hashlib.sha256()
    with p.open('rb') as stream:
        for b in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(b)
    return h.hexdigest()


class Evidence:
    # task_report copies original relative paths into deeply nested directories.
    # Preserve hashes/facts at a short path instead of re-copying deep SQLite snapshots.
    def __init__(self):
        self.files = {}

    def file(self, p):
        p = path_at(p)
        key = p.relative_to(ROOT).as_posix()
        self.files[key] = dict(path=key, sha256=digest(p), bytes=p.stat().st_size)
        return self.files[key]

    def read(self, p):
        p = path_at(p)
        self.file(p)
        return json.loads(p.read_text(encoding='utf-8-sig'))

    def freeze(self, facts, scope):
        data = dict(scope=scope, sources=list(self.files.values()), facts=facts,
                    verification='Rehash each original path and compare its full SHA256 and byte count.', human_signoff=None)
        body = json.dumps(data, ensure_ascii=False, indent=2) + '\n'
        p = ROOT / 'reports/release/ev' / (hashlib.sha256(body.encode('utf8')).hexdigest()[:16] + '.json')
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(body, encoding='utf8')
        return p.relative_to(ROOT).as_posix()


def check_commands(commands, evidence):
    if not commands:
        raise ValueError('Missing actual command records')
    for c in commands:
        if c.get('exit_code') != 0 and not c.get('expected_nonzero_proven'):
            raise ValueError('Unsuccessful command in selected evidence')
        p = path_at(c['output_path']); evidence.file(p)
        header = json.loads(p.read_text(encoding='utf-8-sig').splitlines()[0])
        if header.get('argv') != c.get('argv') or header.get('exit_code') != c.get('exit_code') or path_at(header['cwd']) != ROOT.resolve():
            raise ValueError('Recorded command differs from its actual output header')


def current_source_inputs():
    # The CI module only defines the read-only input inventory; no build is invoked.
    import importlib.util
    spec = importlib.util.spec_from_file_location('mistsea_finish_build_inputs', ROOT / 'tools/ci/build_inputs.py')
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    return module.current_inputs()


def readonly_evidence_uri(path, *, immutable=False):
    # Load the frozen CI URI helper by its actual path; importing this report
    # module alone does not execute CI or inspect any database.
    import importlib.util
    spec = importlib.util.spec_from_file_location('mistsea_readonly_sqlite_evidence', ROOT / 'tools/ci/sqlite_evidence.py')
    if spec is None or spec.loader is None:
        raise ValueError('Read-only Windows SQLite URI helper is unavailable')
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    return module.readonly_uri(path, immutable=immutable)


def normal_receipts(db):
    columns = [r[1] for r in db.execute('PRAGMA table_info(receipts)')]
    required = {'branch_id', 'semantic_key', 'action_type', 'receipt_id', 'commit_sequence', 'effect_hash', 'result_json'}
    if not required <= set(columns):
        raise ValueError('Actual receipt schema does not expose complete provenance')
    branch = db.execute("SELECT value FROM metadata WHERE key='active_branch'").fetchone()
    if not branch or not branch[0]:
        raise ValueError('Actual normal profile has no current branch')
    receipts = [dict(zip(columns, r)) for r in db.execute('SELECT * FROM receipts')]
    created = [r for r in receipts if r['branch_id'] == branch[0] and r['action_type'] == 'normal_campaign_created']
    if not receipts or any(str(r.get('semantic_key', '')).startswith(('fixture:', 'internal:')) or r.get('action_type') in {'explicit_engineering_fixture', 'internal_slice_initialization'} for r in receipts) or len(created) != 1 or not created[0]['semantic_key'].startswith('campaign:new:'):
        raise ValueError('Actual current branch lacks one fixture-free normal campaign creation')
    xp = json.loads(created[0]['result_json']).get('experience_awarded')
    if type(xp) is not int or xp != 0:
        raise ValueError('Actual normal creation must award typed integer zero XP')
    return receipts


def checked_inputs(directory, evidence, require_fresh=False):
    build_path = directory / 'build_inputs.json'; run_path = directory / 'run_inputs.json'
    if not run_path.is_file() and not build_path.is_file():
        raise ValueError('No actual execution input inventory: ' + str(directory))
    execution = evidence.read(run_path if run_path.is_file() else build_path)
    if execution.get('inputs') != current_source_inputs():
        raise ValueError('Execution inputs differ from the complete current source/player input set: ' + str(directory))
    if build_path.is_file():
        compiled = evidence.read(build_path)
        if compiled.get('inputs') != execution['inputs']:
            raise ValueError('Compiled and execution inputs differ: ' + str(directory))
    elif require_fresh:
        raise ValueError('Full suite has no fresh source compilation inventory')
    return execution['inputs']


def checked_binaries(directory, evidence):
    p = directory / 'compiled_artifacts.json'
    data = evidence.read(p)
    records = data.get('binaries', []); result = {}
    if not records:
        raise ValueError('Compiled artifact inventory is empty')
    for entry in records:
        binary = path_at(entry['path']); relative = binary.relative_to(ROOT).as_posix()
        if relative in result or binary.suffix.lower() != '.dll' or not binary.is_file() or digest(binary) != entry['sha256'] or ('bytes' in entry and binary.stat().st_size != entry['bytes']):
            raise ValueError('Current compiled artifact differs or repeats: ' + relative)
        evidence.file(binary); result[relative] = entry['sha256']
    if not any(Path(p).name == 'Mist.Tests.dll' for p in result) or not any(Path(p).name == 'MistSea.dll' for p in result):
        raise ValueError('Actual test/client compiled artifacts are not both bound')
    current = {p.relative_to(ROOT).as_posix() for p in (ROOT / 'game/.godot/mono/temp/bin/Debug').rglob('*.dll')}
    current.update(p.relative_to(ROOT).as_posix() for p in (ROOT / 'tests/Mist.Tests/bin/Debug/net10.0').glob('*.dll'))
    if set(result) != current:
        raise ValueError('Compiled artifact inventory differs from the complete current game/test DLL set')
    return result


def output_header(p, evidence):
    evidence.file(p)
    return json.loads(p.read_text(encoding='utf-8-sig').splitlines()[0])


def read_suite(value, evidence, require_full=True):
    directory = path_at(value)
    if directory.is_file():
        directory = directory.parent
    suite = evidence.read(directory / 'test_results.json')
    result = evidence.read(directory / 'result.json')
    commands = evidence.read(directory / 'commands.json'); check_commands(commands, evidence)
    cases = suite['results']
    if not cases or suite['executed'] != len(cases) or suite['failed'] != 0 or any(c.get('result') != 'passed' for c in cases) or result.get('result') != 'verified':
        raise ValueError('Selected suite has missing or failed actual cases')
    runners = [c for c in commands if Path(c['output_path']).name == 'tests.log']
    filters = ('--task=', '--case-index=', '--fail-probe', '--list-cases')
    if require_full and (result.get('action') != 'test' or len(runners) != 1 or any(str(a).startswith(filters) for c in runners for a in c.get('argv', []))):
        raise ValueError('Specify final unfiltered C# suite; do not sum filtered runs')
    if require_full and (not any('build' in c.get('argv', [])[1:3] and c.get('exit_code') == 0 for c in commands) or (directory / 'reused_compilation.json').exists()):
        raise ValueError('Final full suite must actually compile current sources, not reuse a self-reported binary set')
    checked_inputs(directory, evidence, require_fresh=require_full)
    binaries = checked_binaries(directory, evidence)
    inventory_path = directory / 'full_suite_inventory.json'
    if inventory_path.is_file():
        inventory = evidence.read(inventory_path)['cases']
        if [c.get('index') for c in inventory] != list(range(len(inventory))) or len(inventory) != len(cases) or [(c['task'], c['name']) for c in inventory] != [(c['task'], c['name']) for c in cases]:
            raise ValueError('Full compiled inventory and aggregate results do not match exactly')
        if not any(len(c.get('argv', [])) == 4 and Path(c['argv'][1]).resolve() == ROOT / 'tools/ci/full_suite.py' for c in runners):
            raise ValueError('Parallel full suite has no actual unfiltered full-suite command')
        for case in inventory:
            child = directory / 'case_runs' / f"{case['index']:04d}"
            actual = evidence.read(child / 'test_results.json'); header = output_header(child / 'command.log', evidence)
            argv = header.get('argv', [])
            if actual.get('executed') != 1 or actual.get('failed') != 0 or len(actual.get('results', [])) != 1 or actual['results'][0] != cases[case['index']]:
                raise ValueError('Actual isolated child result does not match the complete suite')
            if header.get('exit_code') != 0 or path_at(header['cwd']) != ROOT.resolve() or len(argv) != 3 or argv[2] != '--case-index=' + str(case['index']) or str(path_at(argv[1]).relative_to(ROOT).as_posix()) not in binaries:
                raise ValueError('Actual isolated child command does not bind the registered case and compiled DLL')
    elif runners and any(Path(a).name == 'full_suite.py' for a in runners[0].get('argv', [])):
        raise ValueError('Parallel full suite is missing its complete compiled registry')
    return directory, suite, commands, digest(directory / 'build_inputs.json')


def case_data_roots(directory):
    roots = [directory / 'testdata']
    roots.extend(sorted((directory / 'case_runs').glob('*/testdata')))
    return [p for p in roots if p.is_dir()]


def case_data_files(directory, pattern):
    return sorted({p for root in case_data_roots(directory) for p in root.rglob(pattern)})


def read_package(value, evidence, suite_hash=None):
    package = evidence.read(path_at(value))
    directory = path_at(package['package'])
    manifest = evidence.read(directory / 'PACKAGE_MANIFEST.json')
    bad, seen = [], set()
    for a in manifest['files']:
        p = path_at(a['path'], directory)
        if not p.is_relative_to(directory) or a['path'] in seen:
            raise ValueError('Unsafe or duplicate package file')
        seen.add(a['path'])
        if not p.is_file() or p.stat().st_size != a['bytes'] or digest(p) != a['sha256']:
            bad.append(a['path'])
    zip_path = path_at(package['zip'])
    evidence.file(zip_path)
    zip_ok = digest(zip_path) == package['zip_sha256']
    rights = evidence.read(directory / 'RIGHTS_AND_DEPENDENCIES.json')
    p = directory / 'VERIFICATION.json'
    verification = evidence.read(p) if p.is_file() else {}
    check_commands(package['commands'], evidence)
    probe = package.get('bundled_runtime_new_and_reopen', {})
    ok = package.get('result') == 'internal_candidate_verified' and not bad and zip_ok and probe.get('debug_driver_absent') is True and probe.get('online_provider') == 'disabled'
    data = dict(status='passed' if ok else 'failed', path=package['package'], zip_sha256=package['zip_sha256'], files_checked=len(seen), bad_files=bad,
                zip_hash_matches=zip_ok, matches_selected_suite_inputs=suite_hash is not None and verification.get('complete_suite_inputs_sha256') == suite_hash,
                actual_bundled_runtime_probe=probe, rights=rights, publisher_signature=manifest.get('signature'), formal_release='blocked')
    return package, manifest, verification, data, package['commands']


def content_facts(package, evidence):
    manifest_path = ROOT / 'game/content/manifest.json'
    manifest = evidence.read(manifest_path)
    assets = {a['path']: a for a in manifest['assets']}
    bad = []
    for key, a in assets.items():
        p = path_at(key, ROOT / 'game/content')
        if not p.is_file() or p.stat().st_size != a['bytes'] or digest(p) != a['sha256']:
            bad.append(key)
    probe = package['actual_bundled_runtime_probe'].get('content', {})
    same = package['status'] == 'passed' and package['matches_selected_suite_inputs'] and probe.get('manifest_sha256') == digest(manifest_path) and probe.get('verified_assets') == len(assets) and not bad
    lex = evidence.read(ROOT / 'game/content/lexicon/learner_lexicon.json')
    entries = lex['entries']; senses = {(e['term'], e['sense_key']) for e in entries}; tiers = Counter(e['tier'] for e in entries)
    bindings = evidence.read(ROOT / 'game/content/audio/regional_runtime_bindings.json')
    tracks = []
    for b in bindings:
        p = path_at(b['audio_path'], ROOT / 'game/content'); evidence.file(p)
        with wave.open(str(p), 'rb') as wav:
            seconds = wav.getnframes() / wav.getframerate()
            pcm_ok = wav.getcomptype() == 'NONE' and wav.getnframes() > 0
        metadata = any(all(a.get(k) == b.get(k) for k in ('material_id', 'material_version', 'text_sha256', 'audio_path', 'audio_sha256')) for a in manifest.get('audio_bindings', []))
        tracks.append(dict(material=b['material_id'], path=b['audio_path'], duration_seconds=seconds,
                           passed=pcm_ok and metadata and digest(p) == b['audio_sha256'] and p.stat().st_size == b['bytes'],
                           review_status=b.get('review_status'), redistribution_rights=b.get('redistribution_rights')))
    return dict(package_projection='passed' if same else 'pending', bad_compiled_assets=bad,
                chapter_packages=dict(expected=8, present=[x for x in CHAPTERS if x in assets], passed=same and all(x in assets for x in CHAPTERS) and probe.get('chapter_packages') == 8),
                voyages=dict(expected=5, present=[x for x in VOYAGES if x in assets], passed=same and all(x in assets for x in VOYAGES) and probe.get('regional_voyages') == 5),
                lexicon=dict(distinct_senses=len(senses), tiers=dict(tiers), passed=same and len(entries) == len(senses) == 720 and tiers == {'core': 360, 'extended': 360}, human_review=lex.get('human_review', 'pending')),
                regional_waves=dict(count=len(bindings), tracks=tracks, passed=same and len(bindings) == len({b['audio_path'] for b in bindings}) == 32 and all(w['passed'] for w in tracks) and probe.get('decoded_wave_tracks', 0) >= 32, audition_and_redistribution='pending'))


def normal_endings(directory, suite, evidence):
    records = []; names = {c['name'] for c in suite['results'] if c['task'] == 'CP-RF-03' and c['result'] == 'passed'}
    for p in case_data_files(directory, 'normal-rf-*.json'):
        if p.name == 'normal-rf-native-entry.json':
            continue
        report = evidence.read(p)
        if 'final' not in report or 'style' not in report:
            continue
        s = report['final']; entities = {e['id']: e for e in s['entities']}; receipts = s['receipts']; keys = Counter(r['semantic_key'] for r in receipts)
        quests = {q: json.loads(entities['quest:' + q]['json']) for q in MAIN_QUESTS if 'quest:' + q in entities}
        methods = {q: [r['action_type'] for r in receipts if r['semantic_key'] == q + ':resolution'] for q in MAIN_QUESTS}
        creation = [json.loads(r['result_json']) for r in receipts if r['action_type'] == 'normal_campaign_created']
        clean = all(not r['semantic_key'].startswith(('fixture:', 'internal:')) and r['action_type'] not in {'explicit_engineering_fixture', 'internal_slice_initialization'} for r in receipts)
        sail = [json.loads(r['result_json']) for r in receipts if r['action_type'] == 'sail_shared_glimmer']
        pairs = {(r.get('from'), r.get('to')) for r in sail}
        ending = json.loads(entities['final:ending']['json']); choice = ending['choice']
        name = 'Actual zero-XP finale ' + report['style'] + ' keeps 28-level resources and closes ' + choice.title() + ' once'
        ok = name in names and clean and len(creation) == 1 and creation[0].get('experience_awarded') == 0 and len(quests) == 32 and all(q.get('lifecycle') == 'closed' and q.get('outcome') == 'completed' for q in quests.values()) and all(keys[q + ':resolution'] == 1 for q in MAIN_QUESTS) and s['balances'].get('experience') == 19140 and sum(e['kind'] == 'shared_vessel' for e in s['entities']) == 1 and all((f'R{n}', f'R{n+1}') in pairs for n in range(1, 6))
        records.append(dict(source=p.relative_to(ROOT).as_posix(), style=report['style'], choice=choice, named_case=name, passed=ok, fixture_free=clean,
                            closed_main_quests=len(quests), main_quest_receipt_methods=methods, forward_voyages=sorted(pairs), numeric_version=report.get('numeric_version'), controller_version=report.get('controller_version'), human_balance='pending'))
    good = [r for r in records if r['passed']]
    return dict(records=records, passed={r['choice'] for r in good} >= {'central', 'island', 'distributed'} and {r['style'] for r in good} >= {'H', 'M', 'S', 'Mixed'},
                scope='Actual command navigation, SQLite persistence and candidate controllers; not a human playthrough or 6.5 assessment.')


def native_run_binding(directory, suite_directory, evidence):
    result = evidence.read(directory / 'result.json')
    if result.get('result') != 'verified' or not str(result.get('action', '')).endswith('client-test'):
        raise ValueError('Native report has no successful actual client action')
    commands = evidence.read(directory / 'commands.json'); check_commands(commands, evidence)
    action = '--' + result['action']
    if not any(c.get('exit_code') == 0 and action in c.get('argv', []) and Path(c['output_path']).name == 'client_tests.log' for c in commands):
        raise ValueError('Native report is not bound to an actual successful matching client command')
    if not (directory / 'run_inputs.json').is_file():
        raise ValueError('Native current-source run_inputs.json is missing')
    checked_inputs(directory, evidence)
    if checked_binaries(directory, evidence) != checked_binaries(suite_directory, evidence):
        raise ValueError('Native client and selected complete suite compiled binaries differ')
    audit = evidence.read(directory / 'client_profile_source.json')
    if audit.get('source_mode') != 'final_full_suite_case_copy' or audit.get('fixture') is not False or not audit.get('suite') or path_at(audit['suite']) != suite_directory:
        raise ValueError('Native input is not bound to the selected exact full suite actual entry')
    files = {
        'suite_results_sha256': suite_directory / 'test_results.json',
        'suite_inventory_sha256': suite_directory / 'full_suite_inventory.json',
        'suite_inputs_sha256': suite_directory / 'build_inputs.json',
        'compiled_artifacts_sha256': suite_directory / 'compiled_artifacts.json',
    }
    case = audit['case']; index = case['index']
    inventory = evidence.read(suite_directory / 'full_suite_inventory.json')['cases']
    if not isinstance(index, int) or index < 0 or index >= len(inventory) or case != inventory[index]:
        raise ValueError('Native source case is not the selected full suite compiled registry member')
    child = suite_directory / 'case_runs' / f'{index:04d}'
    files.update(case_results_sha256=child / 'test_results.json', case_command_sha256=child / 'command.log')
    proof = path_at(audit['source'])
    if not proof.is_relative_to(child / 'testdata'):
        raise ValueError('Normal entry proof is outside its actual isolated source case')
    files['proof_sha256'] = proof
    for field, p in files.items():
        evidence.file(p)
        if audit.get(field) != digest(p):
            raise ValueError('Actual native entry source evidence changed: ' + field)
    profile = proof.parent
    while not (profile / 'control.sqlite').is_file():
        profile = profile.parent
        if not profile.is_relative_to(child / 'testdata'):
            raise ValueError('Actual native source proof has no profile control SQLite')
    copied = audit.get('copied_profile_files', []); seen = set()
    if not copied:
        raise ValueError('No initial copied actual profile file inventory')
    for entry in copied:
        p = path_at(entry['path'], profile)
        if not p.is_relative_to(profile) or entry['path'] in seen or not p.is_file() or digest(p) != entry['sha256']:
            raise ValueError('Actual source profile changed or copied manifest is unsafe')
        seen.add(entry['path']); evidence.file(p)
    actual = {p.relative_to(profile).as_posix() for p in profile.rglob('*') if p.is_file()}
    if actual != seen:
        raise ValueError('Actual normal profile source manifest has missing or extra files')
    copied_bytes = audit.get('copied_files', []); actual_sources = {str(path_at(p, profile).relative_to(ROOT).as_posix()) for p in seen}
    if len(copied_bytes) != len(actual_sources) or {c.get('source') for c in copied_bytes} != actual_sources:
        raise ValueError('Initial copied destination bytes do not cover every actual source profile file')
    for entry in copied_bytes:
        source_hash = digest(path_at(entry['source']))
        if entry.get('sha256') != source_hash or entry.get('copied_before_native_sha256') != source_hash:
            raise ValueError('Initial copy bytes differ from the unchanged actual source')
    active = path_at(audit['source_active_database'])
    if not active.is_relative_to(profile) or digest(active) != audit.get('source_active_database_sha256'):
        raise ValueError('Actual active normal SQLite source changed')
    # Isolated producers have exited. Immutable read-only connections avoid creating
    # new WAL/SHM files while verifying the exact already-hashed source bytes.
    from contextlib import closing
    if any(Path(str(p) + suffix).exists() for p in (profile / 'control.sqlite', active) for suffix in ('-wal', '-shm')):
        raise ValueError('Actual source profile is not a closed self-contained SQLite snapshot')
    with closing(sqlite3.connect(readonly_evidence_uri(profile / 'control.sqlite', immutable=True), uri=True)) as db:
        pointers = db.execute('SELECT current_path FROM recovery_pointers').fetchall()
    if len(pointers) != 1 or path_at(pointers[0][0], profile) != active:
        raise ValueError('Actual source control pointer does not identify the hashed active SQLite')
    with closing(sqlite3.connect(readonly_evidence_uri(active, immutable=True), uri=True)) as db:
        if db.execute('PRAGMA integrity_check').fetchone()[0] != 'ok':
            raise ValueError('Actual normal input SQLite is not intact')
        normal_receipts(db)
    # The native run may legitimately change client_profile. These hashes bind the
    # initial copy to its untouched source, not the post-action world to initial state.
    return dict(status='passed', source=audit['source'], source_case=case,
                source_suite=audit['suite'], source_files=len(copied), fixture=False, zero_xp_normal_creation=True,
                source_scope=audit.get('scope'), source_suite_inputs_sha256=audit['suite_inputs_sha256'])


def native_facts(paths, verification, evidence, suite_directory):
    sources = {str(path_at(p)) for p in paths}
    for c in verification.get('named_checks', []):
        if str(c.get('report', '')).endswith('client_results.json'):
            sources.add(str(path_at(c['report'])))
    reports = []
    for p in sorted(sources):
        p = path_at(p); data = evidence.read(p); checks = data.get('checks', [])
        if isinstance(checks, dict):
            checks = [dict(name=k, passed=v) for k, v in checks.items()]
        controls_ok = bool(checks) and data.get('failures') == 0 and all(c.get('passed') is True for c in checks)
        try:
            binding = native_run_binding(p.parent, suite_directory, evidence)
        except (OSError, ValueError, KeyError, TypeError, IndexError, sqlite3.Error) as error:
            binding = dict(status='pending', reason=str(error))
        readers = [c['name'] for c in checks if c.get('passed') and re.search(r'reader|material|english|original|archive', c['name'], re.I)]
        reports.append(dict(path=p.relative_to(ROOT).as_posix(), passed=controls_ok and binding['status'] == 'passed', named_controls_passed=controls_ok,
                            check_count=len(checks), actual_material_checks=readers, current_actual_source_binding=binding,
                            source_scope=data.get('source', data.get('scope')), human_native_play='not_run', physical_ime='not_run', screen_reader='not_run'))
    return dict(reports=reports, material_native_report_count=sum(r['passed'] and bool(r['actual_material_checks']) for r in reports),
                scope='Only named actual native controls bound to the selected current full suite actual source; no full human route inferred.')


def encounter_methods(value, suite, evidence):
    # The separate 32 encounter slots cannot be inferred from 32 closed quests.
    if not value:
        return dict(status='pending', expected_main_slots=32, reason='No explicit per-slot producer/method evidence supplied; catalog and quest counts do not prove methods.')
    data = evidence.read(value); known = {(c['task'], c['name']) for c in suite['results'] if c['result'] == 'passed'}; records = []
    for e in data.get('encounters', []):
        if e.get('optional') is not False:
            continue
        source = e['producer']; methods = e.get('methods', [])
        ok = evidence.file(path_at(source['path']))['sha256'] == source['sha256'] and bool(methods) and all((m.get('task'), m.get('case_name')) in known for m in methods)
        records.append(dict(id=e['id'], passed=ok, producer=source, methods=methods))
    ok = len(records) == len({e['id'] for e in records}) == 32 and all(e['passed'] for e in records)
    return dict(status='passed' if ok else 'pending', expected_main_slots=32, records=records, scope='Per-slot named methods; not 12 optional slots or the 96-puzzle budget.')


def optional_encounter_candidates(native, evidence):
    source = path_at('content/source/optional_encounter_contracts/learner_package.json')
    data = evidence.read(source)
    contracts = data.get('contracts', [])
    expected = {f'ENC-R{region}-O{number:02}' for region in range(1, 7) for number in (1, 2)}
    if len(contracts) != 12 or {c['id'] for c in contracts} != expected:
        raise ValueError('Current optional source does not contain the twelve distinct declared slots')
    records = []
    for contract in contracts:
        candidates = [r for r in native['reports'] if r['passed'] and Path(r['path']).name == 'optional_' + contract['region'].lower() + '_client_results.json']
        observed = []
        required = [contract['id'] + suffix for suffix in ('_native_full_original_displayed', '_one_typed_local_target_inspection_no_extra_reward', '_accepted_result_survives_continue')]
        for report in candidates:
            actual = evidence.read(report['path'])
            passed_names = {c['name'] for c in actual['checks'] if c.get('passed') is True}
            if actual.get('region') == contract['region'] and set(required) <= passed_names:
                observed.append(dict(report=report['path'], named_checks=required, actual_normal_source_binding=report['current_actual_source_binding']))
        records.append(dict(id=contract['id'], material=contract['material'], current_named_native_checks_passed=bool(observed), actual_reports=observed))
    count = sum(r['current_named_native_checks_passed'] for r in records)
    return dict(budget=12, included_source_candidates=len(contracts), current_named_native_verified_slots=count,
                source=evidence.file(source), content_version=data['content_version'], records=records,
                status='engineering_candidates_verified_human_pending' if count == 12 else 'current_engineering_evidence_pending',
                formal_acceptance='pending', human_signoff=None,
                scope='Twelve distinct source slots with exact current normal-source native checks; no independent puzzle-budget or human acceptance inferred')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('suite', help='Final unfiltered, current-input C# suite directory')
    parser.add_argument('--package', default='artifacts/windows/latest.json')
    parser.add_argument('--native-report', action='append', default=[])
    parser.add_argument('--method-report', help='encounters[].id/optional=false/producer.path+sha256/methods[].task+case_name')
    args = parser.parse_args(); evidence = Evidence()
    directory, suite, commands, suite_hash = read_suite(args.suite, evidence)
    package, manifest, verification, packaged, package_commands = read_package(args.package, evidence, suite_hash)
    native = native_facts(args.native_report, verification, evidence, directory)
    coverage = dict(scope='Selected actual engineering evidence; incomplete production and human gates remain visible', suite=directory.relative_to(ROOT).as_posix(), executed_csharp_cases=suite['executed'], failed_csharp_cases=suite['failed'], windows_candidate=packaged,
                    content=content_facts(packaged, evidence), normal_zero_xp_endings=normal_endings(directory, suite, evidence), native_material=native, main_encounter_methods=encounter_methods(args.method_report, suite, evidence),
                    production_budgets=dict(optional_encounter_slots=optional_encounter_candidates(native, evidence), substantive_puzzles=dict(budget=96, status='incomplete', no_complete_unit_register=True)), online_model='disabled', formal_release='blocked', human_signoff=None,
                    missing=['Human editorial and production acceptance of local producers and twelve optional encounter candidates', 'Complete 96-unit puzzle source/method/save register', 'Physical power/disk-full, IME, screen reader and different clean Windows machine', 'Human play, editorial/target-band calibration, audition, rights and publisher signature'])
    summary = evidence.freeze(coverage, 'Final suite/package/native evidence with verifiable short-path SHA summaries')
    (ROOT / 'reports/release/coverage_current.json').write_text(json.dumps(coverage, ensure_ascii=False, indent=2) + '\n', encoding='utf8')
    compact = dict(scope='Actual selected candidate engineering evidence; no human or formal acceptance inferred',
                   selected_suite=coverage['suite'], executed=suite['executed'], failed=suite['failed'],
                   package=dict(path=packaged['path'], status=packaged['status'], same_suite_inputs=packaged['matches_selected_suite_inputs']),
                   content={k: dict(passed=coverage['content'][k]['passed']) for k in ('chapter_packages', 'voyages', 'lexicon', 'regional_waves')},
                   normal_zero_xp_endings=coverage['normal_zero_xp_endings']['passed'],
                   current_native_reports=sum(r['passed'] for r in coverage['native_material']['reports']),
                   main_encounter_methods=coverage['main_encounter_methods']['status'], production_budgets=coverage['production_budgets'],
                   source_summary=summary, human_signoff=None, formal_release='blocked',
                   pending=['Physical power interruption and physical system disk-full', 'Physical IME and screen reader',
                            'Different clean Windows machine', 'Human play and editorial review',
                            'Reading/listening/writing 6.5 calibration', 'Audio audition and redistribution rights', 'Publisher signature'])
    (ROOT / 'reports/release/status_boundaries_current.json').write_text(json.dumps(compact, ensure_ascii=False, indent=2) + '\n', encoding='utf8')
    plan = json.loads((ROOT / 'planning/plan.json').read_text(encoding='utf8'))
    sources = ['reports/continuous/finish_release_candidates.py', 'planning/plan.json', summary, 'reports/release/coverage_current.json']
    for number in (74, 75, 77, 78, 79, 80):
        task_id = f'CX-{number:03}'; task = next(t for t in plan['tasks'] if t['id'] == task_id); cases = [c for c in suite['results'] if c['task'] == task_id]
        if not cases:
            raise ValueError('Missing actual task cases: ' + task_id)
        passed = {74: {1, 2, 3} if coverage['normal_zero_xp_endings']['passed'] else set(), 75: set(),
                  77: {0, 1, 2} if packaged['status'] == 'passed' and packaged['matches_selected_suite_inputs'] else set(),
                  78: {0, 1, 2}, 79: {0, 1, 2}, 80: {1, 2, 3}}[number]
        acceptance = [dict(criterion=c, status='passed' if i in passed else 'pending', evidence=dict(named_cases=cases, summary=summary, scope_limit='Engineering evidence does not sign missing production, human UX, rights or calibration.')) for i, c in enumerate(task['acceptance'])]
        finish(task_id, sources, commands + package_commands, acceptance, coverage['missing'], dict(task_cases=cases, suite_executed=suite['executed'], suite_failed=suite['failed'], sqlite_version=suite.get('sqlite_version'), dotnet_runtime=suite.get('dotnet_runtime'), human_signoff=None), dict(storage_schema=2, release_scope='internal candidate; no external upload or human signoff'), partial=True)


if __name__ == '__main__':
    main()

"""Read the mounted design packages; do not alter upstream files.
This inventory covers the explicitly named material collections, not every
string or dialogue embedded in every JSON file. Re-run against --source-root.
"""
from __future__ import annotations
import argparse, hashlib, json, zipfile
from pathlib import Path


def digest(value: object) -> str:
    return hashlib.sha256(json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


def build(source_root: Path, output: Path) -> dict:
    originals = [
        ('Part05_English_Learning_Design_v0.1.zip', 'data/Original_Materials_v0.1.json'),
        ('Part06_Puzzle_Design_v0.1.zip', 'data/Puzzle_Materials_v0.1.json'),
        ('Part10_UI_UX_Design_v0.1.zip', 'data/Q102_Material_Reference_v0.1.json'),
    ]
    seen: dict[tuple[str, str], dict] = {}
    occurrences, collisions = [], []
    for archive, member in originals:
        with zipfile.ZipFile(source_root / archive) as z:
            obj = json.loads(z.read(member))
        for item in obj['materials']:
            body = {'paragraphs': item['paragraphs']}
            key = item['id'], item['version']
            origin = {'archive': archive, 'member': member, 'record_id': item['id']}
            row = {'asset_id': key[0], 'version': key[1], 'content_hash': digest(body),
                   'source_ref': origin}
            occurrences.append(row)
            if key in seen:
                if seen[key]['content_hash'] != row['content_hash']:
                    collisions.append({'id': key, 'existing': seen[key]['content_hash'], 'other': row['content_hash']})
                seen[key]['occurrences'].append(origin)
            else:
                seen[key] = {'asset_id': key[0], 'version': key[1], 'kind': item['modality'],
                             'title': item['title'], 'target_band': 6.5,
                             'content_hash': row['content_hash'], 'body': body,
                             'legacy_record': item, 'occurrences': [origin],
                             'calibration_evidence': 'not_supplied_in_inspected_records',
                             'rights_review': 'not_verified_by_this_inventory',
                             'audio_state': 'script_only' if item['modality'] == 'listening' else 'not_applicable'}
    for archive, prefix in [('Part08_Combat_Design_v0.1.zip', 'COM'), ('Part09_NPC_Faction_Design_v0.1.zip', 'NPC')]:
        with zipfile.ZipFile(source_root / archive) as z:
            for member in sorted(z.namelist()):
                if not (member.startswith('materials/') and member.endswith('.md')):
                    continue
                text = z.read(member).decode('utf-8')
                asset_id = Path(member).stem.split('_')[0]
                kind = 'dialogue_script' if prefix == 'NPC' else ('listening' if 'Script' in member else 'reading')
                body = {'markdown': text}
                origin = {'archive': archive, 'member': member, 'record_id': asset_id}
                row = {'asset_id': asset_id, 'version': '0.1', 'content_hash': digest(body), 'source_ref': origin}
                occurrences.append(row)
                key = asset_id, '0.1'
                if key in seen:
                    if seen[key]['content_hash'] != row['content_hash']:
                        collisions.append({'id': key, 'existing': seen[key]['content_hash'], 'other': row['content_hash']})
                    seen[key]['occurrences'].append(origin)
                else:
                    seen[key] = {'asset_id': asset_id, 'version': '0.1', 'kind': kind, 'title': text.splitlines()[0],
                                 'target_band': 6.5, 'content_hash': row['content_hash'], 'body': body,
                                 'occurrences': [origin], 'calibration_evidence': 'not_supplied_in_inspected_records',
                                 'rights_review': 'not_verified_by_this_inventory',
                                 'audio_state': 'script_only' if kind != 'reading' else 'not_applicable'}
    archives, docs, audio = [], [], []
    for p in sorted(source_root.glob('Part*.zip')):
        if p.name.startswith('Part11'):
            continue
        with zipfile.ZipFile(p) as z:
            for member in z.namelist():
                if Path(member).suffix.lower() in {'.wav', '.mp3', '.ogg', '.flac', '.m4a', '.opus', '.aac'}:
                    audio.append({'archive': p.name, 'member': member})
        archives.append({'path': p.name, 'sha256': hashlib.sha256(p.read_bytes()).hexdigest(), 'size_bytes': p.stat().st_size})
    names = ['Game_Design_Core','World_Story_Bible','Exploration_Level_Design','Quest_Event_System',
             'English_Learning_System','Puzzle_System','Character_Progression','Combat_System','NPC_Faction_System','UI_UX_Spec']
    for n in names:
        p = source_root / f'{n}_v0.1.md'
        docs.append({'path': p.name, 'sha256': hashlib.sha256(p.read_bytes()).hexdigest(), 'size_bytes': p.stat().st_size})
    catalog = {'scope': 'Part05/06/10 named material JSON collections + Part08/09 materials/*.md; writing prompts and lexicon counted separately',
               'occurrence_count': len(occurrences), 'unique_count': len(seen),
               'duplicate_reuse_count': len(occurrences)-len(seen), 'same_id_version_conflicts': collisions,
               'materials': list(seen.values())}
    manifest = {'version': '0.1', 'source_root_hint': 'sibling /mnt/data/game_design; runtime may use another path',
                'upstream_documents': docs, 'upstream_archives': archives,
                'audio_files_detected_in_archives': audio,
                'material_inventory_scope': catalog['scope']}
    output.mkdir(parents=True, exist_ok=True)
    (output / 'Source_Material_Catalog_v0.1.json').write_text(json.dumps(catalog, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    (output / 'Upstream_Manifest_v0.1.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    with zipfile.ZipFile(source_root/'Part05_English_Learning_Design_v0.1.zip') as z:
        full = json.loads(z.read('data/Original_Materials_v0.1.json'))
        q102 = [m for m in full['materials'] if m['id'].startswith('Q102-')]
        refs = {'materials': q102, 'writing': {'q102': full['writing']['q102']},
                'note': 'Imported without modifying any paragraph. Answers/items remain in authoring reference; runtime projections exclude them.'}
        (output/'Q102_Canonical_Input_v0.1.json').write_text(json.dumps(refs, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    return {'unique_materials': len(seen), 'occurrences': len(occurrences), 'duplicate_reuses': len(occurrences)-len(seen),
            'conflicts': len(collisions), 'audio_files': len(audio), 'kind_counts': {k:sum(x['kind']==k for x in seen.values()) for k in sorted({x['kind'] for x in seen.values()})}}

if __name__ == '__main__':
    ap=argparse.ArgumentParser(); ap.add_argument('--source-root',type=Path,required=True);ap.add_argument('--output',type=Path,required=True)
    a=ap.parse_args();print(json.dumps(build(a.source_root,a.output),ensure_ascii=False,indent=2))

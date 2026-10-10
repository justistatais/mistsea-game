"""Build an original, offline lexical candidate from frozen learner prose only.

Never reads answer cards. Matching verifies spelling, coordinates and content identity;
it does not certify sense alignment, empirical 6.5 difficulty or human review.
"""
from pathlib import Path
import argparse
import collections
import hashlib
import json
import re
from lexicon_source_adapters import (displayed_materials, display_paragraphs,
                                    display_body, display_version, paragraph_english)

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'content/source'
OUT = SOURCE / 'lexicon'
sha = lambda s: hashlib.sha256(s.encode('utf-8')).hexdigest()
arguments=argparse.ArgumentParser(description=__doc__)
arguments.add_argument('--version',choices=['LEX-20261010-v2','LEX-20261010-v3'],default='LEX-20261010-v3',
    help='v3 binds the newly frozen permission-scoped readers. Historical v2 may only reproduce its preserved bytes.')
VERSION=arguments.parse_args().version
old_path=OUT/'learner_lexicon.json'
previous=json.loads(old_path.read_text(encoding='utf-8-sig')) if old_path.exists() else None
files = [SOURCE/'campaign/first_entry.json', SOURCE/'chapter_one/chapter_one.json',
         SOURCE/'part05-Original_Materials_v0.1.json'] + [SOURCE/p/'learner_package.json' for p in
         ['chapter_two','chapter_three','chapter_four','chapter_five','chapter_six','finale']] + [
         SOURCE/'r1_life/learner_package.json',SOURCE/'first_approaches/learner_package.json',
         SOURCE/'chapter_four/optional_credit.json',SOURCE/'chapter_four/optional_inquiry.json',
         SOURCE/'optional_local_works/field_relay_contract.json']
supplement_paths={f.relative_to(ROOT).as_posix() for f in files[-5:]}
if VERSION=='LEX-20261010-v3':
    new_files=[SOURCE/p for p in ['lina_personal/learner_package.json','r3_shift_work/learner_package.json',
        'vessel_life/learner_package.json','optional_encounter_contracts/learner_package.json',
        'optional_local_works/settlement_support_contracts.json','optional_local_works/old_city_life_contract.json',
        'scoped_personal_work/learner_package.json']]
    files.extend(new_files)
    supplement_paths.update(f.relative_to(ROOT).as_posix() for f in new_files)
supplement_material_ids={'R2-O01-R-LETTER','R2-O02-R-SEEDS'}
paragraphs = []
for f in files:
    doc = json.loads(f.read_text(encoding='utf-8-sig'))
    materials=displayed_materials(f,doc)
    for m in materials:
        ps=display_paragraphs(m)
        body=display_body(f,m)
        region = {'campaign':'R0_R1','chapter_one':'R1','chapter_two':'R2','chapter_three':'R3',
                  'chapter_four':'R4','chapter_five':'R5','chapter_six':'R6','finale':'RF',
                  'r1_life':'R1','optional_local_works':'R6'}.get(f.parent.name,'R1')
        if f.parent.name=='first_approaches':region='R0' if m['scene']=='R0-S05' else 'R1'
        # Apply explicit projections only for the v3 sources. Preserve all v2
        # metadata byte-for-byte when no new version was requested.
        if VERSION=='LEX-20261010-v3' and f.relative_to(ROOT).as_posix() in supplement_paths:
            region=m.get('region',region)
        for p in ps:
            en=paragraph_english(p)
            paragraphs.append(dict(material_id=m['id'], version=display_version(doc,m),
                fingerprint=sha(body), paragraph_id=p['id'], paragraph_english=en,
                paragraph_chinese=p.get('zh',''), region=region,
                family_id=m.get('family_id',m.get('source_root',m['id'])),
                modality='listening' if m.get('kind')=='listening_script' or m.get('modality')=='listening' or '-L-' in m['id'] else 'reading',
                access_class=m.get('access_class','acquired_material'),
                source_file=f.relative_to(ROOT).as_posix(), source_file_sha256=hashlib.sha256(f.read_bytes()).hexdigest()))

# These are lexical chunks, rather than arbitrary combinations of a listed adjective and noun.
chunks={'subject to','in accordance with','on behalf of','in relation to','in advance','at least','at most',
        'no longer','rather than','as well as','even if','provided that'}
# Inflections are aliases, never extra senses. No automatic stemming of unrelated words.
aliases={'retain':['retained','retains','retaining'], 'preserve':['preserved','preserves','preserving'],
 'compare':['compared','comparing'], 'identify':['identified','identifies','identifying'],
 'isolate':['isolated','isolating'], 'supersede':['supersedes','superseded'], 'borrow':['borrowed'],
 'lend':['lent'], 'hire':['hired','hiring'], 'replenish':['replenishes','replenished'],
 'acknowledge':['acknowledges','acknowledged'], 'obligation':['obligations'],
 'draught':['draft'], 'guest':['guests'], 'passenger':['passengers'], 'worker':['workers'],
 'supply':['supplies'], 'part':['parts'], 'resource':['resources'], 'quantity':['quantities'],
 'rate':['rates'], 'room':['rooms'], 'crate':['crates'], 'bed':['beds'], 'word':['words'],
 'sense':['senses'], 'collocation':['collocations'], 'enemy':['enemies'], 'ally':['allies'],
 'reading value':['readings'], 'definition':['definitions'], 'identifier':['identifiers'],
 'licence':['license','licences'], 'authorised':['authorized'], 'organisation':['organization','organisations'],
 'armour':['armor'], 'synchronisation':['synchronization']}
skip={'obligations','replenishes','acknowledges','serials','retained','preserved'}
selected=[]
skipped=[]
frozen_terms={e['term'] for e in previous['entries']} if previous else None
for line in (OUT/'senses.author_only.tsv').read_text(encoding='utf-8').splitlines():
    term, definition, chinese = line.split('|')
    if frozen_terms is not None and term not in frozen_terms:
        skipped.append((term,'outside_frozen_720_senses'));continue
    if term in skip or ' ' in term and term not in chunks:
        skipped.append((term,'not_an_additional_lexical_sense'));continue
    forms=[term]+aliases.get(term,[])
    matches=[]
    for p in paragraphs:
        for form in forms:
            for match in re.finditer(r'(?<![A-Za-z])'+re.escape(form)+r'(?![A-Za-z])',p['paragraph_english'],re.I):
                ctx=dict(p, start_scalar=match.start(), end_scalar=match.end(), matched_form=match.group())
                matches.append(ctx)
    if not matches:
        skipped.append((term,'no_exact_learner_source'));continue
    if len(selected)==720:
        skipped.append((term,'outside_720_budget'));continue
    # Prefer a paragraph genuinely discussing writing for draft, not the alternative spelling of draught.
    if term=='draft':
        matches=[p for p in matches if any(x in p['paragraph_english'].lower() for x in ['letter','writing','prose','draft notice','draft d0','draft note'])]
        if not matches: skipped.append((term,'sense_context_not_found'));continue
    if term=='draught':
        matches=[p for p in matches if any(x in p['paragraph_english'].lower() for x in ['vessel','waterline','metres','metre','draught'])]
    chosen=[]
    seen=set()
    for modality in ['reading','listening']:
        pool=[m for m in matches if m['modality']==modality]
        for match in pool[:1]:
            key=(match['material_id'],match['paragraph_id'])
            if key not in seen: chosen.append(match);seen.add(key)
    for match in matches:
        key=(match['material_id'],match['paragraph_id'])
        if key not in seen and len(chosen)<4:
            chosen.append(match);seen.add(key)
    # Keep the earlier representative contexts and add one genuine occurrence from
    # every newly playable material. A first-four global cap would silently exclude
    # all later life/field/study pages despite their being currently readable.
    for match in matches:
        if match['source_file'] not in supplement_paths and match['material_id'] not in supplement_material_ids:continue
        if any(c['material_id']==match['material_id'] and c['version']==match['version'] for c in chosen):continue
        key=(match['material_id'],match['paragraph_id'])
        if key not in seen:chosen.append(match);seen.add(key)
    n=len(selected)+1
    selected.append(dict(id=f'LEX-{n:04}', tier='core' if n<=360 else 'extended', term=term,
        sense_key='shipping_depth' if term=='draught' else 'preliminary_writing' if term=='draft' else 'contextual_meaning',
        english_definition=definition, chinese_aid=chinese,
        aliases=aliases.get(term,[]), contexts=chosen,
        review_status='original_unreviewed', human_review='human_pending',
        pronunciation_asset='', pronunciation_review='not_recorded',
        semantic_alignment='candidate_requires_human_review'))
if len(selected)!=720:
    raise SystemExit(f'Only {len(selected)} real source-bound senses; cannot fill budget with placeholders. Skipped {skipped}')
if len({e['term']+'|'+e['sense_key'] for e in selected})!=720:
    raise SystemExit('Duplicate lexical sense')
for e in selected:
    assert len(e['english_definition'].split())>=4 and e['chinese_aid']
    for c in e['contexts']:
        assert c['paragraph_english'][c['start_scalar']:c['end_scalar']].lower()==c['matched_form'].lower()
package=dict(content_version=VERSION, player_projection=True, scope='authored_offline_candidate',
    target_bands=dict(reading=6.5,listening=6.5,writing=6.5), empirical_calibration='not_run', human_review='human_pending',
    rights_review='original_definitions_external_assets_not_included',
    count_policy='360_core_plus_360_extended_distinct_senses_not_inflections_or_modalities', entries=selected)
if previous:
    stable=lambda entry:{key:value for key,value in entry.items() if key!='contexts'}
    if [stable(e) for e in previous['entries']]!=[stable(e) for e in selected]:
        raise SystemExit('Context-only update cannot renumber senses, change definitions, aliases or 360/360 tiers.')
    if previous['content_version']==VERSION and previous!=package:
        raise SystemExit('Changed source bytes or contexts require another lexicon version; never overwrite a released version.')
OUT.mkdir(parents=True,exist_ok=True)
if previous and previous['content_version']!=VERSION:
    history=OUT/'history.author_only';history.mkdir(exist_ok=True)
    original=old_path.read_bytes();historical=history/(previous['content_version']+'.json')
    if historical.exists() and historical.read_bytes()!=original:raise SystemExit('Existing historical version bytes conflict; original is preserved.')
    if not historical.exists():historical.write_bytes(original)
serialized=(json.dumps(package,ensure_ascii=False,indent=2)+'\n').encode('utf-8')
historical_target=OUT/'history.author_only'/(VERSION+'.json')
if historical_target.is_file() and historical_target.read_bytes()!=serialized:
    raise SystemExit('Requested historical version has different original bytes; select a new version rather than overwrite it.')
pending=old_path.with_suffix('.json.pending');pending.write_bytes(serialized);pending.replace(old_path)
report=dict(scope='static_source_binding_not_human_semantic_certification', content_version=package['content_version'],
    counts=dict(core=360,extended=360,source_contexts=sum(len(e['contexts']) for e in selected),
       reading_contexts=sum(c['modality']=='reading' for e in selected for c in e['contexts']),
       listening_script_contexts=sum(c['modality']=='listening' for e in selected for c in e['contexts']),actual_pronunciations=0),
    source_files=[dict(path=f.relative_to(ROOT).as_posix(),sha256=hashlib.sha256(f.read_bytes()).hexdigest()) for f in files],
    definitions_sha256=hashlib.sha256((OUT/'senses.author_only.tsv').read_bytes()).hexdigest(),
    previous_version_preserved=previous['content_version'] if previous and previous['content_version']!=VERSION else None,
    exact_source_context_counts=dict(sorted(collections.Counter(c['material_id'] for e in selected for c in e['contexts']).items())),
    package_sha256=hashlib.sha256((OUT/'learner_lexicon.json').read_bytes()).hexdigest(),
    skipped=[dict(term=a,reason=b) for a,b in skipped],
    pending=['human_sense_context_review','pronunciation_recording_review','empirical_6_5_calibration'])
(OUT/'provenance.author_only.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report['counts'],ensure_ascii=False))

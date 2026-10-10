"""Exact displayed learner-text projections; never issues runtime permission.

Source IDs mirror the readers' LearningSourceAccess contracts.  In particular a
single NPC turn and a completed replay have different IDs and different bodies.
The caller may store lexical contexts, but only current application permission
can expose one to a player.  This module performs no writes on import.
"""
from __future__ import annotations

from pathlib import Path


def displayed_materials(path: Path, document: dict) -> list[dict]:
    """Return actual display bodies with their paragraphs and lexical metadata."""
    kind = path.parent.name
    if path.name == 'field_relay_contract.json':
        return [dict(id='R6-FIELD-RELAY-R-SCOPE', version=document['version'],
                     paragraphs=document['paragraphs'],
                     source_root='r6-local-auxiliary-relay-scope',
                     access_class='acquired_local_contract', kind='reading', region='R6')]
    if path.name in {'settlement_support_contracts.json', 'old_city_life_contract.json'}:
        prefix = 'settlement:' if path.name == 'settlement_support_contracts.json' else 'old-city-life:'
        region = 'R6' if prefix == 'settlement:' else 'R5'
        return [dict(id=prefix + section['id'], version=document['version'],
                     paragraphs=section['paragraphs'], source_root=prefix + section['id'],
                     access_class='acquired_local_contract', kind='reading', region=region)
                for section in document['sections']]
    if kind == 'vessel_life':
        materials = []
        for script in document['scripts']:
            version = script.get('content_version', document['content_version'])
            for paragraph in script['paragraphs']:
                # Current sentence UI displays English without the actor prefix.
                materials.append(dict(id=script['id'] + ':' + paragraph['id'], version=version,
                    paragraphs=[dict(id=paragraph['id'], en=paragraph['en'], zh=paragraph['zh'])],
                    source_root=script['id'], access_class='current_line_permission',
                    kind='reading', region='HUB'))
            # Replay UI prefixes each turn with its actual actor and joins with two
            # newlines.  This distinct source is available only after completion
            # and a fresh permission read; a turn scope cannot unlock the replay.
            materials.append(dict(id=script['id'] + ':replay', version=version,
                paragraphs=[dict(id=paragraph['id'],
                    en=paragraph['actor'] + ': ' + paragraph['en'],
                    zh=paragraph['actor'] + '：' + paragraph['zh'])
                    for paragraph in script['paragraphs']],
                source_root=script['id'], access_class='completed_replay_permission',
                kind='reading', region='HUB'))
        return materials
    if kind == 'scoped_personal_work':
        # ListReadableResponsesAsync/OpenResponseAsync issue only current received
        # replies.  The owning reader displays response.en without an added actor.
        # An unrelated original personal-node material cannot grant this source.
        return [dict(id=response['id'],version=document['version'],
            paragraphs=[dict(id=response['id']+'-original',en=response['en'],zh=response['zh'])],
            source_root='scoped-personal:'+response['actor']+':'+response['purpose'],
            access_class='current_received_personal_reply',kind='reading',
            region='R4_R5' if response['actor'] in {'CP-MIRA','W-04'} else 'R3_R5')
            for response in document['responses']]
    materials = []
    for original in document.get('materials', []):
        material = dict(original)
        if kind == 'lina_personal':
            material.update(region={'CQ-LINA-01':'R1', 'CQ-LINA-02':'R2', 'CQ-LINA-03':'R6'}[material['node']],
                            source_root=material['node'], access_class='individual_permission', kind='reading')
        elif kind == 'r3_shift_work':
            material.update(region='R3', source_root='R3-WATCH-S1', kind='reading',
                            access_class='current_local_reply' if material.get('response_actor') else 'acquired_local_contract')
        elif kind == 'optional_encounter_contracts':
            material['region'] = material['scene'].split('-')[0]
        materials.append(material)
    return materials


def display_paragraphs(material: dict) -> list[dict]:
    if 'english' in material:
        # The actual reader shows this single string, including any internal blank
        # lines.  Keep it exact, rather than splitting and normalising its bytes.
        return [dict(id=material['id'] + '.P1', en=material['english'], zh=material.get('chinese', ''))]
    return material.get('paragraphs', [])


def paragraph_english(paragraph: dict) -> str:
    return paragraph.get('en', paragraph.get('text', ''))


def display_body(path: Path, material: dict) -> str:
    separator = '\n' if path.name == 'part05-Original_Materials_v0.1.json' else '\n\n'
    return separator.join(paragraph_english(p) for p in display_paragraphs(material))


def display_version(document: dict, material: dict) -> str:
    return material.get('content_version', material.get('version',
        document.get('content_version', document.get('version', '0.1'))))

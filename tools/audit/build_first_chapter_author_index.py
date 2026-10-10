"""Bind R0/R1 author cards to immutable sources and existing test evidence.

No tests are run here. A historical named-case match is a link, not a new assertion
about the current tree or human review. Existing original player text stays intact.
"""
from pathlib import Path
from datetime import datetime, timezone
import hashlib
import json
import re

ROOT=Path(__file__).resolve().parents[2]


def load(path):return json.loads((ROOT/path).read_text(encoding='utf-8-sig'))
def hash_text(s):return hashlib.sha256(s.encode('utf-8')).hexdigest()
def fact(p):
    raw=(ROOT/p).read_bytes()
    return dict(path=p,sha256=hashlib.sha256(raw).hexdigest(),bytes=len(raw))
def put(path,value):
    path=ROOT/path;path.parent.mkdir(parents=True,exist_ok=True)
    pending=path.with_suffix(path.suffix+'.pending');pending.write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf8');pending.replace(path)


def main():
    progress=load('reference/part08/data/Character_Progression_Reference_v0.1.json')
    rewards={r['quest_id']:r for r in progress['main_rewards'] if r['chapter'] in [0,1]}
    assert [rewards[q]['xp'] for q in ['Q-P01','Q-P02','Q-P03','Q-101','Q-102','Q-103','Q-104']]==[100,40,100,100,160,180,220]
    assert sum(r['xp'] for r in rewards.values())==900
    history=[]
    # Restrict to direct build reports, never evidence snapshots of other snapshots.
    for path in sorted((ROOT/'reports/build').glob('*/test_results.json')):
        value=load(path)
        for result in value.get('results',[]):
            if result.get('result')=='passed':history.append(dict(name=result['name'],task=result['task'],report=path.relative_to(ROOT).as_posix(),build_scope=value.get('scope'),human_signoff=value.get('human_signoff')))
    code=['tests/Mist.Tests/Campaign/CampaignCases.cs','tests/Mist.Tests/Q102/Q102Cases.cs','tests/Mist.Tests/ChapterOne/ChapterOneCases.cs','tests/Mist.Tests/Voyage/VoyageCases.cs']
    index=[]
    for file in code:
        if not (ROOT/file).exists():continue
        source=(ROOT/file).read_text(encoding='utf8')
        for task,name in re.findall(r'Add\("([^"]+)",\s*"([^"]+)"',source):
            matches=[r for r in history if r['name']==name and r['task']==task]
            index.append(dict(source=file,task=task,name=name,static_label_may_be_dynamic_prefix=name.endswith(': '),historical_passed_links=matches[-3:],current_reexecution='not_run_by_author_index',human_playthrough='not_claimed'))
    snapshots=['AGENTS.md','docs/integration/MASTER_GDD_v0.1.md','docs/integration/Integration_Decisions_v0.1.md','reports/continuous/Scope_20261009.md']+[f'docs/baseline/{p}_v0.1.md' for p in ['World_Story_Bible','Exploration_Level_Design','Quest_Event_System','English_Learning_System','Puzzle_System','Character_Progression','Combat_System','NPC_Faction_System','UI_UX_Spec','Content_AI_Pipeline']]+['reference/part04/data/Quest_Event_Reference_v0.1.json','reference/part05/data/English_Learning_Reference_v0.1.json','reference/part06/data/Puzzle_Reference_v0.1.json','reference/part08/data/Character_Progression_Reference_v0.1.json','reference/part08/data/Combat_Reference_v0.1.json','reference/part09/data/NPC_Faction_Reference_v0.1.json','content/source/campaign/first_entry.json','content/source/campaign/production_cards.json','content/source/chapter_one/chapter_one.json','content/source/part05-Original_Materials_v0.1.json']
    blocks=[]
    for file in ['content/source/campaign/first_entry.json','content/source/chapter_one/chapter_one.json']:
        j=load(file)
        for m in j['materials']:
            blocks.append(dict(id=m['id'],source=file,version=j['version'],coordinate='english',english=m['english'],english_sha256=hash_text(m['english']),chinese_sha256=hash_text(m.get('chinese','')),modality=m['modality'],scene=m['scene'],spot=m['spot'],review_status='original_unreviewed',human_review='human_pending'))
    original=load('content/source/part05-Original_Materials_v0.1.json')
    for m in original['materials']:
        if not m['id'].startswith('Q102'):continue
        for p in m['paragraphs']:
            blocks.append(dict(id=m['id'],paragraph=p['id'],source='content/source/part05-Original_Materials_v0.1.json',version=m['version'],coordinate='paragraphs.text',english=p['text'],english_sha256=hash_text(p['text']),modality=m['modality'],family=m['family_id'],review_status='frozen_original_no_edits',human_review='not_certified_by_index'))
    for region,quests in [('R0',['Q-P01','Q-P02','Q-P03']),('R1',['Q-101','Q-102','Q-103','Q-104'])]:
        cards=f'content/source/{region}/production_cards.author_only.md'
        selected=[b for b in blocks if b['id'].startswith('QP' if region=='R0' else ('Q10','Q102'))]
        sources=snapshots+[f'planning/tasks/CP-{region}-0{i}.md' for i in range(1,7)]+code+[cards]
        put(f'content/source/{region}/provenance.author_only.json',dict(version=f'{region.lower()}-author-map-20261010-1',scope='author_only_source_map_not_player_projection',player_projection=False,generated_utc=datetime.now(timezone.utc).isoformat(),source_files=[fact(s) for s in sources if (ROOT/s).exists()],original_blocks=selected,canonical_rewards=[rewards[q] for q in quests],optional_xp=progress['chapter_budgets'][0 if region=='R0' else 1]['optional_xp_envelope'],human_review='human_pending',calibration='not_run',audio_audition='not_run_by_author_index',source_text_changed=False))
        put(f'reports/chapters/{region}/qa_index_20261010.json',dict(scope='static_current_code_labels_and_historical_result_links',chapter=region,current_test_source_hashes=[fact(p) for p in code if (ROOT/p).exists()],named_case_index=index,normal_continuity_reference='ChapterOneCases.NormalComplete from actual zero-XP profile through Q104; no positive fixtures',not_run=['Tests not run by this index script','Human full chapter playthrough','Language 6.5 conditional calibration','Physical IME and screen reader','Audio human audition and rights signoff'],target_bands=dict(reading=6.5,listening=6.5,writing=6.5,speaking=None)))
    print(json.dumps(dict(scope='source mapping only',R0_quests=3,R1_quests=4,source_blocks=len(blocks),static_named_labels=len(index),normal_cumulative_xp=sum(r['xp'] for r in rewards.values()),human_signatures=0),ensure_ascii=False))


if __name__=='__main__':main()

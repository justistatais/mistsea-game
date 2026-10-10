"""Author-only source and QA links; does not run or certify tests."""
from pathlib import Path
from datetime import datetime, timezone
import hashlib
import json
import re

ROOT=Path(__file__).resolve().parents[2]
def load(path):return json.loads((ROOT/path).read_text(encoding='utf-8-sig'))
def fact(path):
    raw=(ROOT/path).read_bytes()
    return dict(path=path,sha256=hashlib.sha256(raw).hexdigest(),bytes=len(raw))
def put(path,value):
    path=ROOT/path;path.parent.mkdir(parents=True,exist_ok=True);pending=path.with_suffix(path.suffix+'.pending')
    pending.write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf8');pending.replace(path)
def main():
    package=load('content/source/r1_life/learner_package.json')
    path='tests/Mist.Tests/R1Life/R1LifeCases.cs';test_source=(ROOT/path).read_text(encoding='utf-8')
    labels=re.findall(r'Add\("(CP-R1-03)",\s*"([^"]+)"',test_source)
    history=[]
    for report in sorted((ROOT/'reports/build').glob('*/test_results.json')):
        data=load(report)
        for case in data.get('results',[]):
            if case.get('task')=='CP-R1-03' and case.get('name') in [n for _,n in labels]:
                history.append(dict(name=case['name'],result=case['result'],report=report.relative_to(ROOT).as_posix(),scope=data.get('scope'),human_signoff=data.get('human_signoff')))
    sources=['docs/baseline/World_Story_Bible_v0.1.md','docs/baseline/Quest_Event_System_v0.1.md','docs/baseline/Exploration_Level_Design_v0.1.md','content/source/part09.json','content/source/r1_life/learner_package.json','content/source/r1_life/build.author_only.py','content/source/r1_life/production_cards.author_only.md','content/source/r1_life/INTEGRATION.md','src/Mist.Core/R1Life/R1LifeModels.cs','src/Mist.Application/R1Life/R1LifeService.cs',path]
    for base in ['game/UI/Shell/Root.R1Life.cs','game/Diagnostics/Root.R1LifeClientTests.cs']:
        source=base if (ROOT/base).exists() else base+'.pending'
        if (ROOT/source).exists():sources.append(source)
    blocks=[]
    for m in package['materials']:
        for paragraph in m['paragraphs']:
            blocks.append(dict(material=m['id'],paragraph=paragraph['id'],version=package['content_version'],english_words=len(re.findall(r"\b\w+(?:['’-]\w+)*\b",paragraph['en'])),english_sha256=hashlib.sha256(paragraph['en'].encode()).hexdigest(),chinese_sha256=hashlib.sha256(paragraph['zh'].encode()).hexdigest(),human_review=m['human_review'],audio_asset=m['audio_asset']))
    original=load('content/source/r1_life/provenance.author_only.json')
    original.update(generated_utc=datetime.now(timezone.utc).isoformat(),production_source_files=[fact(p) for p in sources],paragraphs=blocks,human_review='human_pending',calibration='not_run',source_text_changed_by_index=False)
    put('content/source/r1_life/provenance.author_only.json',original)
    put('reports/chapters/R1/life_qa_index_20261010.json',dict(generated_utc=datetime.now(timezone.utc).isoformat(),scope='static_current_source_map_with_exact_historical_test_links',static_case_labels=[dict(task=t,name=n,source=path)for t,n in labels],parameterised_case_note='The FullIndoor/SmallDaylight loop expands one static Add label into two actual tests. Actual results determine execution count.',historical_results=history,current_reexecution='not_run_by_index',source_hashes=[fact(p)for p in sources],actual_entry_producer='R1LifeCases.ActualEntry(ProfileStore) -> real zero-XP prologue/Q101/travel -> 340XP unaccepted local state',source_blocks=blocks,independent_units=[dict(id='PZ-HOTEL-BOOKING',mechanism='version/group/room constraints, actual inspections/allocation and receiver',quest='SC-R1-001',reward=dict(xp=25,marks=0,parts=0)),dict(id='PZ-R1-SHOW',mechanism='finite installations/current lamp test or isolated daylight/actual people and times',quest='SC-R1-002',reward=dict(xp=0,marks=0,parts=0))],not_run=['No tests run by index','Human whole chapter play','Human language calibration','Audio recording/audition/rights','Physical IME/screenreader']))
    print(json.dumps(dict(scope='author source links, not execution',paragraphs=len(blocks),static_labels=len(labels),historical_results=len(history),human_signatures=0),ensure_ascii=False))
if __name__=='__main__':main()

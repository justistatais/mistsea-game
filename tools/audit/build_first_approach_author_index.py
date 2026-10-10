"""Read-only source inspection, then writes an author-only index. Never asserts runtime success."""
from pathlib import Path
import hashlib,json,re
ROOT=Path(__file__).resolve().parents[2]
out=ROOT/'content/source/first_approaches'
source=out/'learner_package.json'
p=json.loads(source.read_text(encoding='utf-8-sig'))
test=ROOT/'tests/Mist.Tests/FirstApproaches/FirstApproachCases.cs'
files=['src/Mist.Core/FirstApproaches/FirstApproachModels.cs','src/Mist.Application/FirstApproaches/FirstApproachService.cs','tests/Mist.Tests/FirstApproaches/FirstApproachCases.cs','game/UI/Campaign/CampaignClient.FirstApproaches.cs','game/UI/Shell/Root.FirstApproaches.cs','game/Diagnostics/CampaignClient.FirstApproachTests.cs','game/Diagnostics/Root.FirstApproachClientTests.cs']
hashes=[]
for relative in files:
    actual=ROOT/relative
    if not actual.exists():actual=actual.with_suffix('.cs.pending')
    hashes.append(dict(path=actual.relative_to(ROOT).as_posix(),sha256=hashlib.sha256(actual.read_bytes()).hexdigest()))
rows=[]
for material in p['materials']:
    text='\n\n'.join(paragraph['en'] for paragraph in material['paragraphs'])
    rows.append(dict(id=material['id'],version=material['version'],paragraphs=len(material['paragraphs']),english_words=len(re.findall(r"\b[A-Za-z]+(?:['-][A-Za-z]+)*\b",text)),english_sha256=hashlib.sha256(text.encode()).hexdigest(),audio='no_new_audio'))
result=dict(scope='author_only_source_and_static_unrun_engineering_index',source_version=p['content_version'],source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),sources=rows,registered_encounters=[e['id'] for e in p['encounters']],static_case_labels=re.findall(r'Add\("CP-R[01]-03","([^"]+)"',test.read_text(encoding='utf-8-sig')),expanded_case_count=16,files=hashes,build='not_run_by_author_agent',runtime_result='root_actual_reports_required',human_language_review='pending',human_balance_review='pending',human_full_play='not_run',physical_ime='not_run',screen_reader='not_run')
(out/'qa_index.author_only.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(encounters=result['registered_encounters'],sources=rows,engine_cases=16,human_review='pending'),ensure_ascii=False))

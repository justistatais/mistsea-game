"""Source-only provenance. No build, game state mutation or human review attestation."""
from pathlib import Path
import hashlib,json,re
ROOT=Path(__file__).resolve().parents[2]
source=ROOT/'content/source/chapter_two/learner_package.json'
contract=ROOT/'content/source/r2_life/learner_contract.json'
package=json.loads(source.read_text(encoding='utf-8-sig'))
rows=[]
for material in package['materials']:
    if material['id'] not in ('R2-O01-R-LETTER','R2-O02-R-SEEDS'):continue
    english='\n\n'.join(p['en'] for p in material['paragraphs'])
    rows.append(dict(id=material['id'],version=material['content_version'],source_root=material['source_root'],paragraphs=len(material['paragraphs']),english_sha256=hashlib.sha256(english.encode()).hexdigest(),english_words=len(re.findall(r"\b[A-Za-z]+(?:['-][A-Za-z]+)*\b",english))))
test=ROOT/'tests/Mist.Tests/R2Life/R2LifeCases.cs'
if not test.exists():test=test.with_suffix('.cs.pending')
result=dict(scope='author_only_source_provenance_and_unrun_engineering_case_index',source_package='content/source/chapter_two/learner_package.json',source_file_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),contract_sha256=hashlib.sha256(contract.read_bytes()).hexdigest(),sources=rows,static_case_labels=re.findall(r'Add\("CP-R2-03","([^"]+)"',test.read_text(encoding='utf-8')),build='not_run_by_author_agent',human_language_review='pending',human_full_play='not_run',audio='no_new_listening_asset',rights_review='pending')
(ROOT/'content/source/r2_life/provenance.author_only.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(rows,ensure_ascii=False))

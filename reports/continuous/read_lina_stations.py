import json
from pathlib import Path
for p,ids in [('content/source/chapter_one/chapter_one.json',['R1-S08']),('content/source/chapter_two/learner_package.json',['R2-S04','R2-O01']),('content/source/chapter_six/learner_package.json',['R6-S02'])]:
 d=json.loads(Path(p).read_text(encoding='utf-8-sig'))
 for s in d['scenes']:
  if s['id'] in ids:print(s['id'],json.dumps(s['spots'],ensure_ascii=False))

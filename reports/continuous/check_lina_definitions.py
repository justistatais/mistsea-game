from pathlib import Path
import json
r=Path(__file__).resolve().parents[2]
c=json.loads((r/'game/content/runtime_catalog.json').read_text(encoding='utf8'))
for d in c['definitions']['personal_nodes']:
 if d['id'].startswith('CQ-LINA'):print(d['id'],d['payload_json'])

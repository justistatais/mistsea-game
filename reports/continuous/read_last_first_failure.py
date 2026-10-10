from pathlib import Path
import json
r=Path('reports/build');p=max(r.glob('*/first_relay_client_results.json'),key=lambda p:p.stat().st_mtime);d=json.loads(p.read_text(encoding='utf8'));print(str(p));print(json.dumps([c for c in d['checks'] if not c['passed']],ensure_ascii=False,indent=2))

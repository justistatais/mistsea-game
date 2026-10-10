"""Integrity check for this design zip; not a publisher-authentication scheme."""
from pathlib import Path
import json,hashlib,sys
ROOT=Path(__file__).resolve().parents[1]
manifest=json.loads((ROOT/'PACKAGE_MANIFEST.json').read_text(encoding='utf-8'))
errors=[]
for row in manifest['files']:
 p=ROOT/row['path']
 if not p.is_file():errors.append((row['path'],'missing'));continue
 actual=hashlib.sha256(p.read_bytes()).hexdigest()
 if actual!=row['sha256']:errors.append((row['path'],'hash_mismatch'))
print(json.dumps({'checked':len(manifest['files']),'errors':errors,'note':'Hash integrity only; not a digital signature.'},ensure_ascii=False,indent=2))
sys.exit(1 if errors else 0)

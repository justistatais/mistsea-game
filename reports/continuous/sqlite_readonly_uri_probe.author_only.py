"""Actual read-only Windows URI comparison against the preserved native diagnostic."""
import json,sqlite3
from pathlib import Path
from urllib.parse import quote
R=Path(__file__).resolve().parents[2]
d=json.loads((R/'reports/continuous/sqlite_windows_path_probe.actual.json').read_text(encoding='utf8'))
p=Path(next(c['logical_path'] for c in d['cases'] if c['name']=='deep-extended-longpath-vfs' and c['open_rc']==0))
extended='\\\\?\\'+str(p)
uris=[p.as_uri()+'?mode=ro', 'file:'+quote(extended,safe='')+'?mode=ro', 'file:'+quote(extended,safe='')+'?mode=ro&vfs=win32-longpath']
results=[]
for uri in uris:
 try:
  with sqlite3.connect(uri,uri=True) as db:
   value=db.execute('PRAGMA integrity_check').fetchone()[0]
   tables=db.execute("SELECT name FROM sqlite_master WHERE type='table'").fetchall()
  results.append({'uri':uri,'integrity':value,'tables':tables,'error':None})
 except sqlite3.Error as e:results.append({'uri':uri,'error':str(e)})
out={'scope':'actual read-only Python SQLite long Windows path URI diagnostic; no product acceptance','sqlite':sqlite3.sqlite_version,'source':str(p),'source_length':len(str(p)),'results':results}
(R/'reports/continuous/sqlite_readonly_uri_probe.actual.json').write_text(json.dumps(out,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps(out,ensure_ascii=False))

from pathlib import Path, PurePosixPath
import json, re, zipfile, hashlib, shutil, sys, collections, os
BASE=Path('/mnt/data/game_design')
ROOT=BASE/'part13'/'MistSea_Codex_Handoff_v0.1'
ROOT.mkdir(parents=True, exist_ok=True)
for sub in ['docs/baseline','docs/integration','docs/decisions','planning/tasks','planning/state','planning/templates','reports','reference','tools']:
 (ROOT/sub).mkdir(parents=True,exist_ok=True)

def dump(rel,obj):
 p=ROOT/rel; p.parent.mkdir(parents=True,exist_ok=True); p.write_text(json.dumps(obj,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
def write(rel,s):
 p=ROOT/rel;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(s.strip()+'\n',encoding='utf-8')
def sha(b):return hashlib.sha256(b).hexdigest()
NAMES=['Game_Design_Core','World_Story_Bible','Exploration_Level_Design','Quest_Event_System','English_Learning_System','Puzzle_System','Character_Progression','Combat_System','NPC_Faction_System','UI_UX_Spec','Content_AI_Pipeline','Technical_Architecture']
PREFIX=['CORE','STORY','EXP','QEV','ENG','PZ','RPG','COM','NPC','UI','CAI','TECH']
DOCS={i+1:'docs/baseline/'+n+'_v0.1.md' for i,n in enumerate(NAMES)}
source_docs=[];acceptance=[];local_packages=[]
for part,n in enumerate(NAMES,1):
 p=BASE/(n+'_v0.1.md'); raw=p.read_bytes();text=raw.decode('utf-8');shutil.copy2(p,ROOT/DOCS[part])
 source_docs.append({'part':part,'path':DOCS[part],'bytes':len(raw),'sha256':sha(raw),'line_count':len(text.splitlines()),'status':'frozen_design_not_game_implementation'})
 for no,line in enumerate(text.splitlines(),1):
  m=re.match(r'^\|\s*`?([A-Z]+-AT-\d+)`?\s*\|',line)
  if m:
   cols=[x.strip() for x in line.strip().strip('|').split('|')]
   acceptance.append({'id':m.group(1),'part':part,'source':DOCS[part],'line':no,'title':cols[1] if len(cols)>1 else '', 'requirement_cells':cols[2:],'execution_status':'not_run_in_production','evidence':[]})
 for x in sorted(set(re.findall(r'\b[A-Z]+-DEV-\d+\b',text))):
  local_packages.append({'legacy_id':x,'part':part,'source':DOCS[part]})
archives=[];all_members=0;manifest_checked=0;errs=[]
for zp in sorted(BASE.glob('Part*.zip')):
 part=int(re.search(r'Part(\d+)',zp.name).group(1))
 if not 3<=part<=12:continue
 dest=ROOT/'reference'/f'part{part:02d}';dest.mkdir(exist_ok=True)
 with zipfile.ZipFile(zp) as z:
  members=[]
  for info in z.infolist():
   if info.is_dir():continue
   n=PurePosixPath(info.filename)
   if n.is_absolute() or '..' in n.parts or '\\' in str(n):raise RuntimeError('unsafe archive path')
   if n.suffix.lower() in {'.ttf','.otf','.woff','.woff2'}:raise RuntimeError('Font file must not be included')
   raw=z.read(info);target=dest/str(n);target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(raw)
   members.append({'path':str(target.relative_to(ROOT)),'archive_member':str(n),'bytes':len(raw),'sha256':sha(raw)})
   canonical=BASE/n.name
   if canonical.is_file() and canonical.suffix=='.md' and canonical.read_bytes()!=raw:errs.append({'archive':zp.name,'member':str(n),'issue':'canonical_markdown_mismatch'})
  mf=next(n for n in z.namelist() if n in ['MANIFEST.json','FILE_MANIFEST.json','PACKAGE_MANIFEST.json'])
  md=json.loads(z.read(mf))
  if isinstance(md,dict) and 'files' in md: rows=md['files']
  else: rows=[dict(v,path=k) for k,v in md.items()]
  for row in rows:
   path=row['path'];raw=z.read(path);manifest_checked+=1
   if sha(raw)!=row['sha256']:errs.append({'archive':zp.name,'member':path,'issue':'manifest_sha_mismatch'})
   size=row.get('bytes',row.get('size_bytes'))
   if size is not None and len(raw)!=size:errs.append({'archive':zp.name,'member':path,'issue':'manifest_size_mismatch'})
  all_members+=len(members)
  archives.append({'part':part,'original_archive':zp.name,'archive_sha256':sha(zp.read_bytes()),'original_archive_not_redistributed':True,'extracted_root':str(dest.relative_to(ROOT)),'member_count':len(members),'members':members})
if errs:raise RuntimeError(repr(errs))
# Copy explicit historical change orders, without rewriting sources.
changes=[]
for p in sorted((ROOT/'reference').rglob('*.md')):
 if 'CR-' in p.name and p.parent.name not in ('dependencies',):
  dest=ROOT/'docs/decisions'/p.name
  if not dest.exists(): shutil.copy2(p,dest); changes.append(str(dest.relative_to(ROOT)))
source={'date':'2026-10-09','scope':'12 provided Markdown documents + 10 provided design archives; hashes only, no production execution','documents':source_docs,'archives':archives,'manifest_entries_checked':manifest_checked,'archive_members_checked':all_members,'canonical_markdown_conflicts':[],'fonts_included':False}
dump('planning/source_inventory.json',source)
dump('planning/legacy_work_packages.json',local_packages)
# Authoritative quest data is retained verbatim for traceability, not automatically used as runtime DTO.
qdata=json.loads((ROOT/'reference/part04/data/Quest_Event_Reference_v0.1.json').read_text())
dump('planning/main_quest_index.json',{'source':'reference/part04/data/Quest_Event_Reference_v0.1.json','quests':qdata['quests'],'status':'legacy_flow_contract_only'})
print('SOURCE_AUDIT',json.dumps({'documents':len(source_docs),'archives':len(archives),'members':all_members,'manifest_entries':manifest_checked,'acceptance_definitions':len(acceptance),'legacy_packages':len(local_packages)},ensure_ascii=False))
# Used by subsequent builder stage, not a completion claim.
dump('planning/acceptance_raw.json',acceptance)
dump('planning/toolchain_baseline.json',{'checked_at':'2026-10-09','godot':'4.7.2 .NET','dotnet_sdk':'10.0.401','target_framework':'net10.0','sqlite_provider':'Microsoft.Data.Sqlite 10.0.12','renderer':'Compatibility','platform':'Windows x86_64','verification':'official_version_pages_read_only','integration_status':'not_built','lockfile_status':'must_be_produced_by_CX-002','network_ai_default':'disabled'})

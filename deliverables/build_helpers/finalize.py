from pathlib import Path
import json,hashlib,zipfile,shutil,subprocess,sys,platform,re,tempfile
BASE=Path('/mnt/data/game_design');ROOT=BASE/'part13/MistSea_Codex_Handoff_v0.1'
BUILD=BASE/'part13_build'
sys.path.insert(0,str(ROOT/'tools'))
from verify_handoff import verify
plan=json.loads((ROOT/'planning/plan.json').read_text());tr=json.loads((ROOT/'planning/acceptance_traceability.json').read_text())
# Readable cross-reference; no product pass is inferred.
rows=['# 上位验收责任索引','', '478条从十二份原规范表格提取，按原文行号保留。责任映射不是验收证据；本轮所有产品条款均未执行。每项还需CX-078在实际最终候选中核对相应覆盖，人工条款继续由实际人员确认。','','| 验收ID | 原文件行 | 条目 | 责任任务 | 产品状态 |','|---|---|---|---|---|']
for x in tr['requirements']:
 title=' / '.join([x['title']]+x['requirement_cells']).replace('|','／')
 rows.append(f"| {x['id']} | {Path(x['source']).name}:{x['line']} | {title} | {x['assigned_task']} | 未执行 |")
(ROOT/'docs/integration/Acceptance_Traceability_v0.1.md').write_text('\n'.join(rows)+'\n',encoding='utf8')
# Actual verification output from this execution, not reused previous reports.
v=verify(ROOT)
if v['failed']:raise RuntimeError(v)
(ROOT/'reports/plan_integrity_result.json').write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
shutil.copy2(BUILD/'test_results.txt',ROOT/'reports/planning_tests.log')
result={'date':'2026-10-09','environment':{'python':platform.python_version(),'platform':platform.system(),'godot':'not installed/invoked','dotnet':'not installed/invoked'},'scope':'plan data/integrity/dependency/evidence-form logic only','source_docs':12,'source_archives':10,'source_members':277,'prior_manifest_hash_entries_checked':267,'original_markdown_mismatches':0,'tasks':136,'engineering':80,'chapter_cards':48,'cross_project_human_cards':8,'gates':8,'source_product_requirements':478,'legacy_local_work_packages':98,'integrity_checks':v['check_count'],'integrity_passed':v['passed'],'unit_tests_run':32,'unit_test_failures':0,'unit_test_errors':0,'production_tasks_executed':0,'production_gates_accepted':0,'real_voice_assets_created':0,'human_calibration_performed':False,'actual_model_calls':0,'new_game_build':False,'portable_reextract_check':'pending_actual_run'}
(ROOT/'reports/Integration_Validation_v0.1.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
md=f'''# 第十三部分：实际核对与验证记录
版本0.1｜2026-10-09

## 实际执行
环境：Python {platform.python_version()}，{platform.system()}容器。没有运行Godot、.NET、Windows导出或实际语言模型。没有制作音轨，没有安排或伪造真人审校/试测。

| 项目 | 结果 |
|---|---:|
| 原始规范 | 12份，哈希保留 |
| 原始设计包 | 10个，提取277文件 |
| 原包自身清单逐条哈希核对 | 267项 |
| 与独立原件同名Markdown冲突 | 0 |
| 正式产品验收来源 | 478条，原文件/行号/责任卡可追溯 |
| 旧局部工作包 | 98个，保留责任映射 |
| 新任务卡 | 136，全部未开始 |
| 阶段门 | 8，全部关闭 |
| 计划完整性检查 | {v['check_count']}项，通过{v['passed']}项 |
| 计划工具单元检查 | 32项，失败0，错误0 |
| 实际游戏任务执行 | 0 |
| 音轨/模型调用/真人校准 | 0／0／未进行 |

## 检查证明什么
核对文件、哈希、源表引用、任务卡存在、80＋48＋8划分、DAG无环、可选在线启用的额外依赖、6.5固定目标、初始无完成证明、来源只读与无字体二进制。计划工具的反例检查拒绝未知依赖、环、错误目标、缺证据、SYNTHETIC复核、错误哈希等。

计划工具只检查证据形式、文件一致和前置关系，无法证明一个签核人真实存在、日志真实或自然语言解释正确。正式工程仍需相应真实运行、受控审核和用户验收。

## 初始计划状态
唯一第一项：CX-001。online_model=false，CX-069与HV-008显示特性未启用，不是accepted或passed。所有478产品要求保留not_run_in_production。

## 没有被这些检查证明的事情
没有C#编译、Godot引擎碰撞/输入/声音、真实Windows持久化、真实内容全量、28级终章、任意英语理解、配音审听、6.5人群标定、学习提分、发行权限或物理断电保证。

十个历史参考包的旧通过数没有被相加。本轮267条原包哈希检查只证明来源一致，不重跑或认证其旧测试。历史Python和HTML仅参考，不自动等价正式C#测试。

## 可复核命令
```text
python tools/verify_handoff.py
python -m unittest discover -s tools/tests -v
python tools/plan.py status
python tools/plan.py next
```

## 独立解压检查
待实际运行，结果随后写入本记录。包清单是哈希完整性，不是发布签名。
'''
(ROOT/'reports/Integration_Validation_v0.1.md').write_text(md,encoding='utf8')
# Packaging helper excludes only generated interpreter caches and mutable execution state.
def make_manifest():
 files=[]
 for p in sorted(ROOT.rglob('*')):
  if not p.is_file():continue
  rel=str(p.relative_to(ROOT))
  if '__pycache__' in p.parts or p.suffix=='.pyc' or rel in {'PACKAGE_MANIFEST.json','planning/state/execution.json'}:continue
  if p.suffix.lower() in {'.ttf','.otf','.woff','.woff2'}:raise RuntimeError('font found')
  raw=p.read_bytes();files.append({'path':rel,'bytes':len(raw),'sha256':hashlib.sha256(raw).hexdigest()})
 (ROOT/'PACKAGE_MANIFEST.json').write_text(json.dumps({'version':'0.1','meaning':'hash inventory, not publisher signature or game verification','mutable_exclusions':['planning/state/execution.json','future reports/execution/* and future implementation files'],'files':files},ensure_ascii=False,indent=2)+'\n',encoding='utf8')
def pack(path):
 with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
  for p in sorted(ROOT.rglob('*')):
   if p.is_file() and '__pycache__' not in p.parts and p.suffix!='.pyc':z.write(p,ROOT.name+'/'+str(p.relative_to(ROOT)))
make_manifest()
archive=BASE/'Part13_Codex_Handoff_v0.1.zip'
pack(archive)
# Actually unpack to an unrelated temp directory and execute the shipped tools, never upstream builders.
with tempfile.TemporaryDirectory(prefix='mistsea-handoff-check-') as tmp:
 with zipfile.ZipFile(archive) as z:z.extractall(tmp)
 root=Path(tmp)/ROOT.name
 commands=[[sys.executable,'tools/verify_handoff.py'],[sys.executable,'-m','unittest','discover','-s','tools/tests','-v'],[sys.executable,'tools/plan.py','status'],[sys.executable,'tools/plan.py','next']]
 logs=[]
 for argv in commands:
  run=subprocess.run(argv,cwd=root,text=True,capture_output=True,timeout=30)
  logs.append({'argv':argv,'exit_code':run.returncode,'stdout':run.stdout,'stderr':run.stderr})
  if run.returncode:raise RuntimeError(logs[-1])
(ROOT/'reports/reextract_execution.json').write_text(json.dumps({'scope':'real reextract of planning pack only','commands':logs},ensure_ascii=False,indent=2)+'\n',encoding='utf8')
result['portable_reextract_check']='passed: integrity+32 unit tests+status+next in separate temporary directory'
(ROOT/'reports/Integration_Validation_v0.1.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
md=md.replace('待实际运行，结果随后写入本记录。包清单是哈希完整性，不是发布签名。','已实际解压到独立临时目录，执行完整性检查、32项单元检查、status和next，四条命令均返回0。初始仍为CX-001可开始、0个任务accepted、0个Gate开放。日志见reextract_execution.json。包清单是哈希完整性，不是发布签名。')
(ROOT/'reports/Integration_Validation_v0.1.md').write_text(md,encoding='utf8')
# Update the report snapshot now that a manifest exists, then freeze final artifact hashes.
v=verify(ROOT) # manifest legitimately stale only for reports; do not treat this interim as final artifact validation.
# record same structural checks after regenerating the manifest below
make_manifest();v=verify(ROOT)
assert v['failed']==0,v
# The static planning result file retains original run; final package verifier checks the updated manifest.
pack(archive)
# Recheck final bytes after the report update; no report or manifest changes after this check.
with tempfile.TemporaryDirectory(prefix='mistsea-final-check-') as tmp:
 with zipfile.ZipFile(archive) as z:
  assert z.testzip() is None
  z.extractall(tmp)
 rr=Path(tmp)/ROOT.name
 for argv in [[sys.executable,'tools/verify_handoff.py'],[sys.executable,'-m','unittest','discover','-s','tools/tests','-v']]:
  run=subprocess.run(argv,cwd=rr,capture_output=True,text=True,timeout=30)
  assert run.returncode==0,(argv,run.stdout,run.stderr)
for src,dst in [('docs/integration/MASTER_GDD_v0.1.md','MASTER_GDD_v0.1.md'),('docs/integration/CODEX_IMPLEMENTATION_PLAN_v0.1.md','CODEX_IMPLEMENTATION_PLAN_v0.1.md'),('docs/integration/Integration_Decisions_v0.1.md','Integration_Decisions_v0.1.md'),('reports/Integration_Validation_v0.1.md','Integration_Validation_v0.1.md'),('CODEX_START_HERE.md','CODEX_START_HERE_v0.1.md')]:
 shutil.copy2(ROOT/src,BASE/dst)
print(json.dumps({'archive':str(archive),'bytes':archive.stat().st_size,'archive_sha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'integrity_checks':v['check_count'],'checks_passed':v['passed'],'unit_tests':32,'source_entries':277,'source_hashes_checked':267,'tasks':len(plan['tasks']),'requirements':len(tr['requirements']),'final_reextract':'passed'},ensure_ascii=False,indent=2))

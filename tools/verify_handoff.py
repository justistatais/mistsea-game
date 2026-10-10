#!/usr/bin/env python3
"""Integrity/traceability checks for this planning pack, not game tests."""
from pathlib import Path
import hashlib,json,re,sys
from plan_lib import load_json,validate_plan,safe_relative,schedule

def verify(root:Path)->dict:
    checks=[]
    def add(name,ok,detail=''):
        checks.append({'name':name,'passed':bool(ok),'detail':detail})
    inv=load_json(root/'planning/source_inventory.json');plan=load_json(root/'planning/plan.json')
    tr=load_json(root/'planning/acceptance_traceability.json');state=load_json(root/'planning/state/execution.json')
    add('十二规范存在',len(inv['documents'])==12 and all((root/d['path']).is_file() for d in inv['documents']))
    bad=[]
    for d in inv['documents']:
        path=root/d['path']
        if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest()!=d['sha256']:bad.append(d['path'])
    add('十二规范原字节',not bad,repr(bad))
    bad=[];count=0
    for a in inv['archives']:
        for m in a['members']:
            count+=1;path=root/m['path']
            if not safe_relative(m['path']) or not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest()!=m['sha256']:bad.append(m['path'])
    add('十源包277文件完整',len(inv['archives'])==10 and count==277 and not bad,repr(bad))
    ids=[]
    for d in inv['documents']:
        text=(root/d['path']).read_text(encoding='utf8')
        ids += re.findall(r'^\|\s*`?([A-Z]+-AT-\d+)`?\s*\|',text,re.M)
    add('478条原始验收完整映射',len(ids)==478 and set(ids)=={x['id'] for x in tr['requirements']} and len(ids)==len(set(ids)))
    errs=validate_plan(plan,tr);add('完整及在线分支DAG与类型约束',not errs,'; '.join(errs))
    tasks=plan['tasks'];add('136卡及8Gate数量',len(tasks)==136 and len(plan['gates'])==8)
    bad=[]
    for t in tasks:
        f=root/'planning/tasks'/f'{t["id"]}.md'
        if not f.is_file():bad.append(t['id']);continue
        s=f.read_text(encoding='utf8')
        for h in ['## 目标','## 前置条件','## 允许修改范围','## 必须交付','## 验收','## 交付证据']:
            if h not in s:bad.append(t['id']+h)
        for ref in t['references']:
            if not (root/ref).is_file():bad.append(ref)
    add('独立任务卡和必读源均可访问',not bad,repr(bad))
    bad=[]
    for x in tr['requirements']:
        lines=(root/x['source']).read_text(encoding='utf8').splitlines()
        if x['id'] not in lines[x['line']-1]:bad.append(x['id'])
        card=(root/'planning/tasks'/f'{x["assigned_task"]}.md').read_text(encoding='utf8')
        if x['id'] not in card:bad.append(x['id']+' card')
    add('验收原行与责任卡一致',not bad,repr(bad))
    lp=load_json(root/'planning/legacy_work_packages.json');all_ids={t['id'] for t in tasks}
    add('98旧工作包责任索引',len(lp)==98 and all(x['covered_by'] and set(x['covered_by'])<=all_ids for x in lp))
    chapters=[t for t in tasks if t['id'].startswith('CP-')]
    add('八章六工序及真人校准',len(chapters)==48 and sum(t['kind']=='human_calibration' for t in chapters)==8)
    sched=schedule(root,plan,state)
    add('执行账无无据完成',not sched['errors'],'; '.join(sched['errors']))
    add('默认在线两任务显式关闭',plan['features']['online_model'] is False and set(sched['feature_disabled'])=={'CX-069','HV-008'})
    fonts=[str(p.relative_to(root)) for p in root.rglob('*') if p.is_file() and p.suffix.lower() in {'.ttf','.otf','.woff','.woff2'}]
    add('没有字体二进制',not fonts,repr(fonts))
    add('整合决定与未完成账',len(load_json(root/'planning/integration_decisions.json')['decisions'])==18 and len(load_json(root/'planning/open_issues.json')['items'])==16)
    # Package manifest freezes delivered planning assets; execution state is deliberately mutable.
    mf=root/'PACKAGE_MANIFEST.json'
    bad=[]
    if mf.exists():
        manifest=load_json(mf)
        for row in manifest['files']:
            path=root/row['path']
            if not safe_relative(row['path']) or not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest()!=row['sha256']:bad.append(row['path'])
    add('交付清单哈希（清单存在时）',not bad,'清单尚未生成' if not mf.exists() else repr(bad))
    return {'scope':'planning_integrity_not_production_game_validation','check_count':len(checks),'passed':sum(c['passed'] for c in checks),'failed':sum(not c['passed'] for c in checks),'checks':checks,'production_tasks_executed_by_this_command':0}
if __name__=='__main__':
    try:
        result=verify(Path(__file__).resolve().parents[1]);print(json.dumps(result,ensure_ascii=False,indent=2));raise SystemExit(0 if result['failed']==0 else 1)
    except (OSError,ValueError,KeyError,TypeError) as exc:
        print('VERIFY_ERROR: '+str(exc),file=sys.stderr);raise SystemExit(1)

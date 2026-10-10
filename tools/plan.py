#!/usr/bin/env python3
"""Read-only task browser. Does not install tools or mark work complete."""
import argparse
from pathlib import Path
import sys
from plan_lib import load_json,validate_plan,schedule

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('action',choices=['next','status','show'])
    p.add_argument('task',nargs='?')
    p.add_argument('--limit',type=int,default=10)
    p.add_argument('--kind',choices=['engineering','human','all'],default='all')
    args=p.parse_args();root=Path(__file__).resolve().parents[1]
    try:
        plan=load_json(root/'planning/plan.json');state=load_json(root/'planning/state/execution.json')
        errors=validate_plan(plan)
        if errors:raise ValueError('\n'.join(errors))
        by_id={t['id']:t for t in plan['tasks']}
        if args.action=='show':
            if args.task not in by_id:raise ValueError('Unknown task; example: CX-001')
            print((root/'planning/tasks'/f'{args.task}.md').read_text(encoding='utf8'));return 0
        result=schedule(root,plan,state)
        if result['errors']:raise ValueError('\n'.join(result['errors']))
        if args.action=='next':
            ready=[by_id[i] for i in result['ready']]
            if args.kind=='engineering':ready=[t for t in ready if t['kind']=='engineering']
            if args.kind=='human':ready=[t for t in ready if t['kind'].startswith('human')]
            for t in ready[:max(1,args.limit)]:
                print(f"{t['id']} | {t['title']} | {t['kind']} | planning/tasks/{t['id']}.md")
            if not ready:print('没有满足前置的待开始任务；查看status与对应blocked/待审记录，不自动越过。')
        else:
            done_tasks=[i for i in result['done'] if i in by_id]
            done_gates=[i for i in result['done'] if i not in by_id]
            print(f"工程/内容/人工任务：{len(plan['tasks'])}；实际接受：{len(done_tasks)}；开放Gate：{len(done_gates)}/{len(plan['gates'])}")
            print('现在可开始：'+(', '.join(result['ready']) or '无'))
            print('默认关闭特性（不等于通过）：'+(', '.join(result['feature_disabled']) or '无'))
            for g in plan['gates']:
                print(g['id'], 'OPEN' if g['id'] in done_gates else 'CLOSED',g['title'])
        print('此工具只检查计划/证据形式；不证明日志真实或取代真人验收。')
        return 0
    except (OSError,ValueError,KeyError,TypeError) as exc:
        print(str(exc),file=sys.stderr);return 1
if __name__=='__main__':raise SystemExit(main())

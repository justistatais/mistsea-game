"""Run with: python validation/run_validation.py (Python 3.10+, no third-party packages)."""
from pathlib import Path
import sys,unittest,json,io,hashlib
from collections import Counter
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'validation'))
import combat_reference as C
stream=io.StringIO()
suite=unittest.defaultTestLoader.discover(str(ROOT/'validation'),pattern='test_combat.py')
res=unittest.TextTestRunner(stream=stream,verbosity=2).run(suite)
(ROOT/'validation/run_log.txt').write_text(stream.getvalue())
cases=[]
for b in ['BUILD-Q102-ENTRY-H','BUILD-Q102-ENTRY-M','BUILD-Q102-ENTRY-S']:
    cases.append((b,'BENCH-WORKSHOP','standard'))
for b in ['BUILD-TOWER-H','BUILD-TOWER-M','BUILD-TOWER-S']:
    cases.append((b,'BENCH-TOWER','standard'))
for b in ['BUILD-H','BUILD-M','BUILD-S','BUILD-HM','BUILD-S-ATTACKBAR']:
    for s in ['BENCH-ELITE','BENCH-GROUP','BENCH-LONG']:cases.append((b,s,'standard'))
for b in ['BUILD-Q102-ENTRY-H','BUILD-Q102-ENTRY-M','BUILD-Q102-ENTRY-S']:
    for d in ['relaxed','challenge']:cases.append((b,'BENCH-WORKSHOP',d))
rows=[]
traces=ROOT/'validation/traces';traces.mkdir(exist_ok=True)
for b,s,d in cases:
    row,combat=C.simulate(b,s,d);rows.append(row)
    # Save every generated trace: counterexamples are never dropped from the report.
    (traces/f'{b}__{s}__{d}.json').write_text(json.dumps({'result':row,'scope':C.REF['limits'],'log':combat.logs},ensure_ascii=False,indent=2)+'\n')
report={'unit_tests':res.testsRun,'failures':len(res.failures),'errors':len(res.errors),'scenario_runs':len(rows),
        'results_by_outcome':dict(Counter(r['outcome'] for r in rows)),
        'source_hashes':{f:hashlib.sha256((ROOT/f).read_bytes()).hexdigest() for f in ['validation/combat_reference.py','validation/test_combat.py','data/Combat_Reference_v0.1.json']},
        'limitations':C.REF['limits'],'results':rows}
(ROOT/'validation/validation_report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
lines=['# 战斗参考逻辑检查 v0.1','',f"自动检查：{res.testsRun}；失败：{len(res.failures)}；执行错误：{len(res.errors)}。",'',
       f"执行{len(rows)}个固定策略场景；结果：{report['results_by_outcome']}。这不是人类玩家胜率。",'',
       '| 配置 | 场景 | 模式 | 结果 | 回合 | 剩余团队生命 | 基础急救使用 | 倒地次数 |',
       '|---|---|---|---|---:|---:|---:|---:|']
for r in rows:lines.append(f"| {r['build']} | {r['scenario']} | {r['difficulty']} | {r['outcome']} | {r['rounds']} | {r['party_hp']}/{r['party_max_hp']} | {r['aid_used']} | {r['downs']} |")
lines+=['','## 覆盖与限制','',
'99项检查包含30项独立主动效果/费用检查、13项数据契约检查、42项核心边界检查、14项机关/内存回执检查。测试数量不是完整度、平衡度或6.5校准证据。',
'对比采用第七部分原有合法主修构筑和本轮低等级构筑，固定地方护卫+支援，不装遗物或改件，不携带付费增强补给。高等级示例使用各自登记武器/防具，不能写成全部都是起始装备。',
'参考演算实现：阵营阶段、稳定次序、目标锁定与列对象、主要主动技能、核心被动、护盾、暴露、压力恢复、装置、免费急救、塔站固定操作点。未实现地图几何/近战遮挡、同伴变体、全部装备触发、普通持续伤害完整计时、完整最终首领、真实存档/渲染。',
'固定贪心策略不是最优策略；个别动作选择会浪费专注。尤其不能从进攻主动栏在一次脚本中较慢，推出技能本身一定更差。',
'BENCH-LONG是长战压力台，11—16回合的结果超过主要首领节奏目标；不作为已通过的可发布主线遭遇。保留作为需继续优化的失败节奏指标。',
'塔站参数扫描使用同一当前策略与援勤普攻0.65A：420生命、32/58/42攻击下巡猎与契印失败；350生命、26/50/34攻击下三配置可胜但契印10回合；310生命下为7/7/8回合，290生命下为6/6/7回合。选310作为候选。旧模型初轮观察另存，不把不同版本的改善解释成单一因素效应。',
'终章主线保守入口为28级/校准4，而非30级。当前高等级压力台用30级，仅用于比较构筑方向；它没有证明28级终章可通关。',
'内存快照确定性测试不等于真实磁盘崩溃事务测试。全部引擎与用户试玩要求见COM-AT列表。',
]
(ROOT/'Combat_Validation_v0.1.md').write_text('\n'.join(lines)+'\n')
print(json.dumps({'tests':res.testsRun,'failures':len(res.failures),'errors':len(res.errors),'scenario_runs':len(rows),'outcomes':report['results_by_outcome']},ensure_ascii=False))
for r in rows:
    if r['scenario']=='BENCH-TOWER':print(r)
sys.exit(0 if res.wasSuccessful() else 1)

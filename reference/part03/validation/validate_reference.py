from pathlib import Path
from collections import deque, defaultdict
import json,re,hashlib
P=Path(__file__).resolve().parents[1]
source=P/'Exploration_Layout_Reference_v0.1.json'
d=json.loads(source.read_text(encoding='utf-8'))
results=[]
def ok(name,detail): results.append((name,detail))
scenes=[s for r in d['regions'] for s in r['scenes']]
ids=[s['id'] for s in scenes]
assert len(ids)==78 and len(set(ids))==78
assert sum(s['pool']=='optional' for s in scenes)==12
ok('场景登记','78个唯一场景编号；66个基础池空间（含3个共享船舱），12个可选空间。')
expected=set(re.findall(r'\bQ-(?:P|F)?\d{2,3}\b',(P/'dependencies'/'World_Story_Bible_v0.1.md').read_text(encoding='utf-8')))
used={q for s in scenes for q in s['story_nodes']}
assert expected==used,(expected-used,used-expected)
ok('主线节点引用',f'第二部分的{len(expected)}个不同叙事节点全部在场景登记中有承接；没有未知节点。承接不是任务实现完成。')
links_count=0
for r in d['regions']:
    known={s['id'] for s in r['scenes']}; adj=defaultdict(set)
    for e in r['connections']:
        assert e['a'] in known and e['b'] in known
        adj[e['a']].add(e['b']); adj[e['b']].add(e['a']);links_count+=1
    root=r['scenes'][0]['id']; seen={root};q=deque([root])
    while q:
        for v in adj[q.popleft()]-seen:
            seen.add(v);q.append(v)
    assert seen==known,(r['id'],known-seen)
ok('区域连接登记',f'{len(d["regions"])}组区域/共享据点、{links_count}条连接端点有效；在声明门禁全部满足后，每组图连通。未对其他地区门禁做完整状态穷举。')
geo=d['slice']['geometry']
allportids={p['id'] for g in geo.values() for p in g['ports']}
internal_count=0;external_count=0
for sid,g in geo.items():
    for p in g['ports']:
        if p['target_scene'] in geo:
            assert p['target_port'] in allportids
            reverse=[x for x in geo[p['target_scene']]['ports'] if x['id']==p['target_port']][0]
            assert reverse['target_port']==p['id'];internal_count+=1
        else:external_count+=1
ok('样板出口引用',f'6个场景的{internal_count}个区内方向出口（{internal_count//2}条连接）成对；{external_count}个演示外边界明确留作接入，不宣称目标几何已验收。')
def inside(pt,r):return r[0]<=pt[0]<=r[2] and r[1]<=pt[1]<=r[3]
def reachable(g,start,extra=()):
    rect=g['walk_rect'];blocked=g['blocked_rects']+list(extra)
    free={(x,y) for x in range(rect[0],rect[2]+1) for y in range(rect[1],rect[3]+1) if not any(inside((x,y),b) for b in blocked)}
    assert tuple(start) in free
    seen={tuple(start)};q=deque(seen)
    while q:
        x,y=q.popleft()
        for t in ((x+1,y),(x-1,y),(x,y+1),(x,y-1)):
            if t in free and t not in seen:seen.add(t);q.append(t)
    return seen
npoints=0
for sid,g in geo.items():
    seen=reachable(g,g['ports'][0]['stand_xy'])
    for e in g['ports']+g['spots']:
        assert tuple(e['stand_xy']) in seen,(sid,e)
        npoints+=1
ok('灰盒操作站位',f'6张简化矩形导航图中{npoints}个出口/交互站位通过单位网格四向连通检查。仅点位模型，不含实际角色半径、动画、相机或引擎寻路。')
g=geo['R1-S06']; start=next(x['stand_xy'] for x in g['ports'] if x['target_scene']=='R1-S07')
safe=reachable(g,start,g['danger_rects'])
for suffix in ['spare_bracket','shortcut_latch']:
    e=next(x for x in g['spots'] if x['id'].endswith('.'+suffix)); assert tuple(e['stand_xy']) in safe
assert tuple(next(x['stand_xy'] for x in g['ports'] if x['target_scene']=='R1-S02')) in safe
ok('非计时潜入通路','将工坊扫描区视为不可通行时，检修入口仍可到支架、内侧横闩与回广场出口；没有要求穿过风险区的默认取件路径。')
# Abstract model: location, mode, bracket(0 shelf/1 held/2 installed), battery connected,
# tested-current-config, shortcut, valid-confirmed-plan, departed.
START=('02','active',0,False,False,False,False,False)
def nexts(s,route=None):
    loc,mode,br,bat,test,cut,plan,end=s
    def out(label,**kw):
        keys=['loc','mode','br','bat','test','cut','plan','end'];vals=list(s)
        for k,v in kw.items(): vals[keys.index(k)]=v
        return label,tuple(vals)
    adj={'02':['03','05'], '03':['02'], '04':['02'], '05':['02','07'], '07':['05'], '06':[]}
    for dst in adj[loc]: yield out('move_'+dst,loc=dst)
    if loc=='02' and not end:yield out('board',loc='04')
    if cut and loc=='02':yield out('shortcut_in',loc='06')
    if cut and loc=='06':yield out('shortcut_out',loc='02')
    if loc=='05':
        if mode in ('isolated','disabled'):yield out('front_safe',loc='06')
        elif route in (None,'combat'):yield out('combat_win',loc='06',mode='disabled')
        yield out('cancel_or_retry')
    if loc=='07':
        if mode!='active':yield out('service_in',loc='06')
        if mode!='disabled':
            if route in (None,'shutdown'):yield out('shutdown',mode='isolated')
            if route in (None,'stealth'):yield out('focus',mode='focused')
            yield out('cancel_local_mode',mode='active')
    if loc=='06':
        if mode!='active':yield out('service_out',loc='07')
        if mode in ('isolated','disabled'):yield out('front_out_safe',loc='05')
        elif route in (None,'combat'):yield out('inside_front_combat_win',loc='05',mode='disabled')
        if br==0:yield out('take_bracket',br=1)
        if not cut:yield out('unlatch',cut=True)
    if loc in ('02','03','04') and not end and not plan:yield out('confirm_valid_plan',plan=True)
    if loc=='04' and not end:
        if br==1:yield out('install',br=2,test=False)
        if not bat:yield out('connect_battery',bat=True,test=False)
        if bat:yield out('disconnect_battery',bat=False,test=False)
        if br==2 and bat and not test:yield out('test_current_setup',test=True)
        if br==2 and bat and test and plan:yield out('depart',loc='02',end=True)

def model(route=None):
    seen={START};q=deque([START]);reverse=defaultdict(set);parents={START:None};labels={};transitions=0
    while q:
        s=q.popleft()
        for label,t in nexts(s,route):
            transitions+=1;reverse[t].add(s)
            if t not in seen:seen.add(t);q.append(t);parents[t]=s;labels[t]=label
    goals={s for s in seen if s[-1]}
    assert goals,'no departure'
    solvable=set(goals);q=deque(goals)
    while q:
        for t in reverse[q.popleft()]-solvable:solvable.add(t);q.append(t)
    assert solvable==seen,('dead ends',len(seen-solvable))
    target=min(goals,key=lambda x: depth(x,parents))
    trace=[]
    while parents[target] is not None:trace.append(labels[target]);target=parents[target]
    return len(seen),transitions,list(reversed(trace))
def depth(x,parents):
    n=0
    while parents[x] is not None:x=parents[x];n+=1
    return n
nstate,ntransition,_=model()
ok('样板抽象状态可解性',f'穷举得到{nstate}个可达抽象状态、{ntransition}条动作/移动转换；每个可达状态都存在到离港目标的路径。只包含声明的机械、支架、电池、测试、捷径和方案状态，不是完整游戏证明。')
traces={}
for mode in ['combat','shutdown','stealth']:
    ns,nt,path=model(mode);traces[mode]=path
    required={'combat':'combat_win','shutdown':'shutdown','stealth':'focus'}[mode]
    assert required in path,(mode,path)
    ok('单一方法可完成：'+mode,f'屏蔽另两种取件方法后仍可完成；{ns}个可达抽象状态。')
# Multiple solution orderings and guard regressions.
assert not any(lbl=='depart' for lbl,_ in nexts(('04','disabled',2,True,False,True,True,False)))
assert not any(lbl=='depart' for lbl,_ in nexts(('04','disabled',2,True,True,True,False,False)))
assert not any(lbl=='depart' for lbl,_ in nexts(('04','disabled',0,True,True,True,True,False)))
s=('04','disabled',2,True,True,True,True,False)
t=next(t for label,t in nexts(s) if label=='disconnect_battery');assert not t[4]
done=next(t for label,t in nexts(s) if label=='depart');assert done[0]=='02' and done[-1]
assert not any(t[0]=='04' for _,t in nexts(done))
assert not any(label=='depart' for label,_ in nexts(done))
ok('离港与配置回归','未测试/未确认/未安装不能离港；断开电池使测试失效；提交后回广场，不能再次登船或重复提交同一离港动作。奖励账本不在本抽象模型中。')
def valid_channel(route,pump=False,lamp=False):
    f=d['slice']['facts']
    return (route=='A' and f['A_open']) or (route=='B' and pump and f['ferry_draught_m']<=f['B_max_draught_m']) or (route=='C' and lamp)
assert not valid_channel('A',True,True)
assert not valid_channel('B',True,True)
assert not valid_channel('C',True,False)
assert valid_channel('C',False,True)
ok('航道事实回归','A仍关闭；开泵仍不满足B的吃水上限；C要求灯可用。没有把工坊方法变化写成新的可行航道。')
report=['# 第三部分：设计参考的静态核查记录','', '版本：v0.1  ',f'输入：`{source.name}`  ',f'输入SHA-256：`{hashlib.sha256(source.read_bytes()).hexdigest()}`','', '**本报告只核查设计数据、简化点位导航和一个有限状态模型。没有运行游戏引擎，不是实际存档、写作评价、音频、碰撞或用户试玩验收。**','', '| 检查 | 结果与边界 |','|---|---|']
for name,detail in results:report.append(f'| {name} | 通过：{detail} |')
report += ['', '## 三种方法的一个最短抽象通路','']
for k,t in traces.items():report.extend([f'### {k}', '', '`'+' → '.join(t)+'`',''])
report += ['## 未完成的验证','', '其他地区的门禁需要第四部分完整任务图后进行状态验证。六场景几何还需要引擎中的角色半径、视野、UI遮挡、动态碰撞与相机测试；有限状态模型未包含NPC调度、实际物品经济、完整存档分支、网络和写作评价。玩家是否觉得路线清楚、信息充分、节奏合适，需要真实试玩。','', '支持模式和构筑不能进入门禁是设计合同；本次抽象模型不包含这些输入，因此不能把它描述为已经实测所有语言/职业组合。']
(P/'Exploration_Validation_v0.1.md').write_text('\n'.join(report)+'\n',encoding='utf-8')
(P/'validation/validation_summary.json').write_text(json.dumps({'scene_count':len(ids),'story_nodes':len(expected),'region_links':links_count,'internal_ports':internal_count,'external_ports':external_count,'geometry_points':npoints,'abstract_states':nstate,'abstract_transitions':ntransition,'checks':len(results)},ensure_ascii=False,indent=2))
print(json.dumps({'checks':len(results),'states':nstate,'transitions':ntransition,'nav_points':npoints,'region_links':links_count},ensure_ascii=False))

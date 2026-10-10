"""Build bounded Part 08 design-reference data; no network or engine dependency."""
from pathlib import Path
import json
from copy import deepcopy
ROOT=Path(__file__).resolve().parents[1]
RPG=json.loads((ROOT/'data/Character_Progression_Reference_v0.1.json').read_text())
LAYOUT=json.loads((ROOT/'dependencies/Exploration_Layout_Reference_v0.1.json').read_text())
def write(name,obj): (ROOT/name).write_text(json.dumps(obj,ensure_ascii=False,indent=2)+'\n')
# Numerical parameters inherited from Part 07. Action meanings are refined here.
active={s['id']:{'id':s['id'],'name':s['name'],'ap':s['ap'],'focus':s['focus'],'cd':s['cooldown_personal_turns'],'source':'Part07'} for s in RPG['skills'] if s['kind']=='active'}
effects={
'H01':dict(kind='damage',power='A',coefficient_bp=11000,pressure=0,expose_after=True),
'H02':dict(kind='stance'),
'H04':dict(kind='damage',power='A',coefficient_bp=13000,pressure=0,multi_hit_visual=2),
'H06':dict(kind='step_shield',shield_bp=2500),
'H07':dict(kind='damage',power='A',coefficient_bp=13500,pressure=0,pressure_if_exposed=2),
'H10':dict(kind='damage',power='A',coefficient_bp=19000,pressure=2),
'M01':dict(kind='damage',power='T',coefficient_bp=9500,pressure=1),
'M02':dict(kind='trap',power='T',coefficient_bp=4500,pressure=1),
'M04':dict(kind='damage',power='T',coefficient_bp=7000,pressure=0,max_targets=2),
'M06':dict(kind='shield',shield_bp=7000),
'M07':dict(kind='damage',power='T',coefficient_bp=11500,pressure=2),
'M10':dict(kind='damage',power='T',coefficient_bp=9000,pressure=0,max_targets=3,weaken_after_bp=1500),
'S01':dict(kind='shield',shield_bp=7500),
'S02':dict(kind='damage',power='T',coefficient_bp=9500,pressure=0),
'S04':dict(kind='empower',bonus_bp=2000),
'S06':dict(kind='cleanse_shield',shield_bp=3000),
'S07':dict(kind='shield',shield_bp=4000,max_targets=3),
'S10':dict(kind='shield',shield_bp=6000,max_targets=3,guard_bp=1500),
}
for sid in active: active[sid].update(effects[sid])
# Each auxiliary/companion has two native actions, plus shared basic actions.
companion_specs=[
('CP-LINA','标线射击','CP-LINA-1',dict(kind='damage',power='A',coefficient_bp=7500,pressure=0,expose_after=True),2,1,1,
 '掩护指示','CP-LINA-2',dict(kind='shield',shield_bp=3500,guard_bp=1000),2,1,1),
('CP-OREN','敲定检查','CP-OREN-1',dict(kind='damage',power='T',coefficient_bp=7500,pressure=1),2,1,1,
 '检修绊索','CP-OREN-2',dict(kind='trap',power='T',coefficient_bp=3500,pressure=1),1,2,2),
('CP-MIRA','折光脉冲','CP-MIRA-1',dict(kind='damage',power='T',coefficient_bp=8000,pressure=0),2,0,0,
 '校正护幕','CP-MIRA-2',dict(kind='cleanse_shield',shield_bp=5500),2,2,1),
('CP-TESS','定足打击','CP-TESS-1',dict(kind='damage',power='A',coefficient_bp=8500,pressure=1),2,1,1,
 '避险绳','CP-TESS-2',dict(kind='step_ally_shield',shield_bp=3000),2,1,1),
('AUX-GUARD','压制击','AUX-GUARD-1',dict(kind='damage',power='A',coefficient_bp=8000,pressure=1),2,1,1,
 '协防','AUX-GUARD-2',dict(kind='shield',shield_bp=4500,guard_bp=1000),2,1,1),
('AUX-SUPPORT','脉冲','AUX-SUPPORT-1',dict(kind='damage',power='T',coefficient_bp=7500,pressure=0),2,0,0,
 '保护膜','AUX-SUPPORT-2',dict(kind='shield',shield_bp=6000),2,1,1),
]
companions=[]
for row in companion_specs:
 cid,n1,s1,e1,ap1,fp1,cd1,n2,s2,e2,ap2,fp2,cd2=row
 base=deepcopy(next(c for c in RPG['companions'] if c['id']==cid))
 base.update(ap=2,weapon='WP-03' if cid in ['CP-LINA','CP-TESS'] else 'WP-01' if cid=='AUX-GUARD' else 'WP-05',armor='AR-01',native=[s1,s2])
 companions.append(base)
 for name,sid,effect,ap,fp,cd in [(n1,s1,e1,ap1,fp1,cd1),(n2,s2,e2,ap2,fp2,cd2)]:
  active[sid]=dict(id=sid,name=name,ap=ap,focus=fp,cd=cd,source='Part08-companion',**effect)
# At each milestone choose exactly one improvement of the two existing actions, not a new bar.
variant_rows={
'CP-LINA':[(12,'标线伤害系数+0.10A','掩护护盾系数+0.10T'),(20,'标线命中后本人临时减伤10%，到下次自身开始','掩护允许目标在合法位置移动一次，不产生额外回合'),(24,'标线冷却1变0，仍至下个自身回合才可再用','掩护额外移除目标一项普通可驱散状态')],
'CP-OREN':[(12,'敲定检查伤害系数+0.10T','绊索伤害系数+0.10T'),(20,'敲定检查基础压力1变2','绊索触发后对事先指定友方提供0.20T盾'),(24,'敲定检查冷却1变0','个人装置容量1变2，同一敌方动作仍只触发一件')],
'CP-MIRA':[(12,'折光脉冲系数+0.10T','校正护幕系数+0.10T'),(20,'折光脉冲命中后自身获得0.15T盾','护幕普通驱散1项变2项'),(24,'折光脉冲附加1点基础压力','护幕可选两个不同友方，各0.40T基础盾，仍各驱散1项')],
'CP-TESS':[(12,'定足伤害系数+0.10A','避险绳护盾系数+0.10T'),(20,'定足目标本动作开始时已暴露则基础压力额外+1','避险绳额外驱散一项普通束缚'),(24,'定足冷却1变0','避险绳给目标15%临时减伤，至泰丝下次自身开始')],
}
variants=[dict(companion=k,level=l,left=a,right=b,choose=1,changes_native_action=True,scope='design_only_not_simulated') for k,rs in variant_rows.items() for l,a,b in rs]
# Entry levels are derived from prior rewards in published linear order. Parallel deviations must be checked separately.
entry={};xp=0;grade=0
for row in RPG['main_rewards']:
 level=max(l for l in range(1,31) if 100*(l-1)+20*(l-1)*(l-2)<=xp)
 entry[row['quest_id']]=dict(xp=xp,level=level,grade=grade,scope='published_main_order_lower_floor_requires_dependency_check')
 xp+=row['xp']
 if row['quest_id'] in ['Q-104','Q-304','Q-504','Q-604']:grade+=1
# region, quest, scene, label, encounter kind, archetype template
schedule=[
('R0','Q-P01','R0-S02','近岸脱困','tutorial','mist_scraper'),('R0','Q-P03','R0-S05','中继防卫','ordinary','relay_drone'),
('R1','Q-102','R1-S05','西工坊维护机械','elite_method','workshop'),('R1','Q-104','R1-S09','塔基侧闸替代入口','method','relay_drone'),('R1','Q-104','R1-S09','塔基外闸','ordinary','braced_warden'),('R1','Q-104','R1-S10','塔站维护者','boss','tower'),
('R2','Q-203','R2-S05','温室根道','ordinary','mist_scraper'),('R2','Q-203','R2-S06','失控输送器','method','relay_drone'),('R2','Q-203','R2-S07','转供站缠根核心','boss','grove'),
('R3','Q-301','R3-S05','轨道清障','ordinary','scrap_feeder'),('R3','Q-301','R3-S06','泵房入口','ordinary','mist_scraper'),('R3','Q-302','R3-S07','仪表检修支路','method','braced_warden'),('R3','Q-301','R3-S05','救援回程','escort','signal_runner'),('R3','Q-304','R3-S09','失控开采机','boss','drill'),
('R4','Q-402','R4-S05','试验残件','ordinary','relay_drone'),('R4','Q-402','R4-S05','备用段清理','method','coil_sentry'),('R4','Q-403','R4-S06','校准防卫','ordinary','field_projector'),('R4','Q-403','R4-S07','试验执行机','boss','trial'),
('R5','Q-501','R5-S03','北站查验冲突','method','barricade_guard'),('R5','Q-501','R5-S04','检修隧道威胁','method','scrap_feeder'),('R5','Q-502','R5-S06','北站看守组','boss','warden'),('R5','Q-504','R5-S08','归档继电防卫','ordinary','field_projector'),
('R6','Q-601','R6-S03','山口风群','ordinary','gust_ray'),('R6','Q-601','R6-S04','测站防卫','ordinary','coil_sentry'),('R6','Q-603','R6-S05','首段货载','escort','signal_runner'),('R6','Q-603','R6-S06','外线第二段','escort','barricade_guard'),('R6','Q-603','R6-S07','风脊封锁平台','boss','frontier'),('R6','Q-603','R6-S06','可选现场检修冲突','method','relay_drone'),
('RF','Q-F01','RF-S03','竖井接近','method','coil_sentry'),('RF','Q-F02','RF-S04','鲁克的指挥链','boss','rook'),('RF','Q-F03','RF-S05','继电隔离组','ordinary','field_projector'),('RF','Q-F03','RF-S06','镇潮机组','boss','core'),
]
encounters=[];counts={}
for region,qid,scene,name,kind,template in schedule:
 counts[region]=counts.get(region,0)+1
 encounters.append(dict(id=f'ENC-{region}-{counts[region]:02d}',region=region,quest_id=qid,scene_id=scene,name=name,kind=kind,template=template,entry=entry[qid],mandatory_language_check=False,kill_xp=0,repeat_rewards=False,numeric_status='candidate_not_engine_tested',planned_roster='prologue_specific' if region=='R0' else 'hero_plus_two_legal_helpers'))
for e in encounters:
 if e['id'] in ['ENC-R1-02','ENC-R1-03']:e['exclusive_route_group']='tower_base_access'
for region in range(1,7):
 # Optional contracts reuse safe arena sockets; never insert fighting into a school or private room merely to fill counts.
 scene={1:'R1-S09',2:'R2-S06',3:'R3-O02',4:'R4-S07',5:'R5-S04',6:'R6-S06'}[region]
 for n in [1,2]:
  encounters.append(dict(id=f'ENC-R{region}-O{n:02d}',region=f'R{region}',quest_id=None,scene_id=scene,name=f'地区{region}独立可选处置{n}',kind='optional_contract_slot',template='braced_warden' if n==1 else 'coil_sentry',kill_xp=0,reward_budget='existing_optional_quest_budget_no_new_kill_reward',prerequisite='registered_safe_arena_contract_not_respawn',mandatory_language_check=False,numeric_status='slot_only_no_finished_content'))
# 10 reusable enemy roles. Enemy numbers are keyed to authored encounter reference values, never to current English or live character level.
archetypes=[
('mist_scraper','雾蚀爬兽','生物','追击一名角色',3,False),('scrap_feeder','食屑群','生物','弱个体、群体压力',3,False),('gust_ray','风鳍兽','生物','指定列攻击',4,False),
('relay_drone','巡检浮机','机械','轮换单体与准备',3,False),('braced_warden','固守机卫','机械','前排保护',5,False),('coil_sentry','线圈哨机','机械','远程点名',4,False),('field_projector','护场器','机械','有限护盾、保护队友',4,False),
('barricade_guard','封锁队护卫','人类','护卫后排、非致命制服',5,False),('signal_runner','信号兵','人类','增援一次、不得无限召唤',3,False),('wound_spreader','雾蚀投射体','生物','普通持续伤害、可净化',4,False),
]
archetypes=[dict(id=i,name=n,family=f,role=role,pressure_threshold=t,boss=b,available_response='basic_actions_or_local_objectives',unavoidable_party_stun=False) for i,n,f,role,t,b in archetypes]
# Reference scenarios explicitly not a full story combat campaign.
def enemy(eid,hp,armor,threshold,pattern,boss=False,slot=0,initiative=10):
 return dict(id=eid,hp=hp,armor_bp=armor,pressure_threshold=threshold,pattern=pattern,boss=boss,slot=slot,initiative=initiative)
def hit(name,dmg,target='unit',interruptible=True):
 return dict(name=name,damage=dmg,target=target,attack=True,interruptible=interruptible)
scenarios=[
 dict(id='BENCH-WORKSHOP',name='西工坊低等级实测模型',level=3,grade=0,source_encounter='ENC-R1-01',mechanism=None,enemies=[enemy('maintenance',190,1000,4,[hit('臂击',38),hit('扫描扫击',24,'column'),hit('蓄力压击',64)])]),
 dict(id='BENCH-ELITE',name='单体精英',level=30,grade=4,mechanism=None,enemies=[enemy('elite',1500,1800,5,[hit('重击',165),hit('散射',85,'all'),hit('蓄力击',245)])]),
 dict(id='BENCH-GROUP',name='三目标群体',level=30,grade=4,mechanism=None,enemies=[enemy('g1',480,1000,3,[hit('前列击',75),hit('扫列',40,'column')],slot=0),enemy('g2',400,800,3,[hit('点名',65),hit('三连视觉',65)],slot=1),enemy('g3',400,800,3,[hit('散射',35,'all'),hit('点名',65)],slot=3)]),
 dict(id='BENCH-LONG',name='持久且不可完全打断',level=30,grade=4,mechanism=None,enemies=[enemy('heavy',2450,1400,8,[hit('压击',210),hit('全场脉冲',95,'all'),hit('重压',265)],boss=True)]),
 dict(id='BENCH-TOWER',name='塔站三项安全操作',level=5,grade=0,source_encounter='ENC-R1-04',mechanism='tower',enemies=[enemy('tower',310,1500,8,[hit('臂扫',26,'all'),hit('定点压击',50),hit('冷却甩击',34,'column')],boss=True)]),
]
# Three small support prototypes carry no paid modules, rare relics or story-only facts.
early_support=dict(id='BUILD-Q102-ENTRY-S',name='渡轮开始契印',level=3,stats=dict(body=9,strength=5,agility=5,perception=5,focus=9),skills=['S01','S02'],active=['S01','S02'],weapon='WP-05',armor='AR-01')
builds=deepcopy(RPG['builds'])+[early_support]
for tree,stats,weapon in [('H',dict(body=9,strength=11,agility=7,perception=5,focus=5),'WP-01'),('M',dict(body=9,strength=5,agility=5,perception=7,focus=11),'WP-05'),('S',dict(body=11,strength=5,agility=5,perception=5,focus=11),'WP-05')]:
 builds.append(dict(id=f'BUILD-TOWER-{tree}',name=f'塔站前{tree}',level=5,stats=stats,skills=[tree+'01',tree+'02',tree+'03'],active=[tree+'01',tree+'02'],weapon=weapon,armor='AR-01'))
# Two same-budget support reruns disambiguate bar choice from build quality.
bs=deepcopy(next(b for b in builds if b['id']=='BUILD-S'))
bs.update(id='BUILD-S-ATTACKBAR',name='契印同点数进攻主动栏',active=['S01','S02','S04','M04'])
builds.append(bs)
core=dict(id='BOSS-CORE',quest_id='Q-F03',scene_id='RF-S06',entry_reference=entry['Q-F03'],candidate_hp=1900,armor_bp=1800,pressure_threshold=8,phases=[
 dict(id='link',health_floor_percent=70,local_gates=['isolate_left','isolate_right','verify_link'],damage_before_gate='zero',transition='at_atomic_action_end'),
 dict(id='renewal',health_floor_percent=35,local_gates=['disable_relay_a','disable_relay_b','latch_bypass','verify_bypass'],damage_before_gate='clamp_to_phase_floor',adds=[dict(id='relay_a',hp=240),dict(id='relay_b',hp=240)],transition='at_atomic_action_end'),
 dict(id='damping',health_floor_hp=1,local_gates=['set_damping','verify_damping','arm_controlled_stop'],damage_before_gate='clamp_to_one',transition='all_gates_and_stability_zero_then_safe_stop'),
],reset_on_phase=False,spawn_join='next_round',phase_transition_enemy_replacement='non_damaging_recalibration_declared',outcome='program_stable_not_policy_chosen',free_supplies_total=2,authority='local_encounter_transaction_then_quest_receipt')
data=dict(meta=dict(version='0.1',status='design_reference_not_game_engine',language_target='IELTS_Academic_R_L_W_6.5',change_record='CR-COM-008-001',date='2026-10-09'),
 turn=dict(team_phases=['ally','enemy'],initiative='descending_within_side_then_persistent_actor_id',hero_ap=3,helper_ap=2,helper_basic_coefficient_bp=6500,enemy_declared_actions=1,aid_charges=2,player_slots=4,maximum_player_actors=3,maximum_enemy_actors=4,real_time_deadline=False,revive_action_eligible='next_round',focus_regen_at_living_turn_start=1),
 difficulty=[dict(id='relaxed',enemy_hp_bp=8500,enemy_damage_bp=7500),dict(id='standard',enemy_hp_bp=10000,enemy_damage_bp=10000),dict(id='challenge',enemy_hp_bp=11000,enemy_damage_bp=12000)],
 actions=list(active.values()),companions=companions,companion_variants=variants,archetypes=archetypes,encounters=encounters,scenarios=scenarios,builds=builds,core=core,
 limits=dict(full_story_balanced=False,language_calibrated=False,reference_simulation='selected_actives_core_passives_fixed_auxiliaries_no_paid_items',gear_hooks_not_executed=['WP-04','AR-05','all_relics','all_modules'],not_implemented_in_simulator=['positions_and_range','companion_milestone_variants','ordinary_damage_over_time_ticks','all_status_visuals','full_core_battle','world_commit_storage','real_engine_saving']),
 inherited=dict(no_kill_xp=True,no_language_stat_gates=True,free_respec_at_safe_point=True,no_random_base_miss_or_crit=True,no_phase_resource_reset=True))
write('data/Combat_Reference_v0.1.json',data)
print('created',len(encounters),'encounter slots;',len(active),'active action definitions;',len(builds),'builds')

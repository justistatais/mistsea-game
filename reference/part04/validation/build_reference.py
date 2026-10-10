"""Rebuild the Part 04 design reference from the bundled Part 03 registry.
This builds design data, not a production game format. Standard library only.
"""
from __future__ import annotations
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'dependencies' / 'Exploration_Layout_Reference_v0.1.json'

# id, activation predecessors, extra closing predecessors, observable closing contract
ROWS = [
('Q-P01', [], [], '安全离船路径已建立，幸存者可到达岸边；熄灯先于偏航的初步关系有现场依据'),
('Q-P02', ['Q-P01'], [], '救援索与接应点就绪，幸存者安置完成，求援意图已确认并发出'),
('Q-P03', ['Q-P02'], [], '中继恢复，独立记录与导师邮包合法取得，港口接应成立'),
('Q-101', ['Q-P03'], [], '港口接应完成，临时检修委托由授权方正式签发'),
('Q-102', ['Q-101'], [], '当前配置通过测试且行动方案已确认，明确执行民用渡轮撤离；玩家留港'),
('Q-103', ['Q-101'], ['Q-102'], '测试计划、执行时刻与独立记录形成可核验结论；必要调阅许可由指定方提供'),
('Q-104', ['Q-103'], [], '潮门塔现场安全处置和回执成立；微光号使用权与跨区委托正式移交'),
('Q-201', ['Q-104'], [], '已建立苔林接应，核实实际住民与名册差异，取得基础调阅与检修方向'),
('Q-202', ['Q-201'], [], '转移时间线和D-1交接记录核验完成；身份保护与责任删改分开记录'),
('Q-203', ['Q-201'], [], '地下支站恢复有限本地供能，苔冠塔当前安全核查完成，边界有现场回执'),
('Q-204', ['Q-202','Q-203'], [], '公开或限阅范围由玩家确认并落实，事实摘要与矿区检修批次线索已交接'),
('Q-301', ['Q-204'], [], '矿井安全返程保持，必要救援与基本安置完成，现场仪表可核验'),
('Q-302', ['Q-204'], ['Q-301'], '原始仪表、工坊副本和签章比对完成；奥伦责任有具体证据而非队伍好感'),
('Q-303', ['Q-301'], [], '满足基本保障后确认额外资源与降载节奏，所需供给已实际落实'),
('Q-304', ['Q-302','Q-303'], [], '执行受限过渡或降载，深井塔稳定，学城核验材料与样本具备合法交接'),
('Q-401', ['Q-304'], [], '地点、负荷和报告版本核验完成；记录可检验假设及不确定性'),
('Q-402', ['Q-304'], [], '失败试验复原与局部危险处置完成，反例边界和校准条件有记录'),
('Q-403', ['Q-401','Q-402'], [], '规定条件下实际执行孤网试验，保留成功、失败与适用边界；不能只读报告'),
('Q-404', ['Q-403'], [], '带限制报告已交接、棱镜塔安全核查完成、导师地点核验和准备清单发布'),
('Q-501', ['Q-404'], [], '旧地图与实际道路核对，至少一条进入北站的合法路径成立'),
('Q-502', ['Q-501'], [], '扣押区阻拦已处理，导师救出且安全返程成立；不能按语言错误永久死亡'),
('Q-503', ['Q-502'], [], '历史方案与当前指令经交叉审查，三方责任分别确认且敏感信息按权限使用'),
('Q-504', ['Q-503'], [], '保管和监督安排已确认、归档塔稳定、共同处置授权签发；鲁克接管事件落账'),
('Q-601', ['Q-504'], [], '测站完成现场核查，当前风险与供给边界回传，边境工作入口明确'),
('Q-602', ['Q-601'], ['Q-603'], '六地基础保全的实际回执齐备；增强项目可仍未完成，不以分布式路线关闭本任务'),
('Q-603', ['Q-601'], [], '本地运输包经确认，护送分段完成，风脊塔稳定并产生第六塔基础保全回执'),
('Q-604', ['Q-602','Q-603'], [], '显示方案资格与所有缺项，解除未了高风险承诺，保存并确认起航，封存准备清单'),
('Q-F01', ['Q-604'], [], '从正式通道或基础检修路径抵达接管层；没有一条入口独占后续全部玩法'),
('Q-F02', ['Q-F01'], [], '鲁克被制止且指挥链解除；不自动将机组标记稳定'),
('Q-F03', ['Q-F02'], [], '隔离装置与镇潮机组通过现场处置达到可选择的稳定状态'),
('Q-F04', ['Q-F03'], [], '满足所选方案资格，玩家确认最终行动，规则原子提交结局快照'),
('Q-F05', ['Q-F04'], [], '按结局快照呈现即时与六个月后片段；可跳过表现，不重复世界结算'),
]

EVENTS = [
 dict(id='EV-R1-OFFER-Q102', kind='offer', scene='R1-S02', priority='main', slots=['R1-S02.quay_contact'], actors=['quay_contact'], trigger='合法委托已取得且Q-102未接受；玩家主动交谈或安全场景落点', reads=['world.temporary_work_permit','quest.Q-102.lifecycle'], writes=['quest.Q-102.acceptance'], recovery='离开后保留任务卡；调度室哈文提供同等接受入口', reward='none'),
 dict(id='EV-R1-Q102-DEPART', kind='consequence', scene='R1-S04', priority='confirmed_action', slots=['R1-S04.writing_desk'], actors=['ferry_operator'], trigger='通过离港预览后玩家明确确认', reads=['ferry.configuration','ferry.current_test','proposal.confirmation','ferry.docked'], writes=['ferry.departure','quest.Q-102.settlement','player.safe_position'], recovery='由提交回执恢复表现，不能重新消耗物品或再次结算', reward='REWARD-Q102-RESOLUTION'),
 dict(id='EV-R1-ROOMS', kind='side_offer', scene='R1-O01', priority='side', slots=['R1-O01.front_desk'], actors=['inn_keeper'], trigger='旅舍到访且SC-R1-001未接受，前景与人物资源可用', reads=['world.region.R1','quest.SC-R1-001.lifecycle'], writes=['quest.SC-R1-001.acceptance'], recovery='联系旅舍柜台或受控便条入口，不占用哈文', reward='REWARD-SC-R1-001'),
 dict(id='EV-R2-REPLY', kind='consequence', scene='R2-S02', priority='story', slots=['R2-S02.reply_point'], actors=['local_correspondent'], trigger='Q-204的限阅或脱敏方案已执行，且当前内容符合访问范围', reads=['world.disclosure_scope','quest.Q-204.receipt'], writes=['story.reply_access'], recovery='延后到来信，不把受保护姓名自动写入公共摘要', reward='none'),
 dict(id='EV-HUB-LINA', kind='companion', scene='HUB-S02', priority='story', slots=['HUB-S02.correspondence'], actors=['lina'], trigger='相应个人线索已取得且莉娜已招募，玩家主动打开通信或进入安全交谈', reads=['companion.lina.recruited','knowledge.family_transfer_clue'], writes=['companion.lina.letter_stage'], recovery='保留邀请；不招募时不伪造对话，不影响必需准备', reward='REWARD-COMPANION-STAGE-TBD'),
 dict(id='EV-R6-DELIVERY', kind='work_receipt', scene='R6-S08', priority='main', slots=['R6-S08.work_receipt'], actors=[], trigger='已登记工作单按规定生产步骤取得可验证现场结果', reads=['work_order.receipt','world.tower_configuration'], writes=['work_order.acceptance'], recovery='通信失败采用既有书面回执或现场核验，不凭等待时间自动完成', reward='none'),
 dict(id='EV-HUB-REVIEW', kind='practice_offer', scene='HUB-S02', priority='practice', slots=['HUB-S02.practice_desk'], actors=[], trigger='玩家主动请求复习，或非打断式推荐被接受；使用验证材料池', reads=['learning.recommendation','content.approved_variant'], writes=['practice.instance'], recovery='可关闭或归档，无作业债务，不改主线装置', reward='no_repeatable_rpg_reward'),
 dict(id='EV-R1-AMBIENT', kind='ambient', scene='R1-S02', priority='ambient', slots=['R1-S02.decorative_edge'], actors=['decorative_worker'], trigger='渡轮离港回执已存在，且生活片段本分支未展示', reads=['ferry.departure_receipt'], writes=['presentation.seen'], recovery='环境片段不抢控制，可省略；不能替代正式任务结算', reward='none'),
]

# These are authoring predicates, never raw script strings. The future runtime
# must use owner-provided values and apply an allowlist instead of eval().
Q102 = {
 'quest_id':'Q-102','definition_revision':'0.1','status':'semantic_contract_plus_abstract_model',
 'entry': {'full_game':'Q-101正式结算后签发临时检修委托；首次沟通接受撤离委托', 'slice':'仅播种同等世界许可，不播种读听写成绩'},
 'objective_definitions': [
  {'id':'O-102-PART','kind':'historical_receipt','predicate':{'receipt_exists':'bracket.lawfully_acquired'},'mandatory_for_close':False,'note':'取得是过程里程碑；安装状态有合法来历也满足，不要求再次进入背包'},
  {'id':'O-102-FIX','kind':'current_condition','predicate':{'all':[{'eq':['bracket.location','installed']},{'eq':['battery.connected',True]},{'eq':['battery.charged',True]}]},'mandatory_for_close':True},
  {'id':'O-102-TEST','kind':'versioned_certificate','predicate':{'all':[{'eq':['test.passed',True]},{'same_revision':['test.configuration_revision','ferry.configuration_revision']}]},'mandatory_for_close':True},
  {'id':'O-102-PLAN','kind':'confirmed_proposal','predicate':{'all':[{'eq':['proposal.rule_admissible',True]},{'same_revision':['proposal.confirmed_revision','proposal.current_revision']}]},'mandatory_for_close':True},
  {'id':'O-102-DEPART','kind':'commit_result','predicate':{'receipt_exists':'Q-102.departure'},'mandatory_for_close':False,'note':'提交产生的结果，不得作为自己的提交前置'},
  {'id':'O-102-WORKORDER','kind':'optional_discovery','predicate':{'receipt_exists':'material.work_order.acquired'},'mandatory_for_close':False,'note':'未核验的Q-103线索，不自动确认事故责任'},
 ],
 'ready_predicate': {'all':[{'eq':['quest.lifecycle','active']},{'eq':['world.temporary_work_permit',True]},{'eq':['ferry.docked',True]},{'objective':'O-102-FIX'},{'objective':'O-102-TEST'},{'objective':'O-102-PLAN'}]},
 'proposal_meaning_contract': {'route':'C','reject_a':'closed','reject_b':'draught_exceeds_limit','repair':'replace_bracket','power':'charged_backup_battery','generator':'keep_off_until_isolation_confirmed'},
 'proposal_note':'这些是允许表达的命题与本任务唯一航道解，不是文本关键词匹配表；结构化内容须由玩家确认。',
 'execution_read_set':['quest_instance','content_revision','permission_revision','ferry.configuration_revision','test.configuration_revision','proposal.current_revision','ferry.departure_revision'],
 'execution_context':['loaded_world_context_nonce','active_confirmation_panel','actor_at_authorized_submit_spot'],
 'atomic_effects':['close_quest_with_departure_receipt','move_player_and协作角色_to_R1-S02_safe_spot','set_ferry_departed_and_close_gangway','keep_installed_ship_items_with_ferry','grant_single_settlement_entitlement','retain_acquired_material_access','queue_skippable_presentation'],
 'banned_gate_domains':['english_score','hint_count','subtitle_setting','materials_opened_count','writing_word_count','network_state','companion_recruited','random_drop'],
 'material_bindings': {
  'MAT-R1-A-C':'R1-S02.bulletin','MAT-R1-CORRECTION':'R1-S03.dispatch_audio','MAT-R1-B-LIMIT':'R1-S03.route_b_rule',
  'MAT-R1-VESSEL':'R1-S04.vessel_spec','MAT-R1-LOCAL-MODE':'R1-S07.maintenance_note','MAT-R1-GENERATOR':'R1-S07.main_generator_tag','MAT-R1-UNVERIFIED-ORDER':'R1-S06.work_order'},
 'materials_status':'新增制作别名，原始事实与场景出处沿用第三部分；英文终稿、录音和教学标签留第五/六部分',
}

def main() -> None:
    layout=json.loads(SOURCE.read_text(encoding='utf-8'))
    story=(ROOT/'dependencies/World_Story_Bible_v0.1.md').read_text(encoding='utf-8')
    titles=dict(re.findall(r'\|\s*(Q-(?:P\d{2}|\d{3}|F\d{2}))《([^》]+)》',story))
    scene_map={}
    for region in layout['regions']:
        for scene in region['scenes']:
            for q in scene['story_nodes']:
                scene_map.setdefault(q,[]).append(scene['id'])
    quests=[]
    for q,activation,closing,contract in ROWS:
        quests.append(dict(id=q,title=titles[q],kind='main_container',activation_after=activation,additional_close_after=closing,
          scene_refs=scene_map[q],close_contract=contract,normal_failure_policy='attempt_retry_not_permanent_main_failure',
          detail_status='detailed_sample' if q=='Q-102' else 'flow_contract_not_full_content',
          reward_ref='none' if q=='Q-F05' else ('REWARD-Q102-RESOLUTION' if q=='Q-102' else f'REWARD-{q}-TBD')))
    out=dict(meta=dict(version='0.1',kind='design_reference_not_runtime_schema',date='2026-10-08',
             note='后续第五/六/七/八/十/十二部分收敛内容与实现；只读谓词和动作语义不是完整脚本引擎'),
        task_lifecycle=['unaccepted','active','closed'],
        resolution_outcomes=['completed','completed_with_consequence','withdrawn','declined_permanently','superseded'],
        derived_active_status=['working','ready','waiting_for_declared_dependency'],
        objective_types=['historical_receipt','current_condition','versioned_certificate','confirmed_proposal','choice_commit','aggregate','optional_discovery','commit_result'],
        quests=quests,q102=Q102,event_templates=EVENTS,
        event_slots_note='沿用第三部分已存在热点；新增event slot是制作插槽别名，尚非已验证几何，不能宣称全事件站位已测试。',
        work_orders={'activation_after':'Q-404','instance_key':'preparation_package/tower_id/work_type',
          'base_receipt_task':'Q-602','frontier_receipt_producer':'Q-603',
          'global_extra_requirements':['technical_package_verified','all_enhanced_installed','transition_supply_ready','joint_agreement_accepted','all_field_trials_valid'],
          'per_tower_work':['baseline_check','enhanced_install','transition_supply','operator_assignment','field_trial'],
          'trial_dependencies':['verified_technical_package','current_enhanced_configuration','local_test_supply','qualified_operators','local_test_permission'],
          'note':'集体协议可在看到试运行结果后定案；地方试运行许可不是六方最终共同协议，避免互相等待。'},
        revision_bridge={'beacon_tested':'派生于通过测试且配置版本一致，不保留独立可写布尔值',
          'confirmed_plan':'带内容、版本与玩家确认的命题对象；不是裸布尔值',
          'departure_complete':'派生于任务世界提交回执；不由表现动画各存一个相反标志',
          'new_aliases':'新材料/事件/工作单标识由本部分登记，不假定已存在于第三部分'})
    (ROOT/'data/Quest_Event_Reference_v0.1.json').write_text(json.dumps(out,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

if __name__=='__main__':
    main()

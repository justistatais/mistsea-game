from __future__ import annotations
import copy
import itertools
import json
import unittest
from pathlib import Path
from dataclasses import replace
from npc_reference import (Consent, Disclosure, DeliveryLedger, RuleError, Relationship,
    Claims,Roster,Readiness,RewardLedger,dialogue_view,answer_known_topic,SAFE_UNKNOWN,
    tactic_tiers,fill_party,check_service,validate_joint_signatures,ending_options)
P=Path(__file__).resolve().parents[1]
DATA=json.loads((P/'data/NPC_Faction_Reference_v0.1.json').read_text())
GROWTH=json.loads((P/'dependencies/Character_Progression_Reference_v0.1.json').read_text())
COMBAT=json.loads((P/'dependencies/Combat_Reference_v0.1.json').read_text())
LAYOUT=json.loads((P/'dependencies/Exploration_Layout_Reference_v0.1.json').read_text())

class MetadataTests(unittest.TestCase):
    def test_01_counts_and_unique_ids(self):
        for key in ('profiles','supporting_profiles','factions','personal_nodes','facts','banter_seeds'):
            ids=[x['id'] for x in DATA[key]]
            self.assertEqual(len(ids),len(set(ids)))
        self.assertEqual((len(DATA['profiles']),len(DATA['factions']),len(DATA['arcs']),len(DATA['personal_nodes'])),(13,5,4,12))
    def test_02_language_stays_65(self):
        self.assertEqual(DATA['meta']['language_target'],'IELTS-A-6.5')
        self.assertTrue(all(p['language_target']=='IELTS-A-6.5' for p in DATA['profiles']))
        self.assertFalse(DATA['meta']['mechanical_relationship_bonus'])
    def test_03_all_scenes_exist(self):
        scenes={s['id'] for r in LAYOUT['regions'] for s in r['scenes']}
        for profile in DATA['profiles']:
            self.assertIn(profile['home_scene'],scenes);self.assertIn(profile['standby_scene'],scenes)
        for node in DATA['personal_nodes']:
            self.assertTrue(set(node['scenes'])<=scenes)
        for b in DATA['banter_seeds']:self.assertIn(b['scene'],scenes)
    def test_04_main_binding_is_complete_not_new_main(self):
        main={q for ch in GROWTH['chapter_budgets'] for q in ch['quest_ids']}
        self.assertEqual({r['quest'] for r in DATA['main_reactions']},main)
        self.assertEqual(len(DATA['main_reactions']),32)
        self.assertTrue(all(r['extra_reward']==0 for r in DATA['main_reactions']))
    def test_05_companion_identity_and_tactics_inherited(self):
        cp={x['id'] for x in COMBAT['companions'] if x['id'].startswith('CP-')}
        ours={x['id'] for x in DATA['profiles'] if x['role']=='companion'}
        self.assertEqual(cp,ours)
        self.assertEqual(DATA['recruitment']['tactic_levels'],sorted({v['level'] for v in COMBAT['companion_variants']}))
        self.assertEqual({p['id']:p['recruit_gate'] for p in DATA['profiles'] if p['role']=='companion'},
                         {'CP-LINA':'Q-104','CP-MIRA':'Q-202','CP-OREN':'Q-302','CP-TESS':'Q-601'})
    def test_06_arc_dependencies_acyclic(self):
        done=set();nodes=DATA['personal_nodes']
        for _ in range(len(nodes)+1):
            for n in nodes:
                if n['personal_prerequisite'] is None or n['personal_prerequisite'] in done:done.add(n['id'])
        self.assertEqual(done,{n['id'] for n in nodes})
        main={r['quest'] for r in DATA['main_reactions']}
        self.assertTrue(all(n['main_gate'] in main for n in nodes))
        self.assertTrue(all(not n['requires_recruitment'] for n in nodes))
    def test_07_regional_optional_budgets(self):
        budgets={c['chapter']:c['optional_xp_envelope'] for c in GROWTH['chapter_budgets']}
        allocated={c:sum(n['xp'] for n in DATA['personal_nodes'] if n['chapter_budget']==c) for c in budgets}
        allocated[1]+=25
        self.assertEqual(sum(allocated.values()),375)
        for ch,value in allocated.items():self.assertLessEqual(value,budgets[ch])
        self.assertEqual(sum(budgets.values()),2400)
    def test_08_reused_transport_has_no_double_reward(self):
        n=next(n for n in DATA['personal_nodes'] if n['id']=='CQ-TESS-02')
        self.assertEqual((n['xp'],n['marks'],n['parts']), (0,0,0))
        self.assertTrue(all(n['parent_reward']==0 for n in DATA['personal_nodes']))
    def test_09_life_scenes_and_known_topic_refs(self):
        self.assertEqual(len(DATA['banter_seeds']),24)
        self.assertTrue(all(b['reward']==0 for b in DATA['banter_seeds']))
        facts={f['id'] for f in DATA['facts']}
        self.assertTrue(all(set(p['known_topics'])<=facts for p in DATA['profiles']))
    def test_10_factions_are_not_six_signatories(self):
        self.assertEqual(len(DATA['factions']),5)
        self.assertEqual({m['region'] for m in DATA['regional_mandates']},{'R1','R2','R3','R4','R5','R6'})
        self.assertEqual(len(DATA['regional_mandates']),6)

class KnowledgeTests(unittest.TestCase):
    def test_11_knowing_does_not_release_future_fact(self):
        view=dialogue_view(DATA['facts'],{'K-NORTH-STATION'},{'Q-101'})
        self.assertEqual(view,[])
    def test_12_stage_does_not_teach_all_npcs(self):
        self.assertEqual(dialogue_view(DATA['facts'],set(),{'Q-404'}),[])
    def test_13_allowed_source_preserves_time_and_attribution(self):
        view=dialogue_view(DATA['facts'],{'K-MARA-D1-ALIVE'},{'Q-202'})
        self.assertEqual(view[0]['time_scope'],'D-1_only')
        self.assertNotIn('现在安全',view[0]['text'])
        v=dialogue_view(DATA['facts'],{'K-NETWORK-DEPENDENCE'},{'Q-404'})
        self.assertEqual(v[0]['kind'],'claim_attributed')
    def test_14_private_fact_not_in_generation_context(self):
        view=dialogue_view(DATA['facts'],{'K-W04-IDENTITY','K-W04-PUBLIC'},{'CQ-MIRA-01'})
        self.assertEqual([f['id'] for f in view],['K-W04-PUBLIC'])
    def test_15_permission_alone_does_not_bypass_story_gate(self):
        view=dialogue_view(DATA['facts'],{'K-BROTHER-CONTACT'},{'CQ-LINA-02'},{'K-BROTHER-CONTACT'})
        self.assertEqual(view,[])
    def test_16_guess_does_not_confirm_or_echo_spoiler(self):
        view=dialogue_view(DATA['facts'],{'K-NORTH-STATION'},{'Q-101'})
        self.assertEqual(answer_known_topic('K-NORTH-STATION',view),SAFE_UNKNOWN)
        self.assertEqual(answer_known_topic('unknown-secret',view),SAFE_UNKNOWN)
    def test_17_knowledge_view_does_not_mutate_source(self):
        before=copy.deepcopy(DATA['facts'])
        dialogue_view(DATA['facts'],{f['id'] for f in DATA['facts']},set())
        self.assertEqual(before,DATA['facts'])

class ConsentTests(unittest.TestCase):
    def setUp(self):
        self.c=Consent('N-W04',1,'source-v1',frozenset({'statement','identity'}),frozenset({'reviewer'}),'review')
        self.d=Disclosure('delivery-W04','N-W04','source-v1',frozenset({'statement'}),frozenset({'reviewer'}),'review')
        self.l=DeliveryLedger()
    def test_18_valid_preview_is_read_only_and_commit_once(self):
        p=self.l.preview(self.d,self.c)
        self.assertEqual(len(self.l.messages),0)
        a=self.l.commit('cmd1',self.d,self.c,p,True)
        b=self.l.commit('cmd1',self.d,self.c,p,True)
        self.assertEqual(a,b);self.assertEqual(len(self.l.messages),1)
    def test_19_new_command_same_delivery_still_one_message(self):
        p=self.l.preview(self.d,self.c)
        self.l.commit('cmd1',self.d,self.c,p,True)
        self.l.commit('cmd2',self.d,self.c,p,True)
        self.assertEqual(len(self.l.messages),1)
    def test_20_recipient_and_purpose_scope_cannot_expand(self):
        for changed in [replace(self.d,recipients=frozenset({'public-board'})),replace(self.d,purpose='publish'),replace(self.d,fields=frozenset({'address'}))]:
            with self.assertRaises(RuleError):self.l.preview(changed,self.c)
    def test_21_source_version_change_invalidates(self):
        with self.assertRaises(RuleError):self.l.preview(replace(self.d,source_version='source-v2'),self.c)
    def test_22_revocation_after_preview_blocks_new_delivery(self):
        p=self.l.preview(self.d,self.c)
        with self.assertRaises(RuleError):self.l.commit('cmd',self.d,replace(self.c,active=False,revision=2),p,True)
        self.assertEqual(self.l.messages,{})
    def test_23_delivered_message_is_not_erased_by_revocation(self):
        p=self.l.preview(self.d,self.c)
        a=self.l.commit('cmd',self.d,self.c,p,True)
        b=self.l.commit('cmd',self.d,replace(self.c,active=False,revision=2),p,True)
        self.assertEqual(a,b);self.assertEqual(len(self.l.messages),1)
    def test_24_edited_draft_and_old_context_rejected(self):
        p=self.l.preview(self.d,self.c)
        with self.assertRaises(RuleError):self.l.commit('cmd',replace(self.d,revision=2),self.c,p,True)
        self.l.context='branch-old:context-new'
        with self.assertRaises(RuleError):self.l.commit('cmd',self.d,self.c,p,True)
    def test_25_confirmation_required_and_command_conflict(self):
        p=self.l.preview(self.d,self.c)
        with self.assertRaises(RuleError):self.l.commit('cmd',self.d,self.c,p,False)
        self.l.commit('cmd',self.d,self.c,p,True)
        with self.assertRaises(RuleError):self.l.commit('cmd',replace(self.d,revision=2),self.c,p,True)

class RelationTests(unittest.TestCase):
    def test_26_political_disagreement_not_professional_breach(self):
        r=Relationship();r.apply('a','work_accepted',object_id='rescue')
        r.apply('b','issue_position',object_id='central_policy',value='oppose')
        self.assertEqual(r.view()['work'],'working');self.assertEqual(r.view()['positions']['central_policy'],'oppose')
    def test_27_same_work_cannot_farm_trust(self):
        r=Relationship()
        for i in range(8):r.apply(f'e{i}','work_accepted',object_id='same-rescue')
        self.assertEqual(len(r.work_receipts),1);self.assertEqual(r.view()['work'],'working')
        self.assertFalse(r.ongoing)
    def test_28_gift_learning_grade_and_tone_have_no_effect_authority(self):
        for kind in ['gift','ielts_score','grammar_error','sentiment','subtitle_used']:
            r=Relationship()
            with self.assertRaises(RuleError):r.apply('x',kind)
            self.assertEqual(r.events,{})
    def test_29_explicit_mutual_agreement_not_hidden_point_threshold(self):
        r=Relationship();r.apply('a','ongoing_agreement');self.assertEqual(r.view()['work'],'ongoing')
        r.apply('b','private_invitation');self.assertEqual(r.view()['private'],'invited')
    def test_30_pause_is_not_breach(self):
        r=Relationship();r.apply('a','promise_accepted',object_id='letter')
        r.apply('b','promise_paused',object_id='letter')
        self.assertEqual(r.view()['unresolved'],[])
    def test_31_renegotiation_not_breach(self):
        r=Relationship();r.apply('a','promise_accepted',object_id='letter')
        r.apply('b','promise_renegotiated',object_id='letter')
        self.assertEqual(r.view()['unresolved'],[])
    def test_32_repair_requires_specific_work_and_acceptance(self):
        r=Relationship();r.apply('a','ongoing_agreement');r.apply('b','private_invitation')
        r.apply('c','promise_accepted',object_id='handover');r.apply('d','promise_breached',object_id='handover')
        self.assertEqual(r.view()['work'],'repair_pending');self.assertEqual(r.view()['private'],'paused')
        with self.assertRaises(RuleError):r.apply('e','repair_accepted',object_id='handover',value='apology-only')
        r.apply('f','repair_accepted',object_id='handover',value='verified-correction-1',remedy_verified=True)
        self.assertEqual(r.view()['work'],'ongoing')
        self.assertEqual(r.promises['handover'],'breached')
        self.assertTrue(any(x['kind']=='promise_breached' for x in r.history))
    def test_33_repeat_and_conflict_receipts(self):
        r=Relationship();r.apply('a','private_invitation');r.apply('a','private_invitation')
        self.assertEqual(len(r.events),1)
        with self.assertRaises(RuleError):r.apply('a','private_pause')
    def test_34_unconfirmed_text_does_not_create_breach(self):
        r=Relationship();r.apply('a','promise_accepted',object_id='letter')
        with self.assertRaises(RuleError):r.apply('b','promise_breached',object_id='letter',confirmed=False)
        self.assertEqual(r.promises['letter'],'accepted')

class RosterAndScopeTests(unittest.TestCase):
    def test_35_claims_are_all_or_none(self):
        c=Claims();self.assertTrue(c.acquire('dialogue-A',['actor:N-HAVEL','slot:desk']))
        before=dict(c.holders)
        self.assertFalse(c.acquire('dialogue-B',['actor:N-HAVEL','slot:other']))
        self.assertEqual(c.holders,before)
    def test_36_protected_writing_defers_foreground(self):
        c=Claims();self.assertFalse(c.acquire('urgent-story',['actor:N-HAVEL'],protected_input=True))
        self.assertEqual(c.holders,{})
    def test_37_departure_is_safe_and_equipment_has_one_owner(self):
        r=Roster(members={'CP-OREN'},active=['CP-OREN'],item_owner={'RE-01':'CP-OREN'},frozen=True)
        r.request_departure('CP-OREN')
        self.assertEqual(r.settle_departures({'CP-OREN':'R3-S03'},safe=False),[])
        self.assertIn('CP-OREN',r.active)
        r.frozen=False;r.settle_departures({'CP-OREN':'R3-S03'},safe=True)
        self.assertEqual(r.active,[]);self.assertEqual(r.item_owner,{'RE-01':'collection'})
        self.assertEqual(r.location['CP-OREN'],'R3-S03')
    def test_38_rerecruit_legal_but_not_without_consent(self):
        r=Roster(local={'CP-MIRA'})
        with self.assertRaises(RuleError):r.recruit('CP-MIRA',invitation=False,willing=True,safe=True)
        r.recruit('CP-MIRA',invitation=True,willing=True,safe=True)
        self.assertEqual(r.members,{'CP-MIRA'})
    def test_39_same_actor_not_fighting_and_working(self):
        c=Claims();c.acquire('engineering',['actor:CP-OREN'])
        r=Roster(members={'CP-OREN'})
        with self.assertRaises(RuleError):r.select(['CP-OREN'],c)
        c.release('engineering');r.select(['CP-OREN'],c);self.assertEqual(r.active,['CP-OREN'])
    def test_40_late_tess_gets_all_level_tiers(self):
        self.assertEqual(tactic_tiers(28),(12,20,24));self.assertEqual(tactic_tiers(8),())
        self.assertEqual(fill_party([]),('AUX-GUARD','AUX-SUPPORT'))
        self.assertEqual(fill_party(['CP-TESS']),('CP-TESS','AUX-GUARD'))
    def test_41_service_requires_facts_and_capacity_not_social_score(self):
        s=next(s for s in DATA['cooperation_scopes'] if s['id']=='SCOPE-TRANSPORT')
        r=set(s['required_receipts']);c=set(s['required_capacity'])
        self.assertFalse(check_service(s,r,set())['eligible'])
        self.assertTrue(check_service(s,r,c)['eligible'])
        self.assertEqual(check_service(s,r,c)['result_type'],'allowed_to_dispatch_not_delivered')
    def test_42_dual_mandates_six_regions_current_terms(self):
        rows=copy.deepcopy(DATA['regional_mandates'])
        for i,row in enumerate(rows):row.update(operator_receipt=f'o{i}',representative_receipt=f'r{i}')
        self.assertTrue(validate_joint_signatures(rows,'protocol-d3-v1'))
        self.assertFalse(validate_joint_signatures(rows[:5],'protocol-d3-v1'))
        bad=copy.deepcopy(rows);bad[0]['representative']=bad[0]['operator']
        self.assertFalse(validate_joint_signatures(bad,'protocol-d3-v1'))
        bad=copy.deepcopy(rows);bad[0]['operator_scope']='operate:R2'
        self.assertFalse(validate_joint_signatures(bad,'protocol-d3-v1'))
        self.assertFalse(validate_joint_signatures(rows,'protocol-d3-v2'))
    def test_43_fixed_readiness_ignores_all_companion_positions_and_rosters(self):
        cp=['CP-LINA','CP-MIRA','CP-OREN','CP-TESS'];checked=0
        ready=Readiness(True,True,True,True,True,True,True)
        base=Readiness(True,True)
        for stances in itertools.product(('support','reserved','oppose'),repeat=4):
            for mask in itertools.product((False,True),repeat=4):
                state=[Relationship() for _ in cp]
                for i,r in enumerate(state):r.apply(f'e{i}','issue_position',object_id='ending',value=stances[i])
                roster=[a for a,t in zip(cp,mask) if t]
                # This public interface intentionally has no relation/roster parameters.
                self.assertEqual(ending_options(ready),('limited_central','local_disconnect','distributed'))
                self.assertEqual(ending_options(base),('limited_central','local_disconnect'))
                self.assertLessEqual(len(fill_party(roster[:2])),2)
                checked+=1
        self.assertEqual(checked,1296)
    def test_44_all_enhanced_requirements_really_required(self):
        found=0
        for bits in itertools.product((False,True),repeat=5):
            choices=ending_options(Readiness(True,True,*bits))
            self.assertEqual(len(choices),3 if all(bits) else 2)
            found+=len(choices)==3
        self.assertEqual(found,1)
        self.assertEqual(ending_options(Readiness(True,False,True,True,True,True,True)),())
    def test_45_rewards_once_and_old_branch_can_reearn(self):
        n=DATA['personal_nodes'][0];pre=RewardLedger();post=copy.deepcopy(pre)
        self.assertTrue(post.claim(n,{'closed:'+n['id']}))
        self.assertFalse(post.claim(n,{'closed:'+n['id']}))
        self.assertEqual(post.xp,n['xp'])
        self.assertTrue(pre.claim(n,{'closed:'+n['id']}))
    def test_46_rewards_need_real_completion_receipt(self):
        l=RewardLedger()
        with self.assertRaises(RuleError):l.claim(DATA['personal_nodes'][0],set())
        self.assertEqual(l.xp,0)
    def test_47_relation_snapshot_does_not_copy_future_history_backward(self):
        old=Relationship();old.apply('a','work_accepted',object_id='rescue')
        future=copy.deepcopy(old);future.apply('b','private_invitation')
        self.assertEqual(old.view()['private'],'not_invited')
        self.assertEqual(future.view()['private'],'invited')

class AdditionalConsistencyTests(unittest.TestCase):
    def test_48_existing_auxiliary_not_duplicated_when_filling(self):
        self.assertEqual(fill_party(['AUX-GUARD']),('AUX-GUARD','AUX-SUPPORT'))
        self.assertEqual(len(set(fill_party(['AUX-SUPPORT']))),2)
    def test_49_same_acceptance_receipt_cannot_sign_all_regions(self):
        rows=copy.deepcopy(DATA['regional_mandates'])
        for row in rows:row.update(operator_receipt='one-operator-receipt',representative_receipt='one-rep-receipt')
        self.assertFalse(validate_joint_signatures(rows,'protocol-d3-v1'))
    def test_50_mara_dated_survival_keeps_correct_source(self):
        fact=next(f for f in DATA['facts'] if f['id']=='K-MARA-D1-ALIVE')
        self.assertEqual(fact['source'],'E-03')
        self.assertEqual(fact['time_scope'],'D-1_only')

if __name__=='__main__':unittest.main(verbosity=2)

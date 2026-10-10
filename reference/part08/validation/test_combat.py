"""Executable reference checks, NOT substitutes for engine acceptance or playtests."""
import unittest,json
from fractions import Fraction
from copy import deepcopy
import combat_reference as C
import progression_reference as P

class DataTests(unittest.TestCase):
    def test_01_no_language_gate(self):
        self.assertEqual(C.REF['meta']['language_target'],'IELTS_Academic_R_L_W_6.5')
        self.assertTrue(all(not x['mandatory_language_check'] for x in C.REF['encounters']))
    def test_02_scenarios_have_units(self):
        for s in C.REF['scenarios']:
            self.assertTrue(1<=len(s['enemies'])<=4)
            self.assertEqual(len({e['id'] for e in s['enemies']}),len(s['enemies']))
            for e in s['enemies']:self.assertGreater(e['hp'],0);self.assertTrue(e['pattern'])
    def test_03_encounter_count_and_ids(self):
        es=C.REF['encounters'];self.assertEqual(len(es),44);self.assertEqual(len({e['id'] for e in es}),44)
        self.assertEqual(sum(e['kind']=='boss' for e in es),8)
        self.assertEqual(sum(e['kind']=='optional_contract_slot' for e in es),12)
    def test_04_scene_and_quest_refs(self):
        layout=json.loads((C.ROOT/'dependencies/Exploration_Layout_Reference_v0.1.json').read_text())
        scenes={s['id'] for r in layout['regions'] for s in r['scenes']};qs={q['quest_id'] for q in P.REF['main_rewards']}
        for e in C.REF['encounters']:
            self.assertIn(e['scene_id'],scenes)
            if e['quest_id'] is not None:self.assertIn(e['quest_id'],qs)
    def test_05_no_kill_experience(self):self.assertTrue(all(e['kill_xp']==0 for e in C.REF['encounters']))
    def test_06_inherited_action_costs(self):
        for a in C.REF['actions']:
            if a['source']=='Part07':
                old=P.SKILLS[a['id']]
                self.assertEqual((a['ap'],a['focus'],a['cd']),(old['ap'],old['focus'],old['cooldown_personal_turns']))
    def test_07_builds_are_legal(self):
        for b in C.REF['builds']:
            self.assertTrue(P.valid_stats(b['level'],b['stats']),b['id'])
            self.assertTrue(P.validate_skills(b['level'],b['skills'],b['active'])[0],b['id'])
    def test_08_q102_uses_entry_level(self):
        e=next(x for x in C.REF['encounters'] if x['id']=='ENC-R1-01')
        self.assertEqual((e['entry']['level'],e['entry']['grade']),(3,0))
    def test_09_tower_does_not_use_reward_calibration(self):
        e=next(x for x in C.REF['encounters'] if x['id']=='ENC-R1-04')
        self.assertEqual((e['entry']['level'],e['entry']['grade']),(5,0))
    def test_10_final_main_route_not_assumed_level30(self):
        self.assertEqual(C.REF['core']['entry_reference']['level'],28)
    def test_11_companions_have_two_native_actions(self):
        for c in C.REF['companions']:
            self.assertEqual(c['ap'],2);self.assertEqual(len(c['native']),2)
            self.assertTrue(all(x in C.ACTIONS for x in c['native']))
    def test_12_variants_are_bounded_choices(self):
        self.assertEqual(len(C.REF['companion_variants']),12)
        for x in C.REF['companion_variants']:self.assertIn(x['level'],[12,20,24]);self.assertEqual(x['choose'],1)

    def test_13_task_scene_bindings(self):
        layout=json.loads((C.ROOT/'dependencies/Exploration_Layout_Reference_v0.1.json').read_text())
        scenes={x['id']:x for r in layout['regions'] for x in r['scenes']}
        for e in C.REF['encounters']:
            if e['quest_id'] is not None:self.assertIn(e['quest_id'],scenes[e['scene_id']]['story_nodes'],e['id'])

class KernelTests(unittest.TestCase):
    def setUp(self):
        self.c=C.make_combat('BUILD-Q102-ENTRY-M','BENCH-WORKSHOP');self.c.round=1;self.c.declare()
        self.h=self.c.units['hero'];self.e=self.c.units['maintenance'];self.c.start_slot(self.h)
    def test_01_three_hero_two_helper_ap(self):
        self.assertEqual(self.h.ap,3)
        self.c.start_slot(self.c.units['guard']);self.assertEqual(self.c.units['guard'].ap,2)
    def test_02_side_order_and_stable_tie(self):
        for u in self.c.units.values():u.initiative=10
        self.assertEqual(self.c.order(),['guard','hero','support','maintenance'])
    def test_03_unspent_ap_not_carried(self):
        self.c.end_slot(self.h);self.c.round=2;self.c.start_slot(self.h);self.assertEqual(self.h.ap,3)
    def test_04_cd1_skips_a_personal_turn(self):
        self.assertTrue(self.c.skill(self.h,'M01',[self.e]))
        self.c.round=2;self.c.start_slot(self.h);self.assertFalse(self.c.can_act(self.h,'M01'))
        self.c.round=3;self.c.start_slot(self.h);self.assertTrue(self.c.can_act(self.h,'M01'))
    def test_05_zero_cost_action_no_resource_gain(self):
        u=self.c.units['support'];self.c.start_slot(u);before=u.focus
        self.assertTrue(self.c.skill(u,'AUX-SUPPORT-1',[self.e]));self.assertEqual(u.focus,before)
    def test_06_first_discount_once_and_at_least_one(self):
        a=C.ACTIONS['M01'];self.assertEqual(self.c.cost(self.h,a)[0],1)
        self.c.skill(self.h,'M01',[self.e]);self.assertEqual(self.c.cost(self.h,a)[0],2)
    def test_07_invalid_target_no_payment_or_discount_consumption(self):
        snap=self.c.fingerprint()
        self.assertFalse(self.c.skill(self.h,'M01',[self.c.units['support']]))
        self.assertEqual(snap,self.c.fingerprint())
    def test_08_no_cast_when_not_current_actor(self):
        self.c.current_actor='support';self.assertFalse(self.c.skill(self.h,'M01',[self.e]))
    def test_09_no_duplicate_aoe_targets(self):
        self.h.active=['M04'];self.h.focus=9;before=self.h.focus
        self.assertFalse(self.c.skill(self.h,'M04',[self.e,self.e]));self.assertEqual(self.h.focus,before)
    def test_10_shield_uses_max_not_sum(self):
        self.h.shield=40;self.h.shield_deadline=1000
        self.c.shield_to(self.h,self.h,7500);self.assertEqual(self.h.shield,40)
    def test_11_shield_cap(self):
        self.c.shield_to(self.h,self.h,100000);self.assertEqual(self.h.shield,self.h.max_hp//2)
    def test_12_damage_hits_armor_before_shield(self):
        self.h.shield=20;self.h.armor=2000
        old=self.h.hp;self.c.hurt(self.e,self.h,Fraction(50))
        self.assertEqual(self.h.hp,old-20);self.assertEqual(self.h.shield,0)
    def test_13_lower_guard_does_not_extend_stronger_guard(self):
        self.c.add_guard(self.h,3000,210,'strong');self.c.add_guard(self.h,1000,310,'weak')
        self.c.expire(211);self.assertEqual(self.h.guard,1000)
    def test_14_downed_source_clock_still_expires_shield(self):
        other=self.c.units['guard'];self.c.shield_to(self.h,other,7500)
        self.c.knock_down(self.h);self.c.round=3;self.assertFalse(self.c.start_slot(self.h))
        self.assertEqual(other.shield,0)
    def test_15_damage_bonus_and_guard_caps(self):
        actual=P.damage(Fraction(100),10000,99999,99999,99999,[99999,3000])
        self.assertEqual(actual,int(Fraction(100)*Fraction(3,2)*Fraction(14,10)*Fraction(55,100)*Fraction(65,100)))
    def test_16_multihit_floors_once(self):
        self.assertEqual(P.damage(Fraction(276,10),13000),35)
        self.assertEqual(P.damage(Fraction(276,10),6500)*2,34)
    def test_17_exposure_applied_after_hit(self):
        self.h.active=['H01'];self.h.focus=6;before=self.e.hp
        self.c.skill(self.h,'H01',[self.e]);raw=P.damage(self.h.A,11000,0,0,self.e.armor,[])
        self.assertEqual(before-self.e.hp,raw);self.assertTrue(self.e.exposed)
    def test_18_exposure_consumed_by_one_damage_action(self):
        self.c.expose(self.e);self.c.basic(self.h,self.e);self.assertFalse(self.e.exposed)
    def test_19_stagger_skips_ordinary_declared_action(self):
        old=self.h.hp;self.c.pressure_on(self.h,self.e,6);self.c.enemy_action(self.e)
        self.assertEqual(self.h.hp,old);self.assertFalse(self.e.pending_break);self.assertTrue(self.e.recovery_locked)
    def test_20_recovery_ignores_pressure(self):
        self.e.recovery_locked=True;self.c.pressure_on(self.h,self.e,6)
        self.assertEqual(self.e.pressure,0);self.assertFalse(self.e.pending_break)
    def test_21_full_action_releases_recovery(self):
        self.e.recovery_locked=True;self.c.enemy_action(self.e);self.assertFalse(self.e.recovery_locked)
    def test_22_boss_stagger_weakens_not_skips(self):
        self.e.boss=True;self.c.pressure_on(self.h,self.e,6);old=self.h.hp
        self.c.enemy_action(self.e);self.assertLess(self.h.hp,old);self.assertTrue(self.e.recovery_locked)
    def test_23_traps_one_per_owner_per_enemy_attack(self):
        self.h.learned.add('M08')
        self.c.traps=[C.Trap('a','hero','maintenance',1000,0,2),C.Trap('b','hero','maintenance',1000,0,2)]
        self.c.enemy_action(self.e);self.assertEqual(len(self.c.traps),1)
    def test_24_trap_can_cancel_an_attack_by_stagger(self):
        self.e.pressure=3;old=self.h.hp
        self.c.traps=[C.Trap('a','hero','maintenance',1000,1,2)]
        self.c.enemy_action(self.e);self.assertEqual(self.h.hp,old)
    def test_25_cancelled_intent_does_not_fire_traps(self):
        self.e.pending_break=True;self.e.recovery_locked=True
        self.c.traps=[C.Trap('a','hero','maintenance',1000,1,2)]
        self.c.enemy_action(self.e);self.assertEqual(len(self.c.traps),1)
    def test_26_dead_target_no_secret_retarget(self):
        self.c.knock_down(self.h);g=self.c.units['guard'];old=g.hp
        self.c.enemy_action(self.e);self.assertEqual(g.hp,old)
    def test_27_no_target_no_trap_trigger(self):
        self.c.knock_down(self.h)
        self.c.traps=[C.Trap('a','guard','maintenance',1000,0,2)]
        self.c.enemy_action(self.e);self.assertEqual(len(self.c.traps),1)
    def test_28_unit_target_follows_movement(self):
        self.h.slot=3;old=self.h.hp;self.c.enemy_action(self.e);self.assertLess(self.h.hp,old)
    def test_29_column_target_uses_committed_cells(self):
        self.e.pattern_index=1;self.c.declare();self.h.slot=3;old=self.h.hp
        self.c.enemy_action(self.e);self.assertEqual(self.h.hp,old)
    def test_30_revive_never_grants_current_round_action(self):
        self.c.knock_down(self.h);g=self.c.units['guard'];self.c.start_slot(g)
        self.assertTrue(self.c.aid_action(g,self.h,True));self.assertEqual(self.h.eligible_round,2)
        self.assertFalse(self.c.start_slot(self.h));self.assertEqual(self.h.ap,0)
    def test_31_hero_down_is_not_party_wipe(self):
        self.c.knock_down(self.h);self.assertEqual(self.c.terminal(),'active')
    def test_32_party_wipe_precedes_victory(self):
        for u in self.c.units.values():self.c.knock_down(u)
        self.assertEqual(self.c.terminal(),'defeat')
    def test_33_aid_shared_and_once_per_actor_turn(self):
        self.h.hp=10;self.assertTrue(self.c.aid_action(self.h,self.h));self.assertEqual(self.c.aid,1)
        self.assertFalse(self.c.aid_action(self.h,self.h))
    def test_34_focus_refunds_capped(self):
        self.assertTrue(self.c.refund(self.h));self.assertFalse(self.c.refund(self.h))
        self.h.turn_used.clear();self.assertTrue(self.c.refund(self.h))
        self.h.turn_used.clear();self.assertFalse(self.c.refund(self.h))
    def test_35_refund_requires_real_trap(self):
        self.h.learned.add('M09');before=self.h.focus
        self.assertFalse(self.c.recover_trap(self.h,'missing'));self.assertEqual(before,self.h.focus)
    def test_36_downed_owner_loses_devices(self):
        self.c.traps=[C.Trap('a','hero','maintenance',1000,0,2)]
        self.c.knock_down(self.h);self.assertFalse(self.c.traps)
    def test_37_no_legacy_permanent_temporary_guard(self):
        self.c.defend(self.h);self.c.round=2;self.c.start_slot(self.h);self.assertEqual(self.h.guard,0)
    def test_38_duplicate_defend_not_repeat_trigger(self):
        self.assertTrue(self.c.defend(self.h));ap=self.h.ap
        self.assertFalse(self.c.defend(self.h));self.assertEqual(self.h.ap,ap)
    def test_39_snapshot_continuation_matches(self):
        a=C.make_combat('BUILD-Q102-ENTRY-M','BENCH-WORKSHOP');C.advance_round(a);b=a.snapshot()
        C.advance_round(a);C.advance_round(b);self.assertEqual(a.fingerprint(),b.fingerprint())
    def test_40_difficulty_does_not_touch_language_or_skills(self):
        a=C.make_combat('BUILD-Q102-ENTRY-M','BENCH-WORKSHOP','relaxed')
        self.assertEqual(a.units['hero'].A,self.h.A);self.assertEqual(a.units['hero'].active,self.h.active)
        self.assertLess(a.units['maintenance'].hp,self.e.hp)

    def test_41_helper_basic_is_distinct_from_native_action(self):
        u=self.c.units['support'];self.c.start_slot(u);before=self.e.hp
        self.c.basic(u,self.e)
        expected=P.damage(u.A,6500,0,0,self.e.armor,[])
        self.assertEqual(before-self.e.hp,expected)
    def test_42_all_field_attack_includes_previously_empty_seat(self):
        self.e.pattern=[dict(name='all',damage=30,target='all',attack=True,interruptible=True)]
        self.e.pattern_index=0;self.c.declare();self.h.slot=3;old=self.h.hp
        self.c.enemy_action(self.e);self.assertLess(self.h.hp,old)

class MechanismTests(unittest.TestCase):
    def setUp(self):self.g=C.CoreGate()
    def phase1(self):
        self.g.operate('isolate_left');self.g.operate('isolate_right');self.g.operate('verify');self.g.hit(99999)
    def phase2(self):
        self.phase1();self.g.operate('disable_relay_a');self.g.operate('disable_relay_b');self.g.operate('latch_bypass');self.g.operate('verify');self.g.hit(99999)
    def test_01_locked_core_damage_zero(self):self.assertEqual(self.g.hit(99999),0);self.assertEqual(self.g.phase,1)
    def test_02_phase1_cannot_skip_two_phases(self):self.phase1();self.assertEqual(self.g.phase,2);self.assertEqual(self.g.hp,1330)
    def test_03_phase2_floor_does_not_certify_work(self):
        self.phase1();self.g.hit(99999);self.assertEqual(self.g.hp,665);self.assertEqual(self.g.phase,2)
    def test_04_bypass_requires_both_relays(self):self.phase1();self.assertFalse(self.g.operate('latch_bypass'))
    def test_05_configuration_revision_invalidates_check(self):
        self.g.operate('isolate_left');self.g.operate('isolate_right');self.g.operate('verify');self.assertTrue(self.g.ready())
        self.g.invalidate();self.assertFalse(self.g.ready())
    def test_06_phase_change_no_resource_refresh(self):
        self.g.free_aid=0;self.g.focus=1;self.g.used_once.add('once');self.phase1()
        self.assertEqual((self.g.free_aid,self.g.focus,self.g.used_once),(0,1,{'once'}))
    def test_07_final_gate_cannot_be_ignored(self):self.phase2();self.g.hit(99999);self.assertEqual(self.g.hp,1);self.assertFalse(self.g.won)
    def test_08_controlled_stop_needs_current_verification(self):
        self.phase2();self.g.operate('set_damping');self.assertFalse(self.g.operate('arm_controlled_stop'))
        self.g.operate('verify');self.assertTrue(self.g.operate('arm_controlled_stop'));self.g.hit(99999);self.assertTrue(self.g.won)
    def test_09_world_receipt_once_not_on_defeat(self):
        x=C.ReceiptLedger();self.assertFalse(x.commit('k','defeat',True,1,'p'))
        self.assertTrue(x.commit('k','victory',True,1,'p'));self.assertFalse(x.commit('k','victory',True,1,'p'))
    def test_10_duplicate_payload_conflict(self):
        x=C.ReceiptLedger();x.commit('k','victory',True,1,'p')
        with self.assertRaises(ValueError):x.commit('k','victory',True,1,'other')
    def test_11_loading_pre_commit_branch_can_complete(self):
        x=C.ReceiptLedger();old=deepcopy(x);x.commit('k','victory',True,1,'p')
        self.assertTrue(old.commit('k','victory',True,1,'p'))
    def test_12_tower_controls_not_language_result(self):
        c=C.make_combat('BUILD-TOWER-H','BENCH-TOWER');c.round=1;c.declare();u=c.units['hero'];c.start_slot(u)
        e=c.units['tower'];self.assertEqual(c.hurt(u,e,u.A),0)
        u.slot=2;self.assertFalse(c.operate_tower(u));self.assertFalse(c.controls['isolate'])

    def test_13_gate_completion_at_floor_advances_without_fake_attack(self):
        self.phase1();self.g.hit(99999);self.assertEqual(self.g.phase,2)
        self.g.operate('disable_relay_a');self.g.operate('disable_relay_b');self.g.operate('latch_bypass');self.g.operate('verify')
        self.assertEqual(self.g.phase,3);self.assertEqual(self.g.hp,665)
    def test_14_controlled_stop_at_one_requires_no_extra_hit(self):
        self.phase2();self.g.hit(99999);self.g.operate('set_damping');self.g.operate('verify')
        self.g.operate('arm_controlled_stop');self.assertTrue(self.g.won)

class ActionTests(unittest.TestCase):
    pass

def make_action_test(sid):
    def test(self):
        # Isolated effect fixture; not a legal all-talents player build.
        c=C.make_combat('BUILD-H','BENCH-ELITE');c.round=1;c.declare();h=c.units['hero'];c.start_slot(h)
        h.active=[sid];h.learned=set();h.max_focus=14;h.focus=14;h.ap=3
        a=C.ACTIONS[sid]
        friendly=a['kind'] not in ['damage','trap']
        target=h if friendly else c.enemies()[0]
        fee,_=c.cost(h,a);before=h.focus
        self.assertTrue(c.skill(h,sid,[target]),sid)
        self.assertEqual(h.focus,before-fee);self.assertEqual(h.ap,3-a['ap'])
        self.assertEqual(h.ready[sid],h.personal_turn+a['cd']+1)
        if a['kind']=='damage':self.assertLess(target.hp,target.max_hp)
        elif a['kind']=='trap':self.assertEqual(len(c.traps),1)
        elif a['kind'] in ['shield','cleanse_shield','step_shield','step_ally_shield']:self.assertGreater(target.shield,0)
        elif a['kind'] in ['stance','empower']:self.assertGreater(target.empowered,0)
    return test
for sid in sorted(C.ACTIONS):setattr(ActionTests,'test_action_'+sid.replace('-','_'),make_action_test(sid))

if __name__=='__main__':unittest.main(verbosity=2)

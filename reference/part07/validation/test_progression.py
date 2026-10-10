"""Run with python -m unittest discover -s validation -p 'test_*.py' -v."""
from pathlib import Path
from copy import deepcopy
from fractions import Fraction
from itertools import permutations
import json, unittest
from rpg_reference import *

class LevelTests(unittest.TestCase):
 def test_threshold_sequence(self):
  for l in range(1,30):self.assertEqual(threshold(l+1)-threshold(l),100+40*(l-1))
 def test_exact_boundaries(self):
  for l in range(1,31):self.assertEqual(level_from_xp(threshold(l)),l)
 def test_just_before_boundaries(self):
  for l in range(2,31):self.assertEqual(level_from_xp(threshold(l)-1),l-1)
 def test_cap_and_excess(self): self.assertEqual(level_from_xp(10**9),30)
 def test_invalid_numeric_types(self):
  for x in [-1,1.5,True]:
   with self.assertRaises((TypeError,ValueError)):level_from_xp(x)
 def test_attribute_total(self):
  self.assertEqual(point_budget(1),4);self.assertEqual(point_budget(30),62)
 def test_skill_total(self):
  self.assertEqual(skill_budget(1),0);self.assertEqual(skill_budget(2),2);self.assertEqual(skill_budget(4),3);self.assertEqual(skill_budget(30),16)
 def test_main_only_waypoints(self):
  self.assertEqual([level_from_xp(c['cumulative_main_xp']) for c in REF['chapter_budgets']],[3,6,9,13,17,22,28,30])
 def test_main_equals_level30(self):self.assertEqual(REF['economy']['main_xp_total'],threshold(30))
 def test_optional_envelope_final_departure(self):
  e=REF['chapter_budgets'][6]['cumulative_main_xp']
  self.assertEqual(e+REF['economy']['optional_xp_total'],threshold(30))
 def test_no_chapter_level_requirement(self):
  qr=json.loads((ROOT/'dependencies'/'Quest_Event_Reference_v0.1.json').read_text())
  self.assertEqual({q['id'] for q in qr['quests']},{x['quest_id'] for x in REF['main_rewards']})
 def test_bad_stat_values(self):
  for key,value in [('body',4),('strength',26),('focus',True)]:
   st={k:5 for k in ATTRS};st[key]=value;self.assertFalse(valid_stats(30,st))
 def test_unspent_points_allowed(self):self.assertTrue(valid_stats(30,{k:5 for k in ATTRS}))
 def test_overallocated_stats(self):self.assertFalse(valid_stats(1,{k:6 for k in ATTRS}))

class TalentTests(unittest.TestCase):
 def test_reference_builds(self):
  for b in REF['builds']:
   self.assertTrue(valid_stats(b['level'],b['stats']),b['id']);self.assertTrue(validate_skills(b['level'],b['skills'],b['active'])[0],b['id'])
 def test_fixed_node_counts(self):
  self.assertEqual(len(SKILLS),36)
  for t in 'HMS':
   rows=[s for s in SKILLS.values() if s['tree']==t]
   self.assertEqual(len(rows),12);self.assertEqual(sum(r['kind']=='active' for r in rows),6)
 def test_unknown_node_rejected(self):self.assertFalse(validate_skills(30,['H99'])[0])
 def test_duplicates_not_points(self):self.assertFalse(validate_skills(4,['H01','H01'])[0])
 def test_no_starting_talent_at_level1(self):self.assertFalse(validate_skills(1,['H01'])[0])
 def test_tier2_at_correct_level(self):
  ns=['H01','H02','H04']
  self.assertFalse(validate_skills(6,ns)[0]);self.assertTrue(validate_skills(8,ns)[0])
 def test_no_top_tier_bootstrap(self):self.assertFalse(validate_skills(30,['H07','H08','H09','H10','H11','H12'])[0])
 def test_lower_prerequisite_refund_rejected(self):
  b=REF['builds'][0]; ns=[s for s in b['skills'] if s!='H01'];self.assertFalse(validate_skills(30,ns,b['active'])[0])
 def test_over_budget_rejected(self):self.assertFalse(validate_skills(30,list(SKILLS)[:17])[0])
 def test_four_slot_limit(self):self.assertFalse(validate_skills(30,[f'H{i:02d}' for i in range(1,13)],['H01','H02','H04','H06','H07'])[0])
 def test_unlearned_and_passive_slot(self):
  self.assertFalse(validate_skills(8,['H01','H02','H03'],['M01'])[0]);self.assertFalse(validate_skills(8,['H01','H02','H03'],['H03'])[0])
 def test_free_respec_not_mutate_input(self):
  old={'marks':100,'parts':3,'build_revision':0};b=REF['builds'][0]
  new=apply_build(old,30,b['stats'],b['skills'],b['active'])
  self.assertEqual(old,{'marks':100,'parts':3,'build_revision':0});self.assertEqual(new['marks'],100);self.assertEqual(new['parts'],3)
 def test_failed_respec_atomic(self):
  old={'marks':70,'stats':{k:5 for k in ATTRS}};prev=deepcopy(old)
  with self.assertRaises(ValueError):apply_build(old,1,{k:25 for k in ATTRS},[],[])
  self.assertEqual(old,prev)
 def test_selection_order_not_significant(self):
  ns=['H01','H02','H04']
  for seq in permutations(ns):self.assertTrue(validate_skills(8,seq)[0])

class FormulaTests(unittest.TestCase):
 def test_sample_level4(self):
  b=REF['builds'][4];v=derived(4,b['stats']);self.assertEqual(v['hp'],116);self.assertEqual(v['A'],Fraction(154,5))
 def test_both_scaling_modes(self):
  b=REF['builds'][4];v=derived(4,b['stats'],weapon='WP-05');self.assertEqual(v['A'],v['T'])
 def test_power_grade_progression(self):
  st={k:5 for k in ATTRS}
  self.assertEqual(derived(10,st,4)['A']-derived(10,st,0)['A'],32)
 def test_focus_hard_cap(self):self.assertEqual(derived(30,{k:5 if k!='focus' else 25 for k in ATTRS},focus_bonus=99)['focus'],14)
 def test_armor_hard_cap(self):self.assertEqual(derived(30,{k:5 for k in ATTRS},4,armor_bonus_bp=9000)['armor_bp'],4500)
 def test_armor_guard_multiply_max_not_sum(self):
  self.assertEqual(damage(Fraction(1000),armor_bp=4500,guard_sources=[1500,2000,3500]),357)
 def test_additive_bonus_caps(self):self.assertEqual(damage(Fraction(100),bonus_bp=9000,exposure_bp=7000),210)
 def test_multihit_round_once(self):
  self.assertEqual(damage(Fraction(31),coefficient_bp=13000),40)
 def test_shield_does_not_stack(self):self.assertEqual(shield(70,60,300),70)
 def test_shield_cap(self):self.assertEqual(shield(0,900,301),150)
 def test_focus_cost_floor(self):
  self.assertEqual(focus_cost(4,9),1);self.assertEqual(focus_cost(0,9),0)
 def test_cooldown_intervening_turns(self):
  self.assertEqual(ready_turn(7,1),9);self.assertEqual(ready_turn(7,4),12)
 def test_minimal_unspent_build_scales(self):
  a=derived(1,{k:5 for k in ATTRS});b=derived(28,{k:5 for k in ATTRS},4)
  self.assertGreater(b['hp'],a['hp']);self.assertGreater(b['A'],a['A'])

class EconomyTests(unittest.TestCase):
 def test_reward_totals(self):
  w=Wallet()
  for r in REF['main_rewards']:w.claim_task(r)
  self.assertEqual((w.xp,w.marks,w.parts),(19140,4260,55))
 def test_same_entitlement_only_once(self):
  w=Wallet();r=next(r for r in REF['main_rewards'] if r['quest_id']=='Q-102')
  self.assertTrue(w.claim_task(r));before=deepcopy(w);self.assertFalse(w.claim_task(r));self.assertEqual(w,before)
 def test_replay_earlier_branch_gets_own_reward(self):
  base=Wallet();early=deepcopy(base);r=REF['main_rewards'][0]
  base.claim_task(r);early.claim_task(r);self.assertEqual(base,early);self.assertIsNot(base.receipts,early.receipts)
 def test_replay_after_commit_no_new_reward(self):
  w=Wallet();r=REF['main_rewards'][0];w.claim_task(r);loaded=deepcopy(w);self.assertFalse(loaded.claim_task(r))
 def test_reward_prevalidate_unknown_item(self):
  w=Wallet();prev=deepcopy(w)
  with self.assertRaises(ValueError):w.claim('bad',20,20,1,['bad_item'])
  self.assertEqual(w,prev)
 def test_methods_and_support_multipliers_identical(self):
  self.assertTrue(all(r['method_multiplier']==r['language_multiplier']==1 for r in REF['main_rewards']))
 def test_repeat_learning_and_kills_no_xp(self):
  self.assertEqual(REF['economy']['repeat_practice_xp'],0);self.assertEqual(REF['economy']['kill_xp'],0)
 def test_complete_two_module_upgrades(self):
  w=Wallet(marks=480,parts=10)
  for n in ['MOD-D1','MOD-D2','MOD-L1','MOD-L2']:self.assertTrue(w.craft(n,2))
  self.assertEqual((w.marks,w.parts),(0,0));self.assertNotIn('MOD-D1',w.owned)
 def test_modules_affordable_by_mine_end(self):
  w=Wallet()
  for r in REF['main_rewards']:
   if r['chapter']<=3:w.claim_task(r)
  self.assertEqual(rank_from_receipts(w.receipts),2)
  for n in ['MOD-D1','MOD-D2','MOD-L1','MOD-L2']:self.assertTrue(w.craft(n,2))
  self.assertGreaterEqual(w.marks,0);self.assertGreaterEqual(w.parts,0)
 def test_insufficient_recipe_atomic(self):
  w=Wallet(marks=80,parts=1);before=deepcopy(w);self.assertFalse(w.craft('MOD-D1',1));self.assertEqual(w,before)
 def test_recipe_rank_and_parent(self):
  w=Wallet(marks=1000,parts=20);self.assertFalse(w.craft('MOD-D1',0));self.assertFalse(w.craft('MOD-D2',2))
 def test_recipe_duplicate_no_spend(self):
  w=Wallet(marks=1000,parts=20);w.craft('MOD-D1',1);before=deepcopy(w);self.assertFalse(w.craft('MOD-D1',1));self.assertEqual(w,before)
 def test_no_bound_equipment_sale(self):self.assertTrue(all(i['sell_value']==0 for i in REF['items']))
 def test_parts_roundtrip_loses_currency(self):self.assertLess(REF['trade_rules']['parts_sell'],REF['trade_rules']['parts_buy'])
 def test_hotel_never_draws_player_compensation(self):
  h=REF['hotel'];self.assertFalse(h['deduct_from_player']);self.assertEqual(h['compensation_options'][0]['payer'],'hotel_account');self.assertLessEqual(h['xp'],REF['chapter_budgets'][1]['optional_xp_envelope'])

class EngineeringTests(unittest.TestCase):
 def test_six_tower_totals(self):
  for kind,total in [('base_requirement',REF['engineering']['baseline_total']),('enhanced_requirement',REF['engineering']['enhancement_total'])]:
   actual={k:sum(t[kind][k] for t in REF['engineering']['towers']) for k in total};self.assertEqual(actual,total)
 def test_all_procurement_quantities_match(self):
  accum={}
  for r in REF['engineering']['procurement']:accum[r['resource']]=accum.get(r['resource'],0)+r['quantity']
  self.assertEqual(accum,REF['engineering']['enhancement_total'])
 def test_six_complete_installations_conserve(self):
  initial=REF['engineering']['enhancement_total'];s=Stock(dict(initial))
  for t in REF['engineering']['towers']:
   self.assertTrue(s.reserve(t['id'],t['enhanced_requirement']));self.assertTrue(s.commit(t['id']))
  self.assertTrue(all(x==0 for x in s.available.values()));self.assertEqual(s.totals(),initial)
 def test_reserve_is_atomic(self):
  s=Stock({'controller':1,'coupler':1});before=deepcopy(s)
  self.assertFalse(s.reserve('x',{'controller':1,'coupler':2}));self.assertEqual(s,before)
 def test_no_double_reservation(self):
  s=Stock({'controller':1});self.assertTrue(s.reserve('a',{'controller':1}));self.assertFalse(s.reserve('b',{'controller':1}))
 def test_repeat_same_reserve_idempotent(self):
  s=Stock({'controller':1});s.reserve('a',{'controller':1});before=deepcopy(s);self.assertTrue(s.reserve('a',{'controller':1}));self.assertEqual(s,before)
 def test_cancel_unused_restores(self):
  s=Stock({'controller':1});s.reserve('a',{'controller':1});self.assertTrue(s.cancel('a'));self.assertEqual(s.available,{'controller':1});self.assertTrue(s.reserve('a',{'controller':1}))
 def test_installed_not_refunded(self):
  s=Stock({'controller':1});s.reserve('a',{'controller':1});s.commit('a');self.assertFalse(s.cancel('a'));self.assertFalse(s.commit('a'));self.assertEqual(s.available['controller'],0)
 def test_zero_and_negative_reserve_rejected(self):
  for n in [0,-1]:
   with self.assertRaises(ValueError):Stock({'a':1}).reserve('a',{'a':n})
 def test_personal_wallet_not_engineering_stock(self):
  w=Wallet(marks=0,parts=0);s=Stock(dict(REF['engineering']['enhancement_total']));before=deepcopy(w)
  for t in REF['engineering']['towers']:s.reserve(t['id'],t['enhanced_requirement']);s.commit(t['id'])
  self.assertEqual(w,before)
 def test_receipt_does_not_automatically_grant_stock(self):self.assertEqual(REF['engineering']['initial_player_owned_supply'],{})

class IntegrationTests(unittest.TestCase):
 def test_english_target_fixed(self):self.assertEqual(REF['meta']['english_target'],{'reading':6.5,'listening':6.5,'writing':6.5})
 def test_gear_counts_ids_unique(self):
  self.assertEqual(len(ITEMS),28)
  for kind,n in [('weapon',6),('armor',6),('relic',12),('field_tool',4)]:self.assertEqual(sum(i['kind']==kind for i in ITEMS.values()),n)
 def test_rank_receipts_only(self):
  self.assertEqual(rank_from_receipts({'read_eighteen_reports','ielts_7'}),0)
  self.assertEqual(rank_from_receipts({'Q-604:resolution'}),4)
 def test_shared_relic_not_duplicated(self):self.assertFalse(equip_relics(['RE-01'],[['RE-01']],{'RE-01'},2))
 def test_relic_slot_story_bound(self):
  owned={'RE-01','RE-02'};self.assertFalse(equip_relics(['RE-01','RE-02'],[],owned,1));self.assertTrue(equip_relics(['RE-01','RE-02'],[],owned,2))
 def test_relic_ownership_required(self):self.assertFalse(equip_relics(['RE-03'],[],{'RE-01'},2))
 def test_late_companion_profiles(self):
  for cp in REF['companions']:
   for l in [1,4,12,24,30]:
    st=auto_profile(l,cp['profile']);self.assertTrue(valid_stats(l,st));self.assertEqual(sum(v-5 for v in st.values()),point_budget(l))
 def test_learning_services_cost_zero(self):
  for f in ['respec_cost','calibration_cost','repair_cost','learning_assistance_cost']:self.assertEqual(REF['economy'][f],0)
 def test_hp_reset_only_fresh_encounter_contract(self):
  c=REF['combat_contract'];self.assertTrue(c['full_hp_focus_at_fresh_encounter']);self.assertTrue(c['phases_do_not_refill']);self.assertFalse(c['respec_in_encounter']);self.assertFalse(c['passive_toggle_resets_proc_budget'])
 def test_tools_never_required_main_path(self):self.assertTrue(all(not i['required_main_path'] for i in ITEMS.values() if i['kind']=='field_tool'))
 def test_old_reward_aliases_unique(self):
  rows=REF['main_rewards'];self.assertEqual(len(rows),len({r['old_reward_ref'] for r in rows}));self.assertEqual(len(rows),len({r['entitlement_key'] for r in rows}))
 def test_mandatory_combat_actions_available(self):
  c=REF['combat_contract'];self.assertEqual(c['ap_per_hero_turn'],3);self.assertLessEqual(c['basic_attack_ap'],3);self.assertEqual(c['free_supply_charges_per_encounter'],2)

if __name__=='__main__':unittest.main(verbosity=2)

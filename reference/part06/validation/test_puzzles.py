"""Executable checks for bounded design contracts. No engine/IELTS claims."""
from __future__ import annotations
import copy
import json
import unittest
from pathlib import Path
from puzzle_reference import *

REF = json.loads((BASE/'data/Puzzle_Reference_v0.1.json').read_text())
MATS = json.loads((BASE/'data/Puzzle_Materials_v0.1.json').read_text())
UP_MATS = json.loads((BASE/'dependencies/Original_Materials_v0.1.json').read_text())
ENG = json.loads((BASE/'dependencies/English_Learning_Reference_v0.1.json').read_text())
QUESTS = json.loads((BASE/'dependencies/Quest_Event_Reference_v0.1.json').read_text())
MAP = json.loads((BASE/'dependencies/Exploration_Layout_Reference_v0.1.json').read_text())

class MetadataTests(unittest.TestCase):
    def test_fixed_65(self):
        for skill in ('reading','listening','writing'):
            self.assertEqual(REF['target'][skill],6.5)
        self.assertFalse(REF['target']['auto_escalation'])
    def test_nine_unique_units(self):
        self.assertEqual(len(REF['units']),9)
        self.assertEqual(len({u['id'] for u in REF['units']}),9)
    def test_eight_families(self):
        self.assertEqual({f['id'] for f in REF['families']},set('ABCDEFGH'))
    def test_all_task_bindings(self):
        self.assertEqual({b['quest_id'] for b in REF['chapter_bindings']},{q['id'] for q in QUESTS['quests']})
        self.assertEqual(len(REF['chapter_bindings']),32)
    def test_scene_references(self):
        known={s['id'] for r in MAP['regions'] for s in r['scenes']}
        for u in REF['units']:
            self.assertTrue(set(u['scenes']) <= known)
    def test_skill_references(self):
        known={s['id'] for s in ENG['skills']}
        for u in REF['units']:
            self.assertTrue(set(u['skills']) <= known)
            self.assertLessEqual(len(u['skills']),3)
    def test_material_references(self):
        known={m['id'] for m in MATS['materials']+UP_MATS['materials']}
        for u in REF['units']:
            self.assertTrue(set(u['materials']) <= known)
    def test_local_materials_unclaimed_calibration(self):
        for m in MATS['materials']:
            self.assertEqual(m['target_band'],6.5)
            self.assertEqual(m['difficulty_status'],'draft_target_65')
    def test_paragraph_ids_unique(self):
        ids=[p['id'] for m in MATS['materials'] for p in m['paragraphs']]
        self.assertEqual(len(ids),len(set(ids)))
    def test_script_assets_not_fabricated(self):
        aud=[m for m in MATS['materials'] if m['modality']=='listening']
        self.assertEqual(len(aud),3)
        self.assertTrue(all(m['audio_asset'] is None for m in aud))
    def test_no_learning_or_time_gate(self):
        for u in REF['units']:
            self.assertFalse(u['learning_gate']); self.assertFalse(u['real_time_limit'])
    def test_all_hint_rails_present(self):
        for u in REF['units']:
            self.assertEqual(set(u['hints_required']),{'operation','language','logic'})
    def test_budget_sum(self):
        b=REF['budget'];self.assertEqual(sum(b['main_by_chapter'].values()),64)
        self.assertEqual(b['main_total']+b['optional_total'],b['total'])
    def test_f05_not_forced_puzzle(self):
        self.assertEqual(next(b for b in REF['chapter_bindings'] if b['quest_id']=='Q-F05')['family_refs'],[])
    def test_parent_writing_preserved(self):
        parent={b['quest_id']:b['writing_form'] for b in ENG['curriculum']}
        for b in REF['chapter_bindings']:
            self.assertEqual(b['writing_form_preserved'],parent[b['quest_id']])
    def test_map_reference_edges_exist_upstream(self):
        official={frozenset((e['a'],e['b'])) for r in MAP['regions'] for e in r['connections']}
        for e in FIX['map']['edges']:
            self.assertIn(frozenset((e['a'],e['b'])),official)

class RouteAndProposalTests(unittest.TestCase):
    def test_a_closed(self):
        self.assertIs(ferry_route_now('A',1400,True,True),Truth.FALSE)
    def test_b_pump_does_not_fix_draught(self):
        for pump in (False,True,None):
            self.assertIs(ferry_route_now('B',1400,pump,True),Truth.FALSE)
    def test_b_boundary_in_synthetic_other_vessel(self):
        self.assertIs(ferry_route_now('B',1200,True,False),Truth.TRUE)
        self.assertIs(ferry_route_now('B',1201,True,False),Truth.FALSE)
    def test_b_both_conditions_required(self):
        self.assertIs(ferry_route_now('B',1100,False,True),Truth.FALSE)
    def test_c_not_ready_just_because_chosen(self):
        self.assertIs(ferry_route_now('C',1400,False,False),Truth.FALSE)
    def test_c_ready_with_current_certificate(self):
        self.assertIs(ferry_route_now('C',1400,False,True),Truth.TRUE)
    def test_unknown_is_not_a_silent_true(self):
        self.assertIs(ferry_route_now('C',1400,False,None),Truth.UNKNOWN)
    def test_negative_or_float_draught_rejected(self):
        for bad in (-1,1.4,True):
            with self.assertRaises(ValueError): ferry_route_now('B',bad,True,True)
    def test_unmodelled_route_rejected_not_guessed(self):
        with self.assertRaises(ValueError): ferry_route_now('D',1400,True,True)
    def test_three_valued_conjunction(self):
        self.assertIs(all_truth([Truth.TRUE,Truth.UNKNOWN]),Truth.UNKNOWN)
        self.assertIs(all_truth([Truth.FALSE,Truth.UNKNOWN]),Truth.FALSE)
    def test_three_valued_disjunction(self):
        self.assertIs(any_truth([Truth.TRUE,Truth.UNKNOWN]),Truth.TRUE)
        self.assertIs(any_truth([Truth.FALSE,Truth.UNKNOWN]),Truth.UNKNOWN)
    def test_confirmed_meaning_contract(self):
        self.assertEqual(check_confirmed_meaning(MEANING_REQUIRED,True).verdict,'feasible')
    def test_unconfirmed_plan_is_not_approved(self):
        self.assertEqual(check_confirmed_meaning(MEANING_REQUIRED,False).verdict,'needs_clarification')
    def test_missing_reason_requests_clarification(self):
        m=dict(MEANING_REQUIRED);del m['reject_b']
        self.assertEqual(check_confirmed_meaning(m,True).verdict,'needs_clarification')
    def test_wrong_meaning_not_compensated_by_other_keywords(self):
        m=dict(MEANING_REQUIRED);m['route']='B'
        self.assertEqual(check_confirmed_meaning(m,True).verdict,'contradicted')
    def test_unmodelled_world_rewrite_out_of_scope(self):
        m=dict(MEANING_REQUIRED);m['make_boat_lighter']=True
        self.assertEqual(check_confirmed_meaning(m,True).verdict,'out_of_scope')
    def test_integer_does_not_spoof_bool(self):
        m=dict(MEANING_REQUIRED);m['test_before_departure']=1
        self.assertEqual(check_confirmed_meaning(m,True).verdict,'contradicted')
    def test_plan_check_is_read_only(self):
        m=dict(MEANING_REQUIRED);original=copy.deepcopy(m);check_confirmed_meaning(m,True)
        self.assertEqual(m,original)

class DeviceAndWorkshopTests(unittest.TestCase):
    def test_both_install_orders_work(self):
        a=Beacon().change('installed',True).change('connected',True).test()
        b=Beacon().change('connected',True).change('installed',True).test()
        self.assertTrue(a.ready and b.ready)
    def test_test_before_install_cannot_certify(self):
        self.assertIsNone(Beacon().change('connected',True).test().certificate_revision)
    def test_disconnect_invalidates(self):
        b=Beacon().change('installed',True).change('connected',True).test()
        self.assertFalse(b.change('connected',False).ready)
    def test_reconnect_requires_new_test(self):
        b=Beacon().change('installed',True).change('connected',True).test()
        b=b.change('connected',False).change('connected',True)
        self.assertFalse(b.ready);self.assertTrue(b.test().ready)
    def test_unknown_charge_not_ready(self):
        b=Beacon(installed=True,connected=True,charged=None).test();self.assertFalse(b.ready)
    def test_noop_not_a_configuration_change(self):
        b=Beacon().change('installed',True).change('connected',True).test()
        self.assertEqual(b.change('connected',True),b)
    def test_finite_beacon_recoverable(self):
        graph=explore((False,False,False),finite_beacon_steps)
        self.assertEqual(can_finish_from_all(graph,lambda s:s==(True,True,True))[1],0)
    def test_active_side_blocked(self):
        self.assertNotIn('side_in',workshop_steps(Workshop(place='control')))
    def test_focus_persists_during_reading(self):
        s=Workshop(place='control',mode='focused')
        self.assertEqual(workshop_steps(s)['wait_or_read'],s)
    def test_focus_front_remains_risky_both_directions(self):
        self.assertNotIn('front_in',workshop_steps(Workshop(mode='focused')))
        self.assertNotIn('front_out',workshop_steps(Workshop(place='inside',mode='focused')))
    def test_isolation_opens_side(self):
        self.assertIn('side_in',workshop_steps(Workshop(place='control',mode='isolated')))
    def test_disabled_cannot_repair_by_switch(self):
        steps=workshop_steps(Workshop(place='control',mode='disabled'))
        self.assertNotIn('select_normal',steps);self.assertNotIn('select_focus',steps)
    def test_latch_independent_of_normal_mode(self):
        self.assertIn('shortcut_out',workshop_steps(Workshop(place='inside',latch_open=True)))
    def test_part_cannot_be_taken_twice(self):
        self.assertNotIn('take_part',workshop_steps(Workshop(place='inside',part_taken=True)))
    def test_each_method_has_recovery_from_all_reachable_states(self):
        report=model_report()
        for label in ('all','combat_only','isolate_only','focus_only'):
            self.assertEqual(report[label]['states_without_goal_path'],0)
    def test_full_hint_valid_in_every_reachable_state(self):
        graph=explore(Workshop(),workshop_steps)
        for start in graph:
            s=start
            for act in full_workshop_hint(s):
                allowed=workshop_steps(s,frozenset(('isolate','focus')))
                self.assertIn(act,allowed);s=allowed[act]
            self.assertTrue(s.part_taken and s.place=='yard')
    def test_full_hint_does_not_retake_part(self):
        s=Workshop(place='inside',mode='isolated',part_taken=True)
        self.assertNotIn('take_part',full_workshop_hint(s))
    def test_relevant_preview_invalidates_only_related_change(self):
        old={'config':2,'content':'v1','news':0};new=dict(old,news=1)
        self.assertEqual(relevant_preview_current(old,new,('config','content')).verdict,'feasible')
        new['config']=3
        self.assertEqual(relevant_preview_current(old,new,('config','content')).verdict,'stale')
    def test_missing_version_not_assumed_current(self):
        self.assertEqual(relevant_preview_current({}, {}, ('config',)).verdict,'data_unavailable')

class HotelAndTimelineTests(unittest.TestCase):
    def test_two_bookings_not_four_pages(self):
        self.assertEqual(resolve_booking_arrivals(FIX['hotel']['records']),{'BKG-17':18,'BKG-22':18})
    def test_reading_order_independent(self):
        expected={'BKG-17':18,'BKG-22':18}
        for records in permutations(FIX['hotel']['records']):
            self.assertEqual(resolve_booking_arrivals(list(records)),expected)
    def test_copy_does_not_create_a_booking(self):
        records=copy.deepcopy(FIX['hotel']['records']);r=dict(records[2],id='H2-COPY');records.append(r)
        self.assertEqual(len(resolve_booking_arrivals(records)),2)
    def test_unlinked_newer_record_is_conflict(self):
        records=copy.deepcopy(FIX['hotel']['records']);records.append(dict(id='H4',booking='BKG-17',version=9,arrival=20,provenance_root='H4'))
        with self.assertRaises(ValueError):resolve_booking_arrivals(records)
    def test_copy_content_conflict_not_silently_overwritten(self):
        records=copy.deepcopy(FIX['hotel']['records']);records[1]['arrival']=20
        with self.assertRaises(ValueError):resolve_booking_arrivals(records)
    def test_unique_room_allocation(self):
        self.assertEqual(hotel_assignments(),[{'BKG-17':'ROOM-2','BKG-22':'ROOM-5'}])
    def test_unique_relative_timeline(self):
        self.assertEqual(timeline_orders(),[('authorised','departed','arrived','copied')])
    def test_unknown_exact_authorisation_time_preserved(self):
        self.assertNotIn('authorised',FIX['timeline']['known_minutes'])
    def test_missing_timeline_relation_detects_ambiguity(self):
        self.assertGreater(len(timeline_orders([['authorised','departed'],['arrived','copied']])),1)
    def test_cycle_has_no_valid_order(self):
        self.assertEqual(timeline_orders(FIX['timeline']['before']+[['copied','authorised']]),[])
    def test_duplicate_evidence_not_independent(self):
        self.assertEqual(evidence_status(['origin','origin'],[]),'supported')
        self.assertEqual(len(set(['origin','origin'])),1)
    def test_insufficient_not_false(self):
        self.assertEqual(evidence_status([],[]),'not_enough_information')
    def test_conflicting_admitted_sources_request_resolution(self):
        self.assertEqual(evidence_status(['sourceA'],['sourceB']),'content_conflict')

class MapCargoTrialTests(unittest.TestCase):
    def test_two_map_routes_when_both_prepared(self):
        self.assertEqual(len(map_paths(dict(credentials_verified=True,basic_tools=True,tunnel_cleared=True))),2)
    def test_no_credentials_still_has_basic_tool_route(self):
        self.assertEqual(len(map_paths(dict(credentials_verified=False,basic_tools=True,tunnel_cleared=True))),1)
    def test_no_tools_still_has_credentials_route(self):
        self.assertEqual(len(map_paths(dict(credentials_verified=True,basic_tools=False,tunnel_cleared=False))),1)
    def test_current_path_cannot_use_historic_bridge(self):
        c=dict(credentials_verified=True,basic_tools=True,tunnel_cleared=True)
        self.assertEqual(map_route_check(('M-F-R','M-R-P','M-OLD-BRIDGE'),c).verdict,'contradicted')
    def test_planned_tunnel_not_automatically_cleared(self):
        c=dict(credentials_verified=False,basic_tools=True,tunnel_cleared=False)
        self.assertEqual(map_route_check(('M-F-R','M-R-P','M-P-T','M-T-N'),c).verdict,'incomplete')
    def test_unknown_map_authority_not_false_fact(self):
        self.assertEqual(map_route_check(('M-F-R','M-R-P','M-P-N'),{}).verdict,'data_unavailable')
    def test_map_goal_does_not_include_detention_access(self):
        self.assertEqual(FIX['map']['goal'],'R5-S05')
        self.assertNotIn('R5-S06',{e[k] for e in FIX['map']['edges'] for k in ('a','b')})
    def test_exact_two_cargo_solutions(self):
        self.assertEqual(len(cargo_solutions()),2)
    def test_cargo_both_preferences_equally_feasible(self):
        sol=cargo_solutions()
        self.assertEqual({s['panels'] for s in sol},{1,2})
        self.assertTrue(all(cargo_check(s).verdict=='feasible' for s in sol))
    def test_same_trip_not_earlier(self):
        schedule={'tools':1,'mounts':1,'light':1,'panels':2,'archive':2}
        self.assertIn('tools_not_earlier_than_light',cargo_check(schedule).reasons)
    def test_over_capacity_rejected(self):
        schedule={k:1 for k in FIX['cargo']['packages']}
        self.assertIn('capacity_trip_1',cargo_check(schedule).reasons)
    def test_missing_package_not_valid(self):
        schedule=dict(cargo_solutions()[0]);del schedule['archive']
        self.assertEqual(cargo_check(schedule).verdict,'incomplete')
    def test_cannot_invent_third_trip_or_new_package(self):
        schedule=dict(cargo_solutions()[0]);schedule['archive']=3
        self.assertEqual(cargo_check(schedule).verdict,'out_of_scope')
    def test_basic_safety_and_endings_not_used_as_cargo_penalty(self):
        self.assertFalse(FIX['cargo']['basic_safety_resources_shared'])
        self.assertFalse(FIX['cargo']['ending_eligibility_effect'])
    def test_comparable_alternative_pair_accepted(self):
        pairs={frozenset(p) for p in comparable_load_pairs()}
        self.assertIn(frozenset(('T1','T2')),pairs)
        self.assertIn(frozenset(('T2','T4')),pairs)
        self.assertNotIn(frozenset(('T2','T3')),pairs)
    def test_all_declared_trial_claims(self):
        for claim,expect in FIX['trials']['claims'].items():
            self.assertEqual(trial_claim(claim),expect)
    def test_unobserved_load_not_interpolated(self):
        self.assertEqual(trial_claim('load_55_safe'),'not_enough_information')
    def test_short_trial_not_six_tower_approval(self):
        self.assertEqual(trial_claim('all_six_towers_ready'),'not_enough_information')
    def test_undeclared_claim_not_hallucinated(self):
        with self.assertRaises(ValueError):trial_claim('Mara_is_guilty')


class SourceAndSufficiencyTests(unittest.TestCase):
    def test_constraint_ids_and_units(self):
        ids=[c['id'] for c in REF['constraint_registry']]
        self.assertEqual(len(ids),27);self.assertEqual(len(ids),len(set(ids)))
        self.assertEqual({c['puzzle_id'] for c in REF['constraint_registry']},{u['id'] for u in REF['units']})
    def test_all_constraint_spans_exist(self):
        known={p['id'] for m in MATS['materials']+UP_MATS['materials'] for p in m['paragraphs']}
        for c in REF['constraint_registry']:
            self.assertTrue(c['evidence_spans']);self.assertTrue(set(c['evidence_spans']) <= known)
    def test_no_renamed_puzzle_family_as_new_learning_proof(self):
        self.assertTrue(all('material_family' not in u for u in REF['units']))
        self.assertTrue(all('family_id' in m for m in MATS['materials']))
    def test_given_facts_determine_unique_candidate_route(self):
        given=dict(a_open=False,b_limit_mm=1200,draught_mm=1400,c_open=True,beacon_repair_possible=True)
        self.assertEqual(compatible_route_conclusions(given),{frozenset({'C'})})
    def test_missing_draught_creates_ambiguity_in_visible_facts(self):
        given=dict(a_open=False,b_limit_mm=1200,c_open=True,beacon_repair_possible=True)
        self.assertEqual(compatible_route_conclusions(given),{frozenset({'C'}),frozenset({'B','C'})})
    def test_missing_closed_route_fact_does_not_justify_unique_C(self):
        given=dict(b_limit_mm=1200,draught_mm=1400,c_open=True,beacon_repair_possible=True)
        self.assertEqual(compatible_route_conclusions(given),{frozenset({'C'}),frozenset({'A','C'})})
    def test_unknown_repair_feasibility_not_invented(self):
        given=dict(a_open=False,b_limit_mm=1200,draught_mm=1400,c_open=True)
        self.assertEqual(compatible_route_conclusions(given),{frozenset({'C'}),frozenset()})

if __name__ == '__main__':
    unittest.main(verbosity=2)

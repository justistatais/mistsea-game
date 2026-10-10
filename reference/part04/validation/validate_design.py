"""Run with Python 3: python validation/validate_design.py (no extra packages)."""
from __future__ import annotations
import json
from pathlib import Path
import re
import unittest
from dataclasses import replace
from reference_model import ContractError, FerryEngine, Meaning, ResourceLeases, explore

ROOT=Path(__file__).resolve().parents[1]
REF=json.loads((ROOT/'data/Quest_Event_Reference_v0.1.json').read_text(encoding='utf-8'))
LAYOUT=json.loads((ROOT/'dependencies/Exploration_Layout_Reference_v0.1.json').read_text(encoding='utf-8'))

def ready_engine(method='isolated'):
    e=FerryEngine();e.accept();e.set_mode(method);e.take_bracket();e.install();e.connect(True);e.test();e.edit_proposal(Meaning());e.confirm_proposal();return e

class RegistryTests(unittest.TestCase):
    def test_32_unique_quests(self):
        ids=[q['id'] for q in REF['quests']];self.assertEqual(len(ids),32);self.assertEqual(len(set(ids)),32)
    def test_story_ids_match(self):
        text=(ROOT/'dependencies/World_Story_Bible_v0.1.md').read_text(encoding='utf-8')
        ids=set(re.findall(r'\|\s*(Q-(?:P\d{2}|\d{3}|F\d{2}))《',text));self.assertEqual(ids,{q['id'] for q in REF['quests']})
    def test_all_scene_references_exist(self):
        scenes={s['id'] for r in LAYOUT['regions'] for s in r['scenes']}
        for q in REF['quests']: self.assertTrue(set(q['scene_refs'])<=scenes)
        for e in REF['event_templates']: self.assertIn(e['scene'],scenes)
    def test_dependency_graph_acyclic(self):
        deps={q['id']:set(q['activation_after']+q['additional_close_after']) for q in REF['quests']}
        self.assertTrue(all(v<=deps.keys() for v in deps.values()))
        done=set()
        while len(done)<len(deps):
            eligible={k for k,v in deps.items() if k not in done and v<=done}
            self.assertTrue(eligible,'dependency cycle');done|=eligible
    def test_frontier_no_circular_aggregate(self):
        q={x['id']:x for x in REF['quests']}
        self.assertIn('Q-603',q['Q-602']['additional_close_after'])
        self.assertNotIn('Q-602',q['Q-603']['activation_after']+q['Q-603']['additional_close_after'])
    def test_terminal_receipt_not_own_guard(self):
        s=json.dumps(REF['q102']['ready_predicate']);self.assertNotIn('O-102-DEPART',s)
    def test_learning_fields_not_in_ready_guard(self):
        s=json.dumps(REF['q102']['ready_predicate'])
        for word in REF['q102']['banned_gate_domains']: self.assertNotIn(word,s)
    def test_material_sources_match_existing_spots(self):
        spots={s['id'] for g in LAYOUT['slice']['geometry'].values() for s in g['spots']}
        self.assertTrue(set(REF['q102']['material_bindings'].values())<=spots)
    def test_trial_does_not_require_final_joint_agreement(self):
        self.assertNotIn('joint_agreement_accepted',REF['work_orders']['trial_dependencies'])
        self.assertIn('local_test_permission',REF['work_orders']['trial_dependencies'])
    def test_unique_events_and_material_ids(self):
        ids=[e['id'] for e in REF['event_templates']];self.assertEqual(len(ids),len(set(ids)))

class FerryTests(unittest.TestCase):
    def test_all_methods_can_complete(self):
        for method in ('disabled','isolated','focused'):
            e=ready_engine(method);e.submit('one',e.preview());self.assertTrue(e.closed)
    def test_take_before_accept_is_retained(self):
        e=FerryEngine();e.set_mode('isolated');e.take_bracket();e.install();e.accept();self.assertEqual(e.bracket,'installed')
    def test_duplicate_take_no_item_copy(self):
        e=FerryEngine();e.set_mode('focused');e.take_bracket();e.take_bracket();e.install();e.take_bracket();self.assertEqual(e.bracket,'installed')
    def test_both_install_orders(self):
        for early_battery in (False,True):
            e=FerryEngine();e.accept();e.set_mode('isolated');e.take_bracket()
            if early_battery:e.connect(True)
            e.install();e.connect(True);e.test();e.edit_proposal(Meaning());e.confirm_proposal();self.assertTrue(e.ready)
    def test_no_test_without_configuration(self):
        with self.assertRaises(ContractError):FerryEngine().test()
    def test_disconnect_invalidates_certificate(self):
        e=ready_engine();e.connect(False);self.assertFalse(e.ready)
    def test_restore_same_configuration_still_requires_new_test(self):
        e=ready_engine();e.connect(False);e.connect(True);self.assertFalse(e.ready);e.test();self.assertTrue(e.ready)
    def test_early_plan_does_not_block_repair(self):
        e=FerryEngine();e.accept();e.edit_proposal(Meaning());e.confirm_proposal();e.set_mode('isolated');e.take_bracket();e.install();e.connect(True);e.test();self.assertTrue(e.ready)
    def test_changing_text_revision_invalidates_intent_confirmation(self):
        e=ready_engine();e.edit_proposal(Meaning());self.assertFalse(e.ready);e.confirm_proposal();self.assertTrue(e.ready)
    def test_route_a_cannot_be_confirmed(self):
        e=ready_engine();e.edit_proposal(replace(Meaning(),route='A'))
        with self.assertRaises(ContractError):e.confirm_proposal()
    def test_route_b_not_fixed_by_pump_wording(self):
        e=ready_engine();e.edit_proposal(replace(Meaning(),route='B',reject_b='pump_running'))
        with self.assertRaises(ContractError):e.confirm_proposal()
    def test_generator_start_not_admissible(self):
        e=ready_engine();e.edit_proposal(replace(Meaning(),generator='start_now'));self.assertFalse(e.ready)
    def test_arbitrary_json_cannot_be_world_command(self):
        with self.assertRaises(ContractError):FerryEngine().edit_proposal({'set_all_towers_ready':True})
    def test_ready_does_not_auto_depart(self):
        e=ready_engine();self.assertTrue(e.docked);self.assertEqual(e.entitlement_count,0)
    def test_preview_does_not_mutate_world(self):
        e=ready_engine();before=e.snapshot();e.preview();self.assertEqual(e.snapshot(),before)
    def test_commit_moves_player_and_items_stay_on_ship(self):
        e=ready_engine();e.submit('a',e.preview());self.assertEqual(e.player_scene,'R1-S02');self.assertEqual(e.bracket,'installed');self.assertFalse(e.docked)
    def test_same_command_idempotent(self):
        e=ready_engine();t=e.preview();r=e.submit('a',t);self.assertEqual(e.submit('a',t),r);self.assertEqual(e.entitlement_count,1)
    def test_second_command_cannot_grant_again(self):
        e=ready_engine();t=e.preview();e.submit('a',t)
        with self.assertRaises(ContractError):e.submit('b',t)
        self.assertEqual(e.entitlement_count,1)
    def test_same_command_id_different_payload_rejected(self):
        e=ready_engine();t=e.preview();e.submit('a',t)
        with self.assertRaises(ContractError):e.submit('a',replace(t,context='different'))
    def test_simulated_failure_before_publish_changes_nothing(self):
        e=ready_engine();before=e.snapshot()
        with self.assertRaises(ContractError):e.submit('a',e.preview(),fail_before_publish=True)
        self.assertEqual(before,e.snapshot())
    def test_earlier_snapshot_can_settle_in_its_own_world(self):
        e=ready_engine();snap=e.snapshot();e.submit('a',e.preview());restored=FerryEngine.load(snap);restored.submit('b',restored.preview());self.assertEqual(restored.entitlement_count,1)
    def test_later_snapshot_keeps_reward_receipt(self):
        e=ready_engine();t=e.preview();e.submit('a',t);restored=FerryEngine.load(e.snapshot());restored.submit('a',t);self.assertEqual(restored.entitlement_count,1)
    def test_reload_invalidates_uncommitted_ui_token(self):
        e=ready_engine();t=e.preview();restored=FerryEngine.load(e.snapshot())
        with self.assertRaises(ContractError):restored.submit('a',t)
    def test_unrelated_learning_feedback_does_not_invalidate_preview(self):
        e=ready_engine();t=e.preview();e.language_callback('1','use a clearer article');e.submit('a',t);self.assertTrue(e.closed)
    def test_feedback_cannot_send_ship(self):
        e=ready_engine();e.language_callback('1','excellent');self.assertTrue(e.docked);self.assertEqual(e.entitlement_count,0)
    def test_stale_equipment_preview_rejected_even_after_retest(self):
        e=ready_engine();t=e.preview();e.connect(False);e.connect(True);e.test()
        with self.assertRaises(ContractError):e.submit('a',t)
    def test_stale_proposal_preview_rejected(self):
        e=ready_engine();t=e.preview();e.edit_proposal(Meaning());e.confirm_proposal()
        with self.assertRaises(ContractError):e.submit('a',t)
    def test_changed_rules_reject_old_token(self):
        e=ready_engine();t=e.preview();e.rules_revision='another_bundle'
        with self.assertRaises(ContractError):e.submit('a',t)
    def test_revoked_permission_rejects_commit(self):
        e=ready_engine();t=e.preview();e.permit=False
        with self.assertRaises(ContractError):e.submit('a',t)
    def test_move_away_from_submit_spot_rejects_commit(self):
        e=ready_engine();t=e.preview();e.authorized_submit_spot=False
        with self.assertRaises(ContractError):e.submit('a',t)
    def test_no_respawn_disabled_encounter(self):
        e=ready_engine('disabled')
        with self.assertRaises(ContractError):e.set_mode('active')
    def test_online_help_combinations_do_not_gate(self):
        for online in (False,True):
            for hints in (False,True):
                for subtitles in (False,True):
                    e=ready_engine();e.learning_callbacks.append(dict(online=online,hints=hints,subtitles=subtitles));self.assertTrue(e.ready)

class LeaseTests(unittest.TestCase):
    def test_conflicting_actor_cannot_be_in_two_scenes(self):
        q=ResourceLeases();self.assertTrue(q.acquire('a',['scene:R1-S03','npc:havel']));self.assertFalse(q.acquire('b',['scene:R1-S02','npc:havel']))
    def test_conflicting_request_leaves_no_partial_lock(self):
        q=ResourceLeases();q.acquire('a',['npc:havel']);before=dict(q.holders);self.assertFalse(q.acquire('b',['scene:R1-S02','npc:havel']));self.assertEqual(before,q.holders)
    def test_writing_blocks_new_foreground_event(self):
        q=ResourceLeases();self.assertFalse(q.acquire('main',['scene:R1-S03'],protected_input=True));self.assertFalse(q.holders)
    def test_release_allows_deferred_event(self):
        q=ResourceLeases();q.acquire('side',['scene:R1-S03','npc:havel']);q.release('side');self.assertTrue(q.acquire('main',['scene:R1-S03','npc:havel']))
    def test_same_scene_one_foreground(self):
        q=ResourceLeases();q.acquire('a',['scene:R1-S03']);self.assertFalse(q.acquire('b',['scene:R1-S03']))
    def test_same_event_reacquire_is_idempotent(self):
        q=ResourceLeases();self.assertTrue(q.acquire('a',['scene:R1-S03']));self.assertTrue(q.acquire('a',['scene:R1-S03']));self.assertEqual(len(q.holders),1)

class ExhaustiveTests(unittest.TestCase):
    def test_abstract_model_has_completion_path(self):
        self.assertEqual(explore()['states_without_completion_path'],0)

if __name__=='__main__':
    suite=unittest.defaultTestLoader.loadTestsFromModule(__import__(__name__))
    result=unittest.TextTestRunner(verbosity=2).run(suite)
    model=explore()
    summary=dict(unit_tests_run=result.testsRun,failures=len(result.failures),errors=len(result.errors),
                 all_passed=result.wasSuccessful(),abstract_model=model,
                 registry={'main_containers':len(REF['quests']),'event_templates':len(REF['event_templates']),
                           'scene_registry':sum(len(r['scenes']) for r in LAYOUT['regions'])},
                 limitations=['无引擎、UI、实际磁盘原子性、网络、完整战斗、语言理解或学习效果验证',
                              '穷举仅覆盖Q-102任务层的有限抽象；不覆盖实际空间、全部世界资源及其他31个任务内部逻辑',
                              '事件测试仅验证资源排他与保护输入，不等于完整事件选择、抢占、NPC调度均已实现'])
    (ROOT/'validation/validation_summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(summary,ensure_ascii=False,indent=2))
    raise SystemExit(0 if result.wasSuccessful() else 1)

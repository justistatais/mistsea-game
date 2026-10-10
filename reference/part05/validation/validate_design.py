"""Run: python validation/validate_design.py. No external dependencies."""
from pathlib import Path
from dataclasses import replace
import json,sys,unittest,io
from learning_reference import *
ROOT=Path(__file__).resolve().parents[1]
REF=json.loads((ROOT/'data/English_Learning_Reference_v0.1.json').read_text(encoding='utf-8'))
MAT=json.loads((ROOT/'data/Original_Materials_v0.1.json').read_text(encoding='utf-8'))
QREF=json.loads((ROOT/'dependencies/Quest_Event_Reference_v0.1.json').read_text(encoding='utf-8'))

def o(**kwargs):
    args=dict(id='o1',goal='R03',mode='reading',family='f1',context='harbour',session='s1',day=0,outcome='correct')
    args.update(kwargs);return Observation(**args)

def diverse():
    return [o(),o(id='o2',family='f2',context='school',session='s2',day=2),
            o(id='o3',family='f3',context='housing',session='s3',day=9)]

class MetadataTests(unittest.TestCase):
    def test_sixty_unique_skills(self):
        self.assertEqual(len(REF['skills']),60);self.assertEqual(len({s['id'] for s in REF['skills']}),60)
    def test_thirty_two_task_bindings(self):
        self.assertEqual({c['quest_id'] for c in REF['curriculum']},{q['id'] for q in QREF['quests']})
        self.assertEqual(len(REF['curriculum']),32)
    def test_six_five_only(self):
        self.assertTrue(all(c['target_band']==6.5 for c in REF['curriculum']))
        self.assertTrue(all(s['target_band']==6.5 for s in REF['skills']))
        self.assertFalse(REF['target']['automatic_mainline_band_escalation'])
    def test_all_goal_refs_exist_and_planned(self):
        ids={s['id'] for s in REF['skills']}; refs={x for c in REF['curriculum'] for x in c['primary_goals']+c['supporting_goals']}
        self.assertEqual(ids,refs)
    def test_three_primary_goals(self):
        self.assertTrue(all(len(c['primary_goals'])<=3 for c in REF['curriculum']))
    def test_long_writing_budget(self):
        self.assertEqual(sum(c['writing_form']=='visual' for c in REF['curriculum']),6)
        self.assertEqual(sum(c['writing_form']=='argument' for c in REF['curriculum']),6)
    def test_no_overall_or_speaking_score(self):
        self.assertIsNone(REF['target']['speaking']);self.assertFalse(REF['target']['overall_score_claim'])
        self.assertFalse(REF['assessment']['full_band_output_enabled'])
    def test_no_world_permissions(self):
        self.assertEqual(REF['assessment']['world_write_permissions'],[])
    def test_original_materials_not_calibrated(self):
        self.assertTrue(all(m['target_band']==6.5 and m['difficulty_status']=='authored_target_not_empirically_calibrated' for m in MAT['materials']))
    def test_long_reading_present(self):
        m=next(m for m in MAT['materials'] if m['id']=='LAB-R-001');self.assertTrue(600<=m['approx_word_count']<=950)
    def test_long_audio_is_script_not_recording(self):
        m=next(m for m in MAT['materials'] if m['id']=='LAB-L-001')
        self.assertGreaterEqual(m['approx_word_count'],580);self.assertIsNone(m['audio_asset'])
        self.assertEqual(REF['content_budget']['provided_rendered_audio_assets'],0)
    def test_question_evidence_refs_exist(self):
        for m in MAT['materials']:
            ids={p['id'] for p in m['paragraphs']}
            for item in m.get('items',[]):self.assertTrue(set(item['evidence_paragraphs'])<=ids)
    def test_material_goal_refs(self):
        ids={s['id'] for s in REF['skills']}
        for m in MAT['materials']:self.assertTrue(set(m['goal_refs'])<=ids)
    def test_short_writing_not_full_band(self):
        self.assertFalse(MAT['writing']['q102']['full_band_estimate_allowed'])
    def test_task_one_data_totals(self):
        rows=MAT['writing']['visual']['rows']
        self.assertEqual([sum(r[i] for r in rows) for i in range(1,5)],[100,110,120,140])

class EvidenceTests(unittest.TestCase):
    def test_reading_source_no_hint(self):self.assertEqual(condition_class(o()),'independent_first_conditions')
    def test_visit_is_not_success(self):self.assertEqual(condition_class(o(outcome=None)),'unassessed')
    def test_unassessed_not_wrong(self):self.assertEqual(profile([o(outcome=None)],'R03','reading')['status'],'insufficient_evidence')
    def test_empty_response_not_evidence(self):self.assertEqual(condition_class(o(response_nonempty=False)),'insufficient_evidence')
    def test_insufficient_scope(self):self.assertEqual(condition_class(o(scope_sufficient=False)),'insufficient_evidence')
    def test_unknown_conditions(self):self.assertEqual(condition_class(o(conditions_complete=False)),'conditions_unknown')
    def test_unknown_hint_scope_conservative(self):self.assertEqual(condition_class(o(supports=(Support('gloss',50),))),'support_scope_unknown')
    def test_unrelated_hint_not_global_penalty(self):self.assertEqual(condition_class(o(supports=(Support('gloss',50,frozenset({'V02'})),))),'independent_first_conditions')
    def test_ui_help_not_content(self):self.assertEqual(condition_class(o(supports=(Support('ui',50),))),'independent_first_conditions')
    def test_target_hint_supported(self):self.assertEqual(condition_class(o(supports=(Support('gloss',50,frozenset({'R03'})),))),'supported')
    def test_help_after_submission_not_retroactive(self):self.assertEqual(condition_class(o(supports=(Support('solution',101,frozenset({'R03'})),))),'independent_first_conditions')
    def test_help_before_submission_affects_target(self):self.assertEqual(condition_class(o(supports=(Support('solution',99,frozenset({'R03'})),))),'supported')
    def test_audio_first(self):self.assertEqual(condition_class(o(mode='listening',goal='L03')),'independent_first_conditions')
    def test_audio_replay(self):self.assertEqual(condition_class(o(mode='listening',goal='L03',audio_replays=1)),'independent_audio_review')
    def test_audio_pause(self):self.assertEqual(condition_class(o(mode='listening',goal='L03',audio_paused=True)),'independent_audio_review')
    def test_audio_speed(self):self.assertEqual(condition_class(o(mode='listening',goal='L03',speed_changed=True)),'independent_audio_review')
    def test_subtitle_not_pure_audio(self):self.assertEqual(condition_class(o(mode='listening',goal='L03',supports=(Support('transcript',40,frozenset({'L03'})),))),'text_mediated_listening')
    def test_missing_audio(self):self.assertEqual(condition_class(o(mode='listening',goal='L03',audio_delivered=False)),'text_path_no_audio_evidence')
    def test_prior_visual_fact(self):self.assertEqual(condition_class(o(mode='listening',goal='L03',answer_seen_before=True)),'prior_answer_exposure')
    def test_repeat_family_not_fresh(self):self.assertEqual(condition_class(o(family_seen_before=True)),'independent_repeat_material')
    def test_duplicate_observation_idempotent(self):
        led=ObservationLedger();led.upsert(o());led.upsert(o());self.assertEqual(len(led.records()),1)
    def test_reevaluation_not_new_attempt(self):
        led=ObservationLedger();led.upsert(o());led.upsert(o(outcome='incorrect',evaluator_version='v2'))
        self.assertEqual(len(led.records()),1);self.assertEqual(led.records()[0].outcome,'incorrect')
    def test_id_not_reused_for_different_text(self):
        led=ObservationLedger();led.upsert(o())
        with self.assertRaises(ValueError):led.upsert(o(response_version='v2'))
    def test_three_same_family_not_three(self):
        rows=[o(id=str(i),day=i,session=str(i)) for i in range(3)]
        self.assertEqual(profile(rows,'R03','reading')['distinct_success_families'],1)
    def test_three_families_repeated(self):
        rows=[replace(x,day=i) for i,x in enumerate(diverse())]
        self.assertEqual(profile(rows,'R03','reading')['status'],'repeated_independent')
    def test_one_session_not_multisession(self):
        self.assertEqual(profile([replace(x,session='one') for x in diverse()],'R03','reading')['status'],'independent_once')
    def test_delayed_transfer(self):self.assertTrue(profile(diverse(),'R03','reading')['has_delayed_transfer'])
    def test_untrusted_clock_not_retention(self):self.assertFalse(profile([replace(x,clock_trusted=False) for x in diverse()],'R03','reading')['has_delayed_transfer'])
    def test_modality_separate(self):self.assertFalse(profile(diverse(),'R03','listening')['has_independent_success'])
    def test_two_difficulties_do_not_erase_history(self):
        rows=diverse()+[o(id='o4',family='f4',day=10,outcome='incorrect'),o(id='o5',family='f5',day=11,outcome='incorrect')]
        p=profile(rows,'R03','reading');self.assertTrue(p['needs_recheck']);self.assertTrue(p['has_independent_success'])
    def test_no_numeric_band_generated(self):self.assertIsNone(profile(diverse(),'R03','reading')['band_score'])
    def test_delete_discards_late_result(self):
        led=ObservationLedger();led.upsert(o());led.delete()
        with self.assertRaises(ValueError):led.upsert(o())

class SchedulingTests(unittest.TestCase):
    def test_new_success_expands_advisory_interval(self):self.assertEqual(next_review(0,'clean_new',10),(1,13))
    def test_supported_not_promoted(self):self.assertEqual(next_review(2,'supported',10,30),(2,11))
    def test_incorrect_not_history_reset(self):self.assertEqual(next_review(3,'incorrect',10),(2,11))
    def test_repeat_not_due_postpone(self):self.assertEqual(next_review(2,'repeat',10,15),(2,15))
    def test_unassessed_no_schedule_failure(self):self.assertEqual(next_review(2,'unassessed',10,15),(2,15))
    def test_protected_input(self):self.assertEqual(select_reviews([ReviewCandidate('a','R03')],10,True),[])
    def test_incompatible_content_filtered(self):
        cs=[ReviewCandidate('a','R01',story_compatible=False),ReviewCandidate('b','R02',access_allowed=False),ReviewCandidate('c','R03',asset_available=False),ReviewCandidate('d','R04',target_band=7)]
        self.assertEqual(select_reviews(cs,10,False),[])
    def test_recommendation_cap_and_goal_dedup(self):
        cs=[ReviewCandidate(str(i),f'R{i%4}') for i in range(8)]
        self.assertEqual(len(select_reviews(cs,10,False)),3)
    def test_explicit_decline_respected(self):self.assertEqual(select_reviews([ReviewCandidate('a','R03')],10,False,frozenset({'a'})),[])
    def test_requested_priority(self):
        cs=[ReviewCandidate('a','R01'),ReviewCandidate('z','R02',requested=True,due_day=100)]
        self.assertEqual(select_reviews(cs,10,False)[0],'z')
    def test_zero_capacity(self):self.assertEqual(select_reviews([ReviewCandidate('a','R03')],10,False,maximum=0),[])

class FeedbackTests(unittest.TestCase):
    def payload(self):return {'observation_id':'o1','text_version':'v1','status':'assessed','feedback':[]}
    def test_valid_feedback(self):self.assertTrue(validate_feedback(self.payload(),'o1','v1'))
    def test_world_mutation_not_allowed(self):
        p=self.payload();p['world_effects']={'depart_ferry':True};self.assertFalse(validate_feedback(p,'o1','v1'))
    def test_new_draft_rejects_old_result(self):self.assertFalse(validate_feedback(self.payload(),'o1','v2'))
    def test_deleted_profile(self):self.assertFalse(validate_feedback(self.payload(),'o1','v1',True))
    def test_no_band_field_in_contract(self):
        p=self.payload();p['band_score']=6.5;self.assertFalse(validate_feedback(p,'o1','v1'))

if __name__=='__main__':
    suite=unittest.defaultTestLoader.loadTestsFromModule(sys.modules[__name__])
    log=io.StringIO();res=unittest.TextTestRunner(stream=log,verbosity=2).run(suite)
    (ROOT/'validation/run_log.txt').write_text(log.getvalue(),encoding='utf-8')
    report={'tests_run':res.testsRun,'failures':len(res.failures),'errors':len(res.errors),'success':res.wasSuccessful(),
            'skills':len(REF['skills']),'quest_bindings':len(REF['curriculum']),'original_materials':len(MAT['materials']),
            'audio_scripts':sum(m['modality']=='listening' for m in MAT['materials']),
            'rendered_audio_assets':0,'human_pilot_participants':0,
            'difficulty_empirically_calibrated':False,'actual_english_grading_implemented':False,
            'limitations':['Reference condition and metadata checks only','No real audio/player data or full writing scorer','No game engine or real disk recovery test','Thresholds are transparent project heuristics, not IELTS conversions']}
    (ROOT/'validation/validation_report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(log.getvalue());print(json.dumps(report,ensure_ascii=False,indent=2))
    sys.exit(0 if res.wasSuccessful() else 1)

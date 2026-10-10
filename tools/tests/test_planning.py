"""Planning-tool tests only. No Godot, model, voice or human evidence is created."""
from pathlib import Path
import copy,hashlib,json,sys,tempfile,unittest
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools'))
from plan_lib import load_json,validate_plan,safe_relative,all_dependencies,derive_completed,schedule,evidence_errors,execution_candidates
from verify_handoff import verify

class PlanTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.plan=load_json(ROOT/'planning/plan.json')
        cls.trace=load_json(ROOT/'planning/acceptance_traceability.json')
        cls.state=load_json(ROOT/'planning/state/execution.json')
    def fresh(self):return copy.deepcopy(self.plan)
    def test_01_real_source_integrity(self):
        self.assertEqual(verify(ROOT)['failed'],0)
    def test_02_current_plan_valid(self):
        self.assertEqual(validate_plan(self.plan,self.trace),[])
    def test_03_exact_counts(self):
        self.assertEqual(len(self.plan['tasks']),136);self.assertEqual(len(self.plan['gates']),8)
        self.assertEqual(self.trace['count'],478)
    def test_04_only_first_task_ready(self):
        r=schedule(ROOT,self.plan,self.state)
        self.assertEqual(r['ready'],['CX-001']);self.assertEqual(r['done'],[])
    def test_05_default_disabled_not_passed(self):
        r=schedule(ROOT,self.plan,self.state)
        self.assertEqual(set(r['feature_disabled']),{'CX-069','HV-008'})
        self.assertNotIn('CX-069',r['done'])
    def test_06_language_raise_rejected(self):
        p=self.fresh();p['language_target']['reading']=7
        self.assertTrue(any('LANGUAGE_TARGET' in e for e in validate_plan(p)))
    def test_07_speaking_score_rejected(self):
        p=self.fresh();p['language_target']['speaking']=6.5
        self.assertTrue(validate_plan(p))
    def test_08_auto_raise_rejected(self):
        p=self.fresh();p['language_target']['auto_raise']=True
        self.assertTrue(validate_plan(p))
    def test_09_unknown_dependency_rejected(self):
        p=self.fresh();p['tasks'][0]['depends_on']=['CX-999']
        self.assertTrue(any('UNKNOWN_DEPENDENCY' in e for e in validate_plan(p)))
    def test_10_cycle_rejected(self):
        p=self.fresh();p['tasks'][0]['depends_on']=['CX-002']
        self.assertTrue(any('DEPENDENCY_CYCLE' in e for e in validate_plan(p)))
    def test_11_duplicate_id_rejected(self):
        p=self.fresh();p['tasks'].append(copy.deepcopy(p['tasks'][0]))
        self.assertIn('DUPLICATE_ID',validate_plan(p))
    def test_12_unsafe_write_scope_rejected(self):
        p=self.fresh();p['tasks'][0]['allowed_paths']=['../outside/']
        self.assertTrue(any('UNSAFE_PATH' in e for e in validate_plan(p)))
    def test_13_absolute_and_windows_paths_rejected(self):
        for path in ['/root/file','C:/x','x/../../a','a\\b','a\x00b']:
            self.assertFalse(safe_relative(path))
        self.assertTrue(safe_relative('reports/execution/CX-001.json'))
    def test_14_fake_initial_done_rejected(self):
        p=self.fresh();p['tasks'][0]['initial_status']='accepted'
        self.assertTrue(any('FAKE_INITIAL' in e for e in validate_plan(p)))
    def test_15_unknown_trace_owner_rejected(self):
        tr=copy.deepcopy(self.trace);tr['requirements'][0]['assigned_task']='UNKNOWN'
        self.assertTrue(any('TRACE_OWNER' in e for e in validate_plan(self.plan,tr)))
    def test_16_fake_product_pass_rejected(self):
        tr=copy.deepcopy(self.trace);tr['requirements'][0]['execution_status']='passed'
        self.assertTrue(any('TRACE_FALSE' in e for e in validate_plan(self.plan,tr)))
    def test_17_all_chapters_require_human_calibration(self):
        cps=[t for t in self.plan['tasks'] if t['id'].startswith('CP-')]
        self.assertEqual(len(cps),48)
        self.assertEqual(len([t for t in cps if t['id'].endswith('-06') and t['kind']=='human_calibration']),8)
    def test_18_28_level_gate_has_specific_owner(self):
        x=next(x for x in self.trace['requirements'] if x['id']=='TECH-AT-49')
        self.assertEqual(x['assigned_task'],'CX-073')
    def test_19_g0_needs_actual_human(self):
        candidates={f'CX-{i:03d}' for i in range(1,6)}
        done,errors=derive_completed(self.plan,candidates)
        self.assertNotIn('G0',done)
        done,errors=derive_completed(self.plan,candidates|{'HV-001'})
        self.assertIn('G0',done);self.assertNotIn('G1',done)
    def test_20_claimed_task_without_dependencies_not_done(self):
        done,errors=derive_completed(self.plan,{'CX-080'})
        self.assertNotIn('CX-080',done);self.assertTrue(errors)
    def test_21_enabled_online_adds_real_gate_dependencies(self):
        g=next(g for g in self.plan['gates'] if g['id']=='G6')
        self.assertNotIn('CX-069',all_dependencies(g,{'online_model':False}))
        self.assertIn('CX-069',all_dependencies(g,{'online_model':True}))
        self.assertIn('HV-008',all_dependencies(g,{'online_model':True}))
    def test_22_all_other_work_cannot_pass_enabled_online(self):
        p=self.fresh();p['features']['online_model']=True
        ids={t['id'] for t in p['tasks']} - {'CX-069','HV-008'}
        done,_=derive_completed(p,ids)
        self.assertNotIn('G6',done);self.assertNotIn('CX-080',done)
    def test_23_missing_evidence_rejected(self):
        state=copy.deepcopy(self.state);state['tasks']['CX-001']['status']='accepted'
        candidates,errors=execution_candidates(ROOT,self.plan,state)
        self.assertNotIn('CX-001',candidates);self.assertTrue(errors)
    def test_24_synthetic_review_rejected(self):
        r={'task_id':'TEST','result':'accepted','review':{'decision':'accepted','reviewer':'UNIT_TEST','is_synthetic':True},'artifacts':[]}
        self.assertIn('SYNTHETIC_OR_UNKNOWN_REVIEW',evidence_errors(ROOT,{'id':'TEST','kind':'engineering'},r))
    def test_25_pending_review_rejected(self):
        r={'task_id':'TEST','result':'accepted','review':{'decision':'pending','is_synthetic':False},'artifacts':[]}
        self.assertIn('REVIEW_PENDING',evidence_errors(ROOT,{'id':'TEST','kind':'engineering'},r))
    def test_26_human_evidence_cannot_be_empty(self):
        r={'task_id':'TEST','result':'accepted','review':{'decision':'accepted','reviewer':'UNIT_TEST','is_synthetic':False},'artifacts':[]}
        self.assertIn('HUMAN_EVIDENCE_MISSING',evidence_errors(ROOT,{'id':'TEST','kind':'human_calibration'},r))
    def test_27_wrong_artifact_hash_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d);(p/'sample.txt').write_text('fixture, not production')
            r={'task_id':'TEST','result':'accepted','review':{'decision':'accepted','reviewer':'UNIT_TEST','is_synthetic':True},'artifacts':[{'path':'sample.txt','sha256':'0'*64}]}
            self.assertIn('ARTIFACT_HASH',evidence_errors(p,{'id':'TEST','kind':'engineering'},r))
    def test_28_failed_command_rejected(self):
        r={'task_id':'TEST','result':'accepted','commands':[{'exit_code':2}],'review':{},'artifacts':[]}
        self.assertIn('COMMAND_FAILED',evidence_errors(ROOT,{'id':'TEST','kind':'engineering'},r))
    def test_29_duplicate_json_keys_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'fixture.json';p.write_text('{"a":1,"a":2}')
            with self.assertRaises(ValueError):load_json(p)
    def test_30_trace_source_locations_unchanged(self):
        for x in self.trace['requirements']:
            line=(ROOT/x['source']).read_text(encoding='utf8').splitlines()[x['line']-1]
            self.assertIn(x['id'],line)
    def test_31_cards_have_bounded_outputs(self):
        for t in self.plan['tasks']:
            self.assertGreaterEqual(len(t['deliverables']),2)
            self.assertGreaterEqual(len(t['acceptance']),3)
            self.assertTrue(t['allowed_paths'])
    def test_32_no_current_evidence_created_by_planning(self):
        self.assertTrue(all(x['status']=='not_started' and x['evidence'] is None for x in self.state['tasks'].values()))
        self.assertFalse((ROOT/'reports/execution').exists() and list((ROOT/'reports/execution').glob('*.json')))

if __name__=='__main__':unittest.main(verbosity=2)

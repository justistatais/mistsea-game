from __future__ import annotations
import copy,json,unittest
from dataclasses import replace
from decimal import Decimal
from pathlib import Path
from jsonschema import Draft202012Validator
from pipeline_reference import *

ROOT=Path(__file__).resolve().parents[1]
def load(p):return json.loads((ROOT/p).read_text(encoding='utf-8'))

class Contracts(unittest.TestCase):
    def setUp(self):
        self.catalog=load('data/Source_Material_Catalog_v0.1.json')
        self.bundle=load('data/Q102_Production_Bundle_v0.1.json')
        self.schema=Draft202012Validator(load('schemas/Runtime_Reply_v0.1.schema.json'))
        self.draft='The beacon can be used after replace the bracket.'
        self.ctx=RuntimeContext('synthetic-profile',0,2,4,'branch-A',1,'bundle-0.1','draft-A',1,canonical_hash(self.draft),'sentence_only','model-fixture','prompt-0.1')
        self.source={'asset_id':'visible','version':'0.1','segment_id':'p1','text':'Only if the lamp has been checked.','audience':'player_visible'}
        self.sources=project_sources([self.source],{'visible::p1':'0.1'},{'visible'},cloud_consent=True)
        self.req=build_request(self.ctx,'feedback',self.draft,self.sources)
        i=self.draft.index('after replace')
        self.reply={'request_id':self.req['request_id'],'mode':'feedback','status':'candidate','items':[{'start':i,'end':i+13,'quote':'after replace','category':'grammar','certainty':'issue','explanation':'Use a gerund after this preposition in this construction.','suggestion':'after replacing'}]}
    def valid_release_fixture(self):
        b=copy.deepcopy(self.bundle);b['rights']='reviewed'
        for a in b['assets']:
            if a['requires_audio']:a.update(audio_status='auditioned',audio_script_hash=a['script_hash'])
        fp=assessment_fingerprint(b)
        reviewers={'fixture-story':{'story'},'fixture-language':{'language','calibration'},'fixture-designer':{'puzzle','integration'}}
        approvals=[{'role':role,'reviewer':reviewer,'fingerprint':fp,'decision':'accept','evidence_ref':'SYNTHETIC-TEST-ONLY'}
                   for reviewer,roles in reviewers.items() for role in roles]
        return b,approvals,reviewers
    # Inventory and target checks
    def test_inventory_28_unique(self):self.assertEqual(self.catalog['unique_count'],28)
    def test_inventory_5_reuses(self):self.assertEqual(self.catalog['duplicate_reuse_count'],5)
    def test_inventory_no_conflicting_body(self):self.assertEqual(self.catalog['same_id_version_conflicts'],[])
    def test_all_body_hashes_recompute(self):
        for m in self.catalog['materials']:self.assertEqual(m['content_hash'],canonical_hash(m['body']))
    def test_audio_archive_inventory_empty(self):self.assertEqual(load('data/Upstream_Manifest_v0.1.json')['audio_files_detected_in_archives'],[])
    def test_target_65_valid(self):validate_target(self.bundle['target'])
    def test_reject_target_70(self):
        self.bundle['target']['reading']=7
        with self.assertRaisesRegex(ContractError,'65'):validate_target(self.bundle['target'])
    def test_reject_auto_escalation(self):
        self.bundle['target']['auto_escalate']=True
        with self.assertRaises(ContractError):validate_target(self.bundle['target'])
    def test_reject_speaking_inference(self):
        self.bundle['target']['speaking']=6.5
        with self.assertRaises(ContractError):validate_target(self.bundle['target'])
    def test_reject_target_bool(self):
        self.bundle['target']['listening']=True
        with self.assertRaises(ContractError):validate_target(self.bundle['target'])
    def test_same_id_changed_body_rejected(self):
        m=self.catalog['materials'][0];n=copy.deepcopy(m);n['content_hash']='different'
        with self.assertRaises(ContractError):merge_records([m,n])
    def test_same_id_same_body_coalesced(self):
        m=self.catalog['materials'][0];self.assertEqual(len(merge_records([m,m])),1)
    def test_five_ui_materials_equal_source(self):
        samples=[x for x in self.catalog['materials'] if len(x['occurrences'])>1]
        self.assertEqual(len(samples),5);self.assertTrue(all(x['asset_id'].startswith('Q102-') for x in samples))
    # Dependency and review gates
    def test_dependency_graph_acyclic(self):self.assertEqual(len(dependency_order(self.bundle['dependencies'])),len(self.bundle['dependencies']))
    def test_unresolved_dependency_rejected(self):
        with self.assertRaises(ContractError):dependency_order({'a':['x']})
    def test_cycle_rejected(self):
        with self.assertRaises(ContractError):dependency_order({'a':['b'],'b':['a']})
    def test_audio_change_invalidates_calibration(self):
        impact=affected_dependents(self.bundle['dependencies'],{'Q102-L-REVISION@0.1'})
        self.assertIn('Q102-AUDIO@pending',impact);self.assertIn('Q102-CALIBRATION@pending',impact);self.assertNotIn('UI-FONT-TOKENS@cosmetic',impact)
    def test_font_token_change_not_fact_change(self):
        self.assertEqual(affected_dependents(self.bundle['dependencies'],{'UI-FONT-TOKENS@cosmetic'}),{'UI-FONT-TOKENS@cosmetic'})
    def test_internal_draft_allowed_with_label(self):self.assertEqual(release_gate(self.bundle,[],{},'internal'),[])
    def test_internal_without_label_blocked(self):
        self.bundle['unassessed_label']=False;self.assertIn('internal_label_required',release_gate(self.bundle,[],{},'internal'))
    def test_actual_bundle_cannot_player_release(self):
        errors=release_gate(self.bundle,[],{},'player_release')
        self.assertIn('required_audio_not_auditioned',errors);self.assertIn('missing_scoped_approval',errors);self.assertIn('rights_review_pending',errors)
    def test_complete_synthetic_attestations_pass(self):
        b,a,r=self.valid_release_fixture();self.assertEqual(release_gate(b,a,r,'player_release'),[])
    def test_fake_model_reviewer_not_trusted(self):
        b,a,r=self.valid_release_fixture()
        for x in a:x['reviewer']='model_says_approved'
        self.assertIn('missing_scoped_approval',release_gate(b,a,r,'player_release'))
    def test_same_story_language_reviewer_blocked(self):
        b,a,r=self.valid_release_fixture();r['fixture-story'].add('language')
        for x in a:
            if x['role']=='language':x['reviewer']='fixture-story'
        self.assertIn('independent_story_language_review_required',release_gate(b,a,r,'player_release'))
    def test_approval_after_source_change_invalid(self):
        b,a,r=self.valid_release_fixture();b['assets'][0]['hash']='revised'
        self.assertIn('missing_scoped_approval',release_gate(b,a,r,'player_release'))
    def test_approval_after_support_change_invalid(self):
        b,a,r=self.valid_release_fixture();b['support_contract']='auto show answer'
        self.assertIn('missing_scoped_approval',release_gate(b,a,r,'player_release'))
    def test_blocking_issue_blocks_release(self):
        b,a,r=self.valid_release_fixture();b['blocked_issues']=['DUPLICATE_REWARD'];self.assertIn('unresolved_blocker',release_gate(b,a,r,'player_release'))
    def test_quarantined_blocks_release(self):
        b,a,r=self.valid_release_fixture();b['status']='quarantined';self.assertIn('bundle_quarantined',release_gate(b,a,r,'player_release'))
    def test_rights_pending_blocks_release(self):
        b,a,r=self.valid_release_fixture();b['rights']='pending';self.assertIn('rights_review_pending',release_gate(b,a,r,'player_release'))
    def test_audio_from_old_script_blocked(self):
        b,a,r=self.valid_release_fixture();b['assets'][-1]['audio_script_hash']='old';self.assertIn('audio_script_version_mismatch',release_gate(b,a,r,'player_release'))
    # Runtime input projections, not model attack tests
    def test_source_projection_excludes_answer_fields(self):
        s=dict(self.source,answer='HIDDEN',world_patch={'coins':1000},title='editor-only title')
        out=project_sources([s],{'visible::p1':'0.1'},{'visible'},cloud_consent=True)
        self.assertNotIn('answer',out[0]);self.assertNotIn('world_patch',out[0]);self.assertNotIn('title',out[0])
    def test_hidden_segment_same_asset_excluded(self):
        s=dict(self.source,segment_id='p99',text='future reveal')
        self.assertEqual(len(project_sources([self.source,s],{'visible::p1':'0.1'},{'visible'},cloud_consent=True)),1)
    def test_hidden_asset_excluded(self):
        s=dict(self.source,asset_id='future',text='future reveal')
        self.assertEqual(project_sources([s],{'visible::p1':'0.1'},{'future'},cloud_consent=True),[])
    def test_view_permission_not_export_permission(self):self.assertEqual(project_sources([self.source],{'visible::p1':'0.1'},set(),cloud_consent=True),[])
    def test_no_consent_no_projection(self):
        with self.assertRaises(ContractError):project_sources([self.source],{'visible::p1':'0.1'},{'visible'},cloud_consent=False)
    def test_wrong_source_version_not_exported(self):self.assertEqual(project_sources([self.source],{'visible::p1':'0.2'},{'visible'},cloud_consent=True),[])
    def test_unreleased_audience_not_exported(self):
        s=dict(self.source,audience='authoring')
        self.assertEqual(project_sources([s],{'visible::p1':'0.1'},{'visible'},cloud_consent=True),[])
    def test_npc_generation_disabled(self):
        with self.assertRaises(ContractError):build_request(self.ctx,'npc_free_chat',self.draft,self.sources)
    def test_draft_hash_must_match(self):
        with self.assertRaises(ContractError):build_request(self.ctx,'feedback','changed',self.sources)
    def test_context_limit_not_silent_truncation(self):
        with self.assertRaises(ContractError):build_request(self.ctx,'feedback',self.draft,self.sources*7)
    # Reply shape and source integrity; semantics intentionally not proved
    def test_feedback_schema_and_span_pass(self):self.schema.validate(self.reply);validate_reply(self.reply,self.req)
    def test_reply_cannot_contain_world_patch(self):
        self.reply['world_patch']={'coins':1000}
        with self.assertRaises(Exception):self.schema.validate(self.reply)
        with self.assertRaises(ContractError):validate_reply(self.reply,self.req)
    def test_reply_cannot_contain_band(self):
        self.reply['band']=6.5
        with self.assertRaises(ContractError):validate_reply(self.reply,self.req)
    def test_nested_command_rejected(self):
        self.reply['items'][0]['execute']='depart'
        with self.assertRaises(ContractError):validate_reply(self.reply,self.req)
    def test_wrong_request_id_rejected(self):
        self.reply['request_id']='0'*64
        with self.assertRaises(ContractError):validate_reply(self.reply,self.req)
    def test_wrong_quote_rejected(self):
        self.reply['items'][0]['quote']='never in draft'
        with self.assertRaises(ContractError):validate_reply(self.reply,self.req)
    def test_bad_span_rejected(self):
        self.reply['items'][0]['end']=9999
        with self.assertRaises(ContractError):validate_reply(self.reply,self.req)
    def test_noninteger_span_rejected(self):
        self.reply['items'][0]['start']=True
        with self.assertRaises(ContractError):validate_reply(self.reply,self.req)
    def test_empty_candidate_rejected(self):
        self.reply['items']=[]
        with self.assertRaises(ContractError):validate_reply(self.reply,self.req)
    def test_unassessed_empty_passes(self):
        self.reply.update(status='unassessed',items=[]);self.schema.validate(self.reply);validate_reply(self.reply,self.req)
    def test_intent_B_not_silently_changed(self):
        req=build_request(self.ctx,'intent_candidate',self.draft,self.sources)
        reply={'request_id':req['request_id'],'mode':'intent_candidate','status':'candidate','items':[{'route':'B','main_generator':'on','beacon_plan':'unspecified','unresolved':[]}]}
        self.schema.validate(reply);validate_reply(reply,req);self.assertEqual(reply['items'][0]['route'],'B')
    def test_citation_must_be_authorized(self):
        c={'asset_id':'future','version':'0.1','segment_id':'p1','start':0,'end':4,'quote':'Only'}
        with self.assertRaises(ContractError):validate_citation(c,self.sources)
    def test_citation_exact_span(self):
        c={'asset_id':'visible','version':'0.1','segment_id':'p1','start':0,'end':4,'quote':'Only'};validate_citation(c,self.sources)
    def test_schema_does_not_prove_semantic_truth(self):
        self.reply['items'][0]['explanation']='This invented grammatical claim is wrong, despite valid shape.'
        self.schema.validate(self.reply);validate_reply(self.reply,self.req)
        # This passing test explicitly documents a limitation, not semantic safety.
    # Delivery and cache partition
    def test_current_feedback_delivery(self):self.assertEqual(route_delivery(self.ctx,self.ctx,still_consented=True),'current_feedback_only')
    def test_modified_draft_goes_old_only(self):self.assertEqual(route_delivery(self.ctx,replace(self.ctx,draft_revision=2,draft_hash='new'),still_consented=True),'old_draft_only')
    def test_reload_keeps_future_out_of_world(self):self.assertEqual(route_delivery(self.ctx,replace(self.ctx,load_epoch=2),still_consented=True),'history_only_no_world_projection')
    def test_other_branch_no_current_projection(self):self.assertEqual(route_delivery(self.ctx,replace(self.ctx,branch_id='B'),still_consented=True),'history_only_no_world_projection')
    def test_deletion_discards_late_response(self):self.assertEqual(route_delivery(self.ctx,replace(self.ctx,deletion_epoch=1),still_consented=True),'discard')
    def test_revoked_consent_discards(self):self.assertEqual(route_delivery(self.ctx,self.ctx,still_consented=False),'discard')
    def test_permission_change_discards(self):self.assertEqual(route_delivery(self.ctx,replace(self.ctx,permission_revision=5),still_consented=True),'discard')
    def test_other_profile_discards(self):self.assertEqual(route_delivery(self.ctx,replace(self.ctx,profile_id='other'),still_consented=True),'discard')
    def test_request_revoked_discards(self):self.assertEqual(route_delivery(self.ctx,self.ctx,still_consented=True,request_revoked=True),'discard')
    def test_bundle_revoked_discards(self):self.assertEqual(route_delivery(self.ctx,self.ctx,still_consented=True,bundle_revoked=True),'discard')
    def test_cache_key_changes_with_support(self):self.assertNotEqual(self.ctx.key('explain',self.sources),replace(self.ctx,support_scope='full_solution').key('explain',self.sources))
    def test_cache_key_changes_with_model(self):self.assertNotEqual(self.ctx.key('feedback',self.sources),replace(self.ctx,model_revision='model-2').key('feedback',self.sources))
    def test_cache_key_changes_with_profile(self):self.assertNotEqual(self.ctx.key('feedback',self.sources),replace(self.ctx,profile_id='other').key('feedback',self.sources))
    def test_cache_key_same_request_stable(self):self.assertEqual(self.ctx.key('feedback',self.sources),self.ctx.key('feedback',copy.deepcopy(self.sources)))
    # Retry budget, no prices or paid calls
    def test_only_two_attempts_allowed(self):self.assertTrue(retry_allowed(failure='rate_limit',attempts=1,consent=True,budget_ok=True));self.assertFalse(retry_allowed(failure='rate_limit',attempts=2,consent=True,budget_ok=True))
    def test_schema_failure_no_retry(self):self.assertFalse(retry_allowed(failure='schema',attempts=1,consent=True,budget_ok=True))
    def test_consent_cancellation_no_retry(self):self.assertFalse(retry_allowed(failure='service_error',attempts=1,consent=False,budget_ok=True))
    def test_budget_blocks_retry(self):self.assertFalse(retry_allowed(failure='service_error',attempts=1,consent=True,budget_ok=False))
    def test_budget_reserves_and_releases(self):
        b=Budget('10');self.assertTrue(b.reserve('a','8'));self.assertFalse(b.reserve('b','3'));b.cancel_unsent('a');self.assertTrue(b.reserve('b','3'))
    def test_budget_replay_not_double_charge(self):
        b=Budget('10');b.reserve('a','8');b.settle('a','6');b.settle('a','6');self.assertEqual(b.spent,Decimal('6'))
    def test_budget_payload_conflict(self):
        b=Budget('10');b.reserve('a','5')
        with self.assertRaises(ContractError):b.reserve('a','6')
    def test_actual_overage_recorded_not_hidden(self):
        b=Budget('10');b.reserve('a','4')
        with self.assertRaises(ContractError):b.settle('a','11')
        self.assertEqual(b.spent,Decimal('11'));self.assertFalse(b.reserve('b','1'))
    def test_negative_charge_rejected(self):
        b=Budget('10');b.reserve('a','4')
        with self.assertRaises(ContractError):b.settle('a','-1')
    # Audio and immutable bundles
    def test_real_audio_card_stays_script(self):validate_audio(load('data/Audio_Production_Card_v0.1.json'))
    def test_script_cannot_fake_timestamps(self):
        a=load('data/Audio_Production_Card_v0.1.json');a['segments']=[{'id':'x','start_ms':0,'end_ms':10}]
        with self.assertRaises(ContractError):validate_audio(a)
    def valid_audio_fixture(self):return {'state':'auditioned','script_hash':'fixture','rendered_from_hash':'fixture','audio_hash':'FAKE-TEST-BYTES-NOT-AUDIO','duration_ms':200,'listening_review_ref':'SYNTHETIC','required_segment_ids':['p1','p2'],'segments':[{'id':'p1','start_ms':0,'end_ms':100},{'id':'p2','start_ms':100,'end_ms':200}]}
    def test_synthetic_audio_metadata_pass(self):validate_audio(self.valid_audio_fixture())
    def test_out_of_range_audio_rejected(self):
        a=self.valid_audio_fixture();a['segments'][1]['end_ms']=201
        with self.assertRaises(ContractError):validate_audio(a)
    def test_audio_missing_text_segment_rejected(self):
        a=self.valid_audio_fixture();a['segments'].pop()
        with self.assertRaises(ContractError):validate_audio(a)
    def test_audio_unreviewed_overlap_rejected(self):
        a=self.valid_audio_fixture();a['segments'][1]['start_ms']=99
        with self.assertRaises(ContractError):validate_audio(a)
    def test_audio_wrong_script_rejected(self):
        a=self.valid_audio_fixture();a['rendered_from_hash']='older'
        with self.assertRaises(ContractError):validate_audio(a)
    def test_active_pin_not_overwritten(self):self.assertEqual(choose_bundle('old','new',{'old','new'},set()),'old')
    def test_missing_pin_does_not_upgrade(self):
        with self.assertRaises(ContractError):choose_bundle('old','new',{'new'},set())
    def test_quarantine_requires_explicit_repair(self):
        with self.assertRaises(ContractError):choose_bundle('old','new',{'old','new'},{'old'})
    def test_new_task_uses_preferred(self):self.assertEqual(choose_bundle(None,'new',{'old','new'},set()),'new')
    def test_safe_package_member(self):self.assertTrue(safe_member('data/example.json'))
    def test_reject_package_escape_paths(self):
        for s in ['../secret','/etc/passwd','C:/temp/a','data\\a','data/../../secret']:
            self.assertFalse(safe_member(s))

if __name__=='__main__':unittest.main(verbosity=2)

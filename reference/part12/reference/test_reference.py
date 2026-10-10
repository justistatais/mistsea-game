from __future__ import annotations
import hashlib,json,shutil,sqlite3,subprocess,sys,tempfile,threading,unittest
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from storage_reference import Store,ContractError,digest,canonical,connect,BRANCH_TABLES
from content_reference import safe_path,parse_strict,validate_manifest,migrate_candidate

class StoreTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.path=Path(self.tmp.name);self.s=Store(self.path)
    def tearDown(self):
        try:self.s.close()
        except sqlite3.ProgrammingError:pass
        self.tmp.cleanup()
    def assert_before(self,s=None):
        s=s or self.s
        self.assertTrue(s.entity('B1','ferry')[1]['docked']);self.assertEqual(s.balance('B1','xp'),340)
        self.assertEqual(s.entity('B1','player')[1]['scene_id'],'R1-S04')
        self.assertEqual(s.db.execute('SELECT count(*) FROM receipts').fetchone()[0],0)
    def assert_after(self,s=None):
        s=s or self.s
        self.assertFalse(s.entity('B1','ferry')[1]['docked']);self.assertEqual(s.balance('B1','xp'),500)
        self.assertEqual(s.balance('B1','coins'),200);self.assertEqual(s.balance('B1','personal_parts'),5)
        self.assertEqual(s.entity('B1','player')[1]['scene_id'],'R1-S02')
        self.assertEqual(s.entity('B1','Q-102')[1]['lifecycle'],'closed')
        self.assertEqual(s.db.execute('SELECT count(*) FROM receipts').fetchone()[0],1)
        self.assertEqual(s.db.execute('SELECT count(*) FROM outbox').fetchone()[0],1)
        self.assertEqual(s.db.execute("SELECT count(*) FROM items WHERE definition_id='RE-01'").fetchone()[0],1)
    def test_atomic_depart_and_vessel_ownership(self):
        self.s.depart('a',self.s.preview());self.assert_after()
        self.assertEqual(self.s.db.execute("SELECT owner_id FROM items WHERE item_id='I-BATTERY'").fetchone()[0],'civil-ferry')
    def test_repeat_same_command(self):
        p=self.s.preview();a=self.s.depart('a',p);self.assertEqual(a,self.s.depart('a',p));self.assert_after()
    def test_same_id_different_payload_conflicts(self):
        p=self.s.preview();self.s.depart('a',p);p['route']='B'
        with self.assertRaisesRegex(ContractError,'command_id_conflict'):self.s.depart('a',p)
        self.assert_after()
    def test_new_command_same_semantic_slot(self):
        a=self.s.depart('a',self.s.preview());b=self.s.depart('b',self.s.preview());self.assertEqual(a,b);self.assert_after()
    def test_same_semantic_different_effect_conflicts(self):
        self.s.depart('a',self.s.preview());p=self.s.preview();p['route']='B'
        with self.assertRaisesRegex(ContractError,'semantic_conflict'):self.s.depart('b',p)
    def test_stale_entity_precondition(self):
        p=self.s.preview();self.s.update_entity('B1','ferry',{'battery_connected':False})
        with self.assertRaisesRegex(ContractError,'stale_precondition'):self.s.depart('a',p)
        self.assert_before()
    def test_current_invalid_configuration(self):
        self.s.update_entity('B1','ferry',{'config_revision':8})
        with self.assertRaisesRegex(ContractError,'not_ready'):self.s.depart('a',self.s.preview())
        self.assert_before()
    def test_missing_boolean_not_truthy(self):
        self.s.update_entity('B1','ferry',{'battery_connected':None})
        with self.assertRaisesRegex(ContractError,'not_ready'):self.s.depart('a',self.s.preview())
    def test_unrelated_ui_setting_does_not_invalidate(self):
        p=self.s.preview();self.s.db.execute('INSERT INTO settings VALUES(?,?)',('font_scale','2.0'));self.s.depart('a',p);self.assert_after()
    def test_context_rotation_rejects_uncommitted_click(self):
        p=self.s.preview();self.s.rotate_context()
        with self.assertRaisesRegex(ContractError,'stale_load_context'):self.s.depart('a',p)
    def test_exception_rollback_all_writes(self):
        for point in ['before_mutation','after_world','after_rewards','before_commit']:
            with self.subTest(point=point):
                def hook(value):
                    if value==point:raise RuntimeError('injected')
                with self.assertRaises(RuntimeError):self.s.depart(point,self.s.preview(),hook)
                self.assert_before();self.assertEqual(self.s.db.execute('SELECT count(*) FROM outbox').fetchone()[0],0)
    def test_ui_failure_after_commit_keeps_authoritative_result(self):
        p=self.s.preview()
        def hook(value):
            if value=='after_commit_before_ui':raise RuntimeError('ui unavailable')
        with self.assertRaises(RuntimeError):self.s.depart('a',p,hook)
        self.assert_after();self.s.depart('a',p);self.assert_after()
    def test_two_connections_one_semantic_award(self):
        p=self.s.preview();barrier=threading.Barrier(2)
        def run(i):
            s=Store(self.path,new_session=False)
            try:barrier.wait(timeout=5);return s.depart('worker'+str(i),p)['receipt_id']
            finally:s.close()
        with ThreadPoolExecutor(2) as pool:results=list(pool.map(run,[1,2]))
        self.assertEqual(results[0],results[1]);self.assert_after()
    def test_foreign_keys_and_nonnegative_balance(self):
        with self.assertRaises(sqlite3.IntegrityError):self.s.db.execute("INSERT INTO balances VALUES('UNKNOWN','x',1)")
        with self.assertRaises(sqlite3.IntegrityError):self.s.db.execute("UPDATE balances SET quantity=-1 WHERE account='coins'")
    def test_unique_item_slot(self):
        with self.assertRaises(sqlite3.IntegrityError):self.s.db.execute("INSERT INTO items VALUES('B1','OTHER','X','installed','civil-ferry','battery')")
    def test_checkpoint_before_reaward_after_fork(self):
        cp=self.s.snapshot();self.s.depart('a',self.s.preview());b=self.s.fork(cp)
        self.assertEqual(self.s.balance(b,'xp'),340);self.s.depart('new',self.s.preview(b));self.assertEqual(self.s.balance(b,'xp'),500)
        self.assertEqual(self.s.balance('B1','xp'),500)
    def test_checkpoint_after_no_second_award(self):
        a=self.s.depart('a',self.s.preview());b=self.s.fork(self.s.snapshot())
        result=self.s.depart('new',self.s.preview(b));self.assertEqual(a,result);self.assertEqual(self.s.balance(b,'xp'),500)
    def test_learning_not_in_world_snapshot(self):
        self.s.observe('obs1','attempt1');cp=self.s.snapshot();self.s.observe('obs2','attempt2');self.s.fork(cp)
        self.assertEqual(self.s.db.execute('SELECT count(*) FROM observations').fetchone()[0],2)
        payload=json.loads(self.s.db.execute('SELECT snapshot_json FROM checkpoints WHERE checkpoint_id=?',(cp,)).fetchone()[0]);self.assertNotIn('observations',payload['tables'])
    def test_loading_does_not_leak_future_world_fact(self):
        cp=self.s.snapshot();self.s.update_entity('B1','Q-102',{'future_secret':'north station'})
        b=self.s.fork(cp);self.assertNotIn('future_secret',self.s.entity(b,'Q-102')[1])
    def test_missing_release_blocks_load_without_fork(self):
        cp=self.s.snapshot()
        with self.assertRaisesRegex(ContractError,'missing_release'):self.s.fork(cp,installed_releases=set())
        self.assertEqual(self.s.db.execute('SELECT count(*) FROM branches').fetchone()[0],1)
    def test_corrupt_checkpoint_blocks_load(self):
        cp=self.s.snapshot();self.s.db.execute("UPDATE checkpoints SET snapshot_json='{}' WHERE checkpoint_id=?",(cp,))
        with self.assertRaisesRegex(ContractError,'checkpoint_checksum'):self.s.fork(cp)
    def test_future_snapshot_schema_blocks_load(self):
        cp=self.s.snapshot();row=self.s.db.execute('SELECT snapshot_json FROM checkpoints WHERE checkpoint_id=?',(cp,)).fetchone()
        payload=json.loads(row[0]);payload['schema']=99
        self.s.db.execute('UPDATE checkpoints SET snapshot_json=?,checksum=? WHERE checkpoint_id=?',(canonical(payload),digest(payload),cp))
        with self.assertRaisesRegex(ContractError,'unsupported_snapshot'):self.s.fork(cp)
    def test_draft_same_canonical_head_and_cas(self):
        d,r=self.s.save_draft('B1','Text one',expected_head=0,context=self.s.context)
        self.s.save_draft('B1','Text two',expected_head=r,context=self.s.context)
        with self.assertRaisesRegex(ContractError,'draft_cas_conflict'):self.s.save_draft('B1','Old request',expected_head=r,context=self.s.context)
        self.assertEqual(self.s.draft('B1'),'Text two')
    def test_draft_old_branch_head_without_destroying_newer_text(self):
        d,r=self.s.save_draft('B1','before',expected_head=0,context=self.s.context);cp=self.s.snapshot()
        self.s.save_draft('B1','future',expected_head=r,context=self.s.context);b=self.s.fork(cp)
        self.assertEqual(self.s.draft(b),'before');self.assertEqual(self.s.draft('B1'),'future')
        self.s.save_draft(b,'alternate',expected_head=r,context=self.s.context)
        self.assertEqual(self.s.db.execute('SELECT count(*) FROM draft_revisions').fetchone()[0],3)
    def test_draft_bound_to_load_context(self):
        context=self.s.context;self.s.rotate_context()
        with self.assertRaisesRegex(ContractError,'stale_load_context'):self.s.save_draft('B1','late',expected_head=0,context=context)
    def test_feedback_current_and_old_draft(self):
        _,r=self.s.save_draft('B1','v1',expected_head=0,context=self.s.context);request=self.s.request_feedback('B1')
        self.assertEqual(self.s.feedback(request,active_branch='B1'),'current')
        self.s.save_draft('B1','v2',expected_head=r,context=self.s.context)
        self.assertEqual(self.s.feedback(request,active_branch='B1'),'old_draft')
    def test_feedback_after_rewind_archived_not_world_command(self):
        self.s.save_draft('B1','v1',expected_head=0,context=self.s.context);cp=self.s.snapshot();request=self.s.request_feedback('B1');b=self.s.fork(cp)
        self.assertEqual(self.s.feedback(request,active_branch=b),'archived_branch');self.assertTrue(self.s.entity(b,'ferry')[1]['docked'])
    def test_feedback_after_consent_revoke_discarded(self):
        self.s.save_draft('B1','v1',expected_head=0,context=self.s.context);request=self.s.request_feedback('B1');self.s.revoke_consent()
        self.assertEqual(self.s.feedback(request,active_branch='B1'),'discarded')
    def test_delete_learning_not_world_or_canonical_draft(self):
        self.s.save_draft('B1','keep draft',expected_head=0,context=self.s.context);self.s.observe('o','a');request=self.s.request_feedback('B1')
        self.s.erase_learning();self.assertEqual(self.s.db.execute('SELECT count(*) FROM observations').fetchone()[0],0)
        self.assertEqual(self.s.draft('B1'),'keep draft');self.assertEqual(self.s.feedback(request,active_branch='B1'),'discarded');self.assert_before()
    def test_interrupted_privacy_erasure_resumes_on_open(self):
        self.s.observe('o','a')
        with self.assertRaises(ContractError):self.s.erase_learning(crash_after_control=True)
        self.s.close();self.s=Store(self.path)
        self.assertEqual(self.s.db.execute('SELECT count(*) FROM observations').fetchone()[0],0)
    def test_old_backup_does_not_resurrect_erased_learning(self):
        self.s.observe('o','a');backup=self.path/'old.sqlite';self.s.backup(backup);self.s.erase_learning();self.s.close()
        shutil.copyfile(backup,self.path/'profile.sqlite');self.s=Store(self.path)
        self.assertEqual(self.s.epoch,1);self.assertEqual(self.s.db.execute('SELECT count(*) FROM observations').fetchone()[0],0)
    def test_delete_draft_all_branches_and_late_feedback(self):
        d,r=self.s.save_draft('B1','secret',expected_head=0,context=self.s.context);cp=self.s.snapshot();request=self.s.request_feedback('B1')
        self.s.erase_draft(d);b=self.s.fork(cp)
        self.assertIsNone(self.s.draft(b));self.assertIsNone(self.s.draft('B1'));self.assertEqual(self.s.feedback(request,active_branch=b),'discarded')
        new_d,_=self.s.save_draft(b,'new consented text',expected_head=r,context=self.s.context);self.assertNotEqual(d,new_d)
    def test_existing_profile_without_privacy_control_requires_explicit_recovery(self):
        self.s.close();self.s.control_path.unlink()
        with self.assertRaisesRegex(ContractError,'missing_privacy_control'):Store(self.path)
    def test_foreign_profile_checkpoint_rejected(self):
        cp=self.s.snapshot();raw=self.s.db.execute('SELECT snapshot_json FROM checkpoints WHERE checkpoint_id=?',(cp,)).fetchone()[0]
        payload=json.loads(raw);payload['profile_id']='another-profile'
        self.s.db.execute('UPDATE checkpoints SET snapshot_json=?,checksum=? WHERE checkpoint_id=?',(canonical(payload),digest(payload),cp))
        with self.assertRaisesRegex(ContractError,'profile_mismatch'):self.s.fork(cp)
    def test_repeated_observation_not_new_attempt(self):
        self.s.observe('o1','a1');self.s.observe('o1','a1');self.s.observe('o2','a1')
        self.assertEqual(self.s.db.execute('SELECT count(*) FROM observations').fetchone()[0],1)
    def test_leases_all_or_none(self):
        self.assertTrue(self.s.acquire('B1','eventA',['Havel']))
        self.assertFalse(self.s.acquire('B1','eventB',['Counter','Havel']))
        self.assertEqual(self.s.db.execute('SELECT resource_id FROM leases').fetchall()[0][0],'Havel')
        self.assertEqual(self.s.db.execute('SELECT count(*) FROM leases').fetchone()[0],1)
    def test_load_reclaims_ephemeral_leases_only(self):
        self.s.acquire('B1','event',['Havel']);self.s.rotate_context()
        self.assertEqual(self.s.db.execute('SELECT count(*) FROM leases').fetchone()[0],0);self.assert_before()
    def test_backup_actual_sqlite_integrity(self):
        self.s.depart('a',self.s.preview());backup=self.path/'backup.sqlite';self.s.backup(backup)
        db=connect(backup)
        try:self.assertEqual(db.execute("SELECT quantity FROM balances WHERE account='xp'").fetchone()[0],500)
        finally:db.close()
    def test_migration_failure_preserves_copy_and_original(self):
        copy=self.path/'candidate.sqlite';self.s.backup(copy);db=connect(copy)
        try:
            with self.assertRaises(ContractError):migrate_candidate(db,fail=True)
            self.assertEqual(db.execute('PRAGMA user_version').fetchone()[0],1)
            self.assertIsNone(db.execute("SELECT name FROM sqlite_master WHERE name='migration_probe'").fetchone())
        finally:db.close()
        self.assertEqual(self.s.db.execute('PRAGMA user_version').fetchone()[0],1)
    def test_migration_success_is_on_candidate_only(self):
        copy=self.path/'candidate.sqlite';self.s.backup(copy);db=connect(copy)
        try:migrate_candidate(db);self.assertEqual(db.execute('PRAGMA user_version').fetchone()[0],2)
        finally:db.close()
        self.assertEqual(self.s.db.execute('PRAGMA user_version').fetchone()[0],1)

class CrashTests(unittest.TestCase):
    def test_process_termination_recovery_at_five_boundaries(self):
        for point in ['before_mutation','after_world','after_rewards','before_commit','after_commit_before_ui']:
            with self.subTest(point=point),tempfile.TemporaryDirectory() as tmp:
                store=Store(tmp);store.close()
                process=subprocess.run([sys.executable,str(Path(__file__).with_name('storage_reference.py')),'crash',tmp,point],capture_output=True,timeout=15)
                self.assertEqual(process.returncode,73,process.stderr.decode())
                store=Store(tmp)
                try:
                    committed=point=='after_commit_before_ui'
                    self.assertEqual(store.entity('B1','ferry')[1]['docked'],not committed)
                    self.assertEqual(store.balance('B1','xp'),500 if committed else 340)
                    self.assertEqual(store.db.execute('SELECT count(*) FROM receipts').fetchone()[0],int(committed))
                    self.assertEqual(store.db.execute('SELECT count(*) FROM outbox').fetchone()[0],int(committed))
                    self.assertEqual(store.db.execute('PRAGMA integrity_check').fetchone()[0],'ok')
                finally:store.close()

class ContentTests(unittest.TestCase):
    def setUp(self):
        self.files={'materials/notice.txt':b'Original notice'}
        self.m={'schema_version':1,'release_id':'REL-0.1','rules_version':'1','target':{'exam':'IELTS Academic','reading':6.5,'listening':6.5,'writing':6.5,'speaking':None},
        'assets':[{'path':p,'size':len(b),'sha256':hashlib.sha256(b).hexdigest(),'definition_id':'Q102-R-NOTICE','version':'0.1'} for p,b in self.files.items()],
        'required_audio':['audio/revision.ogg'],'review_status':'draft','calibration_status':'draft_target_65'}
    def test_valid_internal_candidate(self):validate_manifest(self.m,self.files)
    def test_fixed_target_rejects_7(self):
        self.m['target']['reading']=7
        with self.assertRaisesRegex(ContractError,'target_mismatch'):validate_manifest(self.m,self.files)
    def test_public_missing_qualification_is_blocked(self):
        with self.assertRaisesRegex(ContractError,'release_not_qualified'):validate_manifest(self.m,self.files,channel='public')
    def test_claimed_review_not_enough_without_actual_audio(self):
        self.m.update(review_status='approved',calibration_status='calibrated_for_declared_conditions')
        with self.assertRaisesRegex(ContractError,'missing_required_audio'):validate_manifest(self.m,self.files,channel='public')
    def test_content_hash_rejects_changed_same_version(self):
        self.files['materials/notice.txt']=b'Changed notice'
        with self.assertRaisesRegex(ContractError,'asset_integrity'):validate_manifest(self.m,self.files)
    def test_traversal_drive_and_active_code_rejected(self):
        for p in ['../secret.txt','/secret.txt','C:/save.txt','assets\\save.txt','a/../x.txt','a//x.txt','code.cs','addon.gd','plugin.dll','res.pck','CON.txt','dir./x.json']:
            with self.subTest(path=p),self.assertRaises(ContractError):safe_path(p)
    def test_case_collision_rejected(self):
        extra=dict(self.m['assets'][0]);extra['path']='materials/NOTICE.txt';self.m['assets'].append(extra);self.files[extra['path']]=b'Original notice'
        with self.assertRaisesRegex(ContractError,'path_collision'):validate_manifest(self.m,self.files)
    def test_duplicate_json_keys_rejected(self):
        with self.assertRaisesRegex(ContractError,'duplicate_json_key'):parse_strict('{"route":"B","route":"C"}')
    def test_nonfinite_json_rejected(self):
        with self.assertRaisesRegex(ContractError,'non_finite_number'):parse_strict('{"value":NaN}')
    def test_unknown_file_rejected(self):
        self.files['answer.txt']=b'author solution'
        with self.assertRaisesRegex(ContractError,'undeclared_file'):validate_manifest(self.m,self.files)
    def test_source_dependency_hashes_match(self):
        root=Path(__file__).resolve().parents[1];manifest=json.loads((root/'data/upstream_manifest.json').read_text())
        self.assertEqual(len(manifest['documents']),11)
        # Archive includes digest inventory only. Validate input presence when running in original workspace.
        upstream=root.parent
        available=[x for x in manifest['documents'] if (upstream/x['name']).exists()]
        for row in available:self.assertEqual(hashlib.sha256((upstream/row['name']).read_bytes()).hexdigest(),row['sha256'])

if __name__=='__main__':unittest.main(verbosity=2)

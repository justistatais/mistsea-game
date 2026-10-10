"""Executable bounded contract checks. Run with python -m unittest discover."""
from pathlib import Path
import json, copy, unittest
from ui_reference import *
ROOT=Path(__file__).resolve().parents[1]
REF=json.loads((ROOT/'data/UI_UX_Reference_v0.1.json').read_text())
MATERIALS=json.loads((ROOT/'data/Q102_Material_Reference_v0.1.json').read_text())['materials']

class RegistryTests(unittest.TestCase):
    def test_01_twenty_four_unique_views(self):
        ids=[r['id'] for r in REF['screens']];self.assertEqual(len(ids),24);self.assertEqual(len(set(ids)),24)
    def test_02_eighteen_unique_shared_components(self):
        ids=[r['id'] for r in REF['components']];self.assertEqual(len(ids),18);self.assertEqual(len(ids),len(set(ids)))
    def test_03_all_views_lock_65_and_keyboard(self):
        self.assertTrue(all(s['target_language_band']==6.5 and s['keyboard_required'] for s in REF['screens']))
    def test_04_all_views_declare_owner_restore_empty_error(self):
        self.assertTrue(all(s['authority'] and s['view_restore'] and s['forbidden'] and s['empty_loading_error_required'] for s in REF['screens']))
    def test_05_layout_rectangles_in_bounds_and_no_overlap(self):
        for item in REF['layouts']:
            w,h=item['viewport'];rects=list(item['rects'].values())
            for x,y,rw,rh in rects: self.assertTrue(0<=x<x+rw<=w and 0<=y<y+rh<=h)
            for i,(x,y,rw,rh) in enumerate(rects):
                for xx,yy,ww,hh in rects[i+1:]:
                    self.assertFalse(x<xx+ww and xx<x+rw and y<yy+hh and yy<y+rh)
    def test_06_sample_materials_are_unscored_and_audio_missing(self):
        self.assertEqual(len(MATERIALS),5)
        for m in MATERIALS:self.assertEqual(m['target_band'],6.5);self.assertEqual(m['difficulty_status'],'authored_target_not_empirically_calibrated')
        audio=next(m for m in MATERIALS if m['modality']=='listening')
        self.assertNotIn(str(audio['audio_asset']).lower(),('ready','complete','produced'))
    def test_07_no_send_keyboard_shortcut_in_editable(self):
        bindings=[b for b in REF['bindings'] if 'editable' in b['contexts']]
        self.assertEqual({b['action'] for b in bindings},{'save_draft','newline'})
    def test_08_large_font_reflows(self):
        self.assertEqual(layout(1280,1),'two_pane');self.assertEqual(layout(1280,2),'single_pane');self.assertEqual(layout(900,1),'single_pane')

class InputTests(unittest.TestCase):
    def test_09_letters_in_editor_do_not_navigate(self):
        r=InputRouter();r.switch('editable')
        for k in 'wasdmjbcl1234 ':self.assertEqual(r.key(KeyEvent(k,r.epoch)),'editor_owns_input')
    def test_10_search_and_quantity_fields_are_editable(self):
        r=InputRouter();self.assertEqual(r.key(KeyEvent('m',0),editable=True),'editor_owns_input')
    def test_11_ctrl_s_only_saves_draft(self):
        r=InputRouter();r.switch('editable');self.assertEqual(r.key(KeyEvent('s',r.epoch,control=True)),'save_draft')
    def test_12_enter_and_ctrl_enter_do_not_send(self):
        r=InputRouter();r.switch('editable')
        for ctrl in (True,False):self.assertEqual(r.key(KeyEvent('Enter',r.epoch,control=ctrl)),'editor_owns_input')
    def test_13_composition_owns_enter_escape_even_in_dialog(self):
        r=InputRouter();r.switch('confirmation')
        for k in ('Enter','Escape'):self.assertEqual(r.key(KeyEvent(k,r.epoch,composing=True)),'ime_owns_input')
        self.assertEqual(r.key(KeyEvent('Enter',r.epoch,code=229)),'ime_owns_input')
    def test_14_mode_change_discards_queue_and_stale_input(self):
        r=InputRouter();r.pending=['move'];r.switch('workspace');self.assertEqual(r.pending,[]);self.assertEqual(r.key(KeyEvent('e',0)),'stale_input_ignored')
    def test_15_key_repeat_does_not_confirm(self):
        r=InputRouter();r.switch('confirmation');self.assertEqual(r.key(KeyEvent('Enter',r.epoch,repeat=True)),'repeated_command_ignored')
    def test_16_pointer_release_cannot_cross_layers(self):
        r=InputRouter();self.assertTrue(r.release_pointer(0,'read','read'));r.switch('confirmation');self.assertFalse(r.release_pointer(0,'read','send'));self.assertFalse(r.release_pointer(1,'cancel','send'))
    def test_17_transition_cannot_move(self):
        r=InputRouter();r.switch('transition');self.assertEqual(r.key(KeyEvent('w',r.epoch)),'no_world_command')
    def test_18_layers_pause_and_restore_focus(self):
        l=Layers();l.focus='draft';l.open('writer','draft');l.open('dictionary','definition');self.assertTrue(l.paused);self.assertEqual(l.close(),'draft');self.assertTrue(l.paused);l.close();self.assertFalse(l.paused)
    def test_19_missing_return_focus_has_safe_fallback(self):
        l=Layers();l.focus='removed_material';l.open('dictionary');self.assertEqual(l.close({'workspace_heading'}),'workspace_heading')

class DraftTests(unittest.TestCase):
    def test_20_save_requires_acknowledgement(self):
        d=Draft();d.edit('A valid draft.');self.assertEqual(d.status,'dirty');t=d.request_save();self.assertEqual(d.status,'saving');d.acknowledge(t);self.assertEqual(d.status,'saved')
    def test_21_old_save_ack_cannot_mark_new_draft_saved(self):
        d=Draft();d.edit('old');t=d.request_save();d.edit('new');d.acknowledge(t);self.assertEqual(d.body,'new');self.assertEqual(d.status,'dirty')
    def test_22_storage_rejects_out_of_order_old_body(self):
        s=DurableStore();d=Draft();d.edit('old');old=d.request_save();d.edit('new');new=d.request_save();self.assertTrue(s.write(new));self.assertFalse(s.write(old));self.assertEqual(s.values[d.document].body,'new')
    def test_23_forged_document_or_generation_ack_rejected(self):
        d=Draft();d.edit('x');t=d.request_save();self.assertFalse(d.acknowledge(SaveTicket('other',t.revision,t.body,t.generation)));self.assertFalse(d.acknowledge(SaveTicket(t.document,t.revision,t.body,99)))
    def test_24_save_failure_keeps_text(self):
        d=Draft();d.edit('keep this');t=d.request_save();s=DurableStore();self.assertFalse(s.write(t,fail=True));d.fail();self.assertEqual(d.status,'error');self.assertEqual(d.body,'keep this')
    def test_25_composition_not_sent_as_finished_body(self):
        d=Draft();d.edit('confirmed');d.composing=True;self.assertIsNone(d.request_save());self.assertEqual(d.revision,1)
    def test_26_edit_invalidates_intent_not_physical_work(self):
        d=Draft();w=WorldFixture(bracket=True);d.edit('old');d.confirm_intent();d.edit('new');self.assertIsNone(d.intent_revision);self.assertTrue(w.bracket)
    def test_27_no_revision_change_for_identical_body(self):
        d=Draft();d.edit('same');d.edit('same');self.assertEqual(d.revision,1)

class ConfirmationTests(unittest.TestCase):
    def ready(self):
        w=WorldFixture();w.configure('bracket',True);w.configure('battery',True);w.test();w.route='C';w.confirmed_revision=w.proposal_revision;return w
    def test_28_ready_requires_real_fixture_and_intent(self):
        w=WorldFixture();w.route='C';w.confirmed_revision=1;self.assertFalse(w.ready());self.assertTrue(self.ready().ready())
    def test_29_no_wall_clock_in_preview(self):
        w=self.ready();p=ActionPreview(w.relevant());self.assertEqual(p.validate(w),'ready');self.assertNotIn('time',p.__dict__)
    def test_52_language_settings_cannot_change_world_qualification(self):
        w=self.ready();p=ActionPreview(w.relevant())
        w.ielts_score=0;w.help_count=999
        self.assertTrue(w.ready());self.assertEqual(p.validate(w),'ready')

    def test_30_unrelated_settings_do_not_invalidate(self):
        w=self.ready();p=ActionPreview(w.relevant());w.ui_scale=2;w.notifications+=1;self.assertEqual(p.validate(w),'ready')
    def test_31_disconnect_reconnect_invalidates_test(self):
        w=self.ready();p=ActionPreview(w.relevant());w.configure('battery',False);w.configure('battery',True);self.assertFalse(w.ready());self.assertEqual(p.validate(w),'stale_preview')
    def test_32_permission_change_invalidates(self):
        w=self.ready();p=ActionPreview(w.relevant());w.permission+=1;w.permission_valid=False;self.assertEqual(p.validate(w),'stale_preview')
    def test_33_load_and_close_invalidate_preview(self):
        w=self.ready();p=ActionPreview(w.relevant());w.load_epoch+=1;self.assertEqual(p.validate(w),'stale_preview');p.close();self.assertEqual(p.validate(w),'closed_preview')
    def test_34_proposal_change_invalidates(self):
        w=self.ready();p=ActionPreview(w.relevant());w.proposal_revision+=1;self.assertEqual(p.validate(w),'stale_preview')
    def test_35_repeated_command_has_one_receipt_and_reward(self):
        l=ReceiptLedger();a=l.submit('Q102:depart','c1','payload');self.assertEqual(l.submit('Q102:depart','c1','payload'),a);self.assertEqual(l.reward_count,1)
    def test_36_new_command_cannot_repeat_same_goal_reward(self):
        l=ReceiptLedger();l.submit('Q102:depart','c1','p');l.submit('Q102:depart','c2','p');self.assertEqual(l.reward_count,1)
    def test_37_changed_payload_same_command_rejected(self):
        l=ReceiptLedger();l.submit('q','c','p')
        with self.assertRaises(ValueError):l.submit('q','c','different')
    def test_51_changed_goal_same_command_rejected(self):
        l=ReceiptLedger();l.submit('old_goal','c','p')
        with self.assertRaises(ValueError):l.submit('different_goal','c','p')

    def test_38_query_does_not_mutate_unknown_or_known(self):
        l=ReceiptLedger();self.assertIsNone(l.query('unknown'));self.assertEqual(l.reward_count,0);l.submit('q','c','p');l.query('c');self.assertEqual(l.reward_count,1)
    def test_39_old_snapshot_can_legally_complete_once(self):
        l=ReceiptLedger();before=copy.deepcopy(l);l.submit('q','c','p');before.submit('q','new_c','p');self.assertEqual(before.reward_count,1)
    def test_40_loading_post_commit_cannot_repeat(self):
        l=ReceiptLedger();l.submit('q','c','p');after=copy.deepcopy(l);after.submit('q','new','p');self.assertEqual(after.reward_count,1)

class EvidenceFeedbackTests(unittest.TestCase):
    def test_41_hidden_preload_is_not_shown_support(self):
        s=SupportLog();s.shown('m','p1','translation',visible=False);self.assertEqual(s.events,[])
    def test_42_help_scope_not_global(self):
        s=SupportLog();s.shown('m','p1','dictionary');self.assertEqual(s.snapshot('m','p2'),());self.assertEqual(s.snapshot('other','p1'),())
    def test_43_support_after_answer_does_not_change_snapshot(self):
        s=SupportLog();before=s.snapshot('m','p1');s.shown('m','p1','full_answer');self.assertEqual(before,());self.assertEqual(len(s.snapshot('m','p1')),1)
    def test_44_audio_missing_does_not_fake_playback(self):
        a=Audio(False);self.assertFalse(a.play());self.assertEqual(a.state,'missing');self.assertEqual(a.show_script(),'reading_script_not_listening')
    def test_45_interrupt_keeps_audio_position(self):
        a=Audio(True);a.position=19.5;a.play();a.interrupt();self.assertEqual(a.state,'paused');self.assertEqual(a.position,19.5)
    def test_46_old_feedback_is_attached_to_old_revision(self):
        f=FeedbackArchive();self.assertEqual(f.receive(1,2,3,'old feedback'),'older_draft');self.assertEqual(f.entries[0]['revision'],2)
    def test_47_deleted_generation_cannot_repopulate(self):
        f=FeedbackArchive();f.delete();self.assertEqual(f.receive(1,2,3,'should not return'),'discarded_deleted_generation');self.assertEqual(f.entries,[])
    def test_48_permissions_before_search_and_counts(self):
        r=[dict(id='known',title='Notice',text='Harbour'),dict(id='secret',title='Future prisoner location',text='North Station')]
        self.assertEqual(visible_search(r,{'known'},'North'),[]);self.assertEqual(len(visible_search(r,{'known'},'')),1)
    def test_49_protected_input_defers_notification(self):
        n=Notifications();n.receive('letter');self.assertIsNone(n.next(True));self.assertEqual(n.next(False),'letter')
    def test_50_duplicate_notification_not_repeated(self):
        n=Notifications();n.receive('letter');n.receive('letter');self.assertEqual(n.queue,['letter']);n.next(False);n.receive('letter');self.assertEqual(n.queue,[])

if __name__=='__main__':unittest.main(verbosity=2)

#if DEBUG
using Godot;
using Mist.Application.ChapterFour;
using Mist.Application.Contracts;
using Mist.Core.ChapterFour;
using Mist.Core.Contracts;
using Mist.Core.Exploration;

public partial class Root
{
    // A copied normal entry profile, not a second producer of arrival/permits/materials.
    private async Task RunChapterFourLocalClientTests()
    {
        var checks=new List<object>();int failures=0;
        void Check(string name,bool ok){checks.Add(new{name,passed=ok});if(!ok)failures++;}
        async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        async Task Settle(){await Frame();for(int i=0;_busy&&i<3000;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);if(_busy)throw new TimeoutException("R4 local UI command did not settle.");await Frame();}
        IEnumerable<Node> Nodes(Node parent){foreach(var child in parent.GetChildren()){yield return child;foreach(var node in Nodes(child))yield return node;}}
        async Task Click(string caption){var b=Nodes(_panels.Peek()).OfType<Button>().Where(b=>b.Text==caption).ToArray();if(b.Length!=1)throw new InvalidOperationException("Expected one local UI button: "+caption+"; got "+b.Length);b[0].EmitSignal(BaseButton.SignalName.Pressed);await Settle();}
        bool HasButton(string caption)=>Nodes(_panels.Peek()).OfType<Button>().Any(b=>b.Text==caption);
        void Choices(string name,params int[] indices){var fields=Nodes(_panels.Peek()).OfType<OptionButton>().ToArray();Check(name+"_unknown_defaults",fields.Length==indices.Length&&fields.All(f=>f.Selected==0));if(fields.Length!=indices.Length)throw new InvalidOperationException(name+" row count differs.");for(int i=0;i<fields.Length;i++)fields[i].Select(indices[i]);}
        async Task Walk(Point2 stand){CloseAllPanels();var v=await _chapterFour!.ViewAsync();var route=new NavigationGrid(v.Geometry[v.Actor.Scene]).FindPath(v.Actor.Position,stand,true);if(route.Length==0)throw new InvalidOperationException("No actual safe path to local R4 stand.");await _chapterFour.TraverseAsync(route);await Refresh();}
        async Task Spot(string id){var v=await _chapterFour!.ViewAsync();await Walk(v.Geometry[v.Actor.Scene].Spots.Single(s=>s.Id==id).Stand);_selected=id;InteractSelected();await Settle();}
        async Task Exit(string scene){var v=await _chapterFour!.ViewAsync();var p=v.Geometry[v.Actor.Scene].Ports.Single(p=>p.TargetScene==scene);await Walk(p.Stand);_selected=p.Id;InteractSelected();await Settle();if((await _chapterFour.ViewAsync()).Actor.Scene!=scene)throw new InvalidOperationException("Actual optional port refused "+scene+": "+_status.Text);}
        async Task AtButton(string stand,string caption){await Spot(stand);await Click(caption);}
        async Task Original(string id,string caption){var m=_chapterFour!.Package.Materials.Single(m=>m.Id==id);await Spot(m.Spot);await Click(caption);Check(id+"_exact_retained_reader",Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&t.Text==m.English));}
        async Task SaveText(string key,string task,string button,string text)
        {
            if(_routing.Top!="第四章正文 · "+key||_chapterFourWriter is null)throw new InvalidOperationException("Reader did not route to current optional writer: "+key);
            _chapterFourWriter.Text=text;_chapterFourWriter.EmitSignal(TextEdit.SignalName.TextChanged);await Click(button);
            Check(key+"_actual_draft_CAS_ack",(await _store.GetDraftAsync(task))?.Body==text);
        }
        async Task StudyMeaning(string name){await Click("明确使用结构化辅助");Choices(name,1,2,1,1,3,1,1);await Click("明确保存当前询问意思");}
        async Task CreditMeaning(string name){await Click("明确使用结构化辅助");Choices(name,1,2,1,1,1,1);await Click("明确保存当前提议意思");}
        async Task StudyPreview(string operation,bool cancel=false)
        {
            await Spot("R4-O01.inquiry");long sequence=(await _store.SnapshotAsync()).Sequence;
            await Click(operation=="seal"?"预览实际定稿封口":"预览交付当前封件到课程台");Check("study_"+operation+"_readonly_preview",(await _store.SnapshotAsync()).Sequence==sequence);
            string title=operation=="seal"?"课程询问 · 实际定稿预览":"课程询问 · 实际交付预览";
            if(!_chapterFourStudyPreviews.TryGetValue(title,out var stack)||stack.Count==0)throw new InvalidOperationException("Study UI preview not ready: "+operation);
            if(cancel){var token=stack.Peek();CloseAllPanels();Check("study_"+operation+"_close_cancels_actual_token",(await ChapterFourStudy().ConfirmPreviewAsync(token)).Status==CommitStatus.Rejected);Check("study_"+operation+"_cancel_no_world_action",(await _store.SnapshotAsync()).Sequence==sequence);return;}
            await Click(operation=="seal"?"明确封口本份当前询问":"明确交本份封件到实际课程台");CloseAllPanels();
        }
        async Task CreditPreview(bool receive,bool cancel=false)
        {
            await Spot("R4-O02.revision");long sequence=(await _store.SnapshotAsync()).Sequence;await Click(receive?"预览组织岗位接收实际已挂卡":"预览工作台制作当前卡片");Check("credit_"+(receive?"receive":"print")+"_readonly_preview",(await _store.SnapshotAsync()).Sequence==sequence);
            string title=receive?"贡献展签 · 组织岗位接收预览":"贡献展签 · 实际制作预览";
            if(!_chapterFourCreditPreviews.TryGetValue(title,out var stack)||stack.Count==0)throw new InvalidOperationException("Credit UI preview not ready.");
            if(cancel){var token=stack.Peek();CloseAllPanels();Check("credit_preview_close_cancels_actual_token",(await ChapterFourCredit().ConfirmPreviewAsync(token)).Status==CommitStatus.Rejected);Check("credit_cancel_no_world_action",(await _store.SnapshotAsync()).Sequence==sequence);return;}
            await Click(receive?"明确由组织岗位接收实际已挂卡":"明确在工作台制成当前卡片");CloseAllPanels();
        }
        try
        {
            if(_chapterFour is null)throw new InvalidOperationException("Copy a fresh actual normal-r4-native-entry profile before invoking this DEBUG driver.");
            var start=await _store.SnapshotAsync();var initial=await _chapterFour.ViewAsync();
            Check("true_zero_XP_normal_chain_3840_entry",start.Balances["experience"]==3840&&initial.Actor.Scene=="R4-S01"&&!initial.State.SampleReceived&&!initial.State.PublicAssignment&&initial.Materials.Length==0&&start.Receipts.Any(r=>r.SemanticKey=="Q-304:resolution")&&start.Receipts.Any(r=>r.ActionType=="sail_shared_glimmer")&&start.Receipts.All(r=>!r.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal)&&r.ActionType is not("explicit_engineering_fixture" or "internal_slice_initialization")));
            Check("local_work_not_seeded",(await ChapterFourStudy().ViewAsync()).State is null&&(await ChapterFourCredit().ViewAsync()).State is null&&!start.Items.Any(i=>i.Id==StudyInquiryService.PaperId||i.Id==StudyInquiryService.ReplyPaperId||i.Id==ExhibitionCreditService.CardId));
            var landing=_chapterFour.Package.Materials.Single(m=>m.Id=="R4-Q401-R-LANDING");await Original(landing.Id,"实际领取/读取："+landing.Title);
            var receive=_chapterFour.Package.Stations.Single(s=>s.Action=="receive_sample");await AtButton(receive.Spot,receive.Label);CloseAllPanels();
            var custody=await _store.SnapshotAsync();Check("unique_Q304_sample_received_without_main_reward",custody.Items.Count(i=>i.Id==ChapterFourService.SampleId)==1&&custody.Items.Single(i=>i.Id==ChapterFourService.SampleId).OwnerId=="R4-public-verification-desk"&&custody.Balances["experience"]==3840);
            await Exit("R4-S02");await Exit("R4-O01");

            // Original reader -> same ordinary optional writer. No permit, answer or body is seeded.
            await Original("R4-O01-R-NOTICES","读取并保留课程通知原文");await Click("编辑对应的同一份正文");Check("study_reader_routes_to_free_writer",_chapterFourWriter is not null&&_chapterFourWriter.Text.Length==0&&HasButton("保存当前询问正文"));
            const string inquiry="Please retain the original and correction notices. I request an alternative public practice opportunity; allocation and the simultaneous course times remain pending.";
            await SaveText("R4-OPTIONAL-STUDY",ChapterFourStudy().Package.DraftTask,"保存当前询问正文",inquiry);CloseAllPanels();
            foreach(var m in ChapterFourStudy().Package.Materials){await AtButton(m.Spot,"实际读取公共规程："+m.Title);Check(m.Id+"_public_exact_text",Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&t.Text==m.English));CloseAllPanels();}
            await AtButton("R4-O01.inquiry","申请本次有限公共课程询问工作");await AtButton("R4-O01.inquiry","从课程台领取本份空白纸");
            await AtButton("R4-O01.notices","在通知板明确比较原件与更正");Choices("study_notice_compare",1,2,1,1,1,1);await Click("明确记录通知链比较");
            await AtButton("R4-O01.inquiry","编辑同一份正式询问正文");Check("study_saved_body_restored",_chapterFourWriter?.Text==inquiry);ApplyScale(200);await Frame();Check("study_writer_200_percent",_split.Vertical&&_chapterFourWriter?.GetThemeFontSize("font_size")==40);ApplyScale(100);await StudyMeaning("study_meaning");CloseAllPanels();
            await StudyPreview("seal",true);Check("study_cancel_keeps_held_blank_paper",(await ChapterFourStudy().ViewAsync()).State?.Stage==StudyInquiryStage.CarriedBlank);await StudyPreview("seal");long oldSeal=(await ChapterFourStudy().ViewAsync()).State!.Revision;
            await AtButton("R4-O01.inquiry","编辑同一份正式询问正文");const string revisedInquiry=inquiry+" Please keep this revised question with the paper's earlier retained version.";
            await SaveText("R4-OPTIONAL-STUDY",ChapterFourStudy().Package.DraftTask,"保存当前询问正文",revisedInquiry);await StudyMeaning("study_revised_meaning");CloseAllPanels();
            await AtButton("R4-O01.inquiry","预览交付当前封件到课程台");Check("study_edited_sealed_paper_requires_physical_recovery",!HasButton("明确交本份封件到实际课程台")&&(await ChapterFourStudy().ViewAsync()).State?.Stage==StudyInquiryStage.Sealed);CloseAllPanels();
            await AtButton("R4-O01.inquiry","明确拆封未交付的同一纸件");Check("study_recovery_reuses_unique_paper",(await ChapterFourStudy().ViewAsync()).State?.Stage==StudyInquiryStage.CarriedBlank&&(await _store.SnapshotAsync()).Items.Count(i=>i.Id==StudyInquiryService.PaperId)==1);await StudyPreview("seal");Check("study_recovery_retains_revision_history",(await ChapterFourStudy().ViewAsync()).State!.Revision>oldSeal);
            await StudyPreview("deliver",true);await StudyPreview("deliver");var delivered=await ChapterFourStudy().ViewAsync();Check("delivery_is_actual_custody_not_completion",delivered.State?.Stage==StudyInquiryStage.Received&&delivered.CollectedReply is null&&delivered.Snapshot.Items.Single(i=>i.Id==StudyInquiryService.PaperId).OwnerId==ChapterFourStudy().Package.Desk&&!delivered.Snapshot.Receipts.Any(r=>r.SemanticKey=="R4:optional-study:resolution"));
            await AtButton("R4-O01.inquiry","实际领取课程台本份答复");var reply=await ChapterFourStudy().OpenReplyAsync();Check("study_bounded_reply_shown_not_fabricated_attendance",Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&t.Text==reply.English)&&reply.ActualConsequence=="alternative_request_registered_allocation_pending"&&!reply.AlternateSessionApproved&&!reply.TimetableChanged&&!reply.AttendanceCertified);await Click("明确确认已查看本份答复");
            await AtButton("R4-O01.inquiry","实际领取课程台本份答复");await Click("明确确认已查看本份答复");Check("study_repeated_receipt_once",(await _store.SnapshotAsync()).Receipts.Count(r=>r.SemanticKey=="R4:optional-study:resolution")==1);CloseAllPanels();
            await Exit("R4-S02");await Exit("R4-S03");await Exit("R4-O02");

            var originalCredit=_chapterFour.Package.Materials.Single(m=>m.Id=="R4-O02-R-CONTRIBUTION");await Original(originalCredit.Id,"查看留存地区材料："+originalCredit.Title);await Click("编辑对应的同一份正文");Check("credit_reader_routes_to_free_writer",_chapterFourWriter is not null&&_chapterFourWriter.Text.Length==0&&HasButton("保存当前展签正文"));
            const string label="The workshop constructed the housing. Two students checked reference marks, and the presenter explained the result. Retain the original records and leave unsupported claims as questions.";
            await SaveText("R4-OPTIONAL-CREDIT",ChapterFourCredit().Package.DraftTask,"保存当前展签正文",label);CloseAllPanels();
            foreach(var m in ChapterFourCredit().Package.Materials){await AtButton(m.Spot,"实际读取来源："+m.Title);Check(m.Id+"_public_exact_text",Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&t.Text==m.English));CloseAllPanels();}
            await AtButton("R4-O02.revision","请求本次限定展签委托");await AtButton("R4-O02.revision","实际取出本份空白展示卡");await AtButton("R4-O02.revision","核对三种实际贡献岗位");Choices("credit_contribution_compare",1,1,2,1,1,1);await Click("明确记录当前来源比较");
            await AtButton("R4-O02.revision","编辑同一份展签提议");Check("credit_saved_body_restored",_chapterFourWriter?.Text==label);ApplyScale(200);await Frame();Check("credit_writer_200_percent",_split.Vertical&&_chapterFourWriter?.GetThemeFontSize("font_size")==40);ApplyScale(100);await CreditMeaning("credit_meaning");CloseAllPanels();
            await CreditPreview(false,true);Check("credit_cancel_keeps_single_blank_card",(await ChapterFourCredit().ViewAsync()).State?.Stage==ExhibitionCardStage.PlayerBlank);await CreditPreview(false);long oldCard=(await ChapterFourCredit().ViewAsync()).State!.CardRevision;
            await AtButton("R4-O02.revision","编辑同一份展签提议");await SaveText("R4-OPTIONAL-CREDIT",ChapterFourCredit().Package.DraftTask,"保存当前展签正文",label+" This revision changes public attribution only.");await CreditMeaning("credit_revised_meaning");CloseAllPanels();
            await AtButton("R4-O02.revision","明确取回未接收候选，复用同一张卡");Check("credit_recovery_reuses_card_and_retains_history",(await ChapterFourCredit().ViewAsync()).State is{Stage:ExhibitionCardStage.PlayerBlank} st&&st.CardRevision>oldCard&&(await _store.SnapshotAsync()).Items.Count(i=>i.Id==ExhibitionCreditService.CardId)==1);await CreditPreview(false);
            await AtButton("R4-O02.label","将持有的制成卡挂到原签旁");Check("mounted_card_is_real_installed_object",(await ChapterFourCredit().ViewAsync()).State?.Stage==ExhibitionCardStage.Mounted&&(await _store.SnapshotAsync()).Items.Single(i=>i.Id==ExhibitionCreditService.CardId) is{OwnerKind:"installed",OwnerId:"R4-O02.label"});await AtButton("R4-O02.label","在展位对照留存来源检查已挂卡");await CreditPreview(true,true);await CreditPreview(true);
            var credit=await ChapterFourCredit().ViewAsync();Check("credit_actual_received_once",credit.State is{Stage:ExhibitionCardStage.Received}&&credit.Snapshot.Receipts.Count(r=>r.SemanticKey=="R4:optional-credit:resolution")==1);

            CloseAllPanels();await OpenChapterFourArchive();await Frame();await Click("学生共学室：正式询问与已取答复");Check("study_archive_has_actual_collected_reply",HasButton("查看已经实际领取的答复"));await Click("查看已经实际领取的答复");Check("study_archive_preserves_actual_reply",Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&t.Text==reply.English));CloseAllPanels();
            await OpenChapterFourArchive();await Frame();await Click("贡献展示室：已得来源与实际卡片");Check("credit_archive_has_acquired_public_sources",ChapterFourCredit().Package.Materials.All(m=>HasButton(m.Title)));CloseAllPanels();
            var final=await _store.SnapshotAsync();var main=await _chapterFour.ViewAsync();
            Check("both_optional_tasks_zero_reward_and_no_main_resolution",final.Balances.Count==start.Balances.Count&&start.Balances.All(pair=>final.Balances.GetValueOrDefault(pair.Key)==pair.Value)&&ChapterFourService.MainQuests.All(q=>!final.Receipts.Any(r=>r.SemanticKey==q+":resolution"))&&!main.State.PublicAssignment&&main.Runs.Length==0);
            Check("limited_physical_stock_not_duplicated",new[]{ExhibitionCreditService.CardId,StudyInquiryService.PaperId,StudyInquiryService.ReplyPaperId}.All(id=>final.Items.Count(i=>i.Id==id)==1));
            var studyReceipt=StrictJson.Deserialize<StudyInquiryReceipt>(final.Entities.Single(e=>e.Id=="R4:study:acknowledged").Json);var creditReceipt=StrictJson.Deserialize<ExhibitionCreditReceipt>(final.Entities.Single(e=>e.Id=="R4:exhibition:accepted-label").Json);
            Check("explicit_proofs_do_not_claim_language_or_world_rewards",!studyReceipt.EnglishAssessed&&studyReceipt.ExperienceAwarded==0&&studyReceipt.MarksAwarded==0&&studyReceipt.PartsAwarded==0&&creditReceipt.ExperienceAwarded==0&&creditReceipt.MarksAwarded==0&&creditReceipt.PartsAwarded==0&&creditReceipt.TrialSafetyScope=="public_credit_only_not_trial_approval");
            await SaveChapterFourDraft();await _store.CreateCheckpointAsync("manual");
        }
        catch(Exception error){failures++;checks.Add(new{name="chapter_four_local_client_exception",passed=false,error=error.ToString()});}
        CloseAllPanels();ReleaseAudio(ref _lessonAudio);ReleaseAudio(ref _lookupAudio);await Frame();
        string output=System.Environment.GetEnvironmentVariable("MISTSEA_CLIENT_TEST_OUTPUT")??ProjectSettings.GlobalizePath("user://client-test");Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"chapter_four_local_client_results.json"),StrictJson.Serialize(new{checks,failures,source="Copied actual zero-XP normal-r4-native-entry SQLite profile; real native reader/editor/meaning/commission/physical production/recovery/delivery/receipt/archive controls; permitted safe geometry paths; no optional progress seed",human_native_play="not_run",physical_ime="not_run",screen_reader="not_run",language_calibration="human_pending",audio_playback="not_asserted"}));
        GD.Print("MISTSEA_CHAPTER_FOUR_LOCAL_CLIENT_TESTS "+checks.Count+" failures="+failures);GetTree().Quit(failures==0?0:1);
    }
}
#endif

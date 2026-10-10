#if DEBUG
using Godot;
using Mist.Core.ChapterFive;
using Mist.Core.Contracts;
using Mist.Core.ScopedPersonalWorks;

public partial class Root
{
    private async Task RunScopedPersonalClientTests()
    {
        var checks=new List<object>();int failures=0;void Check(string name,bool pass){checks.Add(new{name,passed=pass});if(!pass)failures++;}
        IEnumerable<Node> Nodes(Node n){foreach(var c in n.GetChildren()){yield return c;foreach(var x in Nodes(c))yield return x;}}
        async Task Settle(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);for(int n=0;_busy&&n<4000;n++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);if(_busy)throw new TimeoutException("Personal UI action did not settle.");await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        async Task Click(string text){var buttons=Nodes(_panels.Peek()).OfType<Button>().Where(b=>b.Text==text).ToArray();if(buttons.Length!=1)throw new InvalidOperationException("Expected one personal button "+text+"; found "+buttons.Length);buttons[0].EmitSignal(BaseButton.SignalName.Pressed);await Settle();}
        OptionButton[] Choices()=>Nodes(_panels.Peek()).OfType<OptionButton>().ToArray();
        void Select(params int[] answers){var c=Choices();Check("unknown_default_"+_routing.Top,c.Length==answers.Length&&c.All(x=>x.Selected==0));if(c.Length!=answers.Length)throw new InvalidOperationException("Unexpected personal choice fields.");for(int n=0;n<c.Length;n++)c[n].Select(answers[n]);}
        async Task At(string spot)
        {if(!await SaveChapterFiveDraft())throw new InvalidOperationException("Personal writer not safely saved.");CloseAllPanels();var v=await _chapterFive!.ViewAsync();if(v.Actor.Scene!=spot.Split('.')[0])throw new InvalidOperationException("Native detail entry remains onsite; no actor teleporter.");var path=new Mist.Core.Exploration.NavigationGrid(v.Geometry[v.Actor.Scene]).FindPath(v.Actor.Position,v.Geometry[v.Actor.Scene].Spots.Single(p=>p.Id==spot).Stand,true);if(path.Length==0)throw new InvalidOperationException("Native current safe onsite path missing.");await _chapterFive.TraverseAsync(path);await Refresh();_selected=spot;InteractSelected();await Settle();}
        async Task Station(string action){var station=_chapterFive!.Package.Stations.Single(x=>x.Action==action);await At(station.Spot);await Click(station.Label);}
        async Task Detail(string spot){await At(spot);await Click("个人工作 · 实际纸件、范围与交班");}
        async Task Material(string id){var m=_chapterFive!.Package.Materials.Single(m=>m.Id==id);await At(m.Spot);await Click("实际领取/读取："+m.Title);Check("actual_original_"+id,Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&t.Text==m.English));}
        async Task LegacyStep(string label){await Station("run_personal_access_exercise");await Click(label);}
        async Task Probe(int kind,int recipient,int fields)
        {await Detail("R5-S07.personal_review");await Click("实际尝试当前限阅或未来联系");Select(kind,recipient,fields);await Click("实际尝试并记录允许或拒绝");}
        async Task MeaningAndPreview(string quest,string body,bool cancel)
        {
            await Detail(quest=="CQ-MIRA-03"?"R5-S07.personal_review":"R5-S07.oren_duty");await Click("编辑原"+quest+"同一份正文");Check("blank_free_writer_"+quest,_chapterFiveWriter is{Text:""});
            _chapterFiveWriter!.Text=body;_chapterFiveWriter.EmitSignal(TextEdit.SignalName.TextChanged);await Click("保存第五章正文");string task=_chapterFive!.Package.WritingTasks.Single(t=>t.Quest==quest).Id;Check("actual_ack_saved_"+quest,(await _store.GetDraftAsync(task))?.Body==body);
            await Click("明确使用结构化辅助");Select(quest=="CQ-MIRA-03"?[1,1,1,1,1]:[2,1,1,1,1]);await Click("明确记录当前第五章提议");await Click("查看当前提交预览");long seq=_store.Session.Sequence;
            if(cancel){await Click("返回");Check("cancel_"+quest+"_no_world",_store.Session.Sequence==seq);await Click("查看当前提交预览");}
            await Click("明确提交第五章当前结果");
        }
        try
        {
            var initial=await _store.SnapshotAsync();Check("normal_10620_actual_previous_nodes_no_fixture",initial.Balances["experience"]==10620&&initial.Receipts.All(r=>!r.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal)&&r.ActionType is not("explicit_engineering_fixture" or "internal_slice_initialization")));Check("no_personal_source_exposed_before_work",(await ScopedPersonal().ListReadableResponsesAsync()).Length==0);
            await Station("decide_mira_review_invitation");await Click("接受本次私人工作");await Material("R5-CQMIRA03-R-EXERCISE");await Station("run_personal_access_exercise");Select(1);await Click("实际开始本次限定演练");
            for(int actor=1;actor<=2;actor++){await Detail("R5-S07.personal_review");await Click("提出当前字段与接收方问题");Select(actor,1,1);await Click("实际提出本次有限范围问题");}
            var actualReplies=await ScopedPersonal().ListReadableResponsesAsync();Check("two_current_named_responses",actualReplies.Length==2&&actualReplies.All(r=>r.PermissionReceipt!=""&&r.SourceReceipt!=""&&r.Version=="SCOPED-PERSONAL-20261010-v1"));
            await Detail("R5-S07.personal_review");await Click("查看当前实际获准应答档案");await Click("SP-W04-RENEW · CQ-MIRA-03");var reply=await ScopedPersonal().OpenResponseAsync("SP-W04-RENEW");Check("actual_readonly_reply_exact",Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&t.Text==reply.English));Check("current_dictionary_scope_actual_reply",(await ReadScopedPersonalLearningSources()).Any(s=>s.MaterialId==reply.Id&&s.Fingerprint==reply.BodyHash));
            await Detail("R5-S07.personal_review");await Click("实际把有限副件交具名保管范围");await Probe(1,1,1);await LegacyStep("实际检查必要角色字段");await Detail("R5-S07.personal_review");await Click("实际关闭本演练未来自愿联系");await Probe(2,1,1);await Detail("R5-S07.personal_review");await Click("核对实际退出拒绝并保留历史");await LegacyStep("实际演练退出未来联系");
            await Detail("R5-S07.personal_review");Select(1,1);await Click("实际追加解释更正保留旧版");await LegacyStep("实际追加纠错保留原版");await Probe(1,2,2);await LegacyStep("实际演练指定接收者限阅");
            for(int person=1;person<=2;person++){await Station("accept_personal_arrangement");Select(person,1);await Click("实际记录本人的安排决定");}
            var drillState=await _store.SnapshotAsync();Check("operations_zero_reward",drillState.Balances.OrderBy(x=>x.Key).SequenceEqual(initial.Balances.OrderBy(x=>x.Key)));Check("one_same_root_controlled_copy",drillState.Items.Count(i=>i.Id.StartsWith("MIRA03-CONTROLLED-COPY-",StringComparison.Ordinal))==1&&drillState.Items.Count(i=>i.Id==Mist.Application.ScopedPersonalWorks.ScopedPersonalWorksService.ReviewPaper)==1);
            await MeaningAndPreview("CQ-MIRA-03","The named trustee may review role counts and source references. Future optional contact is closed and a real attempt was denied. The interpretation correction keeps both versions; the main archive custody remains unchanged.",true);Check("mira_original_reward_once",(await _store.SnapshotAsync()).Balances["experience"]==10660);
            await Station("decide_oren_duty_invitation");await Click("接受本次私人工作");await Material("R5-CQOREN03-R-DUTY");await Station("arrange_oren_duty");Select(2);await Click("实际安排本次有限岗位");
            await Detail("R5-S07.oren_duty");Select(1,1);await Click("实际开展当前有限维修或答复");foreach(string label in new[]{"实际核对原同一套件","实际隔离本次检查输入","实际检查被动报警连续性","实际恢复原已核配置"}){await Detail("R5-S07.oren_duty");await Click(label);}
            await Detail("R5-S07.oren_duty");Select(1,1);await Click("实际由具名独立接替接收说明");var replies=await ScopedPersonal().ListReadableResponsesAsync();Check("actual_receiving_both_public_bounded_replies",replies.Any(r=>r.Id=="SP-LOCAL-RECEIVER")&&replies.Any(r=>r.Id=="SP-REMOTE-REVIEW"));await MeaningAndPreview("CQ-OREN-03","Oren gives a bounded answer for the same independently reviewed auxiliary kit. The incoming worker receives the tested limits and unfinished checks. The old bypass acceptance responsibility stays on record, and the post closes at the next safe reassignment window.",true);
            await Detail("R5-S07.oren_duty");await Click("实际核对独立交班与原位置安全返程");await Detail("R5-S07.oren_duty");await Click("由原节点正式交班释放岗位");var final=await _store.SnapshotAsync();Check("two_original_rewards_once",final.Balances["experience"]==10700&&final.Balances["marks"]-initial.Balances["marks"]==60&&final.Receipts.Count(r=>r.SemanticKey=="CQ-MIRA-03:resolution")==1&&final.Receipts.Count(r=>r.SemanticKey=="CQ-OREN-03:resolution")==1);Check("actual_named_replacement_and_work_released",final.Entities.Single(e=>e.Id=="workpost:CP-OREN").Kind=="released_post");
            CloseAllPanels();await OpenSystem();await Click("继续当前冒险");await Detail("R5-S07.personal_review");await Click("查看当前实际获准应答档案");await Click("SP-W04-RENEW · CQ-MIRA-03");await Click("编辑对应原节点同一份正文");Check("response_archive_dispatch_same_restored_writer",_routing.Top==ChapterFiveWriterTitle("CQ-MIRA-03")&&_chapterFiveWriter?.Text.StartsWith("The named trustee",StringComparison.Ordinal)==true);await Click("返回");
            await Station("decide_mira_review_invitation");await Click("撤回以后私人访问");Check("withdrawn_reply_and_dictionary_scope_hidden",(await ScopedPersonal().ListReadableResponsesAsync()).All(r=>r.Node!="CQ-MIRA-03")&&(await ReadScopedPersonalLearningSources()).All(s=>s.MaterialId is not("SP-W04-RENEW" or "SP-CP-MIRA-RENEW")));Check("withdraw_preserves_lawful_history_and_reward",(await _store.SnapshotAsync()).Balances["experience"]==10700&&(await _store.GetDraftAsync(_chapterFive!.Package.WritingTasks.Single(t=>t.Quest=="CQ-MIRA-03").Id))?.Body?.StartsWith("The named trustee",StringComparison.Ordinal)==true);
        }
        catch(Exception e){checks.Add(new{name="native_exception",passed=false,error=e.ToString()});failures++;}
        CloseAllPanels();string output=System.Environment.GetEnvironmentVariable("MISTSEA_CLIENT_TEST_OUTPUT")??ProjectSettings.GlobalizePath("user://client-test");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"scoped_personal_client_results.json"),StrictJson.Serialize(new{scope="actual normal 10620XP prior-source entry, native current response access, same finite copies, real policy operations, explicit unknown meaning, same drafts, cancel, fixed old rewards, independent handover, continuation and withdrawal; no positive fixture",checks,failures,human_language="pending",physical_ime="not_run",screen_reader="not_run",audio="not_recorded"}));GD.Print("MISTSEA_SCOPED_PERSONAL_CLIENT_TESTS "+checks.Count+" failures="+failures);GetTree().Quit(failures==0?0:1);
    }
}
#endif

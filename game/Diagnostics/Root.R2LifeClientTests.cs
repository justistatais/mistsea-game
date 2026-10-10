#if DEBUG
using Godot;
using Mist.Application.Contracts;
using Mist.Application.R2Life;
using Mist.Core.Contracts;
using Mist.Core.Exploration;
using Mist.Core.Quests;
using Mist.Core.R2Life;

public partial class Root
{
    private async Task RunR2LifeClientTests()
    {
        var checks=new List<object>();int failures=0;void Check(string name,bool ok){checks.Add(new{name,passed=ok});if(!ok)failures++;}
        async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        async Task Settle(){await Frame();for(int i=0;_busy&&i<3000;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);if(_busy)throw new TimeoutException("R2 life UI action did not settle.");await Frame();}
        IEnumerable<Node> Nodes(Node root){foreach(var node in root.GetChildren()){yield return node;foreach(var child in Nodes(node))yield return child;}}
        async Task Click(string caption){var buttons=Nodes(_panels.Peek()).OfType<Button>().Where(b=>b.Text==caption).ToArray();if(buttons.Length!=1)throw new InvalidOperationException("Expected one actual R2 life control: "+caption+"; found "+buttons.Length);buttons[0].EmitSignal(BaseButton.SignalName.Pressed);await Settle();}
        OptionButton Choice(string name)=>Nodes(_panels.Peek()).OfType<OptionButton>().Single(o=>o.AccessibilityName==name);
        async Task Walk(Point2 to){CloseAllPanels();var view=await _r2Life!.ViewAsync();var geometry=_chapterTwo!.CurrentGeometry(view.Snapshot)[view.Actor.Scene];var path=new NavigationGrid(geometry).FindPath(view.Actor.Position,to,true);if(path.Length==0)throw new InvalidOperationException("Actual R2 life safe path absent.");await _r2Life.TraverseAsync(path);await Refresh();}
        async Task Spot(string id){var a=(await _r2Life!.ViewAsync()).Actor;await Walk(_r2Life.Geometry[a.Scene].Spots.Single(s=>s.Id==id).Stand);_selected=id;InteractSelected();await Settle();}
        async Task Exit(string target){var a=(await _r2Life!.ViewAsync()).Actor;var port=_r2Life.Geometry[a.Scene].Ports.Single(p=>p.TargetScene==target);await Walk(port.Stand);_selected=port.Id;InteractSelected();await Settle();if((await _r2Life.ViewAsync()).Actor.Scene!=target)throw new InvalidOperationException("Actual R2 life port failed: "+target+"; "+_status.Text);}
        async Task Material(string id){var material=_r2Life!.Materials.Single(m=>m.Id==id);await Spot(material.Spot);var button=Nodes(_panels.Peek()).OfType<Button>().Single(b=>b.Text.EndsWith(material.Title,StringComparison.Ordinal));button.EmitSignal(BaseButton.SignalName.Pressed);await Settle();Check("source_"+id+"_complete_actual_display",Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&t.Text==material.English));CloseAllPanels();}
        async Task Scope()
        {
            await Spot("R2-S03.address");await Click("向作者请求这封信的有限协助许可");var fields=Nodes(_panels.Peek()).OfType<CheckBox>().ToArray();Check("scope_fields_not_default_granted",fields.All(c=>!c.ButtonPressed));foreach(var field in fields)field.ButtonPressed=true;await Click("向作者明确请求所选当前字段");CloseAllPanels();
        }
        async Task Write(string quest,string body)
        {
            await Spot(_r2Life!.Writing.Single(w=>w.Quest==quest).Spot);await Click("编辑地方英文短说明");if(_r2LifeWriter is null)throw new InvalidOperationException("Actual R2 life writer absent.");Check("new_"+quest+"_draft_blank",_r2LifeWriter.Text.Length==0);_r2LifeWriter.Text=body;_r2LifeWriter.EmitSignal(TextEdit.SignalName.TextChanged);await Click("保存地方英文短说明");Check("draft_"+quest+"_actual_local_ack",(await _store.GetDraftAsync(_r2Life.Writing.Single(w=>w.Quest==quest).Id))?.Body==body);
            ApplyScale(200);await Frame();Check("writer_"+quest+"_200_percent",_split.Vertical&&_r2LifeWriter.GetThemeFontSize("font_size")==40);ApplyScale(100);await Click("明确当前说明的意思");
            if(quest==R2LifeRules.Reply){foreach(string name in new[]{"回信邀请范围","来访承诺","地址发布范围","送达路径"}){Check("meaning_"+name+"_not_default_answer",Choice(name).Selected==0);Choice(name).Select(1);}await Click("明确确认当前稿的这些意思（结构化辅助）");}CloseAllPanels();
        }
        async Task Review()
        {await Spot("R2-O01.reply");await Click("请求作者审阅当前保存稿");Check("review_route_not_default_author_decision",Choice("作者当前处理路径").Selected==0);Choice("作者当前处理路径").Select(1);await Click("向作者提交当前保存稿供决定");CloseAllPanels();}
        async Task Submit(string quest,bool cancel)
        {
            await Spot(_r2Life!.Writing.Single(w=>w.Quest==quest).Spot);long seq=(await _store.SnapshotAsync()).Sequence;await Click("预览正式地方提交");string title="地方正式提交预览 · "+quest;if(!_r2LifePreviewTokens.TryGetValue(title,out var token))throw new InvalidOperationException("Actual R2 life preview not ready: "+_status.Text);Check("preview_"+quest+"_read_only",(await _store.SnapshotAsync()).Sequence==seq);
            if(cancel){CloseAllPanels();Check("cancelled_"+quest+"_cannot_commit",(await _r2Life.ConfirmPreviewAsync(token)).Status==CommitStatus.Rejected&&(await _store.SnapshotAsync()).Sequence==seq);await Spot(_r2Life.Writing.Single(w=>w.Quest==quest).Spot);await Click("预览正式地方提交");}
            await Click("明确提交当前地方结果");Check("receipt_"+quest+"_once",(await _store.SnapshotAsync()).Receipts.Count(r=>r.SemanticKey==quest+":resolution")==1);CloseAllPanels();
        }
        try
        {
            if(_r2Life is null||_chapterTwo is null)throw new InvalidOperationException("Requires normal-r2-life-native-entry from actual zero-XP same-vessel voyage/landing.");var initial=await _r2Life.ViewAsync();var balances=initial.Snapshot.Balances;var items=initial.Snapshot.Items;var main201=initial.Snapshot.Entities.Single(e=>e.Id=="quest:Q-201");
            Check("actual_900XP_unique_vessel_no_fixture_entry",initial.Actor.Scene=="R2-S01"&&initial.Snapshot.Balances["experience"]==900&&initial.Snapshot.Entities.Count(e=>e.Kind=="shared_vessel")==1&&initial.Snapshot.Receipts.All(r=>!r.ActionType.Contains("fixture",StringComparison.Ordinal)&&r.ActionType!="internal_slice_initialization"));Check("optional_entry_unaccepted",initial.Quests.All(q=>q.Lifecycle==QuestLifecycle.Unaccepted)&&initial.Materials.IsEmpty);
            await Exit("R2-S02");await Exit("R2-S03");var beforeScope=await _r2Life.ViewAsync();Check("private_gate_closed_without_person_scope",_r2Life.PortIssue(beforeScope.Snapshot,_r2Life.Geometry[beforeScope.Actor.Scene].Ports.Single(p=>p.TargetScene=="R2-O01")) is not null);await Scope();await Exit("R2-O01");await Spot("R2-O01.reply");await Click("受理同一地方请求");await Material(R2LifeRules.LetterMaterial);
            await Write(R2LifeRules.Reply,"I would like to start writing again. Please keep the invitation to correspond, without promising a visit or putting the street address on a public board. A short acknowledgement can go through the custodian using the existing room code; I do not wish to send the whole page.");await Review();await Submit(R2LifeRules.Reply,true);
            var letter=await _r2Life.ViewAsync();Check("actual_short_ack_custody_current_hash_and_no_visit_publication",letter.Snapshot.Entities.Any(e=>e.Id=="r2-life:post-custody")&&letter.Quests.Single(q=>q.Id==R2LifeRules.Reply).Outcome==QuestOutcome.Completed&&letter.WriterReview?.DraftHash==(await _store.GetDraftAsync(_r2Life.Writing.Single(w=>w.Quest==R2LifeRules.Reply).Id))?.Hash);
            await Exit("R2-S03");await Exit("R2-S02");await Exit("R2-S05");await Exit("R2-O02");await Spot("R2-O02.allocation");await Click("受理同一地方请求");await Material(R2LifeRules.SeedMaterial);
            foreach(string bed in _r2Life.Contract.BedIds){await Spot("R2-O02.allocation");await Click("实际检查床位 "+bed);CloseAllPanels();}await Spot("R2-O02.allocation");await Click("逐床标出当前试种配置");int index=0;foreach(string bed in _r2Life.Contract.BedIds){Check("bed_"+bed+"_no_default_crop",Choice(bed).Selected==0);Choice(bed).Select(index++<4?1:2);}await Click("明确标出当前六床配置");CloseAllPanels();Check("marked_plan_has_no_planting",(await _r2Life.ViewAsync()).State.Planted.IsEmpty);
            await Spot("R2-O02.allocation");await Click("向种植者请求当前布局批准");CloseAllPanels();foreach(string bed in _r2Life.Contract.BedIds){await Spot("R2-O02.allocation");await Click("实际种植床位 "+bed);CloseAllPanels();}
            await Write(R2LifeRules.Seeds,"The agreed trial has four quick-leaf beds and two storage-root beds. All six available spaces have been checked and planted using the existing local tools, with the walkway kept open. The expected eight early-serving baskets and four later-storage baskets remain conditional trial estimates, not a harvested stock or tower delivery.");await Submit(R2LifeRules.Seeds,true);
            var final=await _r2Life.ViewAsync();Check("six_actual_bed_records_bound_to_current_grower_approval",R2LifeRules.PlantedAll(_r2Life.Contract,final.State)&&final.Snapshot.Receipts.Count(r=>r.ActionType=="r2_trial_bed_actually_planted")==6);Check("no_rewards_no_harvest_no_mainline_change",final.Snapshot.Balances.SequenceEqual(balances)&&final.Snapshot.Items.SequenceEqual(items)&&final.Snapshot.Entities.Single(e=>e.Id=="quest:Q-201")==main201);await OpenR2LifeArchive();await Frame();Check("archive_two_actual_current_sources",Nodes(_panels.Peek()).OfType<Button>().Count(b=>_r2Life.Materials.Any(m=>m.Title==b.Text))==2);CloseAllPanels();await SaveR2LifeDraft();await _store.CreateCheckpointAsync("manual");
        }
        catch(Exception error){failures++;checks.Add(new{name="r2_life_client_exception",passed=false,error=error.ToString()});}
        CloseAllPanels();await Frame();string output=System.Environment.GetEnvironmentVariable("MISTSEA_CLIENT_TEST_OUTPUT")??ProjectSettings.GlobalizePath("user://client-test");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"r2_life_client_results.json"),StrictJson.Serialize(new{checks,failures,source="actual zero-XP 900XP same-vessel R1R2 normal arrival; native existing optional ports, sources, permission, editor, author review, custody, six inspections/planting and read-only submission controls",controller="explicit engineering selections and candidate English prose",human_native_play="not_run",physical_ime="not_run",screen_reader="not_run",language_calibration="human_pending",audio="no_new_listening_track"}));GD.Print("MISTSEA_R2_LIFE_CLIENT_TESTS "+checks.Count+" failures="+failures);GetTree().Quit(failures==0?0:1);
    }
}
#endif

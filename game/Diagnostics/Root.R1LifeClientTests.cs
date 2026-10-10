#if DEBUG
using Godot;
using Mist.Application.Storage;
using Mist.Application.Contracts;
using Mist.Application.R1Life;
using Mist.Core.Contracts;
using Mist.Core.Exploration;
using Mist.Core.Quests;
using Mist.Core.R1Life;

public partial class Root
{
    private async Task RunR1LifeClientTests()
    {
        var checks=new List<object>();int failures=0;void Check(string name,bool ok){checks.Add(new{name,passed=ok});if(!ok)failures++;}
        async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        async Task Settle(){await Frame();for(int i=0;_busy&&i<3000;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);if(_busy)throw new TimeoutException("R1 life UI command did not settle.");await Frame();}
        IEnumerable<Node> Nodes(Node root){foreach(var node in root.GetChildren()){yield return node;foreach(var child in Nodes(node))yield return child;}}
        async Task Click(string caption){var buttons=Nodes(_panels.Peek()).OfType<Button>().Where(b=>b.Text==caption).ToArray();if(buttons.Length!=1)throw new InvalidOperationException("Expected one actual R1 life UI button: "+caption+"; found "+buttons.Length);buttons[0].EmitSignal(BaseButton.SignalName.Pressed);await Settle();}
        OptionButton Choice(string name)=>Nodes(_panels.Peek()).OfType<OptionButton>().Single(o=>o.AccessibilityName==name);
        async Task Walk(Point2 point){CloseAllPanels();var actor=(await _r1Life!.ViewAsync()).Actor;var path=new NavigationGrid(_r1Life.Geometry[actor.Scene]).FindPath(actor.Position,point,true);if(path.Length==0)throw new InvalidOperationException("Actual R1 life safe route absent.");await _r1Life.TraverseAsync(path);await Refresh();}
        async Task Spot(string id){var actor=(await _r1Life!.ViewAsync()).Actor;await Walk(_r1Life.Geometry[actor.Scene].Spots.Single(s=>s.Id==id).Stand);_selected=id;InteractSelected();await Settle();}
        async Task Exit(string scene){var actor=(await _r1Life!.ViewAsync()).Actor;var port=_r1Life.Geometry[actor.Scene].Ports.Single(p=>p.TargetScene==scene);await Walk(port.Stand);_selected=port.Id;InteractSelected();await Settle();if((await _r1Life.ViewAsync()).Actor.Scene!=scene)throw new InvalidOperationException("Actual R1 life port failed: "+scene+"; "+_status.Text);}
        async Task Act(string action){var station=_r1Life!.Package.Stations.Single(s=>s.Action==action);await Spot(station.Spot);await Click(station.Label);if(action is not("arrange_rooms" or "assign_show_roles" or "watch_show"))CloseAllPanels();}
        async Task Material(string id){var material=_r1Life!.Package.Materials.Single(m=>m.Id==id);await Spot(material.Spot);var buttons=Nodes(_panels.Peek()).OfType<Button>().Where(b=>b.Text.EndsWith(material.Title,StringComparison.Ordinal)).ToArray();if(buttons.Length!=1)throw new InvalidOperationException("Actual R1 life material control missing: "+id);buttons[0].EmitSignal(BaseButton.SignalName.Pressed);await Settle();Check("source_"+id+"_actual_bilingual_display",Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&t.Text==material.English));if(material.Kind=="listening_script")Check("script_"+id+"_not_claimed_as_audio",_activeAudioId is null);CloseAllPanels();}
        async Task Write(string quest,string body)
        {
            var task=_r1Life!.Package.Writing.Single(w=>w.Quest==quest);await Spot(task.Spot);await Click("编辑地方英文留言");if(_r1LifeWriter is null)throw new InvalidOperationException("Actual R1 life writer absent.");
            Check("new_"+quest+"_draft_is_blank",_r1LifeWriter.Text.Length==0);_r1LifeWriter.Text=body;_r1LifeWriter.EmitSignal(TextEdit.SignalName.TextChanged);await Click("保存地方英文留言");Check("draft_"+quest+"_has_actual_local_save_ack",(await _store.GetDraftAsync(task.Id))?.Body==body);
            ApplyScale(200);await Frame();Check("writer_"+quest+"_200_percent",_split.Vertical&&_r1LifeWriter.GetThemeFontSize("font_size")==40);ApplyScale(100);await Click("明确确认当前地方安排（结构化辅助，不评分）");CloseAllPanels();
        }
        async Task Submit(string quest,bool cancel)
        {
            string action=quest==R1LifeRules.Rooms?"submit_rooms":"submit_show";var station=_r1Life!.Package.Stations.Single(s=>s.Action==action);await Spot(station.Spot);long before=(await _store.SnapshotAsync()).Sequence;await Click(station.Label);Check("preview_"+quest+"_readonly",(await _store.SnapshotAsync()).Sequence==before);
            string title="地方提交预览 · "+quest;if(!_r1LifePreviewTokens.TryGetValue(title,out var token))throw new InvalidOperationException("Actual R1 life preview was not ready: "+_status.Text);
            if(cancel)
            {
                CloseAllPanels();bool rejected=false;
                try{rejected=(await _r1Life.ConfirmPreviewAsync(token)).Status==CommitStatus.Rejected;}
                catch(InvalidOperationException error) when(error.Message.Contains("预览已关闭",StringComparison.Ordinal)){rejected=true;}
                Check("closed_"+quest+"_preview_cannot_commit",rejected&&(await _store.SnapshotAsync()).Sequence==before);
                await Spot(station.Spot);await Click(station.Label);
            }
            await Click("明确提交当前地方结果");Check("receipt_"+quest+"_once",(await _store.SnapshotAsync()).Receipts.Count(r=>r.SemanticKey==quest+":resolution")==1);CloseAllPanels();
        }
        try
        {
            if(_r1Life is null)throw new InvalidOperationException("Native R1 life requires normal-r1-life-native-entry from actual zero-XP prologue/Q101.");var initial=await _r1Life.ViewAsync();long marks=initial.Snapshot.Balances.GetValueOrDefault("marks"),parts=initial.Snapshot.Balances.GetValueOrDefault("parts");
            Check("actual_340XP_entry_no_fixture_history",initial.Actor.Scene=="R1-S02"&&initial.Snapshot.Balances.GetValueOrDefault("experience")==340&&initial.Snapshot.Receipts.Any(r=>r.SemanticKey=="Q-101:resolution"&&r.ActionType=="temporary_maintenance_commission_issued")&&initial.Snapshot.Receipts.All(r=>!r.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal)&&r.ActionType is not("explicit_engineering_fixture" or "internal_slice_initialization")));
            Check("entry_no_local_discovery_or_progress",!initial.State.InnDiscovered&&!initial.State.TheatreDiscovered&&initial.Materials.IsEmpty&&initial.Quests.All(q=>q.Lifecycle==QuestLifecycle.Unaccepted));
            await Act("discover_inn");await Exit("R1-O01");await Act("accept_rooms");
            foreach(string id in new[]{"R1-ROOM-L-GUESTS","R1-ROOM-R-SEPARATE","R1-ROOM-R-AMEND","R1-ROOM-R-OLD","R1-ROOM-R-CARDS","R1-ROOM-R-DESK"})await Material(id);
            await Act("confirm_arrived_guests");await Act("inspect_quiet_room");await Act("inspect_ground_room");await Act("arrange_rooms");Check("room_choices_have_no_default_allocation",new[]{"BKG-17房间","BKG-22房间","一次额外安排"}.All(n=>Choice(n).Selected==0));Choice("BKG-17房间").Select(1);Choice("BKG-22房间").Select(2);Choice("一次额外安排").Select(2);await Click("明确记录两笔当前订单的实际分房");CloseAllPanels();
            await Write(R1LifeRules.Rooms,"BKG-17 version 2 confirms the same two guests for Harbour Day 2. Keep the earlier copy as history. Quiet Twin has their two quiet beds; Ground Single gives BKG-22 level access. Please record the agreed local information appointment.");await Submit(R1LifeRules.Rooms,true);
            Check("actual_inn_365XP_current_records_not_extra_booking",(await _store.SnapshotAsync()).Balances["experience"]==365&&(await _r1Life.ViewAsync()).State.Allocations.Length==2);
            await Act("discover_theatre");await Exit("R1-O02");await Act("accept_show");await Exit("R1-O01");await Material("R1-SHOW-R-OFFER");await Exit("R1-O02");foreach(string id in new[]{"R1-SHOW-R-EQUIPMENT","R1-SHOW-L-CREW","R1-SHOW-R-STAGE"})await Material(id);
            foreach(string action in new[]{"inspect_stage","isolate_theatre_lamp","install_theatre_brace","install_theatre_cell","power_theatre_lamp","test_theatre_lamp","place_screen"})await Act(action);
            await Act("assign_show_roles");Check("show_choices_have_no_default_solution",new[]{"实际节目","讲述者","第二岗位","第二岗位人员","节目时段"}.All(n=>Choice(n).Selected==0));foreach(string name in new[]{"实际节目","讲述者","第二岗位","第二岗位人员","节目时段"})Choice(name).Select(1);await Click("明确记录实际节目和分工");CloseAllPanels();
            await Write(R1LifeRules.Show,"We will give the indoor programme from 18:00 to 18:30. The organiser will speak and the second performer will operate the lamp. The local brace, cell and screen are installed, the stage route is clear, and the current lamp configuration has passed its test.");await Submit(R1LifeRules.Show,false);
            var final=await _r1Life.ViewAsync();Check("finite_local_props_actual_install_no_main_item_payment",final.Snapshot.Items.Count(i=>i.OwnerKind=="installed"&&(i.OwnerId==R1LifeRules.Lamp||i.OwnerId==R1LifeRules.Stage))==3&&final.Snapshot.Items.Count(i=>i.OwnerKind=="warehouse"&&i.OwnerId==R1LifeRules.Store)==1);Check("life_show_zero_rewards_main_Q102_unaccepted",final.Snapshot.Balances["experience"]==365&&final.Snapshot.Balances.GetValueOrDefault("marks")==marks&&final.Snapshot.Balances.GetValueOrDefault("parts")==parts&&StrictJson.Deserialize<QuestState>(final.Snapshot.Entities.Single(e=>e.Id=="quest:Q-102").Json).Lifecycle==QuestLifecycle.Unaccepted);
            await Act("watch_show");await Click("观看已安排节目");Check("restored_local_scene_actual_learner_display",Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&t.Text==_r1Life.Package.Materials.Single(m=>m.Id=="R1-SHOW-R-SCENE").English));CloseAllPanels();await Act("watch_show");await Click("跳过演出并保留实际结果");Check("repeat_watch_skip_do_not_repeat_rewards",(await _store.SnapshotAsync()).Balances["experience"]==365&&(await _store.SnapshotAsync()).Receipts.Count(r=>r.SemanticKey==R1LifeRules.Show+":resolution")==1);
            await OpenR1LifeArchive();await Frame();Check("archive_only_current_acquired_materials",Nodes(_panels.Peek()).OfType<Button>().Count(b=>_r1Life.Package.Materials.Any(m=>m.Title==b.Text))==11);CloseAllPanels();await SaveR1LifeDraft();await _store.CreateCheckpointAsync("manual");
        }
        catch(Exception error){failures++;checks.Add(new{name="r1_life_client_exception",passed=false,error=error.ToString()});}
        CloseAllPanels();ReleaseAudio(ref _lessonAudio);ReleaseAudio(ref _lookupAudio);await Frame();string output=System.Environment.GetEnvironmentVariable("MISTSEA_CLIENT_TEST_OUTPUT")??ProjectSettings.GlobalizePath("user://client-test");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"r1_life_client_results.json"),StrictJson.Serialize(new{checks,failures,source="actual zero-XP prologue/Q101 340XP copied entry; actual native optional ports, materials, room allocation, finite local theatre installations, roles, editor, submission and restored scene controls",controller="explicit engineering choices and candidate English prose, not automatic semantic scoring",human_native_play="not_run",physical_ime="not_run",screen_reader="not_run",language_calibration="human_pending",audio_playback="not_asserted_by_script_read"}));GD.Print("MISTSEA_R1_LIFE_CLIENT_TESTS "+checks.Count+" failures="+failures);GetTree().Quit(failures==0?0:1);
    }
}
#endif

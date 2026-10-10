#if DEBUG
using System.Collections.Immutable;
using Godot;
using Mist.Application.Contracts;
using Mist.Application.OptionalLocalWorks;
using Mist.Application.Storage;
using Mist.Core.Contracts;
using Mist.Core.Exploration;
using Mist.Core.OptionalLocalWorks;

public partial class Root
{
    // Requires a copied actual normal-musicbox-native-entry SQLite profile. Navigation commands
    // are engineering inputs; invitation, readers, selection, repair, writing and receipt use UI controls.
    private async Task RunMusicBoxClientTests()
    {
        var checks=new List<object>();int failures=0;void Check(string name,bool ok){checks.Add(new{name,passed=ok});if(!ok)failures++;}
        async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        async Task Settle(){await Frame();for(int i=0;_busy&&i<3000;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);if(_busy)throw new TimeoutException("Music-box native command did not settle.");await Frame();}
        IEnumerable<Node> Nodes(Node root){foreach(var node in root.GetChildren()){yield return node;foreach(var child in Nodes(node))yield return child;}}
        async Task Click(string caption){var buttons=Nodes(_panels.Peek()).OfType<Button>().Where(b=>b.Text==caption).ToArray();if(buttons.Length!=1)throw new InvalidOperationException("Expected one actual music-box UI button: "+caption+"; got "+buttons.Length);buttons[0].EmitSignal(BaseButton.SignalName.Pressed);await Settle();}
        async Task Walk(Point2 point){CloseAllPanels();var view=await _chapterThree!.ViewAsync();var path=new NavigationGrid(view.Geometry[view.Actor.Scene]).FindPath(view.Actor.Position,point,true);if(path.Length==0)throw new InvalidOperationException("Actual safe music-box route absent.");await _chapterThree.TraverseAsync(path);await Refresh();}
        async Task Exit(string scene){var view=await _chapterThree!.ViewAsync();var port=view.Geometry[view.Actor.Scene].Ports.Single(p=>p.TargetScene==scene);await Walk(port.Stand);_selected=port.Id;InteractSelected();await Settle();if((await _chapterThree.ViewAsync()).Actor.Scene!=scene)throw new InvalidOperationException("Actual native music-box paired port rejected: "+_status.Text);}
        async Task Goto(string destination)
        {
            var view=await _chapterThree!.ViewAsync();var queue=new Queue<string>();queue.Enqueue(view.Actor.Scene);var previous=new Dictionary<string,string?>{{view.Actor.Scene,null}};
            while(queue.TryDequeue(out string? scene)){if(scene==destination)break;foreach(var port in view.Geometry[scene].Ports){var scoped=view.Snapshot with{Entities=view.Snapshot.Entities.Select(e=>e.Id=="actor"?e with{Json=StrictJson.Serialize(view.Actor with{Scene=scene})}:e).ToImmutableArray()};if(_chapterThree.PortIssue(scoped,port) is not null||previous.ContainsKey(port.TargetScene))continue;previous.Add(port.TargetScene,scene);queue.Enqueue(port.TargetScene);}}
            if(!previous.ContainsKey(destination))throw new InvalidOperationException("No currently permitted music-box navigation route.");var steps=new List<string>();for(string? scene=destination;previous[scene!] is{} from;scene=from)steps.Add(scene!);steps.Reverse();foreach(string scene in steps)await Exit(scene);
        }
        async Task Spot(string id){await Goto(id.Split('.')[0]);var view=await _chapterThree!.ViewAsync();await Walk(view.Geometry[view.Actor.Scene].Spots.Single(s=>s.Id==id).Stand);_selected=id;InteractSelected();await Settle();}
        async Task Work(string suffix,string caption){await Spot("R3-O01."+suffix);await Click(caption);CloseAllPanels();}
        MusicBoxWorkState State(BranchSnapshot s)=>StrictJson.Deserialize<MusicBoxWorkState>(s.Entities.Single(e=>e.Id==MusicBoxRepairService.StateId).Json);
        string MainWorld(BranchSnapshot s)=>StrictJson.Serialize(new{entities=s.Entities.Where(e=>e.Id=="chapter3"||e.Kind is "quest" or "shared_vessel" or "tower_readiness" or "permission" or "companion_roster" or "workpost"||e.Id.StartsWith("R3:worker:",StringComparison.Ordinal)||e.Id.Contains("actual_handover",StringComparison.Ordinal)).ToImmutableArray(),items=s.Items.Where(i=>!i.Id.StartsWith("R3-O01:",StringComparison.Ordinal)).ToImmutableArray(),balances=s.Balances});
        async Task Meaning(string prose)
        {
            await Spot("R3-O01.note");await Click("编辑实际保存的私人维修便条");if(_musicBoxWriter is null)throw new InvalidOperationException("Actual music-box native editor absent.");_musicBoxWriter.Text=prose;_musicBoxWriter.EmitSignal(TextEdit.SignalName.TextChanged);await Click("保存私人便条");Check("native_note_real_cas_ack",(await _store.GetDraftAsync(MusicBoxRepairService.Task))?.Body==_musicBoxWriter.Text);
            await Click("明确使用结构化辅助");var choices=Nodes(_panels.Peek()).OfType<OptionButton>().ToArray();Check("native_meaning_six_unselected_fields",choices.Length==6&&choices.All(o=>o.Selected==0));if(choices.Length!=6)throw new InvalidOperationException("Music-box meaning rows missing.");foreach(var choice in choices)choice.Select(1);await Click("明确记录当前便条的所选意思");CloseAllPanels();
        }
        try
        {
            if(_chapterThree is null)throw new InvalidOperationException("Native music-box driver requires Root to load its copied actual R3 arrival profile.");var start=await _store.SnapshotAsync();var initial=await _chapterThree.ViewAsync();
            bool normal=start.Balances.GetValueOrDefault("experience")==1920&&initial.Actor.Scene=="R3-S01"&&!initial.State.Landing&&!initial.State.Commission&&initial.Materials.Length==0&&!start.Entities.Any(e=>e.Id==MusicBoxRepairService.StateId)&&start.Receipts.All(r=>!r.SemanticKey.StartsWith("internal:",StringComparison.Ordinal)&&!r.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal)&&r.ActionType is not("explicit_engineering_fixture" or "internal_slice_initialization"));Check("native_input_actual_normal_arrival_not_fixture",normal);if(!normal)throw new InvalidOperationException("Music-box native input is not untouched normal arrival.");
            var landing=_chapterThree.Package.Materials.Single(m=>m.Id=="R3-Q301-R-LANDING");await Spot(landing.Spot);await Click("实际领取/读取："+landing.Title);Check("native_landing_original_reader",Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&t.Text==landing.English));CloseAllPanels();var register=_chapterThree.Package.Stations.Single(s=>s.Action=="register_landing");await Spot(register.Spot);await Click(register.Label);CloseAllPanels();string main=MainWorld(await _store.SnapshotAsync());
            await Work("owner","接受主人提出的同盒机构维修与规格阅读邀请");await Spot("R3-O01.parts");await Click("在主人当前许可内实际取得规格说明");Check("native_actual_permission_aware_full_specification",_musicBoxRepair is not null&&Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&t.Text==_musicBoxRepair.Specification.English));CloseAllPanels();
            await Work("musicbox","检查原同一音乐盒的机构");await Spot("R3-O01.parts");var part=Nodes(_panels.Peek()).OfType<OptionButton>().Single(o=>o.AccessibilityName=="音乐盒明确选件");Check("native_part_not_default_selected",part.Selected==0);part.Select(2);await Click("实际比较原规格与所选件");Check("native_wrong_fourteen_tooth_not_fitted",!State(await _store.SnapshotAsync()).Fitted&&(await _store.SnapshotAsync()).Items.Single(i=>i.Id==MusicBoxRepairService.Wheel14).OwnerKind=="warehouse");CloseAllPanels();await Spot("R3-O01.parts");Nodes(_panels.Peek()).OfType<OptionButton>().Single(o=>o.AccessibilityName=="音乐盒明确选件").Select(1);await Click("实际比较原规格与所选件");CloseAllPanels();
            await Work("owner","向主人核对已选件并请求审批");await Work("musicbox","停止同盒上弦，为装件隔离机构");await Work("musicbox","实际安装主人当前批准的同一部件");await Work("musicbox","检查当前配置的转动机构");await Work("owner","向主人实际归还已检查的同一盒子");
            const string prose="The approved MBOX-12 twelve-tooth wheel was fitted. The turning mechanism was checked, and the same box was returned to the owner. The check covered only the mechanism; the family memory remains private.";await Meaning(prose);Check("native_saved_note_without_delivery_changes_no_main_world",!State(await _store.SnapshotAsync()).Completed&&MainWorld(await _store.SnapshotAsync())==main);
            await Spot("R3-O01.owner");long before=(await _store.SnapshotAsync()).Sequence;await Click("核对当前私人便条的交付预览");const string title="音乐盒私人便条交付预览";Check("native_ready_preview_is_readonly",(await _store.SnapshotAsync()).Sequence==before);if(!_musicBoxUiPreviews.TryGetValue(title,out var previews)||previews.Count==0)throw new InvalidOperationException("Actual native delivery preview not ready.");var cancelled=previews.Peek();CloseAllPanels();Check("native_close_cancels_actual_preview",(await _musicBoxRepair!.ConfirmPreviewAsync(cancelled)).Status==CommitStatus.Rejected);
            await Meaning(prose+" This later note preserves the same limited check.");await Spot("R3-O01.owner");await Click("核对当前私人便条的交付预览");var committed=_musicBoxUiPreviews[title].Peek();await Click("明确将当前私人维修便条交给主人");var final=await _store.SnapshotAsync();Check("native_same_box_return_and_private_delivery_once",State(final).Completed&&final.Receipts.Count(r=>r.SemanticKey==MusicBoxRepairService.Completion)==1&&final.Items.Single(i=>i.Id==MusicBoxRepairService.Box) is{OwnerKind:"warehouse",OwnerId:MusicBoxRepairService.Owner});Check("native_delivery_leaves_main_balance_and_six_tower_world_exact",MainWorld(final)==main&&final.Balances["experience"]==1920);Check("native_closed_completed_preview_cannot_reexecute",(await _musicBoxRepair!.ConfirmPreviewAsync(committed)).Status==CommitStatus.Rejected&&(await _store.SnapshotAsync()).Receipts.Count(r=>r.SemanticKey==MusicBoxRepairService.Completion)==1);
            await Spot("R3-O01.owner");Check("native_completed_owner_panel_has_no_repeat_delivery",!Nodes(_panels.Peek()).OfType<Button>().Any(b=>b.Text=="核对当前私人便条的交付预览"));CloseAllPanels();await SaveOptionalLocalWorksDraft();await _store.CreateCheckpointAsync("manual");
        }
        catch(Exception error){failures++;checks.Add(new{name="musicbox_client_exception",passed=false,error=error.ToString()});}
        CloseAllPanels();await Frame();string output=System.Environment.GetEnvironmentVariable("MISTSEA_CLIENT_TEST_OUTPUT")??ProjectSettings.GlobalizePath("user://client-test");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"musicbox_client_results.json"),StrictJson.Serialize(new{checks,failures,source="actual normal zero-XP prior chapters and typed R2R3 arrival copied SQLite input; native invitation/specification/part/repair/editor/meaning/preview/receipt controls; actual safe command navigation",human_native_play="not_run",physical_ime="not_run",screen_reader="not_run",language_calibration="human_pending",audio="no music-box recording generated or listened"}));GD.Print("MISTSEA_MUSICBOX_CLIENT_TESTS "+checks.Count+" failures="+failures);GetTree().Quit(failures==0?0:1);
    }
}
#endif

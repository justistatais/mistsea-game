using Godot;
using Mist.Application.Contracts;
using Mist.Core.Contracts;
using Mist.Core.Exploration;

public partial class Root
{
    private async Task RunVesselClientTests()
    {
        var checks=new List<object>();int failures=0;void Check(string name,bool ok){checks.Add(new{name,passed=ok});if(!ok)failures++;}
        async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        async Task Settle(){await Frame();for(int i=0;_busy&&i<3000;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);if(_busy)throw new TimeoutException("UI command did not settle.");await Frame();}
        IEnumerable<Node> Nodes(Node root){foreach(var node in root.GetChildren()){yield return node;foreach(var child in Nodes(node))yield return child;}}
        async Task Click(string caption){var button=Nodes(_panels.Peek()).OfType<Button>().Single(b=>b.Text==caption);button.EmitSignal(BaseButton.SignalName.Pressed);await Settle();}
        async Task Walk(Point2 point){var path=new NavigationGrid(ActiveGeometry[_view.Actor.Scene]).FindPath(_view.Actor.Position,point,true);if(path.Length==0)throw new InvalidOperationException("Actual UI test path is absent.");if(_view.Actor.Scene.StartsWith("HUB-",StringComparison.Ordinal))CheckCommit(await _vessel!.MoveAsync(path));else await _chapter!.MoveAsync(path);await Refresh();}
        async Task Exit(string scene){var port=ActiveGeometry[_view.Actor.Scene].Ports.Single(p=>p.TargetScene==scene);await Walk(port.Stand);_selected=port.Id;InteractSelected();await Settle();}
        try
        {
            var start=await _store.SnapshotAsync();Check("actual_handover_source_contains_no_fixture",HasReceipt(start,"Q-104")&&start.Receipts.All(r=>r.ActionType!="explicit_engineering_fixture"&&!r.SemanticKey.StartsWith("internal:",StringComparison.Ordinal)));
            Check("normal_handoff_exposes_three_ship_geometries",_vessel is not null&&ActiveGeometry.Keys.Count(k=>k.StartsWith("HUB-",StringComparison.Ordinal))==3);
            await OpenVesselBerth();await Click("检查泊位并登记三个空间");Check("real_berth_button_opens_explicit_board_preview",_routing.Top=="微光号登船预览"&&_view.Actor.Scene=="R1-S01");
            var token=_vesselUiPreviews["微光号登船预览"].Peek();CloseAllPanels();Check("return_cancels_board_preview",(await _vessel!.ConfirmPreviewAsync(token)).Status==CommitStatus.Rejected&&(await _q.ViewAsync()).Actor.Scene=="R1-S01");
            await OpenVesselBerth();await Click("核对当前登记并预览登船");await Click("明确登上微光号");Check("board_button_uses_actual_actor_and_deck",_view.Actor.Scene=="HUB-S01"&&_world.Geometry?.Id=="HUB-S01"&&!_routing.WorldPaused);
            await Exit("HUB-S02");Check("native_port_control_enters_archive",_view.Actor.Scene=="HUB-S02"&&_world.Geometry?.Id=="HUB-S02");
            await Walk(ActiveGeometry["HUB-S02"].Spots.Single(p=>p.Id=="HUB-S02.archive").Stand);long sequence=(await _store.SnapshotAsync()).Sequence;await TryVesselInteraction("HUB-S02.archive");Check("actual_archive_station_opens_read_only",_panels.Count==1&&(await _store.SnapshotAsync()).Sequence==sequence);CloseAllPanels();
            await Walk(ActiveGeometry["HUB-S02"].Spots.Single(p=>p.Id=="HUB-S02.draft_desk").Stand);await TryVesselInteraction("HUB-S02.draft_desk");await Click("编辑事故核查正文");Check("ship_desk_uses_same_saved_finding",_findingWriter is not null&&_findingWriter.Text==(await _store.GetDraftAsync(Mist.Application.ChapterOne.ChapterOneService.FindingDraft))?.Body&&!MayMove());
            ApplyScale(200);await Frame();Check("ship_writer_and_buttons_scale_200",_split.Vertical&&_findingWriter!.GetThemeFontSize("font_size")==40&&Nodes(_panels.Peek()).OfType<Button>().All(b=>b.GetThemeFontSize("font_size")==40));ApplyScale(100);CloseAllPanels();
            await Exit("HUB-S03");await Walk(ActiveGeometry["HUB-S03"].Spots.Single(p=>p.Id=="HUB-S03.collection").Stand);sequence=(await _store.SnapshotAsync()).Sequence;await TryVesselInteraction("HUB-S03.collection");Check("collection_station_does_not_grant_or_duplicate",(await _store.SnapshotAsync()).Sequence==sequence&&_view.Snapshot.Items.Count(i=>i.Id.StartsWith("Q104:original:",StringComparison.Ordinal))==4);CloseAllPanels();
            await Exit("HUB-S02");await Exit("HUB-S01");await Walk(ActiveGeometry["HUB-S01"].Spots.Single(p=>p.Id=="HUB-S01.gangway").Stand);await TryVesselInteraction("HUB-S01.gangway");await Click("明确离船返回当前泊位");Check("leave_button_returns_actual_registered_berth",_view.Actor.Scene=="R1-S01"&&!_routing.WorldPaused);
            var end=await _store.SnapshotAsync();Check("ship_ui_preserves_rewards_and_one_instance",end.Balances.OrderBy(p=>p.Key).SequenceEqual(start.Balances.OrderBy(p=>p.Key))&&end.Items.SequenceEqual(start.Items)&&end.Entities.Count(e=>e.Kind=="shared_vessel")==1&&end.Receipts.Count(r=>r.SemanticKey=="Q-104:resolution")==1);
        }
        catch(Exception error){failures++;checks.Add(new{name="vessel_client_exception",passed=false,error=error.ToString()});}
        CloseAllPanels();ReleaseAudio(ref _lessonAudio);ReleaseAudio(ref _lookupAudio);await Frame();
        string output=System.Environment.GetEnvironmentVariable("MISTSEA_CLIENT_TEST_OUTPUT")??ProjectSettings.GlobalizePath("user://client-test");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"vessel_client_results.json"),StrictJson.Serialize(new{checks,failures,source="actual normal Q104 service route copied from named C# test profile; engineering input",native_manual_ime="not_run",screen_reader="not_run"}));GD.Print("MISTSEA_VESSEL_CLIENT_TESTS "+checks.Count+" failures="+failures);GetTree().Quit(failures==0?0:1);
    }
}

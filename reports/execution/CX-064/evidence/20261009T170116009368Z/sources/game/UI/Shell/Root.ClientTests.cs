using Godot;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.DebugCandidates;
using Mist.Infrastructure.Storage;
using Mist.Core.Combat;

public partial class Root
{
    private async Task RunClientTests()
    {
        var checks=new List<object>(); int failures=0;
        void Check(string name,bool passed) { checks.Add(new { name,passed }); if(!passed)failures++; }
        try
        {
            OpenArchive(); _routing.Push("dictionary-test",Mist.Application.UI.InputOwner.Workspace); _routing.Pop(); Check("nested_close_keeps_reader_paused",_routing.WorldPaused); ClosePanel();
            OpenWriter(); await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            Check("native_text_edit_created",_writer is not null&&_writer.Editable);
            if(_writer is not null) { _writer.Text="中文😀e\u0301\nWASD Enter"; _draft!.Edit(_writer.Text); Check("native_unicode_roundtrip",_writer.Text=="中文😀e\u0301\nWASD Enter"); }
            Check("editor_blocks_world",!MayMove()); Check("real_draft_ack",await SaveDraft());
            ApplyScale(200); Check("200_percent_editor_font",_writer!.GetThemeFontSize("font_size")==40); ApplyScale(100);
            CloseAllPanels(); Check("all_panels_release_pause",!_routing.WorldPaused);
            long peopleSequence=(await _store.SnapshotAsync()).Sequence;await OpenPeople();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);Check("people_list_uses_actual_known_region",_routing.Top=="人物与同行安排");ClosePanel();
            await OpenPerson("N-HAVEL");Check("dialogue_known_actor_reads_no_world_result",_routing.Owner==Mist.Application.UI.InputOwner.Dialogue);ClosePanel();await OpenWorkOrders();Check("unopened_work_orders_remain_read_only",_routing.Top=="工作、协议与准备"&&(await _store.SnapshotAsync()).Sequence==peopleSequence);ClosePanel();
            ApplyScale(200);await OpenPeople();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);Check("people_200_percent_retains_single_column",_split.Vertical);ClosePanel();ApplyScale(100);
            string routeDirectory=System.Environment.GetEnvironmentVariable("MISTSEA_CLIENT_TEST_OUTPUT")??ProjectSettings.GlobalizePath("user://client-test");
            foreach(var mode in new[]{WorkshopMode.Isolated,WorkshopMode.Focused})
            {
                using var profile=new ProfileStore(System.IO.Path.Combine(routeDirectory,"godot-route-"+mode),_catalog.ReleaseId);
                var candidate=new Q102RouteCandidate(profile,_catalog);await candidate.Complete(mode,mode==WorkshopMode.Focused,mode==WorkshopMode.Focused);
                File.WriteAllText(System.IO.Path.Combine(routeDirectory,"godot-route-"+mode+".json"),StrictJson.Serialize(candidate.Steps));
                Check("godot_actual_route_"+mode,true);
            }
            {
                using var profile=new ProfileStore(System.IO.Path.Combine(routeDirectory,"godot-route-combat"),_catalog.ReleaseId);var candidate=new Q102RouteCandidate(profile,_catalog);await candidate.Complete(WorkshopMode.Disabled);File.WriteAllText(System.IO.Path.Combine(routeDirectory,"godot-route-combat.json"),StrictJson.Serialize(candidate.Steps));Check("godot_actual_route_combat",true);
            }
            var rootRoute=new Q102RouteCandidate(_store,_catalog);await rootRoute.Exit("R1-S05");await rootRoute.Walk(_q.Geometry["R1-S05"].Ports.Single(p=>p.TargetScene=="R1-S06").Stand);CheckCommit(await _encounters.BeginWorkshopAsync());await Refresh();await OpenBattle();
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);Check("battle_cards_receive_readable_width",_battleCards.Count==8&&_battleCards.All(c=>c.Size.X>200));Check("actual_combat_native_controls",_routing.Top=="工坊战斗"&&_routing.Owner==Mist.Application.UI.InputOwner.CombatDecision);string combatBefore=StrictJson.Serialize(await _encounters.ViewAsync());var returnFocus=GetViewport().GuiGetFocusOwner();OpenArchive();ClosePanel();Check("nested_archive_returns_original_battle_focus",GetViewport().GuiGetFocusOwner()==returnFocus);OpenWriter();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);Check("down_or_active_combat_editor_available",_writer is not null);ClosePanel();await SaveDraft();Check("archive_and_writer_do_not_refill_combat",combatBefore==StrictJson.Serialize(await _encounters.ViewAsync()));ClosePanel();Check("closed_battle_view_cannot_move_world",!MayMove());CheckCommit(await _encounters.WithdrawAsync());await Refresh();
            foreach(string route in new[]{"H","M","S"})foreach(string scenario in new[]{"BENCH-WORKSHOP","BENCH-TOWER"})
            {
                var definitions=new BattleDefinitions(_catalog);var state=definitions.CreateReference((scenario=="BENCH-WORKSHOP"?"BUILD-Q102-ENTRY-":"BUILD-TOWER-")+route,scenario,Guid.NewGuid(),Guid.NewGuid());var engine=new BattleEngine(definitions,state);engine.Start();var result=BattleCandidate.Complete(engine);File.WriteAllText(System.IO.Path.Combine(routeDirectory,"godot-battle-"+scenario+"-"+route+".json"),StrictJson.Serialize(result));Check("godot_core_"+scenario+"_"+route,result.Outcome==CombatOutcome.Controlled);
            }
            foreach(var material in _catalog.Materials.Where(m=>m.Modality=="listening"))
            {
                using var stream=ResourceLoader.Load<AudioStream>("res://content/"+material.AudioAsset,"",ResourceLoader.CacheMode.Ignore);Check("actual_audio_decode_"+material.Id,stream is not null&&stream.GetLength()>1);
            }
            var listening=_catalog.Materials.Single(m=>m.Id=="Q102-L-REVISION");OpenListening(listening);
            _lessonAudio!.Play(3);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);_lessonAudio.StreamPaused=true;
            float position=_lessonAudio.GetPlaybackPosition();OpenDictionary("bracket",listening);Check("dictionary_preserves_main_audio_instance",_activeAudioId==listening.Id&&_lessonAudio.StreamPaused);ClosePanel();
            ClosePanel();Check("lesson_close_remembers_position",_audioPositions.GetValueOrDefault(listening.Id)>=position-0.1);OpenListening(listening);Check("lesson_position_survives_reopen",_audioPositions[listening.Id]>=2.9);ClosePanel();
            ApplyScale(200);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);Check("200_percent_single_column",_split.Vertical);Check("200_percent_no_horizontal_overflow",_split.Size.X<=Size.X);
            ApplyScale(100);
            CloseAllPanels();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
        catch(Exception error){failures++; checks.Add(new { name="client_exception",passed=false,error=error.ToString() });}
        string directory=System.Environment.GetEnvironmentVariable("MISTSEA_CLIENT_TEST_OUTPUT")??ProjectSettings.GlobalizePath("user://client-test");Directory.CreateDirectory(directory);
        File.WriteAllText(System.IO.Path.Combine(directory,"client_results.json"),StrictJson.Serialize(new { checks,failures,native_manual_ime="not_run",screen_reader="not_run" }));
        GD.Print("MISTSEA_CLIENT_TESTS "+checks.Count+" failures="+failures);GetTree().Quit(failures==0?0:1);
    }
}

#if DEBUG
using System.Collections.Immutable;
using Godot;
using Mist.Application.Finale;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Exploration;
using Mist.Core.Finale;
using Mist.Core.Progression;
using Mist.Core.Quests;
using Mist.Core.ReleaseReadiness;
using Mist.Core.Skills;
using Mist.DebugCandidates;

public partial class Root
{
    private async Task RunFinaleClientTests()
    {
        var checks=new List<object>();var transitions=new List<object>();var battles=new List<object>();int failures=0;void Check(string name,bool yes){checks.Add(new{name,passed=yes});if(!yes)failures++;}
        async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        async Task Settle(){await Frame();for(int n=0;_busy&&n<3000;n++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);if(_busy)throw new TimeoutException("RF native action did not settle.");await Frame();}
        IEnumerable<Node> Nodes(Node root){foreach(var node in root.GetChildren()){yield return node;foreach(var child in Nodes(node))yield return child;}}
        async Task Click(string caption){var found=Nodes(_panels.Peek()).OfType<Button>().Where(b=>b.Text==caption).ToArray();if(found.Length!=1)throw new InvalidOperationException("Expected one actual RF UI button: "+caption+" / "+found.Length);found[0].EmitSignal(BaseButton.SignalName.Pressed);await Settle();}
        OptionButton Choice(string name)=>Nodes(_panels.Peek()).OfType<OptionButton>().Single(o=>o.AccessibilityName==name);
        static string DeviceLabel(string id)=>id switch{"command_left"=>"关闭左指挥点","command_right"=>"关闭右指挥点","isolate_left"=>"隔离左联网端","isolate_right"=>"隔离右联网端","verify_link"=>"核验当前两端隔离","reconnect_left"=>"明确重接左联网端","latch_bypass"=>"锁定续启旁路","verify_bypass"=>"核验当前旁路","release_bypass"=>"明确释放续启旁路","set_damping"=>"设置停机阻尼","verify_damping"=>"核验当前阻尼","arm_controlled_stop"=>"批准当前配置受控停机","unset_damping"=>"明确撤下停机阻尼",_=>throw new InvalidOperationException("Unregistered RF native control.")};
        void CheckDevices(BattleSnapshot s,string name)
        {
            var f=s.Finale;if(f is null)return;string[] ids=f.Kind==FinaleMechanismKind.RookCommand?["command_left","command_right"]:f.Phase switch{FinaleCorePhase.ForcedLink=>["isolate_left","isolate_right","verify_link","reconnect_left"],FinaleCorePhase.AutomaticRestart=>["latch_bypass","verify_bypass","release_bypass"],_=>["set_damping","verify_damping","arm_controlled_stop","unset_damping"]};var option=Choice("装置、补给或现场工作点");var actual=Enumerable.Range(0,option.ItemCount).Select(option.GetItemText).ToArray();Check(name+"_exact_current_devices",ids.All(id=>actual.Contains(DeviceLabel(id)))&&new[]{"command_left","command_right","isolate_left","isolate_right","verify_link","reconnect_left","latch_bypass","verify_bypass","release_bypass","set_damping","verify_damping","arm_controlled_stop","unset_damping"}.Except(ids).All(id=>!actual.Contains(DeviceLabel(id))));
            string phase=f.Kind==FinaleMechanismKind.RookCommand?"指挥接管":f.Phase switch{FinaleCorePhase.ForcedLink=>"强制联网",FinaleCorePhase.AutomaticRestart=>"自动续启",_=>"阻尼停机"};Check(name+"_current_phase_configuration_visible",Nodes(_panels.Peek()).OfType<Label>().Any(l=>l.Text.Contains("当前阶段："+phase,StringComparison.Ordinal)&&l.Text.Contains("配置 "+f.Configuration,StringComparison.Ordinal)&&l.Text.Contains("核验 "+(f.VerifiedConfiguration?.ToString()??"尚未核验"),StringComparison.Ordinal)&&l.Text.Contains("停机批准 "+(f.StopApproval?.ToString()??"尚未批准"),StringComparison.Ordinal)));
        }
        async Task Walk(Point2 point){CloseAllPanels();var v=await _finale!.ViewAsync();var path=new NavigationGrid(v.Geometry[v.Actor.Scene]).FindPath(v.Actor.Position,point,true);if(path.Length==0)throw new InvalidOperationException("No actual safe RF native path.");await _finale.TraverseAsync(path);await Refresh();}
        async Task Spot(string id){var v=await _finale!.ViewAsync();await Walk(v.Geometry[v.Actor.Scene].Spots.Single(s=>s.Id==id).Stand);_selected=id;InteractSelected();await Settle();}
        async Task Exit(string target){var v=await _finale!.ViewAsync();var port=v.Geometry[v.Actor.Scene].Ports.Single(p=>p.TargetScene==target);await Walk(port.Stand);_selected=port.Id;InteractSelected();await Settle();if((await _finale.ViewAsync()).Actor.Scene!=target)throw new InvalidOperationException("RF native paired port failed: "+target+" / "+_status.Text);}
        async Task Goto(string target)
        {
            var v=await _finale!.ViewAsync();var seen=new Dictionary<string,string?>{{v.Actor.Scene,null}};var queue=new Queue<string>();queue.Enqueue(v.Actor.Scene);
            while(queue.TryDequeue(out var scene)){if(scene==target)break;foreach(var p in v.Geometry[scene].Ports){var scoped=v.Snapshot with{Entities=v.Snapshot.Entities.Select(e=>e.Id=="actor"?e with{Json=StrictJson.Serialize(v.Actor with{Scene=scene})}:e).ToImmutableArray()};if(_finale.PortIssue(scoped,p) is null&&seen.TryAdd(p.TargetScene,scene))queue.Enqueue(p.TargetScene);}}
            if(!seen.ContainsKey(target))throw new InvalidOperationException("No current permitted RF paired route: "+target);var steps=new List<string>();for(string? scene=target;seen[scene!] is{} from;scene=from)steps.Add(scene!);steps.Reverse();foreach(string scene in steps)await Exit(scene);
        }
        async Task At(string spot){await Goto(spot.Split('.')[0]);await Spot(spot);}
        async Task Material(string id)
        {
            var m=_finale!.Package.Materials.Single(x=>x.Id==id);await At(m.Spot);var button=Nodes(_panels.Peek()).OfType<Button>().Single(b=>b.Text.EndsWith(m.Title,StringComparison.Ordinal));button.EmitSignal(BaseButton.SignalName.Pressed);await Settle();Check("source_"+id+"_actual_readonly_original",Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&t.Text==m.English));if(m.Kind=="listening_script")Check("script_"+id+"_does_not_claim_audio",_activeAudioId is null);CloseAllPanels();
        }
        async Task Menu(string action){var station=_finale!.Package.Stations.Single(x=>x.Action==action);await At(station.Spot);await Click(station.Label);}
        async Task Do(string action){await Menu(action);CloseAllPanels();}
        async Task Meaning(string quest,int[] selected)
        {
            await At(FinaleService.SubmissionSpot(quest));CloseAllPanels();await OpenFinaleWriter(quest);await Frame();if(_finaleWriter is null)throw new InvalidOperationException("RF native writer absent.");Check("writer_"+quest+"_starts_without_author_answer",!_finaleWriter.Text.Contains("scope=",StringComparison.Ordinal));_finaleWriter.Text="Engineering native short prose for "+quest+". This controller explicitly supplies the separate meaning confirmation; no English interpretation is claimed.";_finaleWriter.EmitSignal(TextEdit.SignalName.TextChanged);await Click("保存终章正文");var task=_finale!.Package.WritingTasks.Single(t=>t.Quest==quest);Check("writer_"+quest+"_actual_CAS_ack",(await _store.GetDraftAsync(task.Id))?.Body==_finaleWriter.Text);ApplyScale(200);await Frame();Check("writer_"+quest+"_200_percent",_split.Vertical&&_finaleWriter.GetThemeFontSize("font_size")==40);ApplyScale(100);await Click("明确使用结构化辅助");var options=Nodes(_panels.Peek()).OfType<OptionButton>().ToArray();Check("meaning_"+quest+"_unknown_defaults",options.Length==selected.Length&&options.All(o=>o.Selected==0));if(options.Length!=selected.Length)throw new InvalidOperationException("RF explicit meaning row count mismatch.");for(int n=0;n<selected.Length;n++)options[n].Select(selected[n]);await Click(quest=="Q-F04"?"明确记录最终方向":"明确记录当前终章提议");CloseAllPanels();
        }
        async Task Submit(string quest,bool cancel=false)
        {
            long before=(await _store.SnapshotAsync()).Sequence;await OpenFinaleSubmission(quest);await Frame();Check("preview_"+quest+"_readonly",(await _store.SnapshotAsync()).Sequence==before);string title=quest=="Q-F04"?"最终指令执行预览":"终章提交预览 · "+quest;if(!_finaleUiPreviews.TryGetValue(title,out var stack)||stack.Count==0)throw new InvalidOperationException("RF native preview not ready: "+quest);
            if(cancel){Guid token=stack.Peek();CloseAllPanels();Check("preview_"+quest+"_closed_no_commit",(await _finale!.ConfirmPreviewAsync(token)).Status==Mist.Application.Contracts.CommitStatus.Rejected);await OpenFinaleSubmission(quest);await Frame();}await Click(quest=="Q-F04"?"明确执行当前最终指令":"明确提交终章当前结果");Check("reward_"+quest+"_once",(await _store.SnapshotAsync()).Receipts.Count(r=>r.SemanticKey==quest+":resolution")==1);CloseAllPanels();
        }
        BuildState Mixed()=>new(ProgressionRules.AllocateTemplate(28,[3,1,1,3,4]),Enumerable.Range(1,12).Select(n=>"M"+n.ToString("00")).Concat(new[]{"H01","H02","S01"}).ToImmutableArray(),["M04","M07","M10","S01"]);
        async Task Prepare()
        {
            var v=await _finale!.ViewAsync();await At(v.State.RookCustody&&v.Quests.Single(q=>q.Id=="Q-F02").Lifecycle==QuestLifecycle.Closed?"RF-S04.safe":"RF-S01.writer");CloseAllPanels();CheckCommit(await _party.ApplyBuildAsync(Mixed()));var weapon=(await _store.SnapshotAsync()).Items.Single(x=>x.Id=="collection:WP-05");if(weapon.OwnerKind!="equipped"||weapon.Slot!="weapon")CheckCommit(await _party.EquipAsync(weapon.Id,"weapon"));var armor=(await _store.SnapshotAsync()).Items.FirstOrDefault(i=>i.DefinitionId=="AR-02"&&i.OwnerKind is "player" or "equipped");if(armor is not null&&(armor.OwnerKind!="equipped"||armor.Slot!="armor"))CheckCommit(await _party.EquipAsync(armor.Id,"armor"));
        }
        async Task Battle(string id,string action)
        {
            await Prepare();await Menu(action);await Click("明确进入终章所选遭遇");Check("encounter_"+id+"_native_shared_controls",_routing.Owner==Mist.Application.UI.InputOwner.CombatDecision&&_battleCards.Count==8&&_battleCards.All(c=>c.Size.X>120));var entry=(await _finale!.EncounterAsync(id))!;CheckDevices(entry.Battle,"entry_"+id);var timer=System.Diagnostics.Stopwatch.StartNew();int commands=0;var uiControls=new HashSet<string>();
            for(int n=0;n<1500&&(await _finale!.EncounterAsync(id))!.Battle.Outcome==CombatOutcome.Active;n++)
            {
                var before=(await _finale.EncounterAsync(id))!.Battle;var request=FinaleBattleCandidate.Choose(_finale.BattleDefinitions,new(_finale.BattleDefinitions,before));
                if(request.Action=="CONTROL"&&uiControls.Add(id+":"+before.Finale!.Phase))
                {
                    CloseAllPanels();await Refresh();await OpenBattle();await Frame();CheckDevices(before,"control_"+id+"_"+before.Finale.Phase);var plan=Choice("计划行动");plan.Select(Enumerable.Range(0,plan.ItemCount).Single(i=>plan.GetItemText(i)=="现场操作"));var device=Choice("装置、补给或现场工作点");device.Select(Enumerable.Range(0,device.ItemCount).Single(i=>device.GetItemText(i)==DeviceLabel(request.Device!)));long sequence=(await _store.SnapshotAsync()).Sequence;await Click("预览此行动与费用");Check("control_"+id+"_"+before.Finale.Phase+"_native_readonly_preview",(await _store.SnapshotAsync()).Sequence==sequence);string state=StrictJson.Serialize(before);await Click("取消计划，保留当前资源");Check("control_"+id+"_"+before.Finale.Phase+"_native_cancel_preserves_resources",StrictJson.Serialize((await _finale.EncounterAsync(id))!.Battle)==state);await Click("预览此行动与费用");await Click("执行已选行动");
                }
                else{var preview=await PreviewEncounter(request);if(!preview.Action.Legal)throw new InvalidOperationException(preview.Description);CheckCommit(await ConfirmEncounter(preview.Token));}
                commands++;var after=(await _finale.EncounterAsync(id))!.Battle;
                if(before.Finale?.Phase!=after.Finale?.Phase){transitions.Add(new{request,before,after,engineering_elapsed_seconds=timer.Elapsed.TotalSeconds});Check("actual_phase_"+after.Finale!.Phase+"_same_attempt",after.Instance==before.Instance&&after.Attempt==before.Attempt&&after.Aid<=before.Aid);CloseAllPanels();await Refresh();await OpenBattle();await Frame();CheckDevices(after,"actual_phase_"+after.Finale.Phase);}
                if(n==0&&id=="ENC-RF-04")
                {string actual=StrictJson.Serialize(after);await OpenFinaleArchive();await Frame();ClosePanel();Check("core_readonly_source_panel_does_not_advance_or_refill",StrictJson.Serialize((await _finale.EncounterAsync(id))!.Battle)==actual);bool blocked=false;try{await _party.ApplyBuildAsync(Mixed());}catch(InvalidOperationException){blocked=true;}Check("core_actual_active_attempt_blocks_safe_build",blocked);}
            }
            timer.Stop();var result=(await _finale!.EncounterAsync(id))!.Battle;battles.Add(new{encounter=id,entry=entry.Entry,result,rounds=result.Round,commands,engineering_elapsed_seconds=timer.Elapsed.TotalSeconds,human_play_seconds=(double?)null,human_play="not_run"});Check("encounter_"+id+"_actual_controlled",result.Outcome==CombatOutcome.Controlled);CloseAllPanels();await Refresh();
        }
        try
        {
            if(_finale is null)throw new InvalidOperationException("RF normal native-entry actual service not loaded.");var initial=await _finale.ViewAsync();long marks=initial.Snapshot.Balances.GetValueOrDefault("marks"),parts=initial.Snapshot.Balances.GetValueOrDefault("parts");Check("real_zero_XP_history_to_unacted_RF_entry",initial.Snapshot.Balances["experience"]==16740&&initial.Actor.Scene=="RF-S01"&&initial.Actor.Position==FinalePackage.LandingStand&&initial.Materials.Length==0&&!initial.State.CheckpointRegistered&&initial.State.ActualApproach is null&&initial.Snapshot.Receipts.All(r=>!r.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal)&&r.ActionType is not("explicit_engineering_fixture" or "internal_slice_initialization")));
            foreach(string id in new[]{"RF-QF01-R-SCOPE","RF-QF01-R-ROUTES","RF-QF01-R-CHECKPOINT"})await Material(id);await Do("register_checkpoint");await Material("RF-QF01-R-AUTHORITY");await Menu("formal_handover");await Click("明确接受本次正式入场交接");CloseAllPanels();await Material("RF-QF01-L-DUTY");await Material("RF-QF01-R-CORRIDOR");await Do("release_corridor");await Goto("RF-S04");await Meaning("Q-F01",[1,1]);await Submit("Q-F01",true);
            foreach(string id in new[]{"RF-QF02-R-COMMAND","RF-QF02-L-ROOK","RF-QF02-R-AUDIT"})await Material(id);await Battle("ENC-RF-02","begin_rook");await Do("audit_command_result");Check("actual_rook_custody_does_not_claim_core_safe",!(await _store.SnapshotAsync()).Entities.Any(e=>e.Id=="final:central-safety"));await Meaning("Q-F02",[1,1,1,1]);await Submit("Q-F02");await Do("safe_preparation");CheckCommit(await _party.ApplyBuildAsync(Mixed()));await At("RF-S04.audit");CloseAllPanels();bool away=false;try{await _party.ApplyBuildAsync(Mixed());}catch(InvalidOperationException){away=true;}Check("actual_RF04_safe_build_requires_specific_stand",away);
            await Material("RF-QF03-R-RESTART");await Material("RF-QF03-R-ISOLATION");await Battle("ENC-RF-03","begin_relay");foreach(int index in new[]{1,2}){await Menu("configure_layer_isolation");Check("selector_default_unknown_"+index,Choice("本层具体对象").Selected==0&&Choice("本层隔离位置").Selected==0);Choice("本层具体对象").Select(index);Choice("本层隔离位置").Select(1);await Click("实际改变本层所选隔离器");CloseAllPanels();}await Do("verify_layer_isolation");foreach(string id in new[]{"RF-QF03-R-LINK","RF-QF03-R-RENEWAL","RF-QF03-R-DAMPING","RF-QF03-R-CLOCK","RF-QF03-L-OPERATOR"})await Material(id);await Battle("ENC-RF-04","begin_core");await Do("deliver_stability");Check("actual_stop_source_not_quest_or_policy",(await _store.SnapshotAsync()).Entities.Any(e=>e.Id=="final:central-safety")&&!(await _store.SnapshotAsync()).Entities.Any(e=>e.Id=="final:ending"));await Meaning("Q-F03",[1,1,1,1]);await Submit("Q-F03");
            foreach(string id in new[]{"RF-QF04-R-OPTIONS","RF-QF04-R-COMMAND","RF-QF04-L-DISPATCH"})await Material(id);await Do("verify_final_control");await Meaning("Q-F04",[1]);await Submit("Q-F04",true);await Material("RF-QF05-R-AFTER");await Menu("present_epilogue");var epilogue=await _finale.PresentEpilogueAsync();Check("actual_final_snapshot_six_regions_four_memberships",epilogue.EndingId=="E-CENTRAL"&&epilogue.Regions.Length==6&&epilogue.Companions.Length==4&&epilogue.NaturalMistRemains);await Click("明确记录已查看本次尾声");CloseAllPanels();var final=await _store.SnapshotAsync();Check("fixed_RF_rewards_once_and_zero_epilogue",final.Balances["experience"]==19140&&final.Balances["marks"]-marks==620&&final.Balances["parts"]-parts==4&&FinaleService.MainQuests.All(q=>final.Receipts.Count(r=>r.SemanticKey==q+":resolution")==1));Check("same_original_single_vessel",final.Entities.Count(e=>e.Kind=="shared_vessel")==1);await OpenFinaleArchive();await Frame();Check("learner_archive_preserves_actual_original",_routing.Top=="终章已得档案"&&Nodes(_panels.Peek()).OfType<Button>().Any(b=>b.Text==_finale.Package.Materials.Single(m=>m.Id=="RF-QF04-R-OPTIONS").Title));CloseAllPanels();await SaveFinaleDraft();await _store.CreateCheckpointAsync("manual");
        }
        catch(Exception error){failures++;checks.Add(new{name="finale_native_exception",passed=false,error=error.ToString()});}
        CloseAllPanels();ReleaseAudio(ref _lessonAudio);ReleaseAudio(ref _lookupAudio);await Frame();string output=System.Environment.GetEnvironmentVariable("MISTSEA_CLIENT_TEST_OUTPUT")??ProjectSettings.GlobalizePath("user://client-test");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"finale_client_results.json"),StrictJson.Serialize(new{checks,failures,transitions,battles,source="actual zero-XP normal-rf-native-entry copied SQLite/checkpoint profile; native learner reader, stations, draft CAS, unknown-default explicit meaning, preview cancellation, actual paired paths; one actual native CONTROL preview/cancel/execute per mechanism phase, other shared battle actions supplied by engineering controller",human_native_play="not_run",physical_ime="not_run",screen_reader="not_run",language_calibration="human_pending",audio_playback="not_asserted_by_script_read"}));GD.Print("MISTSEA_FINALE_CLIENT_TESTS "+checks.Count+" failures="+failures);GetTree().Quit(failures==0?0:1);
    }
}
#endif

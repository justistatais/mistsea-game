#if DEBUG
using System.Collections.Immutable;
using Godot;
using Mist.Application.ChapterSix;
using Mist.Application.Contracts;
using Mist.Core.ChapterSix;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Engineering;
using Mist.Core.Exploration;
using Mist.Core.Quests;
using Mist.DebugCandidates;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Application.WorkOrders;
using Mist.Core.Devices;
using Mist.Core.ReleaseReadiness;

public partial class Root
{
    // Requires a copied actual zero-XP -> Q504 -> actual R5/R6 voyage -> regional arrival profile.
    // This controller presses actual native station, reader, editor and confirmation controls.
    // Safe navigation and combat requests are supplied by engineering code, not by a human.
    private async Task RunChapterSixClientTests()
    {
        var checks=new List<object>();int failures=0;
        void Check(string name,bool ok){checks.Add(new{name,passed=ok});if(!ok)failures++;}
        async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        async Task Settle(){await Frame();for(int i=0;_busy&&i<3000;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);if(_busy)throw new TimeoutException("R6 native UI command did not settle.");await Frame();}
        IEnumerable<Node> Nodes(Node root){foreach(var node in root.GetChildren()){yield return node;foreach(var child in Nodes(node))yield return child;}}
        async Task Click(string caption){var buttons=Nodes(_panels.Peek()).OfType<Button>().Where(b=>b.Text==caption).ToArray();if(buttons.Length!=1)throw new InvalidOperationException("Expected single actual R6 UI button: "+caption+"; got "+buttons.Length);buttons[0].EmitSignal(BaseButton.SignalName.Pressed);await Settle();}
        OptionButton Choice(string name)=>Nodes(_panels.Peek()).OfType<OptionButton>().Single(o=>o.AccessibilityName==name);
        void Pick(string name,int index)=>Choice(name).Select(index);
        void Count(string name,int value)=>Nodes(_panels.Peek()).OfType<SpinBox>().Single(o=>o.AccessibilityName==name).Value=value;
        async Task Walk(Point2 point){CloseAllPanels();var view=await _chapterSix!.ViewAsync();var path=new NavigationGrid(view.Geometry[view.Actor.Scene]).FindPath(view.Actor.Position,point,true);if(path.Length==0)throw new InvalidOperationException("Actual current safe R6 route absent: "+view.Actor.Scene);await _chapterSix.TraverseAsync(path);await Refresh();}
        async Task Exit(string scene){var view=await _chapterSix!.ViewAsync();var port=view.Geometry[view.Actor.Scene].Ports.Single(p=>p.TargetScene==scene);await Walk(port.Stand);_selected=port.Id;InteractSelected();await Settle();if((await _chapterSix.ViewAsync()).Actor.Scene!=scene)throw new InvalidOperationException("Actual R6 native paired port failed: "+scene+"; "+_status.Text);}
        async Task Goto(string destination)
        {
            var view=await _chapterSix!.ViewAsync();var queue=new Queue<string>();queue.Enqueue(view.Actor.Scene);var previous=new Dictionary<string,string?>{{view.Actor.Scene,null}};
            while(queue.TryDequeue(out string? scene))
            {
                if(scene==destination)break;
                foreach(var port in view.Geometry[scene].Ports)
                {
                    // Read-only route planning projection; all persistent moves below use real paths/ports.
                    var scoped=view.Snapshot with{Entities=view.Snapshot.Entities.Select(e=>e.Id=="actor"?e with{Json=StrictJson.Serialize(view.Actor with{Scene=scene})}:e).ToImmutableArray()};
                    if(_chapterSix.PortIssue(scoped,port) is not null||previous.ContainsKey(port.TargetScene))continue;
                    previous.Add(port.TargetScene,scene);queue.Enqueue(port.TargetScene);
                }
            }
            if(!previous.ContainsKey(destination))throw new InvalidOperationException("No current permitted R6 route to "+destination);
            var steps=new List<string>();for(string? scene=destination;previous[scene!] is{} from;scene=from)steps.Add(scene!);steps.Reverse();foreach(string scene in steps)await Exit(scene);
        }
        async Task Spot(string id){await Goto(id.Split('.')[0]);var view=await _chapterSix!.ViewAsync();await Walk(view.Geometry[view.Actor.Scene].Spots.Single(s=>s.Id==id).Stand);_selected=id;InteractSelected();await Settle();}
        async Task Material(string id)
        {
            var material=_chapterSix!.Package.Materials.Single(m=>m.Id==id);await Spot(material.Spot);var buttons=Nodes(_panels.Peek()).OfType<Button>().Where(b=>b.Text.EndsWith(material.Title,StringComparison.Ordinal)).ToArray();if(buttons.Length!=1)throw new InvalidOperationException("Actual R6 material button absent: "+id);buttons[0].EmitSignal(BaseButton.SignalName.Pressed);await Settle();
            if(!Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&t.Text==material.English))throw new InvalidOperationException("Actual learner original not displayed: "+id);
            if(material.Kind=="listening_script")Check("script_"+id+"_read_is_not_playback",_activeAudioId is null&&_lessonAudio is null);CloseAllPanels();
        }
        async Task Act(string action){var station=_chapterSix!.Package.Stations.Single(s=>s.Action==action);await Spot(station.Spot);await Click(station.Label);CloseAllPanels();}
        async Task Form(string action,string caption,Action fill){var station=_chapterSix!.Package.Stations.Single(s=>s.Action==action);await Spot(station.Spot);await Click(station.Label);fill();await Click(caption);CloseAllPanels();}
        async Task Meaning(string quest,int[] indices)
        {
            var task=_chapterSix!.Package.WritingTasks.Single(t=>t.Quest==quest);await Spot(task.Spot);CloseAllPanels();await OpenChapterSixWriter(quest);await Frame();if(_chapterSixWriter is null)throw new InvalidOperationException("Actual R6 writer absent.");
            _chapterSixWriter.Text="Engineering native UI candidate prose for "+quest+". This controller supplies the explicit interpretation; this is not automatic English understanding or human language calibration.";_chapterSixWriter.EmitSignal(TextEdit.SignalName.TextChanged);await Click("保存第六章正文");Check("draft_"+quest+"_actual_cas_ack",(await _store.GetDraftAsync(task.Id))?.Body==_chapterSixWriter.Text);
            ApplyScale(200);await Frame();Check("writer_"+quest+"_200_percent",_split.Vertical&&_chapterSixWriter.GetThemeFontSize("font_size")==40);ApplyScale(100);
            await Click("明确使用结构化辅助");var choices=Nodes(_panels.Peek()).OfType<OptionButton>().ToArray();Check("meaning_"+quest+"_has_no_default_answer",choices.Length==indices.Length&&choices.All(c=>c.Selected==0));if(choices.Length!=indices.Length)throw new InvalidOperationException("R6 explicit meaning row count mismatch.");for(int i=0;i<choices.Length;i++)choices[i].Select(indices[i]);await Click("明确记录当前第六章提议");CloseAllPanels();
        }
        async Task Submit(string quest,bool checkCancellation=false)
        {
            await Spot(_chapterSix!.SubmissionSpot(quest));CloseAllPanels();long before=(await _store.SnapshotAsync()).Sequence;await OpenChapterSixSubmission(quest);await Frame();Check("preview_"+quest+"_readonly",(await _store.SnapshotAsync()).Sequence==before);string title="第六章提交预览 · "+quest;
            if(!_chapterSixUiPreviews.TryGetValue(title,out var stack)||stack.Count==0)throw new InvalidOperationException("Actual R6 preview not ready: "+quest+"; "+string.Join(" | ",Nodes(_panels.Peek()).OfType<Label>().Select(l=>l.Text)));
            if(checkCancellation)
            {
                var token=stack.Peek();CloseAllPanels();Check("closed_"+quest+"_preview_cancelled",(await _chapterSix.ConfirmPreviewAsync(token)).Status==CommitStatus.Rejected);
                await OpenChapterSixSubmission(quest);await Frame();token=_chapterSixUiPreviews[title].Peek();await OpenChapterSixWriter(quest);await Frame();_chapterSixWriter!.Text+=" Actual save-only edit invalidates the old proposal.";_chapterSixWriter.EmitSignal(TextEdit.SignalName.TextChanged);await Click("保存第六章正文");
                Check("edited_draft_cancels_old_native_preview",(await _chapterSix.ConfirmPreviewAsync(token)).Status==CommitStatus.Rejected);Check("saved_prose_does_not_close_world",(await _chapterSix.ViewAsync()).Quests.Single(q=>q.Id==quest).Lifecycle==QuestLifecycle.Active&&(await _store.SnapshotAsync()).Balances["experience"]==10500);CloseAllPanels();
                await Meaning(quest,[1,1,1,1,1]);await Spot(_chapterSix.SubmissionSpot(quest));CloseAllPanels();await OpenChapterSixSubmission(quest);await Frame();
            }
            await Click("明确提交第六章当前结果");Check("submission_"+quest+"_single_actual_receipt",(await _store.SnapshotAsync()).Receipts.Count(r=>r.SemanticKey==quest+":resolution")==1);
        }
        async Task Battle(string id,string stationAction)
        {
            var station=_chapterSix!.Package.Stations.Single(s=>s.Action==stationAction);await Spot(station.Spot);await Click(station.Label);await Click("明确进入边境所选遭遇");Check("encounter_"+id+"_native_shared_controls",_routing.Owner==Mist.Application.UI.InputOwner.CombatDecision&&_battleCards.Count==8&&_battleCards.All(c=>c.Size.X>120));
            for(int i=0;i<800&&(await _chapterSix.EncounterAsync(id))!.Battle.Outcome==CombatOutcome.Active;i++)
            {
                var battle=(await _chapterSix.EncounterAsync(id))!.Battle;var request=BattleCandidate.Choose(new(_chapterSix.BattleDefinitions,battle));var preview=await PreviewEncounter(request);if(!preview.Action.Legal)throw new InvalidOperationException(preview.Description);CheckCommit(await ConfirmEncounter(preview.Token));
            }
            Check("encounter_"+id+"_actual_control",(await _chapterSix.EncounterAsync(id))!.Battle.Outcome==CombatOutcome.Controlled);CloseAllPanels();await Refresh();
        }
        async Task ActualRegionalAudio()
        {
            const string id="R6-Q604-L-HANDOVER";await Material(id);await OpenChapterSixMaterial(id);await Frame();var before=await _store.SnapshotAsync();await Click("听力音轨与脚本");
            Check("regional_audio_actual_decode_and_no_autoplay",_activeAudioId==id&&_lessonAudio?.Stream is not null&&_lessonAudio.Stream.GetLength()>1&&!_lessonAudio.Playing);
            await Click("播放 / 暂停候选音轨");await ToSignal(GetTree().CreateTimer(.4),SceneTreeTimer.SignalName.Timeout);Check("regional_audio_actual_playback_position",_lessonAudio is{Playing:true}&&_lessonAudio.GetPlaybackPosition()>0);
            await Click("播放 / 暂停候选音轨");Check("regional_audio_pause_preserves_position",_lessonAudio is{StreamPaused:true}&&_lessonAudio.GetPlaybackPosition()>0);var speed=Choice("候选音轨播放速度");speed.Select(2);speed.EmitSignal(OptionButton.SignalName.ItemSelected,2L);await Settle();Check("regional_audio_speed_changes_player",_lessonAudio is not null&&Math.Abs(_lessonAudio.PitchScale-1.25f)<.001);
            await Click("查看原文脚本 / 字幕");Check("regional_audio_retains_full_original_script",Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&t.Text==_chapterSix!.Package.Materials.Single(m=>m.Id==id).English));ClosePanel();var notes=Nodes(_panels.Peek()).OfType<TextEdit>().Single(t=>t.Editable);notes.Text="Engineering playback observation; human audition, speaker review and language scoring remain pending.";await Click("保存当前听力证据笔记");var after=await _store.SnapshotAsync();Check("regional_audio_notes_do_not_reward_world",before.Sequence==after.Sequence&&before.Balances.OrderBy(p=>p.Key).SequenceEqual(after.Balances.OrderBy(p=>p.Key)));CloseAllPanels();await Frame();Check("regional_audio_close_releases_player",_lessonAudio is null&&_activeAudioId is null);
        }
        async Task ReadQuest(string quest){foreach(var material in _chapterSix!.Package.Materials.Where(m=>m.Quest==quest&&m.Kind!="listening_script"))await Material(material.Id);}
        async Task RegionWalk(Point2 to)
        {
            CloseAllPanels();await Refresh();var a=_view.Actor;bool avoid=a.Scene[..2]!="R1";var path=new NavigationGrid(ActiveGeometry[a.Scene]).FindPath(a.Position,to,avoid);if(path.Length==0)throw new InvalidOperationException("Actual source region path missing: "+a.Scene);
            if(a.Scene.StartsWith("HUB-",StringComparison.Ordinal))CheckCommit(await _vessel!.MoveAsync(path));
            else if(a.Scene.StartsWith("R6-",StringComparison.Ordinal))await _chapterSix!.TraverseAsync(path);
            else if(a.Scene.StartsWith("R5-",StringComparison.Ordinal))await _chapterFive!.TraverseAsync(path);
            else if(a.Scene.StartsWith("R4-",StringComparison.Ordinal))await _chapterFour!.TraverseAsync(path);
            else if(a.Scene.StartsWith("R3-",StringComparison.Ordinal))await _chapterThree!.TraverseAsync(path);
            else if(a.Scene.StartsWith("R2-",StringComparison.Ordinal))await _chapterTwo!.TraverseAsync(path);
            else await _chapter!.MoveAsync(path);await Refresh();
        }
        string? RegionGate(BranchSnapshot snapshot,Port port,string region)=>region switch{"R1"=>_chapter!.PortIssue(snapshot,port),"R2"=>_chapterTwo!.PortIssue(snapshot,port),"R3"=>_chapterThree!.PortIssue(snapshot,port),"R4"=>_chapterFour!.PortIssue(snapshot,port),"R5"=>_chapterFive!.PortIssue(snapshot,port),"R6"=>_chapterSix!.PortIssue(snapshot,port),_=>null};
        async Task RegionGoto(string destination)
        {
            await Refresh();var a=_view.Actor;string region=a.Scene[..2];var queue=new Queue<string>();queue.Enqueue(a.Scene);var prev=new Dictionary<string,string?>{{a.Scene,null}};var snapshot=await _store.SnapshotAsync();
            while(queue.TryDequeue(out string? current)){if(current==destination)break;foreach(var port in ActiveGeometry[current].Ports){if(!ActiveGeometry.ContainsKey(port.TargetScene))continue;var scoped=snapshot with{Entities=snapshot.Entities.Select(e=>e.Id=="actor"?e with{Json=StrictJson.Serialize(a with{Scene=current})}:e).ToImmutableArray()};if(RegionGate(scoped,port,region) is not null||!prev.TryAdd(port.TargetScene,current))continue;queue.Enqueue(port.TargetScene);}}
            if(!prev.ContainsKey(destination))throw new InvalidOperationException("No actual permitted regional path to "+destination);var route=new List<string>();for(string? c=destination;prev[c!] is{} from;c=from)route.Add(c!);route.Reverse();foreach(string next in route){var port=ActiveGeometry[_view.Actor.Scene].Ports.Single(p=>p.TargetScene==next);await RegionWalk(port.Stand);_selected=port.Id;InteractSelected();await Settle();if(_view.Actor.Scene!=next)throw new InvalidOperationException("Actual native regional port rejected: "+next+" / "+_status.Text);}
        }
        async Task RegionSpot(string spot){await RegionGoto(spot.Split('.')[0]);await RegionWalk(ActiveGeometry[_view.Actor.Scene].Spots.Single(s=>s.Id==spot).Stand);_selected=spot;InteractSelected();await Settle();}
        async Task Facility(string region)
        {
            var f=_chapterSix!.Package.Facilities.Single(x=>x.Region==region);await RegionSpot(f.PhysicalSpot);await Click(region=="R6"?"第六塔实际工程工作台":"打开当前塔站实际工程工作台");
        }
        async Task Base(string region)
        {
            var f=_chapterSix!.Package.Facilities.Single(x=>x.Region==region);if(region=="R4"){await Facility(region);await Click("到场重新核验当前学城棱镜与C试验安全");CloseAllPanels();}
            await Facility(region);await Click("核对当前地方合同与基础四件实物");Pick("地方工程合同版本",1);Pick("当前安装班组",1);foreach(var check in Nodes(_panels.Peek()).OfType<CheckBox>())check.ButtonPressed=true;await Click("明确核验这份地方合同与实际四件序号");CloseAllPanels();
            foreach(string caption in new[]{"登记已到场核验的安装班组与两个值守岗","登记已核验的当前塔站安全来源"}){await Facility(region);await Click(caption);CloseAllPanels();}
            foreach(var source in _chapterSix.Engineering.Rules.Sources.Values.Where(x=>x.Id.StartsWith("BASE-"+f.Tower+"-",StringComparison.Ordinal)))for(int step=1;step<=2;step++){await Facility(region);await Click("核对下一规定实物步骤 · "+source.Id);foreach(var check in Nodes(_panels.Peek()).OfType<CheckBox>())check.ButtonPressed=true;await Click("实际核验第"+step+"步 · "+(step==1?"match_contract_serials":"check_and_deliver_components"));CloseAllPanels();}
            await Facility(region);await Click("预留当前地方工作单");Pick("本次工作类型",1);Pick("本次工作配置",1);await Click("明确预留所选实际工作单");CloseAllPanels();for(int step=1;step<=4;step++){await Facility(region);await Click("实际执行 BaselineCheck 第"+step+"步");CloseAllPanels();}
            var ready=RegionalSnapshotRead<TowerReadiness>(await _store.SnapshotAsync(),"readiness:"+f.Tower);Check("actual_native_onsite_baseline_"+region,ready is{Safe:true,Baseline:true,Operators:true}&&ready.EnhancedVersion is null);
        }
        async Task BoardChart()
        {
            var snap=await _store.SnapshotAsync();var ship=RegionalSnapshotRead<SharedVessel>(snap,"ship:glimmer")!;var berth=RegionalSnapshotRead<VesselBerth>(snap,"berth:"+ship.DockRegion)!;await RegionGoto(berth.Scene);await RegionWalk(berth.Boarding);_selected=ship.DockRegion=="R1"?"R1-S01.glimmer_handover":berth.Scene+".berth";InteractSelected();await Settle();if(ship.DockRegion=="R1")await Click("核对当前登记并预览登船");await Click("明确登上微光号");await RegionGoto("HUB-S02");await RegionSpot("HUB-S02.chart");
        }
        async Task Sail(string region)
        {
            await BoardChart();await Click("预览返回已登记地区 "+region);await Click("明确按已登记航路返回 "+region);Check("actual_native_sail_same_vessel_"+region,_view.Actor.Scene==region+"-S01"&&RegionalSnapshotRead<SharedVessel>(await _store.SnapshotAsync(),"ship:glimmer")!.DockRegion==region);CloseAllPanels();
        }
        try
        {
            if(_chapterSix is null||_finaleLaunch is null)throw new InvalidOperationException("Native test requires copied untouched chapter-six-normal-arrival SQLite profile and formal R6/launch services.");var start=await _store.SnapshotAsync();long marks=start.Balances.GetValueOrDefault("marks"),parts=start.Balances.GetValueOrDefault("parts");var originalShip=RegionalSnapshotRead<SharedVessel>(start,"ship:glimmer")!;
            Check("actual_10500_XP_untouched_zero_XP_predecessor_chain",start.Balances.GetValueOrDefault("experience")==10500&&start.Receipts.Any(r=>r.ActionType=="actual_vessel_arrival")&&start.Receipts.Any(r=>r.SemanticKey=="Q-504:resolution"&&r.ActionType=="old_city_joint_handover_received")&&start.Receipts.All(r=>!r.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal)&&!r.SemanticKey.StartsWith("internal:",StringComparison.Ordinal)&&r.ActionType is not("explicit_engineering_fixture" or "internal_slice_initialization")));Check("unvisited_R6_sources_and_engineering_not_granted",(await _chapterSix.ViewAsync()).Materials.Length==0&&start.Balances.Where(p=>p.Key.StartsWith("engineering:",StringComparison.Ordinal)).All(p=>p.Value==0));
            await Material("R6-Q601-R-LANDING");await Act("register_landing");await Material("R6-Q601-R-BRIEFING");await Act("request_survey");await Act("inspect_basic_gear");await Material("R6-Q601-R-FORECAST");await Form("choose_corridor","明确选择当前调查路线",()=>Pick("本次调查路线",1));await Act("secure_guide_markers");await Battle("ENC-R6-01","begin_pass_encounter");await Material("R6-Q601-R-INSTRUMENT");await Material("R6-Q601-R-RISK");await Battle("ENC-R6-02","begin_station_encounter");await Act("isolate_survey_instrument");await Form("configure_survey","实际设置所核对的测站配置",()=>{Pick("零位通道",1);Pick("参考通道",1);Count("实际零位读数",0);Count("实际参考读数",10);Pick("测量范围",1);Pick("观察时间边界",1);});await Act("test_survey");await Material("R6-Q601-R-RETURN");await Act("inspect_inner_return");await Meaning("Q-601",[1,1,1,1,1]);await Submit("Q-601",true);Check("actual_survey_opens_Q602_Q603_parallel_without_calibration",(await _chapterSix.ViewAsync()).Quests.Where(q=>q.Id is "Q-602" or "Q-603").All(q=>q.Lifecycle==QuestLifecycle.Active)&&(await _party.InspectAsync()).Grade==3);
            await Material("R6-Q603-R-MANIFEST");await Act("inspect_cargo_manifest");await Material("R6-Q603-R-SEGMENTS");await Form("plan_cargo_segment","明确保留当前真实货组与护送段计划",()=>{foreach(var check in Nodes(_panels.Peek()).OfType<CheckBox>())check.ButtonPressed=true;Pick("本次接收目的地",1);Pick("备用交接方案",1);});await Battle("ENC-R6-03","begin_first_escort");await Act("start_outer_segment");Check("cargo_start_is_actual_unfinished_transport",RegionalSnapshotRead<TransportSegment>(await _store.SnapshotAsync(),ChapterSixService.CargoSegment) is{CargoInTransit:true,Completed:false});await Exit("R6-S06");await Battle("ENC-R6-04","begin_outer_escort");await Act("continue_ridge_segment");await Exit("R6-S07");await Material("R6-Q603-R-PLATFORM");foreach(string caption in new[]{"实际断开护场器输入","实际固定外侧制动","实际核验当前零场能"})await Form("control_field_device",caption,()=>{});await Act("protect_current_cargo");await Battle("ENC-R6-05","begin_platform_encounter");await Act("isolate_platform");await Material("R6-Q603-R-LOCAL");await Act("receive_base_packet");await Act("inspect_ridge_return");await Act("test_ridge_tower");await Base("R6");await ReadQuest("Q-603");await Meaning("Q-603",[1,1,1,1,1]);await Submit("Q-603");Check("actual_local_Q603_closes_before_distant_Q602",(await _chapterSix.ViewAsync()).Quests.Single(q=>q.Id=="Q-602").Lifecycle==QuestLifecycle.Active&&(await _store.SnapshotAsync()).Balances["experience"]==13700);
            foreach(string region in new[]{"R5","R4","R3","R2","R1"}){await Sail(region);await Base(region);}await Sail("R6");await ReadQuest("Q-602");await Meaning("Q-602",[1,1,1,1]);await Submit("Q-602");Check("six_real_onsite_baselines_open_Q604",(await _chapterSix.ViewAsync()).Quests.Single(q=>q.Id=="Q-604").Lifecycle==QuestLifecycle.Active&&(await _store.SnapshotAsync()).Balances["experience"]==15300&&(await _party.InspectAsync()).Grade==3);
            await ReadQuest("Q-604");await Act("inspect_departure_qualification");await Spot("R6-S08.qualification");CloseAllPanels();await OpenChapterSixQualification();Pick("明确准备方向",1);await Click("明确记录本次准备方向");CloseAllPanels();await Meaning("Q-604",[1,1,1]);await ActualRegionalAudio();await Act("verify_current_risk_register");await Spot("R6-S08.qualification");CloseAllPanels();OpenChapterSixSeal();await Click("明确保存保护档并封存当前准备");CloseAllPanels();var frozen=RegionalSnapshotRead<FrozenPreparation>(await _store.SnapshotAsync(),"final:preparation")!;Check("formal_actual_protected_checkpoint_is_persistent",await _store.FindAsync(frozen.PreFinalCheckpoint) is{} checkpoint&&checkpoint.Profile==_store.Session.Profile&&checkpoint.Slot=="protected:pre-final:"+frozen.Id);Check("base_only_enhanced_missing_remains_public",!EndingRules.Evaluate(frozen.Preparation,false,false).Options.Single(o=>o.Choice==EndingChoice.Distributed).Prepared&&frozen.Preparation.Towers.All(t=>t.Safe&&t.Baseline&&t.Operators));
            await BoardChart();CloseAllPanels();long beforePreview=(await _store.SnapshotAsync()).Sequence;await OpenChapterSixLaunchPreview();await Frame();Check("final_launch_preview_readonly",(await _store.SnapshotAsync()).Sequence==beforePreview);const string launchTitle="最后起航实际预览";if(!_chapterSixLaunchPreviews.TryGetValue(launchTitle,out var launchStack)||launchStack.Count==0)throw new InvalidOperationException("Actual launch preview not ready: "+string.Join(" | ",Nodes(_panels.Peek()).OfType<Label>().Select(l=>l.Text)));var cancelled=launchStack.Peek();CloseAllPanels();Check("closing_final_launch_revokes_token",(await _finaleLaunch.ConfirmAsync(cancelled)).Status==CommitStatus.Rejected);await OpenChapterSixLaunchPreview();await Click("明确最后起航并在最终区实际登陆");var final=await _store.SnapshotAsync();var actual=RegionalSnapshotRead<FinaleDeparture>(final,"final:actual-departure")!;Check("Q604_actual_atomic_same_vessel_RF_landing",actual.VesselInstance==originalShip.Instance&&actual.PreparationId==frozen.Id&&actual.Destination=="RF"&&_view.Actor.Scene=="RF-S01"&&_view.Actor.Position==actual.Landing&&RegionalSnapshotRead<SharedVessel>(final,"ship:glimmer") is{DockRegion:"RF"} ship&&ship.Instance==originalShip.Instance);Check("main_rewards_and_fourth_calibration_once",final.Balances["experience"]==16740&&final.Balances["marks"]-marks==1000&&final.Balances["parts"]-parts==12&&final.Items.Count(i=>i.DefinitionId=="TL-04")==1&&ChapterSixService.MainQuests.All(q=>final.Receipts.Count(r=>r.SemanticKey==q+":resolution")==1)&&(await _party.InspectAsync()).Grade==4);Check("no_positive_fixture_or_private_Tess_requirement",final.Receipts.All(r=>!r.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal))&&!final.Entities.Any(e=>e.Id=="roster:CP-TESS"&&e.Json.Contains("joined",StringComparison.OrdinalIgnoreCase))&&!final.Entities.Any(e=>e.Id=="final:ending"));await SaveChapterSixDraft();await _store.CreateCheckpointAsync("manual");
        }
        catch(Exception error){failures++;checks.Add(new{name="chapter_six_client_exception",passed=false,error=error.ToString()});}
        CloseAllPanels();ReleaseAudio(ref _lessonAudio);ReleaseAudio(ref _lookupAudio);await Frame();string output=System.Environment.GetEnvironmentVariable("MISTSEA_CLIENT_TEST_OUTPUT")??ProjectSettings.GlobalizePath("user://client-test");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"chapter_six_client_results.json"),StrictJson.Serialize(new{checks,failures,source="copied untouched actual 0XP through Q504 and typed R5/R6 arrival SQLite profile; native material/station/editor/meaning/submission/onsite procurement/work/boarding/sailing/seal/launch controls; safe navigation and combat requests supplied by engineering controller",human_native_play="not_run",physical_ime="not_run",screen_reader="not_run",language_calibration="human_pending",audio="actual source-bound candidate decode/playback only; human audition pending"}));GD.Print("MISTSEA_CHAPTER_SIX_CLIENT_TESTS "+checks.Count+" failures="+failures);GetTree().Quit(failures==0?0:1);
    }
}
#endif

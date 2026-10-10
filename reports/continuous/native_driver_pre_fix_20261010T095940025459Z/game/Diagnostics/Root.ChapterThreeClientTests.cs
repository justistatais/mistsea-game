using System.Collections.Immutable;
using Godot;
using Mist.Application.ChapterThree;
using Mist.Application.Contracts;
using Mist.Core.ChapterThree;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Engineering;
using Mist.Core.Exploration;
using Mist.Core.Quests;
using Mist.DebugCandidates;

public partial class Root
{
    // Requires a copied actual zero-XP -> Q204 -> R2/R3 voyage -> regional arrival profile.
    // This controller presses actual native station, reader, editor and confirmation controls.
    // Safe navigation and combat requests are supplied by engineering code, not by a human.
    private async Task RunChapterThreeClientTests()
    {
        var checks=new List<object>();int failures=0;
        void Check(string name,bool ok){checks.Add(new{name,passed=ok});if(!ok)failures++;}
        async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        async Task Settle(){await Frame();for(int i=0;_busy&&i<3000;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);if(_busy)throw new TimeoutException("R3 native UI command did not settle.");await Frame();}
        IEnumerable<Node> Nodes(Node root){foreach(var node in root.GetChildren()){yield return node;foreach(var child in Nodes(node))yield return child;}}
        async Task Click(string caption){var buttons=Nodes(_panels.Peek()).OfType<Button>().Where(b=>b.Text==caption).ToArray();if(buttons.Length!=1)throw new InvalidOperationException("Expected single actual R3 UI button: "+caption+"; got "+buttons.Length);buttons[0].EmitSignal(BaseButton.SignalName.Pressed);await Settle();}
        OptionButton Choice(string name)=>Nodes(_panels.Peek()).OfType<OptionButton>().Single(o=>o.AccessibilityName==name);
        void Pick(string name,int index)=>Choice(name).Select(index);
        void Fields(params string[] captions){foreach(var checkbox in Nodes(_panels.Peek()).OfType<CheckBox>())checkbox.ButtonPressed=captions.Contains(checkbox.Text);}
        void Count(string name,int value)=>Nodes(_panels.Peek()).OfType<SpinBox>().Single(o=>o.AccessibilityName==name).Value=value;
        async Task Walk(Point2 point){CloseAllPanels();var view=await _chapterThree!.ViewAsync();var path=new NavigationGrid(view.Geometry[view.Actor.Scene]).FindPath(view.Actor.Position,point,true);if(path.Length==0)throw new InvalidOperationException("Actual current safe R3 route absent: "+view.Actor.Scene);await _chapterThree.TraverseAsync(path);await Refresh();}
        async Task Exit(string scene){var view=await _chapterThree!.ViewAsync();var port=view.Geometry[view.Actor.Scene].Ports.Single(p=>p.TargetScene==scene);await Walk(port.Stand);_selected=port.Id;InteractSelected();await Settle();if((await _chapterThree.ViewAsync()).Actor.Scene!=scene)throw new InvalidOperationException("Actual R3 native paired port failed: "+scene+"; "+_status.Text);}
        async Task Goto(string destination)
        {
            var view=await _chapterThree!.ViewAsync();var queue=new Queue<string>();queue.Enqueue(view.Actor.Scene);var previous=new Dictionary<string,string?>{{view.Actor.Scene,null}};
            while(queue.TryDequeue(out string? scene))
            {
                if(scene==destination)break;
                foreach(var port in view.Geometry[scene].Ports)
                {
                    // Read-only route planning projection; all persistent moves below use real paths/ports.
                    var scoped=view.Snapshot with{Entities=view.Snapshot.Entities.Select(e=>e.Id=="actor"?e with{Json=StrictJson.Serialize(view.Actor with{Scene=scene})}:e).ToImmutableArray()};
                    if(_chapterThree.PortIssue(scoped,port) is not null||previous.ContainsKey(port.TargetScene))continue;
                    previous.Add(port.TargetScene,scene);queue.Enqueue(port.TargetScene);
                }
            }
            if(!previous.ContainsKey(destination))throw new InvalidOperationException("No current permitted R3 route to "+destination);
            var steps=new List<string>();for(string? scene=destination;previous[scene!] is{} from;scene=from)steps.Add(scene!);steps.Reverse();foreach(string scene in steps)await Exit(scene);
        }
        async Task Spot(string id){await Goto(id.Split('.')[0]);var view=await _chapterThree!.ViewAsync();await Walk(view.Geometry[view.Actor.Scene].Spots.Single(s=>s.Id==id).Stand);_selected=id;InteractSelected();await Settle();}
        async Task Material(string id)
        {
            var material=_chapterThree!.Package.Materials.Single(m=>m.Id==id);await Spot(material.Spot);var buttons=Nodes(_panels.Peek()).OfType<Button>().Where(b=>b.Text.EndsWith(material.Title,StringComparison.Ordinal)).ToArray();if(buttons.Length!=1)throw new InvalidOperationException("Actual R3 material button absent: "+id);buttons[0].EmitSignal(BaseButton.SignalName.Pressed);await Settle();
            if(!Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&t.Text==material.English))throw new InvalidOperationException("Actual learner original not displayed: "+id);
            if(material.Kind=="listening_script")Check("script_"+id+"_read_is_not_playback",_activeAudioId is null&&_lessonAudio is null);CloseAllPanels();
        }
        async Task Act(string action){var station=_chapterThree!.Package.Stations.Single(s=>s.Action==action);await Spot(station.Spot);await Click(station.Label);CloseAllPanels();}
        async Task Form(string action,string caption,Action fill){var station=_chapterThree!.Package.Stations.Single(s=>s.Action==action);await Spot(station.Spot);await Click(station.Label);fill();await Click(caption);CloseAllPanels();}
        async Task Meaning(string quest,int[] indices)
        {
            var task=_chapterThree!.Package.WritingTasks.Single(t=>t.Quest==quest);await Spot(task.Spot);CloseAllPanels();await OpenChapterThreeWriter(quest);await Frame();if(_chapterThreeWriter is null)throw new InvalidOperationException("Actual R3 writer absent.");
            _chapterThreeWriter.Text="Engineering native UI candidate prose for "+quest+". This controller supplies the explicit interpretation; this is not automatic English understanding or human language calibration.";_chapterThreeWriter.EmitSignal(TextEdit.SignalName.TextChanged);await Click("保存第三章正文");Check("draft_"+quest+"_actual_cas_ack",(await _store.GetDraftAsync(task.Id))?.Body==_chapterThreeWriter.Text);
            ApplyScale(200);await Frame();Check("writer_"+quest+"_200_percent",_split.Vertical&&_chapterThreeWriter.GetThemeFontSize("font_size")==40);ApplyScale(100);
            await Click("明确使用结构化辅助");var choices=Nodes(_panels.Peek()).OfType<OptionButton>().ToArray();Check("meaning_"+quest+"_has_no_default_answer",choices.Length==indices.Length&&choices.All(c=>c.Selected==0));if(choices.Length!=indices.Length)throw new InvalidOperationException("R3 explicit meaning row count mismatch.");for(int i=0;i<choices.Length;i++)choices[i].Select(indices[i]);await Click("明确记录当前第三章提议");CloseAllPanels();
        }
        async Task Submit(string quest,bool checkCancellation=false)
        {
            await Spot(_chapterThree!.SubmissionSpot(quest));CloseAllPanels();long before=(await _store.SnapshotAsync()).Sequence;await OpenChapterThreeSubmission(quest);await Frame();Check("preview_"+quest+"_readonly",(await _store.SnapshotAsync()).Sequence==before);string title="第三章提交预览 · "+quest;
            if(!_chapterThreeUiPreviews.TryGetValue(title,out var stack)||stack.Count==0)throw new InvalidOperationException("Actual R3 preview not ready: "+quest+"; "+string.Join(" | ",Nodes(_panels.Peek()).OfType<Label>().Select(l=>l.Text)));
            if(checkCancellation)
            {
                var token=stack.Peek();CloseAllPanels();Check("closed_"+quest+"_preview_cancelled",(await _chapterThree.ConfirmPreviewAsync(token)).Status==CommitStatus.Rejected);
                await OpenChapterThreeSubmission(quest);await Frame();token=_chapterThreeUiPreviews[title].Peek();await OpenChapterThreeWriter(quest);await Frame();_chapterThreeWriter!.Text+=" Actual save-only edit invalidates the old proposal.";_chapterThreeWriter.EmitSignal(TextEdit.SignalName.TextChanged);await Click("保存第三章正文");
                Check("edited_draft_cancels_old_native_preview",(await _chapterThree.ConfirmPreviewAsync(token)).Status==CommitStatus.Rejected);Check("saved_prose_does_not_close_world",(await _chapterThree.ViewAsync()).Quests.Single(q=>q.Id==quest).Lifecycle==QuestLifecycle.Active&&(await _store.SnapshotAsync()).Balances["experience"]==1920);CloseAllPanels();
                await Meaning(quest,[1,1,1,1,1]);await Spot(_chapterThree.SubmissionSpot(quest));CloseAllPanels();await OpenChapterThreeSubmission(quest);await Frame();
            }
            await Click("明确提交第三章当前结果");Check("submission_"+quest+"_single_actual_receipt",(await _store.SnapshotAsync()).Receipts.Count(r=>r.SemanticKey==quest+":resolution")==1);
        }
        async Task Battle(string id,string stationAction)
        {
            var station=_chapterThree!.Package.Stations.Single(s=>s.Action==stationAction);await Spot(station.Spot);await Click(station.Label);await Click("明确进入矿区所选遭遇");Check("encounter_"+id+"_native_shared_controls",_routing.Owner==Mist.Application.UI.InputOwner.CombatDecision&&_battleCards.Count==8&&_battleCards.All(c=>c.Size.X>120));
            for(int i=0;i<800&&(await _chapterThree.EncounterAsync(id))!.Battle.Outcome==CombatOutcome.Active;i++)
            {
                var battle=(await _chapterThree.EncounterAsync(id))!.Battle;var request=BattleCandidate.Choose(new(_chapterThree.BattleDefinitions,battle));var preview=await PreviewEncounter(request);if(!preview.Action.Legal)throw new InvalidOperationException(preview.Description);CheckCommit(await ConfirmEncounter(preview.Token));
            }
            Check("encounter_"+id+"_actual_control",(await _chapterThree.EncounterAsync(id))!.Battle.Outcome==CombatOutcome.Controlled);CloseAllPanels();await Refresh();
        }
        async Task ActualRegionalAudio()
        {
            const string id="R3-Q304-L-TRANSFER";await Material(id);await OpenChapterThreeMaterial(id);await Frame();var before=await _store.SnapshotAsync();await Click("听力音轨与脚本");
            Check("regional_audio_actual_decode_and_no_autoplay",_activeAudioId==id&&_lessonAudio?.Stream is not null&&_lessonAudio.Stream.GetLength()>1&&!_lessonAudio.Playing);
            await Click("播放 / 暂停候选音轨");await ToSignal(GetTree().CreateTimer(.4),SceneTreeTimer.SignalName.Timeout);Check("regional_audio_actual_playback_position",_lessonAudio is{Playing:true}&&_lessonAudio.GetPlaybackPosition()>0);
            await Click("播放 / 暂停候选音轨");Check("regional_audio_pause_preserves_position",_lessonAudio is{StreamPaused:true}&&_lessonAudio.GetPlaybackPosition()>0);var speed=Choice("候选音轨播放速度");speed.Select(2);speed.EmitSignal(OptionButton.SignalName.ItemSelected,2L);Check("regional_audio_speed_changes_player",_lessonAudio is not null&&Math.Abs(_lessonAudio.PitchScale-1.25f)<.001);
            await Click("查看原文脚本 / 字幕");Check("regional_audio_retains_full_original_script",Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&t.Text==_chapterThree!.Package.Materials.Single(m=>m.Id==id).English));ClosePanel();var notes=Nodes(_panels.Peek()).OfType<TextEdit>().Single(t=>t.Editable);notes.Text="Engineering playback observation; human audition, speaker review and language scoring remain pending.";await Click("保存当前听力证据笔记");var after=await _store.SnapshotAsync();Check("regional_audio_notes_do_not_reward_world",before.Sequence==after.Sequence&&before.Balances.OrderBy(p=>p.Key).SequenceEqual(after.Balances.OrderBy(p=>p.Key)));CloseAllPanels();await Frame();Check("regional_audio_close_releases_player",_lessonAudio is null&&_activeAudioId is null);
        }
        try
        {
            if(_chapterThree is null)throw new InvalidOperationException("Native test requires actual copied normal-r3-native-entry SQLite profile and initialized R3 service.");var start=await _store.SnapshotAsync();long marks=start.Balances.GetValueOrDefault("marks"),parts=start.Balances.GetValueOrDefault("parts");
            Check("normal_zero_xp_source_has_no_positive_fixture",start.Balances.GetValueOrDefault("experience")==1920&&start.Receipts.Any(r=>r.ActionType=="actual_vessel_arrival")&&start.Receipts.All(r=>!r.SemanticKey.StartsWith("internal:",StringComparison.Ordinal)&&r.ActionType is not("explicit_engineering_fixture" or "internal_slice_initialization")));
            var initial=await _chapterThree.ViewAsync();Check("actual_arrival_leaves_local_work_untouched",initial.Actor.Scene=="R3-S01"&&!initial.State.Landing&&!initial.State.Commission&&initial.Materials.Length==0&&initial.Quests.Single(q=>q.Id=="Q-301").Lifecycle==QuestLifecycle.Active&&initial.Quests.Single(q=>q.Id=="Q-302").Lifecycle==QuestLifecycle.Active);
            if(start.Balances.GetValueOrDefault("experience")!=1920||initial.State.Landing||initial.Materials.Length!=0)throw new InvalidOperationException("The native input must be untouched actual normal arrival, not a completion fixture.");
            await Material("R3-Q301-R-LANDING");await Act("register_landing");await Material("R3-Q301-R-COMMISSION");await Act("request_local_work");await Material("R3-Q301-R-BASIC");await Act("inspect_households");await Act("receive_basic_supplies");
            CheckCommit(await _party.ApplyBuildAsync(_chapterThree.BattleDefinitions.ReferenceBuild("BUILD-TOWER-M")));var weapon=(await _store.SnapshotAsync()).Items.Single(i=>i.Id=="collection:WP-05");if(weapon.OwnerKind!="equipped"||weapon.OwnerId!="hero"||weapon.Slot!="weapon")CheckCommit(await _party.EquipAsync(weapon.Id,"weapon"));
            await Form("request_batch_record_scope","明确提出MC-2限定字段申请",()=>Fields("现场检查状态","正式验收状态","签章责任范围","版本来源"));
            await Material("R3-Q301-R-SHIFT");await Material("R3-Q301-R-LADDER");await Act("inspect_ladder");await Act("inspect_lift");await Material("R3-Q301-R-RAIL");await Battle("ENC-R3-01","clear_rail_encounter");await Act("inspect_worker_signal");await Material("R3-Q301-R-PUMP");await Act("isolate_pump");await Spot("R3-S06.drain_control");await Click("明确设置隔离排水档");Pick("泵房档位",1);await Click("实际设置所选泵房档位");CloseAllPanels();await Battle("ENC-R3-02","clear_pump_encounter");await Act("drain_pump");await Act("inspect_water");
            await Material("R3-Q301-R-RESCUE");await Material("R3-Q301-R-ENVIRONMENT");await Material("R3-Q302-R-RAW");foreach(string caption in new[]{"实际隔离仪表机卫","实际固定仪表支架","现场核验当前零能状态"})await Form("clear_instrument_guard",caption,()=>{});await Act("inspect_raw_gauges");await Act("locate_workers");foreach(string worker in new[]{"W1","W2","W3"})await Form("rescue_workers","实际接出工人 "+worker,()=>{});await Battle("ENC-R3-04","escort_workers");await Act("receive_workers");
            var received=await _store.SnapshotAsync();Check("three_workers_actual_native_escort_and_reception",new[]{"W1","W2","W3"}.All(w=>StrictJson.Deserialize<MiningWorker>(received.Entities.Single(e=>e.Id=="R3:worker:"+w).Json) is{Status:"received",Scene:"R3-S02",Spot:"R3-S02.basic_handover"})&&(await _chapterThree.ViewAsync()).State.BasicDelivered);await Meaning("Q-301",[1,1,1,1,1]);await Submit("Q-301",true);
            foreach(string id in new[]{"R3-Q303-R-ESSAY","R3-Q303-R-DEMAND","R3-Q303-R-STOCK","R3-Q303-R-CHOICES"})await Material(id);await Act("inspect_local_stores");await Form("allocate_extra_packages","实际保留所选六件地方工包",()=>{Count("安置工包",4);Count("改造工包",2);Count("生产工包",0);});await Act("deliver_extra_packages");await Form("confirm_load_policy","实际应用明确的地方负荷方案",()=>{Pick("实际负荷政策",1);Count("当前抽取负荷",0);Pick("工作窗口",1);});await Meaning("Q-303",[1,5,3,1,1,1]);await Submit("Q-303");Check("resource_arrangement_closes_before_archive_findings",(await _chapterThree.ViewAsync()).Quests.Single(q=>q.Id=="Q-304").Lifecycle==QuestLifecycle.Unaccepted);
            await Act("inspect_bypass");foreach(string id in new[]{"R3-Q302-R-COPY","R3-Q302-R-SIGNED","R3-Q302-R-BATCHES","R3-Q302-R-HISTORY"})await Material(id);await Act("inspect_workshop_copy");await Act("inspect_signed_acceptance");await Form("compare_batch_records","明确提交三方版本与字段比较",()=>{foreach(string name in new[]{"原仪表业务版本","工坊副本业务版本","正式签章业务版本","副本独立性","奥伦验收责任","全部事故因果范围","导师当前状态"})Pick(name,1);Fields("MC-2-05","MC-2-06");});await Form("decide_oren_accountability","明确记录当前责任范围",()=>Pick("责任结论范围",1));await Meaning("Q-302",[1,1,1,1,1,1]);await Submit("Q-302");Check("actual_parallel_results_open_formal_tower_and_grade_stays_one",(await _chapterThree.ViewAsync()).Quests.Single(q=>q.Id=="Q-304").Lifecycle==QuestLifecycle.Active&&(await _party.InspectAsync()).Grade==1);
            foreach(string id in new[]{"R3-Q304-R-HANDOVER","R3-Q304-R-SEQUENCE","R3-Q304-R-LOAD","R3-Q304-R-DUTY","R3-Q304-R-TOWER","R3-Q304-R-REQUEST"})await Material(id);await Act("stop_machine_feed");Check("stopping_feed_does_not_auto_brace_arm",!(await _chapterThree.ViewAsync()).State.ArmBraced);await Act("brace_machine_arm");await Battle("ENC-R3-05","begin_extraction_machine_encounter");await Form("configure_tower_load","实际设置塔站当前负荷",()=>Count("塔站抽取负荷",0));await Act("test_tower_load");await Act("inspect_tower_duty");await Act("inspect_tower_return");await Spot("R3-S09.load_test");await Click("现场签发当前配置安全核验");CloseAllPanels();await Act("pack_verification_sample");await Act("record_tower_future_requirements");await Meaning("Q-304",[1,1,1,1,1]);await Submit("Q-304");
            var final=await _store.SnapshotAsync();Check("main_rewards_exact_once",final.Balances["experience"]==3840&&final.Balances["marks"]-marks==480&&final.Balances["parts"]-parts==8&&ChapterThreeService.MainQuests.All(q=>final.Receipts.Count(r=>r.SemanticKey==q+":resolution")==1));var tower=StrictJson.Deserialize<TowerReadiness>(final.Entities.Single(e=>e.Id=="readiness:TWR-R3").Json);Check("safe_only_not_full_conversion",tower.Safe&&!tower.Baseline&&!tower.Trial&&!tower.Operators&&!tower.Transition&&tower.EnhancedVersion is null);Check("second_calibration_only_after_actual_tower_handover",(await _party.InspectAsync()).Grade==2&&final.Items.Count(i=>i.DefinitionId=="TL-02")==1);Check("sample_and_shared_vessel_unique",final.Items.Count(i=>i.Id=="R3-MC2-SAMPLE-01"&&i.OwnerKind=="player"&&i.OwnerId=="hero")==1&&final.Entities.Count(e=>e.Kind=="shared_vessel")==1);
            var handover=StrictJson.Deserialize<MiningTowerHandover>(final.Entities.Single(e=>e.Id=="R3:actual_handover").Json);Check("actual_r4_handover_does_not_infer_current_mara_or_complete_causality",handover.Destination=="R4"&&handover.Batch=="MC-2"&&handover.MaraCurrentStatus=="unknown"&&handover.Causality=="association_requires_verification"&&!final.Entities.Any(e=>e.Id=="finding:K-MARA-DESIGN-LIMIT"));Check("retained_versions_and_actual_deliveries_survive",handover.SourceVersions["MC-2.original-instrument"]=="MC-2.instrument.I@1"&&handover.SourceVersions["MC-2.workshop-copy"]=="MC-2.retained.I@1"&&handover.SourceVersions["MC-2.formal-acceptance"]=="MC-2.acceptance@3"&&final.Items.Count(i=>i.Id.StartsWith("R3-local:work_kit:",StringComparison.Ordinal)&&i.OwnerKind=="installed")==6);
            await ActualRegionalAudio();await OpenChapterThreeArchive();await Frame();Check("archive_displays_only_actual_acquired_sources",_routing.Top=="沉钟矿区已得档案"&&Nodes(_panels.Peek()).OfType<Button>().Any(b=>b.Text==_chapterThree.Package.Materials.Single(m=>m.Id=="R3-Q304-R-REQUEST").Title)&&!(await _chapterThree.ViewAsync()).Materials.Any(m=>m.Id=="R3-Q302-R-STATEMENT"));CloseAllPanels();await SaveChapterThreeDraft();await _store.CreateCheckpointAsync("manual");
        }
        catch(Exception error){failures++;checks.Add(new{name="chapter_three_client_exception",passed=false,error=error.ToString()});}
        CloseAllPanels();ReleaseAudio(ref _lessonAudio);ReleaseAudio(ref _lookupAudio);await Frame();string output=System.Environment.GetEnvironmentVariable("MISTSEA_CLIENT_TEST_OUTPUT")??ProjectSettings.GlobalizePath("user://client-test");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"chapter_three_client_results.json"),StrictJson.Serialize(new{checks,failures,source="actual normal zero-XP through Q204 and typed R2/R3 voyage, copied untouched R3 SQLite arrival profile; native station/material/editor/meaning/submission controls with actual safe navigation commands; combat requests supplied by engineering controller",human_native_play="not_run",physical_ime="not_run",screen_reader="not_run",language_calibration="human_pending",audio="candidate playback tested only when actual binding decodes; human audition pending"}));GD.Print("MISTSEA_CHAPTER_THREE_CLIENT_TESTS "+checks.Count+" failures="+failures);GetTree().Quit(failures==0?0:1);
    }
}

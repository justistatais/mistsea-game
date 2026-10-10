using Godot;
using Mist.Application.ChapterTwo;
using Mist.Application.Contracts;
using Mist.Core.ChapterTwo;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Engineering;
using Mist.Core.Exploration;
using Mist.DebugCandidates;

public partial class Root
{
    private async Task RunChapterTwoClientTests()
    {
        var checks=new List<object>();int failures=0;void Check(string name,bool ok){checks.Add(new{name,passed=ok});if(!ok)failures++;}
        async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        async Task Settle(){await Frame();for(int i=0;_busy&&i<3000;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);if(_busy)throw new TimeoutException("R2 UI command did not settle.");await Frame();}
        IEnumerable<Node> Nodes(Node root){foreach(var node in root.GetChildren()){yield return node;foreach(var child in Nodes(node))yield return child;}}
        async Task Click(string caption){var buttons=Nodes(_panels.Peek()).OfType<Button>().Where(b=>b.Text==caption).ToArray();if(buttons.Length!=1)throw new InvalidOperationException("Expected single actual UI button: "+caption+"; got "+buttons.Length);buttons[0].EmitSignal(BaseButton.SignalName.Pressed);await Settle();}
        OptionButton Choice(string name)=>Nodes(_panels.Peek()).OfType<OptionButton>().Single(o=>o.AccessibilityName==name);
        void Pick(string name,int index)=>Choice(name).Select(index);
        void Fields(params string[] captions){foreach(var checkbox in Nodes(_panels.Peek()).OfType<CheckBox>())checkbox.ButtonPressed=captions.Contains(checkbox.Text);}
        async Task Walk(Point2 point){CloseAllPanels();var view=await _chapterTwo!.ViewAsync();var path=new NavigationGrid(view.Geometry[view.Actor.Scene]).FindPath(view.Actor.Position,point,true);if(path.Length==0)throw new InvalidOperationException("Actual safe R2 UI route absent.");await _chapterTwo.TraverseAsync(path);await Refresh();}
        async Task Spot(string id){var view=await _chapterTwo!.ViewAsync();await Walk(view.Geometry[view.Actor.Scene].Spots.Single(s=>s.Id==id).Stand);_selected=id;InteractSelected();await Settle();}
        async Task Exit(string scene){var view=await _chapterTwo!.ViewAsync();var port=view.Geometry[view.Actor.Scene].Ports.Single(p=>p.TargetScene==scene);await Walk(port.Stand);_selected=port.Id;InteractSelected();await Settle();if(_view.Actor.Scene!=scene)throw new InvalidOperationException("Actual R2 UI port failed: "+scene+"; "+_status.Text);}
        async Task Material(string id)
        {
            var material=_chapterTwo!.Package.Materials.Single(m=>m.Id==id);await Spot(material.Spot);
            if(material.Spot=="R2-S03.address")
            {
                long before=(await _store.SnapshotAsync()).Sequence;await Click("查看此处公共房间说明");
                Check("public_room_wrapper_"+id+"_is_readonly",(await _store.SnapshotAsync()).Sequence==before&&_routing.Top=="第二章现场 · "+material.Spot);
            }
            var button=Nodes(_panels.Peek()).OfType<Button>().Single(b=>b.Text.EndsWith(material.Title,StringComparison.Ordinal));button.EmitSignal(BaseButton.SignalName.Pressed);await Settle();if(!Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&t.Text==material.English))throw new InvalidOperationException("Acquired learner original not displayed: "+id);if(material.Kind=="listening_script")Check("script_"+id+"_does_not_claim_audio",_activeAudioId is null);CloseAllPanels();
        }
        async Task Act(string action){var station=_chapterTwo!.Package.Stations.Single(s=>s.Action==action);await Spot(station.Spot);await Click(station.Label);CloseAllPanels();}
        async Task Meaning(string quest,int[] indices)
        {
            var task=_chapterTwo!.Package.WritingTasks.Single(t=>t.Quest==quest);await Spot(task.Spot);CloseAllPanels();await OpenChapterTwoWriter(quest);await Frame();if(_chapterTwoWriter is null)throw new InvalidOperationException("Actual R2 writer absent.");
            _chapterTwoWriter.Text="Engineering UI candidate prose for "+quest+". The explicit choices below are supplied by this controller and are not automatic English understanding.";_chapterTwoWriter.EmitSignal(TextEdit.SignalName.TextChanged);await Click("保存第二章正文");Check("draft_"+quest+"_actual_ack",(await _store.GetDraftAsync(task.Id))?.Body==_chapterTwoWriter.Text);
            ApplyScale(200);await Frame();Check("writer_"+quest+"_200_percent",_split.Vertical&&_chapterTwoWriter.GetThemeFontSize("font_size")==40);ApplyScale(100);
            await Click("明确使用结构化辅助");var choices=Nodes(_panels.Peek()).OfType<OptionButton>().ToArray();Check("meaning_"+quest+"_has_no_default_answer",choices.Length==indices.Length&&choices.All(c=>c.Selected==0));if(choices.Length!=indices.Length)throw new InvalidOperationException("Explicit meaning row count mismatch.");for(int i=0;i<choices.Length;i++)choices[i].Select(indices[i]);await Click("明确记录当前第二章提议");CloseAllPanels();
        }
        async Task Submit(string quest,bool checkCancellation=false)
        {
            long before=(await _store.SnapshotAsync()).Sequence;await OpenChapterTwoSubmission(quest);await Frame();Check("preview_"+quest+"_readonly",(await _store.SnapshotAsync()).Sequence==before);string title="第二章提交预览 · "+quest;if(!_chapterTwoUiPreviews.TryGetValue(title,out var stack)||stack.Count==0)throw new InvalidOperationException("Actual R2 submission not ready: "+quest);
            if(checkCancellation){var token=stack.Peek();CloseAllPanels();Check("closed_"+quest+"_preview_cancelled",(await _chapterTwo!.ConfirmPreviewAsync(token)).Status==CommitStatus.Rejected);await OpenChapterTwoSubmission(quest);await Frame();}
            await Click("明确提交第二章当前结果");Check("submission_"+quest+"_once",(await _store.SnapshotAsync()).Receipts.Count(r=>r.SemanticKey==quest+":resolution")==1);
        }
        async Task RecordScope(string record)
        {
            await Spot("R2-S04.scope");await Click("申请具体记录版本与查验范围");Pick("记录与用途",record=="TR-D1-7"?1:2);if(record=="TR-D1-7")Fields("交接编号","身份链接","有日期的状态","去向代码","版本来源");else Fields("署名索引","版本来源");await Click("明确提出当前限定字段申请");CloseAllPanels();
        }
        async Task Battle(string id,string stationAction)
        {
            var station=_chapterTwo!.Package.Stations.Single(s=>s.Action==stationAction);await Spot(station.Spot);await Click(station.Label);if(id=="ENC-R2-03")await Click("查看核心实际遭遇入口");await Click("明确进入第二章所选遭遇");Check("encounter_"+id+"_native_shared_controls",_routing.Owner==Mist.Application.UI.InputOwner.CombatDecision&&_battleCards.Count==8&&_battleCards.All(c=>c.Size.X>120));
            // The controller supplies actions; UI visibility is observed, engine transitions remain actual.
            // This is explicitly not a claim that a human selected every native combat action.
            for(int i=0;i<600&&(await _chapterTwo.EncounterAsync(id))!.Battle.Outcome==CombatOutcome.Active;i++){var battle=(await _chapterTwo.EncounterAsync(id))!.Battle;var request=BattleCandidate.Choose(new(_chapterTwo.BattleDefinitions,battle));var preview=await PreviewEncounter(request);if(!preview.Action.Legal)throw new InvalidOperationException(preview.Description);CheckCommit(await ConfirmEncounter(preview.Token));}
            Check("encounter_"+id+"_actual_control",(await _chapterTwo.EncounterAsync(id))!.Battle.Outcome==CombatOutcome.Controlled);CloseAllPanels();await Refresh();
        }
        async Task CoreStep(string spot,string caption){await Spot(spot);await Click(caption);CloseAllPanels();}
        async Task ActualRegionalAudio()
        {
            const string id="R2-Q204-L-SCOPE";
            await Material(id);await OpenChapterTwoMaterial(id);await Frame();
            var before=await _store.SnapshotAsync();await Click("听力音轨与脚本");
            Check("regional_audio_actual_decode_and_no_autoplay",_activeAudioId==id&&_lessonAudio?.Stream is not null&&_lessonAudio.Stream.GetLength()>1&&!_lessonAudio.Playing);
            await Click("播放 / 暂停候选音轨");await ToSignal(GetTree().CreateTimer(.4),SceneTreeTimer.SignalName.Timeout);
            Check("regional_audio_real_playback_position",_lessonAudio is{Playing:true}&&_lessonAudio.GetPlaybackPosition()>0);
            await Click("播放 / 暂停候选音轨");Check("regional_audio_pause_preserves_position",_lessonAudio is{StreamPaused:true}&&_lessonAudio.GetPlaybackPosition()>0);
            Check("five_audio_channels_and_content_route",MistAudioChannels.Names.Length==5&&MistAudioChannels.Names.All(n=>AudioServer.GetBusIndex(n)>=0)&&_lessonAudio!.Bus==MistAudioChannels.Content);
            _Notification((int)NotificationApplicationFocusOut);Check("focus_loss_retains_actual_paused_audio",_lessonAudio is{StreamPaused:true}&&_audioPositions.GetValueOrDefault(id)>0);
            double pausedPosition=_lessonAudio!.GetPlaybackPosition();await Click("播放 / 暂停候选音轨");await ToSignal(GetTree().CreateTimer(.2),SceneTreeTimer.SignalName.Timeout);Check("explicit_resume_retains_actual_position",_lessonAudio is{Playing:true,StreamPaused:false}&&_lessonAudio.GetPlaybackPosition()>=pausedPosition);await Click("播放 / 暂停候选音轨");
            var speed=Choice("候选音轨播放速度");speed.Select(2);speed.EmitSignal(OptionButton.SignalName.ItemSelected,2L);await Settle();
            Check("regional_audio_speed_changes_actual_player",_lessonAudio is not null&&Math.Abs(_lessonAudio.PitchScale-1.25f)<.001);
            await Click("查看原文脚本 / 字幕");Check("regional_audio_original_transcript_unchanged",Nodes(_panels.Peek()).OfType<TextEdit>().Any(t=>!t.Editable&&t.Text==_chapterTwo!.Package.Materials.Single(m=>m.Id==id).English));ClosePanel();
            var notes=Nodes(_panels.Peek()).OfType<TextEdit>().Single(t=>t.Editable);notes.Text="Engineering playback observation; no human audition or language score.";
            await Click("保存当前听力证据笔记");var after=await _store.SnapshotAsync();
            Check("regional_audio_learning_does_not_reward_world",before.Sequence==after.Sequence&&before.Balances.OrderBy(p=>p.Key).SequenceEqual(after.Balances.OrderBy(p=>p.Key)));
            CloseAllPanels();await Frame();Check("regional_audio_close_releases_player",_lessonAudio is null&&_activeAudioId is null);
            await OpenSystem();await Frame();var volume=Nodes(_panels.Peek()).OfType<HSlider>().Single(s=>s.AccessibilityName=="内容语音音量");volume.Value=37;await Click("保存声音偏好");
            var saved=await _store.GetSettingAsync("audio_preferences");for(int i=0;saved is null&&i<60;i++){await Frame();saved=await _store.GetSettingAsync("audio_preferences");}
            Check("audio_independent_volume_saved_without_world_reward",saved is not null&&MistAudioChannels.Volume(MistAudioChannels.Content)==37&&MistAudioChannels.Volume("Ambience")==100&&(await _store.SnapshotAsync()).Sequence==after.Sequence);
            MistAudioChannels.Set(MistAudioChannels.Content,100);await MistAudioChannels.LoadAsync(_store);Check("audio_preferences_reload_actual_bus",MistAudioChannels.Volume(MistAudioChannels.Content)==37);
            MistAudioChannels.Set(MistAudioChannels.Content,100);await MistAudioChannels.SaveAsync(_store);CloseAllPanels();
        }
        try
        {
            if(_chapterTwo is null)throw new InvalidOperationException("Client test requires real copied Voyage normal-r2 profile and initialized ChapterTwo.");var start=await _store.SnapshotAsync();long marks=start.Balances.GetValueOrDefault("marks"),parts=start.Balances.GetValueOrDefault("parts");Check("normal_voyage_source_without_positive_fixture",start.Balances.GetValueOrDefault("experience")==900&&start.Receipts.Any(r=>r.ActionType=="sail_shared_glimmer")&&start.Receipts.All(r=>!r.SemanticKey.StartsWith("internal:",StringComparison.Ordinal)&&r.ActionType is not("explicit_engineering_fixture" or "internal_slice_initialization")));Check("arrival_does_not_grant_local_sources",!(await _chapterTwo.ViewAsync()).State.Landing&&!(await _chapterTwo.ViewAsync()).State.Reception&&(await _chapterTwo.ViewAsync()).Materials.Length==0);
            await Material("R2-Q201-R-LANDING");await Act("register_landing");await Exit("R2-S02");await Material("R2-Q201-R-REQUEST");await Act("request_local_reception");await Material("R2-Q201-R-OFFICIAL");await Exit("R2-S03");foreach(string action in new[]{"inspect_residence_a","inspect_residence_b","inspect_residence_c"})await Act(action);await Material("R2-Q201-R-RATION");await Material("R2-Q201-R-ROOMS");await Exit("R2-S02");await Meaning("Q-201",[1,1,1]);await Submit("Q-201",true);
            await Exit("R2-S04");await Material("R2-Q202-R-ACCESS");await Material("PZTIME-R-ORDER");await Material("PZTIME-R-ARRIVAL");await Spot("R2-S04.scope");await Click("保存M-12顺序与未知项说明");foreach(string name in new[]{"M12授权时刻","M12到达时刻","M12抄写时刻","M12抄本独立性","M12中央所知","M12抄写意图"})Pick(name,1);await Click("明确提交M-12顺序与未知项");CloseAllPanels();await RecordScope("TR-D1-7");await Material("R2-Q202-R-SENDING");await Material("R2-Q202-R-D1");await Spot("R2-S04.d1_handover");await Click("核验两端交接编号与观察日期");foreach(string name in new[]{"交接编号","观察日期范围","导师当前状态"})Pick(name,1);await Click("明确核对两端编号与日期范围");CloseAllPanels();await Material("R2-Q202-R-COHORT");await Meaning("Q-202",[1,1,1,1]);await Submit("Q-202");await Exit("R2-S02");
            CheckCommit(await _party.ApplyBuildAsync(_chapterTwo.BattleDefinitions.ReferenceBuild("BUILD-TOWER-M")));var weapon=(await _store.SnapshotAsync()).Items.Single(i=>i.Id=="collection:WP-05");if(weapon.OwnerKind!="equipped"||weapon.OwnerId!="hero"||weapon.Slot!="weapon")CheckCommit(await _party.EquipAsync(weapon.Id,"weapon"));
            await Exit("R2-S05");await Material("R2-Q203-R-WATER");await Act("collect_local_maintenance_items");await Battle("ENC-R2-01","begin_root_route_encounter");await Act("inspect_stair_boundary");await Exit("R2-S06");await Material("R2-Q203-R-OLD");await Material("R2-Q203-R-INTERLOCK");await Act("isolate_conveyor");foreach(string caption in new[]{"实际打开已隔离的护罩","实际释放卡滞","实际重新固定护罩","现场检查当前零运动"}){await Spot("R2-S06.conveyor");await Click("输送器实际手动四步");await Click(caption);CloseAllPanels();}await Act("set_external_clamp");await Act("drain_upper_floor");await Act("inspect_upper_route");await Exit("R2-S07");await Material("R2-Q203-R-PANEL");await Act("inspect_branch_a");await Act("inspect_branch_b");await Spot("R2-S07.core_entry");await Click("选择并实际执行核心处置");await Click("明确选择机械清理方法");CloseAllPanels();await CoreStep("R2-S07.branch_a","实际固定A支路外侧阀");await CoreStep("R2-S07.branch_b","实际固定B支路外侧阀");await Battle("ENC-R2-03","begin_core_procedure");await Spot("R2-S07.core_entry");await Click("选择并实际执行核心处置");await Click("威胁控制后实际清理缠绕");CloseAllPanels();await CoreStep("R2-S07.branch_a","处置后复查A支路外侧阀");await CoreStep("R2-S07.branch_b","处置后复查B支路外侧阀");
            await Act("isolate_local_reserve");await Act("seat_local_connector");await Spot("R2-S07.selector");await Click("设置地方负荷档位");Pick("地方负荷档位",3);await Click("实际设置所选档位");CloseAllPanels();await Act("power_local_reserve");await Act("test_local_configuration");await Act("open_inner_latch");await Exit("R2-S06");await Exit("R2-S05");await Act("inspect_kitchen_feed");await Exit("R2-S02");await Act("request_tower_work_permit");await Exit("R2-S08");await Material("R2-Q203-R-TOWER");await Act("inspect_tower_duty");await Act("inspect_tower_isolation");await Act("inspect_tower_return_route");await Meaning("Q-203",[1,1,3]);await Submit("Q-203");await Exit("R2-S02");
            await Exit("R2-S04");await RecordScope("TS-18");await Material("R2-Q204-R-ESSAY");await Material("R2-Q204-R-SIGNATURE");await Material("R2-Q204-R-BATCH");await Exit("R2-S02");await Spot("R2-S02.delivery");await Click("确认范围并实际交付事实摘要");await Click("选择实际交付范围");Pick("事实交付方式",1);Fields("公共事实","记录日期","来源版本","本地安全边界","历史署名的不确定范围","矿业批号");await Click("明确确认当前交付字段与接收方");CloseAllPanels();await Meaning("Q-204",[1,1,1,1]);await Submit("Q-204");
            var final=await _store.SnapshotAsync();Check("main_rewards_exact_once",final.Balances["experience"]==1920&&final.Balances["marks"]-marks==320&&final.Balances["parts"]-parts==5&&ChapterTwoService.MainQuests.All(q=>final.Receipts.Count(r=>r.SemanticKey==q+":resolution")==1));var tower=StrictJson.Deserialize<TowerReadiness>(final.Entities.Single(e=>e.Id=="readiness:TWR-R2").Json);Check("tower_safe_is_not_full_conversion",tower.Safe&&!tower.Baseline&&!tower.Trial&&!tower.Operators&&!tower.Transition&&tower.EnhancedVersion is null);Check("three_independent_actual_dispositions",new[]{"ENC-R2-01","ENC-R2-02","ENC-R2-03"}.All(id=>final.Receipts.Count(r=>r.SemanticKey=="R2:"+id+":result")==1));Check("no_fabricated_current_mara",final.Entities.Any(e=>e.Id=="finding:K-MARA-D1-ALIVE")&&!final.Entities.Any(e=>e.Id=="finding:K-MARA-DESIGN-LIMIT"));
            await ActualRegionalAudio();
            await OpenChapterTwoArchive();await Frame();Check("archive_contains_only_actual_learner_materials",_routing.Top=="第二章已得档案"&&Nodes(_panels.Peek()).OfType<Button>().Any(b=>b.Text==_chapterTwo.Package.Materials.Single(m=>m.Id=="R2-Q204-R-ESSAY").Title));CloseAllPanels();await SaveChapterTwoDraft();await _store.CreateCheckpointAsync("manual");Check("native_candidate_keeps_single_vessel",(await _store.SnapshotAsync()).Entities.Count(e=>e.Kind=="shared_vessel")==1);
        }
        catch(Exception error){failures++;checks.Add(new{name="chapter_two_client_exception",passed=false,error=error.ToString()});}
        CloseAllPanels();ReleaseAudio(ref _lessonAudio);ReleaseAudio(ref _lookupAudio);await Frame();string output=System.Environment.GetEnvironmentVariable("MISTSEA_CLIENT_TEST_OUTPUT")??ProjectSettings.GlobalizePath("user://client-test");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"chapter_two_client_results.json"),StrictJson.Serialize(new{checks,failures,source="actual no-fixture Voyage normal-r2 copied SQLite profile; native station/material/editor/meaning/submission buttons and actual safe paths; battle choices supplied by engineering controller",human_native_play="not_run",physical_ime="not_run",screen_reader="not_run",language_calibration="human_pending",audio="named local candidate decode/play/pause/position checks; human audition pending"}));GD.Print("MISTSEA_CHAPTER_TWO_CLIENT_TESTS "+checks.Count+" failures="+failures);GetTree().Quit(failures==0?0:1);
    }
}

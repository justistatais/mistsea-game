using Godot;
using Mist.Application.Contracts;
using Mist.Application.Drafts;
using Mist.Application.Materials;
using Mist.Application.Q102;
using Mist.Application.UI;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Learning;
using System.Collections.Immutable;
using System.Text.Json;

public partial class Root
{
    private TextEdit? _writer;
    private Label? _saveStatus;
    private double _draftDebounce;
    private bool _savingDraft;
    private Task<bool>? _saveTask;
    private Guid? _previewToken;
    private AudioStreamPlayer? _lessonAudio;
    private AudioStreamPlayer? _lookupAudio;
    private string? _activeAudioId;
    private readonly Dictionary<string,double> _audioPositions=new();
    private string? _leaseEvent;
    private readonly List<SupportUse> _support=new();
    private readonly Stack<Control?> _returnFocus=new();
    private VBoxContainer Panel(string title,InputOwner owner)
    {
        _returnFocus.Push(GetViewport().GuiGetFocusOwner());
        _path.Clear();
        foreach(var key in _keys.Values) if(Input.IsPhysicalKeyPressed(key)) _releaseBarrier.Add(key);
        var outer=new PanelContainer { MouseFilter=MouseFilterEnum.Stop }; outer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(outer);
        var style=new StyleBoxFlat { BgColor=new Color(0.035f,0.07f,0.1f,0.98f),ContentMarginLeft=28,ContentMarginRight=28,ContentMarginTop=20,ContentMarginBottom=20 }; outer.AddThemeStyleboxOverride("panel",style);
        var scroll=new ScrollContainer { HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,FollowFocus=true,SizeFlagsVertical=SizeFlags.ExpandFill }; outer.AddChild(scroll);
        var flow=new VBoxContainer { SizeFlagsHorizontal=SizeFlags.ExpandFill }; flow.AddThemeConstantOverride("separation",14); scroll.AddChild(flow);
        var heading=new HBoxContainer(); flow.AddChild(heading); Label(heading,title,28).SizeFlagsHorizontal=SizeFlags.ExpandFill;var back=Button(heading,"返回",ClosePanel);back.GrabFocus();
        _panels.Push(outer); _routing.Push(title,owner); return flow;
    }
    private void ClosePanel()
    {
        if(_panels.Count==0)return;
        string? title=_routing.Pop(); _panels.Pop().QueueFree();
        var prior=_returnFocus.Pop();
        if(title=="撤离草稿") { _writer=null; _saveStatus=null; _=SaveDraft(); }
        if(title=="执行预览"&&_previewToken is { } token) { _q.ClosePreview(token); _previewToken=null; }
        if(title=="行动预览"&&_battlePreview is { } actionToken){CloseEncounterPreview(actionToken);_battlePreview=null;}
        if(title is not null){CloseScopedPersonalUiPreview(title);CloseOldCityLifePreview(title);CloseR3ShiftUiPreview(title);CloseLinaWorkPreview(title);CloseSettlementPreview(title);CloseR2LifeUiPreview(title);CloseSideWellObservationPreview(title);CloseR1LifeUiPreview(title);CloseTessLocalReceiptBridgePreview(title);CloseChapterFourStudyPreview(title);CloseChapterFourCreditPreview(title);CloseOptionalLocalWorksUiPreview(title);}
        if(title is not null)CloseChapterUiPreview(title);
        if(title is not null)CloseVesselUiPreview(title);
        if(title is not null)CloseChapterTwoUiPreview(title);
        if(title is not null)CloseChapterFourUiPreview(title);
        if(title is not null)CloseChapterFiveUiPreview(title);
        if(title is not null)CloseChapterSixUiPreview(title);
        if(title is not null)CloseFinaleUiPreview(title);
        if(title is not null)CloseChapterThreeUiPreview(title);
        if(title is not null&&title.StartsWith("听力：")) CloseLessonAudio();
        if(title=="本地词典")ReleaseAudio(ref _lookupAudio);
        if(title=="哈文"&&_leaseEvent is { } evt) { _= _store.ReleaseLeaseAsync("N-HAVEL",evt); _leaseEvent=null; }
        if(_panels.Count==0)_world.GrabFocus();else if(prior is not null&&GodotObject.IsInstanceValid(prior)&&_panels.Peek().IsAncestorOf(prior))prior.GrabFocus();
    }
    private void CloseAllPanels() { while(_panels.Count>0) ClosePanel(); }
    private void OpenNavigation(string name)
    {
        if(!_initialized||_routing.WorldPaused)return;
        Execute(async()=>
        {
            switch(name)
            {
                case "地图": OpenMap(); break;
                case "任务": OpenQuests(); break;
                case "档案": OpenArchive(); break;
                case "队伍": await OpenParty(); break;
                case "学习": await OpenLearning(); break;
                case "系统": await OpenSystem(); break;
            }
        });
    }
    private async ValueTask OpenCurrentWritingWorkspace()
    {
        string region=CurrentWorkRegion();
        if(region=="R6"&&_chapterSix is not null&&_view.Snapshot.Entities.Any(e=>e.Id=="chapter6")){await OpenChapterSixQuests();return;}
        if(region=="RF"&&_finale is not null&&_view.Snapshot.Entities.Any(e=>e.Id=="finale")){await OpenFinaleQuests();return;}
        if(region=="R3"&&_chapterThree is not null&&_view.Snapshot.Entities.Any(e=>e.Id=="chapter3")){await OpenChapterThreeQuests();return;}
        if(region=="R4"&&_chapterFour is not null&&_view.Snapshot.Entities.Any(e=>e.Id=="chapter4")){await OpenChapterFourQuests();return;}
        if(region=="R5"&&_chapterFive is not null&&_view.Snapshot.Entities.Any(e=>e.Id=="chapter5")){await OpenChapterFiveQuests();return;}
        if(region=="R2"&&_chapterTwo is not null&&_view.Snapshot.Entities.Any(e=>e.Id=="chapter2")){await OpenChapterTwoQuests();return;}
        if(_chapter is not null&&HasReceipt(_view.Snapshot,"Q-102")){await OpenFindingWriter();return;}
        OpenWriter();
    }
    private void OpenMap()
    {
        var flow=Panel("港区地图",InputOwner.Workspace); Label(flow,"地图标注地点和已知出口。航道是否可用须按现行资料核对。",20);
        foreach(var scene in ActiveGeometry.Values.OrderBy(s=>s.Id)) Label(flow,$"{_catalog.Scenes.Single(s=>s.Id==scene.Id).Name} · {scene.Ports.Count(p=>ActiveGeometry.ContainsKey(p.TargetScene))} 个内部出口",22);
        Label(flow,"选择地点不会传送角色。返回场景后从登记出口走过去。",18);
    }
    private void OpenQuests()
    {
        var flow=Panel("任务日志",InputOwner.Workspace);
        Label(flow,_objective.Text,24);
        foreach(var entity in _view.Snapshot.Entities.Where(e=>e.Kind=="quest").OrderBy(e=>e.Id))
        {
            var state=StrictJson.Deserialize<Mist.Core.Quests.QuestState>(entity.Json);if(state.Lifecycle==Mist.Core.Quests.QuestLifecycle.Unaccepted)continue;
            var definition=_catalog.Quests.FirstOrDefault(q=>q.Id==state.Id);Label(flow,(definition?.Title??state.Id)+" · "+state.Lifecycle,22);
            if(definition is not null&&state.Lifecycle!=Mist.Core.Quests.QuestLifecycle.Closed)Label(flow,definition.CloseContract,18);
        }
        Button(flow,"打开同一份撤离草稿",()=>OpenWriter());
        Button(flow,"查看实际工作与协议",()=>Execute(async()=>await OpenWorkOrders()));
        if(_view.Snapshot.Entities.Any(e=>e.Id==Mist.Application.R2Life.R2LifeService.StateId))Button(flow,"修会地方生活工作",()=>Execute(OpenR2LifeQuests));
        if(_view.Snapshot.Entities.Any(e=>e.Id=="chapter2"))Button(flow,"第二章工作与当前状态",()=>Execute(OpenChapterTwoQuests));
        if(_view.Snapshot.Entities.Any(e=>e.Id=="chapter4"))Button(flow,"第四章工作与当前状态",()=>Execute(OpenChapterFourQuests));
        if(_view.Snapshot.Entities.Any(e=>e.Id=="chapter5"))Button(flow,"第五章工作与当前状态",()=>Execute(OpenChapterFiveQuests));
        if(_view.Snapshot.Entities.Any(e=>e.Id=="chapter6"))Button(flow,"第六章工作与当前状态",()=>Execute(OpenChapterSixQuests));
        if(_view.Snapshot.Entities.Any(e=>e.Id=="finale"))Button(flow,"终章工作与当前状态",()=>Execute(OpenFinaleQuests));
        if(_view.Snapshot.Entities.Any(e=>e.Id=="chapter3"))Button(flow,"第三章工作与当前状态",()=>Execute(OpenChapterThreeQuests));
        Button(flow,_trackingQ102?"取消当前HUD跟踪（保留委托）":"跟踪此委托",()=>Execute(async()=>{_trackingQ102=!_trackingQ102;await SavePreferences();ClosePanel();OpenQuests();}));
        Label(flow,_chapter is null?"当前为港口内部样章；序章游玩与后续章节未在此内部测试档案完成。":"任务日志显示本档案的实际委托和回执；各章只按现场取得、实际操作和正式交接推进。",18);
    }
    private void OpenArchive()
    {
        var flow=Panel("已得档案",InputOwner.Workspace);Button(flow,"离线查词",OpenRegionalDictionarySearch);Button(flow,"主动查看到期复习",()=>Execute(OpenRegionalReview)); var visible=MaterialJournal.Visible(_catalog,_view.Snapshot);
        if(visible.Length==0)Label(flow,"已得材料按下方档案入口查看。走近现场可取得新的公告、资料和录音。",22);
        // Some frozen reference IDs also exist in the source-bound regional package.
        // Open those through its versioned permission-aware reader, never the older catalog copy.
        foreach(var material in visible.Where(m=>!RegionalMaterialId(m.Id))) Button(flow,material.Title,()=>OpenMaterial(material.Id));
        if(_r1Life is not null&&_view.Snapshot.Entities.Any(e=>e.Id==Mist.Application.R1Life.R1LifeService.StateId))Button(flow,"盐雾港生活委托与档案",()=>Execute(OpenR1LifeQuests));
        Button(flow,"查看音乐盒地方工作档案",()=>Execute(async()=>{if(!await HasOptionalLocalWorksArchive())throw new InvalidOperationException("尚未取得当前可读的地方音乐盒档案。");await OpenOptionalLocalWorksArchive();}));
        if(_firstApproaches is not null)Button(flow,"中继与塔基处置档案",()=>Execute(async()=>{var archive=Panel("中继与塔基已得处置来源",InputOwner.Workspace);await AddFirstApproachArchive(archive);}));
        if(_chapter is not null)Button(flow,"序章与首章原始记录",()=>Execute(async()=>await OpenChapterArchive()));
        if(_view.Snapshot.Entities.Any(e=>e.Id=="chapter2"))Button(flow,"第二章已得档案",()=>Execute(OpenChapterTwoArchive));
        if(_view.Snapshot.Entities.Any(e=>e.Id=="chapter4"))Button(flow,"第四章已得档案",()=>Execute(OpenChapterFourArchive));
        if(_view.Snapshot.Entities.Any(e=>e.Id=="chapter5"))Button(flow,"第五章已得档案",()=>Execute(OpenChapterFiveArchive));
        if(_view.Snapshot.Entities.Any(e=>e.Id=="chapter6"))Button(flow,"第六章已得档案",()=>Execute(OpenChapterSixArchive));
        if(_view.Snapshot.Entities.Any(e=>e.Id=="finale"))Button(flow,"终章已得档案",()=>Execute(OpenFinaleArchive));
        if(_view.Snapshot.Entities.Any(e=>e.Id=="chapter3"))Button(flow,"第三章已得档案",()=>Execute(OpenChapterThreeArchive));
        Button(flow,"当前获准个人工作应答",()=>Execute(OpenScopedPersonalArchive));
        Button(flow,"旧都已取得地方公开材料",()=>Execute(OpenOldCityLifeArchive));
        Button(flow,"换班的人当前公开材料",()=>Execute(async()=>{if(!await HasR3ShiftArchive())throw new InvalidOperationException("尚未实际取得本班原件。");await OpenR3ShiftArchive();}));
        Button(flow,"莉娜当前获准个人材料",()=>Execute(OpenLinaWorkArchive));
        Button(flow,"当前已得独立安全合同",()=>Execute(async()=>{var archive=Panel("独立安全合同档案",InputOwner.Workspace);await AddOptionalEncounterArchive(archive);}));
        Button(flow,"撤离草稿",()=>OpenWriter());
    }
    private async ValueTask OpenSystem()
    {
        var flow=Panel("系统与存档",InputOwner.Workspace); Label(flow,"当前分支："+_store.Session.Branch.Value.ToString()[..8],18);
        var scaleRow=new HFlowContainer(); flow.AddChild(scaleRow);
        foreach(int scale in new[]{100,125,150,175,200}) {var scaleButton=Button(scaleRow,scale+"%",()=>{ ApplyScale(scale); _=SavePreferences(); });scaleButton.AutowrapMode=TextServer.AutowrapMode.Off;}
        MistAudioChannels.Controls(flow,_store,_scale,text=>_status.Text=text);
        Label(flow,"移动与交互按键",22);
        foreach(string action in _keys.Keys.ToArray()) Button(flow,action+" · "+_keys[action],()=>{ _rebind=action; _status.Text="请按新按键。"; });
        Button(flow,"继续当前冒险",()=>Execute(async()=>{ if(!await SaveAdventureDrafts())throw new InvalidOperationException("正文尚未保存；请重试或导出后再切换。"); CloseAllPanels();ResetRegionalUiState(); await _store.ContinueAsync(); _draft=null;_findingDraft=null; await Refresh(); }));
        for(int slot=0;slot<6;slot++)
        {
            int index=slot; var row=new HBoxContainer(); flow.AddChild(row);
            Button(row,"保存手动槽 "+(index+1),()=>Execute(async()=>{ if(!await SaveAdventureDrafts())throw new InvalidOperationException("当前正文未保存，检查点尚未创建。"); await _store.CreateCheckpointAsync("manual",index); _status.Text="冒险检查点已保存。"; }));
            Button(row,"读取手动槽 "+(index+1),()=>Execute(async()=>{ var slots=await _store.SlotsAsync(); if(!slots.TryGetValue("manual:"+index,out string? checkpoint))throw new InvalidOperationException("该槽为空。"); await LoadCheckpoint(checkpoint); }));
        }
        Label(flow,"已登记检查点",22);
        foreach(var saved in await _store.SlotsAsync())
        {
            string checkpoint=saved.Value;Button(flow,saved.Key+" · "+checkpoint[..8],()=>Execute(async()=>await LoadCheckpoint(checkpoint)));
        }
        Button(flow,"创建一致恢复备份",()=>Execute(async()=>{ if(!await SaveAdventureDrafts())throw new InvalidOperationException("正文尚未保存，备份尚未创建。");var backup=await _store.BackupAsync(); _status.Text="恢复备份已验证："+backup.Id[..8]; }));
        Label(flow,"隐私操作需要单独明确确认。在线帮助默认关闭。",18);
        Button(flow,"删除当前草稿…",()=>{ var confirm=Panel("确认删除正文",InputOwner.Confirmation); Label(confirm,"删除此稿的所有正文版本；任务、物品和世界回执保留。删除会撤回在线许可。",22); Button(confirm,"明确删除当前正文",()=>Execute(async()=>{ var draft=await _store.GetDraftAsync(Q102Service.DraftTask); if(draft is not null)await _store.EraseAsync("draft",draft.Id); _draft=null; CloseAllPanels(); })); });
        Button(flow,"删除学习记录…",()=>{ var confirm=Panel("确认删除学习记录",InputOwner.Confirmation); Label(confirm,"删除既有学习观察和评价，并撤回在线许可。旧存档不会恢复这些记录。",22); Button(confirm,"明确删除学习记录",()=>Execute(async()=>{ await _store.EraseAsync("learning"); CloseAllPanels(); })); });
        string? preferences=await _store.GetSettingAsync("ui_preferences"); if(preferences is not null) Label(flow,"本地偏好已保存。",18);
    }
    private async ValueTask LoadCheckpoint(string checkpoint)
    {
        if(!await SaveAdventureDrafts())throw new InvalidOperationException("正文尚未保存；请重试或导出后再读取。");
        CloseAllPanels();ResetRegionalUiState();await _store.LoadCheckpointAsync(checkpoint);_draft=null;_findingDraft=null;await Refresh();if(_initialized)_status.Text="已创建旧检查点的冒险分支。学习历史保留；当前草稿指针采用检查点版本。";
    }
    private async Task SavePreferences()
    {
        string? saved=await _store.GetSettingAsync("ui_preferences");var preferences=saved is null?new System.Text.Json.Nodes.JsonObject():System.Text.Json.Nodes.JsonNode.Parse(saved)?.AsObject()??throw new InvalidDataException("界面偏好不是有效对象。");
        preferences["font_scale"]=_scale;preferences["tracking_q102"]=_trackingQ102;preferences["keys"]=System.Text.Json.JsonSerializer.SerializeToNode(_keys.ToDictionary(k=>k.Key,k=>(long)k.Value));
        await _store.SetSettingAsync("ui_preferences",preferences.ToJsonString());
    }
    private async Task LoadPreferences()
    {
        string? json=await _store.GetSettingAsync("ui_preferences"); if(json is null)return;
        using var document=JsonDocument.Parse(json); int scale=document.RootElement.GetProperty("font_scale").GetInt32(); if(new[]{100,125,150,175,200}.Contains(scale))_scale=scale;
        if(document.RootElement.TryGetProperty("tracking_q102",out var tracking))_trackingQ102=tracking.GetBoolean();
        if(document.RootElement.TryGetProperty("keys",out var bindings))foreach(var key in bindings.EnumerateObject())if(_keys.ContainsKey(key.Name))_keys[key.Name]=(Key)key.Value.GetInt64();
        Theme.DefaultFontSize=20*_scale/100;
    }
    private void ApplyScale(int scale)
    {
        _scale=scale; Theme.DefaultFontSize=20*scale/100;
        void Apply(Node node)
        {
            if(node is Label label&&label.HasMeta("base_font_size"))label.AddThemeFontSizeOverride("font_size",label.GetMeta("base_font_size").AsInt32()*scale/100);
            if(node is TextEdit edit)edit.AddThemeFontSizeOverride("font_size",20*scale/100);
            if(node is Button button)button.AddThemeFontSizeOverride("font_size",20*scale/100);
            foreach(var child in node.GetChildren())Apply(child);
        }
        Apply(this);
        ReflowShell();
    }
    private void InteractSelected()
    {
        if(_routing.WorldPaused||_selected is null)return;
        var target=_targetList.Single(t=>t.Id==_selected);
        if(_position.Distance(target.Stand)>2.5){ _status.Text="请先走近选定站位。";return; }
        Execute(async()=>
        {
            if(target.Port)
            {
                var port=ActiveGeometry[_view.Actor.Scene].Ports.Single(p=>p.Id==target.Id);
                if(await TrySideWellPortInteraction(port.Id))return;
                if(_r2Life is not null&&port.Id is "R2-S03.to.R2-O01" or "R2-O01.to.R2-S03"){CheckCommit(await _r2Life.EnterPortAsync(port.Id));await _store.CreateCheckpointAsync("automatic");return;}
                if(_r1Life is not null&&_r1Life.Package.PortGates.ContainsKey(port.Id)){CheckCommit(await _r1Life.EnterPortAsync(target.Id));await _store.CreateCheckpointAsync("automatic");return;}
                if(_vessel is not null&&_view.Actor.Scene.StartsWith("HUB-",StringComparison.Ordinal))
                {
                    CheckCommit(await _vessel.EnterPortAsync(target.Id));await _store.CreateCheckpointAsync("automatic");return;
                }
                if(_chapterTwo is not null&&_view.Actor.Scene.StartsWith("R2-",StringComparison.Ordinal))
                {
                    CheckCommit(await _chapterTwo.EnterPortAsync(target.Id));await _store.CreateCheckpointAsync("automatic");return;
                }
                if(_chapterThree is not null&&_view.Actor.Scene.StartsWith("R3-",StringComparison.Ordinal))
                {
                    CheckCommit(await _chapterThree.EnterPortAsync(target.Id));await _store.CreateCheckpointAsync("automatic");return;
                }
                if(_chapterFour is not null&&_view.Actor.Scene.StartsWith("R4-",StringComparison.Ordinal))
                {
                    CheckCommit(await _chapterFour.EnterPortAsync(target.Id));await _store.CreateCheckpointAsync("automatic");return;
                }
                if(_finale is not null&&_view.Actor.Scene.StartsWith("RF-",StringComparison.Ordinal)){CheckCommit(await _finale.EnterPortAsync(target.Id));await _store.CreateCheckpointAsync("automatic");return;}
                if(_chapterSix is not null&&_view.Actor.Scene.StartsWith("R6-",StringComparison.Ordinal)){CheckCommit(await _chapterSix.EnterPortAsync(target.Id));await _store.CreateCheckpointAsync("automatic");return;}
                if(_chapterFive is not null&&_view.Actor.Scene.StartsWith("R5-",StringComparison.Ordinal))
                {
                    CheckCommit(await _chapterFive.EnterPortAsync(target.Id));await _store.CreateCheckpointAsync("automatic");return;
                }
                if(((_view.Actor.Scene=="R1-S05"&&port.TargetScene=="R1-S06")||(_view.Actor.Scene=="R1-S06"&&port.TargetScene=="R1-S05"))&&!_view.Workshop.FrontSafe){await OpenBattleEntrance();return;}
                bool useChapter=_chapter is not null&&(!_q.Geometry.TryGetValue(_view.Actor.Scene,out var qscene)||!_q.Geometry.ContainsKey(port.TargetScene)||!qscene.Ports.Any(p=>p.Id==target.Id));
                var result=useChapter?await _chapter!.EnterPortAsync(target.Id):await _q.EnterPortAsync(target.Id);CheckCommit(result);
                if(result.Status==CommitStatus.Committed&&result.Receipt is {} receipt)
                {
                    try{if(!useChapter)CheckCommit(await new Mist.Application.Travel.TravelService(_store,_catalog).RecordWalkedRouteAsync(receipt.Id.Value.ToString()));}
                    catch(Exception error){_status.Text="已实际通过出口；安全返程登记尚未完成："+error.Message;}
                    await _store.CreateCheckpointAsync("automatic");
                }
                return;
            }
            if(await TryFirstApproachInteraction(target.Id))return;
            if(await TryChapterSixFacilityInteraction(target.Id))return;
            if(await TryOptionalEncounterInteraction(target.Id))return;
            if(await TryOldCityLifeInteraction(target.Id))return;
            if(await TrySettlementSupportInteraction(target.Id))return;
            if(await TryFieldRelayRepairInteraction(target.Id))return;
            if(await TryChapterSixInteraction(target.Id))return;
            if(await TryFinaleInteraction(target.Id))return;
            if(await TryVesselInteraction(target.Id))return;
            if(await TryR2LifeInteraction(target.Id))return;
            if(await TryChapterTwoInteraction(target.Id))return;
            if(await TryChapterFourStudyInteraction(target.Id))return;
            if(await TryChapterFourCreditInteraction(target.Id))return;
            if(await TryChapterFourInteraction(target.Id))return;
            if(await TryChapterFiveInteraction(target.Id))return;
            if(await TryOptionalLocalWorksInteraction(target.Id))return;
            if(await TryR1LifeInteraction(target.Id))return;
            if(await TrySideWellObservationInteraction(target.Id))return;
            if(await TryChapterThreeInteraction(target.Id))return;
            if(await TryChapterInteraction(target.Id))return;
            switch(target.Id.Split('.').Last())
            {
                case "quay_contact": case "havel": await OpenHavel(); break;
                case "bulletin": await ReadAt("Q102-R-NOTICE",target.Id); break;
                case "route_b_rule": await ReadAt("Q102-R-B",target.Id); break;
                case "dispatch_audio": await ReadAt("Q102-L-REVISION",target.Id); break;
                case "vessel_spec": await ReadAt("Q102-R-VESSEL",target.Id); break;
                case "reserve_battery": OpenBattery(target.Id); break;
                case "beacon": OpenBeacon(); break;
                case "writing_desk": OpenWriter(); break;
                case "main_generator_tag": await ReadAt("PZ102-R-PANEL",target.Id); break;
                case "maintenance_note": await ReadAt("PZ102-R-WORKSHOP",target.Id); break;
                case "local_controller": OpenController(target.Id); break;
                case "spare_bracket": await _q.PickBracketAsync(); _status.Text="唯一替换支架已移入持有物。"; break;
                case "shortcut_latch": await _q.OpenLatchAsync(); _status.Text="回程横闩已打开。"; break;
                case "district_map": case "service_map": OpenMap(); break;
                case "observe_unit": Label(Panel("观察维护机械",InputOwner.Workspace),"机械按当前检修模式运行；固定检查不是完全停机。",22); break;
                case "work_order": Label(Panel("未核实工单",InputOwner.Workspace),"这里留有未经核实的工单。它尚不能证明事故原因，需要后续来源核对。",22); break;
            }
        });
    }
    private async ValueTask ReadAt(string material,string spot) { await _q.DiscoverAsync(material,spot); _view=await _q.ViewAsync(); OpenMaterial(material); }
    private void OpenBattery(string spot)
    {
        var flow=Panel("船内备用电池",InputOwner.Puzzle); Label(flow,"这是民用渡轮已有的充电电池，工坊本地储备是另一套设备。",22);
        Button(flow,"查看电池资料",()=>Execute(async()=>await ReadAt("Q102-R-BATTERY",spot)));
        Button(flow,_view.Ferry.BatteryConnected?"断开电池连接":"连接到导航灯",()=>Execute(async()=>{ await _q.ConnectBatteryAsync(!_view.Ferry.BatteryConnected); ClosePanel(); }));
    }
    private void OpenBeacon()
    {
        var flow=Panel("民用渡轮导航灯",InputOwner.Puzzle); Label(flow,_view.Ferry.BracketInstalled?"支架已安装。":"灯体可用，支架受损。",22);
        Button(flow,"安装持有的替换支架",()=>Execute(async()=>{ await _q.InstallBracketAsync(); ClosePanel(); }));
        Button(flow,"测试当前安装配置",()=>Execute(async()=>{ await _q.TestBeaconAsync(); _status.Text="已记录当前配置测试；拔插或安装改变后须重测。"; ClosePanel(); }));
    }
    private void OpenController(string spot)
    {
        var flow=Panel("本地检修控制器",InputOwner.Puzzle); Label(flow,"本地储备供电 · 与港口主发电机分开",22);
        Button(flow,"阅读检修说明",()=>Execute(async()=>await ReadAt("PZ102-R-WORKSHOP",spot)));
        foreach(var mode in new[]{WorkshopMode.Isolated,WorkshopMode.Focused,WorkshopMode.Active}) Button(flow,mode switch{ WorkshopMode.Isolated=>"本地隔离",WorkshopMode.Focused=>"固定检查",_=>"恢复正常运行" },()=>Execute(async()=>{ await _q.WorkshopModeAsync(mode); ClosePanel(); }));
    }
    private async ValueTask OpenHavel()
    {
        string evt=Guid.NewGuid().ToString(); if(!await _store.TryLeaseAsync("N-HAVEL",evt))throw new InvalidOperationException("哈文正在处理已占用的交谈，请稍后。");
        _leaseEvent=evt; var flow=Panel("哈文",InputOwner.Dialogue);
        Label(flow,_view.Quest.Lifecycle==Mist.Core.Quests.QuestLifecycle.Unaccepted?"We have a work permit for the ferry. You may inspect the equipment before deciding whether to accept the job.":_view.Ferry.Docked?"The departure still needs a tested beacon and confirmed instructions. The ferry remains at the quay.":"The civilian ferry has left. You are staying here; the tower work is still ahead.",22);
        if(_view.Snapshot.Items.Any(i=>i.Id==Q102Service.BracketId&&i.OwnerKind!="warehouse"))Label(flow,"I see that the replacement bracket has already been collected. There is no need to take another.",22);
        if(_view.Quest.Lifecycle==Mist.Core.Quests.QuestLifecycle.Unaccepted)Button(flow,"明确接受检修委托",()=>Execute(async()=>{ await _q.AcceptAsync(); ClosePanel(); }));
        Button(flow,"查看当前提议",()=>OpenWriter());
    }
    private void OpenMaterial(string id)
    {
        var material=MaterialJournal.Open(_catalog,_view.Snapshot,id);
        if(material.Modality=="listening"){ OpenListening(material);return; }
        var flow=Panel(material.Title,InputOwner.Workspace); Label(flow,$"来源 {material.Id} · 版本 {material.Version}",16);
        var text=new TextEdit { Text=string.Join("\n\n",material.Paragraphs.Select(p=>p.Id+"\n"+p.Text)),Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new Vector2(0,350),AccessibilityName=material.Title+"原文" }; flow.AddChild(text); text.AddThemeFontSizeOverride("font_size",20*_scale/100);
        Button(flow,"查所选词",()=>OpenDictionary(text.GetSelectedText(),material));
        Button(flow,"打开同一份撤离草稿",()=>OpenWriter());
    }
    private void OpenListening(MaterialDefinition material)
    {
        CloseLessonAudio();
        var flow=Panel("听力："+material.Title,InputOwner.Workspace); string path="res://content/"+material.AudioAsset;
        bool available=material.AudioAsset is not("missing" or "not_applicable")&&Godot.FileAccess.FileExists(path);
        var state=Label(flow,available?"实际音轨 · 未经人工听审与6.5校准":"音轨缺失。可查看脚本继续任务；不会显示虚假播放进度。",20);
        bool subtitlesViewed=false,played=false;int replays=0,pauses=0;decimal playbackSpeed=1m;string attempt=Guid.NewGuid().ToString();
        if(available)
        {
            _activeAudioId=material.Id; _lessonAudio=new AudioStreamPlayer { Stream=AudioStreamWav.LoadFromBuffer(Godot.FileAccess.GetFileAsBytes(path),new Godot.Collections.Dictionary()),Bus=MistAudioChannels.Content }; AddChild(_lessonAudio);
            if(_lessonAudio.Stream is null){ state.Text="音轨无法解码。请使用脚本入口。"; available=false; }
            else
            {
                var player=_lessonAudio; Button(flow,"播放 / 暂停",()=>{ if(player.StreamPaused)player.StreamPaused=false;else if(player.Playing){player.StreamPaused=true;_audioPositions[material.Id]=player.GetPlaybackPosition();pauses++;}else {player.Play((float)_audioPositions.GetValueOrDefault(material.Id));if(played)replays++;played=player.Playing;} state.Text=player.StreamPaused?"已暂停，位置保留。":"正在播放实际音轨。"; });
                Button(flow,"记住当前位置",()=>{ _audioPositions[material.Id]=player.GetPlaybackPosition(); state.Text="已保留位置："+_audioPositions[material.Id].ToString("F1")+" 秒"; });
                var speed=new OptionButton(); foreach(double value in new[]{0.75,1.0,1.25})speed.AddItem(value+"×"); speed.Select(1); speed.ItemSelected+=index=>{playbackSpeed=new[]{0.75m,1m,1.25m}[index];player.PitchScale=(float)playbackSpeed;}; flow.AddChild(speed);
                Button(flow,"回到保存的位置",()=>{ player.Play((float)_audioPositions.GetValueOrDefault(material.Id));if(played)replays++;played=player.Playing; });
            }
        }
        Button(flow,"查看脚本 / 字幕",()=>{ subtitlesViewed=true; _support.Add(new("transcript",material.Id,material.Paragraphs[0].Id,0,material.Paragraphs[0].Text.EnumerateRunes().Count())); var transcript=Panel("脚本："+material.Title,InputOwner.Workspace); foreach(var p in material.Paragraphs)Label(transcript,p.Text,22); });
        var answer=new TextEdit { CustomMinimumSize=new Vector2(0,100),AccessibilityName="听力笔记与证据",WrapMode=TextEdit.LineWrappingMode.Boundary }; flow.AddChild(answer);
        Button(flow,"查笔记中选中的词",()=>OpenDictionary(answer.GetSelectedText(),material));
        Button(flow,"保存本次证据笔记",()=>Execute(async()=>{ var observation=new LearningObservation(Guid.NewGuid().ToString(),material.FamilyId,attempt,available?InputModality.Listening:InputModality.Reading,!string.IsNullOrWhiteSpace(answer.Text),true,available,subtitlesViewed,_support.ToArray(),material.Version,answer.Text,played,playbackSpeed,replays,pauses); var view=await _store.ObserveAsync(observation); state.Text="记录状态："+view.State+"。尚未评价理解或能力分数。"; }));
    }
    private void CloseLessonAudio()
    {
        if(_lessonAudio is null)return;
        if(_activeAudioId is { } id)_audioPositions[id]=_lessonAudio.GetPlaybackPosition();
        ReleaseAudio(ref _lessonAudio);_activeAudioId=null;
    }
    private static void ReleaseAudio(ref AudioStreamPlayer? audio)
    {
        if(audio is null)return;if(GodotObject.IsInstanceValid(audio)){var stream=audio.Stream;audio.Stop();audio.Stream=null;audio.QueueFree();stream?.Dispose();}audio=null;
    }
    private void OpenDictionary(string word,MaterialDefinition material)
    {
        _support.Add(new("dictionary",material.Id,material.Paragraphs[0].Id,0,material.Paragraphs[0].Text.EnumerateRunes().Count()));
        Execute(async()=>await OpenRegionalDictionary(word,ReadGrantedLearningSources));
    }
    private async void OpenWriter()
    {
        try
        {
            if(_routing.Contains("撤离草稿")) { while(_routing.Top!="撤离草稿")ClosePanel(); _writer?.GrabFocus();return; }
            if(_draft is null)
            {
                var head=await _store.GetDraftAsync(Q102Service.DraftTask); var s=_store.Session;
                _draft=new DraftEditor(_store,new(s.Profile,s.Branch,s.Load,Q102Service.DraftTask,head?.Id,head?.Revision??0,head?.Body??"","Q102-W-PLAN@0.1"));
            }
            var flow=Panel("撤离草稿",InputOwner.Editable); Label(flow,_catalog.Q102WritingPrompt,22);
            Label(flow,"目标 R/L/W 6.5 · 建议90–120词，字数不限制世界行动。当前无在线英文理解或能力评分。",18);
            _writer=new TextEdit { Text=_draft.Text,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new Vector2(0,300),AccessibilityName="撤离指令正文" }; _writer.AddThemeFontSizeOverride("font_size",20*_scale/100); flow.AddChild(_writer);
            _writer.TextChanged+=()=>{ _draft.Edit(_writer.Text); _draftDebounce=0.5; if(_saveStatus is not null)_saveStatus.Text="有未保存修改。"; if(_previewToken is { } token){_q.ClosePreview(token);_previewToken=null;} };
            _writer.FocusExited+=()=>{ foreach(var key in _keys.Values.Append(Key.Enter))if(Input.IsPhysicalKeyPressed(key))_releaseBarrier.Add(key); };
            _saveStatus=Label(flow,"尚未收到本次正文的保存回执。",18);
            Button(flow,"保存草稿",()=>_=SaveDraft());
            Button(flow,"导出正文到本地",()=>{ string directory=ProjectSettings.GlobalizePath("user://draft-exports"); Directory.CreateDirectory(directory); string path=System.IO.Path.Combine(directory,"Q102-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".txt"); File.WriteAllText(path,_draft.Text,new System.Text.UTF8Encoding(false)); _status.Text="正文已导出："+path; });
            Button(flow,"明确核对行动意图",()=>OpenMeaning(false)); Button(flow,"使用结构化辅助继续",()=>OpenMeaning(true));
            Button(flow,"当前设备与执行预览",()=>Execute(async()=>{ if(!await SaveDraft()||!await SaveFindingDraft())throw new InvalidOperationException("当前正文未保存，执行预览尚未创建。"); await OpenDeparturePreview(); }));
            Button(flow,"局部写作检查",()=>
            {
                var advice=Mist.Application.Learning.LocalWritingFeedback.Inspect(_draft.Text);var feedback=Panel("写作证据",InputOwner.Workspace);
                Label(feedback,$"本稿约 {advice.WordCount} 个英文词。字数建议不限制世界行动；短功能写作尚未评估能力分数。",22);
                foreach(var item in advice.Advice)Button(feedback,item.Message,()=>{ClosePanel();_writer?.Select(item.StartLine,item.StartColumn,item.EndLine,item.EndColumn);_writer?.GrabFocus();});
                if(advice.Advice.Length==0)Label(feedback,"没有触发当前离线结构提示。这不表示语法、论证或任务表达已得到评价。",20);
            });
            _writer.GrabFocus();
        }
        catch(Exception error){_status.Text=error.Message;}
    }
    public override void _PhysicsProcess(double delta)
    {
        if(_draftDebounce<=0||_draft is null)return; _draftDebounce-=delta;
        if(_draftDebounce<=0&&!Composing())_=SaveDraft(); else if(_draftDebounce<=0)_draftDebounce=0.2;
    }
    private async Task<bool> SaveDraft()
    {
        if(_draft is null)return true;
        if(_saveTask is { IsCompleted:false }) { await _saveTask; if(_draft.IsSaved)return true; }
        _saveTask=SaveDraftCore();return await _saveTask;
    }
    private async Task<bool> SaveDraftCore()
    {
        if(_draft is null)return true; _savingDraft=true;
        try { var ack=await _draft.SaveAsync(); if(_saveStatus is not null&&IsInstanceValid(_saveStatus))_saveStatus.Text=_draft.IsSaved?"正文已保存 · 版本 "+ack.Revision:"当前修改还未保存；旧回执不会覆盖提示。"; return _draft.IsSaved; }
        catch(Exception error){if(_saveStatus is not null&&IsInstanceValid(_saveStatus))_saveStatus.Text="保存失败，正文仍可导出："+error.Message;return false;}
        finally{_savingDraft=false;}
    }
    private void OpenMeaning(bool assisted)
    {
        var flow=Panel(assisted?"结构化辅助 · 行动意图":"核对行动意图",InputOwner.Confirmation);
        Label(flow,assisted?"这些选择属于明确辅助，不计独立自由写作。选择只形成提议，不施工或离港。":"请按你写下的意图逐项确认。离线模式未自动理解正文；不按关键词替你修改路线。",22);
        var choices=new List<OptionButton>();
        var fieldValues=new[]{new[]{"","A","B","C"},new[]{"","closed","other"},new[]{"","draught_exceeds_limit","pump_alone_sufficient"},new[]{"","replace_bracket","replace_beacon"},new[]{"","charged_backup_battery","workshop_reserve"},new[]{"","keep_off_until_isolation_confirmed","start_now"},new[]{"","test_before_departure","test_not_required"}};
        foreach(var values in new[]{new[]{"尚未明确路线","航道 A","航道 B","航道 C"},new[]{"A的限制尚未明确","A 已关闭","其他限制"},new[]{"B的限制尚未明确","吃水超过限制","只开泵就足够"},new[]{"维修对象尚未明确","更换支架","更换整个灯体"},new[]{"供电尚未明确","船内已充电备用电池","工坊本地储备"},new[]{"主发电机尚未明确","确认电缆隔离前保持关闭","现在启动"},new[]{"测试安排尚未明确","离港前测试","无需测试"}})
        {
            var option=new OptionButton { CustomMinimumSize=new Vector2(0,46) }; foreach(string value in values)option.AddItem(value); choices.Add(option);flow.AddChild(option);
        }
        Button(flow,"确认这份意图",()=>Execute(async()=>
        {
            if(!await SaveDraft()||!await SaveFindingDraft())throw new InvalidOperationException("请先保存当前正文或导出；旧版本不能代表这次修改。"); string? V(int i)=>choices[i].Selected==0?null:fieldValues[i][choices[i].Selected];
            RouteChoice? route=V(0) is { } value?Enum.Parse<RouteChoice>(value):null;
            await _q.ConfirmMeaningAsync(new(route,V(1),V(2),V(3),V(4),V(5),choices[6].Selected==0?null:choices[6].Selected==1),assisted); _status.Text="行动意图已确认；设备和离港尚需分别执行。"; ClosePanel();
        }));
    }
    private async ValueTask OpenDeparturePreview()
    {
        var preview=await _q.PreviewDepartureAsync(); _previewToken=preview.Token;
        var flow=Panel("执行预览",InputOwner.Confirmation); Label(flow,"状态："+preview.Preview.Status,24);
        foreach(var issue in preview.Preview.Issues)Label(flow,issue.SafeMessage+" · "+issue.EntityId,20);
        if(preview.Preview.Status==Mist.Application.Commands.PreviewStatus.Ready)
        {
            Label(flow,"将提交民用渡轮撤离：玩家回到广场并留港，设备随船；奖励为160经验、60刻币、2零件和RE-01。微光号仍未移交。",22);
            Button(flow,"明确执行民用渡轮撤离",()=>Execute(async()=>{ var result=await _q.DepartAsync(preview.Token); if(result.Status is not(CommitStatus.Committed or CommitStatus.AlreadyCommitted))throw new InvalidOperationException("撤离未提交。"); _previewToken=null; CloseAllPanels(); _status.Text="民用渡轮已经撤离。世界结果已提交。"; await _store.CreateCheckpointAsync("automatic"); }));
        }
        else Label(flow,"这份预览不扣物品。补齐当前设备状态或澄清提议后，再次预览。",20);
    }
    private void OpenHelp()
    {
        var flow=Panel("分范围帮助",InputOwner.Workspace); Label(flow,"帮助不改变英语目标，不扣奖励或补给。可以分别查看操作、语言和当前逻辑。",22);
        Button(flow,"操作帮助",()=>Label(flow,"在场景中走近站位后再交互；本地控制器与港口主发电机属于不同设备。",22));
        Button(flow,"语言帮助",()=>{ _support.Add(new("language_help","Q102-W-PLAN","prompt",0,_catalog.Q102WritingPrompt.EnumerateRunes().Count())); Label(flow,"核对 must、only if、until 等条件表达的对象和范围。查词只说明选中词语。",22); });
        Button(flow,"当前逻辑帮助",()=>{ _support.Add(new("logic_hint","Q102-W-PLAN","prompt",0,_catalog.Q102WritingPrompt.EnumerateRunes().Count())); Label(flow,_view.Ferry.Ready?"当前灯已通过有效测试。核对现行航道资料和你的意图，并走到写作台预览。":_view.Snapshot.Items.Single(i=>i.Id==Q102Service.BracketId).OwnerKind=="warehouse"?"检修控制间有独立本地供电，可查看隔离或固定检查说明，从可用侧道取得支架并打开回程横闩。":"核对船内支架、备用电池连接和当前配置测试。帮助不会自动安装或签发证书。",22); });
    }
    private async ValueTask OpenLearning()
    {
        var flow=Panel("学习证据",InputOwner.Workspace); Label(flow,"IELTS Academic · R/L/W目标6.5 · Speaking未启用",24);
        Label(flow,"已有观察："+await _store.ObservationCountAsync()+"。观察数量和播放次数不是掌握度；当前没有经校准的能力分数。",22);
        foreach(var skill in _catalog.Definitions["language_skills"])
        {
            Button(flow,skill.Id+" · "+skill.Name+" · 证据不足",()=>
            {
                using var source=JsonDocument.Parse(skill.PayloadJson);var detail=Panel(skill.Id+" · "+skill.Name,InputOwner.Workspace);
                Label(detail,source.RootElement.GetProperty("observable").GetString()!,22);Label(detail,"可核查证据："+source.RootElement.GetProperty("acceptable_evidence").GetString(),20);
                Label(detail,"当前没有已评价的成功证据。接触材料和完成世界任务不会自动增加此技能状态。",20);
                foreach(string status in Mist.Core.Learning.EvidenceStateRules.Labels)Label(detail,status,18);
            });
        }
        Button(flow,"阅读与听力练习",()=>OpenArchive()); Button(flow,"功能写作练习",()=>Execute(OpenCurrentWritingWorkspace));
    }
}

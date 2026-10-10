using Godot;
using Mist.Application;
using System.Collections.Immutable;
using Mist.Application.Q102;
using Mist.Application.Drafts;
using Mist.Application.UI;
using Mist.Application.Inventory;
using Mist.Application.CombatSessions;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Infrastructure.Storage;

public partial class Root : Control
{
    private ProfileStore _store=null!;
    private FileStream? _clientProfileLock;
    private CampaignClient? _campaignClient;
    private Mist.Application.ChapterOne.ChapterOneService? _chapter;
    private Mist.Application.Campaign.CampaignService? _coastArchive;
    private DraftEditor? _findingDraft;
    private Mist.Application.VesselInterior.VesselInteriorService? _vessel;
    private Mist.Application.ChapterTwo.ChapterTwoService? _chapterTwo;
    private Mist.Application.Voyage.VoyageService? _voyage;
    private ImmutableDictionary<string,SceneGeometry>? _regionalGeometry;
    private ImmutableDictionary<string,SceneGeometry> ActiveGeometry=>_regionalGeometry??_voyage?.Geometry??_vessel?.Geometry??_chapter?.Geometry??_q.Geometry;
    private bool _closing;
    private RuntimeCatalog _catalog=null!;
    private Q102Service _q=null!;
    private Q102View _view=null!;
    private PartyService _party=null!;
    private EncounterService _encounters=null!;
    private EncounterState? _encounterView;
    private DraftEditor? _draft;
    private readonly InputRouting _routing=new();
    private readonly Stack<PanelContainer> _panels=new();
    private readonly Dictionary<string,Key> _keys=new() { ["up"]=Key.W,["down"]=Key.S,["left"]=Key.A,["right"]=Key.D,["interact"]=Key.E };
    private readonly HashSet<Key> _releaseBarrier=new();
    private readonly List<Point2> _trace=new();
    private List<Point2> _path=new();
    private WorldCanvas _world=null!;
    private Label _status=null!,_objective=null!,_details=null!,_location=null!,_sectionTitle=null!;
    private OptionButton _targets=null!;
    private readonly List<(string Id,Point2 Stand,bool Port)> _targetList=new();
    private string? _selected,_rebind;
    private Point2 _position;
    private bool _busy,_initialized;
    private int _scale=100;
    private bool _trackingQ102=true;
    private BoxContainer _split=null!;
    private ScrollContainer _sideScroll=null!;
    public override async void _Ready()
    {
        Theme = new Theme { DefaultFont = new SystemFont { FontNames = new[] { "Microsoft YaHei", "Segoe UI" } }, DefaultFontSize = 20 };
        GetTree().AutoAcceptQuit=false;
        StyleTheme();
        try
        {
            using var source=Godot.FileAccess.Open("res://content/runtime_catalog.json",Godot.FileAccess.ModeFlags.Read);
            if(source is null) throw new InvalidDataException("内容文件缺失。");
            _catalog=StrictJson.Deserialize<RuntimeCatalog>(source.GetAsText());
            bool internalSlice=false;
#if DEBUG
            internalSlice=OS.GetCmdlineUserArgs().Any(a=>a is "--internal-q102" or "--smoke" or "--client-test" or "--native-battle-candidate");
#endif
            string root=System.Environment.GetEnvironmentVariable("MISTSEA_PROFILE_ROOT")??ProjectSettings.GlobalizePath(internalSlice?"user://profiles/internal-q102":"user://profiles/main-campaign");
            Directory.CreateDirectory(root);
            try{_clientProfileLock=new FileStream(Path.Combine(root,"client.lock"),FileMode.OpenOrCreate,System.IO.FileAccess.ReadWrite,FileShare.None);}catch(IOException){throw new InvalidOperationException("该档案已在另一游戏窗口打开；关闭那个窗口后再打开此档案。");}
            _store=new ProfileStore(root,_catalog.ReleaseId);
            await MistAudioChannels.LoadAsync(_store);
            if(!internalSlice)
            {
                using var campaignSource=Godot.FileAccess.Open("res://content/campaign/first_entry.json",Godot.FileAccess.ModeFlags.Read);if(campaignSource is null)throw new InvalidDataException("正常序章内容包缺失，请修复内容安装；原档案保留。");
                var package=StrictJson.Deserialize<Mist.Core.Campaign.CampaignPackage>(campaignSource.GetAsText());var campaign=new Mist.Application.Campaign.CampaignService(_store,_catalog,package);await campaign.InitializeNewAsync();
                _campaignClient=new CampaignClient();_campaignClient.Configure(campaign,_store,_catalog,StartQ102Shell);AddChild(_campaignClient);GD.Print("MISTSEA_CLIENT_BOOT "+BuildIdentity.Version);
#if DEBUG
                if(OS.GetCmdlineUserArgs().Contains("--performance-candidate"))_ =RunPerformanceCandidate();
#endif
                return;
            }
            _q=new Q102Service(_store,_catalog); await _q.InitializeSliceAsync();
            _party=new PartyService(_store,_catalog);await _party.InitializeStarterCollectionAsync();
            _encounters=new EncounterService(_store,_catalog,id=>_store.CreateCheckpointAsync("protected",protectedId:id));
            await LoadPreferences(); BuildShell(); await Refresh(); _initialized=true;
            GD.Print("MISTSEA_CLIENT_BOOT "+BuildIdentity.Version);
            if(OS.GetCmdlineUserArgs().Contains("--smoke")) { GetTree().Quit(); return; }
#if DEBUG
            if(OS.GetCmdlineUserArgs().Contains("--client-test")) { await RunClientTests(); return; }
#endif
            _status.Text="港口灰盒 · 点击物件先走近，E或按钮交互。WASD/方向键移动。";
            _world.GrabFocus();
#if DEBUG
            if(OS.GetCmdlineUserArgs().Contains("--native-battle-candidate"))
            {
                var candidate=new Mist.DebugCandidates.Q102RouteCandidate(_store,_catalog);await candidate.Exit("R1-S05");await candidate.Walk(_q.Geometry["R1-S05"].Ports.Single(p=>p.TargetScene=="R1-S06").Stand);CheckCommit(await _encounters.BeginWorkshopAsync());await Refresh();await OpenBattle();
            }
#endif
        }
        catch(Exception error) { GD.PrintErr("MISTSEA_BOOT_ERROR "+error); Label(this,"无法打开当前档案\n"+error.Message+"\n原文件保留。请通过登记备份恢复，不能自动重置。",28); if(OS.GetCmdlineUserArgs().Any(a=>a is "--smoke" or "--client-test" or "--campaign-client-test" or "--vessel-client-test" or "--chapter-two-client-test" or "--chapter-three-client-test" or "--chapter-four-local-client-test" or "--chapter-four-client-test" or "--chapter-five-client-test" or "--chapter-six-client-test" or "--scoped-personal-client-test" or "--old-city-local-life-client-test" or "--r3-shift-client-test" or "--lina-personal-client-test" or "--vessel-life-client-test" or "--settlement-support-client-test" or "--optional-r1-client-test" or "--optional-r2-client-test" or "--optional-r3-client-test" or "--optional-r4-client-test" or "--optional-r5-client-test" or "--optional-r6-client-test" or "--field-relay-client-test" or "--finale-client-test" or "--r2-life-client-test" or "--first-relay-client-test" or "--first-side-client-test" or "--first-outer-client-test" or "--r1-life-client-test" or "--musicbox-client-test" or "--release-probe")) GetTree().Quit(5); }
    }
    private async Task StartQ102Shell()
    {
        _q=new Q102Service(_store,_catalog);_party=new PartyService(_store,_catalog);_encounters=new EncounterService(_store,_catalog,id=>_store.CreateCheckpointAsync("protected",protectedId:id));
        using var chapterSource=Godot.FileAccess.Open("res://content/chapter_one/chapter_one.json",Godot.FileAccess.ModeFlags.Read);using var coastSource=Godot.FileAccess.Open("res://content/campaign/first_entry.json",Godot.FileAccess.ModeFlags.Read);
        if(chapterSource is null||coastSource is null)throw new InvalidDataException("首章现场内容缺失；当前档案保留，请修复内容安装。");
        var coast=new Mist.Application.Campaign.CampaignService(_store,_catalog,StrictJson.Deserialize<Mist.Core.Campaign.CampaignPackage>(coastSource.GetAsText()));
        _coastArchive=coast;
        _chapter=new(_store,_catalog,StrictJson.Deserialize<Mist.Core.ChapterOne.ChapterOnePackage>(chapterSource.GetAsText()),coast.Geometry,id=>_store.CreateCheckpointAsync("protected",protectedId:id));
        using var vesselSource=Godot.FileAccess.Open("res://content/vessel/vessel_interior.json",Godot.FileAccess.ModeFlags.Read);
        using var chapterTwoSource=Godot.FileAccess.Open("res://content/chapter_two/learner_package.json",Godot.FileAccess.ModeFlags.Read);
        using var voyageSource=Godot.FileAccess.Open("res://content/voyage/r1_r2_voyage.json",Godot.FileAccess.ModeFlags.Read);
        if(vesselSource is null||chapterTwoSource is null||voyageSource is null)throw new InvalidDataException("船舱或第二章内容文件缺失；当前档案保留，请修复内容安装。");
        _chapterTwo=new(_store,_catalog,Mist.Core.ChapterTwo.ChapterTwoPackage.ParseJson(chapterTwoSource.GetAsText()));
        LoadR2Life();
        LoadR1Life(_chapter.Geometry);
        LoadRegionalChapters();
        InitializeOptionalLocalWorks();
        LoadFirstApproaches(MergeRegionalGeometry(_r1Life!.Geometry.SetItems(_chapterTwo.Geometry)));
        _vessel=new(_store,_catalog,StrictJson.Deserialize<Mist.Core.VesselInterior.VesselInteriorPackage>(vesselSource.GetAsText()),_firstApproaches!.Geometry);
        _voyage=new(_store,_catalog,StrictJson.Deserialize<Mist.Core.Voyage.VoyagePackage>(voyageSource.GetAsText()),_chapterTwo.Package,_vessel.Geometry);
        _finaleLaunch=new(_store,_catalog,_vessel.Package,_finale!.Package,_store);
        LoadRegionalVoyages();
        LoadOptionalEncounterContracts(MergeRegionalGeometry(_firstApproaches!.Geometry));
        RegisterRegionalLearningScope("current-permissions",ReadGrantedLearningSources);
        await LoadPreferences();BuildShell();await Refresh();_initialized=true;
        _campaignClient?.SetProcess(false);_campaignClient?.QueueFree();_campaignClient=null;_status.Text=HasReceipt(_view.Snapshot,"Q-104")?"微光号移交与已有进度已恢复。走近当前工位后交互；WASD/方向键移动。":HasReceipt(_view.Snapshot,"Q-102")?"渡轮撤离进度已恢复。继续核查原始事故记录与潮门塔现场。":"真实入港委托已签发。继续民用渡轮检修；WASD/方向键移动，E交互。";_world.GrabFocus();
#if DEBUG
        if(OS.GetCmdlineUserArgs().Contains("--scoped-personal-client-test"))await RunScopedPersonalClientTests();
        if(OS.GetCmdlineUserArgs().Contains("--old-city-local-life-client-test"))await RunOldCityLocalLifeClientTests();
        if(OS.GetCmdlineUserArgs().Contains("--r3-shift-client-test"))await RunR3ShiftWorkClientTests();
        if(OS.GetCmdlineUserArgs().Contains("--lina-personal-client-test"))await RunLinaPersonalWorkClientTests();
        if(OS.GetCmdlineUserArgs().Contains("--vessel-life-client-test"))await RunVesselLifeClientTests();
        if(OS.GetCmdlineUserArgs().Contains("--settlement-support-client-test"))await RunSettlementSupportClientTests();
        if(OS.GetCmdlineUserArgs().Any(a=>a.StartsWith("--optional-r",StringComparison.Ordinal)&&a.EndsWith("-client-test",StringComparison.Ordinal)))await RunOptionalEncounterClientTests();
        if(OS.GetCmdlineUserArgs().Contains("--musicbox-client-test"))await RunMusicBoxClientTests();
        if(OS.GetCmdlineUserArgs().Contains("--r2-life-client-test"))await RunR2LifeClientTests();
        if(OS.GetCmdlineUserArgs().Any(a=>a is "--first-side-client-test" or "--first-outer-client-test"))await RunFirstTowerClientTests();
        if(OS.GetCmdlineUserArgs().Contains("--r1-life-client-test"))await RunR1LifeClientTests();
        if(OS.GetCmdlineUserArgs().Contains("--vessel-client-test"))await RunVesselClientTests();
        if(OS.GetCmdlineUserArgs().Contains("--chapter-two-client-test"))await RunChapterTwoClientTests();
        if(OS.GetCmdlineUserArgs().Contains("--chapter-three-client-test"))await RunChapterThreeClientTests();
        if(OS.GetCmdlineUserArgs().Contains("--chapter-four-local-client-test"))await RunChapterFourLocalClientTests();
        if(OS.GetCmdlineUserArgs().Contains("--chapter-four-client-test"))await RunChapterFourClientTests();
        if(OS.GetCmdlineUserArgs().Contains("--chapter-five-client-test"))await RunChapterFiveClientTests();
        if(OS.GetCmdlineUserArgs().Contains("--chapter-six-client-test"))await RunChapterSixClientTests();
        if(OS.GetCmdlineUserArgs().Contains("--field-relay-client-test"))await RunFieldRelayClientTests();
        if(OS.GetCmdlineUserArgs().Contains("--finale-client-test"))await RunFinaleClientTests();
#endif
    }
    public override void _Notification(int what){if(what==NotificationWMCloseRequest)_=SaveThenQuit();if(what==NotificationApplicationFocusOut)PauseContentAudio("应用已切到后台");}
    private async Task SaveThenQuit()
    {
        if(_closing)return;_closing=true;
        try
        {
            while(_busy)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(_campaignClient is null&&_trace.Count>0)await FlushMovement();
            bool saved=_campaignClient is not null?await _campaignClient.SaveBeforeClose():await SaveAdventureDrafts();
            if(saved)GetTree().Quit();else if(_status is not null)_status.Text="正文尚未可靠保存，窗口保留。请重试保存或导出当前正文。";
        }
        finally{_closing=false;}
    }
    private async ValueTask<bool> SaveAdventureDrafts()=>await SaveOldCityLifeDrafts()&&await SaveR3ShiftDraft()&&await PauseVesselLifeAsync()&&await SaveLinaLetter()&&await SaveSettlementDrafts()&&await SaveDraft()&&await SaveFindingDraft()&&await SaveChapterTwoDraft()&&await SaveChapterThreeDraft()&&await SaveChapterFourDraft()&&await SaveChapterFiveDraft()&&await SaveChapterSixDraft()&&await SaveFinaleDraft()&&await SaveR1LifeDraft()&&await SaveOptionalLocalWorksDraft()&&await SaveR2LifeDraft()&&await SaveSideWellObservationDraft();
    private void StyleTheme()
    {
        var normal=new StyleBoxFlat { BgColor=new Color("223b49"),BorderColor=new Color("64818c"),BorderWidthBottom=1,BorderWidthTop=1,BorderWidthLeft=1,BorderWidthRight=1,CornerRadiusBottomLeft=5,CornerRadiusBottomRight=5,CornerRadiusTopLeft=5,CornerRadiusTopRight=5,ContentMarginLeft=14,ContentMarginRight=14,ContentMarginTop=10,ContentMarginBottom=10 };
        Theme.SetStylebox("normal","Button",normal); var hover=(StyleBoxFlat)normal.Duplicate(); hover.BgColor=new Color("315768"); Theme.SetStylebox("hover","Button",hover);
        var focus=(StyleBoxFlat)normal.Duplicate(); focus.BorderColor=new Color("e4c887"); focus.BorderWidthBottom=3; Theme.SetStylebox("focus","Button",focus);
        Theme.SetColor("font_color","Label",new Color("d8e3e6")); Theme.SetColor("font_color","Button",new Color("eff4ef"));
    }
    private void BuildShell()
    {
        var margin=new MarginContainer(); margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); foreach(string edge in new[]{"left","right","top","bottom"}) margin.AddThemeConstantOverride("margin_"+edge,16); AddChild(margin);
        var wholeScroll=new ScrollContainer { HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,FollowFocus=true }; margin.AddChild(wholeScroll);
        var layout=new VBoxContainer { SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill }; layout.AddThemeConstantOverride("separation",12); wholeScroll.AddChild(layout);
        var header=new HFlowContainer(); layout.AddChild(header); Label(header,"雾海档案",26).CustomMinimumSize=new Vector2(220,46);
        foreach(string name in new[]{"地图","任务","档案","队伍","学习","系统"}) Button(header,name,()=>OpenNavigation(name));
        _location=Label(layout,"",18);
        _split=new BoxContainer { SizeFlagsVertical=SizeFlags.ExpandFill }; _split.AddThemeConstantOverride("separation",16); layout.AddChild(_split);
        _world=new WorldCanvas { SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill,CustomMinimumSize=new Vector2(400,300),SpotLabel=ReadableSpotName }; _split.AddChild(_world); _world.Clicked=ClickWorld;
        _sideScroll=new ScrollContainer { CustomMinimumSize=new Vector2(310,0),HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,FollowFocus=true,SizeFlagsVertical=SizeFlags.ExpandFill }; _split.AddChild(_sideScroll);
        var side=new VBoxContainer { SizeFlagsHorizontal=SizeFlags.ExpandFill }; side.AddThemeConstantOverride("separation",12); _sideScroll.AddChild(side);
        _sectionTitle=Label(side,"最后一班渡轮",24); _objective=Label(side,"",20);
        Label(side,"出口与操作站位",18); _targets=new OptionButton { FitToLongestItem=false,ClipText=true,CustomMinimumSize=new Vector2(280,48),AccessibilityName="选择要走近的出口或物件" }; side.AddChild(_targets);
        _targets.ItemSelected+=index=>SelectTarget((int)index); Button(side,"走近选定站位",ApproachSelected); Button(side,"交互 / E",InteractSelected);
        _details=Label(side,"",18); Button(side,"继续当前战斗",()=>Execute(async()=>await OpenBattle())); Button(side,"编辑当前工作正文",()=>Execute(OpenCurrentWritingWorkspace)); Button(side,"调查工作台",OpenPuzzleWork); Button(side,"当前状态帮助",()=>OpenHelp());
        _status=Label(layout,"",18); _status.CustomMinimumSize=new Vector2(0,46);
        ReflowShell();
    }
    private void ReflowShell()
    {
        if(_split is null)return;
        _split.Vertical=_scale>=150;
        _world.CustomMinimumSize=_scale>=150?new(0,240):new(400,300);
        _sideScroll.CustomMinimumSize=_scale>=150?new(0,200):new(310,0);
        _sideScroll.SizeFlagsHorizontal=_scale>=150?SizeFlags.ExpandFill:SizeFlags.Fill;
    }
    public Label Label(Node parent,string text,int size=20)
    {
        var label=new Label { Text=text,AutowrapMode=TextServer.AutowrapMode.WordSmart,SizeFlagsHorizontal=SizeFlags.ExpandFill }; label.SetMeta("base_font_size",size); label.AddThemeFontSizeOverride("font_size",size*_scale/100); parent.AddChild(label); return label;
    }
    public static Button Button(Node parent,string text,Action action)
    {
        Node? ancestor=parent;while(ancestor is not null&&ancestor is not Root)ancestor=ancestor.GetParent();int fontSize=ancestor is Root root?20*root._scale/100:20;
        var button=new Button { Text=text,AutowrapMode=TextServer.AutowrapMode.WordSmart,CustomMinimumSize=new Vector2(120,46),AccessibilityName=text };button.AddThemeFontSizeOverride("font_size",fontSize); parent.AddChild(button); button.Pressed+=action; return button;
    }
    private async Task Refresh()
    {
        if(_coastArchive is not null)
        {
            var snapshot=await _store.SnapshotAsync();var campaign=snapshot.Entities.SingleOrDefault(e=>e.Id=="campaign");
            if(campaign is not null&&!StrictJson.Deserialize<Mist.Core.Campaign.CampaignState>(campaign.Json).Q102Ready)
            {
                _initialized=false;_path.Clear();_trace.Clear();CloseAllPanels();ResetRegionalUiState();ReleaseAudio(ref _lessonAudio);ReleaseAudio(ref _lookupAudio);foreach(var child in GetChildren())child.QueueFree();_writer=null;_chapter=null;_vessel=null;_chapterTwo=null;_voyage=null;_regionalGeometry=null;
                _campaignClient=new CampaignClient();_campaignClient.Configure(_coastArchive,_store,_catalog,StartQ102Shell);AddChild(_campaignClient);return;
            }
        }
        _view=await _q.ViewAsync(); _position=_view.Actor.Position; _trace.Clear();
        if(_r1Life is not null&&_view.Actor.Scene.StartsWith("R1-",StringComparison.Ordinal)&&!_view.Snapshot.Entities.Any(e=>e.Id==Mist.Application.R1Life.R1LifeService.StateId)&&_view.Snapshot.Receipts.Any(r=>r.SemanticKey=="Q-101:resolution"))
        {
            CheckCommit(await _r1Life.InitializeRegionAsync());_view=await _q.ViewAsync();
        }
        if(await TryInitializeR2Life())_view=await _q.ViewAsync();
        _regionalGeometry=_voyage?.Geometry;
        if(_r1Life is not null)_regionalGeometry=(_regionalGeometry??_vessel!.Geometry).SetItems(_r1Life.Geometry);
        if(_chapterTwo is not null&&_view.Snapshot.Entities.Any(e=>e.Id==Mist.Application.ChapterTwo.ChapterTwoService.StateId))_regionalGeometry=(_regionalGeometry??_vessel!.Geometry).SetItems(_chapterTwo.CurrentGeometry(_view.Snapshot));
        if(_chapterThree is not null&&_view.Snapshot.Entities.Any(e=>e.Id==Mist.Application.ChapterThree.ChapterThreeService.StateId))_regionalGeometry=(_regionalGeometry??_vessel!.Geometry).SetItems(_chapterThree.CurrentGeometry(_view.Snapshot));
        if(_chapterFour is not null&&_view.Snapshot.Entities.Any(e=>e.Id==Mist.Application.ChapterFour.ChapterFourService.StateId))_regionalGeometry=(_regionalGeometry??_vessel!.Geometry).SetItems(_chapterFour.CurrentGeometry(_view.Snapshot));
        if(_chapterFive is not null&&_view.Snapshot.Entities.Any(e=>e.Id==Mist.Application.ChapterFive.ChapterFiveService.StateId))_regionalGeometry=(_regionalGeometry??_vessel!.Geometry).SetItems(_chapterFive.CurrentGeometry(_view.Snapshot));
        if(_chapterSix is not null&&_view.Snapshot.Entities.Any(e=>e.Id=="chapter6"))_regionalGeometry=(_regionalGeometry??_vessel!.Geometry).SetItems(_chapterSix.CurrentGeometry(_view.Snapshot));
        if(_finale is not null&&_view.Snapshot.Entities.Any(e=>e.Id=="finale"))_regionalGeometry=(_regionalGeometry??_vessel!.Geometry).SetItems(_finale.CurrentGeometry(_view.Snapshot));
        var activeSix=_view.Snapshot.Entities.Where(e=>e.Kind=="combat"&&e.Id.StartsWith("combat:ENC-R6-",StringComparison.Ordinal)&&!Mist.Core.OptionalEncounterContracts.OptionalContractRules.Ids.Any(id=>e.Id=="combat:"+id)).Select(e=>StrictJson.Deserialize<Mist.Core.ChapterSix.FrontierEncounterState>(e.Json)).FirstOrDefault(e=>e.Battle.Outcome is Mist.Core.Combat.CombatOutcome.Active or Mist.Core.Combat.CombatOutcome.Defeat);
        if(activeSix is not null)_chapterSixSelectedEncounter=activeSix.Encounter;else if(!_view.Actor.Scene.StartsWith("R6-",StringComparison.Ordinal))_chapterSixSelectedEncounter=null;
        var activeFinale=_view.Snapshot.Entities.Where(e=>e.Kind=="combat"&&e.Id.StartsWith("combat:ENC-RF-",StringComparison.Ordinal)).Select(e=>StrictJson.Deserialize<Mist.Core.Finale.FinaleEncounterState>(e.Json)).FirstOrDefault(e=>e.Battle.Outcome is Mist.Core.Combat.CombatOutcome.Active or Mist.Core.Combat.CombatOutcome.Defeat);
        if(activeFinale is not null)_finaleSelectedEncounter=activeFinale.Encounter;else if(!_view.Actor.Scene.StartsWith("RF-",StringComparison.Ordinal))_finaleSelectedEncounter=null;
        var activeRegional=_view.Snapshot.Entities.Where(e=>e.Kind=="combat"&&e.Id.StartsWith("combat:ENC-R2-",StringComparison.Ordinal)&&!Mist.Core.OptionalEncounterContracts.OptionalContractRules.Ids.Any(id=>e.Id=="combat:"+id)).Select(e=>StrictJson.Deserialize<Mist.Application.ChapterTwo.ChapterEncounterState>(e.Json)).FirstOrDefault(e=>e.Battle.Outcome is Mist.Core.Combat.CombatOutcome.Active or Mist.Core.Combat.CombatOutcome.Defeat);
        if(activeRegional is not null)_chapterTwoSelectedEncounter=activeRegional.Encounter;else if(!_view.Actor.Scene.StartsWith("R2-",StringComparison.Ordinal))_chapterTwoSelectedEncounter=null;
        var activeFour=_view.Snapshot.Entities.Where(e=>e.Kind=="combat"&&e.Id.StartsWith("combat:ENC-R4-",StringComparison.Ordinal)&&!Mist.Core.OptionalEncounterContracts.OptionalContractRules.Ids.Any(id=>e.Id=="combat:"+id)).Select(e=>StrictJson.Deserialize<Mist.Application.ChapterFour.GlasshavenEncounterState>(e.Json)).FirstOrDefault(e=>e.Battle.Outcome is Mist.Core.Combat.CombatOutcome.Active or Mist.Core.Combat.CombatOutcome.Defeat);
        if(activeFour is not null)_chapterFourSelectedEncounter=activeFour.Encounter;else if(!_view.Actor.Scene.StartsWith("R4-",StringComparison.Ordinal))_chapterFourSelectedEncounter=null;
        var activeFive=_view.Snapshot.Entities.Where(e=>e.Kind=="combat"&&e.Id.StartsWith("combat:ENC-R5-",StringComparison.Ordinal)&&!Mist.Core.OptionalEncounterContracts.OptionalContractRules.Ids.Any(id=>e.Id=="combat:"+id)).Select(e=>StrictJson.Deserialize<Mist.Application.ChapterFive.OldCityEncounterState>(e.Json)).FirstOrDefault(e=>e.Battle.Outcome is Mist.Core.Combat.CombatOutcome.Active or Mist.Core.Combat.CombatOutcome.Defeat);
        if(activeFive is not null)_chapterFiveSelectedEncounter=activeFive.Encounter;else if(!_view.Actor.Scene.StartsWith("R5-",StringComparison.Ordinal))_chapterFiveSelectedEncounter=null;
        var activeThree=_view.Snapshot.Entities.Where(e=>e.Kind=="combat"&&e.Id.StartsWith("combat:ENC-R3-",StringComparison.Ordinal)&&!Mist.Core.OptionalEncounterContracts.OptionalContractRules.Ids.Any(id=>e.Id=="combat:"+id)).Select(e=>StrictJson.Deserialize<Mist.Application.ChapterThree.MiningEncounterState>(e.Json)).FirstOrDefault(e=>e.Battle.Outcome is Mist.Core.Combat.CombatOutcome.Active or Mist.Core.Combat.CombatOutcome.Defeat);
        if(activeThree is not null)_chapterThreeSelectedEncounter=activeThree.Encounter;else if(!_view.Actor.Scene.StartsWith("R3-",StringComparison.Ordinal))_chapterThreeSelectedEncounter=null;
        if(_firstApproaches is not null&&_regionalGeometry is not null&&_firstApproaches.Geometry.TryGetValue("R1-S09",out var firstBase))_regionalGeometry=_regionalGeometry.SetItem("R1-S09",firstBase);
        if(_regionalGeometry is not null)_regionalGeometry=ExtendOptionalEncounterGeometry(_regionalGeometry);
        await RestoreFirstApproachSelection();
        _encounterView=await CurrentEncounterAsync();
        var geometry=ActiveGeometry[_view.Actor.Scene]; _world.Geometry=geometry; _world.Player=_position; _world.FrontSafe=_view.Actor.Scene=="R1-S06"&&_view.Workshop.FrontSafe;
        _world.SceneTitle=_catalog.Scenes.Single(s=>s.Id==geometry.Id).Name; _world.QueueRedraw();
        _location.Text=$"{(_view.Actor.Scene.StartsWith("HUB-",StringComparison.Ordinal)?"微光号":_view.Actor.Scene.StartsWith("R2-",StringComparison.Ordinal)?"苔灯修院":"港口篇")} · {_world.SceneTitle}  /  内部灰盒候选";
        _objective.Text=_view.Quest.Lifecycle==Mist.Core.Quests.QuestLifecycle.Closed?"民用渡轮已撤离，玩家留港。微光号尚未移交。":_view.Quest.Lifecycle==Mist.Core.Quests.QuestLifecycle.Unaccepted?"先与哈文交谈，接受授权检修委托。":"为民用渡轮准备可用导航灯，并确认撤离方案。";
        if(_chapter is not null&&_view.Quest.Lifecycle==Mist.Core.Quests.QuestLifecycle.Closed)
        {
            bool q103=_view.Snapshot.Receipts.Any(r=>r.SemanticKey=="Q-103:resolution"),q104=_view.Snapshot.Receipts.Any(r=>r.SemanticKey=="Q-104:resolution");
            _objective.Text=q104?(_view.Actor.Scene.StartsWith("HUB-",StringComparison.Ordinal)?"微光号船舱：在档案室航图工位核对航路与公开泊位，再明确确认起航。甲板登离船口返回当前泊位。":"微光号已实际移交。在接应泊位登记三个空间后登船；档案室航图提供跨区航路核对。"):q103?"核查结论已提交。取得潮门塔现场许可，处置两阀与隔离后回港办理船务移交。":"玩家留港。向哈文取得原始记录调阅许可，核对事故原因与证据边界。";
        }
        _objective.Visible=_trackingQ102;
        _sectionTitle.Text=_view.Actor.Scene.StartsWith("HUB-",StringComparison.Ordinal)?"微光号船务":HasReceipt(_view.Snapshot,"Q-104")?"巡灯船与跨区核查":HasReceipt(_view.Snapshot,"Q-103")?"潮门塔现场":HasReceipt(_view.Snapshot,"Q-102")?"事故原始记录核查":"最后一班渡轮";
        _details.Text=$"工坊：{_view.Workshop.Mode}\n支架：{(_view.Ferry.BracketInstalled?"已安装":"未安装")}\n船内电池：{(_view.Ferry.BatteryConnected?"已连接":"未连接")}\n导航灯：{(_view.Ferry.Ready?"当前测试有效":"尚未通过当前测试")}";
        if(_view.Actor.Scene.StartsWith("HUB-",StringComparison.Ordinal))_details.Text="当前空间属于同一微光号。工位须实际走近后使用；草稿和学习帮助也可从导航入口访问。";
        else if(HasReceipt(_view.Snapshot,"Q-102"))_details.Text="民用渡轮撤离：已完成\n事故核查："+(HasReceipt(_view.Snapshot,"Q-103")?"已提交":"待核验")+"\n微光号移交："+(HasReceipt(_view.Snapshot,"Q-104")?"已完成":"待现场复核");
        if(_view.Actor.Scene.StartsWith("R2-",StringComparison.Ordinal)&&_chapterTwo is not null)
        {
            _sectionTitle.Text="苔灯修院核查";
            _objective.Text=HasReceipt(_view.Snapshot,"Q-204")?"修院事实交接已完成，矿区批次方向已登记。可继续个人工作与回访。":HasReceipt(_view.Snapshot,"Q-202")&&HasReceipt(_view.Snapshot,"Q-203")?"核对历史签名与矿区批次，明确本次公开或限阅交接范围。":HasReceipt(_view.Snapshot,"Q-201")?"转移记录核查与地方站安全可并行。材料、现场操作和提交各自登记。":"办理实际登陆与接待，走访三组住区，对照分类名册与当前观察。";
            var stateEntity=_view.Snapshot.Entities.SingleOrDefault(e=>e.Id==Mist.Application.ChapterTwo.ChapterTwoService.StateId);var st=stateEntity is null?null:StrictJson.Deserialize<Mist.Core.ChapterTwo.ChapterTwoState>(stateEntity.Json);
            _details.Text=st is null?"本地登记尚未建立。":"本地登陆："+(st.Landing?"已登记":"待办理")+"\n上层通路："+(st.UpperSafe?"当前复核有效":"待现场处置")+"\n备用配置："+(st.LocalStable?"当前测试有效":"待核验")+"\n导师当前状态：未知";
        }
        RefreshRegionalHud();
        _targetList.Clear(); _targets.Clear();
        foreach(var port in geometry.Ports) { _targetList.Add((port.Id,port.Stand,true)); string name=_catalog.Scenes.FirstOrDefault(s=>s.Id==port.TargetScene)?.Name??port.TargetScene; _targets.AddItem("出口 · "+name); }
        foreach(var spot in geometry.Spots) { _targetList.Add((spot.Id,spot.Stand,false)); _targets.AddItem(ReadableSpotName(spot)); }
        int selected=_targetList.FindIndex(t=>t.Id==_selected); if(selected>=0) _targets.Select(selected); else { _selected=null; _targets.Select(-1); }
        _world.Selected=_selected;
    }
    private bool Composing()=>DisplayServer.HasFeature(DisplayServer.Feature.Ime)&&DisplayServer.ImeGetText().Length>0;
    private bool MayMove()=>_initialized&&!_closing&&!_busy&&_encounterView?.Battle.Outcome is not(Mist.Core.Combat.CombatOutcome.Active or Mist.Core.Combat.CombatOutcome.Defeat)&&_routing.MayExecuteWorld(GetViewport().GuiGetFocusOwner() is TextEdit or LineEdit,Composing(),GetViewport().GuiGetFocusOwner() is not null&&GetViewport().GuiGetFocusOwner()!=_world,false);
    public override void _Process(double delta)
    {
        PollAudioDevice(delta);
#if DEBUG
        CapturePerformanceFrame();
#endif
        if(!MayMove()) return;
        Point2? next=null;
        if(_path.Count>0)
        {
            var p=_path[0]; double distance=_position.Distance(p),step=Math.Min(distance,25*delta);
            next=distance<0.02?p:new(_position.X+(p.X-_position.X)*step/distance,_position.Y+(p.Y-_position.Y)*step/distance);
            if(distance<=step+0.02) _path.RemoveAt(0);
        }
        else
        {
            bool Press(string action,Key alternate)=>!_releaseBarrier.Contains(_keys[action])&&(Input.IsPhysicalKeyPressed(_keys[action])||Input.IsPhysicalKeyPressed(alternate));
            int x=(Press("right",Key.Right)?1:0)-(Press("left",Key.Left)?1:0),y=(Press("down",Key.Down)?1:0)-(Press("up",Key.Up)?1:0);
            if(x!=0||y!=0) { double divisor=Math.Sqrt(x*x+y*y); next=new(_position.X+x/divisor*25*delta,_position.Y+y/divisor*25*delta); _world.Facing=Math.Abs(x)>=Math.Abs(y)?x>0?1:3:y>0?2:0; }
        }
        if(next is { } point&&new NavigationGrid(_world.Geometry!).SegmentClear(_position,point,!(_view.Actor.Scene=="R1-S06"&&_view.Workshop.FrontSafe))) { _position=point; _trace.Add(point); _world.Player=point; _world.QueueRedraw(); }
        if(_path.Count==0&&_trace.Count>0&&!Input.IsPhysicalKeyPressed(_keys["up"])&&!Input.IsPhysicalKeyPressed(_keys["down"])&&!Input.IsPhysicalKeyPressed(_keys["left"])&&!Input.IsPhysicalKeyPressed(_keys["right"])&&!Input.IsPhysicalKeyPressed(Key.Left)&&!Input.IsPhysicalKeyPressed(Key.Right)&&!Input.IsPhysicalKeyPressed(Key.Up)&&!Input.IsPhysicalKeyPressed(Key.Down)) _=FlushMovement();
    }
    private async Task FlushMovement()
    {
        if(_busy||_trace.Count==0) return; _busy=true;
        try { if(_vessel is not null&&_view.Actor.Scene.StartsWith("HUB-",StringComparison.Ordinal))CheckCommit(await _vessel.MoveAsync(_trace.ToImmutableArray()));else if(_chapterTwo is not null&&_view.Actor.Scene.StartsWith("R2-",StringComparison.Ordinal))await _chapterTwo.TraverseAsync(_trace.ToImmutableArray());else if(_chapterThree is not null&&_view.Actor.Scene.StartsWith("R3-",StringComparison.Ordinal))await _chapterThree.TraverseAsync(_trace.ToImmutableArray());else if(_chapterFour is not null&&_view.Actor.Scene.StartsWith("R4-",StringComparison.Ordinal))await _chapterFour.TraverseAsync(_trace.ToImmutableArray());else if(_finale is not null&&_view.Actor.Scene.StartsWith("RF-",StringComparison.Ordinal))await _finale.TraverseAsync(_trace.ToImmutableArray());else if(_chapterSix is not null&&_view.Actor.Scene.StartsWith("R6-",StringComparison.Ordinal))await _chapterSix.TraverseAsync(_trace.ToImmutableArray());else if(_chapterFive is not null&&_view.Actor.Scene.StartsWith("R5-",StringComparison.Ordinal))await _chapterFive.TraverseAsync(_trace.ToImmutableArray());else if(_r1Life is not null&&_view.Actor.Scene is "R1-O01" or "R1-O02" or "R1-S02")await _r1Life.TraverseAsync(_trace.ToImmutableArray());else if(_chapter is null)await _q.TraverseAsync(_trace.ToImmutableArray());else await _chapter.MoveAsync(_trace.ToImmutableArray());await Refresh(); }
        catch(Exception error) { _status.Text=error.Message; await Refresh(); }
        finally { _busy=false; }
    }
    private void ClickWorld(Point2 point)
    {
        if(!MayMove()) return;
        var nearest=_targetList.OrderBy(t=>t.Stand.Distance(point)).FirstOrDefault();
        if(nearest.Id is not null&&nearest.Stand.Distance(point)<5) { _selected=nearest.Id; _targets.Select(_targetList.FindIndex(t=>t.Id==nearest.Id)); point=nearest.Stand; }
        _path=new NavigationGrid(_world.Geometry!).FindPath(_position,point,!(_view.Actor.Scene=="R1-S06"&&_view.Workshop.FrontSafe)).ToList();
        if(_path.Count==0) _status.Text="站位不可达：墙体、边界或当前扫描区阻挡。";
        _world.Selected=_selected; _world.QueueRedraw();
    }
    private void SelectTarget(int index) { if(index<0||index>=_targetList.Count)return; _selected=_targetList[index].Id; _world.Selected=_selected; _world.QueueRedraw(); }
    private void ApproachSelected() { if(_selected is null)return; _world.GrabFocus(); ClickWorld(_targetList.Single(t=>t.Id==_selected).Stand); }
    public override void _Input(InputEvent input)
    {
        if(input is InputEventKey { Pressed:false } up) _releaseBarrier.Remove(up.Keycode);
        if(input is InputEventKey { Pressed:true,Echo:false } key&&_rebind is not null) { _keys[_rebind]=key.PhysicalKeycode; _rebind=null; _status.Text="按键已更新。"; _=SavePreferences(); GetViewport().SetInputAsHandled(); return; }
        if(input is InputEventKey { Pressed:true,Keycode:Key.Tab } tab&&GetViewport().GuiGetFocusOwner() is TextEdit edit&&!Composing()) { if(tab.ShiftPressed) edit.FindPrevValidFocus()?.GrabFocus(); else edit.FindNextValidFocus()?.GrabFocus(); GetViewport().SetInputAsHandled(); }
    }
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if(input is not InputEventKey { Pressed:true,Echo:false } key||Composing()||_releaseBarrier.Contains(key.Keycode)) return;
        if(key.Keycode==Key.Escape&&_panels.Count>0&&GetViewport().GuiGetFocusOwner() is not TextEdit) { Execute(async()=>{if(!await SaveAdventureDrafts())throw new InvalidOperationException("当前正文尚未保存，请完成输入法候选、重试或导出；窗口保留。");ClosePanel();}); GetViewport().SetInputAsHandled(); return; }
        if(!MayMove()) return;
        if(key.PhysicalKeycode==_keys["interact"]||key.Keycode==Key.Enter) { InteractSelected(); GetViewport().SetInputAsHandled(); }
    }
    private async void Execute(Func<ValueTask> action)
    {
        if(_busy||_closing)return; await FlushMovement(); _path.Clear(); _busy=true;
        try { await action(); await Refresh(); }
        catch(Exception error) { _status.Text=error.Message; }
        finally { _busy=false; }
    }
    public override void _ExitTree() { ReleaseAudio(ref _lessonAudio);ReleaseAudio(ref _lookupAudio);if(_store is not null) _store.Dispose();_clientProfileLock?.Dispose(); }
}

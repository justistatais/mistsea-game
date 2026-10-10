using Godot;
using System.Collections.Immutable;
using Mist.Application.Campaign;
using Mist.Application.Contracts;
using Mist.Application.Drafts;
using Mist.Application.Inventory;
using Mist.Core.Campaign;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Exploration;
using Mist.Infrastructure.Storage;

public partial class CampaignClient : Control
{
    private CampaignService _service=null!;private ProfileStore _store=null!;private Mist.Core.Content.RuntimeCatalog _catalog=null!;private Func<Task> _handoff=null!;
    private CampaignView _view=null!;private WorldCanvas _world=null!;private Label _status=null!,_objective=null!,_location=null!;private OptionButton _targets=null!;private BoxContainer _split=null!;
    private readonly List<(string Id,Point2 Stand,bool Port)> _targetList=[];private readonly Stack<(PanelContainer Panel,Control? Focus,Action? Close)> _panels=new();
    private readonly Dictionary<string,DraftEditor> _drafts=new();private readonly HashSet<Key> _release=[];private Point2 _position;private readonly List<Point2> _trace=[];private List<Point2> _path=[];
    private string? _selected;private bool _busy,_ready,_handedOff;private int _scale=100;private TextEdit? _editor;private DraftEditor? _activeDraft;private Label? _saved;private double _debounce;private Task<bool>? _save;
    private static readonly Key[] MoveKeys=[Key.W,Key.A,Key.S,Key.D,Key.Up,Key.Down,Key.Left,Key.Right,Key.E,Key.Enter];
    public void Configure(CampaignService service,ProfileStore store,Mist.Core.Content.RuntimeCatalog catalog,Func<Task> handoff){_service=service;_store=store;_catalog=catalog;_handoff=handoff;}
    public override async void _Ready()
    {
        try
        {
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);Build();await Refresh();if(_handedOff)return;_ready=true;_world.GrabFocus();
            GD.Print("MISTSEA_CAMPAIGN_BOOT");if(OS.GetCmdlineUserArgs().Contains("--campaign-client-test"))await ClientChecks();
        }
        catch(Exception e){GD.PrintErr("MISTSEA_CAMPAIGN_ERROR "+e);if(_status is not null)_status.Text=e.Message;if(OS.GetCmdlineUserArgs().Contains("--campaign-client-test"))GetTree().Quit(5);}
    }
    private Label Text(Node parent,string text,int size=20)
    {
        var label=new Label{Text=text,AutowrapMode=TextServer.AutowrapMode.WordSmart,SizeFlagsHorizontal=SizeFlags.ExpandFill,MouseFilter=MouseFilterEnum.Ignore};label.SetMeta("base_font_size",size);label.AddThemeFontSizeOverride("font_size",size*_scale/100);parent.AddChild(label);return label;
    }
    private Button Action(Node parent,string name,Action action)
    {
        var b=new Button{Text=name,AccessibilityName=name,CustomMinimumSize=new(0,46),SizeFlagsHorizontal=SizeFlags.ExpandFill};b.Pressed+=action;parent.AddChild(b);return b;
    }
    private void Build()
    {
        var background=new ColorRect{Color=new Color("101e29"),MouseFilter=MouseFilterEnum.Ignore};background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);AddChild(background);
        var outer=new MarginContainer();outer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);outer.AddThemeConstantOverride("margin_left",24);outer.AddThemeConstantOverride("margin_right",24);outer.AddThemeConstantOverride("margin_top",18);outer.AddThemeConstantOverride("margin_bottom",18);AddChild(outer);
        var scroll=new ScrollContainer{FollowFocus=true,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};outer.AddChild(scroll);var flow=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};flow.AddThemeConstantOverride("separation",12);scroll.AddChild(flow);
        Text(flow,"雾海 · 岸边救援",30);_location=Text(flow,"",18);var nav=new HFlowContainer();flow.AddChild(nav);
        foreach(string name in new[]{"地图","任务","档案","队伍","学习","系统"})Action(nav,name,()=>{if(_panels.Count==0)Run(()=>OpenNav(name));});
        _objective=Text(flow,"",22);_split=new BoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};flow.AddChild(_split);
        _world=new WorldCanvas{CustomMinimumSize=new(300,450),SizeFlagsHorizontal=SizeFlags.ExpandFill};_world.Clicked=Click;_split.AddChild(_world);
        var side=new VBoxContainer{CustomMinimumSize=new(250,0),SizeFlagsHorizontal=SizeFlags.ExpandFill};_split.AddChild(side);Text(side,"现场物件与出口",22);
        _targets=new OptionButton{FitToLongestItem=false,AccessibilityName="当前场景物件或出口"};_targets.ItemSelected+=i=>Select((int)i);side.AddChild(_targets);
        Action(side,"沿可走路径接近选中站位",()=>{if(_selected is not null){_world.GrabFocus();Click(_targetList.Single(t=>t.Id==_selected).Stand);}});
        Action(side,"交互选中物件 / 使用选中出口",()=>Run(Interact));Action(side,"继续当前近岸遭遇",()=>Run(OpenBattle));
        Text(side,"点击先走近，E或Enter交互。WASD和方向键移动。材料需在现场取得。",18);_status=Text(flow,"序章内部候选 · 原创材料尚待人工审校。",18);
        Resized+=Reflow;Reflow();
    }
    private void Reflow(){if(_split is not null)_split.Vertical=_scale>=150||Size.X<1000;}
    private async Task Refresh()
    {
        _view=await _service.ViewAsync();if(_view.Q102Ready&&!_handedOff){_handedOff=true;_ready=false;CloseAll();await _handoff();return;}
        _position=_view.Actor.Position;_trace.Clear();var geometry=_service.Geometry[_view.Actor.Scene];_world.Geometry=geometry;_world.Player=_position;_world.FrontSafe=_view.Prologue.ShoreSafe;_world.SceneTitle=_catalog.Scenes.Single(s=>s.Id==geometry.Id).Name;_world.QueueRedraw();
        _location.Text=_world.SceneTitle+" · 正常新档 / 内部灰盒候选";_objective.Text=_catalog.Quests.Single(q=>q.Id==_view.CurrentQuest).Title+" · 经验 "+_view.Snapshot.Balances.GetValueOrDefault("experience")+" · "+_catalog.Quests.Single(q=>q.Id==_view.CurrentQuest).CloseContract;
        _targets.Clear();_targetList.Clear();foreach(var port in geometry.Ports){_targetList.Add((port.Id,port.Stand,true));_targets.AddItem("出口 · "+_catalog.Scenes.Single(s=>s.Id==port.TargetScene).Name);}
        foreach(var spot in geometry.Spots){_targetList.Add((spot.Id,spot.Stand,false));_targets.AddItem(_view.Stations.Single(s=>s.Spot==spot.Id).Label);}
        int index=_targetList.FindIndex(t=>t.Id==_selected);_targets.Select(index);if(index<0)_selected=null;_world.Selected=_selected;
    }
    private bool Composing()=>DisplayServer.HasFeature(DisplayServer.Feature.Ime)&&DisplayServer.ImeGetText().Length>0;
    private bool MayMove()=>_ready&&!_busy&&_panels.Count==0&&!Composing()&&GetViewport().GuiGetFocusOwner()==_world&&_view.ShoreBattle?.Battle.Outcome is not(CombatOutcome.Active or CombatOutcome.Defeat);
    private void Select(int i){if(i<0||i>=_targetList.Count)return;_selected=_targetList[i].Id;_world.Selected=_selected;_world.QueueRedraw();}
    private void Click(Point2 point)
    {
        if(!MayMove())return;var nearest=_targetList.OrderBy(t=>t.Stand.Distance(point)).First();if(nearest.Stand.Distance(point)<5){_selected=nearest.Id;_targets.Select(_targetList.FindIndex(t=>t.Id==nearest.Id));point=nearest.Stand;}
        _path=new NavigationGrid(_world.Geometry!).FindPath(_position,point,true).ToList();if(_path.Count==0)_status.Text="站位不可达：当前墙体或危险边界阻挡。";_world.Selected=_selected;_world.QueueRedraw();
    }
    public override void _Process(double delta)
    {
        if(_debounce>0){_debounce-=delta;if(_debounce<=0){if(Composing())_debounce=0.2;else _=Save();}}
        if(!MayMove())return;Point2? next=null;
        if(_path.Count>0){var target=_path[0];double d=_position.Distance(target),step=Math.Min(d,25*delta);next=d<0.02?target:new(_position.X+(target.X-_position.X)*step/d,_position.Y+(target.Y-_position.Y)*step/d);if(d<=step+0.02)_path.RemoveAt(0);}
        else
        {
            bool Down(Key key)=>!_release.Contains(key)&&Input.IsPhysicalKeyPressed(key);int x=(Down(Key.D)||Down(Key.Right)?1:0)-(Down(Key.A)||Down(Key.Left)?1:0),y=(Down(Key.S)||Down(Key.Down)?1:0)-(Down(Key.W)||Down(Key.Up)?1:0);if(x!=0||y!=0){double d=Math.Sqrt(x*x+y*y);next=new(_position.X+x/d*25*delta,_position.Y+y/d*25*delta);}
        }
        if(next is{} p&&new NavigationGrid(_world.Geometry!).SegmentClear(_position,p,true)){_position=p;_trace.Add(p);_world.Player=p;_world.QueueRedraw();}
        if(_path.Count==0&&_trace.Count>0&&!MoveKeys.Take(8).Any(k=>Input.IsPhysicalKeyPressed(k)))_=Flush();
    }
    public override void _Input(InputEvent e)
    {
        if(e is InputEventKey{Pressed:false} release)_release.Remove(release.Keycode);
        if(e is InputEventKey{Pressed:true,Keycode:Key.Tab} tab&&GetViewport().GuiGetFocusOwner() is TextEdit editor&&!Composing()){if(tab.ShiftPressed)editor.FindPrevValidFocus()?.GrabFocus();else editor.FindNextValidFocus()?.GrabFocus();GetViewport().SetInputAsHandled();}
    }
    public override void _UnhandledKeyInput(InputEvent e)
    {
        if(e is not InputEventKey{Pressed:true,Echo:false} key||Composing()||_release.Contains(key.Keycode))return;
        if(key.Keycode==Key.Escape&&_panels.Count>0&&GetViewport().GuiGetFocusOwner() is not TextEdit){Close();GetViewport().SetInputAsHandled();return;}
        if(MayMove()&&key.Keycode is Key.E or Key.Enter){Run(Interact);GetViewport().SetInputAsHandled();}
    }
    private async Task Flush(){if(_busy||_trace.Count==0)return;_busy=true;try{await _service.TraverseAsync(_trace.ToImmutableArray());await Refresh();}catch(Exception e){_status.Text=e.Message;await Refresh();}finally{_busy=false;}}
    private async void Run(Func<Task> action){if(_busy)return;await Flush();_path.Clear();_busy=true;try{await action();if(!_handedOff)await Refresh();}catch(Exception e){_status.Text=e.Message;try{await Refresh();}catch(Exception r){_ready=false;_status.Text=e.Message+" · 当前状态不可用："+r.Message;}}finally{_busy=false;}}
    private static void Committed(CommandResult r){if(r.Status is not(CommitStatus.Committed or CommitStatus.AlreadyCommitted))throw new InvalidOperationException(string.Join("；",r.Issues.Select(i=>i.SafeMessage))+" · "+r.Status);}
    private VBoxContainer Panel(string title,Action? onClose=null)
    {
        var previous=GetViewport().GuiGetFocusOwner();foreach(var key in MoveKeys)if(Input.IsPhysicalKeyPressed(key))_release.Add(key);_path.Clear();
        var panel=new PanelContainer{MouseFilter=MouseFilterEnum.Stop};panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);AddChild(panel);panel.AddThemeStyleboxOverride("panel",new StyleBoxFlat{BgColor=new Color("101e29"),ContentMarginLeft=28,ContentMarginRight=28,ContentMarginTop=20,ContentMarginBottom=20});
        var scroll=new ScrollContainer{FollowFocus=true,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};panel.AddChild(scroll);var flow=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};flow.AddThemeConstantOverride("separation",14);scroll.AddChild(flow);Text(flow,title,28);var back=Action(flow,"返回",Close);_panels.Push((panel,previous,onClose));back.GrabFocus();return flow;
    }
    private void Close(){if(_panels.Count==0)return;var frame=_panels.Pop();frame.Close?.Invoke();frame.Panel.QueueFree();if(_panels.Count==0)_world.GrabFocus();else if(frame.Focus is{} f&&GodotObject.IsInstanceValid(f)&&_panels.Peek().Panel.IsAncestorOf(f))f.GrabFocus();}
    private void CloseAll(){while(_panels.Count>0)Close();}
    private async Task Interact()
    {
        if(_panels.Count>0||_selected is null)return;var selected=_targetList.Single(t=>t.Id==_selected);if(_position.Distance(selected.Stand)>2.5)throw new InvalidOperationException("请先走到选中的操作站位。");
        if(selected.Port){Committed(await _service.EnterPortAsync(selected.Id));await _store.CreateCheckpointAsync("automatic");return;}
        var station=_view.Stations.Single(s=>s.Spot==selected.Id);var flow=Panel(station.Label);
        foreach(string id in station.Materials)Action(flow,"取得并阅读现场材料",()=>Run(async()=>{Committed(await _service.DiscoverAsync(id));await Refresh();await OpenMaterial(id);}));
        if(station.Spot=="R0-S01.ship_log")Action(flow,"比对观测时间线",OpenTimeline);
        if(station.Spot=="R0-S03.winch")Action(flow,"按现行来源选择接应方向",OpenLanding);
        foreach(string action in station.Actions)
        {
            if(action is "confirm_coast_timeline" or "choose_receiving_direction")continue;
            if(action is "write_rescue_message" or "write_harbour_request")Action(flow,action=="write_rescue_message"?"打开同一份求援草稿":"打开同一份委托申请",()=>Run(()=>OpenWriter(action=="write_rescue_message"?CampaignService.RescueDraft:CampaignService.HarbourDraft)));
            else if(action=="begin_shore_encounter")Action(flow,"明确处置近岸威胁",()=>Run(async()=>{Committed(await _service.BeginShoreEncounterAsync());CloseAll();await Refresh();await OpenBattle();}));
            else Action(flow,ActionName(action),()=>Run(async()=>{Committed(await _service.ExecuteStationAsync(action));Close();}));
        }
    }
    private static string ActionName(string action)=>action switch
    {
        "acquire_rescue_tools"=>"取出这份救援包和救援索","reinforce_cabin_path"=>"按说明加固船舱通路","evacuate_to_shore"=>"将人员移至受保护岸段",
        "isolate_winch"=>"关闭并隔离绞盘驱动","seat_winch_cable"=>"在隔离状态调整钢缆","fix_rescue_rope"=>"固定持有的救援索","power_winch"=>"确认索具后启用绞盘","test_winch"=>"检查当前索具与接应方向","move_survivors_to_shelter"=>"沿已安全的接应路移送人员",
        "register_survivors"=>"核对并登记实际到棚人员","inspect_relay_entrance"=>"检查中继检修入口","isolate_relay"=>"关闭并隔离中继设备","seat_relay_lead"=>"在隔离状态接好中继引线","power_relay"=>"确认接线后启用中继","test_relay"=>"测试当前中继配置",
        "collect_coast_record"=>"取得现场独立记录","collect_inspection_mail"=>"取得登记的检修邮包","open_shelter_back_door"=>"从内侧打开回棚后门","acknowledge_pickup"=>"核对并确认接应回执","register_harbour_arrival"=>"办理实际到港接应登记",_=>"查看现场操作说明"
    };
    private Task OpenMaterial(string id)
    {
        return Show();async Task Show(){var m=await _service.OpenMaterialAsync(id);var flow=Panel(m.Title);Text(flow,m.English,22);if(m.Modality=="listening_script")Text(flow,"当前只有英文脚本，尚无可播放音轨。阅读脚本属于文字辅助，不记录独立听力。",18);Action(flow,"显示这份已得材料的中文释义",()=>Text(flow,m.Chinese,20));}
    }
    private void OpenTimeline(){var flow=Panel("观测时间线 · 明确关系确认");Text(flow,"根据已得原始抄记核对先后。这项选择属于明确辅助，尚未评价英语能力。",20);foreach(var item in new[]{(CoastTimeline.LightLossBeforeDrift,"灯光异常早于航向偏离"),(CoastTimeline.DriftBeforeLightLoss,"航向偏离早于灯光异常"),(CoastTimeline.InsufficientEvidence,"当前材料不足以确认先后")})Action(flow,item.Item2,()=>Run(async()=>{Committed(await _service.ConfirmTimelineAsync(item.Item1));Close();}));}
    private void OpenLanding(){var flow=Panel("接应方向 · 当前来源确认");Text(flow,"按潮位说明和更正留言选择；改变方向会使此前的张力检查失效。",20);Action(flow,"选择南侧标记",()=>Run(async()=>{Committed(await _service.ChooseLandingAsync(RescueLanding.SouthMarker));Close();}));Action(flow,"选择北侧接应段",()=>Run(async()=>{Committed(await _service.ChooseLandingAsync(RescueLanding.NorthReach));Close();}));}
    private async Task OpenWriter(string task)
    {
        if(_editor is not null){_editor.GrabFocus();return;}if(!_drafts.TryGetValue(task,out var draft)){var head=await _store.GetDraftAsync(task);var session=_store.Session;draft=new(_store,new(session.Profile,session.Branch,session.Load,task,head?.Id,head?.Revision??0,head?.Body??"",_service.Package.Version));_drafts.Add(task,draft);}
        _activeDraft=draft;var flow=Panel(task==CampaignService.RescueDraft?"求援消息草稿":"检修委托申请草稿",()=>{_=Save();_editor=null;_saved=null;});Text(flow,task==CampaignService.RescueDraft?"Write a short request for help. Include where the survivors are, how many need collection, what assistance is needed, and the current restriction on the approach.":"Write a request to Havel for a temporary maintenance commission. State your purpose, the equipment you propose to inspect, and the boundary you will respect.",22);
        Text(flow,"当前离线模式不会自动理解英文或给出能力分数。草稿与行动意图分别保存、确认。",18);_editor=new TextEdit{Text=draft.Text,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,300),AccessibilityName="当前工作英文正文"};_editor.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(_editor);
        var currentEditor=_editor;currentEditor.TextChanged+=()=>{draft.Edit(currentEditor.Text);_debounce=0.5;if(_saved is not null&&GodotObject.IsInstanceValid(_saved))_saved.Text="有未保存修改。";};_editor.FocusExited+=()=>{foreach(var key in MoveKeys)if(Input.IsPhysicalKeyPressed(key))_release.Add(key);};_saved=Text(flow,"尚未收到当前正文保存回执。",18);
        Action(flow,"保存当前草稿",()=>_=Save());Action(flow,"导出当前正文到本地",()=>{string dir=ProjectSettings.GlobalizePath("user://draft-exports");Directory.CreateDirectory(dir);string path=Path.Combine(dir,"MistSea-"+Guid.NewGuid()+".txt");File.WriteAllText(path,draft.Text,new System.Text.UTF8Encoding(false));_status.Text="正文已导出："+path;});
        Action(flow,"回看已得档案，保留当前草稿",()=>Run(()=>OpenNav("档案")));Action(flow,"使用明确结构化辅助确认意图",()=>OpenMeaning(task));Action(flow,"核对当前状态并预览提交",()=>Run(async()=>{if(!await Save())throw new InvalidOperationException("当前正文尚未保存。");var preview=task==CampaignService.RescueDraft?await _service.PreviewRescueMessageAsync():await _service.PreviewHarbourRequestAsync();var confirm=Panel("工作提交预览",()=>_service.CloseSubmissionPreview(preview.Token));foreach(var issue in preview.Issues)Text(confirm,issue.SafeMessage,20);if(preview.Ready)Action(confirm,"明确提交这一次工作",()=>Run(async()=>{Committed(await _service.ConfirmSubmissionAsync(preview.Token));CloseAll();await _store.CreateCheckpointAsync("automatic");}));else Text(confirm,"预览没有执行工作。补齐现场状态或重新确认当前意图后再预览。",18);}));_editor.GrabFocus();
    }
    private void OpenMeaning(string task)
    {
        var flow=Panel("结构化辅助 · 确认本次意图");Text(flow,"选择只形成你的提案，不替你施工或提交，不计为独立自由写作。请按已得来源填写；空项保持未明确。",20);
        string[][] labels=task==CampaignService.RescueDraft?[["位置尚未明确","避风救援棚","浅滩"],["人数尚未明确","4人","5人"],["需求尚未明确","接应人员","只送工具"],["进路尚未明确","仅北侧接应段","仅南侧标记"]]:[["目的尚未明确","协助民用撤离","申请巡灯船"],["工作范围尚未明确","民用渡轮与独立本地工坊","全部港口设备"],["主发电机边界尚未明确","不属于此次检修委托","立即启动主发电机"]];
        var choices=new List<OptionButton>();foreach(var group in labels){var option=new OptionButton{FitToLongestItem=false,AccessibilityName=group[0]};foreach(string label in group)option.AddItem(label);flow.AddChild(option);choices.Add(option);}
        Action(flow,"确认这些明确选择，保留提交步骤",()=>Run(async()=>{if(!await Save())throw new InvalidOperationException("先保存当前正文。");string V(int i,string a,string b)=>choices[i].Selected==1?a:choices[i].Selected==2?b:"unconfirmed";
            if(task==CampaignService.RescueDraft)Committed(await _service.ConfirmRescueMeaningAsync(new(V(0,"shelter","shore"),choices[1].Selected==1?4:choices[1].Selected==2?5:0,V(2,"pickup","tools_only"),V(3,"north_reach_only","south_marker_only")),true));else Committed(await _service.ConfirmHarbourMeaningAsync(new(V(0,"evacuation_assistance","patrol_ship_request"),V(1,"civil_ferry_and_local_workshop","all_port_equipment"),V(2,"main_generator_excluded","start_main_generator")),true));Close();}));
    }
    private async Task<bool> Save()
    {
        if(_activeDraft is null)return true;if(Composing())return false;SyncEditor();if(_save is{IsCompleted:false})await _save;_save=Core();return await _save;
        async Task<bool> Core(){var draft=_activeDraft!;try{var ack=await draft.SaveAsync();if(_saved is not null&&GodotObject.IsInstanceValid(_saved))_saved.Text=draft.IsSaved?"正文已保存 · 版本 "+ack.Revision:"新修改尚未保存，旧回执不会覆盖提示。";return draft.IsSaved;}catch(Exception e){if(_saved is not null&&GodotObject.IsInstanceValid(_saved))_saved.Text="保存失败，正文仍可导出："+e.Message;return false;}}
    }
    private void SyncEditor(){if(_activeDraft is not null&&_editor is not null&&GodotObject.IsInstanceValid(_editor)&&_activeDraft.Text!=_editor.Text)_activeDraft.Edit(_editor.Text);}
    public async Task<bool> SaveBeforeClose()
    {
        if(Composing())return false;SyncEditor();bool wasReady=_ready;_ready=false;while(_busy)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        if(_trace.Count>0){try{await _service.TraverseAsync(_trace.ToImmutableArray());_trace.Clear();}catch(Exception e){_ready=wasReady;_status.Text=e.Message;return false;}}
        if(_save is{IsCompleted:false})await _save;
        foreach(var draft in _drafts.Values)if(!draft.IsSaved){try{await draft.SaveAsync();}catch(Exception e){_ready=wasReady;if(_status is not null)_status.Text="正文尚未保存："+e.Message;return false;}if(!draft.IsSaved){_ready=wasReady;return false;}}
        return true;
    }
    private async Task OpenNav(string name)
    {
        var flow=Panel(name);switch(name)
        {
            case "地图":Text(flow,"地图只显示空间与出口；选地点不会替你走路或执行任务。",20);foreach(var scene in _service.Geometry.Values)Text(flow,_catalog.Scenes.Single(s=>s.Id==scene.Id).Name,20);break;
            case "任务":foreach(var quest in _view.Quests)Text(flow,_catalog.Quests.Single(q=>q.Id==quest.Id).Title+" · "+quest.Lifecycle,20);Text(flow,"先处理人员救援，再核对中继记录。未来章节尚未在此候选中完成。",18);break;
            case "档案":if(_view.Materials.Length==0)Text(flow,"还没有取得现场材料。",20);foreach(var material in _view.Materials)Action(flow,material.Title,()=>Run(()=>OpenMaterial(material.Id)));break;
            case "队伍":var hero=await new PartyService(_store,_catalog).InspectAsync();Text(flow,$"主角等级 {hero.Level} · 校准 {hero.Grade} · 未分配属性 {hero.UnspentAttributes}",22);Text(flow,"莉娜只参与本次救援协作；正式同行邀请在潮门塔交接后开放。",18);break;
            case "学习":Text(flow,"当前序章材料待人工审校与目标校准。阅读脚本、使用中文释义或结构化辅助都不计为独立听力或完整能力评分。",20);Text(flow,"阅读 / 听力 / 写作：尚未评估。",22);break;
            case "系统":foreach(int size in new[]{100,125,150,175,200})Action(flow,size+"% 字体",()=>ApplyFontScale(size));Action(flow,"保存当前冒险检查点",()=>Run(async()=>{if(!await Save())throw new InvalidOperationException("正文未保存。");await _store.CreateCheckpointAsync("manual");_status.Text="冒险检查点已保存。";}));Action(flow,"按现有状态继续，保持资源与同一任务实例",()=>Run(async()=>{if(!await Save())throw new InvalidOperationException("正文未保存。");CloseAll();await _store.ContinueAsync();_drafts.Clear();_activeDraft=null;}));Text(flow,"默认离线，无在线正文发送。完整故事、美术、音轨和人工验收仍在制作。",18);break;
        }
    }
    private void ApplyFontScale(int size){_scale=size;void Apply(Node n){if(n is Label l&&l.HasMeta("base_font_size"))l.AddThemeFontSizeOverride("font_size",l.GetMeta("base_font_size").AsInt32()*size/100);if(n is TextEdit e)e.AddThemeFontSizeOverride("font_size",20*size/100);foreach(var child in n.GetChildren())Apply(child);}Apply(this);Reflow();}
    private async Task OpenBattle()
    {
        var battle=(await _service.ViewAsync()).ShoreBattle?.Battle??throw new InvalidOperationException("请先走到近岸威胁边界，明确进入遭遇。");var flow=Panel("近岸救援协作");Text(flow,$"第 {battle.Round} 轮 · 当前行动者 {battle.CurrentActor??"结果已稳定"} · 共享急救 {battle.Aid}",22);
        foreach(var u in battle.Units)Text(flow,$"{u.Name} · HP {u.Hp}/{u.MaxHp} · 盾 {u.Shield} · AP {u.Ap} · 专注 {u.Focus}",20);var engine=new BattleEngine(new(_catalog),battle);foreach(var i in battle.Intents){var p=engine.PreviewIntent(i.Source);Text(flow,i.Name+" · 原始威胁 "+i.RawDamage+" · "+(p.Cancelled?p.Reason:string.Join("；",p.Losses.Select(t=>battle.Units.Single(u=>u.Id==t.Target).Name+"当前防护下HP预计减少 "+t.HpLoss))),18);}
        Action(flow,"暂停回看已得档案",()=>Run(()=>OpenNav("档案")));
        if(battle.Outcome!=CombatOutcome.Active){Text(flow,battle.Outcome==CombatOutcome.Controlled?"近岸威胁已制止，继续索具检查与人员移送；没有击杀经验。":"本次结果未提供救援成果。可明确撤回入口再准备。",20);if(battle.Outcome==CombatOutcome.Defeat)Action(flow,"明确撤回本次入口",()=>Run(async()=>{Committed(await _service.WithdrawShoreAsync());Close();}));return;}
        var actor=battle.Units.Single(u=>u.Id==battle.CurrentActor);var ids=new[]{"BASIC","GUARD","MOVE","AID","REVIVE"}.Concat(actor.Active).Append("END").ToArray();var actions=new OptionButton{FitToLongestItem=false,AccessibilityName="近岸计划行动"};foreach(string id in ids)actions.AddItem(id switch{"BASIC"=>"基础攻击","GUARD"=>"防御","MOVE"=>"相邻换位","AID"=>"共享急救","REVIVE"=>"救起友方","END"=>"结束个人计划槽",_=>new BattleDefinitions(_catalog).Actions[id].Name});flow.AddChild(actions);
        var targets=new OptionButton{FitToLongestItem=false,AccessibilityName="近岸计划目标"};foreach(var u in battle.Units)targets.AddItem(u.Name);targets.Select(battle.Units.FindIndex(u=>u.Side==CombatSide.Enemy&&u.Alive));flow.AddChild(targets);var position=new OptionButton{AccessibilityName="相邻换位目标位置"};foreach(string p in new[]{"不换位","左前","右前","左后","右后"})position.AddItem(p);flow.AddChild(position);
        Action(flow,"预览近岸行动与费用",()=>Run(async()=>{string id=ids[actions.Selected];var r=new BattleRequest(new(Guid.NewGuid()),id,actor.Id,id is "GUARD" or "END" or "MOVE"?[]:targets.Selected<0?[]:[battle.Units[targets.Selected].Id],position.Selected>0?position.Selected-1:null);var p=await _service.PreviewShoreActionAsync(r);if(!p.Action.Legal)throw new InvalidOperationException(p.Action.Reason);var confirm=Panel("近岸行动预览",()=>_service.CloseShoreActionPreview(p.Token));Text(confirm,$"AP {p.Action.Ap} · 专注 {p.Action.Focus} · 预计结算伤害 {p.Action.PredictedDamage}（先由盾吸收）",22);Action(confirm,"明确执行所选近岸行动",()=>Run(async()=>{Committed(await _service.ConfirmShoreActionAsync(p.Token));Close();Close();await Refresh();await OpenBattle();}));}));
        Action(flow,"撤回完整近岸入口…",()=>{var confirm=Panel("撤回近岸入口");Text(confirm,"放弃本次局部战斗结果，恢复本次入口资源；已取得材料和正文保留。",20);Action(confirm,"明确撤回这一次遭遇",()=>Run(async()=>{Committed(await _service.WithdrawShoreAsync());Close();Close();}));});
    }
    private async Task ClientChecks()
    {
        var checks=new List<object>();int failed=0;void Check(string name,bool passed){checks.Add(new{name,passed});if(!passed)failed++;}
        foreach(string name in new[]{"地图","任务","档案","队伍","学习","系统"}){long sequence=(await _store.SnapshotAsync()).Sequence;await OpenNav(name);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);Check("campaign_nav_"+name+"_read_only",_panels.Count==1&&(await _store.SnapshotAsync()).Sequence==sequence);Close();}
        await OpenWriter(CampaignService.RescueDraft);_editor!.Text="中文😀e\u0301\nWASD Enter";await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);Check("campaign_unicode_editor",_editor.Text=="中文😀e\u0301\nWASD Enter");Check("campaign_editor_blocks_world",!MayMove());Check("campaign_actual_draft_ack",await Save());Check("campaign_unicode_payload_persisted",(await _store.GetDraftAsync(CampaignService.RescueDraft))?.Body==_editor.Text);ApplyFontScale(200);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);Check("campaign_200_percent_single_column",_split.Vertical&&_editor.GetThemeFontSize("font_size")==40);await OpenNav("档案");Close();Check("campaign_nested_reader_returns_editor",GetViewport().GuiGetFocusOwner()==_editor);CloseAll();ApplyFontScale(100);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        string output=System.Environment.GetEnvironmentVariable("MISTSEA_CLIENT_TEST_OUTPUT")??ProjectSettings.GlobalizePath("user://client-test");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"campaign_client_results.json"),StrictJson.Serialize(new{checks,failures=failed,native_manual_ime="not_run",screen_reader="not_run"}));GD.Print("MISTSEA_CAMPAIGN_CLIENT_TESTS "+checks.Count+" failures="+failed);GetTree().Quit(failed==0?0:1);
    }
}

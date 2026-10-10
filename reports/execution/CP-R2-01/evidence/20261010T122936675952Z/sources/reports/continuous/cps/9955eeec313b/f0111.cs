using System.Collections.Immutable;
using System.Text.Json;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.EventScheduler;
using Mist.Application.Storage;
using Mist.Application.Travel;
using TidegateHandover=Mist.Core.ChapterOne.TidegateHandover;
using Mist.Core.ChapterTwo;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.Quests;

namespace Mist.Application.ChapterTwo;

public sealed record ChapterTwoView(BranchSnapshot Snapshot,ActorState Actor,ChapterTwoState State,
    ImmutableArray<QuestState> Quests,ImmutableArray<ChapterMaterial> Materials,ImmutableArray<ChapterStation> Stations,
    ImmutableDictionary<string,SceneGeometry> Geometry);
public sealed record ChapterTwoPreview(Guid Token,bool Ready,ImmutableArray<RuleIssue> Issues,string DraftTask,string? DraftHash,bool StructuredAssistance);
public sealed partial class ChapterTwoService
{
    public const string StateId="chapter2",ClampId="R2-CLAMP-01",ConnectorId="R2-CONNECTOR-01";
    public static readonly ImmutableArray<string> MainQuests=["Q-201","Q-202","Q-203","Q-204"];
    private readonly IAdventureRepository _repository;
    private readonly RuntimeCatalog _catalog;
    private readonly Dictionary<Guid,CommitPlanCommand> _previews=new();
    public ChapterTwoPackage Package {get;}
    public ImmutableDictionary<string,SceneGeometry> Geometry {get;}
    public ChapterTwoService(IAdventureRepository repository,RuntimeCatalog catalog,ChapterTwoPackage package)
    {ChapterTwoPackage.Validate(package);_repository=repository;_catalog=catalog;Package=package;Geometry=package.Scenes.ToImmutableDictionary(s=>s.Geometry.Id,s=>s.Geometry);}
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static ActorState Actor(BranchSnapshot s)=>BranchOperations.Read<ActorState>(s,"actor")??throw new InvalidOperationException("角色现场未加载。");
    private ChapterTwoState State(BranchSnapshot s)=>BranchOperations.Read<ChapterTwoState>(s,StateId) is{} v&&v.ContentVersion==Package.Version?v:throw new InvalidOperationException("本章候选状态未初始化或版本不符。");
    private static QuestState? Quest(BranchSnapshot s,string q)=>BranchOperations.Read<QuestState>(s,"quest:"+q);
    private static bool Closed(BranchSnapshot s,string q)=>Quest(s,q) is{Lifecycle:QuestLifecycle.Closed,Outcome:QuestOutcome.Completed};
    private static bool HasResult(BranchSnapshot s,string id)=>BranchOperations.Read<ChapterEncounterResult>(s,"R2:result:"+id) is{Completed:true} r&&s.Receipts.Any(x=>x.SemanticKey=="R2:"+id+":result"&&x.ActionType=="chapter_two_encounter_result"&&StrictJson.Deserialize<ChapterEncounterResult>(x.ResultJson)==r);
    private string[] Reads(BranchSnapshot s)=>s.Entities.Select(e=>e.Id).Concat(new[]{"actor",StateId,"ship:glimmer","commission:cross-region","sea-route:R1:R2","berth:R2","readiness:TWR-R2","proposal:Q-201","proposal:Q-202","proposal:Q-203","proposal:Q-204","proposal:CQ-MIRA-01"}).Concat(MainQuests.Select(q=>"quest:"+q)).Distinct().ToArray();
    private EntityChange Change(BranchSnapshot s,ChapterTwoState state)=>BranchOperations.Change(s,StateId,"chapter_two_state",state);
    private async ValueTask<CommandResult> Execute(CommitPlanCommand command)
    {
        if(command.Context.Load!=_repository.Session.Load||command.Context.Branch!=_repository.Session.Branch)return new(CommitStatus.Rejected,null,[new("stale_load",StateId,"档案分支或读取世代已改变，请重新预览。")]);
        if(_repository is not IEventLeaseStore leases)throw new InvalidOperationException("地方工作需要原子前景占用接口。");
        string owner="chapter2:"+command.Context.Command.Value;Require(await leases.TryLeasesAsync(command.Context.Load,owner,["input:foreground"]),"另一项前景工作尚未交接。");
        try{return await _repository.ExecuteAsync(command);}finally{await leases.ReleaseLeasesAsync(command.Context.Load,owner);}
    }
    private ValueTask<CommandResult> Commit(BranchSnapshot s,string action,ImmutableArray<EntityChange> changes,
        object result,string? key=null,ImmutableArray<ItemChange> items=default,ImmutableArray<BalanceChange> balances=default,DraftStamp? draft=null)
        =>Execute(BranchOperations.Plan(_repository,s,key??"R2:action:"+Guid.NewGuid(),action,GateChanges(s,changes),Reads(s),result,items,balances,draft:draft));
    private ImmutableArray<EntityChange> GateChanges(BranchSnapshot s,ImmutableArray<EntityChange> changes)
    {
        var stateChange=changes.SingleOrDefault(c=>c.Id==StateId);var st=stateChange is null?BranchOperations.Read<ChapterTwoState>(s,StateId):StrictJson.Deserialize<ChapterTwoState>(stateChange.Json);if(st is null)return changes;
        bool Result(string id)=>changes.Any(c=>c.Id=="R2:result:"+id&&StrictJson.Deserialize<ChapterEncounterResult>(c.Json).Completed)||HasResult(s,id);
        var values=new Dictionary<string,bool>{{"public_landing",st.Landing},{"public_path",true},{"public_greenhouse_corridor",true},{"public_planting_entry",true},{"basic_archive_identity",st.BasicArchive},{"onsite_inspection_and_root_route_safe",st.InspectionScope&&st.StairSafe&&Result("ENC-R2-01")},{"upper_route_safe_current",st.UpperSafe&&Result("ENC-R2-02")},{"inner_room_latch_open",st.InnerLatch},{"local_station_stable_and_tower_work_permit",st.LocalStable&&st.KitchenFeed&&st.TowerPermit},{"private_letter_invitation_or_discovered_public_threshold",false}};
        var output=changes.ToBuilder();foreach(var p in values){string id="R2:gate:"+p.Key;var old=BranchOperations.Read<ChapterGate>(s,id);if(old is null||old.Allowed!=p.Value)output.Add(BranchOperations.Change(s,id,"chapter_gate",new ChapterGate(p.Key,p.Value,Package.Version)));}return output.ToImmutable();
    }
    private static void Safe(BranchSnapshot s)
    {
        BranchOperations.Safe(s);
        Require(!s.Entities.Where(e=>e.Kind=="event").Select(e=>StrictJson.Deserialize<EventSession>(e.Json)).Any(e=>e.State==EventLifecycle.Running),"请先安全交接当前前景事件。");
        Require(!s.Entities.Where(e=>e.Kind=="transport_segment").Select(e=>StrictJson.Deserialize<TransportSegment>(e.Json)).Any(e=>e.CargoInTransit&&!e.Completed),"当前货物运输尚未交接。");
        Require(!BranchOperations.Receipt(s,"Q-604"),"终章起航后外区工作暂停。");
    }
    private static bool Current(BranchSnapshot s,IEnumerable<EntityStamp> reads)=>reads.All(r=>(BranchOperations.Entity(s,r.EntityId)?.Revision??-1)==r.Revision);
    // A scene string alone is not regional authority. All these records come from existing physical vessel services.
    private static void ActualSeaArrival(BranchSnapshot s)
    {
        Require(Closed(s,"Q-104"),"需要真实第一章塔站收束。");
        var hand=s.Receipts.SingleOrDefault(r=>r.SemanticKey=="Q-104:resolution"&&r.ActionType=="tidegate_vessel_handover")??throw new InvalidOperationException("缺少实际巡灯船移交回执。");
        var h=StrictJson.Deserialize<TidegateHandover>(hand.ResultJson);var ship=BranchOperations.Read<SharedVessel>(s,"ship:glimmer")??throw new InvalidOperationException("没有权威巡灯船实例。");
        Require(h.VesselInstance==ship.Instance.ToString()&&h.VesselDefinition=="GLIMMER"&&h.Destination=="R2"&&h.WatchOfficer=="N-DECKWATCH"&&ship.WatchOfficer==h.WatchOfficer&&ship.DockRegion=="R2"&&ship.Spaces.SequenceEqual(h.Spaces),"船只来源、当前泊位或值守不符。");
        var e=BranchOperations.Entity(s,"commission:cross-region")??throw new InvalidOperationException("跨区核查委托未实际签发。");using(var j=JsonDocument.Parse(e.Json))Require(j.RootElement.GetProperty("allowed").GetBoolean()&&j.RootElement.GetProperty("next_region").GetString()=="R2"&&j.RootElement.GetProperty("basis").GetString()=="Q-104:resolution","跨区委托范围不符。");
        var route=BranchOperations.Read<SeaRoute>(s,"sea-route:R1:R2")??throw new InvalidOperationException("R1到R2实际海图许可未登记。");
        var r=StrictJson.Deserialize<SeaRoute>(BranchOperations.Evidence(s,route.AuthorityReceipt,"sea_route_verified").ResultJson);
        Require(((route.A=="R1"&&route.B=="R2")||(route.A=="R2"&&route.B=="R1"))&&r.A==route.A&&r.B==route.B&&r.GateReads.SequenceEqual(route.GateReads)&&Current(s,route.GateReads),"当前海图来源或安全读戳失效。");
        var berth=BranchOperations.Read<VesselBerth>(s,"berth:R2")??throw new InvalidOperationException("R2实际泊位未登记。");var b=StrictJson.Deserialize<VesselBerth>(BranchOperations.Evidence(s,berth.AuthorityReceipt,"vessel_berth_verified").ResultJson);
        Require(berth.Instance==ship.Instance&&berth.Scene=="R2-S01"&&berth.Region=="R2"&&StrictJson.Serialize(b with{AuthorityReceipt=berth.AuthorityReceipt})==StrictJson.Serialize(berth)&&Current(s,berth.GateReads),"登陆泊位来源或当前安全条件无效。");
        Require(s.Receipts.Where(r=>r.ActionType=="sail_shared_glimmer").Any(r=>{using var d=JsonDocument.Parse(r.ResultJson);var j=d.RootElement;return j.GetProperty("ship_instance").GetGuid()==ship.Instance&&j.GetProperty("from").GetString()=="R1"&&j.GetProperty("to").GetString()=="R2";}),"需要同一巡灯船实际航行到R2，不能仅改角色场景。");
        Require(Actor(s).Scene=="R2-S01","请从实际R2登陆场景接入。");
    }
    public async ValueTask InitializeRegionAsync()
    {
        var s=await _repository.SnapshotAsync();if(BranchOperations.Entity(s,StateId) is not null){State(s);return;}Safe(s);ActualSeaArrival(s);
        Require(new NavigationGrid(Geometry["R2-S01"]).CanStand(Actor(s).Position,true),"登陆落点不是实际安全几何。");
        var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(Change(s,new(){ContentVersion=Package.Version}));
        foreach(var q in MainQuests)changes.Add(BranchOperations.Change(s,"quest:"+q,"quest",new QuestState(q,Guid.NewGuid(),q=="Q-201"?QuestLifecycle.Active:QuestLifecycle.Unaccepted,null)));
        changes.Add(BranchOperations.Change(s,"quest:CQ-MIRA-01","quest",new QuestState("CQ-MIRA-01",Guid.NewGuid(),QuestLifecycle.Unaccepted,null)));
        foreach(var room in new[]{"A","B","C"})
        {
            int start=room=="A"?1:room=="B"?9:19,count=room=="A"?8:room=="B"?10:6;string spot="R2-S03.living_"+room.ToLowerInvariant();
            changes.Add(BranchOperations.Change(s,"R2:residents:"+room,"local_residents",new LocalResidentGroup(room,Enumerable.Range(start,count).Select(i=>"R2-RES-"+i.ToString("D2")).ToImmutableArray(),"R2-S03",spot,true)));
        }
        var initialized=await Commit(s,"chapter_two_region_initialized",changes.ToImmutable(),new{scope="original_unreviewed_runtime_candidate",experience_awarded=0,landing_is_not_registration=true},"R2:initialize:"+Package.Version,
            [new(ClampId,"R2-LOCAL-CLAMP","new","","warehouse","R2-S05",null),new(ConnectorId,"R2-LOCAL-CONNECTOR","new","","warehouse","R2-S05",null)]);
        Require(initialized.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,"当前登陆状态改变，本章初始化未提交。");
    }
    public ImmutableDictionary<string,SceneGeometry> CurrentGeometry(BranchSnapshot s)
    {
        var state=State(s);return Package.Scenes.ToImmutableDictionary(x=>x.Geometry.Id,x=>x.Geometry with{Danger=x.Hazards.Where(h=>h.Id switch
        {"R2-S05.root-route-threat"=>!HasResult(s,"ENC-R2-01"),"R2-S06.wet-live-floor"=>!state.UpperSafe||!HasResult(s,"ENC-R2-02"),"R2-S07.root-core-zone"=>!HasResult(s,"ENC-R2-03"),_=>true}).Select(h=>h.Area).ToImmutableArray()});
    }
    public async ValueTask<ImmutableDictionary<string,SceneGeometry>> CurrentGeometryAsync()=>CurrentGeometry(await _repository.SnapshotAsync());
    private void At(BranchSnapshot s,string spot)
    {
        var actor=Actor(s);var geo=CurrentGeometry(s);Require(geo.TryGetValue(actor.Scene,out var scene)&&scene.Spots.Any(x=>x.Id==spot&&actor.Position.Distance(x.Stand)<=2.5)&&new NavigationGrid(scene).CanStand(actor.Position,true),"请先走到该材料或操作的实际安全站位。");
    }
    private bool Acquired(BranchSnapshot s,ChapterMaterial m)=>BranchOperations.Read<ChapterMaterialAccess>(s,"material:"+m.Id) is{} a&&a.Id==m.Id&&a.Version==m.Version&&a.Fingerprint==m.Fingerprint&&a.SourceRoot==m.SourceRoot;
    private bool Accessible(BranchSnapshot s,ChapterMaterial m)
    {
        var st=State(s);if(m.Quest=="Q-203"&&!st.InspectionScope)return false;if(m.Id=="R2-Q202-R-COHORT"&&!Closed(s,"Q-201"))return false;
        if(m.Id=="R2-Q204-R-SIGNATURE"&&(!Closed(s,"Q-202")||!Closed(s,"Q-203")||!st.HistoricalReview))return false;
        if(m.Id.StartsWith("R2-Q204-",StringComparison.Ordinal)&&(!Closed(s,"Q-202")||!Closed(s,"Q-203")))return false;
        if(m.AccessClass=="controlled_review")
        {
            string record=m.Id=="R2-Q204-R-SIGNATURE"?"TS-18":"TR-D1-7";var scope=BranchOperations.Read<ChapterRecordScope>(s,"R2:scope:"+record);
            string[] displayed=record=="TS-18"?["signature_reference","version_source"]:["transfer_id","identity_link","dated_condition","destination_code","version_source"];
            return scope is{Grantor:"N-JUNE",Recipient:"patrol_technician",Version:"1",IdentityPublication:false}&&displayed.All(scope.Fields.Contains);
        }
        if(m.AccessClass=="personal_invitation")return st.W04Invited&&st.W04Scope&&BranchOperations.Read<ChapterRecordScope>(s,"R2:w04_scope") is{Grantor:"W-04",IndividualConsent:true,Version:"W04-v1"} w&&new[]{"observation_time","witness_duty","version_source"}.All(w.Fields.Contains);
        return m.AccessClass switch{"public"=>true,"individual_consent"=>false,_=>false};
    }
    public async ValueTask<ChapterTwoView> ViewAsync()
    {
        var s=await _repository.SnapshotAsync();var actor=Actor(s);var st=State(s);return new(s,actor,st,MainQuests.Append("CQ-MIRA-01").Select(q=>Quest(s,q)!).ToImmutableArray(),Package.Materials.Where(m=>Acquired(s,m)&&Accessible(s,m)).ToImmutableArray(),Package.Stations.Where(x=>x.Spot.StartsWith(actor.Scene+".",StringComparison.Ordinal)).ToImmutableArray(),CurrentGeometry(s));
    }
    public async ValueTask MoveAsync(Point2 point)=>await TraverseAsync([point]);
    public async ValueTask TraverseAsync(ImmutableArray<Point2> path)
    {
        if(path.IsDefaultOrEmpty)return;var s=await _repository.SnapshotAsync();Safe(s);var a=Actor(s);var nav=new NavigationGrid(CurrentGeometry(s)[a.Scene]);var from=a.Position;
        foreach(var to in path){Require(nav.SegmentClear(from,to,true),"路径穿过当前障碍或危险，角色未移动。");from=to;}
        var result=await Commit(s,"chapter_two_walk",[BranchOperations.Change(s,"actor","actor",a with{Position=from,ActionClock=checked(a.ActionClock+1)})],new{actual_path=path});Require(result.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,"移动因当前状态改变而拒绝。");
    }
    public string? PortIssue(BranchSnapshot s,Port port)
    {
        var st=State(s);return Package.PortGates[port.Id] switch
        {"public_landing"=>st.Landing?null:"请登记实际登陆。","public_path"or"public_greenhouse_corridor"or"public_planting_entry"=>null,
        "basic_archive_identity"=>st.BasicArchive?null:"需要基础公共调阅身份。","onsite_inspection_and_root_route_safe"=>st.InspectionScope&&st.StairSafe&&HasResult(s,"ENC-R2-01")?null:"检修范围、根道处置或梯道检查未成立。",
        "upper_route_safe_current"=>st.UpperSafe&&HasResult(s,"ENC-R2-02")?null:"当前上层安全检查尚未成立。","inner_room_latch_open"=>st.InnerLatch?null:"须从机房内侧打开返程门。",
        "local_station_stable_and_tower_work_permit"=>st.LocalStable&&st.KitchenFeed&&st.TowerPermit?null:"支站稳定和现场塔站许可未成立。",
        "private_letter_invitation_or_discovered_public_threshold"=>"私人回信独立合同与本人许可尚未接入。",_=>"该通路范围尚未登记。"};
    }
    public async ValueTask<CommandResult> EnterPortAsync(string id)
    {
        var s=await _repository.SnapshotAsync();Safe(s);var a=Actor(s);var geo=CurrentGeometry(s);var port=geo[a.Scene].Ports.Single(p=>p.Id==id);Require(a.Position.Distance(port.Stand)<=2.5,"请先走到实际出口。");Require(PortIssue(s,port) is null,PortIssue(s,port)??"");var pair=geo[port.TargetScene].Ports.Single(p=>p.Id==port.TargetPort);
        Require(new NavigationGrid(geo[a.Scene]).CanStand(a.Position,true)&&new NavigationGrid(geo[port.TargetScene]).CanStand(pair.Stand,true),"出口/入口当前危险，不执行移动。");
        var connection=_catalog.Connections.Single(c=>c.A==a.Scene&&c.B==port.TargetScene||c.B==a.Scene&&c.A==port.TargetScene);string gateId="R2:gate:"+Package.PortGates[id],travelId="travel-gate:"+TravelService.ConnectionKey(connection.A,connection.B);
        Require(BranchOperations.Read<ChapterGate>(s,gateId) is{Allowed:true},"当前连接权限缓存与真实工作状态不一致。");
        var dependencies=ImmutableArray.Create(new EntityStamp(gateId,BranchOperations.Entity(s,gateId)!.Revision));var registered=BranchOperations.Read<ConnectionSafetyState>(s,travelId);
        if(registered is null||!registered.Dependencies.SequenceEqual(dependencies))
        {
            var safety=new ConnectionSafetyState(connection.A,connection.B,TravelService.Contract(connection),true,true,dependencies,"");
            var issued=await Commit(s,"connection_safety_verified",[],safety);Require(issued.Status==CommitStatus.Committed&&issued.Receipt is not null,"连接现场许可未提交。");
            var registration=await new TravelService(_repository,_catalog,CurrentGeometry(await _repository.SnapshotAsync())).RegisterConnectionSafetyAsync(issued.Receipt!.Id.Value.ToString());Require(registration.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,"当前连接许可登记未提交。");s=await _repository.SnapshotAsync();a=Actor(s);
        }
        var stamps=dependencies.Add(new(travelId,BranchOperations.Entity(s,travelId)!.Revision));
        var proof=new TraversedConnection(a.Scene,port.TargetScene,port.Id,pair.Id,port.Stand,pair.Stand,checked(a.ActionClock+1),TravelService.Contract(connection),stamps,true);
        return await Commit(s,"scene_transition",[BranchOperations.Change(s,"actor","actor",a with{Scene=pair.TargetScene==a.Scene?port.TargetScene:throw new InvalidDataException(),Position=pair.Stand,ActionClock=proof.ActionClock})],proof);
    }
    public async ValueTask<CommandResult> DiscoverAsync(string id)
    {
        var s=await _repository.SnapshotAsync();Safe(s);var m=Package.Materials.Single(m=>m.Id==id);At(s,m.Spot);Require(Accessible(s,m),"材料的当前具体访问范围尚未成立。");if(Acquired(s,m))return new(CommitStatus.AlreadyCommitted,null,[]);
        var a=new ChapterMaterialAccess(m.Id,m.Version,m.TranslationVersion,m.Fingerprint,m.SourceRoot,m.Spot);return await Commit(s,"chapter_two_material_acquired",[BranchOperations.Change(s,"material:"+m.Id,"material_access",a)],new{material=m.Id,source_root=m.SourceRoot,version=m.Version,source_channel=m.Kind=="listening_script"?"written_transcript_only":"written_original",audio_listened=false},"material:"+m.Id+":acquired");
    }
    public async ValueTask<ChapterMaterial> OpenMaterialAsync(string id)=>(await ViewAsync()).Materials.SingleOrDefault(m=>m.Id==id)??throw new InvalidOperationException("仅实际取得且当前获准的材料可查看。");
    public async ValueTask<DraftSaved> SaveDraftAsync(string task,long expectedRevision,DraftId? draft,string body)
    {
        Require(Package.WritingTasks.Any(x=>x.Id==task),"本章没有该写作任务。");var s=_repository.Session;return await _repository.SaveAsync(new(s.Profile,s.Branch,s.Load,task,draft,expectedRevision,body,Package.Version));
    }
    public async ValueTask<CommandResult> ConfirmMeaningAsync(string quest,ImmutableDictionary<string,string> fields,bool structuredAssistance)
    {
        var s=await _repository.SnapshotAsync();Safe(s);var task=Package.WritingTasks.Single(x=>x.Quest==quest);At(s,task.Spot);Require(Quest(s,quest) is{Lifecycle:QuestLifecycle.Active},"当前任务未开启或已收束。");var d=await _repository.GetDraftAsync(task.Id);
        Require(structuredAssistance||d is{Deleted:false}&&!string.IsNullOrWhiteSpace(d.Body),"空白正文须明确选择结构化辅助，不能冒充自由英文理解。");
        return await Commit(s,"chapter_two_meaning_confirmed",[BranchOperations.Change(s,"proposal:"+quest,"proposal",new ChapterWorldMeaning(quest,d?.Hash,structuredAssistance,fields))],new{structured_assistance=structuredAssistance,free_english_understanding="not_claimed",language_score=(string?)null},draft:new(task.Id,d?.Id,d?.Revision??0,d?.Hash));
    }
    public ValueTask<CommandResult> ConfirmPreviewAsync(Guid token)=>_previews.TryGetValue(token,out var command)?Execute(command):ValueTask.FromResult(new CommandResult(CommitStatus.Rejected,null,[new("preview_closed",StateId,"预览未就绪或已取消。") ]));
    public void ClosePreview(Guid token)=>_previews.Remove(token);
    public async ValueTask<CommandResult> RegisterCurrentReturnAnchorAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);var actor=Actor(s);var st=State(s);string spot=actor.Scene switch{"R2-S01"when st.Landing=>"R2-S01.berth","R2-S02"when st.Reception=>"R2-S02.june","R2-S08"when st.TowerDuty&&st.TowerIsolation&&st.TowerReturn&&Closed(s,"Q-203")=>"R2-S08.return_check",_=>throw new InvalidOperationException("这里不是已实际成立的地区安全返程点。")};At(s,spot);var point=CurrentGeometry(s)[actor.Scene].Spots.Single(x=>x.Id==spot).Stand;var anchor=new ReturnAnchor(actor.Scene,point,actor.Scene=="R2-S08"?ReturnAnchorKind.TowerSafeExit:ReturnAnchorKind.Settlement,"");
        var issued=await Commit(s,"return_anchor_verified",[],anchor);Require(issued.Status==CommitStatus.Committed&&issued.Receipt is not null,"安全返程来源未提交。");return await new TravelService(_repository,_catalog,CurrentGeometry(await _repository.SnapshotAsync())).RegisterAnchorAsync(issued.Receipt!.Id.Value.ToString());
    }
}

using System.Collections.Immutable;
using System.Text.Json;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.EventScheduler;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Core.ChapterTwo;
using Mist.Core.ChapterThree;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.Quests;
using Mist.Core.Engineering;
using Mist.Core.Voyage;

namespace Mist.Application.ChapterThree;

public sealed record ChapterThreeView(BranchSnapshot Snapshot,ActorState Actor,ChapterThreeState State,
    ImmutableArray<QuestState> Quests,ImmutableArray<ChapterMaterial> Materials,ImmutableArray<ChapterStation> Stations,
    ImmutableDictionary<string,SceneGeometry> Geometry);
public sealed record ChapterThreePreview(Guid Token,bool Ready,ImmutableArray<RuleIssue> Issues,string DraftTask,string? DraftHash,bool StructuredAssistance);
public sealed partial class ChapterThreeService
{
    public const string StateId="chapter3",RescueSegment="R3:rescue_transport";
    public static readonly ImmutableArray<string> MainQuests=["Q-301","Q-302","Q-303","Q-304"];
    public static readonly ImmutableArray<string> SideQuests=["CQ-OREN-01","CQ-OREN-02","CQ-OREN-03"];
    private readonly IAdventureRepository _repository;
    private readonly RuntimeCatalog _catalog;
    private readonly Dictionary<Guid,CommitPlanCommand> _previews=new();
    public ChapterThreePackage Package {get;}
    public ImmutableDictionary<string,SceneGeometry> Geometry {get;}
    public ChapterThreeService(IAdventureRepository repository,RuntimeCatalog catalog,ChapterThreePackage package)
    {ChapterThreePackage.Validate(package);_repository=repository;_catalog=catalog;Package=package;Geometry=package.Scenes.ToImmutableDictionary(s=>s.Geometry.Id,s=>s.Geometry);}
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static ActorState Actor(BranchSnapshot s)=>BranchOperations.Read<ActorState>(s,"actor")??throw new InvalidOperationException("角色现场未加载。");
    private ChapterThreeState State(BranchSnapshot s)=>BranchOperations.Read<ChapterThreeState>(s,StateId) is{} v&&v.ContentVersion==Package.Version?v:throw new InvalidOperationException("矿区状态未初始化或原文版本不符。");
    private static QuestState? Quest(BranchSnapshot s,string q)=>BranchOperations.Read<QuestState>(s,"quest:"+q);
    private static bool Closed(BranchSnapshot s,string q)=>Quest(s,q) is{Lifecycle:QuestLifecycle.Closed,Outcome:QuestOutcome.Completed};
    private static bool HasResult(BranchSnapshot s,string id)=>BranchOperations.Read<MiningEncounterResult>(s,"R3:result:"+id) is{Completed:true} r&&s.Receipts.Any(x=>x.SemanticKey=="R3:"+id+":result"&&x.ActionType=="chapter_three_encounter_result"&&StrictJson.Serialize(StrictJson.Deserialize<MiningEncounterResult>(x.ResultJson))==StrictJson.Serialize(r));
    private string[] Reads(BranchSnapshot s)=>s.Entities.Select(e=>e.Id).Concat(new[]{"actor",StateId,"ship:glimmer","commission:next-inquiry","sea-route:R2:R3","berth:R3",RescueSegment,"readiness:TWR-R3","workpost:CP-OREN","roster:CP-OREN"}).Concat(MainQuests.Concat(SideQuests).SelectMany(q=>new[]{"quest:"+q,"proposal:"+q})).Concat(Enumerable.Range(1,5).Select(i=>"combat:ENC-R3-"+i.ToString("D2"))).Distinct().ToArray();
    private EntityChange Change(BranchSnapshot s,ChapterThreeState state)=>BranchOperations.Change(s,StateId,"chapter_three_state",state);
    private async ValueTask<CommandResult> Execute(CommitPlanCommand c)
    {
        if(c.Context.Load!=_repository.Session.Load||c.Context.Branch!=_repository.Session.Branch)return new(CommitStatus.Rejected,null,[new("stale_load",StateId,"档案分支或读取世代改变，请重新预览。")]);
        if(_repository is not IEventLeaseStore leases)throw new InvalidOperationException("矿区工作需要原子前景占用接口。");
        string owner="chapter3:"+c.Context.Command.Value;Require(await leases.TryLeasesAsync(c.Context.Load,owner,["input:foreground"]),"另一项前景工作尚未交接。");
        try{return await _repository.ExecuteAsync(c);}finally{await leases.ReleaseLeasesAsync(c.Context.Load,owner);}
    }
    private ValueTask<CommandResult> Commit(BranchSnapshot s,string action,ImmutableArray<EntityChange> changes,object result,string? key=null,ImmutableArray<ItemChange> items=default,ImmutableArray<BalanceChange> balances=default,DraftStamp? draft=null)
        =>Execute(BranchOperations.Plan(_repository,s,key??"R3:action:"+Guid.NewGuid(),action,GateChanges(s,changes),Reads(s),result,items,balances,draft:draft));
    private static void Safe(BranchSnapshot s)
    {
        BranchOperations.Safe(s);
        Require(!s.Entities.Where(e=>e.Kind=="event").Select(e=>StrictJson.Deserialize<EventSession>(e.Json)).Any(e=>e.State==EventLifecycle.Running),"请先交接当前前景事件。");
        Require(!s.Entities.Where(e=>e.Kind=="transport_segment"&&e.Id!=RescueSegment).Select(e=>StrictJson.Deserialize<TransportSegment>(e.Json)).Any(e=>e.CargoInTransit&&!e.Completed),"另一项货物运输尚未交接。");
        Require(!BranchOperations.Receipt(s,"Q-604"),"终章起航后外区工作暂停。");
    }
    private static bool Current(BranchSnapshot s,IEnumerable<EntityStamp> reads)=>reads.All(r=>(BranchOperations.Entity(s,r.EntityId)?.Revision??-1)==r.Revision);
    private void ActualSeaArrival(BranchSnapshot s)
    {
        Require(Closed(s,"Q-204")&&s.Receipts.Any(r=>r.SemanticKey=="Q-204:resolution"&&r.ActionType=="chapter_two_archive_handover_received"),"需要实际R2档案与塔站闭合来源。");
        using(var h=JsonDocument.Parse(BranchOperations.Entity(s,"R2:actual_handover")?.Json??throw new InvalidOperationException("R2实际档案交接不存在。")))Require(h.RootElement.GetProperty("public_facts").GetProperty("mining_batch").GetString()=="MC-2","实际来函不是本次MC-2核查。");
        var ship=BranchOperations.Read<SharedVessel>(s,"ship:glimmer")??throw new InvalidOperationException("没有实际巡灯船。");
        Require(ship.Instance!=Guid.Empty&&ship.DockRegion=="R3"&&ship.WatchOfficer=="N-DECKWATCH"&&ship.Spaces.SequenceEqual(new[]{"HUB-S01","HUB-S02","HUB-S03"})&&s.Entities.Count(e=>e.Kind=="shared_vessel")==1&&BranchOperations.Receipt(s,"Q-104"),"当前单船实例、实际权属、值守或泊位不符。");
        var originalShip=s.Receipts.SingleOrDefault(r=>r.SemanticKey=="Q-104:resolution"&&r.ActionType=="tidegate_vessel_handover")??throw new InvalidOperationException("没有实际单船移交来源。");var owner=Mist.Core.Contracts.StrictJson.Deserialize<Mist.Core.ChapterOne.TidegateHandover>(originalShip.ResultJson);Require(Closed(s,"Q-104")&&owner.VesselInstance==ship.Instance.ToString()&&owner.VesselDefinition=="GLIMMER"&&owner.WatchOfficer==ship.WatchOfficer&&owner.Spaces.SequenceEqual(ship.Spaces),"船只原始使用权、实例或值守与当前船不一致。");
        using(var j=JsonDocument.Parse(BranchOperations.Entity(s,"commission:next-inquiry")?.Json??throw new InvalidOperationException("下一地区核查委托未签发。")))Require(j.RootElement.GetProperty("allowed").GetBoolean()&&j.RootElement.GetProperty("region").GetString()=="R3"&&j.RootElement.GetProperty("purpose").GetString()=="verify_MC-2_original_instruments_and_acceptance"&&j.RootElement.GetProperty("basis").GetString()=="Q-204:resolution","矿区核查委托范围不符。");
        var route=BranchOperations.Read<SeaRoute>(s,"sea-route:R2:R3")??throw new InvalidOperationException("实际R2到R3航路未登记。");var original=StrictJson.Deserialize<SeaRoute>(BranchOperations.Evidence(s,route.AuthorityReceipt,"sea_route_verified").ResultJson);
        Require(((route.A=="R2"&&route.B=="R3")||(route.A=="R3"&&route.B=="R2"))&&original.A==route.A&&original.B==route.B&&original.GateReads.SequenceEqual(route.GateReads)&&Current(s,route.GateReads),"航路来源或当前安全读戳无效。");
        var berth=BranchOperations.Read<VesselBerth>(s,"berth:R3")??throw new InvalidOperationException("R3实际合法泊位未登记。");var b=StrictJson.Deserialize<VesselBerth>(BranchOperations.Evidence(s,berth.AuthorityReceipt,"vessel_berth_verified").ResultJson);
        Require(berth.Instance==ship.Instance&&berth.Region=="R3"&&berth.Scene=="R3-S01"&&StrictJson.Serialize(b with{AuthorityReceipt=berth.AuthorityReceipt})==StrictJson.Serialize(berth)&&Current(s,berth.GateReads),"登陆泊位来源、实例或安全条件无效。");
        var sail=s.Receipts.Where(r=>r.ActionType=="sail_shared_glimmer").OrderByDescending(r=>r.Sequence).FirstOrDefault()??throw new InvalidOperationException("需要实际共享船航行。");using(var j=JsonDocument.Parse(sail.ResultJson))Require(j.RootElement.GetProperty("ship_instance").GetGuid()==ship.Instance&&j.RootElement.GetProperty("from").GetString()=="R2"&&j.RootElement.GetProperty("to").GetString()=="R3","最近实际航行不是本船R2到R3。");
        var arrivals=s.Receipts.Where(r=>r.ActionType=="actual_vessel_arrival").Select(r=>(Receipt:r,Proof:StrictJson.Deserialize<VesselArrivalProof>(r.ResultJson)));
        var arrival=arrivals.SingleOrDefault(x=>x.Proof.SailReceipt==sail.ReceiptId);Require(arrival.Proof is not null&&arrival.Proof.VesselInstance==ship.Instance&&arrival.Proof.From=="R2"&&arrival.Proof.To=="R3"&&arrival.Proof.Scene==berth.Scene&&arrival.Proof.Position==berth.Landing&&arrival.Proof.RouteReceipt==route.AuthorityReceipt&&arrival.Proof.BerthReceipt==berth.AuthorityReceipt&&arrival.Proof.GeometrySha256==VoyageRules.Hash(Geometry[berth.Scene])&&arrival.Proof.PackageVersion=="r2-r3-voyage-candidate-1"&&arrival.Proof.AuthorityScope=="public_landing_only_not_local_registration"&&arrival.Receipt.Sequence>sail.Sequence,"实际抵达typed来源、海图泊位回执或源几何不符。");
        var handoff=BranchOperations.Evidence(s,arrival.Proof!.HandoffReceipt,"regional_voyage_handoff_issued");var binding=StrictJson.Deserialize<RegionalVoyageBinding>(handoff.ResultJson);Require(binding.VesselInstance==ship.Instance&&binding.From=="R2"&&binding.To=="R3"&&binding.Duty=="R3-LOCAL-BERTH-DUTY"&&binding.AuthorityScope=="public_landing_only"&&binding.DestinationGeometrySha256==arrival.Proof.GeometrySha256&&handoff.Sequence<sail.Sequence,"抵达不得借用另一实例、未获授权接应或航后补签来源。");
        Require(Actor(s).Scene==berth.Scene,"必须在本船真实登陆场景接入，场景名称不授予区域权限。");
    }
    public async ValueTask InitializeRegionAsync()
    {
        var s=await _repository.SnapshotAsync();if(BranchOperations.Entity(s,StateId) is not null){State(s);return;}Safe(s);ActualSeaArrival(s);var berth=BranchOperations.Read<VesselBerth>(s,"berth:R3")!;Require(Actor(s).Position.Distance(berth.Landing)<=2.5&&new NavigationGrid(Geometry["R3-S01"]).CanStand(Actor(s).Position,true),"请从本船实际安全登陆落点初始化。");
        var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(Change(s,new(){ContentVersion=Package.Version}));
        foreach(var q in MainQuests.Concat(SideQuests))changes.Add(BranchOperations.Change(s,"quest:"+q,"quest",new QuestState(q,Guid.NewGuid(),q is "Q-301" or "Q-302"?QuestLifecycle.Active:QuestLifecycle.Unaccepted,null)));
        foreach(var worker in new[]{"W1","W2","W3"})changes.Add(BranchOperations.Change(s,"R3:worker:"+worker,"mining_worker",new MiningWorker(worker,"R3-S07","R3-S07.rescue","awaiting_rescue",0)));
        var items=ImmutableArray.CreateBuilder<ItemChange>();foreach(string kind in new[]{"warm_meal","first_aid","wage_shift","work_kit","aux_alarm_kit"}){int count=kind=="warm_meal"?12:kind=="first_aid"?3:kind=="work_kit"?6:1;for(int i=1;i<=count;i++)items.Add(new("R3-local:"+kind+":"+i,"R3-LOCAL-"+kind.ToUpperInvariant(),"new","","warehouse","R3-S08",null));}
        items.Add(new("R3-local:safety_kit:1","R3-LOCAL-SAFETY-KIT","new","","warehouse","R3-S08",null));
        var r=await Commit(s,"chapter_three_region_initialized",changes.ToImmutable(),new{scope="original_unreviewed_runtime_candidate",local_supply_only=true,experience_awarded=0},"R3:initialize:"+Package.Version,items.ToImmutable());Require(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,"当前登陆状态改变，初始化未提交。");
    }
    public ImmutableDictionary<string,SceneGeometry> CurrentGeometry(BranchSnapshot s)
    {
        var st=State(s);return Package.Scenes.ToImmutableDictionary(x=>x.Geometry.Id,x=>x.Geometry with{Danger=x.Hazards.Where(h=>h.Id switch
        {"R3-S05.rail_obstruction"=>!HasResult(s,"ENC-R3-01"),"R3-S06.wet_drive"=>!st.MineRouteSafe||!HasResult(s,"ENC-R3-02"),"R3-S07.instrument_guard"=>!HasResult(s,"ENC-R3-03"),"R3-S09.extraction_machine"=>!HasResult(s,"ENC-R3-05"),_=>true}).Select(h=>h.Area).ToImmutableArray()});
    }
    public async ValueTask<ImmutableDictionary<string,SceneGeometry>> CurrentGeometryAsync()=>CurrentGeometry(await _repository.SnapshotAsync());
    private void At(BranchSnapshot s,string spot){var a=Actor(s);var geo=CurrentGeometry(s);Require(geo.TryGetValue(a.Scene,out var g)&&g.Spots.Any(x=>x.Id==spot&&a.Position.Distance(x.Stand)<=2.5)&&new NavigationGrid(g).CanStand(a.Position,true),"请先走到对应实际安全站位。");}
    private bool Acquired(BranchSnapshot s,ChapterMaterial m)=>BranchOperations.Read<ChapterMaterialAccess>(s,"material:"+m.Id) is{} a&&a.Id==m.Id&&a.Version==m.Version&&a.Fingerprint==m.Fingerprint&&a.SourceRoot==m.SourceRoot;
    private bool Accessible(BranchSnapshot s,ChapterMaterial m)
    {
        var st=State(s);
        if(m.Quest.StartsWith("CQ-OREN",StringComparison.Ordinal)&&st.AlarmInvitation!="accept")return false;
        return m.AccessClass switch{"public"=>true,"commissioned_inspection" or "local_work_scope"=>st.Commission,"controlled_review"=>st.Commission&&BranchOperations.Read<ChapterRecordScope>(s,"R3:batch_scope") is{Record:"MC-2",Version:"MC2-current-source-set",Recipient:"patrol_technician",Purpose:"verify_original_instruments_and_acceptance",IdentityPublication:false} scope&&new[]{"check_status","acceptance_status","signatory_scope","version_source"}.All(scope.Fields.Contains),"personal_invitation" or "individual_consent" or "individual_permission"=>m.Quest.StartsWith("CQ-OREN",StringComparison.Ordinal)&&st.AlarmInvitation=="accept",_=>false};
    }
    private bool Sources(BranchSnapshot s,params string[] ids)=>ids.All(id=>Package.Materials.SingleOrDefault(m=>m.Id==id) is{} m&&Acquired(s,m)&&Accessible(s,m));
    private bool QuestSources(BranchSnapshot s,string q)=>Package.Materials.Where(m=>m.Quest==q&&m.Kind!="listening_script").Any()&&Package.Materials.Where(m=>m.Quest==q&&m.Kind!="listening_script"&&m.Id!="R3-Q302-R-STATEMENT").All(m=>Acquired(s,m)&&Accessible(s,m));
    public async ValueTask<ChapterThreeView> ViewAsync(){var s=await _repository.SnapshotAsync();var a=Actor(s);return new(s,a,State(s),MainQuests.Concat(SideQuests).Select(q=>Quest(s,q)!).ToImmutableArray(),Package.Materials.Where(m=>Acquired(s,m)&&Accessible(s,m)).ToImmutableArray(),Package.Stations.Where(x=>x.Spot.StartsWith(a.Scene+".",StringComparison.Ordinal)).ToImmutableArray(),CurrentGeometry(s));}
    public async ValueTask MoveAsync(Point2 p)=>await TraverseAsync([p]);
    public async ValueTask TraverseAsync(ImmutableArray<Point2> path)
    {
        if(path.IsDefaultOrEmpty)return;var s=await _repository.SnapshotAsync();Safe(s);var a=Actor(s);var nav=new NavigationGrid(CurrentGeometry(s)[a.Scene]);var from=a.Position;foreach(var to in path){Require(nav.SegmentClear(from,to,true),"路径穿过当前障碍或危险，角色未移动。");from=to;}
        var next=a with{Position=from,ActionClock=checked(a.ActionClock+1)};var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(BranchOperations.Change(s,"actor","actor",next));FollowWorkers(s,next,changes);var r=await Commit(s,"chapter_three_walk",changes.ToImmutable(),new{actual_path=path});Require(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,"移动因当前状态改变而拒绝。");
    }
    private void FollowWorkers(BranchSnapshot s,ActorState a,ImmutableArray<EntityChange>.Builder changes)
    {foreach(var e in s.Entities.Where(e=>e.Kind=="mining_worker")){var w=StrictJson.Deserialize<MiningWorker>(e.Json);if(w.Status=="escorted")changes.Add(BranchOperations.Change(s,e.Id,"mining_worker",w with{Scene=a.Scene,Spot="with_patrol",ActionClock=a.ActionClock}));}}
    private bool EdgeAllowed(BranchSnapshot s,ChapterThreeState st,string from,string to,Func<string,bool>? result=null)
    {
        bool Result(string id)=>result?.Invoke(id)??HasResult(s,id);
        string edge=string.Join("|",new[]{from,to}.Order(StringComparer.Ordinal));return edge switch
        {"R3-S01|R3-S02"=>st.Landing,"R3-S02|R3-S03"=>st.Commission,"R3-S02|R3-S04"=>st.Commission&&st.EntryInstructions&&st.BasicGear,"R3-S04|R3-S05"=>st.LadderSafe,
        "R3-S05|R3-S06"=>st.Commission,"R3-S06|R3-S07"=>st.MineRouteSafe&&Result("ENC-R3-02"),"R3-S04|R3-S07"=>st.FreightLiftTested,
        "R3-S02|R3-S08"=>st.Landing,"R3-S08|R3-S09"=>st.WorkersReceived&&st.BasicDelivered&&st.TowerPermit,"R3-O01|R3-S02"=>st.Landing,"R3-O02|R3-S05"=>false,_=>false};
    }
    private ImmutableArray<EntityChange> GateChanges(BranchSnapshot s,ImmutableArray<EntityChange> changes)
    {
        var sc=changes.SingleOrDefault(x=>x.Id==StateId);var st=sc is null?BranchOperations.Read<ChapterThreeState>(s,StateId):StrictJson.Deserialize<ChapterThreeState>(sc.Json);if(st is null)return changes;
        // Evaluate proposed encounter results as well as current state, so the same transaction updates genuine connection gates.
        bool Result(string id)=>changes.Any(c=>c.Id=="R3:result:"+id&&StrictJson.Deserialize<MiningEncounterResult>(c.Json).Completed)||HasResult(s,id);
        var list=changes.ToBuilder();var readiness=BranchOperations.Read<TowerReadiness>(s,"readiness:TWR-R3");if(readiness is{Safe:true}&&!st.TowerCurrent&&!list.Any(c=>c.Id=="readiness:TWR-R3"))list.Add(BranchOperations.Change(s,"readiness:TWR-R3","tower_readiness",readiness with{Safe=false}));foreach(var p in Geometry.Values.SelectMany(g=>g.Ports.Select(p=>(From:g.Id,Port:p)))){string id="R3:gate:"+TravelService.ConnectionKey(p.From,p.Port.TargetScene);if(list.Any(x=>x.Id==id))continue;bool allowed=EdgeAllowed(s,st,p.From,p.Port.TargetScene,Result);var old=BranchOperations.Read<MiningGate>(s,id);if(old is null||old.Allowed!=allowed)list.Add(BranchOperations.Change(s,id,"chapter_gate",new MiningGate(Package.PortGates[p.Port.Id],allowed,Package.Version)));}return list.ToImmutable();
    }
    public string? PortIssue(BranchSnapshot s,Port p)=>EdgeAllowed(s,State(s),Actor(s).Scene,p.TargetScene)?null:"当前地方许可、永久梯道、独立处置或现场供给尚未成立。";
    public async ValueTask<CommandResult> EnterPortAsync(string id)
    {
        var s=await _repository.SnapshotAsync();Safe(s);var a=Actor(s);var geo=CurrentGeometry(s);var p=geo[a.Scene].Ports.Single(x=>x.Id==id);Require(a.Position.Distance(p.Stand)<=2.5,"请先走到实际出口。");Require(PortIssue(s,p) is null,PortIssue(s,p)??"");var pair=geo[p.TargetScene].Ports.Single(x=>x.Id==p.TargetPort);Require(new NavigationGrid(geo[a.Scene]).CanStand(a.Position,true)&&new NavigationGrid(geo[p.TargetScene]).CanStand(pair.Stand,true),"当前入口或出口危险。");
        if(BranchOperations.Read<TransportSegment>(s,RescueSegment) is{CargoInTransit:true,Completed:false})Require(new[]{"R3-S07","R3-S06","R3-S05","R3-S04","R3-S02"}.Contains(p.TargetScene),"救援组须沿实际安全返程走廊交接，不可离港或转入其他工作区。");
        var connection=_catalog.Connections.Single(c=>c.A==a.Scene&&c.B==p.TargetScene||c.B==a.Scene&&c.A==p.TargetScene);string key=TravelService.ConnectionKey(connection.A,connection.B),gateId="R3:gate:"+key,travelId="travel-gate:"+key;
        var dependencies=ImmutableArray.Create(new EntityStamp(gateId,BranchOperations.Entity(s,gateId)!.Revision));var old=BranchOperations.Read<ConnectionSafetyState>(s,travelId);
        if(old is null||!old.Dependencies.SequenceEqual(dependencies)){var source=new ConnectionSafetyState(connection.A,connection.B,TravelService.Contract(connection),true,true,dependencies,"");var proof=await Commit(s,"connection_safety_verified",[],source);Require(proof.Status==CommitStatus.Committed&&proof.Receipt is not null,"当前连接核验未提交。");var registered=await new TravelService(_repository,_catalog,geo).RegisterConnectionSafetyAsync(proof.Receipt!.Id.Value.ToString());Require(registered.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,"连接安全登记未提交。");s=await _repository.SnapshotAsync();a=Actor(s);}
        var next=a with{Scene=p.TargetScene,Position=pair.Stand,ActionClock=checked(a.ActionClock+1)};var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(BranchOperations.Change(s,"actor","actor",next));FollowWorkers(s,next,changes);
        return await Commit(s,"scene_transition",changes.ToImmutable(),new TraversedConnection(a.Scene,p.TargetScene,p.Id,pair.Id,p.Stand,pair.Stand,next.ActionClock,TravelService.Contract(connection),dependencies.Add(new(travelId,BranchOperations.Entity(s,travelId)!.Revision)),true));
    }
    public async ValueTask<CommandResult> DiscoverAsync(string id){var s=await _repository.SnapshotAsync();Safe(s);var m=Package.Materials.Single(m=>m.Id==id);At(s,m.Spot);Require(Accessible(s,m),"该原文的具体当前访问范围尚未成立。");if(Acquired(s,m))return new(CommitStatus.AlreadyCommitted,null,[]);return await Commit(s,"chapter_three_material_acquired",[BranchOperations.Change(s,"material:"+m.Id,"material_access",new ChapterMaterialAccess(m.Id,m.Version,m.TranslationVersion,m.Fingerprint,m.SourceRoot,m.Spot))],new{material=m.Id,source_root=m.SourceRoot,version=m.Version,source_channel=m.Kind=="listening_script"?"written_transcript_only":"written_original",audio_listened=false},"material:"+m.Id+":acquired");}
    public async ValueTask<ChapterMaterial> OpenMaterialAsync(string id)=>(await ViewAsync()).Materials.SingleOrDefault(m=>m.Id==id)??throw new InvalidOperationException("仅实际取得并当前获准的原文可打开。");
    public async ValueTask<DraftSaved> SaveDraftAsync(string task,long expectedRevision,DraftId? draft,string body){Require(Package.WritingTasks.Any(x=>x.Id==task),"写作任务未登记。");var s=_repository.Session;return await _repository.SaveAsync(new(s.Profile,s.Branch,s.Load,task,draft,expectedRevision,body,Package.Version));}
    public async ValueTask<CommandResult> ConfirmMeaningAsync(string quest,ImmutableDictionary<string,string> fields,bool structuredAssistance)
    {var s=await _repository.SnapshotAsync();Safe(s);var task=Package.WritingTasks.Single(x=>x.Quest==quest);At(s,task.Spot);Require(Quest(s,quest) is{Lifecycle:QuestLifecycle.Active},"任务未开启或已收束。");var d=await _repository.GetDraftAsync(task.Id);Require(structuredAssistance||d is{Deleted:false}&&!string.IsNullOrWhiteSpace(d.Body),"空白正文须明确选择结构化辅助，不能冒充英文理解。");return await Commit(s,"chapter_three_meaning_confirmed",[BranchOperations.Change(s,"proposal:"+quest,"proposal",new ChapterWorldMeaning(quest,d?.Hash,structuredAssistance,fields))],new{structured_assistance=structuredAssistance,free_english_understanding="not_claimed",language_score=(string?)null},draft:new(task.Id,d?.Id,d?.Revision??0,d?.Hash));}
    public ValueTask<CommandResult> ConfirmPreviewAsync(Guid token)=>_previews.TryGetValue(token,out var c)?Execute(c):ValueTask.FromResult(new CommandResult(CommitStatus.Rejected,null,[new("preview_closed",StateId,"预览未就绪或已取消。") ]));
    public void ClosePreview(Guid token)=>_previews.Remove(token);
}


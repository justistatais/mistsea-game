using System.Collections.Immutable;
using System.Text.Json;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.EventScheduler;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Core.ChapterOne;
using Mist.Core.ChapterTwo;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.Quests;
using Mist.Core.VesselInterior;
using Mist.Core.Voyage;

namespace Mist.Application.Voyage;

public sealed record VoyagePreview(Guid Token,bool Ready,ImmutableArray<string> Issues,string Description);
public sealed record VoyageRegistration(CommandResult Route,CommandResult Berth);
public sealed record VoyagePreparation(CommandResult Handoff,CommandResult RouteProof,CommandResult BerthProof,VoyageRegistration Registration);

// Produces only loan-bound R1/R2 chart and public-berth evidence. Actual relocation stays in TravelService.
public sealed class VoyageService
{
    private readonly IAdventureRepository _repository;
    private readonly TravelService _travel;
    private readonly Dictionary<Guid,CommitPlanCommand> _previews=new();
    private readonly string _packageHash;
    public VoyagePackage Package {get;}
    public ImmutableDictionary<string,SceneGeometry> Geometry {get;}
    public VoyageService(IAdventureRepository repository,RuntimeCatalog catalog,VoyagePackage package,ChapterTwoPackage destination,IReadOnlyDictionary<string,SceneGeometry> actualGeometry)
    {
        VoyageRules.Validate(package);ChapterTwoPackage.Validate(destination);
        if(actualGeometry.Any(p=>p.Key!=p.Value.Id||!catalog.Scenes.Any(s=>s.Id==p.Key)))throw new InvalidDataException("航行输入几何身份未登记。");
        _repository=repository;Package=package;_packageHash=VoyageRules.Hash(package);
        Geometry=actualGeometry.ToImmutableDictionary().SetItems(destination.Scenes.Select(s=>new KeyValuePair<string,SceneGeometry>(s.Geometry.Id,s.Geometry)));
        foreach(string scene in new[]{"R1-S01","HUB-S02","R2-S01"})if(!Geometry.ContainsKey(scene)||!catalog.Scenes.Any(s=>s.Id==scene))throw new InvalidDataException("航行需要源泊位、实际船舱和目的接驳场景几何。");
        VoyageRules.SafeBerth(Geometry["R2-S01"],VoyageRules.R2.LandingSpot);
        _travel=new(repository,catalog,Geometry);
    }
    public ValueTask<BranchSnapshot> SnapshotAsync()=>_repository.SnapshotAsync();
    private static ActorState Actor(BranchSnapshot s)=>BranchOperations.Read<ActorState>(s,"actor")??throw new InvalidOperationException("当前现场角色未加载。");
    private static bool Current(BranchSnapshot s,ImmutableArray<EntityStamp> reads)=>!reads.IsDefaultOrEmpty&&reads.Select(r=>r.EntityId).Distinct().Count()==reads.Length&&reads.All(r=>BranchOperations.Entity(s,r.EntityId)?.Revision==r.Revision);
    private static void Guard(BranchSnapshot s)
    {
        BranchOperations.Safe(s);
        if(BranchOperations.Receipt(s,"Q-604"))throw new InvalidOperationException("终章起航后外区航行与泊位签发暂停。");
        if(s.Entities.Where(e=>e.Kind=="event").Select(e=>StrictJson.Deserialize<EventSession>(e.Json)).Any(e=>e.State==EventLifecycle.Running))throw new InvalidOperationException("请先交接实际前景工作。");
        if(s.Entities.Where(e=>e.Kind=="transport_segment").Select(e=>StrictJson.Deserialize<TransportSegment>(e.Json)).Any(e=>e.CargoInTransit&&!e.Completed))throw new InvalidOperationException("专门护送的货物不能借普通航行转移。");
    }
    private SharedVessel Loan(BranchSnapshot s,bool atOrigin)
    {
        if(BranchOperations.Read<QuestState>(s,"quest:Q-104") is not{Lifecycle:QuestLifecycle.Closed,Outcome:QuestOutcome.Completed})throw new InvalidOperationException("塔站实际委托尚未正确收束。");
        var hand=Handover(s);var h=StrictJson.Deserialize<TidegateHandover>(hand.ResultJson);var ship=BranchOperations.Read<SharedVessel>(s,"ship:glimmer")??throw new InvalidOperationException("巡灯船使用权尚未移交。");
        if(ship.Instance==Guid.Empty||h.VesselInstance!=ship.Instance.ToString()||h.VesselDefinition!="GLIMMER"||h.Destination!="R2"||h.WatchOfficer!="N-DECKWATCH"||ship.WatchOfficer!=h.WatchOfficer||!ship.Spaces.SequenceEqual(VesselInteriorRules.Spaces)||!ship.Spaces.SequenceEqual(h.Spaces)||s.Entities.Count(e=>e.Kind=="shared_vessel")!=1||atOrigin&&ship.DockRegion!="R1")throw new InvalidOperationException("当前船实例、实际移交、独立值守或出发泊位不符。");
        return ship;
    }
    private static StoredReceipt Handover(BranchSnapshot s)=>s.Receipts.SingleOrDefault(r=>r.SemanticKey=="Q-104:resolution"&&r.ActionType=="tidegate_vessel_handover")??throw new InvalidOperationException("缺少真实Q104巡灯船移交回执。");
    private static StoredEntity Commission(BranchSnapshot s)
    {
        var e=BranchOperations.Entity(s,"commission:cross-region")??throw new InvalidOperationException("跨区核查委托尚未签发。");using var doc=JsonDocument.Parse(e.Json);var j=doc.RootElement;
        if(!j.GetProperty("allowed").GetBoolean()||j.GetProperty("next_region").GetString()!="R2"||j.GetProperty("basis").GetString()!="Q-104:resolution"||j.GetProperty("purpose").GetString()!=VoyageRules.R2.Purpose||j.GetProperty("mara_current_status").GetString()!=VoyageRules.R2.MentorCurrentStatus)throw new InvalidOperationException("本次委托不支持所选地区、核查用途或导师状态断言。");
        return e;
    }
    private void At(BranchSnapshot s,string scene,Point2 point)
    {
        var actor=Actor(s);var nav=new NavigationGrid(Geometry[scene]);
        if(actor.Scene!=scene||actor.Position.Distance(point)>2.5||!nav.CanStand(actor.Position,true)||!nav.CanStand(point,true)||!nav.SegmentClear(actor.Position,point,true))throw new InvalidOperationException("请实际走到当前合法航图或登陆核对站位。");
    }
    private void Chart(BranchSnapshot s,SharedVessel ship)
    {
        var berth=BranchOperations.Read<VesselBerth>(s,"berth:R1")??throw new InvalidOperationException("源地区实际泊位尚未登记。");var berthSource=StrictJson.Deserialize<VesselBerth>(BranchOperations.Evidence(s,berth.AuthorityReceipt,"vessel_berth_verified").ResultJson);
        var origin=VoyageRules.SafeBerth(Geometry["R1-S01"],"R1-S01.glimmer_handover");
        if(berth.Instance!=ship.Instance||berth.Region!="R1"||berth.Scene!="R1-S01"||berth.Landing!=origin.Stand||berth.Boarding!=origin.Stand||!Current(s,berth.GateReads)||StrictJson.Serialize(berthSource with{AuthorityReceipt=berth.AuthorityReceipt})!=StrictJson.Serialize(berth))throw new InvalidOperationException("源泊位来源、当前安全条件或本船实例不符。");
        var space=BranchOperations.Read<VesselSpace>(s,"ship-space:HUB-S02")??throw new InvalidOperationException("实际船舱航图工位尚未登记。");var source=StrictJson.Deserialize<VesselSpace>(BranchOperations.Evidence(s,space.AuthorityReceipt,"ship_space_verified").ResultJson);var chart=Geometry[Package.ChartScene].Spots.Single(p=>p.Id==Package.ChartSpot);
        if(space.Instance!=ship.Instance||space.Scene!=Package.ChartScene||space.TransferStand!=chart.Stand||!Current(s,space.GateReads)||StrictJson.Serialize(source with{AuthorityReceipt=space.AuthorityReceipt})!=StrictJson.Serialize(space))throw new InvalidOperationException("当前船舱航图的实例、来源、站位或登记版本已改变。");
        At(s,space.Scene,chart.Stand);
    }
    private string HandoffId(SharedVessel ship)=>"voyage:handoff:"+ship.Instance+":R2";
    private string BindingId(SharedVessel ship)=>"voyage:safety:"+ship.Instance+":R1:R2";
    private ArrivalHandoff ExpectedHandoff(BranchSnapshot s,SharedVessel ship)
    {
        var commission=Commission(s);var d=Package.DestinationDuty;var actor=Actor(s);
        return new(ship.Instance,"GLIMMER","R1","R2",d.Id,d.Scope,d.Scene,d.BerthSpot,Handover(s).ReceiptId,VoyageRules.Hash(commission.Json),commission.Revision,VoyageRules.Hash(Geometry[d.Scene]),Package.Version,Package.ReviewStatus,
            "N-DECKWATCH",actor.Scene,actor.Position,actor.ActionClock);
    }
    private ArrivalHandoff IssuedHandoff(BranchSnapshot s,SharedVessel ship)
    {
        var actual=BranchOperations.Read<ArrivalHandoff>(s,HandoffId(ship))??throw new InvalidOperationException("R2地方泊位值守交接尚未实际签发。");
        var receipt=s.Receipts.SingleOrDefault(r=>r.SemanticKey==HandoffId(ship)&&r.ActionType=="arrival_handoff_issued")??throw new InvalidOperationException("地方泊位交接缺少真正签发回执。");
        var expected=ExpectedHandoff(s,ship) with{InspectionScene=actual.InspectionScene,InspectionPosition=actual.InspectionPosition,InspectionActionClock=actual.InspectionActionClock};var chart=Geometry[Package.ChartScene].Spots.Single(p=>p.Id==Package.ChartSpot);
        if(actual!=expected||StrictJson.Deserialize<ArrivalHandoff>(receipt.ResultJson)!=actual||actual.InspectionScene!=Package.ChartScene||actual.InspectionPosition.Distance(chart.Stand)>2.5||actual.InspectionActionClock<=0||!new NavigationGrid(Geometry[Package.ChartScene]).SegmentClear(actual.InspectionPosition,chart.Stand,true))throw new InvalidOperationException("地方交接范围、来源、实际航图核对或目的泊位几何已变更。");
        return actual;
    }
    private VoyageSafetyBinding ExpectedBinding(BranchSnapshot s,SharedVessel ship)=>new(ship.Instance,"R1","R2",Package.Version,_packageHash,Handover(s).ReceiptId,HandoffId(ship),VoyageRules.Hash(Geometry[Package.ChartScene]),VoyageRules.Hash(Geometry["R1-S01"]),VoyageRules.Hash(Geometry["R2-S01"]));
    private ImmutableArray<EntityStamp> Gates(BranchSnapshot s,SharedVessel ship)
    {
        IssuedHandoff(s,ship);var binding=BranchOperations.Read<VoyageSafetyBinding>(s,BindingId(ship));
        if(binding!=ExpectedBinding(s,ship))throw new InvalidOperationException("航路实例及两端当前源几何的安全登记不符。");
        // Actor and ship revisions belong to command CAS, not permanent route gates: sailing changes both.
        return new[]{BindingId(ship),HandoffId(ship),"quest:Q-104"}.Select(id=>new EntityStamp(id,BranchOperations.Entity(s,id)!.Revision)).ToImmutableArray();
    }
    private string[] Reads(BranchSnapshot s,SharedVessel ship)=>s.Entities.Select(e=>e.Id).Concat(new[]{"actor","ship:glimmer","quest:Q-104","quest:Q-604","commission:cross-region","berth:R1","berth:R2","sea-route:R1:R2","ship-space:HUB-S02",HandoffId(ship),BindingId(ship)}).Distinct().ToArray();
    private CommitPlanCommand Command(BranchSnapshot s,SharedVessel ship,string key,string action,ImmutableArray<EntityChange> changes,object result)=>BranchOperations.Plan(_repository,s,key,action,changes,Reads(s,ship),result);
    private async ValueTask<CommandResult> Execute(CommitPlanCommand command)
    {
        if(_repository is not IEventLeaseStore leases)throw new InvalidOperationException("航图核查及值守签发需要原子前景占用接口。");
        string owner="voyage:"+command.Context.Command.Value;if(!await leases.TryLeasesAsync(command.Context.Load,owner,["input:foreground"]))throw new InvalidOperationException("另一项实际前景工作尚未交接。");
        try{Guard(await _repository.SnapshotAsync());return await _repository.ExecuteAsync(command);}finally{await leases.ReleaseLeasesAsync(command.Context.Load,owner);}
    }
    private async ValueTask<CommandResult?> Existing(BranchSnapshot s,string key)
    {
        var receipt=s.Receipts.SingleOrDefault(r=>r.SemanticKey==key);if(receipt is null)return null;var command=s.Commands.First(c=>c.ReceiptId==receipt.ReceiptId);
        return new(CommitStatus.AlreadyCommitted,await _repository.FindReceiptAsync(_repository.Session.Branch,new(Guid.Parse(command.CommandId))),[]);
    }
    public async ValueTask<CommandResult> IssueArrivalHandoffAsync(VoyageRequest request)
    {
        VoyageRules.Request(request);var s=await _repository.SnapshotAsync();Guard(s);var ship=Loan(s,true);Chart(s,ship);var handoff=ExpectedHandoff(s,ship);var binding=ExpectedBinding(s,ship);
        VoyageRules.SafeBerth(Geometry[handoff.BerthScene],handoff.BerthSpot);
        if(BranchOperations.Entity(s,HandoffId(ship)) is not null){IssuedHandoff(s,ship);Gates(s,ship);return (await Existing(s,HandoffId(ship)))!;}
        return await Execute(Command(s,ship,HandoffId(ship),"arrival_handoff_issued",[BranchOperations.Change(s,HandoffId(ship),"arrival_handoff",handoff),BranchOperations.Change(s,BindingId(ship),"voyage_safety",binding)],handoff));
    }
    private VoyagePreview Ready(CommitPlanCommand command,string description){Guid token=Guid.NewGuid();_previews[token]=command;return new(token,true,[],description);}
    private static VoyagePreview Unready(InvalidOperationException error)=>new(Guid.Empty,false,[error.Message],error.Message);
    public async ValueTask<VoyagePreview> PreviewRouteAsync(VoyageRequest request)
    {
        try
        {
            VoyageRules.Request(request);var s=await _repository.SnapshotAsync();Guard(s);var ship=Loan(s,true);Commission(s);Chart(s,ship);var gates=Gates(s,ship);
            return Ready(Command(s,ship,"voyage:route:"+ship.Instance+":"+Package.Version,"sea_route_verified",[],new SeaRoute("R1","R2",gates,"actual commissioned chart verification")),"核对同一巡灯船的跨区委托、当前泊位、航图站位与R2地方公开泊位交接；登记只含R1/R2的开放航路。");
        }catch(InvalidOperationException error){return Unready(error);}
    }
    public async ValueTask<VoyagePreview> PreviewBerthAsync(VoyageRequest request)
    {
        try
        {
            VoyageRules.Request(request);var s=await _repository.SnapshotAsync();Guard(s);var ship=Loan(s,true);Chart(s,ship);var handoff=IssuedHandoff(s,ship);var gates=Gates(s,ship);var berth=VoyageRules.SafeBerth(Geometry[handoff.BerthScene],handoff.BerthSpot);
            return Ready(Command(s,ship,"voyage:berth:"+ship.Instance+":"+Package.Version,"vessel_berth_verified",[],new VesselBerth(ship.Instance,"R2",handoff.BerthScene,berth.Stand,berth.Stand,gates,"actual limited local berth-duty handoff")),"核对R2地方签发的单船公开靠泊交接与真实安全登陆几何；不授予档案或未到场材料。");
        }catch(InvalidOperationException error){return Unready(error);}
    }
    public void ClosePreview(Guid token)=>_previews.Remove(token);
    public async ValueTask<CommandResult> ConfirmPreviewAsync(Guid token)=>_previews.TryGetValue(token,out var command)?await Execute(command):new(CommitStatus.Rejected,null,[new("preview_closed","voyage","核查清单已关闭或不存在，请按当前现场重新核对。")]);
    public async ValueTask<VoyageRegistration> RegisterVerifiedAsync(string routeReceipt,string berthReceipt)
    {
        var s=await _repository.SnapshotAsync();Guard(s);var ship=Loan(s,true);Chart(s,ship);var expected=Gates(s,ship);
        var route=StrictJson.Deserialize<SeaRoute>(BranchOperations.Evidence(s,routeReceipt,"sea_route_verified").ResultJson);var berth=StrictJson.Deserialize<VesselBerth>(BranchOperations.Evidence(s,berthReceipt,"vessel_berth_verified").ResultJson);var stand=VoyageRules.SafeBerth(Geometry["R2-S01"],VoyageRules.R2.LandingSpot).Stand;
        if(route.A!="R1"||route.B!="R2"||!route.GateReads.SequenceEqual(expected)||berth.Instance!=ship.Instance||berth.Region!="R2"||berth.Scene!="R2-S01"||berth.Landing!=stand||berth.Boarding!=stand||!berth.GateReads.SequenceEqual(expected))throw new InvalidOperationException("所引用的核查回执不属于本次委托、船实例与源泊位版本。");
        var registeredRoute=await _travel.RegisterSeaRouteAsync(routeReceipt);
        if(registeredRoute.Status is not(CommitStatus.Committed or CommitStatus.AlreadyCommitted))return new(registeredRoute,new(CommitStatus.Rejected,null,[new("route_register_failed","voyage","航路尚未实际登记；泊位注册未执行。") ]));
        // Each existing registration is its own atomic transaction; an interruption never fabricates the other result.
        s=await _repository.SnapshotAsync();Guard(s);Chart(s,Loan(s,true));
        return new(registeredRoute,await _travel.RegisterBerthAsync(berthReceipt));
    }
    public async ValueTask<VoyagePreparation> VerifyAndRegisterAsync(VoyageRequest request)
    {
        // Explicit user action can compose real commands; it never skips a producer or invents a receipt.
        static void Committed(CommandResult r){if(r.Status is not(CommitStatus.Committed or CommitStatus.AlreadyCommitted)||r.Receipt is null)throw new InvalidOperationException("本次核查未可靠提交："+StrictJson.Serialize(r));}
        var handoff=await IssueArrivalHandoffAsync(request);Committed(handoff);
        var routePreview=await PreviewRouteAsync(request);if(!routePreview.Ready)throw new InvalidOperationException(string.Join("；",routePreview.Issues));
        CommandResult route;
        try{route=await ConfirmPreviewAsync(routePreview.Token);Committed(route);}finally{ClosePreview(routePreview.Token);}
        var berthPreview=await PreviewBerthAsync(request);if(!berthPreview.Ready)throw new InvalidOperationException(string.Join("；",berthPreview.Issues));
        CommandResult berth;
        try{berth=await ConfirmPreviewAsync(berthPreview.Token);Committed(berth);}finally{ClosePreview(berthPreview.Token);}
        var registration=await RegisterVerifiedAsync(route.Receipt!.Id.Value.ToString(),berth.Receipt!.Id.Value.ToString());Committed(registration.Route);Committed(registration.Berth);
        return new(handoff,route,berth,registration);
    }
    public async ValueTask<CommandResult> SailAsync(string destination)
    {
        if(destination!="R2")throw new InvalidOperationException("本生产桥只制作第一次R1到R2；后续地区没有在此获得旅行授权。");
        var s=await _repository.SnapshotAsync();Guard(s);var ship=Loan(s,true);Commission(s);Chart(s,ship);RegisteredDestination(s,ship,Gates(s,ship));
        // Travel performs current route/berth/space checks and acquires its own foreground lease and atomic CAS.
        return await _travel.SailAsync(destination);
    }
    private (SeaRoute Route,VesselBerth Berth) RegisteredDestination(BranchSnapshot s,SharedVessel ship,ImmutableArray<EntityStamp> gates)
    {
        var route=BranchOperations.Read<SeaRoute>(s,"sea-route:R1:R2")??throw new InvalidOperationException("实际航路登记缺失。");var berth=BranchOperations.Read<VesselBerth>(s,"berth:R2")??throw new InvalidOperationException("实际登陆泊位登记缺失。");
        var rs=StrictJson.Deserialize<SeaRoute>(BranchOperations.Evidence(s,route.AuthorityReceipt,"sea_route_verified").ResultJson);var bs=StrictJson.Deserialize<VesselBerth>(BranchOperations.Evidence(s,berth.AuthorityReceipt,"vessel_berth_verified").ResultJson);
        var stand=VoyageRules.SafeBerth(Geometry["R2-S01"],VoyageRules.R2.LandingSpot).Stand;
        if(route.A!="R1"||route.B!="R2"||!route.GateReads.SequenceEqual(gates)||rs.A!=route.A||rs.B!=route.B||!rs.GateReads.SequenceEqual(route.GateReads)||berth.Instance!=ship.Instance||berth.Region!="R2"||berth.Scene!="R2-S01"||berth.Landing!=stand||berth.Boarding!=stand||!berth.GateReads.SequenceEqual(gates)||StrictJson.Serialize(bs with{AuthorityReceipt=berth.AuthorityReceipt})!=StrictJson.Serialize(berth))throw new InvalidOperationException("当前航路、单船泊位或原始登记回执不属于本次实际航图核验。");
        return (route,berth);
    }
    public async ValueTask<CommandResult> RecordArrivalAsync()
    {
        var s=await _repository.SnapshotAsync();Guard(s);var ship=Loan(s,false);Commission(s);var gates=Gates(s,ship);var actor=Actor(s);var stand=VoyageRules.SafeBerth(Geometry["R2-S01"],VoyageRules.R2.LandingSpot).Stand;
        if(ship.DockRegion!="R2")throw new InvalidOperationException("当前巡灯船尚未实际航行到R2。");At(s,"R2-S01",stand);
        var (route,berth)=RegisteredDestination(s,ship,gates);
        var sail=s.Receipts.Where(r=>r.ActionType=="sail_shared_glimmer").OrderByDescending(r=>r.Sequence).FirstOrDefault()??throw new InvalidOperationException("没有实际巡灯船航行回执。");using var doc=JsonDocument.Parse(sail.ResultJson);var j=doc.RootElement;
        if(j.GetProperty("ship_instance").GetGuid()!=ship.Instance||j.GetProperty("from").GetString()!="R1"||j.GetProperty("to").GetString()!="R2"||sail.Sequence<=Handover(s).Sequence)throw new InvalidOperationException("本次登陆不能由改角色场景或其他船的航行代替。");
        string key="voyage:arrival:"+ship.Instance+":R2";var prior=await Existing(s,key);if(prior is not null)return prior;
        var handoff=s.Receipts.Single(r=>r.SemanticKey==HandoffId(ship));var proof=new VesselArrivalProof(ship.Instance,"R1","R2",actor.Scene,actor.Position,actor.ActionClock,sail.ReceiptId,route.AuthorityReceipt,berth.AuthorityReceipt,handoff.ReceiptId,VoyageRules.Hash(Geometry[actor.Scene]),Package.Version,"public_landing_only_not_local_registration");
        return await Execute(Command(s,ship,key,"actual_vessel_arrival",[BranchOperations.Change(s,key,"vessel_arrival",proof)],proof));
    }
}

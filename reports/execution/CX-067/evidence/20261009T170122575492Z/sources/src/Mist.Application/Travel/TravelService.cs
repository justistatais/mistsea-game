using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.EventScheduler;
using Mist.Application.Q102;
using Mist.Application.Storage;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Engineering;
using Mist.Core.Exploration;

namespace Mist.Application.Travel;

public enum ReturnAnchorKind { Settlement,TowerSafeExit }
public sealed record ReturnAnchor(string Scene,Point2 Position,ReturnAnchorKind Kind,string AuthorityReceipt);
public sealed record SharedVessel(Guid Instance,string DockRegion,string WatchOfficer,ImmutableArray<string> Spaces);
// The movement producer issues this in the same transaction as the physical actor transition.
public sealed record TraversedConnection(string From,string To,string FromPort,string ToPort,Point2 FromPosition,Point2 ToPosition,
    long ActionClock,string ConnectionContract,ImmutableArray<EntityStamp> GateReads,bool ReverseAllowed);
public sealed record WalkedRoute(string From,string To,string SourceReceipt,TraversedConnection? Proof=null);
public sealed record ConnectionSafetyState(string A,string B,string Contract,bool AllowAToB,bool AllowBToA,
    ImmutableArray<EntityStamp> Dependencies,string AuthorityReceipt);
public sealed record SeaRoute(string A,string B,ImmutableArray<EntityStamp> GateReads,string AuthorityReceipt);
public sealed record VesselBerth(Guid Instance,string Region,string Scene,Point2 Landing,Point2 Boarding,
    ImmutableArray<EntityStamp> GateReads,string AuthorityReceipt);
public sealed record VesselSpace(Guid Instance,string Scene,Point2 Landing,Point2 TransferStand,
    ImmutableArray<EntityStamp> GateReads,string AuthorityReceipt);
public sealed record TransportSegment(string Id,string From,string To,bool CargoInTransit,bool Completed);

public sealed partial class TravelService
{
    private readonly IAdventureRepository repository;
    private readonly RuntimeCatalog catalog;
    private static readonly string[] ShipSpaces=["HUB-S01","HUB-S02","HUB-S03"];
    private static readonly ImmutableDictionary<string,string> TowerScenes=new Dictionary<string,string>
    { ["R1-S10"]="TWR-R1",["R2-S08"]="TWR-R2",["R3-S09"]="TWR-R3",["R4-S08"]="TWR-R4",["R5-S08"]="TWR-R5",["R6-S07"]="TWR-R6" }.ToImmutableDictionary();
    private static readonly string[] SettlementScenes=["R1-S01","R1-S02","R2-S01","R2-S02","R3-S01","R3-S02","R4-S01","R4-S02","R5-S01","R5-S02","R6-S01","R6-S02"];
    private readonly ImmutableDictionary<string,SceneGeometry> _q102Geometry;
    private readonly ImmutableDictionary<string,SceneGeometry> _geometry;
    public ImmutableDictionary<string,SceneGeometry> Geometry=>_geometry;
    // Later regions must supply their actual current geometry. Scene registration alone never establishes collision safety.
    public TravelService(IAdventureRepository repository,RuntimeCatalog catalog,IReadOnlyDictionary<string,SceneGeometry>? authoritativeGeometry=null)
    {
        this.repository=repository;this.catalog=catalog;_q102Geometry=new Q102Service(repository,catalog).Geometry;
        if(authoritativeGeometry is not null&&authoritativeGeometry.Any(p=>p.Key!=p.Value.Id||!catalog.Scenes.Any(s=>s.Id==p.Key)))throw new InvalidDataException("旅行几何的场景身份未登记。");
        _geometry=authoritativeGeometry is null?_q102Geometry:_q102Geometry.SetItems(authoritativeGeometry);
    }
    public static string Contract(ConnectionDefinition c)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(StrictJson.Serialize(c)))).ToLowerInvariant();
    public static string ConnectionKey(string a,string b)=>string.CompareOrdinal(a,b)<0?a+":"+b:b+":"+a;
    private static ActorState Actor(BranchSnapshot s)=>BranchOperations.Read<ActorState>(s,"actor")??throw new InvalidOperationException("角色现场状态不可用。");
    private static void Safe(BranchSnapshot s)
    {
        BranchOperations.Safe(s);
        if(s.Entities.Where(e=>e.Kind=="transport_segment").Select(e=>StrictJson.Deserialize<TransportSegment>(e.Json)).Any(v=>v.CargoInTransit&&!v.Completed))throw new InvalidOperationException("当前货物仍在护送行程中，请先完成规定安全路段，不能通过快捷旅行移动它。");
        if(s.Entities.Where(e=>e.Kind=="event").Select(e=>StrictJson.Deserialize<EventSession>(e.Json)).Any(e=>e.State==EventLifecycle.Running))throw new InvalidOperationException("当前前景工作尚未在安全检查点交接，请先完成或暂停该工作。");
    }
    private static void FinalStage(BranchSnapshot s,string from,string to)
    {
        if(BranchOperations.Receipt(s,"Q-604")&&(!from.StartsWith("RF-",StringComparison.Ordinal)||!to.StartsWith("RF-",StringComparison.Ordinal)))throw new InvalidOperationException("已确认终章起航，外区旅行暂停；可从保留的终章前存档继续探索。");
    }
    private static string[] TravelReads(BranchSnapshot s)=>s.Entities.Where(e=>e.Kind is "transport_segment" or "event").Select(e=>e.Id).Concat(new[]{"quest:Q-604","campaign"}).ToArray();
    private void Stand(string scene,Point2 position,bool avoidDanger=true)
    {
        if(!catalog.Scenes.Any(s=>s.Id==scene)||!double.IsFinite(position.X)||!double.IsFinite(position.Y)||position.X is <0 or >100||position.Y is <0 or >100)throw new InvalidOperationException("安全落点未登记在当前内容中。");
        if(!_geometry.TryGetValue(scene,out var g))throw new InvalidOperationException("该处的实际碰撞与危险几何尚未接入，不能猜测安全落点。");
        if(!new NavigationGrid(g).CanStand(position,avoidDanger))throw new InvalidOperationException("登记落点仍有碰撞或现场危险。");
    }
    private static void At(ActorState actor,string scene,Point2 position)
    {
        if(actor.Scene!=scene||actor.Position.Distance(position)>2.5)throw new InvalidOperationException("请先实际走到登记的旅行站位。");
    }
    private ConnectionDefinition Connection(string a,string b)=>catalog.Connections.SingleOrDefault(c=>c.A==a&&c.B==b||c.A==b&&c.B==a)??throw new InvalidOperationException("这两个场景之间没有登记连接，不能折叠成路径。");
    private static bool Current(BranchSnapshot s,ImmutableArray<EntityStamp> reads,bool preserveTower=false)
    {
        if(reads.IsDefault||reads.Select(r=>r.EntityId).Distinct().Count()!=reads.Length)return false;
        return reads.All(r=>BranchOperations.Entity(s,r.EntityId) is {} entity&&(entity.Revision==r.Revision||preserveTower&&entity.Kind=="tower_readiness"&&StrictJson.Deserialize<TowerReadiness>(entity.Json).Safe));
    }
    private string[] RequiredQ102(string a,string b)
    {
        if(!_q102Geometry.ContainsKey(a)||!_q102Geometry.ContainsKey(b))return [];
        var ids=new List<string>{"permission:Q-102"};if(a=="R1-S04"||b=="R1-S04")ids.Add("ferry");if(a=="R1-S06"||b=="R1-S06")ids.Add("workshop");return ids.ToArray();
    }
    private bool Q102Gate(BranchSnapshot s,string a,string b)
    {
        if(!_q102Geometry.ContainsKey(a)||!_q102Geometry.ContainsKey(b))return true;
        var permission=BranchOperations.Entity(s,"permission:Q-102");if(permission is null)return false;
        using(var p=System.Text.Json.JsonDocument.Parse(permission.Json))if(!p.RootElement.GetProperty("allowed").GetBoolean())return false;
        if((a=="R1-S04"||b=="R1-S04")&&BranchOperations.Read<FerryState>(s,"ferry") is not{Docked:true})return false;
        var w=BranchOperations.Read<WorkshopState>(s,"workshop");string edge=ConnectionKey(a,b);
        if(edge==ConnectionKey("R1-S06","R1-S02")&&w is not{LatchOpen:true})return false;
        if(edge==ConnectionKey("R1-S06","R1-S07")&&w is not{SideOpen:true})return false;
        if(edge==ConnectionKey("R1-S06","R1-S05")&&w is not{FrontSafe:true})return false;
        return true;
    }
    public async ValueTask<CommandResult> RegisterConnectionSafetyAsync(string receiptId)
    {
        var s=await repository.SnapshotAsync();var r=BranchOperations.Evidence(s,receiptId,"connection_safety_verified");var safety=StrictJson.Deserialize<ConnectionSafetyState>(r.ResultJson);var c=Connection(safety.A,safety.B);
        if(safety.A!=c.A||safety.B!=c.B||safety.Contract!=Contract(c)||!Current(s,safety.Dependencies)||(!safety.AllowAToB&&!safety.AllowBToA))throw new InvalidOperationException("连接安全回执与当前门禁、方向或版本不符。");
        string id="travel-gate:"+ConnectionKey(c.A,c.B);return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,"travel-safety:"+receiptId,"register_connection_safety",[BranchOperations.Change(s,id,"travel_gate",safety with{AuthorityReceipt=receiptId})],safety.Dependencies.Select(d=>d.EntityId).Append(id).ToArray(),new{source_receipt=receiptId}));
    }
    private bool CanUse(BranchSnapshot s,TraversedConnection p,string from,string to)
    {
        if(!(from==p.From&&to==p.To||from==p.To&&to==p.From&&p.ReverseAllowed))return false;
        var c=Connection(p.From,p.To);if(p.ConnectionContract!=Contract(c)||!Current(s,p.GateReads,true)||!Q102Gate(s,from,to)||from!=p.From&&!p.ReverseAllowed)return false;
        if(_q102Geometry.ContainsKey(p.From)&&_q102Geometry.ContainsKey(p.To))return RequiredQ102(p.From,p.To).All(id=>p.GateReads.Any(r=>r.EntityId==id));
        string id="travel-gate:"+ConnectionKey(c.A,c.B);var access=BranchOperations.Read<ConnectionSafetyState>(s,id);
        if(access is null||!p.GateReads.Any(r=>r.EntityId==id)||access.Contract!=Contract(c)||!Current(s,access.Dependencies,true))return false;
        var source=StrictJson.Deserialize<ConnectionSafetyState>(BranchOperations.Evidence(s,access.AuthorityReceipt,"connection_safety_verified").ResultJson);
        if(source.A!=access.A||source.B!=access.B||source.Contract!=access.Contract||source.AllowAToB!=access.AllowAToB||source.AllowBToA!=access.AllowBToA||!source.Dependencies.SequenceEqual(access.Dependencies))return false;
        return from==c.A?access.AllowAToB:access.AllowBToA;
    }
    public async ValueTask<CommandResult> RecordWalkedRouteAsync(string transitionReceipt)
    {
        var s=await repository.SnapshotAsync();Safe(s);var receipt=BranchOperations.Evidence(s,transitionReceipt,"scene_transition");var p=StrictJson.Deserialize<TraversedConnection>(receipt.ResultJson);Connection(p.From,p.To);
        var fromNavigation=Navigation(s,p.From);var toNavigation=Navigation(s,p.To);
        if(fromNavigation is null||toNavigation is null||!fromNavigation.CanStand(p.FromPosition,true)||!toNavigation.CanStand(p.ToPosition,true))throw new InvalidOperationException("实际通过的站位缺少几何、碰撞可达性或当前危险仍未解除。");
        var actor=Actor(s);
        if(p.ActionClock<1||string.IsNullOrWhiteSpace(p.FromPort)||string.IsNullOrWhiteSpace(p.ToPort)||actor.Scene!=p.To||actor.Position!=p.ToPosition||actor.ActionClock!=p.ActionClock||!Current(s,p.GateReads)||!CanUse(s,p,p.From,p.To))throw new InvalidOperationException("只有刚实际走通、当前仍安全的登记连接能成为返程路径。");
        if(!PairedPorts(s,p,out _,out _))throw new InvalidOperationException("回执没有经过实际配对出口及当前安全登记落点。");
        var walked=new WalkedRoute(p.From,p.To,transitionReceipt,p);string id="walked:"+p.From+":"+p.To;
        return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,"walked-source:"+transitionReceipt,"record_actual_walked_connection",[BranchOperations.Change(s,id,"walked_route",walked)],p.GateReads.Select(r=>r.EntityId).Concat(new[]{"actor",id}).Concat(TravelReads(s)).ToArray(),new{source_receipt=transitionReceipt}));
    }
    public async ValueTask<SharedVessel?> VesselAsync()=>BranchOperations.Read<SharedVessel>(await repository.SnapshotAsync(),"ship:glimmer");
    public async ValueTask<ImmutableArray<ReturnAnchor>> AnchorsAsync(){var s=await repository.SnapshotAsync();return s.Entities.Where(e=>e.Kind=="return_anchor").Select(e=>StrictJson.Deserialize<ReturnAnchor>(e.Json)).ToImmutableArray();}
    private static string? Tower(ReturnAnchor anchor)=>anchor.Kind==ReturnAnchorKind.TowerSafeExit&&TowerScenes.TryGetValue(anchor.Scene,out string? tower)?tower:null;
    private static bool AnchorSafe(BranchSnapshot s,ReturnAnchor anchor)=>anchor.Kind switch
    {
        ReturnAnchorKind.Settlement=>SettlementScenes.Contains(anchor.Scene),
        ReturnAnchorKind.TowerSafeExit=>Tower(anchor) is{} tower&&BranchOperations.Read<TowerReadiness>(s,"readiness:"+tower) is{Safe:true},
        _=>false
    };
    public async ValueTask<CommandResult> RegisterAnchorAsync(string verifiedSafetyReceipt)
    {
        var s=await repository.SnapshotAsync();Safe(s);var receipt=BranchOperations.Evidence(s,verifiedSafetyReceipt,"return_anchor_verified");var anchor=StrictJson.Deserialize<ReturnAnchor>(receipt.ResultJson);Stand(anchor.Scene,anchor.Position);At(Actor(s),anchor.Scene,anchor.Position);FinalStage(s,anchor.Scene,anchor.Scene);
        if(!AnchorSafe(s,anchor))throw new InvalidOperationException("这里只能登记生活中心或已实际安全的塔站出口。");
        var actual=anchor with{AuthorityReceipt=verifiedSafetyReceipt};string id="anchor:"+anchor.Scene;
        return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,"return-anchor-source:"+verifiedSafetyReceipt,"register_safe_return_anchor",[BranchOperations.Change(s,id,"return_anchor",actual)],new[]{id,"actor"}.Concat(Tower(anchor) is{} t?new[]{"readiness:"+t}:[]).Concat(TravelReads(s)).ToArray(),new{source_receipt=verifiedSafetyReceipt}));
    }
    public async ValueTask<CommandResult> ReturnAsync(string destination)
    {
        var s=await repository.SnapshotAsync();Safe(s);var actor=Actor(s);FinalStage(s,actor.Scene,destination);var anchor=BranchOperations.Read<ReturnAnchor>(s,"anchor:"+destination)??throw new InvalidOperationException("该处尚未取得安全返程点。");Stand(anchor.Scene,anchor.Position);
        var source=StrictJson.Deserialize<ReturnAnchor>(BranchOperations.Evidence(s,anchor.AuthorityReceipt,"return_anchor_verified").ResultJson);
        if(source.Scene!=anchor.Scene||source.Position!=anchor.Position||source.Kind!=anchor.Kind||!AnchorSafe(s,anchor))throw new InvalidOperationException("返程点当前安全条件尚未成立。");
        if(!Reachable(s,actor,anchor))throw new InvalidOperationException("已走路径当前有门禁、单向阻断、内部障碍、缺失几何或尚未解除的危险，不能合并返程。");
        var reads=s.Entities.Where(e=>e.Kind is "walked_route" or "travel_gate").Select(e=>e.Id).Concat(s.Entities.Where(e=>e.Kind=="walked_route").Select(e=>StrictJson.Deserialize<WalkedRoute>(e.Json)).Where(r=>r.Proof is not null).SelectMany(r=>r.Proof!.GateReads).Select(r=>r.EntityId)).Concat(s.Entities.Where(e=>e.Kind=="travel_gate").SelectMany(e=>StrictJson.Deserialize<ConnectionSafetyState>(e.Json).Dependencies).Select(r=>r.EntityId)).Concat(new[]{"actor","anchor:"+destination}).Concat(Tower(anchor) is{} t?new[]{"readiness:"+t}:[]).Concat(TravelReads(s)).ToArray();
        return await RelocateAsync(BranchOperations.Plan(repository,s,"return:"+Guid.NewGuid(),"return_along_walked_safe_path",[BranchOperations.Change(s,"actor","actor",actor with{Scene=destination,Position=anchor.Position,ActionClock=checked(actor.ActionClock+1)})],reads,new{from=actor.Scene,to=destination}));
    }
    private async ValueTask<CommandResult> RelocateAsync(CommitPlanCommand command)
    {
        if(repository is not IEventLeaseStore leases)throw new InvalidOperationException("旅行需要原子的前景占用接口。");
        string owner="travel:"+command.Context.Command.Value;
        if(!await leases.TryLeasesAsync(command.Context.Load,owner,["input:foreground"]))throw new InvalidOperationException("另一项前景工作已取得操作权，请先完成安全交接。");
        try{return await repository.ExecuteAsync(command);}finally{await leases.ReleaseLeasesAsync(command.Context.Load,owner);}
    }
    private NavigationGrid? Navigation(BranchSnapshot s,string scene)
    {
        if(!_geometry.TryGetValue(scene,out var geometry))return null;
        if(scene=="R1-S06"&&BranchOperations.Read<WorkshopState>(s,"workshop") is{FrontSafe:true}&&_q102Geometry.TryGetValue(scene,out var original))
        {
            // Only the original mechanical scan strip is cleared by the real device state. Additional hazards remain hazardous.
            geometry=geometry with{Danger=geometry.Danger.Where(d=>!original.Danger.Contains(d)).ToImmutableArray()};
        }
        return new(geometry);
    }
    private bool SafePath(BranchSnapshot s,string scene,Point2 from,Point2 to)
    {
        if(!double.IsFinite(from.X)||!double.IsFinite(from.Y)||!double.IsFinite(to.X)||!double.IsFinite(to.Y))return false;
        var nav=Navigation(s,scene);return nav is not null&&nav.FindPath(from,to,true).Length>0;
    }
    private bool PairedPorts(BranchSnapshot s,TraversedConnection proof,out Port? from,out Port? to)
    {
        from=null;to=null;
        if(!_geometry.TryGetValue(proof.From,out var a)||!_geometry.TryGetValue(proof.To,out var b))return false;
        from=a.Ports.SingleOrDefault(p=>p.Id==proof.FromPort&&p.TargetScene==proof.To&&p.TargetPort==proof.ToPort);
        to=b.Ports.SingleOrDefault(p=>p.Id==proof.ToPort&&p.TargetScene==proof.From&&p.TargetPort==proof.FromPort);
        if(from is null||to is null||from.Stand.Distance(proof.FromPosition)>2.5||to.Stand!=proof.ToPosition)return false;
        var source=Navigation(s,proof.From);var target=Navigation(s,proof.To);
        return source is not null&&target is not null&&source.CanStand(from.Stand,true)&&target.CanStand(to.Stand,true);
    }
    private bool Reachable(BranchSnapshot s,ActorState actor,ReturnAnchor destination)
    {
        var routes=s.Entities.Where(e=>e.Kind=="walked_route").Select(e=>StrictJson.Deserialize<WalkedRoute>(e.Json)).Where(r=>r.Proof is not null).ToArray();
        var initial=(Scene:actor.Scene,Entry:actor.Position);var seen=new HashSet<(string Scene,Point2 Entry)>{initial};var queue=new Queue<(string Scene,Point2 Entry)>();queue.Enqueue(initial);
        while(queue.TryDequeue(out var node))
        {
            if(node.Scene==destination.Scene&&SafePath(s,node.Scene,node.Entry,destination.Position))return true;
            foreach(var route in routes.Where(r=>r.From==node.Scene||r.To==node.Scene))
            {
                var p=route.Proof!;if(route.From!=p.From||route.To!=p.To)continue;
                var receipt=s.Receipts.SingleOrDefault(r=>r.ReceiptId==route.SourceReceipt&&r.ActionType=="scene_transition");if(receipt is null||receipt.ResultJson!=StrictJson.Serialize(p)||!PairedPorts(s,p,out var fromPort,out var toPort))continue;
                bool forward=route.From==node.Scene;string other=forward?route.To:route.From;var exit=forward?fromPort!.Stand:toPort!.Stand;var arrival=forward?toPort!.Stand:fromPort!.Stand;
                if(!CanUse(s,p,node.Scene,other)||!SafePath(s,node.Scene,node.Entry,exit))continue;
                var next=(Scene:other,Entry:arrival);if(seen.Add(next))queue.Enqueue(next);
            }
        }
        return false;
    }
}

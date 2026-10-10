using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mist.Application.ChapterOne;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.EventScheduler;
using Mist.Application.Roster;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Core.ChapterOne;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.VesselInterior;

namespace Mist.Application.VesselInterior;

public sealed record VesselInteriorPreview(Guid Token,bool Ready,ImmutableArray<string> Issues,string Description);
public sealed record VesselTransfer(Guid Instance,string From,string To,string FromPort,string ToPort,Point2 FromPosition,Point2 ToPosition,long ActionClock,string GeometryVersion);
public sealed record VesselWorkspaceView(VesselStation Station,SharedVessel Ship,ImmutableArray<StoredEntity> Entities,
    ImmutableArray<StoredItem> Items,ImmutableDictionary<string,long> Balances,ImmutableArray<StoredHead> DraftHeads,ImmutableArray<RosterView> Roster,ImmutableArray<string> Remaining);

// Owns only local placement inspection, ship-space geometry evidence and the actor's physical movement.
// It never creates a ship, grants a sea route, recruits a companion or advances an engineering work order.
public sealed class VesselInteriorService
{
    private readonly IAdventureRepository _repository;
    private readonly RuntimeCatalog _catalog;
    private readonly Dictionary<Guid,CommitPlanCommand> _previews=new();
    private readonly string _geometryHash;
    public VesselInteriorPackage Package {get;}
    public ImmutableDictionary<string,SceneGeometry> Geometry {get;}
    public VesselInteriorService(IAdventureRepository repository,RuntimeCatalog catalog,VesselInteriorPackage package,IReadOnlyDictionary<string,SceneGeometry> authoritativeGeometry)
    {
        VesselInteriorRules.Validate(package);_repository=repository;_catalog=catalog;Package=package;
        if(authoritativeGeometry.Any(p=>p.Key!=p.Value.Id||!catalog.Scenes.Any(s=>s.Id==p.Key)))throw new InvalidDataException("共享船接入的外部几何身份不符。");
        if(package.Scenes.Any(s=>!catalog.Scenes.Any(c=>c.Id==s.Id)))throw new InvalidDataException("船舱场景未登记在本内容版本中。");
        Geometry=authoritativeGeometry.ToImmutableDictionary().SetItems(package.Scenes.ToDictionary(s=>s.Id));
        _geometryHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(StrictJson.Serialize(package.Scenes)))).ToLowerInvariant();
    }
    public ValueTask<BranchSnapshot> SnapshotAsync()=>_repository.SnapshotAsync();
    private static ActorState Actor(BranchSnapshot s)=>BranchOperations.Read<ActorState>(s,"actor")??throw new InvalidOperationException("当前角色现场状态不存在。");
    private static string BindingId(SharedVessel ship)=>"vessel:interior:"+ship.Instance;
    private static string[] GuardReads(BranchSnapshot s)=>s.Entities.Where(e=>e.Kind is "transport_segment" or "event" or "vessel_space" or "vessel_berth").Select(e=>e.Id)
        .Concat(s.Entities.Where(e=>e.Kind=="vessel_space").SelectMany(e=>StrictJson.Deserialize<VesselSpace>(e.Json).GateReads).Select(r=>r.EntityId))
        .Concat(s.Entities.Where(e=>e.Kind=="vessel_berth").SelectMany(e=>StrictJson.Deserialize<VesselBerth>(e.Json).GateReads).Select(r=>r.EntityId))
        .Concat(new[]{"actor","ship:glimmer","quest:Q-104","quest:Q-604","campaign","combat:shore"}).ToArray();
    private static void Guard(BranchSnapshot s)
    {
        BranchOperations.Safe(s);
        if(BranchOperations.Receipt(s,"Q-604"))throw new InvalidOperationException("已确认终章起航，船舱与外区通行暂停；请使用保留的起航前存档。");
        if(s.Entities.Where(e=>e.Kind=="transport_segment").Select(e=>StrictJson.Deserialize<TransportSegment>(e.Json)).Any(t=>t.CargoInTransit&&!t.Completed))throw new InvalidOperationException("当前有实际货物护送，请先完成规定安全路段。");
        if(s.Entities.Where(e=>e.Kind=="event").Select(e=>StrictJson.Deserialize<EventSession>(e.Json)).Any(e=>e.State==EventLifecycle.Running))throw new InvalidOperationException("当前前景工作需要先在安全检查点交接。");
    }
    private SharedVessel Ship(BranchSnapshot s)
    {
        var ship=BranchOperations.Read<SharedVessel>(s,"ship:glimmer")??throw new InvalidOperationException("微光号使用权尚未实际移交。");
        var receipt=s.Receipts.SingleOrDefault(r=>r.SemanticKey=="Q-104:resolution"&&r.ActionType=="tidegate_vessel_handover")??throw new InvalidOperationException("需要真实Q104船务移交回执，不能以地图发现或测试赠船代替。");
        var handover=StrictJson.Deserialize<TidegateHandover>(receipt.ResultJson);
        if(ship.Instance==Guid.Empty||!Guid.TryParse(handover.VesselInstance,out var instance)||instance!=ship.Instance||handover.VesselDefinition!="GLIMMER"||handover.WatchOfficer!="N-DECKWATCH"||ship.WatchOfficer!=handover.WatchOfficer||!ship.Spaces.SequenceEqual(VesselInteriorRules.Spaces)||!handover.Spaces.SequenceEqual(ship.Spaces)||s.Entities.Count(e=>e.Kind=="shared_vessel")!=1)
            throw new InvalidOperationException("当前共享船实例、产权来源、值守岗位或三空间不一致。");
        return ship;
    }
    private StoredReceipt Handover(BranchSnapshot s)=>s.Receipts.Single(r=>r.SemanticKey=="Q-104:resolution"&&r.ActionType=="tidegate_vessel_handover");
    private VesselInteriorBinding Binding(BranchSnapshot s,SharedVessel ship)
    {
        var binding=BranchOperations.Read<VesselInteriorBinding>(s,BindingId(ship))??throw new InvalidOperationException("船舱当前实例的几何登记尚未完成。");
        if(binding.Instance!=ship.Instance||binding.PackageVersion!=Package.Version||binding.GeometrySha256!=_geometryHash||binding.HandoverReceipt!=Handover(s).ReceiptId)throw new InvalidOperationException("船舱登记来源或当前几何版本不一致，请重新核对。");
        return binding;
    }
    private void At(BranchSnapshot s,string scene,Point2 point)
    {
        var actor=Actor(s);if(actor.Scene!=scene||actor.Position.Distance(point)>2.5||!Geometry.TryGetValue(scene,out var g)||!new NavigationGrid(g).CanStand(actor.Position,true)||!new NavigationGrid(g).CanStand(point,true))throw new InvalidOperationException("请沿当前可走路径实际抵达该操作站位。");
    }
    private Spot Spot(string id)=>Geometry.Values.SelectMany(s=>s.Spots).SingleOrDefault(s=>s.Id==id)??throw new InvalidOperationException("当前实际几何没有这个工位。");
    private static bool Current(BranchSnapshot s,ImmutableArray<EntityStamp> reads)=>!reads.IsDefault&&reads.Select(r=>r.EntityId).Distinct().Count()==reads.Length&&reads.All(r=>BranchOperations.Entity(s,r.EntityId)?.Revision==r.Revision);
    private VesselBerth Berth(BranchSnapshot s,SharedVessel ship)
    {
        var berth=BranchOperations.Read<VesselBerth>(s,"berth:"+ship.DockRegion)??throw new InvalidOperationException("当前船的合法泊位尚未实际检查及登记。");
        var source=StrictJson.Deserialize<VesselBerth>(BranchOperations.Evidence(s,berth.AuthorityReceipt,"vessel_berth_verified").ResultJson);
        if(berth.Instance!=ship.Instance||berth.Region!=ship.DockRegion||berth.Scene!=ship.DockRegion+"-S01"||StrictJson.Serialize(source with{AuthorityReceipt=berth.AuthorityReceipt})!=StrictJson.Serialize(berth)||!Current(s,berth.GateReads)||!Geometry.TryGetValue(berth.Scene,out var geometry)||!new NavigationGrid(geometry).CanStand(berth.Landing,true)||!new NavigationGrid(geometry).CanStand(berth.Boarding,true))throw new InvalidOperationException("当前泊位的船实例、来源、安全站位或版本已变更。");
        return berth;
    }
    private (Point2 Landing,Point2 Transfer) Placement(string scene)=>scene switch
    {
        "HUB-S01"=>(Spot("HUB-S01.gangway").Stand,Spot("HUB-S01.gangway").Stand),
        "HUB-S02"=>(Geometry[scene].Ports.Single(p=>p.TargetScene=="HUB-S01").Stand,Spot("HUB-S02.chart").Stand),
        "HUB-S03"=>(Geometry[scene].Ports.Single(p=>p.TargetScene=="HUB-S02").Stand,Spot("HUB-S03.workbench").Stand),
        _=>throw new InvalidOperationException("不属于当前微光号的三个空间。")
    };
    private VesselSpace Space(BranchSnapshot s,SharedVessel ship,string scene)
    {
        Binding(s,ship);if(!ship.Spaces.Contains(scene))throw new InvalidOperationException("该空间不属于当前船。");
        var space=BranchOperations.Read<VesselSpace>(s,"ship-space:"+scene)??throw new InvalidOperationException("当前船内空间尚未登记实际几何。");var source=StrictJson.Deserialize<VesselSpace>(BranchOperations.Evidence(s,space.AuthorityReceipt,"ship_space_verified").ResultJson);var expected=Placement(scene);
        if(space.Instance!=ship.Instance||space.Scene!=scene||space.Landing!=expected.Landing||space.TransferStand!=expected.Transfer||!Current(s,space.GateReads)||!space.GateReads.Any(r=>r.EntityId==BindingId(ship))||StrictJson.Serialize(source with{AuthorityReceipt=space.AuthorityReceipt})!=StrictJson.Serialize(space))throw new InvalidOperationException("船舱来源、实例、实际落点或几何版本已变更。");
        return space;
    }
    private CommitPlanCommand Command(BranchSnapshot s,string key,string action,ImmutableArray<EntityChange> changes,IEnumerable<string> reads,object result)=>BranchOperations.Plan(_repository,s,key,action,changes,reads.Concat(GuardReads(s)).Distinct().ToArray(),result);
    private async ValueTask<CommandResult> Execute(CommitPlanCommand command)
    {
        if(_repository is not IEventLeaseStore leases)throw new InvalidOperationException("船舱实际操作需要原子的前景占用接口。");
        string owner="vessel-interior:"+command.Context.Command.Value;
        if(!await leases.TryLeasesAsync(command.Context.Load,owner,["input:foreground"]))throw new InvalidOperationException("另一项前景工作尚未交接。");
        try{Guard(await _repository.SnapshotAsync());return await _repository.ExecuteAsync(command);}finally{await leases.ReleaseLeasesAsync(command.Context.Load,owner);}
    }
    private async ValueTask<CommandResult?> Existing(BranchSnapshot s,string key)
    {
        var receipt=s.Receipts.SingleOrDefault(r=>r.SemanticKey==key);if(receipt is null)return null;
        var known=s.Commands.First(c=>c.ReceiptId==receipt.ReceiptId);
        return new(CommitStatus.AlreadyCommitted,await _repository.FindReceiptAsync(_repository.Session.Branch,new(Guid.Parse(known.CommandId))),[]);
    }
    public async ValueTask<CommandResult> InspectBerthAsync()
    {
        var s=await _repository.SnapshotAsync();Guard(s);var ship=Ship(s);if(ship.DockRegion!="R1")throw new InvalidOperationException("本候选只制作已经实际移交的R1泊位检查，其他地区没有检查生产者。");
        var handover=Spot("R1-S01.glimmer_handover");At(s,"R1-S01",handover.Stand);var binding=new VesselInteriorBinding(ship.Instance,Package.Version,_geometryHash,Handover(s).ReceiptId);var old=BranchOperations.Entity(s,BindingId(ship));
        if(old is not null&&StrictJson.Deserialize<VesselInteriorBinding>(old.Json)!=binding)throw new InvalidOperationException("已有几何登记与本版本不同，需明确的迁移，不能覆盖旧空间。");
        string key="vessel:berth-inspection:"+ship.Instance+":"+Package.Version;var existing=await Existing(s,key);if(existing is not null)return existing;
        ImmutableArray<EntityChange> changes=old is null?[BranchOperations.Change(s,BindingId(ship),"vessel_interior",binding)]:[];
        var gates=ImmutableArray.Create(new EntityStamp(BindingId(ship),old?.Revision??0));
        var berth=new VesselBerth(ship.Instance,"R1","R1-S01",handover.Stand,handover.Stand,gates,"local actual berth inspection");
        return await Execute(Command(s,key,"vessel_berth_verified",changes,[BindingId(ship)],berth));
    }
    public async ValueTask<CommandResult> PublishSpaceAsync(string scene)
    {
        var s=await _repository.SnapshotAsync();Guard(s);var ship=Ship(s);Binding(s,ship);var berth=Berth(s,ship);At(s,berth.Scene,berth.Boarding);
        var placement=Placement(scene);var binding=BranchOperations.Entity(s,BindingId(ship))!;var space=new VesselSpace(ship.Instance,scene,placement.Landing,placement.Transfer,[new(binding.Id,binding.Revision)],"verified current package collision and station geometry");
        return await Execute(Command(s,"vessel:space-inspection:"+ship.Instance+":"+Package.Version+":"+scene,"ship_space_verified",[],[binding.Id,"berth:"+ship.DockRegion],space));
    }
    public async ValueTask<CommandResult> MoveAsync(ImmutableArray<Point2> path)
    {
        var s=await _repository.SnapshotAsync();Guard(s);var ship=Ship(s);var actor=Actor(s);Space(s,ship,actor.Scene);if(path.IsDefaultOrEmpty)throw new InvalidOperationException("请提供实际船内路径。");var nav=new NavigationGrid(Geometry[actor.Scene]);var point=actor.Position;
        foreach(var next in path){if(!double.IsFinite(next.X)||!double.IsFinite(next.Y)||!nav.SegmentClear(point,next,true))throw new InvalidOperationException("船舱路径有碰撞、危险或无效坐标。");point=next;}
        var nextActor=actor with{Position=point,ActionClock=checked(actor.ActionClock+1)};
        return await Execute(Command(s,"vessel:walk:"+Guid.NewGuid(),"walk_shared_vessel",[BranchOperations.Change(s,"actor","actor",nextActor)],[BindingId(ship),"ship-space:"+actor.Scene],new{ship_instance=ship.Instance,scene=actor.Scene,path}));
    }
    private VesselInteriorPreview Ready(CommitPlanCommand command,string description){Guid token=Guid.NewGuid();_previews[token]=command;return new(token,true,[],description);}
    private static VesselInteriorPreview Unready(InvalidOperationException e)=>new(Guid.Empty,false,[e.Message],e.Message);
    public async ValueTask<VesselInteriorPreview> PreviewBoardAsync()
    {
        try
        {
            var s=await _repository.SnapshotAsync();Guard(s);var ship=Ship(s);var berth=Berth(s,ship);At(s,berth.Scene,berth.Boarding);var target=Space(s,ship,"HUB-S01");var actor=Actor(s);var next=actor with{Scene=target.Scene,Position=target.Landing,ActionClock=checked(actor.ActionClock+1)};
            return Ready(Command(s,"vessel:board:"+Guid.NewGuid(),"board_shared_glimmer",[BranchOperations.Change(s,"actor","actor",next)],new[]{BindingId(ship),"berth:"+ship.DockRegion,"ship-space:HUB-S01"}.Concat(berth.GateReads.Concat(target.GateReads).Select(r=>r.EntityId)),new VesselTransfer(ship.Instance,actor.Scene,next.Scene,"registered-current-berth","HUB-S01.gangway",actor.Position,next.Position,next.ActionClock,Package.Version)),"从当前已核验泊位登上同一微光号。");
        }
        catch(InvalidOperationException e){return Unready(e);}
    }
    public async ValueTask<VesselInteriorPreview> PreviewLeaveAsync()
    {
        try
        {
            var s=await _repository.SnapshotAsync();Guard(s);var ship=Ship(s);var berth=Berth(s,ship);var deck=Space(s,ship,"HUB-S01");At(s,deck.Scene,Spot("HUB-S01.gangway").Stand);var actor=Actor(s);var next=actor with{Scene=berth.Scene,Position=berth.Landing,ActionClock=checked(actor.ActionClock+1)};
            return Ready(Command(s,"vessel:leave:"+Guid.NewGuid(),"leave_shared_glimmer",[BranchOperations.Change(s,"actor","actor",next)],new[]{BindingId(ship),"berth:"+ship.DockRegion,"ship-space:HUB-S01"}.Concat(berth.GateReads.Concat(deck.GateReads).Select(r=>r.EntityId)),new VesselTransfer(ship.Instance,actor.Scene,next.Scene,"HUB-S01.gangway","registered-current-berth",actor.Position,next.Position,next.ActionClock,Package.Version)),"从甲板登离船口回到这艘船当前合法泊位。");
        }
        catch(InvalidOperationException e){return Unready(e);}
    }
    public async ValueTask<VesselInteriorPreview> PreviewExitAsync(string portId)
    {
        try
        {
            var s=await _repository.SnapshotAsync();Guard(s);var ship=Ship(s);var actor=Actor(s);Space(s,ship,actor.Scene);var port=Geometry[actor.Scene].Ports.SingleOrDefault(p=>p.Id==portId)??throw new InvalidOperationException("当前船舱没有这个配对出口。");At(s,actor.Scene,port.Stand);Space(s,ship,port.TargetScene);var reverse=Geometry[port.TargetScene].Ports.Single(p=>p.Id==port.TargetPort&&p.TargetScene==actor.Scene&&p.TargetPort==port.Id);var next=actor with{Scene=port.TargetScene,Position=reverse.Stand,ActionClock=checked(actor.ActionClock+1)};
            return Ready(Command(s,"vessel:exit:"+Guid.NewGuid(),"vessel_interior_transition",[BranchOperations.Change(s,"actor","actor",next)],[BindingId(ship),"ship-space:"+actor.Scene,"ship-space:"+next.Scene],new VesselTransfer(ship.Instance,actor.Scene,next.Scene,port.Id,reverse.Id,actor.Position,next.Position,next.ActionClock,Package.Version)),"沿实际配对出口进入同一船的相邻空间。");
        }
        catch(InvalidOperationException e){return Unready(e);}
    }
    public ValueTask<CommandResult> ConfirmPreviewAsync(Guid token)=>_previews.TryGetValue(token,out var command)?Execute(command):ValueTask.FromResult(new CommandResult(CommitStatus.Rejected,null,[new("preview_closed","vessel-interior","预览已关闭，请重新核对当前船与泊位。") ]));
    public void ClosePreview(Guid token)=>_previews.Remove(token);
    public async ValueTask<CommandResult> EnterPortAsync(string portId)
    {
        var preview=await PreviewExitAsync(portId);if(!preview.Ready)throw new InvalidOperationException(string.Join("；",preview.Issues));
        return await ConfirmPreviewAsync(preview.Token);
    }
    public async ValueTask<VesselWorkspaceView> WorkspaceAtAsync(string spotId)
    {
        var s=await _repository.SnapshotAsync();var ship=Ship(s);Binding(s,ship);var station=Package.Stations.SingleOrDefault(t=>t.Spot==spotId)??throw new InvalidOperationException("共享船没有这个受控工作区。");Space(s,ship,station.Scene);At(s,station.Scene,Spot(station.Spot).Stand);
        ImmutableArray<StoredEntity> entities=station.Kind switch
        {
            VesselStationKind.Archive or VesselStationKind.Correspondence=>s.Entities.Where(e=>e.Id.StartsWith("material:",StringComparison.Ordinal)).ToImmutableArray(),
            VesselStationKind.Chart=>s.Entities.Where(e=>e.Kind is "sea_route" or "vessel_berth" or "return_anchor").ToImmutableArray(),
            VesselStationKind.WorkOrders=>s.Entities.Where(e=>e.Kind is "work_order" or "tower_readiness" or "engineering_inventory" or "procurement" or "work_post" or "operator_duty").ToImmutableArray(),
            VesselStationKind.Roster=>s.Entities.Where(e=>e.Kind is "roster" or "formation" or "work_post" or "operator_duty").ToImmutableArray(),
            _=>[]
        };
        var items=station.Kind is VesselStationKind.Collection or VesselStationKind.Workbench?s.Items.Where(i=>i.OwnerKind is "player" or "equipped" or "module_installed").ToImmutableArray():[];
        var heads=station.Kind==VesselStationKind.DraftDesk?s.DraftHeads:[];
        var roster=station.Kind==VesselStationKind.Roster?await new RosterService(_repository,_catalog).ViewAsync():[];
        return new(station,ship,entities,items,s.Balances,heads,roster,Package.Remaining);
    }
}

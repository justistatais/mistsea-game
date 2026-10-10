using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.Contracts;

namespace Mist.Application.Travel;

public sealed partial class TravelService
{
    private SharedVessel Ship(BranchSnapshot s)
    {
        var ship=BranchOperations.Read<SharedVessel>(s,"ship:glimmer")??throw new InvalidOperationException("微光号实际使用权尚未移交。");
        if(ship.Instance==Guid.Empty||ship.WatchOfficer!="N-DECKWATCH"||!ship.Spaces.SequenceEqual(ShipSpaces)||!BranchOperations.Receipt(s,"Q-104"))throw new InvalidOperationException("巡灯船实例、实际移交或值守岗位不合法。");return ship;
    }
    private static string ExitGate(string a,string b)
    {
        if(a.Length!=2||b.Length!=2||a[0]!='R'||b[0]!='R'||a[1] is <'1' or >'6'||b[1] is <'1' or >'6'||Math.Abs(a[1]-b[1])!=1)throw new InvalidOperationException("该航路不是登记的相邻地区航路。");
        return "Q-"+Math.Min(a[1]-'0',b[1]-'0')+"04";
    }
    public async ValueTask<CommandResult> RegisterSeaRouteAsync(string receiptId)
    {
        var s=await repository.SnapshotAsync();var receipt=BranchOperations.Evidence(s,receiptId,"sea_route_verified");var route=StrictJson.Deserialize<SeaRoute>(receipt.ResultJson);
        if(!BranchOperations.Receipt(s,ExitGate(route.A,route.B))||!Current(s,route.GateReads))throw new InvalidOperationException("章节出口或当前航路尚未可靠核验。");string id="sea-route:"+ConnectionKey(route.A,route.B);
        return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,"sea-route-source:"+receiptId,"register_actual_sea_route",[BranchOperations.Change(s,id,"sea_route",route with{AuthorityReceipt=receiptId})],route.GateReads.Select(r=>r.EntityId).Append(id).ToArray(),new{source_receipt=receiptId}));
    }
    public async ValueTask<CommandResult> RegisterBerthAsync(string receiptId)
    {
        var s=await repository.SnapshotAsync();var receipt=BranchOperations.Evidence(s,receiptId,"vessel_berth_verified");var berth=StrictJson.Deserialize<VesselBerth>(receipt.ResultJson);var ship=Ship(s);
        if(berth.Instance!=ship.Instance||berth.Scene!=berth.Region+"-S01"||berth.Region is not("R1" or "R2" or "R3" or "R4" or "R5" or "R6")||!Current(s,berth.GateReads))throw new InvalidOperationException("泊位实例、地区或当前安全条件不符。");Stand(berth.Scene,berth.Landing);Stand(berth.Scene,berth.Boarding);string id="berth:"+berth.Region;
        return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,"berth-source:"+receiptId,"register_actual_vessel_berth",[BranchOperations.Change(s,id,"vessel_berth",berth with{AuthorityReceipt=receiptId})],berth.GateReads.Select(r=>r.EntityId).Concat(new[]{id,"ship:glimmer"}).ToArray(),new{source_receipt=receiptId}));
    }
    public async ValueTask<CommandResult> RegisterShipSpaceAsync(string receiptId)
    {
        var s=await repository.SnapshotAsync();var receipt=BranchOperations.Evidence(s,receiptId,"ship_space_verified");var space=StrictJson.Deserialize<VesselSpace>(receipt.ResultJson);var ship=Ship(s);
        if(space.Instance!=ship.Instance||!ShipSpaces.Contains(space.Scene)||!Current(s,space.GateReads))throw new InvalidOperationException("船内空间不属于当前巡灯船实例或尚未安全。");Stand(space.Scene,space.Landing);Stand(space.Scene,space.TransferStand);string id="ship-space:"+space.Scene;
        return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,"ship-space-source:"+receiptId,"register_actual_ship_space",[BranchOperations.Change(s,id,"vessel_space",space with{AuthorityReceipt=receiptId})],space.GateReads.Select(r=>r.EntityId).Concat(new[]{id,"ship:glimmer"}).ToArray(),new{source_receipt=receiptId}));
    }
    private VesselBerth Berth(BranchSnapshot s,SharedVessel ship,string region)
    {
        var berth=BranchOperations.Read<VesselBerth>(s,"berth:"+region)??throw new InvalidOperationException("该地区的实际安全泊位尚未接入。");
        var source=StrictJson.Deserialize<VesselBerth>(BranchOperations.Evidence(s,berth.AuthorityReceipt,"vessel_berth_verified").ResultJson);
        if(berth.Instance!=ship.Instance||berth.Region!=region||StrictJson.Serialize(source with{AuthorityReceipt=berth.AuthorityReceipt})!=StrictJson.Serialize(berth)||!Current(s,berth.GateReads))throw new InvalidOperationException("泊位来源、实例或当前安全条件已变更。");return berth;
    }
    private VesselSpace Space(BranchSnapshot s,SharedVessel ship,string scene)
    {
        var space=BranchOperations.Read<VesselSpace>(s,"ship-space:"+scene)??throw new InvalidOperationException("该船内空间的实际站位尚未接入。");
        var source=StrictJson.Deserialize<VesselSpace>(BranchOperations.Evidence(s,space.AuthorityReceipt,"ship_space_verified").ResultJson);
        if(space.Instance!=ship.Instance||!ShipSpaces.Contains(space.Scene)||StrictJson.Serialize(source with{AuthorityReceipt=space.AuthorityReceipt})!=StrictJson.Serialize(space)||!Current(s,space.GateReads))throw new InvalidOperationException("船内空间来源、实例或当前安全条件已变更。");return space;
    }
    private bool SeaReachable(BranchSnapshot s,string from,string to)
    {
        var routes=s.Entities.Where(e=>e.Kind=="sea_route").Select(e=>StrictJson.Deserialize<SeaRoute>(e.Json)).ToArray();var seen=new HashSet<string>{from};var queue=new Queue<string>();queue.Enqueue(from);
        while(queue.TryDequeue(out string? node))
        {
            if(node==to)return true;
            foreach(var route in routes.Where(r=>r.A==node||r.B==node))
            {
                var source=StrictJson.Deserialize<SeaRoute>(BranchOperations.Evidence(s,route.AuthorityReceipt,"sea_route_verified").ResultJson);
                if(source.A!=route.A||source.B!=route.B||!source.GateReads.SequenceEqual(route.GateReads)||!BranchOperations.Receipt(s,ExitGate(route.A,route.B))||!Current(s,route.GateReads,true))continue;
                string other=route.A==node?route.B:route.A;if(seen.Add(other))queue.Enqueue(other);
            }
        }
        return false;
    }
    public async ValueTask<CommandResult> SailAsync(string destinationRegion)
    {
        var s=await repository.SnapshotAsync();Safe(s);var ship=Ship(s);var actor=Actor(s);FinalStage(s,actor.Scene,destinationRegion+"-S01");
        if(destinationRegion==ship.DockRegion||!SeaReachable(s,ship.DockRegion,destinationRegion))throw new InvalidOperationException("当前没有可合并到目的地区的已开放安全航路。");
        var old=Berth(s,ship,ship.DockRegion);var target=Berth(s,ship,destinationRegion);
        var spaceReads=ImmutableArray<EntityStamp>.Empty;
        if(actor.Scene==old.Scene)At(actor,old.Scene,old.Boarding);else if(actor.Scene=="HUB-S02"){var chart=Space(s,ship,actor.Scene);At(actor,actor.Scene,chart.TransferStand);spaceReads=chart.GateReads;}else throw new InvalidOperationException("请先走到当前安全泊位或船舱航图站位办理航行。");
        var reads=s.Entities.Where(e=>e.Kind is "sea_route" or "vessel_berth" or "vessel_space").Select(e=>e.Id).Concat(s.Entities.Where(e=>e.Kind=="sea_route").SelectMany(e=>StrictJson.Deserialize<SeaRoute>(e.Json).GateReads).Select(r=>r.EntityId)).Concat(old.GateReads.Concat(target.GateReads).Concat(spaceReads).Select(r=>r.EntityId)).Concat(new[]{"actor","ship:glimmer"}).Concat(TravelReads(s)).ToArray();
        return await RelocateAsync(BranchOperations.Plan(repository,s,"sail:"+Guid.NewGuid(),"sail_shared_glimmer",[BranchOperations.Change(s,"ship:glimmer","shared_vessel",ship with{DockRegion=destinationRegion}),BranchOperations.Change(s,"actor","actor",actor with{Scene=target.Scene,Position=target.Landing,ActionClock=checked(actor.ActionClock+1)})],reads,new{ship_instance=ship.Instance,from=ship.DockRegion,to=destinationRegion}));
    }
    public async ValueTask<CommandResult> EnterShipSpaceAsync(string space)
    {
        var s=await repository.SnapshotAsync();Safe(s);var ship=Ship(s);var actor=Actor(s);FinalStage(s,actor.Scene,space);var target=Space(s,ship,space);var reads=new List<string>{"actor","ship:glimmer","ship-space:"+space};reads.AddRange(target.GateReads.Select(r=>r.EntityId));
        if(actor.Scene==ship.DockRegion+"-S01"&&space=="HUB-S01"){var berth=Berth(s,ship,ship.DockRegion);At(actor,berth.Scene,berth.Boarding);reads.Add("berth:"+ship.DockRegion);reads.AddRange(berth.GateReads.Select(r=>r.EntityId));}
        else if(ShipSpaces.Contains(actor.Scene)&&catalog.Connections.Any(c=>c.A==actor.Scene&&c.B==space||c.B==actor.Scene&&c.A==space)){var source=Space(s,ship,actor.Scene);At(actor,actor.Scene,source.TransferStand);reads.Add("ship-space:"+actor.Scene);reads.AddRange(source.GateReads.Select(r=>r.EntityId));}
        else throw new InvalidOperationException("请先实际抵达当前泊位或相邻船内空间。");
        reads.AddRange(TravelReads(s));return await RelocateAsync(BranchOperations.Plan(repository,s,"ship-space:"+Guid.NewGuid(),"enter_shared_ship_space",[BranchOperations.Change(s,"actor","actor",actor with{Scene=space,Position=target.Landing,ActionClock=checked(actor.ActionClock+1)})],reads.ToArray(),new{ship_instance=ship.Instance,space}));
    }
    public async ValueTask<CommandResult> LeaveShipAsync()
    {
        var s=await repository.SnapshotAsync();Safe(s);var ship=Ship(s);var actor=Actor(s);var berth=Berth(s,ship,ship.DockRegion);FinalStage(s,actor.Scene,berth.Scene);var deck=Space(s,ship,"HUB-S01");At(actor,deck.Scene,deck.TransferStand);
        return await RelocateAsync(BranchOperations.Plan(repository,s,"leave-ship:"+Guid.NewGuid(),"leave_shared_ship",[BranchOperations.Change(s,"actor","actor",actor with{Scene=berth.Scene,Position=berth.Landing,ActionClock=checked(actor.ActionClock+1)})],new[]{"actor","ship:glimmer","berth:"+ship.DockRegion,"ship-space:HUB-S01"}.Concat(berth.GateReads.Concat(deck.GateReads).Select(r=>r.EntityId)).Concat(TravelReads(s)).ToArray(),new{ship_instance=ship.Instance,dock=ship.DockRegion}));
    }
}

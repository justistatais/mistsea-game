using System.Collections.Immutable;
using System.Text.Json;
using Mist.Application.CombatSessions;
using Mist.Application.Contracts;
using Mist.Application.EventScheduler;
using Mist.Application.Q102;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Application.WorkOrders;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Engineering;
using Mist.Core.Exploration;
using Mist.Infrastructure.Storage;
using static Mist.Tests.NPC.NpcCases;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.Travel;

public static partial class TravelCases
{
    private static readonly Mist.Core.Content.RuntimeCatalog Catalog=Q102.Q102Cases.Catalog();
    private static ProfileStore Store(string name)=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"travel-"+name+"-"+Guid.NewGuid()),Catalog.ReleaseId);
    private static void Success(CommandResult r)=>Require(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(r));
    private static T Read<T>(ProfileStore s,string id)=>StrictJson.Deserialize<T>(Done(s.SnapshotAsync()).Entities.Single(e=>e.Id==id).Json);
    private static void Position(ProfileStore s,string scene,Point2? point=null)=>Fixture(s,"fixture:position:"+Guid.NewGuid(),"explicit_engineering_fixture",new{scope="travel position fixture"},[Entity(s,"actor","actor",new ActorState(scene,point??new(48,90),Read<ActorState>(s,"actor").ActionClock+1))]);
    private static ImmutableArray<EntityStamp> Reads(ProfileStore s,params string[] ids)
    {
        var snap=Done(s.SnapshotAsync());return ids.Select(id=>new EntityStamp(id,snap.Entities.Single(e=>e.Id==id).Revision)).ToImmutableArray();
    }
    private static string Evidence(ProfileStore s,string action,object payload)=>Fixture(s,"fixture:"+action+":"+Guid.NewGuid(),action,payload).Receipt!.Id.Value.ToString();
    private static void Anchor(ProfileStore s,TravelService t,string scene,ReturnAnchorKind kind=ReturnAnchorKind.Settlement,Point2? point=null)
    {
        var p=point??new Point2(48,90);Position(s,scene,p);Success(Done(t.RegisterAnchorAsync(Evidence(s,"return_anchor_verified",new ReturnAnchor(scene,p,kind,"fixture safety producer")))));
    }
    private static string Transition(ProfileStore s,TravelService t,string from,string to,bool reverse=true)
    {
        var q=new Q102Service(s,Catalog);var c=Catalog.Connections.Single(c=>c.A==from&&c.B==to||c.B==from&&c.A==to);
        var actualFrom=t.Geometry[from];var actualTo=t.Geometry[to];var port=actualFrom.Ports.Single(p=>p.TargetScene==to);var back=actualTo.Ports.Single(p=>p.Id==port.TargetPort);
        string fromPort=port.Id,toPort=back.Id;Point2 source=port.Stand,target=back.Stand;
        ImmutableArray<EntityStamp> reads;
        if(q.Geometry.TryGetValue(from,out var a)&&q.Geometry.TryGetValue(to,out var b))
        {
            var ids=new List<string>{"permission:Q-102"};if(from=="R1-S04"||to=="R1-S04")ids.Add("ferry");if(from=="R1-S06"||to=="R1-S06")ids.Add("workshop");reads=Reads(s,ids.ToArray());
        }
        else reads=Reads(s,"travel-gate:"+TravelService.ConnectionKey(from,to),"readiness:TWR-R2");
        Position(s,from,source);var actor=Read<ActorState>(s,"actor");var proof=new TraversedConnection(from,to,fromPort,toPort,source,target,actor.ActionClock+1,TravelService.Contract(c),reads,reverse);
        var receipt=Fixture(s,"fixture:physical-transition:"+Guid.NewGuid(),"scene_transition",proof,[Entity(s,"actor","actor",actor with{Scene=to,Position=target,ActionClock=proof.ActionClock})]);string id=receipt.Receipt!.Id.Value.ToString();Success(Done(t.RecordWalkedRouteAsync(id)));return id;
    }
    // Explicit test geometry for storage/sailing fixtures. These rectangles are not later-region production scenes.
    private static ImmutableDictionary<string,SceneGeometry> FixtureGeometry()
    {
        var map=new Dictionary<string,SceneGeometry>();
        foreach(string scene in new[]{"R1-S01","R2-S01","R2-S02","R2-S08","R3-S01","HUB-S01","HUB-S02","HUB-S03"})
            map[scene]=new(scene,new(5,20,95,94),[],[],[],[]);
        map["R2-S02"]=map["R2-S02"] with{Ports=[new("R2-S02.to.R2-S08","R2-S08","R2-S08.to.R2-S02",new(80,85))]};
        map["R2-S08"]=map["R2-S08"] with{Ports=[new("R2-S08.to.R2-S02","R2-S02","R2-S02.to.R2-S08",new(12,85))]};
        return map.ToImmutableDictionary();
    }
    private static SharedVessel Ship(ProfileStore s,TravelService t,params string[] regions)
    {
        var ship=new SharedVessel(Guid.NewGuid(),regions[0],"N-DECKWATCH",["HUB-S01","HUB-S02","HUB-S03"]);Fixture(s,"Q-104:resolution","explicit_engineering_fixture",new{scope="actual ship handoff fixture"},[Entity(s,"ship:glimmer","shared_vessel",ship)]);
        foreach(var region in regions)Success(Done(t.RegisterBerthAsync(Evidence(s,"vessel_berth_verified",new VesselBerth(ship.Instance,region,region+"-S01",new(48,90),new(48,90),[],"fixture berth producer")))));
        foreach(string space in ship.Spaces)Success(Done(t.RegisterShipSpaceAsync(Evidence(s,"ship_space_verified",new VesselSpace(ship.Instance,space,new(48,90),new(48,90),[],"fixture ship geometry producer")))));
        for(int i=1;i<regions.Length;i++)
        {
            string quest="Q-"+i+"04:resolution";if(!Done(s.SnapshotAsync()).Receipts.Any(r=>r.SemanticKey==quest))Fixture(s,quest,"explicit_engineering_fixture",new{scope="chapter exit fixture"});
            Success(Done(t.RegisterSeaRouteAsync(Evidence(s,"sea_route_verified",new SeaRoute(regions[i-1],regions[i],[],"fixture real opened route")))));
        }
        Position(s,regions[0]+"-S01");return ship;
    }
    public static void Register()
    {
        Add("CX-060","Actual Q102 navigation emits a paired movement receipt that grants one return edge",()=>
        {
            using var s=Store("movement-producer");var run=new Mist.DebugCandidates.Q102RouteCandidate(s,Catalog);Done(run.Service.InitializeSliceAsync());var t=new TravelService(s,Catalog);Anchor(s,t,"R1-S02");Done(run.Exit("R1-S03"));
            var receipt=Done(s.SnapshotAsync()).Receipts.Where(r=>r.ActionType=="scene_transition").OrderBy(r=>r.Sequence).Last();Success(Done(t.RecordWalkedRouteAsync(receipt.ReceiptId)));Success(Done(t.ReturnAsync("R1-S02")));Require(Read<ActorState>(s,"actor").Scene=="R1-S02");
        });
        Add("CX-060","Map discovery cannot register an unvisited anchor; only two declared safe kinds qualify",()=>
        {
            using var s=Store("anchors");Done(new Q102Service(s,Catalog).InitializeSliceAsync());var t=new TravelService(s,Catalog);
            string source=Evidence(s,"return_anchor_verified",new ReturnAnchor("R2-S02",new(48,90),ReturnAnchorKind.Settlement,"fixture"));long seq=Done(s.SnapshotAsync()).Sequence;Throws<InvalidOperationException>(()=>Done(t.RegisterAnchorAsync(source)));Require(Done(s.SnapshotAsync()).Sequence==seq&&Done(t.AnchorsAsync()).Length==0);
            Anchor(s,t,"R1-S02");Position(s,"R1-S03");Throws<InvalidOperationException>(()=>Done(t.RegisterAnchorAsync(Evidence(s,"return_anchor_verified",new ReturnAnchor("R1-S03",new(48,90),ReturnAnchorKind.Settlement,"fixture")))));
            Position(s,"R2-S08");Throws<InvalidOperationException>(()=>Done(t.RegisterAnchorAsync(Evidence(s,"return_anchor_verified",new ReturnAnchor("R2-S08",new(48,90),ReturnAnchorKind.TowerSafeExit,"fixture")))));
        });
        Add("CX-060","Physical paired transition receipt permits return; generic legacy or forged path does not",()=>
        {
            using var s=Store("actual-path");Done(new Q102Service(s,Catalog).InitializeSliceAsync());var t=new TravelService(s,Catalog);Anchor(s,t,"R1-S02");Position(s,"R1-S03");Throws<InvalidOperationException>(()=>Done(t.ReturnAsync("R1-S02")));
            var old=Evidence(s,"scene_transition",new{});Throws<InvalidOperationException>(()=>Done(t.RecordWalkedRouteAsync(old)));
            Transition(s,t,"R1-S02","R1-S03");Success(Done(t.ReturnAsync("R1-S02")));Require(Read<ActorState>(s,"actor").Scene=="R1-S02");
            Fixture(s,"fixture:forged-path","explicit_engineering_fixture",new{},[Entity(s,"walked:fake","walked_route",new WalkedRoute("R1-S07","R1-S02","missing-source"))]);Position(s,"R1-S07");Throws<InvalidOperationException>(()=>Done(t.ReturnAsync("R1-S02")));
        });
        Add("CX-060","A one-way walked edge cannot be silently reversed; departed ferry invalidates the old route",()=>
        {
            using var s=Store("directions");Done(new Q102Service(s,Catalog).InitializeSliceAsync());var t=new TravelService(s,Catalog);Anchor(s,t,"R1-S02");Transition(s,t,"R1-S02","R1-S03",false);Throws<InvalidOperationException>(()=>Done(t.ReturnAsync("R1-S02")));
            Transition(s,t,"R1-S02","R1-S04");Fixture(s,"fixture:ferry-departed","explicit_engineering_fixture",new{},[Entity(s,"ferry","device",Read<FerryState>(s,"ferry") with{Docked=false})]);long seq=Done(s.SnapshotAsync()).Sequence;Throws<InvalidOperationException>(()=>Done(t.ReturnAsync("R1-S02")));Require(Done(s.SnapshotAsync()).Sequence==seq);
        });
        Add("CX-060","Reactivated workshop interlock cannot reuse its previously safe traversal",()=>
        {
            using var s=Store("interlock");Done(new Q102Service(s,Catalog).InitializeSliceAsync());var t=new TravelService(s,Catalog);Anchor(s,t,"R1-S02");
            Fixture(s,"fixture:isolation","explicit_engineering_fixture",new{},[Entity(s,"workshop","device",new WorkshopState(WorkshopMode.Isolated,false))]);Transition(s,t,"R1-S02","R1-S05");Transition(s,t,"R1-S05","R1-S07");Transition(s,t,"R1-S07","R1-S06");
            Fixture(s,"fixture:reactivated","explicit_engineering_fixture",new{},[Entity(s,"workshop","device",new WorkshopState(WorkshopMode.Active,false))]);Throws<InvalidOperationException>(()=>Done(t.ReturnAsync("R1-S02")));Require(Read<ActorState>(s,"actor").Scene=="R1-S06");
        });
        Add("CX-060","Tower-safe return survives continue and enhancement without granting full preparation",()=>
        {
            using var s=Store("tower");Done(new Q102Service(s,Catalog).InitializeSliceAsync());var t=new TravelService(s,Catalog,FixtureGeometry());var work=new WorkOrderService(s,Catalog);Success(Done(work.RegisterSafetyAsync(Evidence(s,"tower_safety_verified",new TowerSafety("TWR-R2",true,"actual fixture tower intervention")))));
            Anchor(s,t,"R2-S08",ReturnAnchorKind.TowerSafeExit);var c=Catalog.Connections.Single(c=>c.A=="R2-S02"&&c.B=="R2-S08"||c.B=="R2-S02"&&c.A=="R2-S08");Success(Done(t.RegisterConnectionSafetyAsync(Evidence(s,"connection_safety_verified",new ConnectionSafetyState(c.A,c.B,TravelService.Contract(c),true,true,Reads(s,"readiness:TWR-R2"),"fixture actual clearance")))));
            Transition(s,t,"R2-S08","R2-S02");var before=Read<TowerReadiness>(s,"readiness:TWR-R2");Fixture(s,"fixture:enhanced-only","explicit_engineering_fixture",new{},[Entity(s,"readiness:TWR-R2","tower_readiness",before with{EnhancedVersion="fixture-upgrade-v2"})]);Done(s.ContinueAsync());Success(Done(t.ReturnAsync("R2-S08")));var after=Read<TowerReadiness>(s,"readiness:TWR-R2");Require(after.Safe&&!after.Baseline&&!after.Trial&&!after.Operators&&after.EnhancedVersion=="fixture-upgrade-v2");
        });
        Add("CX-060","Cargo in transit, active combat and a foreground event prevent relocation",()=>
        {
            using var s=Store("commitments");var q=new Q102Service(s,Catalog);Done(q.InitializeSliceAsync());var t=new TravelService(s,Catalog);Anchor(s,t,"R1-S02");Transition(s,t,"R1-S02","R1-S03");
            Fixture(s,"fixture:cargo","explicit_engineering_fixture",new{},[Entity(s,"transport:promise","transport_segment",new TransportSegment("promise","R6-S05","R6-S07",true,false))],items:[new("actual-cargo","CTRL","new","","vessel","transport-fixture",null)]);long seq=Done(s.SnapshotAsync()).Sequence;Throws<InvalidOperationException>(()=>Done(t.ReturnAsync("R1-S02")));Require(Done(s.SnapshotAsync()).Sequence==seq&&Done(s.SnapshotAsync()).Items.Single(i=>i.Id=="actual-cargo").OwnerId=="transport-fixture");
            Fixture(s,"fixture:handover","explicit_engineering_fixture",new{},[Entity(s,"transport:promise","transport_segment",new TransportSegment("promise","R6-S05","R6-S07",false,true))]);var scheduler=new EventScheduler(s,s);var ev=Done(scheduler.QueueAsync(new("travel-conversation","v1","once",1,["N-HAVEL"],"desk",17)));Done(scheduler.StartNextAsync(ScheduleBoundary.Safe));Throws<InvalidOperationException>(()=>Done(t.ReturnAsync("R1-S02")));Success(Done(scheduler.CheckpointAsync(ev.Instance,"safe",pause:true)));
            Done(new Mist.Application.Inventory.PartyService(s,Catalog).InitializeStarterCollectionAsync());Position(s,"R1-S05",q.Geometry["R1-S05"].Ports.Single(p=>p.TargetScene=="R1-S06").Stand);Success(Done(new EncounterService(s,Catalog).BeginWorkshopAsync()));Throws<InvalidOperationException>(()=>Done(t.ReturnAsync("R1-S02")));
        });
        Add("CX-060","An atomic foreground lease blocks relocation without a running event and remains owned",()=>
        {
            using var s=Store("foreground-lease");Done(new Q102Service(s,Catalog).InitializeSliceAsync());var t=new TravelService(s,Catalog);Anchor(s,t,"R1-S02");Transition(s,t,"R1-S02","R1-S03");Require(Done(s.TryLeasesAsync(s.Session.Load,"writing-workspace",["input:foreground"])));long seq=Done(s.SnapshotAsync()).Sequence;
            Throws<InvalidOperationException>(()=>Done(t.ReturnAsync("R1-S02")));Require(Done(s.SnapshotAsync()).Sequence==seq&&!Done(s.TryLeasesAsync(s.Session.Load,"other",["input:foreground"])));Done(s.ReleaseLeasesAsync(s.Session.Load,"writing-workspace"));Success(Done(t.ReturnAsync("R1-S02")));
        });
        Add("CX-060","One Glimmer instance serves three spaces; declined Lina and zero fuel never lock legal sailing",()=>
        {
            using var s=Store("shared-ship");Done(new Q102Service(s,Catalog).InitializeSliceAsync());var t=new TravelService(s,Catalog,FixtureGeometry());var original=Ship(s,t,"R1","R2","R3");var roster=new Mist.Application.Roster.RosterService(s,Catalog);Success(Done(roster.InviteAsync("CP-LINA")));Success(Done(roster.DecideAsync("CP-LINA",false)));
            var balances=StrictJson.Serialize(Done(s.SnapshotAsync()).Balances);Success(Done(t.EnterShipSpaceAsync("HUB-S01")));Throws<InvalidOperationException>(()=>Done(t.EnterShipSpaceAsync("HUB-S03")));Success(Done(t.EnterShipSpaceAsync("HUB-S02")));Success(Done(t.EnterShipSpaceAsync("HUB-S03")));Success(Done(t.EnterShipSpaceAsync("HUB-S02")));Success(Done(t.SailAsync("R3")));Require(Read<ActorState>(s,"actor").Scene=="R3-S01");var ship=Done(t.VesselAsync())!;Require(ship.Instance==original.Instance&&ship.DockRegion=="R3"&&ship.Spaces.Length==3&&Done(s.SnapshotAsync()).Entities.Count(e=>e.Kind=="shared_vessel")==1&&StrictJson.Serialize(Done(s.SnapshotAsync()).Balances)==balances);
            Done(s.ContinueAsync());Success(Done(t.SailAsync("R1")));Require(Done(t.VesselAsync())!.Instance==original.Instance);
        });
        Add("CX-060","Ship requires actual safe placement and boarding proximity; forged or missing instances fail",()=>
        {
            using var s=Store("boarding");Done(new Q102Service(s,Catalog).InitializeSliceAsync());var t=new TravelService(s,Catalog,FixtureGeometry());Throws<InvalidOperationException>(()=>Done(t.EnterShipSpaceAsync("HUB-S01")));Ship(s,t,"R1","R2");Position(s,"R1-S01",new(10,10));long seq=Done(s.SnapshotAsync()).Sequence;Throws<InvalidOperationException>(()=>Done(t.EnterShipSpaceAsync("HUB-S01")));Throws<InvalidOperationException>(()=>Done(t.SailAsync("R2")));Require(Done(s.SnapshotAsync()).Sequence==seq);
            Position(s,"R1-S01");Success(Done(t.EnterShipSpaceAsync("HUB-S01")));Success(Done(t.LeaveShipAsync()));Require(Read<ActorState>(s,"actor").Scene=="R1-S01");var bad=new VesselSpace(Guid.NewGuid(),"HUB-S01",new(48,90),new(48,90),[],"wrong-instance fixture");Throws<InvalidOperationException>(()=>Done(t.RegisterShipSpaceAsync(Evidence(s,"ship_space_verified",bad))));Throws<InvalidOperationException>(()=>Done(t.SailAsync("R6")));
        });
        Add("CX-060","Q604 freezes outside travel and ship entry while preserving the named prefinal checkpoint",()=>
        {
            using var s=Store("final-boundary");Done(new Q102Service(s,Catalog).InitializeSliceAsync());var t=new TravelService(s,Catalog,FixtureGeometry());Ship(s,t,"R1","R2");string saved=Done(s.CreateCheckpointAsync("protected",protectedId:"before-final-departure"));Fixture(s,"Q-604:resolution","explicit_engineering_fixture",new{scope="final departure fixture"});long seq=Done(s.SnapshotAsync()).Sequence;Throws<InvalidOperationException>(()=>Done(t.SailAsync("R2")));Throws<InvalidOperationException>(()=>Done(t.EnterShipSpaceAsync("HUB-S01")));Require(Done(s.SnapshotAsync()).Sequence==seq&&Done(s.SlotsAsync())["protected:before-final-departure"]==saved);
            Done(s.LoadCheckpointAsync(saved));Success(Done(t.SailAsync("R2")));Require(Read<ActorState>(s,"actor").Scene=="R2-S01");
        });
        RegisterGeometry();
    }
}

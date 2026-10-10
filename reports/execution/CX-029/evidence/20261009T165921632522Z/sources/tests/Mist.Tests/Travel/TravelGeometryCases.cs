using System.Collections.Immutable;
using Mist.Application.Contracts;
using Mist.Application.Q102;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Infrastructure.Storage;
using static Mist.Tests.NPC.NpcCases;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.Travel;

public static partial class TravelCases
{
    private static SceneGeometry Box(string id)=>new(id,new(5,18,95,94),[],[],[],[]);
    private static ImmutableDictionary<string,SceneGeometry> ThreeRooms()
    {
        var a=Box("R1-S02") with{Ports=[new("R1-S02.to.R1-S05","R1-S05","R1-S05.to.R1-S02",new(12,50))]};
        var b=Box("R1-S05") with{Ports=[new("R1-S05.to.R1-S02","R1-S02","R1-S02.to.R1-S05",new(12,50)),new("R1-S05.to.R1-S07","R1-S07","R1-S07.to.R1-S05",new(88,50))]};
        var c=Box("R1-S07") with{Ports=[new("R1-S07.to.R1-S05","R1-S05","R1-S05.to.R1-S07",new(12,50))]};
        return new[]{a,b,c}.ToImmutableDictionary(g=>g.Id);
    }
    private static TravelService WalkThreeRooms(ProfileStore store,ImmutableDictionary<string,SceneGeometry> geometry)
    {
        Done(new Q102Service(store,Catalog).InitializeSliceAsync());var travel=new TravelService(store,Catalog,geometry);
        Anchor(store,travel,"R1-S02",point:new(20,80));Transition(store,travel,"R1-S02","R1-S05");Transition(store,travel,"R1-S05","R1-S07");return travel;
    }
    private static void UnchangedReturn(ProfileStore store,TravelService travel,string destination)
    {
        var before=Done(store.SnapshotAsync());var actor=Read<ActorState>(store,"actor");Throws<InvalidOperationException>(()=>Done(travel.ReturnAsync(destination)));
        Require(Done(store.SnapshotAsync()).Sequence==before.Sequence&&Read<ActorState>(store,"actor")==actor);
    }
    private static void RegisterGeometry()
    {
        Add("CX-060","An intermediate room wall cannot be skipped merely because both scene edges were walked",()=>
        {
            using var s=Store("internal-wall");var map=ThreeRooms();WalkThreeRooms(s,map);
            var wall=map["R1-S05"] with{Blocked=[new(45,18,55,94)]};var closed=map.SetItem(wall.Id,wall);
            Require(new NavigationGrid(wall).CanStand(new(12,50),true)&&new NavigationGrid(wall).CanStand(new(88,50),true));
            UnchangedReturn(s,new(s,Catalog,closed),"R1-S02");
        });
        Add("CX-060","A newly dangerous strip inside an intermediate room invalidates an otherwise current walked route",()=>
        {
            using var s=Store("internal-danger");var map=ThreeRooms();WalkThreeRooms(s,map);
            var danger=map["R1-S05"] with{Danger=[new(45,18,55,94)]};var changed=map.SetItem(danger.Id,danger);
            Require(new NavigationGrid(danger).FindPath(new(88,50),new(12,50),false).Length>0);
            UnchangedReturn(s,new(s,Catalog,changed),"R1-S02");
        });
        Add("CX-060","A safe AStar detour inside the intermediate room still permits the folded physical route",()=>
        {
            using var s=Store("internal-detour");var map=ThreeRooms();WalkThreeRooms(s,map);
            var room=map["R1-S05"] with{Blocked=[new(45,28,55,80)]};var nav=new NavigationGrid(room);Require(!nav.SegmentClear(new(88,50),new(12,50),true)&&nav.FindPath(new(88,50),new(12,50),true).Length>1);
            Success(Done(new TravelService(s,Catalog,map.SetItem(room.Id,room)).ReturnAsync("R1-S02")));Require(Read<ActorState>(s,"actor").Scene=="R1-S02"&&Read<ActorState>(s,"actor").Position==new Point2(20,80));
        });
        Add("CX-060","Return validates both the actor-to-first-exit leg and last-entry-to-anchor leg",()=>
        {
            foreach(bool start in new[]{true,false})
            {
                using var s=Store(start?"blocked-start-leg":"blocked-final-leg");var map=ThreeRooms();var travel=WalkThreeRooms(s,map);
                if(start)Position(s,"R1-S07",new(80,80));else Anchor(s,travel,"R1-S02",point:new(80,80));
                if(!start)Position(s,"R1-S07",new(12,50));string roomId=start?"R1-S07":"R1-S02";
                var room=map[roomId] with{Blocked=[new(45,18,55,94)]};UnchangedReturn(s,new(s,Catalog,map.SetItem(roomId,room)),"R1-S02");
            }
        });
        Add("CX-060","Same-scene return cannot cross a wall or newly hazardous corridor",()=>
        {
            foreach(bool hazard in new[]{false,true})
            {
                using var s=Store(hazard?"same-scene-danger":"same-scene-wall");Done(new Q102Service(s,Catalog).InitializeSliceAsync());var room=Box("R1-S02");var open=ImmutableDictionary<string,SceneGeometry>.Empty.Add(room.Id,room);var travel=new TravelService(s,Catalog,open);
                Anchor(s,travel,"R1-S02",point:new(80,80));Position(s,"R1-S02",new(20,80));
                var blocked=hazard?room with{Danger=[new(45,18,55,94)]}:room with{Blocked=[new(45,18,55,94)]};UnchangedReturn(s,new(s,Catalog,open.SetItem(room.Id,blocked)),"R1-S02");
            }
        });
        Add("CX-060","Same-scene return with a legal obstacle detour uses real navigation and preserves balances",()=>
        {
            using var s=Store("same-scene-detour");Done(new Q102Service(s,Catalog).InitializeSliceAsync());var room=Box("R1-S02") with{Blocked=[new(45,28,55,80)]};var map=ImmutableDictionary<string,SceneGeometry>.Empty.Add(room.Id,room);var travel=new TravelService(s,Catalog,map);
            Anchor(s,travel,"R1-S02",point:new(80,50));Position(s,"R1-S02",new(20,50));var before=Done(s.SnapshotAsync());Require(!new NavigationGrid(room).SegmentClear(new(20,50),new(80,50),true));Success(Done(travel.ReturnAsync("R1-S02")));
            Require(Read<ActorState>(s,"actor").Position==new Point2(80,50)&&StrictJson.Serialize(Done(s.SnapshotAsync()).Balances)==StrictJson.Serialize(before.Balances));
        });
        Add("CX-060","The search may re-enter a different component of one scene through an already walked safe loop",()=>
        {
            using var s=Store("different-entry-loop");Done(new Q102Service(s,Catalog).InitializeSliceAsync());Fixture(s,"fixture:loop-workshop-safe","explicit_engineering_fixture",new{},[Entity(s,"workshop","device",new WorkshopState(WorkshopMode.Isolated,true))]);
            var a=Box("R1-S02") with{Ports=[new("R1-S02.to.R1-S05","R1-S05","R1-S05.to.R1-S02",new(12,50)),new("R1-S02.to.R1-S06","R1-S06","R1-S06.to.R1-S02",new(88,50))]};
            var b=Box("R1-S05") with{Ports=[new("R1-S05.to.R1-S02","R1-S02","R1-S02.to.R1-S05",new(12,50)),new("R1-S05.to.R1-S06","R1-S06","R1-S06.to.R1-S05",new(88,50))]};
            var c=Box("R1-S06") with{Ports=[new("R1-S06.to.R1-S05","R1-S05","R1-S05.to.R1-S06",new(12,50)),new("R1-S06.to.R1-S02","R1-S02","R1-S02.to.R1-S06",new(88,50))]};
            var map=new[]{a,b,c}.ToImmutableDictionary(g=>g.Id);var travel=new TravelService(s,Catalog,map);Anchor(s,travel,"R1-S02",point:new(80,80));Transition(s,travel,"R1-S02","R1-S05");Transition(s,travel,"R1-S05","R1-S06");Transition(s,travel,"R1-S06","R1-S02");Position(s,"R1-S02",new(20,80));
            var divided=a with{Blocked=[new(45,18,55,94)]};Require(new NavigationGrid(divided).FindPath(new(20,80),new(80,80),true).Length==0);Success(Done(new TravelService(s,Catalog,map.SetItem(a.Id,divided)).ReturnAsync("R1-S02")));Require(Read<ActorState>(s,"actor").Position==new Point2(80,80));
        });
        Add("CX-060","Later-region scene IDs and an authority receipt cannot substitute for missing actual geometry",()=>
        {
            using var s=Store("missing-geometry");Done(new Q102Service(s,Catalog).InitializeSliceAsync());var travel=new TravelService(s,Catalog);Position(s,"R2-S02",new(48,90));string source=Evidence(s,"return_anchor_verified",new ReturnAnchor("R2-S02",new(48,90),ReturnAnchorKind.Settlement,"fixture current safety"));long sequence=Done(s.SnapshotAsync()).Sequence;
            Throws<InvalidOperationException>(()=>Done(travel.RegisterAnchorAsync(source)));Require(Done(s.SnapshotAsync()).Sequence==sequence);
            Fixture(s,"fixture:retained-later-anchor","explicit_engineering_fixture",new{},[Entity(s,"anchor:R2-S02","return_anchor",new ReturnAnchor("R2-S02",new(48,90),ReturnAnchorKind.Settlement,source))]);UnchangedReturn(s,travel,"R2-S02");
        });
        Add("CX-060","A copied valid transition proof cannot be relabelled as a different scene edge",()=>
        {
            using var s=Store("relabelled-valid-proof");Done(new Q102Service(s,Catalog).InitializeSliceAsync());var travel=new TravelService(s,Catalog);Anchor(s,travel,"R1-S02");string source=Transition(s,travel,"R1-S02","R1-S03");var snapshot=Done(s.SnapshotAsync());var proof=StrictJson.Deserialize<TraversedConnection>(snapshot.Receipts.Single(r=>r.ReceiptId==source).ResultJson);
            Fixture(s,"fixture:relabelled-edge","explicit_engineering_fixture",new{},[Entity(s,"walked:relabelled","walked_route",new WalkedRoute("R1-S07","R1-S02",source,proof))]);Position(s,"R1-S07",new(48,90));UnchangedReturn(s,travel,"R1-S02");
        });
    }
}

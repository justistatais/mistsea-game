using System.Collections.Immutable;
using Mist.Application.ChapterOne;
using Mist.Application.Contracts;
using Mist.Application.EventScheduler;
using Mist.Application.Inventory;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Application.VesselInterior;
using Mist.Core.ChapterOne;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.VesselInterior;
using Mist.DebugCandidates;
using Mist.Infrastructure.Storage;
using static Mist.Tests.NPC.NpcCases;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.VesselInterior;

public static class VesselInteriorCases
{
    private static readonly Mist.Core.Content.RuntimeCatalog Catalog=Q102.Q102Cases.Catalog();
    private static string ProjectRoot=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    public static VesselInteriorPackage Package()=>StrictJson.Deserialize<VesselInteriorPackage>(File.ReadAllText(Path.Combine(ProjectRoot,"content/source/vessel/vessel_interior.json")));
    private static ProfileStore Store(string name)=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"vessel-interior-"+name+"-"+Guid.NewGuid()),Catalog.ReleaseId);
    private static void Success(CommandResult r)=>Require(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(r));
    private static T Read<T>(ProfileStore s,string id)=>StrictJson.Deserialize<T>(Done(s.SnapshotAsync()).Entities.Single(e=>e.Id==id).Json);
    private static ChapterOneService Chapter(ProfileStore s)=>new(s,Catalog,ChapterOne.ChapterOneCases.Package(),Campaign.CampaignCases.Package().Scenes.ToImmutableDictionary(g=>g.Id),id=>s.CreateCheckpointAsync("protected",protectedId:id));
    private static VesselInteriorService Interior(ProfileStore s,ChapterOneService? chapter=null)=>new(s,Catalog,Package(),(chapter??Chapter(s)).Geometry);
    private static void Walk(ProfileStore s,ChapterOneService c,Point2 point){var a=Read<ActorState>(s,"actor");var path=new NavigationGrid(c.Geometry[a.Scene]).FindPath(a.Position,point,true);Require(path.Length>0);Done(c.MoveAsync(path));}
    private static void Spot(ProfileStore s,ChapterOneService c,string id){var a=Read<ActorState>(s,"actor");Walk(s,c,c.Geometry[a.Scene].Spots.Single(p=>p.Id==id).Stand);}
    private static void Exit(ProfileStore s,ChapterOneService c,string scene){var a=Read<ActorState>(s,"actor");var p=c.Geometry[a.Scene].Ports.Single(p=>p.TargetScene==scene);Walk(s,c,p.Stand);Success(Done(c.EnterPortAsync(p.Id)));}
    private static void VesselWalk(ProfileStore s,VesselInteriorService v,Point2 point){var a=Read<ActorState>(s,"actor");var path=new NavigationGrid(v.Geometry[a.Scene]).FindPath(a.Position,point,true);Require(path.Length>0);Success(Done(v.MoveAsync(path)));}
    private static void VesselSpot(ProfileStore s,VesselInteriorService v,string id){var a=Read<ActorState>(s,"actor");VesselWalk(s,v,v.Geometry[a.Scene].Spots.Single(p=>p.Id==id).Stand);}
    private static void VesselExit(ProfileStore s,VesselInteriorService v,string scene){var a=Read<ActorState>(s,"actor");var port=v.Geometry[a.Scene].Ports.Single(p=>p.TargetScene==scene);VesselWalk(s,v,port.Stand);var preview=Done(v.PreviewExitAsync(port.Id));Require(preview.Ready,string.Join(";",preview.Issues));Success(Done(v.ConfirmPreviewAsync(preview.Token)));}
    private static void ConfirmBoard(VesselInteriorService v){var p=Done(v.PreviewBoardAsync());Require(p.Ready,string.Join(";",p.Issues));Success(Done(v.ConfirmPreviewAsync(p.Token)));}
    private static void RegisterPlacement(ProfileStore s,VesselInteriorService v)
    {
        var travel=new TravelService(s,Catalog,v.Geometry);var berth=Done(v.InspectBerthAsync());Success(berth);Success(Done(travel.RegisterBerthAsync(berth.Receipt!.Id.Value.ToString())));
        foreach(string scene in VesselInteriorRules.Spaces){var space=Done(v.PublishSpaceAsync(scene));Success(space);Success(Done(travel.RegisterShipSpaceAsync(space.Receipt!.Id.Value.ToString())));}
    }
    // This bootstrap plays the actual normal campaign and all three subsequent chapter transactions.
    // No Fixture is called; test-authored prose/meaning and controller choices remain engineering inputs.
    private static ChapterOneService ActualShip(ProfileStore s)
    {
        var coast=new Campaign.CampaignCases.Run(s);coast.ThroughCommission();var c=Chapter(s);Exit(s,c,"R1-S02");var q=new Q102RouteCandidate(s,Catalog);Done(q.Prepare(WorkshopMode.Isolated));var departure=Done(q.Service.PreviewDepartureAsync());Require(departure.Preview.Status==Mist.Application.Commands.PreviewStatus.Ready);Success(Done(q.Service.DepartAsync(departure.Token)));
        Exit(s,c,"R1-S03");Spot(s,c,"R1-S03.havel");Success(Done(c.AcceptInvestigationAsync()));Exit(s,c,"R1-S08");foreach(string id in ChapterOneRules.Evidence){var material=c.Package.Materials.Single(m=>m.Id==id);Spot(s,c,material.Spot);Success(Done(c.CollectAsync(id)));}Exit(s,c,"R1-S03");Spot(s,c,"R1-S03.havel");
        var session=s.Session;Done(s.SaveAsync(new(session.Profile,session.Branch,session.Load,ChapterOneService.FindingDraft,null,0,"The scheduled test and independent coast record fit the bounded clock comparison. The second check was removed; the notice alone cannot establish higher intent. Retain the original records.","actual-vessel-candidate-1")));
        Success(Done(c.ConfirmFindingMeaningAsync(new("approved_preplanned_test","within_recorded_clock_uncertainty","second_check_removed_before_test","insufficient_on_its_own","specific_higher_intent_not_established"),false)));var finding=Done(c.PreviewFindingAsync());Require(finding.Ready);Success(Done(c.ConfirmPreviewAsync(finding.Token)));
        var party=new PartyService(s,Catalog);Success(Done(party.ApplyBuildAsync(c.BattleDefinitions.ReferenceBuild("BUILD-TOWER-M"))));Success(Done(party.EquipAsync("collection:WP-05","weapon")));Exit(s,c,"R1-S02");Exit(s,c,"R1-S09");Spot(s,c,"R1-S09.warden");Success(Done(c.AcceptTowerWorkAsync()));foreach(string id in new[]{"Q104-R-CONTROLS","Q104-R-HANDOVER"}){Spot(s,c,c.Package.Materials.Single(m=>m.Id==id).Spot);Success(Done(c.CollectAsync(id)));}Spot(s,c,"R1-S09.supply_indicator");Success(Done(c.InspectLocalSupplyAsync()));FirstApproaches.FirstApproachCases.CompleteSide(s,c);Exit(s,c,"R1-S10");Spot(s,c,"R1-S10.encounter_entry");Success(Done(c.BeginTowerAsync()));
        for(int i=0;i<600&&Done(c.TowerAsync())!.Battle.Outcome==CombatOutcome.Active;i++){var battle=Done(c.TowerAsync())!.Battle;var p=Done(c.PreviewTowerActionAsync(BattleCandidate.Choose(new(c.BattleDefinitions,battle))));Require(p.Action.Legal);Success(Done(c.ConfirmPreviewAsync(p.Token)));}Require(Done(c.TowerAsync())!.Battle.Outcome==CombatOutcome.Controlled);
        Spot(s,c,"R1-S10.safety_console");Success(Done(c.VerifyTowerAfterControlAsync()));Exit(s,c,"R1-S09");Exit(s,c,"R1-S02");Exit(s,c,"R1-S01");Spot(s,c,"R1-S01.glimmer_handover");var handover=Done(c.PreviewHandoverAsync());Require(handover.Ready);Success(Done(c.ConfirmPreviewAsync(handover.Token)));Require(Done(s.SnapshotAsync()).Receipts.All(r=>!r.SemanticKey.StartsWith("internal:",StringComparison.Ordinal)&&r.ActionType!="explicit_engineering_fixture"));return c;
    }
    public static void Register()
    {
        Add("CX-060","Three actual Glimmer grayboxes pair only adjacent spaces and every functional station has a safe path",()=>
        {
            var package=Package();VesselInteriorRules.Validate(package);Require(package.Scenes.Length==3&&package.Stations.Length==11&&package.Scenes.Sum(s=>s.Ports.Length)==4);
            foreach(var scene in package.Scenes)foreach(var port in scene.Ports)foreach(var spot in scene.Spots){var nav=new NavigationGrid(scene);var path=nav.FindPath(port.Stand,spot.Stand,true);Require(path.Length>0);var previous=port.Stand;foreach(var point in path){Require(nav.SegmentClear(previous,point,true));previous=point;}}
            Throws<InvalidDataException>(()=>VesselInteriorRules.Validate(package with{Stations=package.Stations.RemoveAt(0)}));
        });
        Add("CX-060","Map existence or a test-gifted ship cannot create actual ownership, berth or ship-space evidence",()=>
        {
            using var s=Store("no-gift");var coast=new Campaign.CampaignCases.Run(s);coast.Initialize();var v=Interior(s);long before=Done(s.SnapshotAsync()).Sequence;Require(!Done(v.PreviewBoardAsync()).Ready);Throws<InvalidOperationException>(()=>Done(v.InspectBerthAsync()));Require(Done(s.SnapshotAsync()).Sequence==before);
            var ship=new SharedVessel(Guid.NewGuid(),"R1","N-DECKWATCH",VesselInteriorRules.Spaces);Fixture(s,"Q-104:resolution","explicit_engineering_fixture",new{scope="invalid ownership authority fixture"},[Entity(s,"ship:glimmer","shared_vessel",ship)]);before=Done(s.SnapshotAsync()).Sequence;Throws<InvalidOperationException>(()=>Done(v.InspectBerthAsync()));Require(!Done(v.PreviewBoardAsync()).Ready&&Done(s.SnapshotAsync()).Sequence==before&&!Done(s.SnapshotAsync()).Entities.Any(e=>e.Kind is "vessel_berth" or "vessel_space"));
        });
        Add("CX-060","Actual normal Q104 handover, physical berth inspection, three ship spaces and original workspaces share one persisted vessel",()=>
        {
            using var s=Store("actual-board");var c=ActualShip(s);var v=Interior(s,c);var ship=Read<SharedVessel>(s,"ship:glimmer");var before=Done(s.SnapshotAsync());var original=before.Entities.Single(e=>e.Id=="material:QP03-R-RECORDER");
            Walk(s,c,new(50,84));long rejected=Done(s.SnapshotAsync()).Sequence;Throws<InvalidOperationException>(()=>Done(v.InspectBerthAsync()));Require(Done(s.SnapshotAsync()).Sequence==rejected);Spot(s,c,"R1-S01.glimmer_handover");RegisterPlacement(s,v);
            var inspection=Done(s.SnapshotAsync()).Receipts.Single(r=>r.ActionType=="vessel_berth_verified");var berth=StrictJson.Deserialize<VesselBerth>(inspection.ResultJson);Require(berth.Instance==ship.Instance&&berth.Boarding==c.Geometry["R1-S01"].Spots.Single(p=>p.Id=="R1-S01.glimmer_handover").Stand&&berth.Scene=="R1-S01");Require(Done(v.InspectBerthAsync()).Status==CommitStatus.AlreadyCommitted);
            var p=Done(v.PreviewBoardAsync());Require(p.Ready);Success(Done(v.ConfirmPreviewAsync(p.Token)));Require(Done(v.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.AlreadyCommitted&&Read<ActorState>(s,"actor").Scene=="HUB-S01");Require(!Done(v.PreviewExitAsync("HUB-S01.to.HUB-S02")).Ready);
            VesselSpot(s,v,"HUB-S01.deckwatch");Require(Done(v.WorkspaceAtAsync("HUB-S01.deckwatch")).Ship.WatchOfficer=="N-DECKWATCH");VesselExit(s,v,"HUB-S02");VesselSpot(s,v,"HUB-S02.archive");long query=Done(s.SnapshotAsync()).Sequence;var archive=Done(v.WorkspaceAtAsync("HUB-S02.archive"));Require(archive.Entities.Contains(original)&&Done(s.SnapshotAsync()).Sequence==query);
            VesselSpot(s,v,"HUB-S02.draft_desk");Require(Done(v.WorkspaceAtAsync("HUB-S02.draft_desk")).DraftHeads.Any(h=>h.TaskId==ChapterOneService.FindingDraft));VesselSpot(s,v,"HUB-S02.chart");Require(Done(v.WorkspaceAtAsync("HUB-S02.chart")).Entities.All(e=>e.Kind!="sea_route"));VesselExit(s,v,"HUB-S03");VesselSpot(s,v,"HUB-S03.collection");var collection=Done(v.WorkspaceAtAsync("HUB-S03.collection"));Require(collection.Items.Count(i=>i.Id.StartsWith("Q104:original:",StringComparison.Ordinal))==4);
            VesselSpot(s,v,"HUB-S03.roster");Require(Done(v.WorkspaceAtAsync("HUB-S03.roster")).Roster.All(r=>r.Entry.Membership!=Mist.Application.Roster.Membership.Joined));VesselSpot(s,v,"HUB-S03.work_orders");Require(Done(v.WorkspaceAtAsync("HUB-S03.work_orders")).Entities.Any(e=>e.Id=="readiness:TWR-R1")&&!Done(s.SnapshotAsync()).Entities.Any(e=>e.Kind=="work_order"));
            VesselExit(s,v,"HUB-S02");VesselExit(s,v,"HUB-S01");VesselSpot(s,v,"HUB-S01.gangway");var leave=Done(v.PreviewLeaveAsync());Require(leave.Ready);Success(Done(v.ConfirmPreviewAsync(leave.Token)));var final=Done(s.SnapshotAsync());Require(Read<ActorState>(s,"actor").Scene=="R1-S01"&&StrictJson.Serialize(Read<SharedVessel>(s,"ship:glimmer"))==StrictJson.Serialize(ship)&&StrictJson.Serialize(final.Balances)==StrictJson.Serialize(before.Balances)&&final.Items.SequenceEqual(before.Items)&&final.Entities.Count(e=>e.Kind=="shared_vessel")==1&&final.Receipts.Count(r=>r.SemanticKey=="Q-104:resolution")==1);
            Done(s.ContinueAsync());Require(StrictJson.Serialize(Read<SharedVessel>(s,"ship:glimmer"))==StrictJson.Serialize(ship)&&Done(v.PreviewBoardAsync()).Ready);File.WriteAllText(Path.Combine(Path.GetDirectoryName(s.MainPath)!,"actual-vessel-interior.json"),StrictJson.Serialize(new{scope="actual normal command navigation and SQLite placement/three-space passage; no ownership fixture",ship,berth,receipts=final.Receipts,experience=final.Balances["experience"],human_native_validation="pending",language_6_5_validation="pending"}));
        });
        Add("CX-060","A closed or stale boarding preview cannot move an actor or board a revised berth/other ship",()=>
        {
            using var s=Store("stale-preview");var c=ActualShip(s);var v=Interior(s,c);RegisterPlacement(s,v);var closed=Done(v.PreviewBoardAsync());Require(closed.Ready);v.ClosePreview(closed.Token);Require(Done(v.ConfirmPreviewAsync(closed.Token)).Status==CommitStatus.Rejected);var moved=Done(v.PreviewBoardAsync());Walk(s,c,new(50,84));Require(Done(v.ConfirmPreviewAsync(moved.Token)).Status==CommitStatus.Rejected);Spot(s,c,"R1-S01.glimmer_handover");
            var stale=Done(v.PreviewBoardAsync());var berth=Read<VesselBerth>(s,"berth:R1");Fixture(s,"fixture:berth-revision","explicit_engineering_fixture",new{scope="revision conflict test only"},[Entity(s,"berth:R1","vessel_berth",berth)]);Require(Done(v.ConfirmPreviewAsync(stale.Token)).Status==CommitStatus.Rejected);
            var replacement=Done(v.PreviewBoardAsync());Require(replacement.Ready);var original=Read<SharedVessel>(s,"ship:glimmer");Fixture(s,"fixture:wrong-ship-instance","explicit_engineering_fixture",new{scope="corrupt replacement test only"},[Entity(s,"ship:glimmer","shared_vessel",original with{Instance=Guid.NewGuid()})]);Require(Done(v.ConfirmPreviewAsync(replacement.Token)).Status==CommitStatus.Rejected&&!Done(v.PreviewBoardAsync()).Ready&&Read<ActorState>(s,"actor").Scene=="R1-S01");
        });
        Add("CX-060","Current foreground lease/event and Q604 checkpoint block ship movement without manufacturing placement",()=>
        {
            using var s=Store("foreground");var c=ActualShip(s);var v=Interior(s,c);RegisterPlacement(s,v);var p=Done(v.PreviewBoardAsync());Require(p.Ready);var load=s.Session.Load;Require(Done(s.TryLeasesAsync(load,"actual-test-foreground",["input:foreground"])));Throws<InvalidOperationException>(()=>Done(v.ConfirmPreviewAsync(p.Token)));Done(s.ReleaseLeasesAsync(load,"actual-test-foreground"));
            var scheduler=new EventScheduler(s,s);var e=Done(scheduler.QueueAsync(new("vessel-dialogue","candidate-1","actual-test-foreground",1,["N-DECKWATCH"],"safe",0)));Require(Done(scheduler.StartNextAsync(ScheduleBoundary.Safe,e.Instance)) is not null);Require(!Done(v.PreviewBoardAsync()).Ready);Success(Done(scheduler.CheckpointAsync(e.Instance,"safe-pause",pause:true)));Require(Done(v.PreviewBoardAsync()).Ready);
            Fixture(s,"Q-604:resolution","explicit_engineering_fixture",new{scope="final-stage checkpoint exclusion fixture"});Require(!Done(v.PreviewBoardAsync()).Ready);Throws<InvalidOperationException>(()=>Done(v.InspectBerthAsync()));Require(Read<ActorState>(s,"actor").Scene=="R1-S01");
        });
        Add("CX-060","New ship geometry does not grant later-region berths or invent missing R1 physical station",()=>
        {
            using var s=Store("missing-placement");var c=ActualShip(s);var missing=new VesselInteriorService(s,Catalog,Package(),Campaign.CampaignCases.Package().Scenes.ToImmutableDictionary(g=>g.Id));long before=Done(s.SnapshotAsync()).Sequence;Throws<InvalidOperationException>(()=>Done(missing.InspectBerthAsync()));Require(Done(s.SnapshotAsync()).Sequence==before);
            var v=Interior(s,c);var ship=Read<SharedVessel>(s,"ship:glimmer");Fixture(s,"fixture:other-dock","explicit_engineering_fixture",new{scope="later dock exclusion fixture"},[Entity(s,"ship:glimmer","shared_vessel",ship with{DockRegion="R2"})]);before=Done(s.SnapshotAsync()).Sequence;Throws<InvalidOperationException>(()=>Done(v.InspectBerthAsync()));Require(!Done(v.PreviewBoardAsync()).Ready&&Done(s.SnapshotAsync()).Sequence==before&&!Done(s.SnapshotAsync()).Entities.Any(e=>e.Id=="berth:R2"));
        });
    }
}

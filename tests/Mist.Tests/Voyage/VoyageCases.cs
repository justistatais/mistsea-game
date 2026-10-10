using System.Collections.Immutable;
using Mist.Application.ChapterOne;
using Mist.Application.ChapterTwo;
using Mist.Application.Contracts;
using Mist.Application.EventScheduler;
using Mist.Application.Inventory;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Application.VesselInterior;
using Mist.Application.Voyage;
using Mist.Core.ChapterOne;
using Mist.Core.ChapterTwo;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Engineering;
using Mist.Core.Exploration;
using Mist.Core.Voyage;
using Mist.Core.VesselInterior;
using Mist.DebugCandidates;
using Mist.Infrastructure.Storage;
using static Mist.Tests.NPC.NpcCases;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.Voyage;

public static class VoyageCases
{
    private static readonly Mist.Core.Content.RuntimeCatalog Catalog=Q102.Q102Cases.Catalog();
    private static string ProjectRoot=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    public static VoyagePackage Package()=>StrictJson.Deserialize<VoyagePackage>(File.ReadAllText(Path.Combine(ProjectRoot,"content/source/voyage/r1_r2_voyage.json")));
    public static ChapterTwoPackage Region()=>ChapterTwoPackage.ParseJson(File.ReadAllText(Path.Combine(ProjectRoot,"content/source/chapter_two/learner_package.json")));
    private static ProfileStore Store(string name)=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"voyage-"+name+"-"+Guid.NewGuid()),Catalog.ReleaseId);
    private static void Success(CommandResult r)=>Require(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(r));
    private static T Read<T>(ProfileStore s,string id)=>StrictJson.Deserialize<T>(Done(s.SnapshotAsync()).Entities.Single(e=>e.Id==id).Json);
    private static ChapterOneService Chapter(ProfileStore s)=>new(s,Catalog,ChapterOne.ChapterOneCases.Package(),Campaign.CampaignCases.Package().Scenes.ToImmutableDictionary(g=>g.Id),id=>s.CreateCheckpointAsync("protected",protectedId:id));
    private static VesselInteriorService Interior(ProfileStore s,ChapterOneService c)=>new(s,Catalog,VesselInterior.VesselInteriorCases.Package(),c.Geometry);
    private static void Walk(ProfileStore s,ChapterOneService c,Point2 point){var a=Read<ActorState>(s,"actor");var path=new NavigationGrid(c.Geometry[a.Scene]).FindPath(a.Position,point,true);Require(path.Length>0);Done(c.MoveAsync(path));}
    private static void Spot(ProfileStore s,ChapterOneService c,string id){var a=Read<ActorState>(s,"actor");Walk(s,c,c.Geometry[a.Scene].Spots.Single(p=>p.Id==id).Stand);}
    private static void Exit(ProfileStore s,ChapterOneService c,string scene){var a=Read<ActorState>(s,"actor");var p=c.Geometry[a.Scene].Ports.Single(p=>p.TargetScene==scene);Walk(s,c,p.Stand);Success(Done(c.EnterPortAsync(p.Id)));}
    private static void VesselWalk(ProfileStore s,VesselInteriorService v,Point2 point){var a=Read<ActorState>(s,"actor");var path=new NavigationGrid(v.Geometry[a.Scene]).FindPath(a.Position,point,true);Require(path.Length>0);Success(Done(v.MoveAsync(path)));}
    private static void VesselSpot(ProfileStore s,VesselInteriorService v,string id){var a=Read<ActorState>(s,"actor");VesselWalk(s,v,v.Geometry[a.Scene].Spots.Single(p=>p.Id==id).Stand);}
    public static (ChapterOneService Chapter,VesselInteriorService Interior) NormalChart(ProfileStore s)
    {
        // Public reusable positive bootstrap: every prior authority is an actual normal-new-game service command.
        // Authored test prose/meaning and controller choices remain engineering inputs, not human language evidence.
        var coast=new Campaign.CampaignCases.Run(s);coast.ThroughCommission();var c=Chapter(s);Exit(s,c,"R1-S02");var q=new Q102RouteCandidate(s,Catalog);Done(q.Prepare(WorkshopMode.Isolated));var departure=Done(q.Service.PreviewDepartureAsync());Require(departure.Preview.Status==Mist.Application.Commands.PreviewStatus.Ready);Success(Done(q.Service.DepartAsync(departure.Token)));
        Exit(s,c,"R1-S03");Spot(s,c,"R1-S03.havel");Success(Done(c.AcceptInvestigationAsync()));Exit(s,c,"R1-S08");foreach(string id in ChapterOneRules.Evidence){Spot(s,c,c.Package.Materials.Single(m=>m.Id==id).Spot);Success(Done(c.CollectAsync(id)));}Exit(s,c,"R1-S03");Spot(s,c,"R1-S03.havel");
        var session=s.Session;Done(s.SaveAsync(new(session.Profile,session.Branch,session.Load,ChapterOneService.FindingDraft,null,0,"The scheduled test and independent coast record fit the bounded clock comparison. The second check was removed; the prepared notice alone cannot establish higher intent. Retain the original records.","normal-voyage-candidate-1")));
        Success(Done(c.ConfirmFindingMeaningAsync(new("approved_preplanned_test","within_recorded_clock_uncertainty","second_check_removed_before_test","insufficient_on_its_own","specific_higher_intent_not_established"),false)));var finding=Done(c.PreviewFindingAsync());Require(finding.Ready);Success(Done(c.ConfirmPreviewAsync(finding.Token)));
        var party=new PartyService(s,Catalog);Success(Done(party.ApplyBuildAsync(c.BattleDefinitions.ReferenceBuild("BUILD-TOWER-M"))));Success(Done(party.EquipAsync("collection:WP-05","weapon")));Exit(s,c,"R1-S02");Exit(s,c,"R1-S09");Spot(s,c,"R1-S09.warden");Success(Done(c.AcceptTowerWorkAsync()));foreach(string id in new[]{"Q104-R-CONTROLS","Q104-R-HANDOVER"}){Spot(s,c,c.Package.Materials.Single(m=>m.Id==id).Spot);Success(Done(c.CollectAsync(id)));}Spot(s,c,"R1-S09.supply_indicator");Success(Done(c.InspectLocalSupplyAsync()));FirstApproaches.FirstApproachCases.CompleteSide(s,c);Exit(s,c,"R1-S10");Spot(s,c,"R1-S10.encounter_entry");Success(Done(c.BeginTowerAsync()));
        for(int i=0;i<600&&Done(c.TowerAsync())!.Battle.Outcome==CombatOutcome.Active;i++){var battle=Done(c.TowerAsync())!.Battle;var p=Done(c.PreviewTowerActionAsync(BattleCandidate.Choose(new(c.BattleDefinitions,battle))));Require(p.Action.Legal);Success(Done(c.ConfirmPreviewAsync(p.Token)));}Require(Done(c.TowerAsync())!.Battle.Outcome==CombatOutcome.Controlled);
        Spot(s,c,"R1-S10.safety_console");Success(Done(c.VerifyTowerAfterControlAsync()));Exit(s,c,"R1-S09");Exit(s,c,"R1-S02");Exit(s,c,"R1-S01");Spot(s,c,"R1-S01.glimmer_handover");var handover=Done(c.PreviewHandoverAsync());Require(handover.Ready);Success(Done(c.ConfirmPreviewAsync(handover.Token)));
        var v=Interior(s,c);var travel=new TravelService(s,Catalog,v.Geometry);var berth=Done(v.InspectBerthAsync());Success(berth);Success(Done(travel.RegisterBerthAsync(berth.Receipt!.Id.Value.ToString())));foreach(string scene in VesselInteriorRules.Spaces){var space=Done(v.PublishSpaceAsync(scene));Success(space);Success(Done(travel.RegisterShipSpaceAsync(space.Receipt!.Id.Value.ToString())));}
        var board=Done(v.PreviewBoardAsync());Require(board.Ready);Success(Done(v.ConfirmPreviewAsync(board.Token)));var port=v.Geometry["HUB-S01"].Ports.Single(p=>p.TargetScene=="HUB-S02");VesselWalk(s,v,port.Stand);var transition=Done(v.PreviewExitAsync(port.Id));Require(transition.Ready);Success(Done(v.ConfirmPreviewAsync(transition.Token)));VesselSpot(s,v,"HUB-S02.chart");
        Require(Done(s.SnapshotAsync()).Receipts.All(r=>!r.SemanticKey.StartsWith("internal:",StringComparison.Ordinal)&&r.ActionType is not("explicit_engineering_fixture" or "internal_slice_initialization")));return (c,v);
    }
    public static void Register()
    {
        Add("CX-060","R1/R2 voyage source keeps public landing authority separate from unvisited learner records and verifies actual berth geometry",()=>
        {
            var package=Package();VoyageRules.Validate(package);var region=Region();var landing=region.Scenes.Single(s=>s.Geometry.Id=="R2-S01").Geometry;var spot=VoyageRules.SafeBerth(landing,package.DestinationDuty.BerthSpot);Require(spot.Stand==new Point2(48,90)&&package.DestinationDuty.Scope=="public_landing_only"&&package.OriginalLocalDetails.Length>0);
            foreach(var source in package.Sources){string path=Path.Combine(ProjectRoot,source.Path);Require(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))==source.Sha256);}
            Throws<InvalidDataException>(()=>VoyageRules.Validate(package with{To="R3"}));Throws<InvalidOperationException>(()=>VoyageRules.Request(VoyageRules.R2 with{AdmissionScope="private_archive"}));
            Throws<InvalidOperationException>(()=>VoyageRules.SafeBerth(landing with{Blocked=[new(45,86,52,94)]},spot.Id));Throws<InvalidOperationException>(()=>VoyageRules.SafeBerth(landing with{Danger=[new(45,86,52,94)]},spot.Id));
        });
        Add("CX-060","No new profile, map or explicitly gifted ship can issue the real voyage loan and cross-region arrival handoff",()=>
        {
            using var s=Store("no-authority");new Campaign.CampaignCases.Run(s).Initialize();var c=Chapter(s);var v=Interior(s,c);var voyage=new VoyageService(s,Catalog,Package(),Region(),v.Geometry);long before=Done(s.SnapshotAsync()).Sequence;
            Require(!Done(voyage.PreviewRouteAsync(VoyageRules.R2)).Ready&&!Done(voyage.PreviewBerthAsync(VoyageRules.R2)).Ready);Throws<InvalidOperationException>(()=>Done(voyage.IssueArrivalHandoffAsync(VoyageRules.R2)));Require(Done(s.SnapshotAsync()).Sequence==before);
            Fixture(s,"fixture:invalid-boat-gift","explicit_engineering_fixture",new{scope="invalid authority rejection fixture only"},[Entity(s,"ship:glimmer","shared_vessel",new SharedVessel(Guid.NewGuid(),"R1","N-DECKWATCH",VesselInteriorRules.Spaces))]);Throws<InvalidOperationException>(()=>Done(voyage.IssueArrivalHandoffAsync(VoyageRules.R2)));Require(!Done(s.SnapshotAsync()).Entities.Any(e=>e.Kind is "arrival_handoff" or "sea_route" or "vessel_arrival"));
        });
        Add("CX-060","Actual normal zero-XP rescue through Q104 walks to ship chart, registers real R2 route/berth, sails once and enters ChapterTwo without fixtures",()=>
        {
            using var s=Store("normal-r2");var (_,v)=NormalChart(s);var voyage=new VoyageService(s,Catalog,Package(),Region(),v.Geometry);var before=Done(s.SnapshotAsync());var ship=Read<SharedVessel>(s,"ship:glimmer");var original=before.Entities.Single(e=>e.Id=="material:QP03-R-RECORDER");Require(before.Balances["experience"]==900&&!before.Entities.Any(e=>e.Id=="chapter2"||e.Id.StartsWith("material:Q20",StringComparison.Ordinal)));
            foreach(var request in new[]{VoyageRules.R2 with{Destination="R3"},VoyageRules.R2 with{LandingSpot="R2-S04.archive"},VoyageRules.R2 with{MentorCurrentStatus="alive"}}){Require(!Done(voyage.PreviewRouteAsync(request)).Ready);Throws<InvalidOperationException>(()=>Done(voyage.IssueArrivalHandoffAsync(request)));}Require(Done(s.SnapshotAsync()).Sequence==before.Sequence);
            Throws<InvalidOperationException>(()=>Done(voyage.SailAsync("R2")));Throws<InvalidOperationException>(()=>Done(voyage.RecordArrivalAsync()));
            var session=s.Session;Require(Done(s.TryLeasesAsync(session.Load,"other-foreground-candidate",["input:foreground"])));Throws<InvalidOperationException>(()=>Done(voyage.IssueArrivalHandoffAsync(VoyageRules.R2)));Done(s.ReleaseLeasesAsync(session.Load,"other-foreground-candidate"));Require(Done(s.SnapshotAsync()).Sequence==before.Sequence);
            VesselWalk(s,v,new(72,56));Throws<InvalidOperationException>(()=>Done(voyage.IssueArrivalHandoffAsync(VoyageRules.R2)));VesselSpot(s,v,"HUB-S02.chart");var handoff=Done(voyage.IssueArrivalHandoffAsync(VoyageRules.R2));Success(handoff);Require(Done(voyage.IssueArrivalHandoffAsync(VoyageRules.R2)).Status==CommitStatus.AlreadyCommitted);
            var stale=Done(voyage.PreviewRouteAsync(VoyageRules.R2));Require(stale.Ready);VesselWalk(s,v,new(72,56));Require(Done(voyage.ConfirmPreviewAsync(stale.Token)).Status==CommitStatus.Rejected);VesselSpot(s,v,"HUB-S02.chart");var closed=Done(voyage.PreviewRouteAsync(VoyageRules.R2));voyage.ClosePreview(closed.Token);Require(Done(voyage.ConfirmPreviewAsync(closed.Token)).Status==CommitStatus.Rejected);
            var prepared=Done(voyage.VerifyAndRegisterAsync(VoyageRules.R2));Success(prepared.RouteProof);Success(prepared.BerthProof);Success(prepared.Registration.Route);Success(prepared.Registration.Berth);var registered=Done(s.SnapshotAsync());var route=Read<SeaRoute>(s,"sea-route:R1:R2");var berth=Read<VesselBerth>(s,"berth:R2");Require(route.GateReads.All(g=>g.EntityId is not("actor" or "ship:glimmer"))&&route.GateReads.SequenceEqual(berth.GateReads)&&berth.Instance==ship.Instance&&berth.Landing==new Point2(48,90)&&Read<ActorState>(s,"actor").Scene=="HUB-S02");
            Require(registered.Balances.Count==before.Balances.Count&&registered.Balances.All(p=>before.Balances.GetValueOrDefault(p.Key)==p.Value)&&registered.Items.SequenceEqual(before.Items)&&registered.Entities.Single(e=>e.Id==original.Id)==original&&!registered.Entities.Any(e=>e.Id=="chapter2"||e.Id.StartsWith("material:R2-",StringComparison.Ordinal)));
            var sailed=Done(voyage.SailAsync("R2"));Success(sailed);var arrival=Read<ActorState>(s,"actor");Require(arrival.Scene=="R2-S01"&&arrival.Position==berth.Landing&&StrictJson.Serialize(Read<SharedVessel>(s,"ship:glimmer"))==StrictJson.Serialize(ship with{DockRegion="R2"}));Success(Done(voyage.RecordArrivalAsync()));Require(Done(voyage.RecordArrivalAsync()).Status==CommitStatus.AlreadyCommitted);
            var proof=Read<VesselArrivalProof>(s,"voyage:arrival:"+ship.Instance+":R2");Require(proof.SailReceipt==sailed.Receipt!.Id.Value.ToString()&&proof.AuthorityScope=="public_landing_only_not_local_registration"&&proof.BerthReceipt==berth.AuthorityReceipt&&proof.RouteReceipt==route.AuthorityReceipt);
            var chapter2=new ChapterTwoService(s,Catalog,Region());Done(chapter2.InitializeRegionAsync());var final=Done(s.SnapshotAsync());var state=Read<ChapterTwoState>(s,"chapter2");Require(!state.Landing&&!state.Reception&&!state.BasicArchive&&final.Balances["experience"]==900&&final.Entities.Count(e=>e.Kind=="shared_vessel")==1&&final.Receipts.Count(r=>r.ActionType=="sail_shared_glimmer")==1&&final.Receipts.All(r=>!r.SemanticKey.StartsWith("internal:",StringComparison.Ordinal)&&r.ActionType is not("explicit_engineering_fixture" or "internal_slice_initialization")));
            Require(!final.Entities.Any(e=>e.Id.StartsWith("material:R2-",StringComparison.Ordinal))&&Read<TowerReadiness>(s,"readiness:TWR-R1").Safe&&final.Items.Count(i=>i.Id.StartsWith("Q104:original:",StringComparison.Ordinal))==4);
            Done(s.ContinueAsync());Require(Read<ActorState>(s,"actor").Scene=="R2-S01"&&Read<SharedVessel>(s,"ship:glimmer").Instance==ship.Instance&&Read<TowerReadiness>(s,"readiness:TWR-R1").Safe&&Read<ChapterTwoState>(s,"chapter2")==state);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(s.MainPath)!,"normal-r1-r2-voyage.json"),StrictJson.Serialize(new{scope="actual normal command navigation, source-bound chart/duty receipts, Travel sailing and persistent SQLite arrival; no positive fixtures",experience=900,ship,route,berth,proof,receipts=final.Receipts,unvisited_r2_materials_granted=false,human_native_validation="pending",language_calibration="pending"}));
        });
    }
}

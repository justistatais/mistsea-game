using System.Collections.Immutable;
using Mist.Application.ChapterTwo;
using Mist.Application.Contracts;
using Mist.Application.Inventory;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Core.ChapterOne;
using Mist.Core.ChapterTwo;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Engineering;
using Mist.Core.Exploration;
using Mist.Core.Quests;
using Mist.DebugCandidates;
using Mist.Infrastructure.Storage;
using static Mist.Tests.NPC.NpcCases;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.ChapterTwo;

// Most rejection/branch cases use explicit prior Q104/source SQLite engineering fixtures.
// The final continuous case uses VoyageCases.NormalChart and no positive fixture.
// All R2 sailing/movement/source/device/battle/draft transactions are actual service calls.
// Neither case type is a native-player, audio or language validation claim.
public static class ChapterTwoCases
{
    private static readonly Mist.Core.Content.RuntimeCatalog Catalog=Q102.Q102Cases.Catalog();
    public static ChapterTwoPackage Package()=>ChapterTwoPackage.ParseJson(File.ReadAllText(Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../..")),"content/source/chapter_two/learner_package.json")));
    private static ProfileStore Store(string name)=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"chapter-two-"+name+"-"+Guid.NewGuid()),Catalog.ReleaseId);
    private static void Success(CommandResult r)=>Require(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(r));
    private static T Read<T>(ProfileStore s,string id)=>StrictJson.Deserialize<T>(Done(s.SnapshotAsync()).Entities.Single(e=>e.Id==id).Json);
    private static string Evidence(ProfileStore s,string action,object payload)=>Fixture(s,"fixture:chapter2-source:"+Guid.NewGuid(),action,payload).Receipt!.Id.Value.ToString();
    private sealed class Run(ProfileStore store)
    {
        public ChapterTwoService Service {get;}=new(store,Catalog,Package());
        public ChapterTwoView View=>Done(Service.ViewAsync());
        public void FixturePriorHandoverAndSail()
        {
            Done(new PartyService(store,Catalog).InitializeStarterCollectionAsync());var ship=new SharedVessel(Guid.NewGuid(),"R1","N-DECKWATCH",["HUB-S01","HUB-S02","HUB-S03"]);
            Fixture(store,"Q-104:resolution","tidegate_vessel_handover",new TidegateHandover(ship.Instance.ToString(),"GLIMMER","N-DECKWATCH",ship.Spaces.ToArray(),"R2","E-01"),
                [Entity(store,"quest:Q-104","quest",new QuestState("Q-104",Guid.NewGuid(),QuestLifecycle.Closed,QuestOutcome.Completed)),Entity(store,"ship:glimmer","shared_vessel",ship),Entity(store,"commission:cross-region","permission",new{allowed=true,next_region="R2",basis="Q-104:resolution",purpose="receiving_record_cross_check",mara_current_status="unknown"}),Entity(store,"actor","actor",new ActorState("R1-S01",new(48,90),0))],balances:[new("experience",900)]);
            var geo=Service.Geometry.Add("R1-S01",new SceneGeometry("R1-S01",new(5,20,95,95),[],[],[],[]));var travel=new TravelService(store,Catalog,geo);
            foreach(string region in new[]{"R1","R2"})Success(Done(travel.RegisterBerthAsync(Evidence(store,"vessel_berth_verified",new VesselBerth(ship.Instance,region,region+"-S01",new(48,90),new(48,90),[],"explicit prior berth source fixture")))));
            Success(Done(travel.RegisterSeaRouteAsync(Evidence(store,"sea_route_verified",new SeaRoute("R1","R2",[],"explicit prior sea source fixture")))));
            Success(Done(travel.SailAsync("R2")));Done(Service.InitializeRegionAsync());
        }
        public void Walk(Point2 point){var v=View;var path=new NavigationGrid(v.Geometry[v.Actor.Scene]).FindPath(v.Actor.Position,point,true);Require(path.Length>0,"actual R2 path missing: "+v.Actor.Scene);Done(Service.TraverseAsync(path));}
        public void Spot(string spot)=>Walk(View.Geometry[View.Actor.Scene].Spots.Single(x=>x.Id==spot).Stand);
        public void Exit(string scene){var port=View.Geometry[View.Actor.Scene].Ports.Single(x=>x.TargetScene==scene);Walk(port.Stand);Success(Done(Service.EnterPortAsync(port.Id)));}
        public void Material(string id){var m=Service.Package.Materials.Single(x=>x.Id==id);Spot(m.Spot);Success(Done(Service.DiscoverAsync(id)));}
        public void Act(string action){Spot(Service.Package.Stations.Single(x=>x.Action==action).Spot);Success(Done(Service.ExecuteStationAsync(action)));}
        public void Meaning(string quest,params (string Key,string Value)[] fields)
        {var task=Service.Package.WritingTasks.Single(x=>x.Quest==quest);Spot(task.Spot);var draft=Done(store.GetDraftAsync(task.Id));Done(Service.SaveDraftAsync(task.Id,draft?.Revision??0,draft?.Id,"Engineering test prose; explicit typed meaning below is not automatic English interpretation."));Success(Done(Service.ConfirmMeaningAsync(quest,fields.ToImmutableDictionary(x=>x.Key,x=>x.Value),true)));}
        public ChapterTwoPreview Submit(string quest){var p=Done(Service.PreviewSubmissionAsync(quest));Require(p.Ready,StrictJson.Serialize(p));Success(Done(Service.ConfirmPreviewAsync(p.Token)));return p;}
        public void Node201(bool submit=true)
        {
            Material("R2-Q201-R-LANDING");Act("register_landing");Exit("R2-S02");Material("R2-Q201-R-REQUEST");Act("request_local_reception");Material("R2-Q201-R-OFFICIAL");Exit("R2-S03");Act("inspect_residence_a");Act("inspect_residence_b");Act("inspect_residence_c");Material("R2-Q201-R-RATION");Material("R2-Q201-R-ROOMS");Exit("R2-S02");Meaning("Q-201",("scope","local_group"),("register_meaning","classification_not_current_presence"),("motive","unknown"));if(submit)Submit("Q-201");
        }
        public void Node202()
        {
            Exit("R2-S04");Material("R2-Q202-R-ACCESS");Material("PZTIME-R-ORDER");Material("PZTIME-R-ARRIVAL");Spot("R2-S04.scope");Success(Done(Service.ConfirmM12Async("before_14:10_exact_minute_unknown","14:40","16:05",false,"unknown","unknown")));
            Success(Done(Service.RequestRecordScopeAsync("TR-D1-7","1",["transfer_id","identity_link","dated_condition","destination_code","version_source"],"patrol_technician","verify_transfer")));Material("R2-Q202-R-SENDING");Material("R2-Q202-R-D1");Success(Done(Service.VerifyD1ChainAsync("TR-D1-7","D-1_only","unknown")));Material("R2-Q202-R-COHORT");
            Meaning("Q-202",("mara_time_scope","D-1_only"),("mara_current_status","unknown"),("identity_accountability","separate_fields"),("comparison","different_measures"));Submit("Q-202");Exit("R2-S02");
        }
        public void Battle(string id)
        {
            string spot=id=="ENC-R2-01"?"R2-S05.root_route":id=="ENC-R2-02"?"R2-S06.conveyor":"R2-S07.core_entry";Spot(spot);Success(Done(Service.BeginEncounterAsync(id)));
            for(int i=0;i<600&&Done(Service.EncounterAsync(id))!.Battle.Outcome==CombatOutcome.Active;i++){var b=Done(Service.EncounterAsync(id))!.Battle;var request=BattleCandidate.Choose(new(Service.BattleDefinitions,b));var preview=Done(Service.PreviewEncounterActionAsync(id,request));Require(preview.Action.Legal,preview.Description);Success(Done(Service.ConfirmPreviewAsync(preview.Token)));}
            Require(Done(Service.EncounterAsync(id))!.Battle.Outcome==CombatOutcome.Controlled,"real chapter battle did not control "+id);
        }
        public void CoreStep(string step){Spot(step is "secure_a" or "inspect_valve_a"?"R2-S07.branch_a":step is "secure_b" or "inspect_valve_b"?"R2-S07.branch_b":"R2-S07.core_entry");Success(Done(Service.PerformCoreStepAsync(step)));}
        public void Node203(string method="sample",LocalLoad load=LocalLoad.Kitchen)
        {
            var party=new PartyService(store,Catalog);Success(Done(party.ApplyBuildAsync(Service.BattleDefinitions.ReferenceBuild("BUILD-TOWER-M"))));var weapon=Done(store.SnapshotAsync()).Items.Single(i=>i.Id=="collection:WP-05");if(weapon.OwnerKind!="equipped"||weapon.OwnerId!="hero"||weapon.Slot!="weapon")Success(Done(party.EquipAsync(weapon.Id,"weapon")));
            Exit("R2-S05");Material("R2-Q203-R-WATER");Act("collect_local_maintenance_items");Battle("ENC-R2-01");Act("inspect_stair_boundary");Exit("R2-S06");Material("R2-Q203-R-OLD");Material("R2-Q203-R-INTERLOCK");Act("isolate_conveyor");Spot("R2-S06.conveyor");foreach(string step in new[]{"open_conveyor_guard","release_conveyor_jam","secure_conveyor_guard","inspect_conveyor_stopped"})Success(Done(Service.PerformConveyorStepAsync(step)));
            Act("set_external_clamp");Act("drain_upper_floor");Act("inspect_upper_route");Exit("R2-S07");Material("R2-Q203-R-PANEL");Act("inspect_branch_a");Act("inspect_branch_b");Spot("R2-S07.core_entry");Success(Done(Service.ChooseCoreMethodAsync(method)));CoreStep("secure_a");CoreStep("secure_b");Battle("ENC-R2-03");CoreStep(method=="sample"?"take_sample":"clear_winding");CoreStep("inspect_valve_a");CoreStep("inspect_valve_b");
            Act("isolate_local_reserve");Act("seat_local_connector");Spot("R2-S07.selector");Success(Done(Service.SetLocalLoadAsync(load)));Act("power_local_reserve");Act("test_local_configuration");Act("open_inner_latch");Exit("R2-S06");Exit("R2-S05");Act("inspect_kitchen_feed");Exit("R2-S02");Act("request_tower_work_permit");Exit("R2-S08");Material("R2-Q203-R-TOWER");Act("inspect_tower_duty");Act("inspect_tower_isolation");Act("inspect_tower_return_route");
            Meaning("Q-203",("power_scope","local_only"),("tower_preparation","safe_not_converted"),("load_configuration",load.ToString()));Submit("Q-203");Exit("R2-S02");
        }
        public void Node204(string path="redacted_public_summary")
        {
            Exit("R2-S04");Spot("R2-S04.scope");Success(Done(Service.RequestRecordScopeAsync("TS-18","1",["signature_reference","version_source"],"patrol_technician","verify_historical_scope")));Material("R2-Q204-R-ESSAY");Material("R2-Q204-R-SIGNATURE");Material("R2-Q204-R-BATCH");Exit("R2-S02");Spot("R2-S02.delivery");Success(Done(Service.SetDisclosureScopeAsync(path,["public_fact","record_dates","source_versions","local_safety_boundary","historical_signature_uncertainty","mining_batch"],path=="redacted_public_summary"?"priory_public_board":"priory_review_desk","verify_public_facts","R2-facts-v1")));
            Meaning("Q-204",("mara_current_status","unknown"),("signature_scope","historical_needs_verification"),("public_scrutiny","preserved"),("mining_direction","MC-2"));Submit("Q-204");
        }
    }
    public static void Register()
    {
        Add("CP-R2-01","R2 learner candidate has exact ten-scene graph, real stand references, scripts and full writing prompts",()=>
        {var p=Package();Require(p.Scenes.Length==10&&p.Scenes.Sum(x=>x.Geometry.Ports.Length)==20&&p.Materials.Length==27&&p.Materials.Count(x=>x.Kind=="listening_script")==5&&p.Materials.Where(x=>x.Kind=="listening_script").All(x=>x.AudioAsset==""));Require(p.WritingTasks.Any(x=>x.Quest=="Q-202"&&x.Kind=="full_visual_comparison")&&p.WritingTasks.Any(x=>x.Quest=="Q-204"&&x.Kind=="full_argument"));foreach(var scene in p.Scenes){var nav=new NavigationGrid(scene.Geometry);foreach(var port in scene.Geometry.Ports)foreach(var spot in scene.Geometry.Spots)Require(nav.FindPath(port.Stand,spot.Stand).Length>0,"static blocked-geometry route missing");}});
        Add("CP-R2-03","Bare actor R2 scene cannot fabricate physical voyage authority or regional progress",()=>
        {using var s=Store("bare-actor");Fixture(s,"fixture:bare-r2","explicit_engineering_fixture",new{scope="invalid entry only"},[Entity(s,"actor","actor",new ActorState("R2-S01",new(48,90),0))]);var service=new ChapterTwoService(s,Catalog,Package());long sequence=Done(s.SnapshotAsync()).Sequence;Throws<InvalidOperationException>(()=>Done(service.InitializeRegionAsync()));Require(Done(s.SnapshotAsync()).Sequence==sequence&&!Done(s.SnapshotAsync()).Entities.Any(x=>x.Id=="chapter2"));});
        Add("CP-R2-03","Actual Travel sailing precedes R2 initialise; remote material and archive shortcuts are rejected",()=>
        {using var s=Store("entry");var r=new Run(s);r.FixturePriorHandoverAndSail();Require(r.View.Actor.Scene=="R2-S01"&&r.View.Materials.Length==0&&r.View.Snapshot.Balances["experience"]==900);long seq=r.View.Snapshot.Sequence;Done(r.Service.InitializeRegionAsync());Require(r.View.Snapshot.Sequence==seq);Throws<InvalidOperationException>(()=>Done(r.Service.DiscoverAsync("R2-Q202-R-D1")));Require(s.Session.Sequence==seq);});
        Add("CP-R2-03","Q201 requires three actual porch observations; saved draft and read materials do not award XP",()=>
        {using var s=Store("residence");var r=new Run(s);r.FixturePriorHandoverAndSail();r.Material("R2-Q201-R-LANDING");r.Act("register_landing");r.Exit("R2-S02");r.Material("R2-Q201-R-REQUEST");r.Act("request_local_reception");r.Meaning("Q-201",("scope","local_group"),("register_meaning","classification_not_current_presence"),("motive","unknown"));var p=Done(r.Service.PreviewSubmissionAsync("Q-201"));Require(!p.Ready&&r.View.Snapshot.Balances["experience"]==900&&!r.View.State.BasicArchive);});
        Add("CP-R2-03","Q202 keeps unknown authorisation minute and D-1-only fact, never present mentor state",()=>
        {using var s=Store("d1");var r=new Run(s);r.FixturePriorHandoverAndSail();r.Node201();r.Exit("R2-S04");r.Material("PZTIME-R-ORDER");r.Material("PZTIME-R-ARRIVAL");r.Spot("R2-S04.scope");Throws<InvalidOperationException>(()=>Done(r.Service.ConfirmM12Async("13:50","14:40","16:05",false,"unknown","unknown")));r.Exit("R2-S02");r.Node202();Require(r.View.Snapshot.Balances["experience"]==1380&&r.View.Snapshot.Entities.Single(x=>x.Id=="finding:K-MARA-D1-ALIVE").Json.Contains("D-1_only",StringComparison.Ordinal)&&!r.View.Snapshot.Entities.Any(x=>x.Id=="finding:K-MARA-DESIGN-LIMIT"));});
        Add("CP-R2-03","Parallel Q203 before Q202 uses actual three domains and current tower checks without full conversion",()=>
        {using var s=Store("parallel-safety");var r=new Run(s);r.FixturePriorHandoverAndSail();r.Node201();r.Node203("mechanical",LocalLoad.Alternating);var tower=Read<TowerReadiness>(s,"readiness:TWR-R2");Require(tower.Safe&&!tower.Baseline&&!tower.Trial&&!tower.Operators&&!tower.Transition&&tower.EnhancedVersion is null&&r.View.Snapshot.Balances["experience"]==1380);Require(r.View.Quests.Single(x=>x.Id=="Q-202").Lifecycle==QuestLifecycle.Active&&r.View.Quests.Single(x=>x.Id=="Q-204").Lifecycle==QuestLifecycle.Unaccepted);Require(new[]{"ENC-R2-01","ENC-R2-02","ENC-R2-03"}.All(id=>r.View.Snapshot.Receipts.Count(x=>x.SemanticKey=="R2:"+id+":result")==1));});
        Add("CP-R2-03","All four R2 actual tasks reward once, keep protected scrutiny and survive continue",()=>
        {using var s=Store("chapter");var r=new Run(s);r.FixturePriorHandoverAndSail();long marks=r.View.Snapshot.Balances.GetValueOrDefault("marks"),parts=r.View.Snapshot.Balances.GetValueOrDefault("parts");r.Node201();r.Node202();r.Node203();r.Node204();var final=r.View.Snapshot;Require(final.Balances["experience"]==1920&&final.Balances["marks"]-marks==320&&final.Balances["parts"]-parts==5&&MainQuests.All(q=>final.Receipts.Count(x=>x.SemanticKey==q+":resolution")==1));Require(final.Entities.Any(x=>x.Id=="R2:actual_handover")&&Read<ActorState>(s,"actor").Scene=="R2-S02");Done(s.ContinueAsync());Require(r.View.Snapshot.Balances["experience"]==1920&&r.View.State.LocalStable&&Read<TowerReadiness>(s,"readiness:TWR-R2").Safe);});
        Add("CP-R2-03","Draft CAS and retained read-only preview reject modified writing without issuing archive scope",()=>
        {using var s=Store("draft-preview");var r=new Run(s);r.FixturePriorHandoverAndSail();r.Node201();var task="R2-Q202-W-VISUAL";r.Exit("R2-S04");r.Spot("R2-S04.scope");var d=Done(s.GetDraftAsync(task));Done(r.Service.SaveDraftAsync(task,d?.Revision??0,d?.Id,"first full draft"));var current=Done(s.GetDraftAsync(task))!;Throws<StoreConflictException>(()=>Done(r.Service.SaveDraftAsync(task,0,current.Id,"stale overwrite")));Require(Done(s.GetDraftAsync(task))!.Body=="first full draft");Require(!Done(r.Service.PreviewSubmissionAsync("Q-202")).Ready);});
        Add("CP-R2-03","A ready preview remains read-only and becomes stale after a draft change or continuation",()=>
        {using var s=Store("ready-preview");var r=new Run(s);r.FixturePriorHandoverAndSail();r.Node201(false);var p=Done(r.Service.PreviewSubmissionAsync("Q-201"));Require(p.Ready&&!r.View.State.BasicArchive&&r.View.Snapshot.Balances["experience"]==900);var task="R2-Q201-W-DIFFERENCE";var d=Done(s.GetDraftAsync(task))!;Done(r.Service.SaveDraftAsync(task,d.Revision,d.Id,"changed current draft"));Require(Done(r.Service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.Rejected&&!r.View.State.BasicArchive);Success(Done(r.Service.ConfirmMeaningAsync("Q-201",new Dictionary<string,string>{{"scope","local_group"},{"register_meaning","classification_not_current_presence"},{"motive","unknown"}}.ToImmutableDictionary(),true)));var continued=Done(r.Service.PreviewSubmissionAsync("Q-201"));Require(continued.Ready);Done(s.ContinueAsync());Require(Done(r.Service.ConfirmPreviewAsync(continued.Token)).Status==CommitStatus.Rejected&&r.View.Snapshot.Balances["experience"]==900);});
        Add("CP-R2-03","Individual W04 scope rejects a private address and completes either actual limited-copy path once",()=>
        {using var s=Store("w04");var r=new Run(s);r.FixturePriorHandoverAndSail();r.Node201();r.Node202();r.Node203("mechanical",LocalLoad.Alternating);r.Node204("entrusted_review");r.Exit("R2-S04");r.Spot("R2-S04.mira");Success(Done(r.Service.DecideMiraInvitationAsync(false)));Success(Done(r.Service.DecideW04InvitationAsync("accept")));r.Exit("R2-S02");r.Exit("R2-S03");r.Spot("R2-S03.w04");Throws<InvalidOperationException>(()=>Done(r.Service.RequestW04ScopeAsync(["private_address"],"priory_review_desk","verify_local_note","W04-v1")));Success(Done(r.Service.RequestW04ScopeAsync(["observation_time","witness_duty","version_source"],"priory_review_desk","verify_local_note","W04-v1")));r.Material("R2-CQMIRA01-R-W04");r.Exit("R2-S02");r.Exit("R2-S04");r.Meaning("CQ-MIRA-01",("path","custodian_relay"),("recipient","priory_review_desk"),("purpose","verify_local_note"),("version","W04-v1"),("address","excluded"));var p=r.Submit("CQ-MIRA-01");Require(Done(r.Service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.AlreadyCommitted&&r.View.Snapshot.Balances["experience"]==1945&&r.View.Snapshot.Receipts.Count(x=>x.SemanticKey=="Q-204:resolution")==1&&r.View.Snapshot.Receipts.Count(x=>x.SemanticKey=="CQ-MIRA-01:resolution")==1);});
        Add("CP-R2-03","Continuous actual normal zero-XP voyage reaches all four R2 tasks without positive fixtures",()=>
        {
            using var s=Store("normal-complete");NormalComplete(s);
        });
    }
    // Reuses only the actual zero-XP campaign and command route for later chapter tests.
    public static (ChapterTwoService Service,Mist.Application.VesselInterior.VesselInteriorService Interior) NormalComplete(ProfileStore s)
    {
            var (_,interior)=Voyage.VoyageCases.NormalChart(s);var voyage=new Mist.Application.Voyage.VoyageService(s,Catalog,Voyage.VoyageCases.Package(),Package(),interior.Geometry);
            var verified=Done(voyage.VerifyAndRegisterAsync(Mist.Core.Voyage.VoyageRules.R2));Success(verified.RouteProof);Success(verified.BerthProof);Success(verified.Registration.Route);Success(verified.Registration.Berth);Success(Done(voyage.SailAsync("R2")));Success(Done(voyage.RecordArrivalAsync()));
            var r=new Run(s);Done(r.Service.InitializeRegionAsync());var start=r.View.Snapshot;Require(start.Balances["experience"]==900);long marks=start.Balances.GetValueOrDefault("marks"),parts=start.Balances.GetValueOrDefault("parts");r.Node201();r.Node203("mechanical",LocalLoad.Alternating);r.Node202();r.Node204();
            var final=r.View.Snapshot;Require(final.Balances["experience"]==1920&&final.Balances["marks"]-marks==320&&final.Balances["parts"]-parts==5&&MainQuests.All(q=>final.Receipts.Count(x=>x.SemanticKey==q+":resolution")==1));
            Require(final.Receipts.All(x=>!x.SemanticKey.StartsWith("internal:",StringComparison.Ordinal)&&x.ActionType is not("explicit_engineering_fixture" or "internal_slice_initialization"))&&final.Entities.Count(x=>x.Kind=="shared_vessel")==1&&Read<TowerReadiness>(s,"readiness:TWR-R2").Safe);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(s.MainPath)!,"normal-r2-main.json"),StrictJson.Serialize(new{scope="actual normal zero-XP through Q104, voyage and all four R2 commands; no positive fixtures",experience=1920,delta_marks=320,delta_parts=5,receipts=final.Receipts,native_human="pending",language_calibration="pending",audio="not_recorded"}));
            return (r.Service,interior);
    }
    private static ImmutableArray<string> MainQuests=>ChapterTwoService.MainQuests;
}

using System.Collections.Immutable;
using System.Text.Json;
using Mist.Application.ChapterFour;
using Mist.Application.Contracts;
using Mist.Application.Inventory;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Application.VesselInterior;
using Mist.Application.Voyage;
using Mist.Core.ChapterOne;
using Mist.Core.ChapterThree;
using Mist.Core.ChapterFour;
using Mist.Core.ChapterTwo;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Engineering;
using Mist.Core.Exploration;
using Mist.Core.Quests;
using Mist.Core.Voyage;
using Mist.DebugCandidates;
using Mist.Infrastructure.Storage;
using static Mist.Tests.NPC.NpcCases;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.ChapterFour;

// Branch/rejection cases declare prior Q304 engineering fixtures. Regional sailing, sample custody,
// movement, sources, live apparatus, battles, drafts and rewards then use actual SQLite commands.
// The last case starts from zero XP via the actual preceding chapters and never creates a positive fixture.
public static class ChapterFourCases
{
    private static readonly Mist.Core.Content.RuntimeCatalog Catalog=Q102.Q102Cases.Catalog();
    private static string ProjectRoot=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    public static ChapterFourPackage Package()=>ChapterFourPackage.ParseJson(File.ReadAllText(Path.Combine(ProjectRoot,"content/source/chapter_four/learner_package.json")));
    public static RegionalVoyageDefinition VoyageDefinition()=>StrictJson.Deserialize<RegionalVoyageDefinition>(File.ReadAllText(Path.Combine(ProjectRoot,"content/source/voyage/r3_r4_voyage.json")));
    private static ProfileStore Store(string name)=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"chapter-four-"+name+"-"+Guid.NewGuid()),Catalog.ReleaseId);
    private static void Success(CommandResult r)=>Require(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(r));
    private static T Read<T>(ProfileStore s,string id)=>StrictJson.Deserialize<T>(Done(s.SnapshotAsync()).Entities.Single(e=>e.Id==id).Json);
    private static string Evidence(ProfileStore s,string action,object body)=>Fixture(s,"fixture:R4-prior-source:"+Guid.NewGuid(),action,body).Receipt!.Id.Value.ToString();
    private static void VesselWalk(ProfileStore s,VesselInteriorService v,Point2 point){var a=Read<ActorState>(s,"actor");var path=new NavigationGrid(v.Geometry[a.Scene]).FindPath(a.Position,point,true);Require(path.Length>0);Success(Done(v.MoveAsync(path)));}
    public static (ChapterFourService Service,VesselInteriorService Interior) ActualArrival(ProfileStore s)
    {
        var (prior,oldInterior)=ChapterThree.ChapterThreeCases.NormalComplete(s);var run=new ChapterThree.ChapterThreeCases.Run(s,prior);run.Goto("R3-S01");run.Spot("R3-S01.berth");
        var service=new ChapterFourService(s,Catalog,Package());var geo=oldInterior.Geometry.SetItems(Done(prior.CurrentGeometryAsync())).SetItems(service.Geometry);var interior=new VesselInteriorService(s,Catalog,oldInterior.Package,geo);
        var board=Done(interior.PreviewBoardAsync());Require(board.Ready,StrictJson.Serialize(board));Success(Done(interior.ConfirmPreviewAsync(board.Token)));var exit=interior.Geometry["HUB-S01"].Ports.Single(p=>p.TargetScene=="HUB-S02");VesselWalk(s,interior,exit.Stand);Success(Done(interior.EnterPortAsync(exit.Id)));VesselWalk(s,interior,interior.Geometry["HUB-S02"].Spots.Single(p=>p.Id=="HUB-S02.chart").Stand);
        var voyage=new RegionalVoyageService(s,Catalog,VoyageDefinition(),interior.Geometry);var proof=Done(voyage.VerifyAndRegisterAsync());Success(proof.RouteProof);Success(proof.BerthProof);Success(proof.Registration.Route);Success(proof.Registration.Berth);Success(Done(voyage.SailAsync("R4")));Success(Done(voyage.RecordArrivalAsync()));Done(service.InitializeRegionAsync());return(service,interior);
    }
    public static (ChapterFourService Service,VesselInteriorService Interior) NormalComplete(ProfileStore s)
    {var pair=ActualArrival(s);var r=new Run(s,pair.Service);r.Receive();r.Node402();r.Node401();r.Node403();r.Node404();return pair;}
    public sealed class Run(ProfileStore store,ChapterFourService? service=null)
    {
        public ChapterFourService Service {get;}=service??new(store,Catalog,Package());
        public ChapterFourView View=>Done(Service.ViewAsync());
        public void FixturePriorCommissionAndActualVoyage()
        {
            Done(new PartyService(store,Catalog).InitializeStarterCollectionAsync());var ship=new SharedVessel(Guid.NewGuid(),"R3","N-DECKWATCH",["HUB-S01","HUB-S02","HUB-S03"]);var vp=VesselInterior.VesselInteriorCases.Package();var geo=ChapterThree.ChapterThreeCases.Package().Scenes.ToImmutableDictionary(x=>x.Geometry.Id,x=>x.Geometry).SetItems(Service.Geometry).SetItems(vp.Scenes.ToDictionary(x=>x.Id));
            Fixture(store,"Q-104:resolution","tidegate_vessel_handover",new TidegateHandover(ship.Instance.ToString(),"GLIMMER","N-DECKWATCH",ship.Spaces.ToArray(),"R2","E-01"),[Entity(store,"quest:Q-104","quest",new QuestState("Q-104",Guid.NewGuid(),QuestLifecycle.Closed,QuestOutcome.Completed)),Entity(store,"ship:glimmer","shared_vessel",ship),Entity(store,"actor","actor",new ActorState("HUB-S02",geo["HUB-S02"].Spots.Single(x=>x.Id=="HUB-S02.chart").Stand,1))],balances:[new("experience",3840)]);
            var versions=new Dictionary<string,string>{{"MC-2.original-instrument","MC-2.instrument.I@1"},{"MC-2.workshop-copy","MC-2.retained.I@1"},{"MC-2.formal-acceptance","MC-2.acceptance@3"}}.ToImmutableDictionary();var hand=new MiningTowerHandover("TWR-R3","R4","MC-2",ChapterFourService.SampleId,versions,1,"R4-public-verification-desk","verify_materials_and_signatory_scope","association_requires_verification","unknown");
            Fixture(store,"Q-304:resolution","chapter_three_tower_handover_received",hand,[Entity(store,"quest:Q-304","quest",new QuestState("Q-304",Guid.NewGuid(),QuestLifecycle.Closed,QuestOutcome.Completed)),Entity(store,"R3:actual_handover","mining_handover",hand),Entity(store,"R3:sample_custody","sample_custody",new{seal=ChapterFourService.SampleId,batch="MC-2",custodian="patrol_technician",recipient=hand.Recipient,purpose=hand.Purpose}),Entity(store,"commission:next-inquiry","permission",new{allowed=true,region="R4",purpose=hand.Purpose,basis="Q-304:resolution",arrival_occurred=false})],items:[new(ChapterFourService.SampleId,"R3-SEALED-VERIFICATION-SAMPLE","new","","player","hero",null)]);
            var travel=new TravelService(store,Catalog,geo);var landing=geo["R3-S01"].Spots.Single(x=>x.Id=="R3-S01.berth").Stand;Success(Done(travel.RegisterBerthAsync(Evidence(store,"vessel_berth_verified",new VesselBerth(ship.Instance,"R3","R3-S01",landing,landing,[],"explicit prior engineering berth fixture")))));
            foreach(var scene in vp.Scenes){var space=new VesselSpace(ship.Instance,scene.Id,scene.Spots.First().Stand,scene.Id=="HUB-S02"?scene.Spots.Single(x=>x.Id=="HUB-S02.chart").Stand:scene.Spots.First().Stand,[],"explicit prior ship-space fixture");Success(Done(travel.RegisterShipSpaceAsync(Evidence(store,"ship_space_verified",space))));}
            var voyage=new RegionalVoyageService(store,Catalog,VoyageDefinition(),geo);var proof=Done(voyage.VerifyAndRegisterAsync());Success(proof.RouteProof);Success(proof.BerthProof);Success(Done(voyage.SailAsync("R4")));Success(Done(voyage.RecordArrivalAsync()));Done(Service.InitializeRegionAsync());
        }
        public void Walk(Point2 point){var v=View;var path=new NavigationGrid(v.Geometry[v.Actor.Scene]).FindPath(v.Actor.Position,point,true);Require(path.Length>0,"actual R4 safe path missing: "+v.Actor.Scene);Done(Service.TraverseAsync(path));}
        public void Spot(string spot)=>Walk(View.Geometry[View.Actor.Scene].Spots.Single(x=>x.Id==spot).Stand);
        public void Exit(string scene){var p=View.Geometry[View.Actor.Scene].Ports.Single(x=>x.TargetScene==scene);Walk(p.Stand);Success(Done(Service.EnterPortAsync(p.Id)));}
        public void Goto(string destination)
        {
            var v=View;var queue=new Queue<string>();queue.Enqueue(v.Actor.Scene);var previous=new Dictionary<string,string?>{{v.Actor.Scene,null}};
            while(queue.TryDequeue(out string? scene)){if(scene==destination)break;foreach(var port in v.Geometry[scene].Ports){var scoped=v.Snapshot with{Entities=v.Snapshot.Entities.Select(e=>e.Id=="actor"?e with{Json=StrictJson.Serialize(v.Actor with{Scene=scene})}:e).ToImmutableArray()};if(Service.PortIssue(scoped,port) is not null||previous.ContainsKey(port.TargetScene))continue;previous.Add(port.TargetScene,scene);queue.Enqueue(port.TargetScene);}}
            Require(previous.ContainsKey(destination),"no permitted R4 route to "+destination);var steps=new List<string>();for(string? scene=destination;previous[scene!] is{} from;scene=from)steps.Add(scene!);steps.Reverse();foreach(string scene in steps)Exit(scene);
        }
        public void Material(string id){var m=Service.Package.Materials.Single(x=>x.Id==id);Goto(m.Scene);Spot(m.Spot);Success(Done(Service.DiscoverAsync(id)));}
        public void Act(string action){var station=Service.Package.Stations.Single(x=>x.Action==action);Goto(station.Spot.Split('.')[0]);Spot(station.Spot);Success(Done(Service.ExecuteStationAsync(action)));}
        public void At(string spot){Goto(spot.Split('.')[0]);Spot(spot);}
        public void Meaning(string quest,params (string Key,string Value)[] fields)
        {var task=Service.Package.WritingTasks.Single(x=>x.Quest==quest);At(task.Spot);var old=Done(store.GetDraftAsync(task.Id));Done(Service.SaveDraftAsync(task.Id,old?.Revision??0,old?.Id,"Engineering candidate prose. Explicit interpretation below is not automatic English understanding or human language scoring."));Success(Done(Service.ConfirmMeaningAsync(quest,fields.ToImmutableDictionary(x=>x.Key,x=>x.Value),true)));}
        public GlasshavenPreview Submit(string quest){var task=Service.Package.WritingTasks.Single(x=>x.Quest==quest);At(task.Spot);var p=Done(Service.PreviewSubmissionAsync(quest));Require(p.Ready,StrictJson.Serialize(p));Success(Done(Service.ConfirmPreviewAsync(p.Token)));return p;}
        public void Battle(string id)
        {
            Goto("R4-S02");var party=new PartyService(store,Catalog);Success(Done(party.ApplyBuildAsync(Service.BattleDefinitions.ReferenceBuild("BUILD-TOWER-M"))));var weapon=Done(store.SnapshotAsync()).Items.Single(x=>x.Id=="collection:WP-05");if(weapon.OwnerKind!="equipped"||weapon.Slot!="weapon")Success(Done(party.EquipAsync(weapon.Id,"weapon")));
            At(id switch{"ENC-R4-01"=>"R4-S05.debris","ENC-R4-02"=>"R4-S05.reserve_case","ENC-R4-03"=>"R4-S06.defence",_=>"R4-S07.machine"});Success(Done(Service.BeginEncounterAsync(id)));
            for(int i=0;i<800&&Done(Service.EncounterAsync(id))!.Battle.Outcome==CombatOutcome.Active;i++){var battle=Done(Service.EncounterAsync(id))!.Battle;var p=Done(Service.PreviewEncounterActionAsync(id,BattleCandidate.Choose(new(Service.BattleDefinitions,battle))));Require(p.Action.Legal,p.Description);Success(Done(Service.ConfirmPreviewAsync(p.Token)));}Require(Done(Service.EncounterAsync(id))!.Battle.Outcome==CombatOutcome.Controlled,"actual R4 combat failed "+id);
        }
        public void Receive(){Material("R4-Q401-R-LANDING");Act("receive_sample");Material("R4-Q401-R-BRIEF");Act("accept_public_verification");}
        public void Node401(bool submit=true)
        {
            foreach(string id in new[]{"R4-Q401-R-SURVEY-A","R4-Q401-R-SURVEY-B","R4-Q401-R-VISUAL","R4-Q401-R-ACCESS"})Material(id);At("R4-S03.custodian");Success(Done(Service.RequestReportScopeAsync("7",["observation_ids","test_conditions","outcomes","issue_version","circulation_dates"],"patrol_technician","verify_public_data")));
            foreach(string id in new[]{"R4-Q401-R-MANUSCRIPT","R4-Q401-R-ISSUED","R4-Q401-R-CIRCULATION"})Material(id);At("R4-S04.circulation");Success(Done(Service.CompareReportVersionsAsync(new("GH-R7-v7","GH-R7-v7","GH-R7-v7","7",["T2","T3"],false))));At("R4-S03.table");Success(Done(Service.CompareFieldSourcesAsync(new(true,"W-1","local_field_indicator_not_trial_scale","association_to_test","location_and_history"))));
            Meaning("Q-401",("hypothesis","association_requires_controlled_test"),("field_limits","location_and_history_uncontrolled"),("report_difference","same_version_unfavourable_rows_removed"),("replacement_safety","not_yet_validated"));if(submit)Submit("Q-401");
        }
        public Guid Start(string controller){At(controller=="A"?"R4-S05.bench":"R4-S07.console");var started=Done(Service.StartExperimentAsync(controller));Success(started);return StrictJson.Deserialize<ExperimentRun>(Done(store.SnapshotAsync()).Receipts.Single(r=>r.ReceiptId==started.Receipt!.Id.Value.ToString()).ResultJson).Id;}
        public ExperimentRun RunTen(string controller){var id=Start(controller);Success(Done(Service.ObserveExperimentAsync(id,0)));Success(Done(Service.ObserveExperimentAsync(id,5)));return Done(Service.ExperimentAsync(id))!;}
        public void OldReady()
        {
            Material("R4-Q402-R-SAFETY");Act("request_old_hall_permit");Act("isolate_old_facility");Material("R4-Q402-R-RECONSTRUCT");Material("PZLAB-R-PROTOCOL");Battle("ENC-R4-01");At("R4-S05.reserve_case");foreach(string step in new[]{"open","clear","secure","inspect"})Success(Done(Service.PerformReserveStepAsync(step)));At("R4-S05.bench");Success(Done(Service.PrepareOldBenchStepAsync("secure_guard")));Success(Done(Service.PrepareOldBenchStepAsync("inspect_socket")));
        }
        public void Node402()
        {
            OldReady();At("R4-S05.bench");Success(Done(Service.ConfigureControllerAAsync(70,true)));Require(RunTen("A").Outcome=="safe_stopped");At("R4-S05.protocol");Success(Done(Service.CompareTrialPairAsync("load","T4","T2")));Material("R4-Q402-R-CALIBRATION");At("R4-S06.zero");Success(Done(Service.InspectCalibrationChannelAsync("Z")));At("R4-S06.reference");Success(Done(Service.InspectCalibrationChannelAsync("R")));
            Meaning("Q-402",("trial_scope","Controller_A_ten_minutes"),("load55","untested"),("whole_network","not_established"),("failed_result","retained_not_all_local_impossible"));Submit("Q-402");
        }
        public void IslandConfigure(int load,bool reserve=true){if(View.State.TrialOperator==""){At("R4-S06.duty");Success(Done(Service.AssignTrialDutyAsync("N-SIAN","ROLE-R4-TRIAL-REVIEW")));}if(View.State.IslandContact!="limited"){At("R4-S06.contact");Success(Done(Service.SetIslandContactAsync("limited")));}At("R4-S06.load");Success(Done(Service.ConfigureIslandAsync(load,reserve,"limited","N-SIAN","ROLE-R4-TRIAL-REVIEW")));Act("verify_island_configuration");}
        public void OutputReady(){At("R4-S07.output");foreach(string step in new[]{"isolate","select_TEST","seat_coupler","secure_guard","inspect"})Success(Done(Service.PrepareOutputStepAsync(step)));}
        public void Node403(bool submit=true)
        {
            Battle("ENC-R4-03");Material("R4-Q403-R-ISLAND");Act("inspect_island_isolation");Act("seat_island_reserve");Act("secure_island_guard");IslandConfigure(70);Material("R4-Q403-R-OUTPUT");Battle("ENC-R4-04");Require(!View.Snapshot.Entities.Any(e=>e.Id=="R4:result:ENC-R4-04"));OutputReady();Require(RunTen("C").Outcome=="safe_stopped");IslandConfigure(40);Require(RunTen("C").Outcome=="stable");Act("open_logistics_latch");
            Meaning("Q-403",("process","prepare_execute_observe_accept"),("scope","current_configuration_ten_minutes"),("battle_meaning","device_safe_not_trial_data"),("counterexamples","retained"),("load55","untested"));if(submit)Submit("Q-403");
        }
        public void Node404()
        {
            Material("R4-Q404-R-ESSAY");Material("R4-Q404-R-ACCESS");At("R4-S04.facility");Success(Done(Service.RequestFacilityScopeAsync(["facility_code","supply_destination","watch_post","version_source"],"patrol_technician","verify_holding_facility","1")));foreach(string id in new[]{"R4-Q404-R-LOCATION","R4-Q404-R-SUPPLY","R4-Q404-R-GUARD"})Material(id);At("R4-S04.facility");Success(Done(Service.VerifyNorthStationAsync("F-17","F-17","F-17","old_city_north_station","unknown")));Material("R4-Q404-L-VOSS");At("R4-S02.voss");Success(Done(Service.ExchangeVossAsync(true,true,"unknown")));Act("request_prism_permit");Material("R4-Q404-R-TOWER");Material("R4-Q404-R-PREPARATION");Act("inspect_prism_duty");At("R4-S08.selector");Success(Done(Service.SetPrismLoadAsync(new(2,1,0))));Act("inspect_prism_safety");Act("check_prism_load");Act("check_prism_return");Act("publish_preparation_list");
            Meaning("Q-404",("technical_scope","bounded_not_full_network"),("mist","not_immediately_cleared"),("independent_review","preserved"),("mara_current_status","unknown"),("location","old_city_north_station"),("preparation","public_actual_work_pending"));Submit("Q-404");
        }
    }
    public static void Register()
    {
        Add("CP-R4-01","R4 exact ten-scene source graph preserves immutable A protocol and genuine script-only materials",()=>
        {var p=Package();Require(p.Scenes.Length==10&&p.Scenes.Sum(s=>s.Geometry.Ports.Length)==22&&p.Materials.Length==29&&p.Stations.Length==42&&p.Materials.Count(m=>m.Kind=="listening_script")==4&&p.Materials.Where(m=>m.Kind=="listening_script").All(m=>m.AudioAsset==""));using var original=JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectRoot,"content/source/part06-Puzzle_Materials_v0.1.json")));var a=original.RootElement.GetProperty("materials").EnumerateArray().Single(m=>m.GetProperty("id").GetString()=="PZLAB-R-PROTOCOL");var expected=a.GetProperty("paragraphs").EnumerateArray().Select(x=>x.GetProperty("text").GetString()).ToArray();Require(p.Materials.Single(m=>m.Id=="PZLAB-R-PROTOCOL").Paragraphs.Select(x=>x.English).SequenceEqual(expected));foreach(var scene in p.Scenes){var nav=new NavigationGrid(scene.Geometry);foreach(var port in scene.Geometry.Ports)foreach(var spot in scene.Geometry.Spots)Require(nav.FindPath(port.Stand,spot.Stand,true).Length>0);}});
        Add("CP-R4-03","Bare R4 actor cannot create actual sample authority, voyage or chapter progress",()=>
        {using var s=Store("bare");Fixture(s,"fixture:bare-r4","explicit_engineering_fixture",new{scope="negative entry only"},[Entity(s,"actor","actor",new ActorState("R4-S01",new(48,90),0))]);long seq=s.Session.Sequence;Throws<InvalidOperationException>(()=>Done(new ChapterFourService(s,Catalog,Package()).InitializeRegionAsync()));Require(s.Session.Sequence==seq&&!Done(s.SnapshotAsync()).Entities.Any(e=>e.Id=="chapter4"));});
        Add("CP-R4-03","Actual sailing initialises zero new rewards; sample deposit moves the single instance once",()=>
        {using var s=Store("custody");var r=new Run(s);r.FixturePriorCommissionAndActualVoyage();Require(r.View.Materials.Length==0&&!r.View.State.SampleReceived&&r.View.Snapshot.Balances["experience"]==3840);Throws<InvalidOperationException>(()=>Done(r.Service.DiscoverAsync("R4-Q401-R-SURVEY-A")));r.Material("R4-Q401-R-LANDING");r.Act("receive_sample");r.Act("receive_sample");Require(r.View.Snapshot.Items.Count(i=>i.Id==ChapterFourService.SampleId)==1&&r.View.Snapshot.Items.Single(i=>i.Id==ChapterFourService.SampleId).OwnerId=="R4-public-verification-desk"&&r.View.Snapshot.Receipts.Count(x=>x.SemanticKey=="R4:sample_received")==1&&r.View.Snapshot.Balances["experience"]==3840);});
        Add("CP-R4-03","A comparisons accept both load repetitions and reserve comparison; unknown 55 never produces results",()=>
        {using var s=Store("comparison");var r=new Run(s);r.FixturePriorCommissionAndActualVoyage();r.Receive();r.OldReady();r.At("R4-S05.protocol");foreach(var pair in new[]{("load","T1","T2"),("load","T2","T4"),("reserve","T3","T1")})Success(Done(r.Service.CompareTrialPairAsync(pair.Item1,pair.Item2,pair.Item3)));Throws<InvalidOperationException>(()=>Done(r.Service.CompareTrialPairAsync("load","T2","T3")));r.At("R4-S05.bench");Throws<InvalidOperationException>(()=>Done(r.Service.ConfigureControllerAAsync(55,true)));Require(r.View.Runs.Length==0);});
        Add("CP-R4-03","Parallel Q402 can close before Q401; actual failed run cannot close the dependent trial report",()=>
        {using var s=Store("parallel");var r=new Run(s);r.FixturePriorCommissionAndActualVoyage();r.Receive();r.Node402();Require(r.View.Quests.Single(q=>q.Id=="Q-401").Lifecycle==QuestLifecycle.Active&&r.View.Quests.Single(q=>q.Id=="Q-403").Lifecycle==QuestLifecycle.Unaccepted&&r.View.Snapshot.Balances["experience"]==4440&&r.View.Runs.Single().Outcome=="safe_stopped");r.Node401();Require(r.View.Quests.Single(q=>q.Id=="Q-403").Lifecycle==QuestLifecycle.Active);});
        Add("CP-R4-03","Physical observation records finite five and ten minute steps; retry does not advance time twice",()=>
        {using var s=Store("observation");var r=new Run(s);r.FixturePriorCommissionAndActualVoyage();r.Receive();r.OldReady();r.At("R4-S05.bench");Success(Done(r.Service.ConfigureControllerAAsync(70,true)));var id=r.Start("A");Success(Done(r.Service.ObserveExperimentAsync(id,0)));Require(Done(r.Service.ExperimentAsync(id)) is{Minutes:5,Outcome:"running"});Success(Done(r.Service.ObserveExperimentAsync(id,0)));Require(Done(r.Service.ExperimentAsync(id))!.Minutes==5);Success(Done(r.Service.ObserveExperimentAsync(id,5)));Require(Done(r.Service.ExperimentAsync(id)) is{Minutes:10,Outcome:"safe_stopped"}&&r.View.Snapshot.Balances["experience"]==3840);});
        Add("CP-R4-03","Read-only preview and draft CAS reject stale reports without issuing facts or scope",()=>
        {using var s=Store("preview");var r=new Run(s);r.FixturePriorCommissionAndActualVoyage();r.Receive();r.Node401(false);var p=Done(r.Service.PreviewSubmissionAsync("Q-401"));Require(p.Ready&&r.View.Snapshot.Balances["experience"]==3840);var d=Done(s.GetDraftAsync("R4-Q401-W"))!;Throws<StoreConflictException>(()=>Done(r.Service.SaveDraftAsync("R4-Q401-W",0,d.Id,"stale")));Done(r.Service.SaveDraftAsync("R4-Q401-W",d.Revision,d.Id,"changed prose"));Require(Done(r.Service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.Rejected&&r.View.Snapshot.Balances["experience"]==3840);r.Node401(false);var next=Done(r.Service.PreviewSubmissionAsync("Q-401"));Require(next.Ready);r.Service.ClosePreview(next.Token);Require(Done(r.Service.ConfirmPreviewAsync(next.Token)).Status==CommitStatus.Rejected);});
        Add("CP-R4-03","Actual C counterexample and stable run persist; local reset invalidates current proof and keeps safe exit",()=>
        {using var s=Store("reset");var r=new Run(s);r.FixturePriorCommissionAndActualVoyage();r.Receive();r.Node401();r.Node402();r.Node403(false);Require(r.View.Runs.Any(x=>x.Configuration.Controller=="A"&&x.Outcome=="safe_stopped")&&r.View.Runs.Any(x=>x.Configuration.Controller=="C"&&x.Outcome=="safe_stopped")&&r.View.Runs.Any(x=>x.Configuration.Controller=="C"&&x.Outcome=="stable"));var ready=Done(r.Service.PreviewSubmissionAsync("Q-403"));Require(ready.Ready);r.At("R4-S07.console");Success(Done(r.Service.ResetExperimentAsync("C")));Require(!Done(r.Service.PreviewSubmissionAsync("Q-403")).Ready&&r.View.Runs.Length==3&&r.View.State.LogisticsLatch&&Done(r.Service.ConfirmPreviewAsync(ready.Token)).Status==CommitStatus.Rejected);r.Exit("R4-S06");r.IslandConfigure(40);r.At("R4-S07.output");Success(Done(r.Service.PrepareOutputStepAsync("inspect")));Require(r.RunTen("C").Outcome=="stable");r.Meaning("Q-403",("process","prepare_execute_observe_accept"),("scope","current_configuration_ten_minutes"),("battle_meaning","device_safe_not_trial_data"),("counterexamples","retained"),("load55","untested"));r.Submit("Q-403");});
        Add("CP-R4-03","Main tasks transfer actual sample, bounded technical findings and exact rewards once; Safe is separate from conversion",()=>
        {using var s=Store("complete");var r=new Run(s);r.FixturePriorCommissionAndActualVoyage();long marks=r.View.Snapshot.Balances.GetValueOrDefault("marks"),parts=r.View.Snapshot.Balances.GetValueOrDefault("parts");r.Receive();r.Node401();r.Node402();r.Node403();r.Node404();var v=r.View;var t=Read<TowerReadiness>(s,"readiness:TWR-R4");Require(v.Snapshot.Balances["experience"]==6400&&v.Snapshot.Balances["marks"]-marks==640&&v.Snapshot.Balances["parts"]-parts==9&&t.Safe&&!t.Baseline&&!t.Trial&&!t.Operators&&!t.Transition&&t.EnhancedVersion is null&&ChapterFourService.MainQuests.All(q=>v.Snapshot.Receipts.Count(x=>x.SemanticKey==q+":resolution")==1));Require(Read<TechnicalHandover>(s,"R4:actual_handover").MaraCurrentStatus=="unknown"&&!v.Snapshot.Entities.Any(e=>e.Id=="finding:K-MARA-DESIGN-LIMIT"||e.Kind=="engineering_stock"));Done(s.ContinueAsync());Require(r.View.Runs.Count(x=>x.Outcome=="safe_stopped")==2&&r.View.Snapshot.Balances["experience"]==6400);});
        Add("CP-R4-03","CQ-MIRA02 requires actual prior limited handoff, private acceptance and derived delivery, without recruitment",()=>
        {using var s=Store("mira");var detail=Mist.Tests.ScopedPersonalWorks.ScopedPersonalWorkCases.MainComplete(s);detail.Mira01();detail.Mira02(false);var r=new Run(s,detail.Four);r.At("R4-S04.w04_copy");Throws<InvalidOperationException>(()=>Done(r.Service.GenerateReviewCopyAsync("R4-review-desk","verify_role_counts","W04-review-v1",["role_counts","source_version","source_root","identity_key"])));r.Meaning("CQ-MIRA-02",("fields","role_counts_and_source_only"),("recipient","R4-review-desk"),("purpose","verify_role_counts"),("identity_publication","not_permitted"),("copy_independence","same_source"));var p=r.Submit("CQ-MIRA-02");Require(Done(r.Service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.AlreadyCommitted&&r.View.Snapshot.Balances["experience"]==10560&&Read<DerivedReviewCopy>(s,"R4:mira_review_copy") is{IdentitiesIncluded:false,Observers:2,Service:1}&&r.View.Snapshot.Receipts.Count(x=>x.SemanticKey=="CQ-MIRA-02:resolution")==1&&r.View.Snapshot.Receipts.All(x=>!x.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal)));});
        Add("CP-R4-05","Normal zero-XP native-entry profile reaches R4 by actual source-bound voyage without regional actions",()=>
        {using var s=Store("native-entry");var pair=ActualArrival(s);var v=Done(pair.Service.ViewAsync());Require(v.Snapshot.Balances["experience"]==3840&&v.Actor.Scene=="R4-S01"&&v.Materials.Length==0&&v.Runs.Length==0&&!v.State.SampleReceived&&!v.State.PublicAssignment&&v.Quests.Where(q=>q.Id is "Q-401" or "Q-402").All(q=>q.Lifecycle==QuestLifecycle.Active)&&v.Snapshot.Items.Single(i=>i.Id==ChapterFourService.SampleId).OwnerKind=="player"&&v.Snapshot.Receipts.All(r=>!r.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal)&&r.ActionType is not("explicit_engineering_fixture" or "internal_slice_initialization")));Done(s.CreateCheckpointAsync("manual"));File.WriteAllText(Path.Combine(Path.GetDirectoryName(s.MainPath)!,"normal-r4-native-entry.json"),StrictJson.Serialize(new{scope="actual zero-XP through Q304 and actual R3R4 board/chart/sail; R4 only initialized, no local source/device/sample/reward actions",experience=3840,profile_path=s.MainPath,actor=v.Actor,receipts=v.Snapshot.Receipts,native_human="pending",language_review="human_pending"}));});
        Add("CP-R4-03","Continuous normal zero-XP voyage reaches all R4 tasks with no positive fixtures or inferred current mentor status",()=>
        {using var s=Store("normal-complete");NormalComplete(s);var final=Done(s.SnapshotAsync());var preparation=Done(new Mist.Application.ReleaseReadiness.FinalReadinessService(s,Catalog).ViewAsync());Require(preparation.PublicPreparationVisible&&preparation.Options is not null&&!preparation.Options.BasePrepared&&preparation.DepartureMissing.Contains("actual_closed_Q602")&&!preparation.DepartureMissing.Contains("actual_glimmer_use_transfer"),"Actual typed Q404 must publish preparation without inventing six completed towers.");Require(final.Balances["experience"]==6400&&final.Receipts.All(x=>!x.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal)&&x.ActionType is not("explicit_engineering_fixture" or "internal_slice_initialization"))&&final.Items.Count(i=>i.Id==ChapterFourService.SampleId)==1&&final.Entities.Count(e=>e.Kind=="shared_vessel")==1);File.WriteAllText(Path.Combine(Path.GetDirectoryName(s.MainPath)!,"normal-r4-main.json"),StrictJson.Serialize(new{scope="actual zero-XP through preceding chapters, source-bound voyage and four R4 reports",experience=6400,receipts=final.Receipts,audio="not_recorded",native_human="pending",language_review="pending"}));});
    }
}

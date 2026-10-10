using System.Collections.Immutable;
using System.Text.Json;
using Mist.Application.ChapterOne;
using Mist.Application.Contracts;
using Mist.Application.Inventory;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Core.Campaign;
using Mist.Core.ChapterOne;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Engineering;
using Mist.Core.Exploration;
using Mist.Core.Progression;
using Mist.DebugCandidates;
using Mist.Infrastructure.Storage;
using static Mist.Tests.NPC.NpcCases;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.ChapterOne;

public static class ChapterOneCases
{
    private static string ProjectRoot=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    private static readonly Mist.Core.Content.RuntimeCatalog Catalog=Q102.Q102Cases.Catalog();
    public static ChapterOnePackage Package()=>StrictJson.Deserialize<ChapterOnePackage>(File.ReadAllText(Path.Combine(ProjectRoot,"content/source/chapter_one/chapter_one.json")));
    private static ImmutableDictionary<string,SceneGeometry> Shared()=>StrictJson.Deserialize<CampaignPackage>(File.ReadAllText(Path.Combine(ProjectRoot,"content/source/campaign/first_entry.json"))).Scenes.ToImmutableDictionary(s=>s.Id);
    private static ProfileStore Store(string name)=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"chapter-one-"+name+"-"+Guid.NewGuid()),Catalog.ReleaseId);
    private static void Success(CommandResult r)=>Require(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(r));
    private static T Read<T>(ProfileStore s,string id)=>StrictJson.Deserialize<T>(Done(s.SnapshotAsync()).Entities.Single(e=>e.Id==id).Json);
    private static readonly AccidentFindingMeaning Correct=new("approved_preplanned_test","within_recorded_clock_uncertainty","second_check_removed_before_test","insufficient_on_its_own","specific_higher_intent_not_established");
    private sealed class Run(ProfileStore store)
    {
        public ChapterOneService Service {get;}=new(store,Catalog,Package(),Shared(),id=>store.CreateCheckpointAsync("protected",protectedId:id));
        public void Walk(Point2 point)
        {
            var actor=Read<ActorState>(store,"actor");var path=new NavigationGrid(Service.Geometry[actor.Scene]).FindPath(actor.Position,point,actor.Scene=="R1-S06"&&!Read<WorkshopState>(store,"workshop").FrontSafe);Require(path.Length>0,"actual path required");Done(Service.MoveAsync(path));
        }
        public void Spot(string spot){var actor=Read<ActorState>(store,"actor");Walk(Service.Geometry[actor.Scene].Spots.Single(p=>p.Id==spot).Stand);}
        public void Exit(string scene){var actor=Read<ActorState>(store,"actor");var port=Service.Geometry[actor.Scene].Ports.Single(p=>p.TargetScene==scene);Walk(port.Stand);Success(Done(Service.EnterPortAsync(port.Id)));}
        public void Material(string id){var m=Service.Package.Materials.Single(m=>m.Id==id);Spot(m.Spot);Success(Done(Service.CollectAsync(id)));}
        public void Save(string body)
        {
            var head=Done(store.GetDraftAsync(ChapterOneService.FindingDraft));var s=store.Session;Done(store.SaveAsync(new(s.Profile,s.Branch,s.Load,ChapterOneService.FindingDraft,head?.Id,head?.Revision??0,body,"chapter-one-candidate-1")));
        }
        public void Finding(bool resolve=true)
        {
            var q=new Q102RouteCandidate(store,Catalog);Done(q.Complete(WorkshopMode.Isolated));
            // The Q103/Q104 subsystem test begins at engineering fixtures for prior prologue/Q101 evidence.
            // Only the Q102 traversal and this extension are played; this does not claim normal new-game E2E.
            Fixture(store,"Q-101:resolution","explicit_engineering_fixture",new{scope="prior harbour commission fixture"});Fixture(store,"material:QP03-R-RECORDER:acquired","collect_coast_record",new{scope="explicit prior prologue record fixture",source_root="coast-independent-recorder",current_mara_status="unknown"},[Entity(store,"material:QP03-R-RECORDER","material_access",new{scope="prior record fixture"})]);
            AcquireAndSubmitFinding(resolve);
        }
        public void FromNormalFinding(bool resolve=true)
        {
            var s=Done(store.SnapshotAsync());
            Require(Read<CampaignState>(store,"campaign") is{Scope:CampaignRules.Scope,ProloguePlayed:true,Q104Closed:false,GlimmerOwned:false});
            Require(s.Receipts.Any(r=>r.SemanticKey=="Q-101:resolution")&&s.Receipts.Any(r=>r.SemanticKey=="Q-102:resolution")&&s.Receipts.All(r=>!r.SemanticKey.StartsWith("internal:",StringComparison.Ordinal)&&r.ActionType!="explicit_engineering_fixture"));
            var original=s.Receipts.Single(r=>r.SemanticKey=="material:QP03-R-RECORDER:acquired");
            Require(original.ActionType=="collect_coast_record"&&StrictJson.Deserialize<JsonElement>(original.ResultJson).GetProperty("source_root").GetString()=="coast-independent-recorder");
            AcquireAndSubmitFinding(resolve);
        }
        private void AcquireAndSubmitFinding(bool resolve)
        {
            Exit("R1-S03");Spot("R1-S03.havel");Success(Done(Service.AcceptInvestigationAsync()));Exit("R1-S08");foreach(string id in ChapterOneRules.Evidence)Material(id);Exit("R1-S03");Spot("R1-S03.havel");Save("The schedule preceded the interruption. The control and coast labels fit the recorded clock bounds, and the second traffic check was removed. The prepared notice alone does not establish intent. Retain the originals and investigate the issuing authority separately.");Success(Done(Service.ConfirmFindingMeaningAsync(Correct,false)));if(resolve){var preview=Done(Service.PreviewFindingAsync());Require(preview.Ready,string.Join(";",preview.Issues));Success(Done(Service.ConfirmPreviewAsync(preview.Token)));}
        }
        public void Tower(string build="H")
        {
            var party=new PartyService(store,Catalog);Success(Done(party.ApplyBuildAsync(Service.BattleDefinitions.ReferenceBuild("BUILD-TOWER-"+build))));
            string weapon=build=="M"?"WP-05":build=="S"?"WP-03":"WP-01";var item=Done(store.SnapshotAsync()).Items.Single(i=>i.Id=="collection:"+weapon);
            if(item.OwnerKind!="equipped"||item.OwnerId!="hero"||item.Slot!="weapon")Success(Done(party.EquipAsync(item.Id,"weapon")));
            Require(Done(party.InspectAsync()).Weapon==weapon,"Tower uses the actual selected weapon instance.");
            Exit("R1-S02");Exit("R1-S09");Spot("R1-S09.warden");Success(Done(Service.AcceptTowerWorkAsync()));Material("Q104-R-CONTROLS");Material("Q104-R-HANDOVER");Spot("R1-S09.supply_indicator");Success(Done(Service.InspectLocalSupplyAsync()));Exit("R1-S10");Spot("R1-S10.encounter_entry");Success(Done(Service.BeginTowerAsync()));
        }
        public void CompleteTower(bool rightFirst=false)
        {
            for(int i=0;i<600&&Done(Service.TowerAsync())!.Battle.Outcome==CombatOutcome.Active;i++)
            {
                var battle=Done(Service.TowerAsync())!.Battle;var request=BattleCandidate.Choose(new(Service.BattleDefinitions,battle),rightFirst);var preview=Done(Service.PreviewTowerActionAsync(request));Require(preview.Action.Legal,preview.Description);Success(Done(Service.ConfirmPreviewAsync(preview.Token)));
            }
            Require(Done(Service.TowerAsync())!.Battle.Outcome==CombatOutcome.Controlled,"actual tower controlled required");
        }
        public void InspectAndReturn(){Spot("R1-S10.safety_console");Success(Done(Service.VerifyTowerAfterControlAsync()));Exit("R1-S09");Exit("R1-S02");Exit("R1-S01");Spot("R1-S01.glimmer_handover");}
    }
    public static void Register()
    {
        Add("CX-065","Normal zero-XP rescue, commission, ferry, finding and tower handover persist continuously without fixtures",()=>
        {
            // A fresh SQLite profile is driven only by actual source acquisition, navigation and service commands.
            // Prepared test prose and explicit meaning are engineering inputs, not human/native or English assessment.
            using var s=Store("normal-first-chapter");Require(!Done(s.SnapshotAsync()).Entities.Any(e=>e.Id=="actor"));
            var coast=new Campaign.CampaignCases.Run(s);coast.Initialize();var initial=Done(s.SnapshotAsync());
            Require(initial.Balances.GetValueOrDefault("experience")==0&&Read<ActorState>(s,"actor").Scene=="R0-S01"&&!initial.Entities.Any(e=>e.Id=="permission:Q-102"));
            coast.ThroughCommission();var commission=Done(s.SnapshotAsync());var party=new PartyService(s,Catalog);var ferryEntry=Done(party.InspectAsync());
            Require(commission.Balances["experience"]==340&&ferryEntry.Level==3&&ferryEntry.Grade==0&&coast.View.Q102Ready&&CampaignRules.Quests.All(id=>commission.Receipts.Count(r=>r.SemanticKey==id+":resolution")==1));
            Require(commission.Entities.Single(e=>e.Id=="permission:Q-102").Json.Contains("Q-101:resolution",StringComparison.Ordinal));
            var recorder=commission.Entities.Single(e=>e.Id=="material:QP03-R-RECORDER");var recorderReceipt=commission.Receipts.Single(r=>r.SemanticKey=="material:QP03-R-RECORDER:acquired");
            Require(recorderReceipt.ActionType=="collect_coast_record");
            var run=new Run(s);run.Exit("R1-S02");var q=new Q102RouteCandidate(s,Catalog);Done(q.Prepare(WorkshopMode.Isolated));
            Require(Done(s.SnapshotAsync()).Balances["experience"]==340&&Done(s.SnapshotAsync()).Receipts.All(r=>!r.SemanticKey.StartsWith("internal:",StringComparison.Ordinal)));
            var departure=Done(q.Service.PreviewDepartureAsync());Require(departure.Preview.Status==Mist.Application.Commands.PreviewStatus.Ready);Success(Done(q.Service.DepartAsync(departure.Token)));
            var ferry=Done(q.Service.ViewAsync());Require(ferry.Snapshot.Balances["experience"]==500&&ferry.Actor.Scene=="R1-S02"&&!ferry.Ferry.Docked&&ferry.Snapshot.Items.Count(i=>i.Id==Mist.Application.Q102.Q102Service.BracketId)==1&&ferry.Snapshot.Items.Count(i=>i.Id==Mist.Application.Q102.Q102Service.BatteryId)==1);
            Require(ferry.Snapshot.Items.Where(i=>i.Id is Mist.Application.Q102.Q102Service.BracketId or Mist.Application.Q102.Q102Service.BatteryId).All(i=>i.OwnerKind=="vessel"&&i.OwnerId=="CIVIL-FERRY"));
            run.FromNormalFinding(false);var finding=Done(run.Service.PreviewFindingAsync());Require(finding.Ready,string.Join(";",finding.Issues));Success(Done(run.Service.ConfirmPreviewAsync(finding.Token)));Require(Done(run.Service.ConfirmPreviewAsync(finding.Token)).Status==CommitStatus.AlreadyCommitted);
            var investigation=Done(s.SnapshotAsync());Require(investigation.Balances["experience"]==680&&ProgressionRules.Level(680)==5&&ProgressionRules.Grade(investigation.Receipts.Select(r=>r.SemanticKey))==0);
            run.Tower("M");var towerEntry=Done(run.Service.TowerAsync())!;var entryReceipt=Done(s.SnapshotAsync()).Receipts.Single(r=>r.ActionType=="begin_tidegate_encounter");var entry=StrictJson.Deserialize<JsonElement>(entryReceipt.ResultJson);
            Require(entry.GetProperty("entry_level").GetInt32()==5&&entry.GetProperty("entry_grade").GetInt32()==0&&towerEntry.Battle.Units.Where(u=>u.Side==CombatSide.Ally).All(u=>u.Level==5)&&towerEntry.Battle.Units.Single(u=>u.Id=="hero").Armor=="AR-01");
            run.CompleteTower();var controlled=Done(s.SnapshotAsync());Require(controlled.Balances["experience"]==680&&!controlled.Entities.Any(e=>e.Id=="ship:glimmer")&&!controlled.Items.Any(i=>i.DefinitionId is "AR-02" or "AR-03" or "RE-02" or "TL-01"));
            run.InspectAndReturn();var handover=Done(run.Service.PreviewHandoverAsync());Require(handover.Ready,string.Join(";",handover.Issues));Success(Done(run.Service.ConfirmPreviewAsync(handover.Token)));Require(Done(run.Service.ConfirmPreviewAsync(handover.Token)).Status==CommitStatus.AlreadyCommitted);
            var final=Done(s.SnapshotAsync());var ship=Read<SharedVessel>(s,"ship:glimmer");
            Require(final.Balances["experience"]==900&&ProgressionRules.Grade(final.Receipts.Select(r=>r.SemanticKey))==1&&Read<CampaignState>(s,"campaign") is{Scope:CampaignRules.Scope,ProloguePlayed:true,Q104Closed:true,GlimmerOwned:true});
            Require(final.Entities.Count(e=>e.Kind=="shared_vessel")==1&&ship.Instance!=Guid.Empty&&ship.WatchOfficer=="N-DECKWATCH"&&ship.Spaces.SequenceEqual(new[]{"HUB-S01","HUB-S02","HUB-S03"}));
            Require(new[]{"AR-02","AR-03","RE-02","TL-01"}.All(id=>final.Items.Count(i=>i.DefinitionId==id&&i.Id=="Q104:original:"+id)==1)&&final.Items.Count(i=>i.Id.StartsWith("Q104:original:",StringComparison.Ordinal))==4);
            Require(CampaignRules.Quests.Concat(new[]{"Q-102","Q-103","Q-104"}).All(id=>final.Receipts.Count(r=>r.SemanticKey==id+":resolution")==1)&&final.Receipts.Count(r=>r.ActionType=="normal_campaign_created")==1&&final.Receipts.Count(r=>r.ActionType=="tower_safety_verified")==1);
            Require(final.Receipts.All(r=>!r.SemanticKey.StartsWith("internal:",StringComparison.Ordinal)&&r.ActionType is not("explicit_engineering_fixture" or "internal_slice_initialization")&&!r.ResultJson.Contains("fixture",StringComparison.OrdinalIgnoreCase)));
            Require(final.Entities.Single(e=>e.Id==recorder.Id)==recorder&&final.Receipts.Single(r=>r.SemanticKey==recorderReceipt.SemanticKey)==recorderReceipt&&coast.Service.Package.Materials.All(m=>final.Entities.Any(e=>e.Id=="material:"+m.Id)));
            Done(s.ContinueAsync());Require(Done(s.SnapshotAsync()).Balances["experience"]==900&&Read<SharedVessel>(s,"ship:glimmer").Instance==ship.Instance&&Read<TowerReadiness>(s,"readiness:TWR-R1").Safe&&Done(run.Service.TowerAsync())!.Battle.Instance==towerEntry.Battle.Instance);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(s.MainPath)!,"normal-first-chapter-e2e.json"),StrictJson.Serialize(new{scope="actual command navigation and persistent SQLite normal-new-game E2E; no preloaded fixtures",experience=new[]{0,340,500,680,900},ferry_entry_level=ferryEntry.Level,ferry_entry_grade=ferryEntry.Grade,tower_entry_level=5,tower_entry_grade=0,coast_actions=coast.Steps,ferry_actions=q.Steps,receipts=final.Receipts,actor=Read<ActorState>(s,"actor"),ship,original_reward_items=final.Items.Where(i=>i.Id.StartsWith("Q104:original:",StringComparison.Ordinal)).ToArray(),human_native_validation="pending",language_6_5_validation="pending"}));
        });
        Add("CP-R1-03","Three new grayboxes have paired exits and safe paths to every actual source/work station",()=>
        {
            var p=Package();ChapterOneRules.Validate(p);using var s=Store("geometry");var run=new Run(s);foreach(var scene in p.Scenes){var nav=new NavigationGrid(scene);foreach(var port in scene.Ports){var pair=run.Service.Geometry[port.TargetScene].Ports.Single(x=>x.Id==port.TargetPort);Require(pair.TargetScene==scene.Id&&pair.TargetPort==port.Id&&new NavigationGrid(run.Service.Geometry[port.TargetScene]).CanStand(pair.Stand));foreach(var spot in scene.Spots)Require(nav.FindPath(port.Stand,spot.Stand).Length>0);}}Require(p.Materials.Length==6&&p.Materials.All(m=>m.ReviewStatus=="original_unreviewed"&&m.CalibrationStatus=="human_pending"&&m.Modality=="reading"));
        });
        Add("CX-065","Q103 preserves original versions and uncertainty, then awards 180XP once without hidden knowledge",()=>
        {
            using var s=Store("finding");var run=new Run(s);run.Finding(false);var before=Done(s.SnapshotAsync());Require(before.Balances["experience"]==500&&ProgressionRules.Level(500)==4);var p=Done(run.Service.PreviewFindingAsync());Require(p.Ready&&Done(s.SnapshotAsync()).Sequence==before.Sequence);Success(Done(run.Service.ConfirmPreviewAsync(p.Token)));Require(Done(run.Service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.AlreadyCommitted);var after=Done(s.SnapshotAsync());Require(after.Balances["experience"]==680&&ProgressionRules.Level(680)==5&&ProgressionRules.Grade(after.Receipts.Select(r=>r.SemanticKey))==0);Require(ChapterOneRules.Evidence.All(id=>after.Entities.Any(e=>e.Id=="material:"+id))&&after.Entities.Any(e=>e.Id=="material:QP03-R-RECORDER"));Require(!after.Entities.Any(e=>e.Id=="ship:glimmer"||e.Id=="knowledge:N-MARA"));var f=Read<JsonElement>(s,"finding:E-01");Require(f.GetProperty("higher_intent").GetString()=="not_established"&&f.GetProperty("current_mara_status").GetString()=="unknown");
        });
        Add("CX-065","Q103 preview cannot execute after draft edit or revoked records authority",()=>
        {
            using var s=Store("finding-stale");var run=new Run(s);run.Finding(false);var first=Done(run.Service.PreviewFindingAsync());run.Save("The finding has changed and requires another explicit interpretation.");Require(Done(run.Service.ConfirmPreviewAsync(first.Token)).Status==CommitStatus.Rejected&&!Done(s.SnapshotAsync()).Receipts.Any(r=>r.SemanticKey=="Q-103:resolution"));Success(Done(run.Service.ConfirmFindingMeaningAsync(Correct,false)));var second=Done(run.Service.PreviewFindingAsync());Fixture(s,"fixture:authority-revoked","explicit_engineering_fixture",new{},[Entity(s,"permission:Q-103","permission",new{allowed=false})]);Require(Done(run.Service.ConfirmPreviewAsync(second.Token)).Status==CommitStatus.Rejected&&!Done(run.Service.PreviewFindingAsync()).Ready);
        });
        Add("CX-065","A prepared notice alone and an unsupported higher-intent interpretation never close Q103",()=>
        {
            using var s=Store("finding-limits");var q=new Q102RouteCandidate(s,Catalog);Done(q.Complete(WorkshopMode.Isolated));Fixture(s,"Q-101:resolution","explicit_engineering_fixture",new{});var run=new Run(s);run.Exit("R1-S03");run.Spot("R1-S03.havel");Success(Done(run.Service.AcceptInvestigationAsync()));run.Exit("R1-S08");run.Material("Q103-R-NOTICE");run.Exit("R1-S03");run.Spot("R1-S03.havel");Success(Done(run.Service.ConfirmFindingMeaningAsync(Correct,true)));Require(!Done(run.Service.PreviewFindingAsync()).Ready);run.Exit("R1-S08");run.Spot("R1-S08.recorder_copy");Throws<InvalidOperationException>(()=>Done(run.Service.CollectAsync("Q103-R-CLOCK")));Require(!ChapterOneRules.Admissible(Correct with{ResponsibilityLimit="all_officers_deliberately_caused_the_accident"})&&Done(s.SnapshotAsync()).Balances["experience"]==500);
        });
        Add("CX-049","Tower entry requires actual inspection/configuration and does not receive its result gear early",()=>
        {
            using var s=Store("tower-entry");var run=new Run(s);run.Finding();run.Exit("R1-S02");run.Exit("R1-S09");run.Spot("R1-S09.warden");Success(Done(run.Service.AcceptTowerWorkAsync()));var port=run.Service.Geometry["R1-S09"].Ports.Single(p=>p.TargetScene=="R1-S10");run.Walk(port.Stand);Throws<InvalidOperationException>(()=>Done(run.Service.EnterPortAsync(port.Id)));run.Material("Q104-R-CONTROLS");run.Spot("R1-S09.supply_indicator");Success(Done(run.Service.InspectLocalSupplyAsync()));run.Exit("R1-S10");run.Spot("R1-S10.encounter_entry");Success(Done(run.Service.BeginTowerAsync()));var battle=Done(run.Service.TowerAsync())!.Battle;Require(battle.Units.Where(u=>u.Side==CombatSide.Ally).All(u=>u.Level==5)&&battle.Units.Single(u=>u.Id=="hero").Armor=="AR-01"&&battle.Units.Single(u=>u.Id=="tower").MaxHp==310);var snap=Done(s.SnapshotAsync());Require(!snap.Items.Any(i=>i.DefinitionId is "AR-02" or "AR-03" or "RE-02" or "TL-01")&&!snap.Entities.Any(e=>e.Id=="ship:glimmer")&&snap.Balances["experience"]==680);Throws<InvalidOperationException>(()=>Done(run.Service.BeginTowerAsync()));
        });
        foreach(string build in new[]{"H","M","S"})foreach(bool rightFirst in new[]{false,true})Add("CX-049","Real SQLite L5 grade0 tower controls and resolution: "+build+" / right-first="+rightFirst,()=>
        {
            using var s=Store("tower-"+build+"-"+rightFirst);var run=new Run(s);run.Finding();run.Tower(build);var entry=Done(run.Service.TowerAsync())!;run.CompleteTower(rightFirst);var after=Done(s.SnapshotAsync());var battle=Done(run.Service.TowerAsync())!.Battle;Require(battle.Controls.Values.All(v=>v)&&battle.Units.Where(u=>u.Side==CombatSide.Enemy).All(u=>!u.Alive));Require(Read<TowerReadiness>(s,"readiness:TWR-R1") is{Safe:true,Baseline:false,Trial:false}&&after.Balances["experience"]==680&&!after.Entities.Any(e=>e.Id=="ship:glimmer")&&!after.Receipts.Any(r=>r.SemanticKey=="Q-104:resolution"));Require(after.Receipts.Count(r=>r.ActionType=="tower_safety_verified")==1);Done(s.ContinueAsync());Require(Done(run.Service.TowerAsync())!.Battle.Instance==entry.Battle.Instance&&Read<TowerReadiness>(s,"readiness:TWR-R1").Safe);File.WriteAllText(Path.Combine(Path.GetDirectoryName(s.MainPath)!,"actual_tower_result.json"),StrictJson.Serialize(new{scope="actual subsystem SQLite commands; prior prologue fixture, no player balance claim",build,rightFirst,entry_level=5,entry_grade=0,battle,human_balance_validation="pending"}));
        });
        Add("CX-065","Post-control inspection and physical berth handover create one Glimmer and one original reward set",()=>
        {
            using var s=Store("handover");var run=new Run(s);run.Finding();run.Tower("M");run.CompleteTower();Require(!Done(run.Service.PreviewHandoverAsync()).Ready);run.InspectAndReturn();var p=Done(run.Service.PreviewHandoverAsync());Require(p.Ready,string.Join(";",p.Issues));Success(Done(run.Service.ConfirmPreviewAsync(p.Token)));Require(Done(run.Service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.AlreadyCommitted);var snap=Done(s.SnapshotAsync());var ship=Read<SharedVessel>(s,"ship:glimmer");Require(ship.Instance!=Guid.Empty&&ship.DockRegion=="R1"&&ship.WatchOfficer=="N-DECKWATCH"&&ship.Spaces.SequenceEqual(new[]{"HUB-S01","HUB-S02","HUB-S03"}));Require(snap.Balances["experience"]==900&&ProgressionRules.Grade(snap.Receipts.Select(r=>r.SemanticKey))==1&&snap.Items.Count(i=>i.Id.StartsWith("Q104:original:",StringComparison.Ordinal))==4);Require(snap.Items.Single(i=>i.Id==Mist.Application.Q102.Q102Service.BracketId).OwnerId=="CIVIL-FERRY"&&Read<ActorState>(s,"actor").Scene=="R1-S01");Require(!Done(run.Service.PreviewHandoverAsync()).Ready);var roster=new Mist.Application.Roster.RosterService(s,Catalog);Success(Done(roster.InviteAsync("CP-LINA")));Done(s.ContinueAsync());Require(Read<SharedVessel>(s,"ship:glimmer").Instance==ship.Instance&&Read<TowerReadiness>(s,"readiness:TWR-R1").Safe);
        });
        Add("CX-049","Continue does not refill; explicit withdrawal preserves acquired records/draft and new retry attempt",()=>
        {
            using var s=Store("retry");var run=new Run(s);run.Finding();run.Tower("M");var before=Done(run.Service.TowerAsync())!;var engine=new BattleEngine(run.Service.BattleDefinitions,before.Battle);var request=BattleCandidate.Choose(engine);var preview=Done(run.Service.PreviewTowerActionAsync(request));Success(Done(run.Service.ConfirmPreviewAsync(preview.Token)));var current=StrictJson.Serialize(Done(run.Service.TowerAsync())!.Battle);Done(s.ContinueAsync());Require(StrictJson.Serialize(Done(run.Service.TowerAsync())!.Battle)==current);var draft=Done(s.GetDraftAsync(ChapterOneService.FindingDraft))!;Success(Done(run.Service.RetryTowerAsync()));var retry=Done(run.Service.TowerAsync())!;Require(retry.Battle.Instance==before.Battle.Instance&&retry.Battle.Attempt!=before.Battle.Attempt&&retry.Battle.Round==1&&ChapterOneRules.Evidence.All(id=>Done(s.SnapshotAsync()).Entities.Any(e=>e.Id=="material:"+id))&&Done(s.GetDraftAsync(ChapterOneService.FindingDraft))!.Hash==draft.Hash);Require(Done(s.SnapshotAsync()).Balances["experience"]==680);
        });
    }
}

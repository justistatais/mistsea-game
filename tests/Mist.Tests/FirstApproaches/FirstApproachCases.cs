using System.Collections.Immutable;
using Mist.Application.Campaign;
using Mist.Application.ChapterOne;
using Mist.Application.Contracts;
using Mist.Application.FirstApproaches;
using Mist.Application.Inventory;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.FirstApproaches;
using Mist.Core.Quests;
using Mist.DebugCandidates;
using Mist.Infrastructure.Storage;
using static Mist.Tests.NPC.NpcCases;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.FirstApproaches;

public static class FirstApproachCases
{
    private static readonly Mist.Core.Content.RuntimeCatalog Catalog=Q102.Q102Cases.Catalog();
    private static string Root=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    public static FirstApproachPackage Package()=>FirstApproachPackage.Parse(File.ReadAllText(Path.Combine(Root,"content/source/first_approaches/learner_package.json")));
    private static ProfileStore Store()=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"first-approaches-"+Guid.NewGuid()),Catalog.ReleaseId);
    private static T Read<T>(ProfileStore s,string id)=>StrictJson.Deserialize<T>(Done(s.SnapshotAsync()).Entities.Single(e=>e.Id==id).Json);
    private static void Success(CommandResult result)=>Require(result.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(result));
    public static FirstApproachService RelayService(ProfileStore s)=>new(s,s,Catalog,Package(),Campaign.CampaignCases.Package().Scenes.ToImmutableDictionary(g=>g.Id));
    public static FirstApproachService TowerService(ProfileStore s,ChapterOneService chapter)=>new(s,s,Catalog,Package(),chapter.Geometry);
    public static void Control(FirstApproachService service,string id)
    {
        for(int count=0;count<600&&Done(service.EncounterAsync(id))!.Battle.Outcome==CombatOutcome.Active;count++)
        {var battle=Done(service.EncounterAsync(id))!.Battle;var p=Done(service.PreviewActionAsync(id,BattleCandidate.Choose(new(service.BattleDefinitions,battle))));Require(p.Action.Legal,p.Description);Success(Done(service.ConfirmAsync(p.Token)));}
        Require(Done(service.EncounterAsync(id))is{ResultApplied:true}&&Done(service.EncounterAsync(id))!.Battle.Outcome==CombatOutcome.Controlled,"Real approach combat must control all threats.");
    }
    // Reusable engineering controller: these call actual production services and never fixture success.
    public static FirstApproachService CompleteRelay(ProfileStore s,CampaignService? coast=null)
    {
        coast??=new CampaignService(s,Catalog,Campaign.CampaignCases.Package());var service=RelayService(s);var a=Read<ActorState>(s,"actor");var nav=new NavigationGrid(service.Geometry[a.Scene]);var material=service.Package.Materials.Single(m=>m.Id=="R0-APP-R-RELAY");Done(coast.TraverseAsync(nav.FindPath(a.Position,service.Geometry[a.Scene].Spots.Single(p=>p.Id==material.Spot).Stand,true)));Success(Done(service.DiscoverAsync(material.Id)));
        a=Read<ActorState>(s,"actor");Done(coast.TraverseAsync(nav.FindPath(a.Position,service.Geometry[a.Scene].Spots.Single(p=>p.Id=="R0-S05.relay").Stand,true)));Success(Done(service.BeginAsync(FirstApproachRules.Relay)));Control(service,FirstApproachRules.Relay);return service;
    }
    public static FirstApproachService CompleteSide(ProfileStore s,ChapterOneService chapter)
    {
        var service=TowerService(s,chapter);var a=Read<ActorState>(s,"actor");var nav=new NavigationGrid(service.Geometry[a.Scene]);Done(chapter.MoveAsync(nav.FindPath(a.Position,service.Geometry[a.Scene].Spots.Single(p=>p.Id=="R1-S09.control_notice").Stand,true)));Success(Done(service.DiscoverAsync("R1-APP-R-SIDE")));a=Read<ActorState>(s,"actor");Done(chapter.MoveAsync(nav.FindPath(a.Position,service.Geometry[a.Scene].Spots.Single(p=>p.Id=="R1-S09.side_gate").Stand,true)));foreach(string step in new[]{"isolate","brace","inspect_zero_energy"})Success(Done(service.PerformSideStepAsync(step)));return service;
    }
    private static (Campaign.CampaignCases.Run Coast,FirstApproachService Service) ActualRelayEntry(ProfileStore s)
    {
        var coast=new Campaign.CampaignCases.Run(s);coast.Shelter();coast.SendRescue();coast.Exit("R0-S03");coast.Act("R0-S03.relay_entrance","inspect_relay_entrance");coast.Exit("R0-S05");coast.Read("QP03-R-RELAY");return(coast,RelayService(s));
    }
    public static ChapterOneService ActualTowerEntry(ProfileStore s)
    {
        var coast=new Campaign.CampaignCases.Run(s);coast.ThroughCommission();var chapter=new ChapterOneService(s,Catalog,ChapterOne.ChapterOneCases.Package(),coast.Service.Geometry);
        void Exit(string scene){var a=Read<ActorState>(s,"actor");var port=chapter.Geometry[a.Scene].Ports.Single(p=>p.TargetScene==scene);Walk(s,chapter,port.Stand);Success(Done(chapter.EnterPortAsync(port.Id)));}
        void Spot(string id){var a=Read<ActorState>(s,"actor");Walk(s,chapter,chapter.Geometry[a.Scene].Spots.Single(p=>p.Id==id).Stand);}
        Exit("R1-S02");var q=new Q102RouteCandidate(s,Catalog);Done(q.Prepare(WorkshopMode.Isolated));var departure=Done(q.Service.PreviewDepartureAsync());Require(departure.Preview.Status==Mist.Application.Commands.PreviewStatus.Ready);Success(Done(q.Service.DepartAsync(departure.Token)));
        Exit("R1-S03");Spot("R1-S03.havel");Success(Done(chapter.AcceptInvestigationAsync()));Exit("R1-S08");foreach(string id in Mist.Core.ChapterOne.ChapterOneRules.Evidence){Spot(chapter.Package.Materials.Single(m=>m.Id==id).Spot);Success(Done(chapter.CollectAsync(id)));}Exit("R1-S03");Spot("R1-S03.havel");var session=s.Session;
        Done(s.SaveAsync(new(session.Profile,session.Branch,session.Load,ChapterOneService.FindingDraft,null,0,"The planned test and independent recorder fit the stated clock bounds. The second check was removed. The prepared notice alone does not establish higher intent. Preserve the originals and keep the mentor's current status unknown.","normal-first-approach-finding-1")));
        Success(Done(chapter.ConfirmFindingMeaningAsync(new("approved_preplanned_test","within_recorded_clock_uncertainty","second_check_removed_before_test","insufficient_on_its_own","specific_higher_intent_not_established"),false)));var finding=Done(chapter.PreviewFindingAsync());Require(finding.Ready);Success(Done(chapter.ConfirmPreviewAsync(finding.Token)));
        Exit("R1-S02");Exit("R1-S09");Spot("R1-S09.warden");Success(Done(chapter.AcceptTowerWorkAsync()));foreach(string id in new[]{"Q104-R-CONTROLS","Q104-R-HANDOVER"}){Spot(chapter.Package.Materials.Single(m=>m.Id==id).Spot);Success(Done(chapter.CollectAsync(id)));}Spot("R1-S09.supply_indicator");Success(Done(chapter.InspectLocalSupplyAsync()));Spot("R1-S09.control_notice");return chapter;
    }
    private static ChapterOneService FixtureTowerEntry(ProfileStore s)
    {
        // Explicit prior finding/680XP engineering fixture. Current Q104 work, supply and source are real commands.
        Done(new PartyService(s,Catalog).InitializeStarterCollectionAsync());Fixture(s,"Q-103:resolution","explicit_engineering_fixture",new{scope="prior finding fixture, not a normal played chapter"},[Entity(s,"finding:E-01","verified_finding",new{scope="explicit prior finding fixture"}),Entity(s,"actor","actor",new ActorState("R1-S09",new(22,28),0))],balances:[new("experience",680)]);
        var chapter=new ChapterOneService(s,Catalog,ChapterOne.ChapterOneCases.Package(),Campaign.CampaignCases.Package().Scenes.ToImmutableDictionary(g=>g.Id));Success(Done(chapter.AcceptTowerWorkAsync()));Walk(s,chapter,chapter.Geometry["R1-S09"].Spots.Single(x=>x.Id=="R1-S09.control_notice").Stand);Success(Done(chapter.CollectAsync("Q104-R-CONTROLS")));Walk(s,chapter,chapter.Geometry["R1-S09"].Spots.Single(x=>x.Id=="R1-S09.supply_indicator").Stand);Success(Done(chapter.InspectLocalSupplyAsync()));return chapter;
    }
    private static void Walk(ProfileStore s,ChapterOneService chapter,Point2 to){var a=Read<ActorState>(s,"actor");var path=new NavigationGrid(chapter.Geometry[a.Scene]).FindPath(a.Position,to,true);Require(path.Length>0);Done(chapter.MoveAsync(path));}
    private static void SourceAndSpot(ProfileStore s,ChapterOneService chapter,FirstApproachService service,string id)
    {var d=service.Package.Encounters.Single(e=>e.Id==id);var m=service.Package.Materials.Single(m=>m.Id==d.SourceMaterial);Walk(s,chapter,service.Geometry[m.Scene].Spots.Single(p=>p.Id==m.Spot).Stand);Success(Done(service.DiscoverAsync(m.Id)));Walk(s,chapter,service.Geometry[d.Scene].Spots.Single(p=>p.Id==d.Spot).Stand);}
    public static void Register()
    {
        Add("CP-R0-03","actual_zero_XP_relay_native_entry_retains_unresolved_defence_at_140XP_without_positive_fixtures",()=>
        {string output=Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath();using var s=new ProfileStore(Path.Combine(output,"normal-first-relay-native-entry"),Catalog.ReleaseId);var(coast,_)=ActualRelayEntry(s);var v=coast.View;Require(v.Snapshot.Balances["experience"]==140&&v.Actor.Scene=="R0-S05"&&!v.Prologue.RelayReady&&!FirstApproachService.RelayCleared(v.Snapshot)&&v.Snapshot.Receipts.All(r=>r.ActionType!="explicit_engineering_fixture"&&!r.SemanticKey.StartsWith("internal:",StringComparison.Ordinal)));Done(s.BackupAsync());Done(s.CreateCheckpointAsync("manual"));File.WriteAllText(Path.Combine(Path.GetDirectoryName(s.MainPath)!,"normal-first-relay-native-entry.json"),StrictJson.Serialize(new{scope="actual zero-XP first rescue and short-range message, relay approach not yet played",experience=140,receipts=v.Snapshot.Receipts,human_play="pending"}));});
        Add("CP-R1-03","actual_zero_XP_tower_base_native_entry_has_real_680XP_permission_supply_and_no_access_or_ship",()=>
        {string output=Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath();using var s=new ProfileStore(Path.Combine(output,"normal-first-tower-native-entry"),Catalog.ReleaseId);ActualTowerEntry(s);var snapshot=Done(s.SnapshotAsync());Require(snapshot.Balances["experience"]==680&&Read<ActorState>(s,"actor").Scene=="R1-S09"&&FirstApproachService.TowerAccessIssue(snapshot)is not null&&!snapshot.Entities.Any(e=>e.Id=="ship:glimmer")&&snapshot.Receipts.All(r=>r.ActionType!="explicit_engineering_fixture"&&!r.SemanticKey.StartsWith("internal:",StringComparison.Ordinal)));Done(s.BackupAsync());Done(s.CreateCheckpointAsync("manual"));File.WriteAllText(Path.Combine(Path.GetDirectoryName(s.MainPath)!,"normal-first-tower-native-entry.json"),StrictJson.Serialize(new{scope="actual zero-XP through relay defence, Q101,Q102,Q103, current Q104 permit/supply; base approaches unplayed",experience=680,receipts=snapshot.Receipts,human_play="pending"}));});
        Add("CP-R0-03","first_approach_contract_preserves_registered_ids_template_thresholds_and_original_geometry",()=>
        {var p=Package();FirstApproachRules.Validate(p);Require(p.Encounters.Length==3&&p.Materials.Sum(m=>m.Paragraphs.Length)==6&&p.HumanReview=="human_pending");using var s=Store();var chapter=new ChapterOneService(s,Catalog,ChapterOne.ChapterOneCases.Package(),Campaign.CampaignCases.Package().Scenes.ToImmutableDictionary(g=>g.Id));var original=chapter.Geometry;var extended=FirstApproachRules.ExtendGeometry(p,original);Require(extended["R1-S09"].Ports.SequenceEqual(original["R1-S09"].Ports)&&extended["R1-S09"].Blocked.SequenceEqual(original["R1-S09"].Blocked)&&extended["R1-S09"].Spots.Length==original["R1-S09"].Spots.Length+2);Throws<InvalidDataException>(()=>FirstApproachRules.Validate(p with{HumanReview="passed"}));Throws<InvalidDataException>(()=>FirstApproachRules.ExtendGeometry(p with{ExtraSpots=p.ExtraSpots.SetItem(0,p.ExtraSpots[0]with{Spot=p.ExtraSpots[0].Spot with{Stand=new(50,50)}})},original));});
        Add("CP-R0-03","bare_relay_actor_without_actual_rescue_cannot_acquire_source_or_begin_defence",()=>
        {using var s=Store();Fixture(s,"fixture:invalid-relay","explicit_engineering_fixture",new{scope="invalid actor only"},[Entity(s,"actor","actor",new ActorState("R0-S05",new(24,53),0))]);var service=RelayService(s);long seq=s.Session.Sequence;Throws<InvalidOperationException>(()=>Done(service.DiscoverAsync("R0-APP-R-RELAY")));Throws<InvalidOperationException>(()=>Done(service.BeginAsync(FirstApproachRules.Relay)));Require(s.Session.Sequence==seq);});
        Add("CP-R0-03","actual_zero_XP_rescue_to_relay_140XP_defence_has_one_hero_and_no_cloned_sheltered_Lina_or_awards",()=>
        {using var s=Store();var(coast,service)=ActualRelayEntry(s);Require(coast.View.Snapshot.Balances["experience"]==140);var before=coast.View.Snapshot;CompleteRelay(s,coast.Service);var battle=Done(service.EncounterAsync(FirstApproachRules.Relay))!;var after=Done(s.SnapshotAsync());Require(battle.Battle.Units.Count(u=>u.Side==CombatSide.Ally)==1&&battle.Battle.Units.Single(u=>u.Side==CombatSide.Ally).Id=="hero"&&battle.Battle.Units.Single(u=>u.Id=="hero").Level==2&&FirstApproachService.RelayCleared(after)&&after.Balances.SequenceEqual(before.Balances)&&after.Items.SequenceEqual(before.Items));Require(!coast.View.Prologue.RelayReady&&!coast.View.Prologue.RecorderCollected&&!coast.View.Prologue.MailbagCollected&&coast.View.Quests.Single(q=>q.Id=="Q-P03").Lifecycle==QuestLifecycle.Active);Require(after.Receipts.All(r=>r.ActionType!="explicit_engineering_fixture"));});
        Add("CP-R0-03","relay_gate_requires_real_complete_defence_before_seating_lead_and_does_not_restore_by_reading",()=>
        {using var s=Store();var(coast,service)=ActualRelayEntry(s);coast.Act("R0-S05.relay","isolate_relay");long seq=s.Session.Sequence;Throws<InvalidOperationException>(()=>Done(coast.Service.ExecuteStationAsync("seat_relay_lead")));Require(s.Session.Sequence==seq&&!coast.View.Prologue.RelayLeadSeated);CompleteRelay(s,coast.Service);Success(Done(coast.Service.ExecuteStationAsync("seat_relay_lead")));Require(coast.View.Prologue.RelayLeadSeated&&!coast.View.Prologue.RelayReady&&FirstApproachService.RelayCleared(Done(s.SnapshotAsync())));});
        Add("CP-R0-03","relay_continue_withdraw_retry_preserves_instance_source_and_zero_reward_entry",()=>
        {
            using var s=Store();var(coast,service)=ActualRelayEntry(s);coast.Spot("R0-S05.relay_note");Success(Done(service.DiscoverAsync("R0-APP-R-RELAY")));coast.Spot("R0-S05.relay");Success(Done(service.BeginAsync(FirstApproachRules.Relay)));
            var before=Done(service.EncounterAsync(FirstApproachRules.Relay))!;var p=Done(service.PreviewActionAsync(FirstApproachRules.Relay,BattleCandidate.Choose(new(service.BattleDefinitions,before.Battle))));Require(p.Action.Legal);Success(Done(service.ConfirmAsync(p.Token)));var current=StrictJson.Serialize(Done(service.EncounterAsync(FirstApproachRules.Relay)));
            Done(s.ContinueAsync());Require(StrictJson.Serialize(Done(service.EncounterAsync(FirstApproachRules.Relay)))==current);Success(Done(service.RetryAsync(FirstApproachRules.Relay)));var retry=Done(service.EncounterAsync(FirstApproachRules.Relay))!;
            Require(retry.Battle.Instance==before.Battle.Instance&&retry.Battle.Attempt!=before.Battle.Attempt&&retry.Battle.Round==1&&Done(service.OpenMaterialAsync("R0-APP-R-RELAY")).Version==FirstApproachRules.Version&&Done(s.SnapshotAsync()).Balances["experience"]==140);Control(service,FirstApproachRules.Relay);
        });
        Add("CP-R0-03","controlled_relay_cannot_reopen_or_withdraw_to_repeat_a_result",()=>
        {using var s=Store();var(coast,service)=ActualRelayEntry(s);CompleteRelay(s,coast.Service);var before=Done(s.SnapshotAsync());Throws<InvalidOperationException>(()=>Done(service.BeginAsync(FirstApproachRules.Relay)));Throws<InvalidOperationException>(()=>Done(service.WithdrawAsync(FirstApproachRules.Relay)));Require(s.Session.Sequence==before.Sequence&&before.Receipts.Count(r=>r.SemanticKey=="first-approach:"+FirstApproachRules.Relay+":result")==1);});
        Add("CP-R1-03","tower_base_gate_rejects_reading_and_current_supply_alone_without_either_access_result",()=>
        {using var s=Store();var chapter=FixtureTowerEntry(s);var port=chapter.Geometry["R1-S09"].Ports.Single(p=>p.TargetScene=="R1-S10");Walk(s,chapter,port.Stand);long seq=s.Session.Sequence;Throws<InvalidOperationException>(()=>Done(chapter.EnterPortAsync(port.Id)));Require(s.Session.Sequence==seq&&FirstApproachService.TowerAccessIssue(Done(s.SnapshotAsync()))is not null);});
        Add("CP-R1-03","side_gate_requires_actual_isolation_brace_and_zero_energy_in_order_and_opens_only_base_access",()=>
        {using var s=Store();var chapter=FixtureTowerEntry(s);var service=TowerService(s,chapter);SourceAndSpot(s,chapter,service,FirstApproachRules.Side);Throws<InvalidOperationException>(()=>Done(service.PerformSideStepAsync("brace")));Throws<InvalidOperationException>(()=>Done(service.PerformSideStepAsync("inspect_zero_energy")));var before=Done(s.SnapshotAsync());Success(Done(service.PerformSideStepAsync("isolate")));Require(FirstApproachService.TowerAccessIssue(Done(s.SnapshotAsync()))is not null);Success(Done(service.PerformSideStepAsync("brace")));Require(FirstApproachService.TowerAccessIssue(Done(s.SnapshotAsync()))is not null);Success(Done(service.PerformSideStepAsync("inspect_zero_energy")));var after=Done(s.SnapshotAsync());Require(FirstApproachService.TowerAccessIssue(after)is null&&after.Balances.SequenceEqual(before.Balances)&&after.Items.SequenceEqual(before.Items)&&!Read<Mist.Core.ChapterOne.TidegateLocalState>(s,"tower:local").Safe&&!after.Entities.Any(e=>e.Id=="ship:glimmer"||e.Id=="readiness:TWR-R1"));});
        foreach(string id in new[]{FirstApproachRules.Side,FirstApproachRules.Outer})
        {
            string chosen=id;Add("CP-R1-03","actual_"+chosen+"_shared_combat_uses_current_5_level_two_legal_helpers_and_exclusive_zero_reward_result",()=>
            {using var s=Store();var chapter=FixtureTowerEntry(s);var service=TowerService(s,chapter);SourceAndSpot(s,chapter,service,chosen);var before=Done(s.SnapshotAsync());Success(Done(service.BeginAsync(chosen)));var battle=Done(service.EncounterAsync(chosen))!;Require(battle.Battle.Units.Count(u=>u.Side==CombatSide.Ally)==3&&battle.Battle.Units.Where(u=>u.Side==CombatSide.Ally).All(u=>u.Level==5)&&battle.Battle.Units.Single(u=>u.Id=="hero").Armor=="AR-01");Control(service,chosen);var after=Done(s.SnapshotAsync());Require(FirstApproachService.TowerAccessIssue(after)is null&&after.Balances.SequenceEqual(before.Balances)&&after.Items.SequenceEqual(before.Items)&&!Read<Mist.Core.ChapterOne.TidegateLocalState>(s,"tower:local").Safe);var other=chosen==FirstApproachRules.Side?FirstApproachRules.Outer:FirstApproachRules.Side;SourceAndSpot(s,chapter,service,other);Throws<InvalidOperationException>(()=>Done(service.BeginAsync(other)));Require(after.Receipts.Count(r=>r.ActionType=="first_approach_result")==1);});
        }
        Add("CP-R1-03","manual_side_and_outer_routes_cannot_double_result_or_tower_reward",()=>
        {using var s=Store();var chapter=FixtureTowerEntry(s);var service=CompleteSide(s,chapter);SourceAndSpot(s,chapter,service,FirstApproachRules.Outer);long seq=s.Session.Sequence;Throws<InvalidOperationException>(()=>Done(service.BeginAsync(FirstApproachRules.Outer)));Require(s.Session.Sequence==seq&&!Done(chapter.PreviewHandoverAsync()).Ready&&Done(s.SnapshotAsync()).Receipts.Count(r=>r.ActionType=="first_approach_result")==1);});
        Add("CP-R1-03","unrelated_or_wrong_instance_receipts_cannot_authorize_tower_entry",()=>
        {using var s=Store();var chapter=FixtureTowerEntry(s);var quest=Read<QuestState>(s,"quest:Q-104");var fake=new FirstApproachResult(FirstApproachRules.Outer,"Q-104",Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"shared_combat",FirstApproachRules.Version,true);Fixture(s,"first-approach:"+FirstApproachRules.Outer+":result","first_approach_result",fake,[Entity(s,"first-approach:result:"+FirstApproachRules.Outer,"first_approach_result",fake)]);Require(fake.QuestInstance!=quest.Instance&&FirstApproachService.TowerAccessIssue(Done(s.SnapshotAsync()))is not null);});
        Add("CP-R1-03","typed_matching_result_without_actual_battle_state_still_cannot_authorize_entry",()=>
        {using var s=Store();var chapter=FixtureTowerEntry(s);var fake=new FirstApproachResult(FirstApproachRules.Outer,"Q-104",Read<QuestState>(s,"quest:Q-104").Instance,Guid.NewGuid(),Guid.NewGuid(),"shared_combat",FirstApproachRules.Version,true);Fixture(s,"first-approach:"+FirstApproachRules.Outer+":result","first_approach_result",fake,[Entity(s,"first-approach:result:"+FirstApproachRules.Outer,"first_approach_result",fake)]);Require(FirstApproachService.TowerAccessIssue(Done(s.SnapshotAsync()))is not null);});
        Add("CP-R1-03","old_load_action_preview_is_rejected_and_actual_battle_survives_continue",()=>
        {using var s=Store();var chapter=FixtureTowerEntry(s);var service=TowerService(s,chapter);SourceAndSpot(s,chapter,service,FirstApproachRules.Outer);Success(Done(service.BeginAsync(FirstApproachRules.Outer)));var before=Done(service.EncounterAsync(FirstApproachRules.Outer))!;var p=Done(service.PreviewActionAsync(FirstApproachRules.Outer,BattleCandidate.Choose(new(service.BattleDefinitions,before.Battle))));Require(p.Action.Legal);Done(s.ContinueAsync());Throws<InvalidOperationException>(()=>Done(service.ConfirmAsync(p.Token)));Require(StrictJson.Serialize(Done(service.EncounterAsync(FirstApproachRules.Outer)))==StrictJson.Serialize(before));Control(service,FirstApproachRules.Outer);});
    }
}

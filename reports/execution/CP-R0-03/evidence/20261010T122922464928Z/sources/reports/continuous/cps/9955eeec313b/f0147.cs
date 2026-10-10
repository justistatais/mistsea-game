using System.Collections.Immutable;
using Mist.Application.Campaign;
using Mist.Application.Contracts;
using Mist.Application.Q102;
using Mist.Application.Storage;
using Mist.Core.Campaign;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.Progression;
using Mist.DebugCandidates;
using Mist.Infrastructure.Storage;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.Campaign;

// This drives actual persisted adventure actions. It is not a native-player recording or a language trial.
public static class CampaignCases
{
    private static readonly Mist.Core.Content.RuntimeCatalog Catalog=Q102.Q102Cases.Catalog();
    private static string ProjectRoot=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    public static CampaignPackage Package()=>StrictJson.Deserialize<CampaignPackage>(File.ReadAllText(Path.Combine(ProjectRoot,"content/source/campaign/first_entry.json")));
    private static ProfileStore Store(string name)=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"campaign-"+name+"-"+Guid.NewGuid()),Catalog.ReleaseId);
    private static void Success(CommandResult result)=>Require(result.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(result));
    public sealed class Run(ProfileStore store)
    {
        public CampaignService Service {get;}=new(store,Catalog,Package());
        public CampaignView View=>Done(Service.ViewAsync());
        public List<object> Steps {get;}=[];
        private void Record(string action){var v=View;Steps.Add(new{action,v.Actor,v.Prologue,v.CurrentQuest,sequence=v.Snapshot.Sequence,xp=v.Snapshot.Balances.GetValueOrDefault("experience")});}
        public void Initialize(){Done(Service.InitializeNewAsync());Record("actual_new_game_zero_xp");}
        public void Walk(Point2 target)
        {
            var v=View;var path=new NavigationGrid(Service.Geometry[v.Actor.Scene]).FindPath(v.Actor.Position,target,v.Actor.Scene=="R0-S02"&&!v.Prologue.ShoreSafe);
            Require(path.Length>0,"No legal campaign path: "+v.Actor.Scene);Done(Service.TraverseAsync(path));Record("walk");
        }
        public void Spot(string id){Walk(Service.Geometry[View.Actor.Scene].Spots.Single(s=>s.Id==id).Stand);}
        public void Exit(string target)
        {
            var port=Service.Geometry[View.Actor.Scene].Ports.Single(p=>p.TargetScene==target);Walk(port.Stand);Success(Done(Service.EnterPortAsync(port.Id)));Record("exit:"+target);
        }
        public void Read(string id)
        {
            Spot(Service.Package.Materials.Single(m=>m.Id==id).Spot);Success(Done(Service.DiscoverAsync(id)));Record("material_acquired:"+id);
        }
        public void Act(string spot,string action){Spot(spot);Success(Done(Service.ExecuteStationAsync(action)));Record(action);}
        public void FirstRescue()
        {
            Initialize();Read("QP01-R-ROUTE");Read("QP01-R-LOG");Act("R0-S01.tools","acquire_rescue_tools");Act("R0-S01.floor","reinforce_cabin_path");
            Success(Done(Service.ConfirmTimelineAsync(CoastTimeline.LightLossBeforeDrift)));Exit("R0-S02");Read("QP02-R-TIDE");Read("QP02-L-CORRECTION");Act("R0-S02.landing","evacuate_to_shore");
        }
        public void ShoreBattle()
        {
            Spot("R0-S02.creature");Success(Done(Service.BeginShoreEncounterAsync()));Record("actual_shore_encounter_entered");
            for(int count=0;count<100;count++)
            {
                var battle=View.ShoreBattle!.Battle;if(battle.Outcome!=CombatOutcome.Active)break;
                var engine=new BattleEngine(new(Catalog),battle);var actor=battle.Units.Single(u=>u.Id==battle.CurrentActor);
                var request=new BattleRequest(new(Guid.NewGuid()),"BASIC",actor.Id,["shore_creature"]);
                if(!engine.Preview(request).Legal)request=request with{Action="END",Targets=[]};
                var p=Done(Service.PreviewShoreActionAsync(request));Require(p.Action.Legal,p.Action.Reason??"Illegal shore action");Success(Done(Service.ConfirmShoreActionAsync(p.Token)));Record("actual_shore_combat_action");
            }
            Require(View.ShoreBattle!.Battle.Outcome==CombatOutcome.Controlled&&View.Prologue.ShoreSafe,"Actual shore battle did not control the receiving path.");
        }
        public void PrepareWinch()
        {
            Exit("R0-S03");Read("QP02-R-WINCH");Act("R0-S03.winch","isolate_winch");Act("R0-S03.winch","seat_winch_cable");Act("R0-S03.winch","fix_rescue_rope");
            Spot("R0-S03.winch");Success(Done(Service.ChooseLandingAsync(RescueLanding.NorthReach)));Act("R0-S03.winch","power_winch");Act("R0-S03.winch","test_winch");
        }
        public void Shelter()
        {
            FirstRescue();ShoreBattle();PrepareWinch();Act("R0-S03.winch","move_survivors_to_shelter");Exit("R0-S04");Act("R0-S04.survivor_register","register_survivors");Spot("R0-S04.message_desk");
        }
        public void Save(string task,string body)
        {
            var d=Done(store.GetDraftAsync(task));var s=store.Session;Done(store.SaveAsync(new(s.Profile,s.Branch,s.Load,task,d?.Id,d?.Revision??0,body,"campaign@"+Service.Package.Version)));Record("draft_saved:"+task);
        }
        public void SendRescue()
        {
            Save(CampaignService.RescueDraft,"We are at the rescue shelter. Please collect four survivors and the technician by the northern reach; the southern steps are damaged.");
            Success(Done(Service.ConfirmRescueMeaningAsync(new("shelter",4,"pickup","north_reach_only"),true)));
            var p=Done(Service.PreviewRescueMessageAsync());Require(p.Ready,StrictJson.Serialize(p));Success(Done(Service.ConfirmSubmissionAsync(p.Token)));Record("actual_rescue_message_sent");
        }
        public void Relay()
        {
            Exit("R0-S03");Act("R0-S03.relay_entrance","inspect_relay_entrance");Exit("R0-S05");Read("QP03-R-RELAY");FirstApproaches.FirstApproachCases.CompleteRelay(store,Service);Record("actual_relay_defence_controlled");Act("R0-S05.relay","isolate_relay");Act("R0-S05.relay","seat_relay_lead");Act("R0-S05.relay","power_relay");Act("R0-S05.relay","test_relay");
            Act("R0-S05.recorder","collect_coast_record");Act("R0-S05.mailbag","collect_inspection_mail");Act("R0-S05.back_door","open_shelter_back_door");Exit("R0-S04");Read("QP03-R-PICKUP");Act("R0-S04.pickup","acknowledge_pickup");
        }
        public void ArriveHarbour()
        {
            Exit("R1-S01");Read("Q101-R-ARRIVAL");Read("Q101-R-CREDENTIAL");Act("R1-S01.contact","register_harbour_arrival");Exit("R1-S02");Read("Q101-R-CLOSURE");Exit("R1-S03");Read("Q101-R-EXCEPTION");Spot("R1-S03.havel");
        }
        public void PrepareHarbourRequest()
        {
            Save(CampaignService.HarbourDraft,"I request limited civil ferry navigation maintenance and access to the local workshop parts. The purpose is evacuation assistance. This request excludes operating the harbour main generator.");
            Success(Done(Service.ConfirmHarbourMeaningAsync(new("evacuation_assistance","civil_ferry_and_local_workshop","main_generator_excluded"),true)));
        }
        public void ThroughPrologue(){Shelter();SendRescue();Relay();}
        public void ThroughCommission(){ThroughPrologue();ArriveHarbour();PrepareHarbourRequest();var p=Done(Service.PreviewHarbourRequestAsync());Require(p.Ready,StrictJson.Serialize(p));Success(Done(Service.ConfirmSubmissionAsync(p.Token)));Record("actual_Havel_commission_issued");}
    }
    public static void Register()
    {
        Add("CX-065","Normal new game has zero XP and no maintenance permission; repeat initialise grants nothing",()=>
        {
            using var store=Store("new");var r=new Run(store);r.Initialize();var v=r.View;Require(v.Actor.Scene=="R0-S01"&&v.Snapshot.Balances.GetValueOrDefault("experience")==0&&v.Snapshot.Balances["marks"]==100);
            Require(!v.Campaign.ProloguePlayed&&!v.Q102Ready&&v.Materials.Length==0&&v.Snapshot.Entities.All(e=>e.Id!="permission:Q-102")&&v.Snapshot.Receipts.All(e=>!e.SemanticKey.StartsWith("internal:")));
            Require(v.Snapshot.Items.Single(i=>i.Id==CampaignService.ToolId).OwnerKind=="warehouse"&&v.Snapshot.Items.Count(i=>i.Id==CampaignService.RopeId)==1);
            long sequence=v.Snapshot.Sequence;Done(r.Service.InitializeNewAsync());Require(r.View.Snapshot.Sequence==sequence&&r.View.Snapshot.Balances["marks"]==100);
        });
        Add("CP-R0-03","All sixteen exits are reciprocal and each stand/interaction can be reached by the shared navigation",()=>
        {
            var p=Package();CampaignRules.Validate(p);Require(p.Scenes.Sum(s=>s.Ports.Length)==16);
            foreach(var scene in p.Scenes)
            foreach(var port in scene.Ports)
            foreach(var spot in scene.Spots)
            {
                var nav=new NavigationGrid(scene);var path=nav.FindPath(port.Stand,spot.Stand);Require(path.Length>0,scene.Id+":"+spot.Id);var previous=port.Stand;
                foreach(var point in path){Require(nav.SegmentClear(previous,point));previous=point;}
            }
        });
        Add("CP-R0-03","Cabin exit, far pickup and premature mentor-record access preserve the unprepared world",()=>
        {
            using var store=Store("gates");var r=new Run(store);r.Initialize();long sequence=r.View.Snapshot.Sequence;
            Throws<InvalidOperationException>(()=>Done(r.Service.ExecuteStationAsync("acquire_rescue_tools")));Throws<InvalidOperationException>(()=>Done(r.Service.DiscoverAsync("QP03-R-MAIL")));
            Require(r.View.Snapshot.Sequence==sequence);r.Spot("R0-S01.floor");Throws<InvalidOperationException>(()=>Done(r.Service.ExecuteStationAsync("reinforce_cabin_path")));
            var port=r.Service.Geometry["R0-S01"].Ports.Single();r.Walk(port.Stand);Throws<InvalidOperationException>(()=>Done(r.Service.EnterPortAsync(port.Id)));
            Require(r.View.Actor.Scene=="R0-S01"&&r.View.Snapshot.Balances.GetValueOrDefault("experience")==0&&r.View.Snapshot.Entities.All(e=>e.Id!="material:QP03-R-MAIL"));
        });
        Add("CX-065","Actual shore encounter uses two-level rescue cooperation and never formal recruitment or kill XP",()=>
        {
            using var store=Store("shore");var r=new Run(store);r.FirstRescue();Require(ProgressionRules.Level(r.View.Snapshot.Balances["experience"])==2);
            r.Spot("R0-S02.creature");Success(Done(r.Service.BeginShoreEncounterAsync()));var b=r.View.ShoreBattle!.Battle;Require(b.Units.Count(u=>u.Side==CombatSide.Ally)==2&&b.Units.Any(u=>u.Id=="CP-LINA")&&!b.Units.Any(u=>u.Id.StartsWith("AUX")));
            Throws<InvalidOperationException>(()=>Done(r.Service.BeginShoreEncounterAsync()));Throws<InvalidOperationException>(()=>r.Walk(new(60,83)));
            var request=new BattleRequest(new(Guid.NewGuid()),"BASIC",b.CurrentActor!,["shore_creature"]);long before=r.View.Snapshot.Sequence;var p=Done(r.Service.PreviewShoreActionAsync(request));Require(p.Action.Legal&&r.View.Snapshot.Sequence==before);
            Success(Done(r.Service.ConfirmShoreActionAsync(p.Token)));Require(Done(r.Service.ConfirmShoreActionAsync(p.Token)).Status==CommitStatus.AlreadyCommitted);
            var exact=r.View.ShoreBattle!.Battle;Done(store.ContinueAsync());Require(StrictJson.Serialize(exact)==StrictJson.Serialize(r.View.ShoreBattle!.Battle));Success(Done(r.Service.WithdrawShoreAsync()));
            r.ShoreBattle();Require(r.View.Snapshot.Balances["experience"]==100&&r.View.Snapshot.Entities.All(e=>e.Id!="roster:CP-LINA")&&r.View.Snapshot.Receipts.All(e=>e.SemanticKey!="Q-104:resolution"));
        });
        Add("CP-R0-03","Changing the receiving direction invalidates its check; unsafe line does not move or lose survivors",()=>
        {
            using var store=Store("winch");var r=new Run(store);r.FirstRescue();r.ShoreBattle();r.PrepareWinch();Require(r.View.Prologue.WinchReady);
            Success(Done(r.Service.ChooseLandingAsync(RescueLanding.SouthMarker)));Require(!r.View.Prologue.WinchReady&&r.View.Prologue.WinchTestRevision is null);long sequence=r.View.Snapshot.Sequence;
            Throws<InvalidOperationException>(()=>Done(r.Service.ExecuteStationAsync("test_winch")));Throws<InvalidOperationException>(()=>Done(r.Service.ExecuteStationAsync("move_survivors_to_shelter")));Require(r.View.Snapshot.Sequence==sequence&&!r.View.Prologue.SurvivorsSheltered&&r.View.Snapshot.Balances["experience"]==100);
            Success(Done(r.Service.ChooseLandingAsync(RescueLanding.NorthReach)));Success(Done(r.Service.ExecuteStationAsync("test_winch")));Success(Done(r.Service.ExecuteStationAsync("move_survivors_to_shelter")));Require(r.View.Prologue.SurvivorsSheltered);
            Require(r.View.Snapshot.Items.Single(i=>i.Id==CampaignService.RopeId).OwnerKind=="installed");
        });
        Add("CP-R0-03","Saving and confirming meaning do not send; wrong rescue scope and a changed draft leave rewards untouched",()=>
        {
            using var store=Store("message");var r=new Run(store);r.Shelter();Throws<InvalidOperationException>(()=>Done(r.Service.ConfirmRescueMeaningAsync(new("shelter",4,"pickup","north_reach_only"),false)));
            r.Save(CampaignService.RescueDraft,"Please collect us at the shelter.");Require(r.View.Snapshot.Balances["experience"]==100&&r.View.CurrentQuest=="Q-P02");
            Success(Done(r.Service.ConfirmRescueMeaningAsync(new("shelter",4,"pickup","south_steps"),true)));var wrong=Done(r.Service.PreviewRescueMessageAsync());Require(!wrong.Ready&&Done(r.Service.ConfirmSubmissionAsync(wrong.Token)).Status==CommitStatus.Rejected);
            Success(Done(r.Service.ConfirmRescueMeaningAsync(new("shelter",4,"pickup","north_reach_only"),true)));var p=Done(r.Service.PreviewRescueMessageAsync());Require(p.Ready);r.Save(CampaignService.RescueDraft,"Changed message 😀é.");Require(Done(r.Service.ConfirmSubmissionAsync(p.Token)).Status==CommitStatus.Rejected&&r.View.Snapshot.Balances["experience"]==100);
            r.SendRescue();Require(r.View.Snapshot.Balances["experience"]==140&&r.View.Snapshot.Balances["marks"]==120&&r.View.Snapshot.Balances["parts"]==1&&r.View.CurrentQuest=="Q-P03");
            var final=r.View.Snapshot.Receipts.Single(e=>e.SemanticKey=="Q-P02:resolution");Require(final.ActionType=="rescue_message_sent"&&final.ResultJson.Contains("structured_assistance"));
        });
        Add("CP-R0-03","Relay reconfiguration invalidates pickup while acquired originals remain; prologue closes from actual rescue",()=>
        {
            using var store=Store("relay");var r=new Run(store);r.Shelter();r.SendRescue();r.Exit("R0-S03");r.Act("R0-S03.relay_entrance","inspect_relay_entrance");r.Exit("R0-S05");r.Read("QP03-R-RELAY");
            r.Spot("R0-S05.mailbag");Throws<InvalidOperationException>(()=>Done(r.Service.ExecuteStationAsync("collect_inspection_mail")));FirstApproaches.FirstApproachCases.CompleteRelay(store,r.Service);r.Act("R0-S05.relay","isolate_relay");r.Act("R0-S05.relay","seat_relay_lead");r.Act("R0-S05.relay","power_relay");r.Act("R0-S05.relay","test_relay");
            r.Act("R0-S05.recorder","collect_coast_record");r.Act("R0-S05.mailbag","collect_inspection_mail");r.Act("R0-S05.back_door","open_shelter_back_door");r.Act("R0-S05.relay","isolate_relay");r.Exit("R0-S04");
            r.Spot("R0-S04.pickup");Throws<InvalidOperationException>(()=>Done(r.Service.DiscoverAsync("QP03-R-PICKUP")));Require(!r.View.Campaign.ProloguePlayed&&r.View.Materials.Any(m=>m.Id=="QP03-R-MAIL")&&r.View.Snapshot.Balances["experience"]==140);
            r.Exit("R0-S05");r.Act("R0-S05.relay","power_relay");r.Act("R0-S05.relay","test_relay");r.Exit("R0-S04");r.Read("QP03-R-PICKUP");r.Act("R0-S04.pickup","acknowledge_pickup");Require(r.View.Campaign.ProloguePlayed&&r.View.Snapshot.Balances["experience"]==240&&!r.View.Q102Ready);
            Require(r.View.Snapshot.Entities.All(e=>!e.Id.StartsWith("knowledge:"))&&r.View.Snapshot.Receipts.Single(e=>e.SemanticKey=="Q-P03:resolution").ResultJson.Contains("unknown"));
        });
        Add("CX-065","Missing closure evidence and a request outside Havel authority never create permission or ferry items",()=>
        {
            using var store=Store("authority-boundary");var r=new Run(store);r.ThroughPrologue();r.Exit("R1-S01");r.Read("Q101-R-ARRIVAL");r.Read("Q101-R-CREDENTIAL");r.Act("R1-S01.contact","register_harbour_arrival");r.Exit("R1-S02");r.Exit("R1-S03");r.Read("Q101-R-EXCEPTION");r.Spot("R1-S03.havel");r.PrepareHarbourRequest();
            long sequence=r.View.Snapshot.Sequence;var missing=Done(r.Service.PreviewHarbourRequestAsync());Require(!missing.Ready&&missing.Issues.Any(i=>i.Code=="sources_missing")&&r.View.Snapshot.Sequence==sequence);
            Require(Done(r.Service.ConfirmSubmissionAsync(missing.Token)).Status==CommitStatus.Rejected&&r.View.Snapshot.Entities.All(e=>e.Id!="permission:Q-102")&&r.View.Snapshot.Items.All(i=>i.Id!=Q102Service.BracketId));
            r.Exit("R1-S02");r.Read("Q101-R-CLOSURE");r.Exit("R1-S03");r.Spot("R1-S03.havel");Success(Done(r.Service.ConfirmHarbourMeaningAsync(new("patrol_ship_request","civil_ferry_and_local_workshop","main_generator_excluded"),true)));var outside=Done(r.Service.PreviewHarbourRequestAsync());Require(!outside.Ready&&outside.Issues.Any(i=>i.Code=="meaning_not_admissible")&&r.View.Snapshot.Balances["experience"]==240&&!r.View.Q102Ready);
            r.PrepareHarbourRequest();var valid=Done(r.Service.PreviewHarbourRequestAsync());Require(valid.Ready);Success(Done(r.Service.ConfirmSubmissionAsync(valid.Token)));Require(r.View.Snapshot.Balances["experience"]==340);
        });
        Add("CX-065","Havel commission requires actual arrival and four sources; Q102 is level three from four once-only rewards",()=>
        {
            using var store=Store("commission");var r=new Run(store);r.ThroughCommission();var v=r.View;Require(v.Campaign.ProloguePlayed&&v.Q102Ready&&v.Actor.Scene=="R1-S03"&&v.Snapshot.Balances["experience"]==340&&v.Snapshot.Balances["marks"]==170&&v.Snapshot.Balances["parts"]==2);
            Require(ProgressionRules.Level(340)==3&&ProgressionRules.Grade(v.Snapshot.Receipts.Select(x=>x.SemanticKey))==0&&CampaignRules.Quests.All(q=>v.Snapshot.Receipts.Count(x=>x.SemanticKey==q+":resolution")==1));
            var permission=v.Snapshot.Entities.Single(e=>e.Id=="permission:Q-102");Require(permission.Json.Contains("Q-101:resolution")&&!permission.Json.Contains("seed"));
            Require(v.Snapshot.Items.Single(i=>i.Id==Q102Service.BracketId).OwnerKind=="warehouse"&&v.Snapshot.Items.Single(i=>i.Id==Q102Service.BatteryId).OwnerId=="CIVIL-FERRY"&&!v.Campaign.GlimmerOwned&&!v.Campaign.Q104Closed);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(store.MainPath)!,"actual-first-entry-actions.json"),StrictJson.Serialize(r.Steps));
        });
        Add("CX-065","Continue does not reseed; old pre-commission branch cannot execute its future preview or inherit ferry objects",()=>
        {
            using var store=Store("branch");var r=new Run(store);r.ThroughPrologue();r.ArriveHarbour();r.PrepareHarbourRequest();var p=Done(r.Service.PreviewHarbourRequestAsync());Require(p.Ready);string checkpoint=Done(store.CreateCheckpointAsync("manual"));
            Success(Done(r.Service.ConfirmSubmissionAsync(p.Token)));Require(Done(r.Service.ConfirmSubmissionAsync(p.Token)).Status==CommitStatus.AlreadyCommitted);Done(store.ContinueAsync());Done(r.Service.InitializeNewAsync());Require(r.View.Snapshot.Balances["experience"]==340&&r.View.Snapshot.Items.Count(i=>i.Id==Q102Service.BracketId)==1);
            Done(store.LoadCheckpointAsync(checkpoint));Require(!r.View.Q102Ready&&r.View.Snapshot.Balances["experience"]==240&&r.View.Snapshot.Items.All(i=>i.Id!=Q102Service.BracketId)&&Done(r.Service.ConfirmSubmissionAsync(p.Token)).Status==CommitStatus.Rejected);
            var renewed=Done(r.Service.PreviewHarbourRequestAsync());Require(renewed.Ready);Success(Done(r.Service.ConfirmSubmissionAsync(renewed.Token)));Require(r.View.Snapshot.Balances["experience"]==340&&r.View.Snapshot.Items.Count(i=>i.Id==Q102Service.BracketId)==1);
        });
        Add("CX-065","Normal campaign hands the same actor and unique ferry devices to the existing physical Q102 transaction",()=>
        {
            using var store=Store("handoff");var r=new Run(store);r.ThroughCommission();var run=new Q102RouteCandidate(store,Catalog);Done(run.Exit("R1-S02"));Done(run.Spot("R1-S02.quay_contact"));Success(Done(run.Service.AcceptAsync()));
            Done(run.Read("R1-S02.bulletin","Q102-R-NOTICE"));Done(run.Exit("R1-S05"));Done(run.Exit("R1-S07"));Done(run.Read("R1-S07.main_generator_tag","PZ102-R-PANEL"));Done(run.Read("R1-S07.maintenance_note","PZ102-R-WORKSHOP"));Done(run.Spot("R1-S07.local_controller"));Success(Done(run.Service.WorkshopModeAsync(WorkshopMode.Isolated)));
            Done(run.Exit("R1-S06"));Done(run.Spot("R1-S06.spare_bracket"));Success(Done(run.Service.PickBracketAsync()));Done(run.Spot("R1-S06.shortcut_latch"));Success(Done(run.Service.OpenLatchAsync()));Done(run.Exit("R1-S02"));Done(run.Exit("R1-S03"));Done(run.Read("R1-S03.dispatch_audio","Q102-L-REVISION"));Done(run.Read("R1-S03.route_b_rule","Q102-R-B"));Done(run.Exit("R1-S02"));Done(run.Exit("R1-S04"));Done(run.Read("R1-S04.vessel_spec","Q102-R-VESSEL"));Done(run.Read("R1-S04.reserve_battery","Q102-R-BATTERY"));Success(Done(run.Service.ConnectBatteryAsync(true)));Done(run.Spot("R1-S04.beacon"));Success(Done(run.Service.InstallBracketAsync()));Success(Done(run.Service.TestBeaconAsync()));Done(run.Spot("R1-S04.writing_desk"));Done(run.SaveText("Use C with the tested navigation lamp. A is closed, B is too shallow. Fit the bracket, use the ferry reserve and keep the main generator off."));Success(Done(run.Service.ConfirmMeaningAsync(Q102RouteCandidate.CorrectMeaning,true)));
            var departure=Done(run.Service.PreviewDepartureAsync());Require(departure.Preview.Status==Mist.Application.Commands.PreviewStatus.Ready);Success(Done(run.Service.DepartAsync(departure.Token)));
            var q=Done(run.Service.ViewAsync());Require(q.Snapshot.Balances["experience"]==500&&q.Snapshot.Balances["marks"]==230&&q.Snapshot.Balances["parts"]==4&&q.Actor.Scene=="R1-S02"&&!q.Ferry.Docked);
            Require(q.Snapshot.Items.Count(i=>i.Id==Q102Service.BracketId)==1&&q.Snapshot.Receipts.All(x=>x.SemanticKey!="internal:q102-slice:seed")&&StrictJson.Deserialize<CampaignState>(q.Entity("campaign").Json).ProloguePlayed);
        });
        Add("CP-R0-01","Original sources carry declared review gaps and chronology limits without a player answer-key",()=>
        {
            var p=Package();Require(p.Materials.Length==13&&p.Materials.All(m=>m.ReviewStatus=="original_unreviewed"&&m.CalibrationStatus=="human_pending"));
            var listening=p.Materials.Single(m=>m.Modality=="listening_script");Require(listening.AudioAsset==""&&p.Remaining.Any(x=>x.Contains("实际音轨")));
            Require(p.Materials.Single(m=>m.Id=="QP01-R-LOG").English.Contains("do not identify")&&p.Materials.Single(m=>m.Id=="QP03-R-MAIL").English.Contains("cannot tell you where I am")&&p.Materials.All(m=>!m.English.Contains("answer key",StringComparison.OrdinalIgnoreCase)));
        });
    }
}

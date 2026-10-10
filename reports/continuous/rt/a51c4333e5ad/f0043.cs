using System.Collections.Immutable;
using Mist.Application.Contracts;
using Mist.Application.Factions;
using Mist.Application.ReleaseReadiness;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Application.WorkOrders;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Engineering;
using Mist.Core.Learning;
using Mist.Core.Quests;
using Mist.Core.ReleaseReadiness;
using Mist.Infrastructure.Storage;
using static Mist.Tests.NPC.NpcCases;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.ReleaseReadiness;

public static class ReleaseReadinessCases
{
    private static readonly RuntimeCatalog Catalog=Q102.Q102Cases.Catalog();
    private const string Technical="technical-ending-fixture-v1",Protocol="protocol-d3-v1";
    private static ProfileStore Store(string name)=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"ending-fixture-"+name+"-"+Guid.NewGuid()),Catalog.ReleaseId);
    private static void Success(CommandResult r)=>Require(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(r));
    private static T? Read<T>(ProfileStore s,string id) where T:class=>Done(s.SnapshotAsync()).Entities.SingleOrDefault(e=>e.Id==id) is{} e?StrictJson.Deserialize<T>(e.Json):null;
    private static void Set<T>(ProfileStore s,string id,string kind,T value) where T:notnull=>Fixture(s,"fixture:set:"+Guid.NewGuid(),"explicit_offline_ending_fixture",new{scope="engineering_fixture_not_played_campaign"},[Entity(s,id,kind,value)]);
    private static QuestState Active(ProfileStore s,string id)
    {var q=new QuestState(id,Guid.NewGuid(),QuestLifecycle.Active,null);Set(s,"quest:"+id,"quest",q);return q;}
    private static QuestState Closed(ProfileStore s,string id)
    {
        var q=new QuestState(id,Read<QuestState>(s,"quest:"+id)?.Instance??Guid.NewGuid(),QuestLifecycle.Closed,QuestOutcome.Completed);
        Fixture(s,id+":resolution","main_quest_resolution",new MainQuestResolution(id,q.Instance,"completed"),[Entity(s,"quest:"+id,"quest",q)]);return q;
    }
    // Late chapter, vessel and authority records below are explicitly offline typed fixtures.
    // They exercise consumers in real SQLite. They are not full chapter or narrative execution.
    private static void Prepare(ProfileStore s,bool distributed=true)
    {
        Closed(s,"Q-404");var rules=new EngineeringRules(Catalog);var service=new WorkOrderService(s,Catalog);
        if(distributed){var t=Fixture(s,"fixture:technical","technical_package_verified",new{version=Technical});Set(s,"technical-package","technical_package",new TechnicalPackage(Technical,true,t.Receipt!.Id.Value.ToString()));}
        var balances=new Dictionary<string,long>(StringComparer.Ordinal);
        foreach(string tower in EndingRules.Towers)
        {
            string region=tower[4..];var safety=Fixture(s,"fixture:safety:"+tower,"tower_safety_verified",new TowerSafety(tower,true,"explicit offline safety fixture"));Success(Done(service.RegisterSafetyAsync(safety.Receipt!.Id.Value.ToString())));
            string[] operators=["fixture-operator-"+region+"-1","fixture-operator-"+region+"-2"];
            var authority=new WorkTeamAuthority("fixture-team-"+region,region,true,operators.ToImmutableArray(),true);var ar=Fixture(s,"fixture:crew:"+tower,"engineering_team_verified",authority);
            var team=new QualifiedWorkTeam(authority.Id,region,true,operators.ToImmutableArray(),true,ar.Receipt!.Id.Value.ToString());Set(s,"engineering-team:"+team.Id,"engineering_team",team);
            foreach(var type in distributed?new[]{WorkType.BaselineCheck,WorkType.EnhancedInstall,WorkType.TransitionSupply,WorkType.FieldTrial}:new[]{WorkType.BaselineCheck})
            {
                string key=WorkOrderService.Preparation+":"+tower+":"+type;var resources=rules.Required(tower,type);var work=new TowerWork(WorkOrderService.Preparation,tower,type,Technical,WorkStage.Verified,resources,team.Id,operators,4);
                Fixture(s,"fixture:verified:"+key,"engineering_work_result",new{work=key,stage=WorkStage.Verified,configuration=Technical,operators},[Entity(s,"work:"+key,"work_order",work)]);
                if(type!=WorkType.FieldTrial)foreach(var r in resources){string account=EngineeringRules.Installed(tower,r.Key);balances[account]=balances.GetValueOrDefault(account)+r.Value;}
            }
            foreach(string actor in operators)Set(s,"duty:"+actor,"operator_duty",new{tower,actor,source_work=WorkOrderService.Preparation+":"+tower+":"+WorkType.BaselineCheck});
            Set(s,"readiness:"+tower,"tower_readiness",new TowerReadiness(tower,true,true,distributed?Technical:null,distributed,true,distributed,distributed?Technical:null));
            if(distributed)
            {
                var faction=new FactionService(s,Catalog);
                foreach(var role in new[]{MandateRole.Operator,MandateRole.Representative})
                {
                    var appointment=new RegionalAppointment(region,role,"fixture-appointment-"+region+"-"+role,"fixture-holder-"+region+"-"+role,(role==MandateRole.Operator?"operate:":"represent:")+region,Protocol,"fixture source");
                    var a=Fixture(s,"fixture:appointment:"+region+":"+role,"regional_appointment_verified",appointment);Success(Done(faction.RegisterAppointmentAsync(a.Receipt!.Id.Value.ToString())));
                    var signature=new AgreementSignature(region,role,appointment.Instance,appointment.Holder,Protocol,"fixture source");var r=Fixture(s,"fixture:signature:"+region+":"+role,"regional_signature_verified",signature);Success(Done(faction.AcceptSignatureAsync(r.Receipt!.Id.Value.ToString())));
                }
            }
        }
        Fixture(s,"fixture:installed-materials","explicit_offline_ending_fixture",new{scope="installed engineering quantity fixture"},balances:balances.Select(p=>new BalanceChange(p.Key,p.Value)).ToImmutableArray());
    }
    private static void RiskRegister(ProfileStore s)
    {
        var snapshot=Done(s.SnapshotAsync());long version=(Read<CurrentRiskRegister>(s,"final:commitments")?.Version??0)+1;
        var actual=new RiskRegisterVerification(version,snapshot.Entities.Where(e=>e.Kind=="high_risk_commitment").Select(e=>e.Id).Order().ToImmutableArray(),snapshot.Entities.Where(e=>e.Kind=="transport_segment").Select(e=>e.Id).Order().ToImmutableArray());
        var r=Fixture(s,"fixture:risk-register:"+version,"high_risk_register_verified",actual);Set(s,"final:commitments","current_risk_register",new CurrentRiskRegister(version,actual.CommitmentIds,actual.TransportIds,r.Receipt!.Id.Value.ToString()));
    }
    private static void Departure(ProfileStore s)
    {
        var q104=Closed(s,"Q-104");var vessel=new SharedVessel(Guid.NewGuid(),"R6","N-DECKWATCH",["HUB-S01","HUB-S02","HUB-S03"]);
        Fixture(s,"fixture:glimmer-transfer","vessel_use_transfer_verified",new GlimmerUseTransfer(vessel.Instance,q104.Instance,"player",vessel.WatchOfficer),[Entity(s,"ship:glimmer","shared_vessel",vessel)]);
        Closed(s,"Q-602");Closed(s,"Q-603");Active(s,"Q-604");RiskRegister(s);
    }
    private static void FinalScene(ProfileStore s)
    {
        Closed(s,"Q-604");Closed(s,"Q-F03");var central=new CentralStabilityResult(Guid.NewGuid(),3,true,true,true);var cr=Fixture(s,"fixture:central-stability:"+Guid.NewGuid(),"central_stability_verified",central);
        Set(s,"final:central-safety","central_safety",new CentralSafetyState(central.Instance,central.Configuration,true,true,true,cr.Receipt!.Id.Value.ToString()));
        var quest=Active(s,"Q-F04");var access=new FinalControlAccess(quest.Instance,"RF-S07",50,40,true,"");var r=Fixture(s,"fixture:final-control:"+Guid.NewGuid(),"final_control_access_verified",access);
        Set(s,"final:control-access","final_control_access",access with{SourceReceipt=r.Receipt!.Id.Value.ToString()});Set(s,"actor","actor",new ActorState("RF-S07",new(50,40),10));
    }
    private sealed class SqliteCheckpointArchive(ProfileStore store):IEndingCheckpointArchive
    {
        private readonly Dictionary<string,ProtectedEndingCheckpoint> _records=[];
        public bool WrongSequence {get;set;}
        public Action? AfterSave {get;set;}
        public ValueTask<ProtectedEndingCheckpoint> SavePreFinalAsync(SessionView expected,long worldSequence,Guid preparationId,CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested();Require(store.Session.Profile==expected.Profile&&store.Session.Branch==expected.Branch&&store.Session.Load==expected.Load&&Done(store.SnapshotAsync()).Sequence==worldSequence);
            string id=Done(store.CreateCheckpointAsync("protected",protectedId:"pre-final:"+preparationId));var checkpoint=new ProtectedEndingCheckpoint(id,"protected:pre-final:"+preparationId,expected.Profile,expected.Branch,expected.Load,expected.ReleaseId,worldSequence+(WrongSequence?1:0));_records[id]=checkpoint;AfterSave?.Invoke();return ValueTask.FromResult(checkpoint);
        }
        public ValueTask<ProtectedEndingCheckpoint?> FindAsync(string checkpointId,CancellationToken cancellationToken=default)
        {cancellationToken.ThrowIfCancellationRequested();return ValueTask.FromResult(_records.TryGetValue(checkpointId,out var result)&&Done(store.SlotsAsync()).Values.Contains(checkpointId)?result:null);}
    }
    private sealed class InterceptRepository(ProfileStore store,Action? before=null,bool unknownOnce=false):IAdventureRepository
    {
        private bool _first=true;public List<string> FinalRequests {get;}=[];public SessionView Session=>store.Session;
        public ValueTask<BranchSnapshot> SnapshotAsync(CancellationToken cancellationToken=default)=>store.SnapshotAsync(cancellationToken);
        public ValueTask<DraftView?> GetDraftAsync(string taskId)=>store.GetDraftAsync(taskId);
        public ValueTask EnsureInventorySchemaAsync()=>store.EnsureInventorySchemaAsync();
        public ValueTask<DraftSaved> SaveAsync(SaveDraft command,CancellationToken cancellationToken=default)=>store.SaveAsync(command,cancellationToken);
        public ValueTask<WorldReceipt?> FindReceiptAsync(BranchId branch,CommandId command,CancellationToken cancellationToken=default)=>store.FindReceiptAsync(branch,command,cancellationToken);
        public async ValueTask<CommandResult> ExecuteAsync(IWorldCommand command,CancellationToken cancellationToken=default)
        {
            bool final=command is CommitPlanCommand c&&c.Plan.ActionType=="final_policy_execution";if(final)FinalRequests.Add(StrictJson.Serialize(command is CommitPlanCommand p?p:throw new InvalidOperationException()));bool first=final&&_first;
            if(first){_first=false;before?.Invoke();}var result=await store.ExecuteAsync(command,cancellationToken);return first&&unknownOnce&&result.Status==CommitStatus.Committed?new(CommitStatus.OutcomeUnknown,null,[]):result;
        }
    }
    private static WorldPreparation Pure()=>new("preparation-d3-v1",Technical,true,Protocol,EndingRules.Towers.Select(t=>new PreparedTower(t,true,true,true,Technical,true,Technical,true,Protocol,[])).ToImmutableArray());
    private static ImmutableArray<ReleaseRequirement> Plan(bool online=false)=>ReleaseEvidenceInspection.ReadFrozenPlan(File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../planning/plan.json"))),online);
    private static VerifiedReleaseEvidence Machine(ReleaseRequirement r)=>new(r.Id,"accepted","fixture-release","fixture-source",new('a',64),EvidenceTrust.TrustedMachineExecution);
    public static void Register()
    {
        Add("CX-074","All 32 combinations of five distributed conditions leave the two baseline directions available",()=>
        {
            for(int flags=0;flags<32;flags++)
            {
                var p=Pure() with{TechnicalVerified=(flags&1)!=0,Towers=Pure().Towers.Select(t=>t with{EnhancedVersion=(flags&2)!=0?Technical:null,Transition=(flags&4)!=0,Agreement=(flags&8)!=0,TrialVersion=(flags&16)!=0?Technical:null}).ToImmutableArray()};
                var view=EndingRules.Evaluate(p,true,true);Require(view.BasePrepared&&view.Options.Where(o=>o.Choice!=EndingChoice.Distributed).All(o=>o.Executable));Require(view.Options.Single(o=>o.Choice==EndingChoice.Distributed).Executable==(flags==31));
                Require(EndingRules.Evaluate(p,false,true).Options.All(o=>!o.Executable)&&EndingRules.Evaluate(p,true,false).Options.All(o=>!o.Executable));
            }
            foreach(var tower in EndingRules.Towers){var unsafeP=Pure() with{Towers=Pure().Towers.Select(t=>t.Tower==tower?t with{Safe=false}:t).ToImmutableArray()};Require(!EndingRules.Evaluate(unsafeP,true,true).BasePrepared);}
            Require(!EndingRules.Evaluate(Pure() with{Towers=Pure().Towers.SetItem(5,Pure().Towers[0])},true,true).BasePrepared);
        });
        Add("CX-074","Preparation facts remain unpublished before actual Q404 closure; chapter labels and arbitrary receipts do not unlock",()=>
        {
            using var s=Store("unpublished");var service=new FinalReadinessService(s,Catalog);Fixture(s,"Q-404:resolution","explicit_offline_ending_fixture",new{chapter=4,completed=true});var view=Done(service.ViewAsync());Require(!view.PublicPreparationVisible&&view.Options is null);Active(s,"Q-404");Require(!Done(service.ViewAsync()).PublicPreparationVisible);
        });
        Add("CX-074","Actual six-tower baseline work producer and installed supplies satisfy base readiness without enhancement",()=>
        {
            using var s=Store("actual-baseline-producer");Closed(s,"Q-404");var work=new WorkOrderService(s,Catalog);
            foreach(string tower in EndingRules.Towers)
            {
                string region=tower[4..];var r=Fixture(s,"fixture:actual-safety:"+tower,"tower_safety_verified",new TowerSafety(tower,true,"explicit fixture early tower action"));Success(Done(work.RegisterSafetyAsync(r.Receipt!.Id.Value.ToString())));
                var a=new WorkTeamAuthority("fixture-work-team-"+region,region,true,["fixture-work-operator-"+region+"-1","fixture-work-operator-"+region+"-2"],true);var ar=Fixture(s,"fixture:actual-team:"+tower,"engineering_team_verified",a);Set(s,"engineering-team:"+a.Id,"engineering_team",new QualifiedWorkTeam(a.Id,a.Region,true,a.Operators,true,ar.Receipt!.Id.Value.ToString()));
                foreach(var source in work.Rules.Sources.Values.Where(p=>p.Id.StartsWith("BASE-"+tower+"-",StringComparison.Ordinal)))for(int step=1;step<=source.RequiredSteps;step++){var receipt=Fixture(s,"fixture:actual-supply:"+source.Id+":"+step,"engineering_procurement_step_verified",new ProcurementStep(source.Id,step,source.Region,"explicit fixture procurement operation"));Success(Done(work.ConsumeProcurementStepAsync(receipt.Receipt!.Id.Value.ToString())));}
                Success(Done(work.ReserveAsync(tower,WorkType.BaselineCheck,"baseline-fixture-v1",a.Id)));for(int step=0;step<4;step++)Success(Done(work.NextStepAsync(tower,WorkType.BaselineCheck)));
            }
            var view=Done(new FinalReadinessService(s,Catalog).ViewAsync());Require(view.Options!.BasePrepared&&view.Options!.Options.Take(2).All(o=>o.Prepared)&&!view.Options!.Options[2].Prepared&&view.Options!.Options.All(o=>!o.Executable));Require(Done(s.SnapshotAsync()).Entities.Count(e=>e.Kind=="operator_duty")==12);
        });
        Add("CX-074","Current tower readiness cannot replace scoped verified work, installed quantities or actual local duties",()=>
        {
            using var s=Store("tower-proof");Prepare(s);var service=new FinalReadinessService(s,Catalog);Require(Done(service.ViewAsync()).Options!.BasePrepared);
            const string tower="TWR-R2";string key=WorkOrderService.Preparation+":"+tower+":"+WorkType.BaselineCheck;var original=Read<TowerWork>(s,"work:"+key)!;Set(s,"work:"+key,"work_order",original with{Operators=["invented-one","invented-two"]});Require(!Done(service.ViewAsync()).Options!.BasePrepared);
            Set(s,"work:"+key,"work_order",original);var snapshot=Done(s.SnapshotAsync());string account=EngineeringRules.Installed(tower,original.Resources.Keys.First());Fixture(s,"fixture:missing-installed","explicit_offline_ending_fixture",new{},balances:[new(account,-snapshot.Balances[account])]);Require(!Done(service.ViewAsync()).Options!.BasePrepared);
        });
        Add("CX-074","Current version and appointment instances independently invalidate distributed eligibility without blocking baseline",()=>
        {
            using var s=Store("enhanced-proofs");Prepare(s);var service=new FinalReadinessService(s,Catalog);Require(Done(service.ViewAsync()).Options!.Options[2].Prepared);
            var ready=Read<TowerReadiness>(s,"readiness:TWR-R5")!;Set(s,"readiness:TWR-R5","tower_readiness",ready with{TrialVersion="obsolete"});var view=Done(service.ViewAsync());Require(view.Options!.BasePrepared&&!view.Options!.Options[2].Prepared&&view.Options!.Options[2].Missing.Contains("TWR-R5:current_field_trial"));Set(s,"readiness:TWR-R5","tower_readiness",ready);
            var old=Read<AppointmentHistory>(s,"mandate:R3:Operator")!;var appointment=old.Current with{Instance="fixture-new-operator-r3",Holder="fixture-new-holder",SourceReceipt="fixture"};var r=Fixture(s,"fixture:replacement-r3","regional_appointment_verified",appointment);Success(Done(new FactionService(s,Catalog).RegisterAppointmentAsync(r.Receipt!.Id.Value.ToString())));
            view=Done(service.ViewAsync());Require(view.Options!.BasePrepared&&!view.Options!.Options[2].Prepared&&view.Options!.Options[2].Missing.Contains("TWR-R3:current_operator_and_representative_agreement"));
        });
        Add("CX-074","Departure requires actual transferred vessel instance and current border work; civil ferry or task labels grant no ownership",()=>
        {
            using var s=Store("vessel");Prepare(s,false);Departure(s);var service=new FinalReadinessService(s,Catalog);Require(Done(service.ViewAsync()).DepartureMissing.Length==0);
            var original=Read<SharedVessel>(s,"ship:glimmer")!;Set(s,"ship:glimmer","shared_vessel",original with{Instance=Guid.NewGuid()});Require(Done(service.ViewAsync()).DepartureMissing.Contains("actual_glimmer_use_transfer"));Set(s,"ship:glimmer","shared_vessel",original);
            var q603=Read<QuestState>(s,"quest:Q-603")!;Set(s,"quest:Q-603","quest",q603 with{Instance=Guid.NewGuid()});Require(Done(service.ViewAsync()).DepartureMissing.Contains("actual_closed_Q603"));
        });
        Add("CX-074","Unaccepted optional work does not block departure; accepted onsite work requires safe handover or explicit suspension",()=>
        {
            using var s=Store("risk");Prepare(s,false);Departure(s);var archive=new SqliteCheckpointArchive(s);var service=new FinalReadinessService(s,Catalog,archive);
            var commitment=new HighRiskCommitment("risk:fixture",false,true,false,false);Set(s,commitment.Id,"high_risk_commitment",commitment);RiskRegister(s);Require(Done(service.ViewAsync()).DepartureMissing.Length==0);
            Set(s,commitment.Id,"high_risk_commitment",commitment with{Accepted=true});RiskRegister(s);Throws<InvalidOperationException>(()=>Done(service.SealPreparationAsync()));
            Set(s,commitment.Id,"high_risk_commitment",commitment with{Accepted=true,ExplicitlySuspended=true});RiskRegister(s);Success(Done(service.SealPreparationAsync()));Require(Read<QuestState>(s,"quest:Q-604")!.Lifecycle==QuestLifecycle.Active);
        });
        Add("CX-074","Cargo in transit and an incomplete current risk register are explicit departure blockers",()=>
        {
            using var s=Store("cargo");Prepare(s,false);Departure(s);var service=new FinalReadinessService(s,Catalog,new SqliteCheckpointArchive(s));Set(s,"transport:fixture","transport_segment",new TransportSegment("transport:fixture","R1","R6",true,false));
            var view=Done(service.ViewAsync());Require(view.DepartureMissing.Contains("current_high_risk_register_incomplete")&&view.DepartureMissing.Contains("actual_cargo_handover:transport:fixture"));RiskRegister(s);Require(Done(service.ViewAsync()).DepartureMissing.Contains("actual_cargo_handover:transport:fixture"));
            Set(s,"transport:fixture","transport_segment",new TransportSegment("transport:fixture","R1","R6",false,true));RiskRegister(s);Success(Done(service.SealPreparationAsync()));
        });
        Add("CX-074","Sealing requires a genuine protected checkpoint, locks installed quantities and performs no narrative closure or reward",()=>
        {
            using var s=Store("seal");Prepare(s);Departure(s);Throws<InvalidOperationException>(()=>Done(new FinalReadinessService(s,Catalog).SealPreparationAsync()));var archive=new SqliteCheckpointArchive(s);var service=new FinalReadinessService(s,Catalog,archive);var before=Done(s.SnapshotAsync());Success(Done(service.SealPreparationAsync()));
            var frozen=Done(service.ViewAsync()).Frozen!;Require(Done(s.SlotsAsync()).Values.Contains(frozen.PreFinalCheckpoint)&&frozen.ReservedResources.Count>0);var after=Done(s.SnapshotAsync());Require(after.Balances.GetValueOrDefault("experience")==before.Balances.GetValueOrDefault("experience")&&after.Balances.GetValueOrDefault("marks")==before.Balances.GetValueOrDefault("marks"));Require(after.Balances.Where(p=>p.Key.StartsWith("engineering:installed:",StringComparison.Ordinal)).All(p=>p.Value==0)&&frozen.ReservedResources.All(p=>after.Balances[p.Key]==p.Value));Require(Read<QuestState>(s,"quest:Q-604")!.Lifecycle==QuestLifecycle.Active&&Done(service.SnapshotAsync()) is null);long sequence=after.Sequence;Require(Done(service.SealPreparationAsync()).Status==CommitStatus.AlreadyCommitted&&Done(s.SnapshotAsync()).Sequence==sequence);
        });
        Add("CX-074","Checkpoint metadata mismatch or concurrent world change cannot commit preparation",()=>
        {
            using var s=Store("checkpoint-provenance");Prepare(s,false);Departure(s);var archive=new SqliteCheckpointArchive(s){WrongSequence=true};var service=new FinalReadinessService(s,Catalog,archive);Throws<InvalidOperationException>(()=>Done(service.SealPreparationAsync()));Require(Read<FrozenPreparation>(s,"final:preparation") is null);
            archive.WrongSequence=false;archive.AfterSave=()=>Set(s,"fixture:world-change","fixture",new{changed=true});Throws<StoreConflictException>(()=>Done(service.SealPreparationAsync()));Require(Read<FrozenPreparation>(s,"final:preparation") is null);
        });
        Add("CX-074","Central stabilization supplies safety only and never selects policy, fabricates final activity or grants a reward",()=>
        {
            using var s=Store("central");Prepare(s,false);Departure(s);var service=new FinalReadinessService(s,Catalog,new SqliteCheckpointArchive(s));Success(Done(service.SealPreparationAsync()));FinalScene(s);var before=Done(s.SnapshotAsync());Require(Done(service.ViewAsync()).Options!.Options.Take(2).All(o=>o.Executable)&&Done(service.SnapshotAsync()) is null&&Read<FinalPolicyProposal>(s,"proposal:Q-F04") is null);Require(!Done(service.PreviewAsync()).Ready&&Done(s.SnapshotAsync()).Sequence==before.Sequence);
            var state=Read<CentralSafetyState>(s,"final:central-safety")!;Set(s,"final:central-safety","central_safety",state with{Configuration=state.Configuration+1});Require(Done(service.ViewAsync()).Options!.Options.All(o=>!o.Executable));Throws<InvalidOperationException>(()=>Done(service.ConfirmPolicyAsync(EndingChoice.Central,true)));
        });
        Add("CX-074","Policy confirmation requires current actual final control; prose changes invalidate an old action preview",()=>
        {
            using var s=Store("draft");Prepare(s,false);Departure(s);var service=new FinalReadinessService(s,Catalog,new SqliteCheckpointArchive(s));Success(Done(service.SealPreparationAsync()));FinalScene(s);Throws<InvalidOperationException>(()=>Done(service.ConfirmPolicyAsync(EndingChoice.Central,false)));
            Set(s,"actor","actor",new ActorState("RF-S06",new(50,40),11));Throws<InvalidOperationException>(()=>Done(service.ConfirmPolicyAsync(EndingChoice.Central,true)));Set(s,"actor","actor",new ActorState("RF-S07",new(50,40),12));
            var session=s.Session;Done(s.SaveAsync(new(session.Profile,session.Branch,session.Load,FinalReadinessService.DraftTask,null,0,"I confirm the central direction.","fixture-command-v1")));Success(Done(service.ConfirmPolicyAsync(EndingChoice.Central,false)));var preview=Done(service.PreviewAsync());Require(preview.Ready);var head=Done(s.GetDraftAsync(FinalReadinessService.DraftTask))!;
            Done(s.SaveAsync(new(session.Profile,session.Branch,session.Load,FinalReadinessService.DraftTask,head.Id,head.Revision,"I revised my draft before execution.","fixture-command-v1")));Require(Done(service.ConfirmAsync(preview.Token)).Status==CommitStatus.Rejected&&Done(service.SnapshotAsync()) is null&&!Done(service.PreviewAsync()).Ready);
        });
        Add("CX-074","All three actual SQLite ending commits award once, preserve responsibility and world history, and replay epilogues without assessment",()=>
        {
            foreach(var choice in Enum.GetValues<EndingChoice>())
            {
                using var s=Store("three-"+choice);Prepare(s);Departure(s);Set(s,"history:breach-repair","relationship",new{breach="fixture confirmed breach",repair="fixture accepted repair",stance="opposed",both_retained=true});
                Set(s,"fixture:learning-and-help","fixture",new{band=(decimal?)null,help_level="L4",subtitle=true,translation=true,difficulty="low",companions=Array.Empty<string>(),private_arcs_done=0});var archive=new SqliteCheckpointArchive(s);var service=new FinalReadinessService(s,Catalog,archive);Success(Done(service.SealPreparationAsync()));FinalScene(s);Success(Done(service.ConfirmPolicyAsync(choice,true)));var before=Done(s.SnapshotAsync());var preview=Done(service.PreviewAsync());Require(preview.Ready&&preview.LanguageAssessment=="unassessed");Success(Done(service.ConfirmAsync(preview.Token)));Require(Done(service.ConfirmAsync(preview.Token)).Status==CommitStatus.AlreadyCommitted);
                var after=Done(s.SnapshotAsync());var ending=Done(service.SnapshotAsync())!;Require(ending.Choice==choice&&ending.EndingId==EndingRules.Id(choice)&&ending.Branch==s.Session.Branch&&ending.History.Any(h=>h.Id=="history:breach-repair")&&ending.History.Any(h=>h.Id=="Q-F03:resolution"));Require(after.Balances.GetValueOrDefault("experience")-before.Balances.GetValueOrDefault("experience")==600&&after.Balances.GetValueOrDefault("marks")-before.Balances.GetValueOrDefault("marks")==200&&after.Balances.GetValueOrDefault("parts")==before.Balances.GetValueOrDefault("parts")&&after.Items.SequenceEqual(before.Items));
                long seq=after.Sequence;var presentation=Done(service.PresentAsync())!;Require(presentation.Modules.Contains("history:responsibility_retained")&&presentation.Modules.Contains("world:natural_fog_remains")&&!presentation.LanguageEvaluated&&presentation.Band is null&&!presentation.LearningHistoryReset);Done(service.PresentAsync());Require(Done(s.SnapshotAsync()).Sequence==seq);
            }
        });
        Add("CX-074","Frozen agreements survive irrelevant later preferences; sealed material depletion is caught atomically even after preview",()=>
        {
            using var s=Store("sealed-atomic");Prepare(s);Departure(s);var service=new FinalReadinessService(s,Catalog,new SqliteCheckpointArchive(s));Success(Done(service.SealPreparationAsync()));FinalScene(s);
            Set(s,"agreement:R1","regional_agreement",new RegionalAgreement("R1",Protocol,null,null,[]));Set(s,"fixture:opinion","fixture",new{stance="oppose",help="L4",gift=10000});Require(Done(service.ViewAsync()).Options!.Options[2].Executable);Success(Done(service.ConfirmPolicyAsync(EndingChoice.Distributed,true)));var preview=Done(service.PreviewAsync());Require(preview.Ready);var frozen=Read<FrozenPreparation>(s,"final:preparation")!;var quantity=frozen.ReservedResources.First();Fixture(s,"fixture:illegal-sealed-depletion","explicit_offline_ending_fixture",new{scope="intentional invalid external mutation fixture"},balances:[new(quantity.Key,-quantity.Value)]);
            var before=Done(s.SnapshotAsync());Require(Done(service.ConfirmAsync(preview.Token)).Status==CommitStatus.Rejected&&Done(service.SnapshotAsync()) is null&&Read<QuestState>(s,"quest:Q-F04")!.Lifecycle==QuestLifecycle.Active);Require(Done(s.SnapshotAsync()).Balances.GetValueOrDefault("experience")==before.Balances.GetValueOrDefault("experience"));
        });
        Add("CX-074","Revoked actual control between preview and transaction rejects the ending without any partial reward",()=>
        {
            using var s=Store("revoke");Prepare(s,false);Departure(s);var archive=new SqliteCheckpointArchive(s);var hooked=new InterceptRepository(s,()=>{var access=Read<FinalControlAccess>(s,"final:control-access")!;Set(s,"final:control-access","final_control_access",access with{Allowed=false});});var service=new FinalReadinessService(hooked,Catalog,archive);Success(Done(service.SealPreparationAsync()));FinalScene(s);Success(Done(service.ConfirmPolicyAsync(EndingChoice.Island,true)));var preview=Done(service.PreviewAsync());Require(preview.Ready);long xp=Done(s.SnapshotAsync()).Balances.GetValueOrDefault("experience");Require(Done(service.ConfirmAsync(preview.Token)).Status==CommitStatus.Rejected&&Done(service.SnapshotAsync()) is null&&Done(s.SnapshotAsync()).Balances.GetValueOrDefault("experience")==xp);
        });
        Add("CX-074","Unknown commit outcome retries the identical command and resolves one fixed snapshot and reward",()=>
        {
            using var s=Store("unknown");Prepare(s,false);Departure(s);var hooked=new InterceptRepository(s,unknownOnce:true);var service=new FinalReadinessService(hooked,Catalog,new SqliteCheckpointArchive(s));Success(Done(service.SealPreparationAsync()));FinalScene(s);Success(Done(service.ConfirmPolicyAsync(EndingChoice.Central,true)));var preview=Done(service.PreviewAsync());Require(Done(service.ConfirmAsync(preview.Token)).Status==CommitStatus.OutcomeUnknown);var ending=Done(service.SnapshotAsync())!;Require(Done(service.ConfirmAsync(preview.Token)).Status==CommitStatus.AlreadyCommitted&&hooked.FinalRequests.Count==2&&hooked.FinalRequests[0]==hooked.FinalRequests[1]&&Done(service.SnapshotAsync())!.Id==ending.Id&&Done(s.SnapshotAsync()).Balances.GetValueOrDefault("experience")==600);
        });
        Add("CX-074","Prefinal load starts a different adventure outcome while the original ending remains immutable and old preview is rejected",()=>
        {
            using var s=Store("branch");Prepare(s);Departure(s);var archive=new SqliteCheckpointArchive(s);var service=new FinalReadinessService(s,Catalog,archive);Success(Done(service.SealPreparationAsync()));var beforeFinal=Read<FrozenPreparation>(s,"final:preparation")!;FinalScene(s);Success(Done(service.ConfirmPolicyAsync(EndingChoice.Central,true)));var preview=Done(service.PreviewAsync());Success(Done(service.ConfirmAsync(preview.Token)));var original=Done(service.SnapshotAsync())!;Done(s.ObserveAsync(new("fixture-observation","fixture-family","fixture-attempt",InputModality.StructuredChoice,true,true,false,false,[],"explicit-offline-fixture",null)));string endingCheckpoint=Done(s.CreateCheckpointAsync("protected",protectedId:"fixture-original-ending"));
            Done(s.LoadCheckpointAsync(beforeFinal.PreFinalCheckpoint));Require(s.Session.Branch!=original.Branch&&Done(service.SnapshotAsync()) is null&&Read<FrozenPreparation>(s,"final:preparation") is null&&Done(service.ConfirmAsync(preview.Token)).Status==CommitStatus.Rejected&&Done(s.ObservationCountAsync())==1);Success(Done(service.SealPreparationAsync()));FinalScene(s);Success(Done(service.ConfirmPolicyAsync(EndingChoice.Island,true)));var otherPreview=Done(service.PreviewAsync());Success(Done(service.ConfirmAsync(otherPreview.Token)));var other=Done(service.SnapshotAsync())!;Require(other.Id!=original.Id&&other.Choice==EndingChoice.Island&&other.Branch!=original.Branch&&Done(s.ObservationCountAsync())==1);
            Done(s.LoadCheckpointAsync(endingCheckpoint));Require(StrictJson.Serialize(Done(service.SnapshotAsync()))==StrictJson.Serialize(original)&&Done(s.SnapshotAsync()).Balances.GetValueOrDefault("experience")==600);
        });
        Add("CX-079","Frozen planning graph keeps default inspection blocked and optional online provider work explicitly disabled",()=>
        {
            var plan=Plan();Require(plan.Any(r=>r.Id=="CP-RF-06")&&plan.Any(r=>r.Id=="HV-007")&&plan.Any(r=>r.Id=="G7"));var status=Done(new ReleaseEvidenceInspection(plan).InspectAsync(Catalog.ReleaseId,"actual-source-unapproved"));Require(!status.ReadyForLocalFormalHandoff&&!status.ExternalPublicationAuthorized&&!status.OfficialExamCertification&&status.Requirements.Single(r=>r.Id=="CX-069").State==ReadinessState.OptionalDisabled&&status.Requirements.Single(r=>r.Id=="HV-008").State==ReadinessState.OptionalDisabled&&status.Requirements.Single(r=>r.Id=="G7").State==ReadinessState.Blocked);
        });
        Add("CX-079","All engineering evidence cannot impersonate audio, rights, calibration or human project approval",()=>
        {
            var plan=Plan();var engineering=plan.Where(r=>r.Kind!="gate").Select(Machine).ToImmutableArray();var status=ReleaseGateRules.Evaluate("fixture-release","fixture-source",plan,engineering);Require(!status.ReadyForLocalFormalHandoff&&status.Requirements.Single(r=>r.Id=="G6").State==ReadinessState.Blocked);foreach(string id in new[]{"CP-RF-06","HV-003","HV-004","HV-005","HV-006","HV-007"})Require(status.Requirements.Single(r=>r.Id==id).State==ReadinessState.Blocked);
            foreach(var trust in new[]{EvidenceTrust.IntegrityOnly,EvidenceTrust.ExplicitOfflineFixture,EvidenceTrust.Unknown}){var untrusted=engineering.Select(e=>e with{Trust=trust});Require(ReleaseGateRules.Evaluate("fixture-release","fixture-source",plan,untrusted).Requirements.Single(r=>r.Id=="CX-001").State==ReadinessState.Blocked);}
        });
        Add("CX-079","Versioned evidence, dependency cycles and duplicate approvals fail closed; local approval never authorizes external publication",()=>
        {
            ImmutableArray<ReleaseRequirement> simple=[new("CX","engineering",[]),new("HV-007","human_verification",["CX"]),new("G7","gate",["HV-007"])];var machine=Machine(simple[0]);var human=new VerifiedReleaseEvidence("HV-007","accepted","fixture-release","fixture-source",new('b',64),EvidenceTrust.TrustedProjectApproval);
            Require(!ReleaseGateRules.Evaluate("other-release","fixture-source",simple,[machine,human]).ReadyForLocalFormalHandoff&&!ReleaseGateRules.Evaluate("fixture-release","changed-source",simple,[machine,human]).ReadyForLocalFormalHandoff&&!ReleaseGateRules.Evaluate("fixture-release","fixture-source",simple,[machine,human,human]).ReadyForLocalFormalHandoff);
            // This is a rule-unit hypothetical authority input, not an actual project approval.
            var ready=ReleaseGateRules.Evaluate("fixture-release","fixture-source",simple,[machine,human]);Require(ready.ReadyForLocalFormalHandoff&&!ready.ExternalPublicationAuthorized&&!ready.OfficialExamCertification);
            var cyclic=ReleaseGateRules.Evaluate("fixture-release","fixture-source",[new("A","gate",["B"]),new("B","gate",["A"])],[]);Require(cyclic.Requirements.All(r=>r.State==ReadinessState.Blocked));
        });
        Add("CX-079","Enabling online feature adds both actual supplier and human provider verification to release dependencies",()=>
        {
            var off=Plan();var on=Plan(true);Require(!off.Single(r=>r.Id=="G6").Dependencies.Contains("HV-008")&&on.Single(r=>r.Id=="G6").Dependencies.Contains("HV-008")&&on.Single(r=>r.Id=="G6").Dependencies.Contains("CX-069"));var status=ReleaseGateRules.Evaluate("fixture-release","fixture-source",on,[],true);Require(status.Requirements.Single(r=>r.Id=="HV-008").State==ReadinessState.Blocked&&status.Requirements.Single(r=>r.Id=="CX-069").State==ReadinessState.Blocked&&!status.ReadyForLocalFormalHandoff);
        });
        Add("CX-075","Performance distributions require full metadata and never turn scripts or offline fixture durations into native FPS",()=>
        {
            var machine=new PerformanceMachine("explicit fixture Windows","explicit fixture CPU","explicit fixture GPU",16384,"explicit fixture SSD");var series=new PerformanceSeries("fixture-build","fixture-release","frame_time","milliseconds",machine,PerformanceOrigin.ExplicitOfflineFixture,Enumerable.Range(1,100).Select(i=>(double)i).Reverse().ToImmutableArray());var summary=PerformanceEvidenceRules.Summarize(series);Require(summary.Count==100&&summary.Minimum==1&&summary.Median==50.5&&summary.P95==95&&summary.Maximum==100&&!summary.NativeMeasurement&&summary.Limitation is not null);
            Throws<InvalidDataException>(()=>PerformanceEvidenceRules.Summarize(series with{Origin=PerformanceOrigin.ShellScriptDuration}));Throws<InvalidDataException>(()=>PerformanceEvidenceRules.Summarize(series with{Samples=[double.NaN]}));Throws<InvalidDataException>(()=>PerformanceEvidenceRules.Summarize(series with{Machine=machine with{Gpu=""}}));Throws<InvalidDataException>(()=>PerformanceEvidenceRules.Summarize(series with{Metric="fps",Unit="seconds"}));
        });
        Add("CX-077","Package structure omits SDK/Python requirements and rejects credentials, author answers, holdouts and player saves",()=>
        {
            var allowed=new[]{PackageEntryKind.Executable,PackageEntryKind.ResourcePack,PackageEntryKind.DotnetRuntime,PackageEntryKind.SQLiteNative,PackageEntryKind.ContentRelease,PackageEntryKind.Dictionary,PackageEntryKind.Translation,PackageEntryKind.Audio,PackageEntryKind.License,PackageEntryKind.Manifest}.Select(k=>new PackageEntry("fixture/"+k,k,new('a',64),100)).ToImmutableArray();var check=PackagePreflightRules.Check(allowed,false,false);Require(check.StructurallyAllowed&&!check.FormalReleaseApproved&&check.ActualArtifactAuditStillRequired);
            foreach(var kind in new[]{PackageEntryKind.PrivateKey,PackageEntryKind.ProviderCredential,PackageEntryKind.AuthorAnswer,PackageEntryKind.CalibrationHoldout,PackageEntryKind.ProfileSave,PackageEntryKind.TestFixture})Require(!PackagePreflightRules.Check(allowed.Add(new("fixture/forbidden-"+kind,kind,new('b',64),10)),false,false).StructurallyAllowed);
            Require(!PackagePreflightRules.Check(allowed,true,false).StructurallyAllowed&&!PackagePreflightRules.Check(allowed,false,true).StructurallyAllowed&&!PackagePreflightRules.Check(allowed.Add(new("../escape",PackageEntryKind.Manifest,new('a',64),10)),false,false).StructurallyAllowed&&!PackagePreflightRules.Check(allowed.Add(allowed[0] with{Path=allowed[0].Path.ToUpperInvariant()}),false,false).StructurallyAllowed&&!PackagePreflightRules.Check(allowed.Add(new("fixture/CON.dll",PackageEntryKind.Manifest,new('a',64),10)),false,false).StructurallyAllowed&&!PackagePreflightRules.Check(allowed.Add(new("fixture/Manifest.",PackageEntryKind.Manifest,new('a',64),10)),false,false).StructurallyAllowed);
        });
        Add("CX-078","Thirty-two registered tasks and offline fixture evidence do not constitute a played release route",()=>
        {
            Require(Catalog.Quests.Length==32);var plan=Plan();var fixtures=plan.Where(r=>r.Kind!="gate").Select(r=>Machine(r) with{Trust=EvidenceTrust.ExplicitOfflineFixture});var result=ReleaseGateRules.Evaluate("fixture-release","fixture-source",plan,fixtures);
            Require(result.Requirements.Single(r=>r.Id=="CX-078").State==ReadinessState.Blocked&&result.Requirements.Single(r=>r.Id=="G5").State==ReadinessState.Blocked&&!result.ReadyForLocalFormalHandoff);
        });
        Add("CX-080","Unapproved local candidate cannot be promoted to formal handoff or external publication by readiness inspection",()=>
        {
            var result=Done(new ReleaseEvidenceInspection(Plan()).InspectAsync(Catalog.ReleaseId,"actual-source-unapproved"));Require(result.Requirements.Single(r=>r.Id=="CX-080").State==ReadinessState.Blocked&&!result.ReadyForLocalFormalHandoff&&!result.ExternalPublicationAuthorized&&!result.OfficialExamCertification);Catalog.Target.Validate();Require(Catalog.Target.Speaking is null&&!Catalog.Target.AutoRaise);
        });
    }
}

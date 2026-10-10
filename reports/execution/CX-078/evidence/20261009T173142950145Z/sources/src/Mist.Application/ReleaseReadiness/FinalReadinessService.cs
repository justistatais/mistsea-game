using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Quests;
using Mist.Core.ReleaseReadiness;

namespace Mist.Application.ReleaseReadiness;

// This port must persist a real protected world checkpoint and report its actual provenance.
// No disabled/default adapter claims a checkpoint exists. It does not save or rewrite learning.
public interface IEndingCheckpointArchive
{
    ValueTask<ProtectedEndingCheckpoint> SavePreFinalAsync(SessionView expected,long worldSequence,Guid preparationId,CancellationToken cancellationToken=default);
    ValueTask<ProtectedEndingCheckpoint?> FindAsync(string checkpointId,CancellationToken cancellationToken=default);
}
public sealed record FinalReadinessView(bool PublicPreparationVisible,EndingRulesView? Options,ImmutableArray<string> DepartureMissing,FrozenPreparation? Frozen,FinalEndingSnapshot? Ending);
public sealed record FinalActionPreview(Guid Token,bool Ready,ImmutableArray<string> Missing,EndingChoice? Choice,string LanguageAssessment="unassessed");

// This is an engineering consumer, not a campaign completion producer. The real Q104/Q404,
// Q602/Q603/Q604, central stability, risk registry and final control producers must supply scoped
// receipts. Structured choice confirms an action; it makes no free-English understanding claim.
public sealed class FinalReadinessService(IAdventureRepository repository,RuntimeCatalog catalog,IEndingCheckpointArchive? checkpoints=null)
{
    public const string DraftTask="Q-F04:final-command";
    private readonly WorldPreparationProjection _projection=new(catalog);
    private readonly Dictionary<Guid,CommitPlanCommand> _previews=[];
    private const string FrozenId="final:preparation",PolicyId="proposal:Q-F04",EndingId="final:ending";
    private static T? Read<T>(BranchSnapshot s,string id) where T:class=>WorldPreparationProjection.Read<T>(s,id);
    private static QuestState? Quest(BranchSnapshot s,string id)=>WorldPreparationProjection.Quest(s,id);
    private static bool Active(BranchSnapshot s,string id)=>Quest(s,id) is{} q&&q.Id==id&&q.Instance!=Guid.Empty&&q.Lifecycle==QuestLifecycle.Active&&q.Outcome is null;
    private static StoredReceipt? Receipt(BranchSnapshot s,string key,string action)=>s.Receipts.SingleOrDefault(r=>r.SemanticKey==key&&r.ActionType==action);
    private static bool Same<T>(T a,T b)=>StrictJson.Serialize(a)==StrictJson.Serialize(b);
    private static string[] RelevantReads(BranchSnapshot s)=>s.Entities.Where(e=>e.Kind is "tower_readiness" or "work_order" or "engineering_team" or "operator_duty" or "technical_package" or "regional_agreement" or "regional_appointment" or "high_risk_commitment" or "transport_segment")
        .Select(e=>e.Id).Concat(new[]{"quest:Q-104","quest:Q-404","quest:Q-602","quest:Q-603","quest:Q-604","quest:Q-F03","quest:Q-F04","ship:glimmer","final:commitments",FrozenId,PolicyId,EndingId,"final:central-safety","final:control-access","actor"})
        .Concat(EndingRules.Towers.SelectMany(t=>new[]{"readiness:"+t,"agreement:"+t[4..],"mandate:"+t[4..]+":Operator","mandate:"+t[4..]+":Representative"})).Distinct(StringComparer.Ordinal).ToArray();
    private static ImmutableArray<string> RiskMissing(BranchSnapshot s)
    {
        var missing=ImmutableArray.CreateBuilder<string>();var register=Read<CurrentRiskRegister>(s,"final:commitments");
        if(register is null||register.Version<0||register.CommitmentIds.IsDefault||register.TransportIds.IsDefault||register.CommitmentIds.Distinct().Count()!=register.CommitmentIds.Length||register.TransportIds.Distinct().Count()!=register.TransportIds.Length){missing.Add("verified_current_high_risk_register");return missing.ToImmutable();}
        var source=WorldPreparationProjection.Receipt(s,register.SourceReceipt,"high_risk_register_verified");
        if(source is null||!Same(StrictJson.Deserialize<RiskRegisterVerification>(source.ResultJson),new RiskRegisterVerification(register.Version,register.CommitmentIds,register.TransportIds))){missing.Add("verified_current_high_risk_register");return missing.ToImmutable();}
        var commitments=s.Entities.Where(e=>e.Kind=="high_risk_commitment").ToArray();var transports=s.Entities.Where(e=>e.Kind=="transport_segment").ToArray();
        if(!commitments.Select(e=>e.Id).Order().SequenceEqual(register.CommitmentIds.Order())||!transports.Select(e=>e.Id).Order().SequenceEqual(register.TransportIds.Order()))missing.Add("current_high_risk_register_incomplete");
        foreach(var entity in commitments)
        {
            var c=StrictJson.Deserialize<HighRiskCommitment>(entity.Json);
            if(c.Id!=entity.Id||c.Accepted&&c.OnsiteRequired&&!c.SafelyHandedOver&&!c.ExplicitlySuspended)missing.Add("safe_handover_or_explicit_suspension:"+entity.Id);
        }
        foreach(var entity in transports){var c=StrictJson.Deserialize<TransportSegment>(entity.Json);if(c.Id!=entity.Id||c.CargoInTransit&&!c.Completed)missing.Add("actual_cargo_handover:"+entity.Id);}
        return missing.ToImmutable();
    }
    private static FrozenPreparation? Frozen(BranchSnapshot s)
    {
        var frozen=Read<FrozenPreparation>(s,FrozenId);if(frozen is null)return null;
        var receipt=Receipt(s,"Q-604:preparation-sealed","seal_final_preparation");
        if(frozen.Id==Guid.Empty||frozen.Release!=s.ReleaseId||frozen.Q604Instance!=Quest(s,"Q-604")?.Instance||string.IsNullOrWhiteSpace(frozen.PreFinalCheckpoint)||frozen.ReservedResources.Count==0||receipt is null||!Same(StrictJson.Deserialize<FrozenPreparation>(receipt.ResultJson),frozen))throw new InvalidOperationException("sealed_preparation_provenance_invalid");
        return frozen;
    }
    private static FinalEndingSnapshot? Ending(BranchSnapshot s)
    {
        var ending=Read<FinalEndingSnapshot>(s,EndingId);if(ending is null)return null;var receipt=Receipt(s,"Q-F04:resolution","final_policy_execution");
        if(receipt is null||!Same(StrictJson.Deserialize<FinalEndingSnapshot>(receipt.ResultJson),ending)||ending.QF04Instance!=Quest(s,"Q-F04")?.Instance||Quest(s,"Q-F04")?.Lifecycle!=QuestLifecycle.Closed||ending.EndingId!=EndingRules.Id(ending.Choice))throw new InvalidOperationException("ending_snapshot_provenance_invalid");return ending;
    }
    private static WorldPreparation Prepared(FrozenPreparation frozen,BranchSnapshot s)
    {
        // Materials and authorised appointments are frozen. Current physical tower safety still
        // belongs to the safety producer; central stability is independently checked at Q-F03.
        return frozen.Preparation with{Towers=frozen.Preparation.Towers.Select(t=>t with{Safe=t.Safe&&Read<Mist.Core.Engineering.TowerReadiness>(s,"readiness:"+t.Tower)?.Safe==true}).ToImmutableArray()};
    }
    private ImmutableArray<string> DepartureMissing(BranchSnapshot s,EndingRulesView view)
    {
        var missing=ImmutableArray.CreateBuilder<string>();if(!Active(s,"Q-604"))missing.Add("actual_active_Q604");if(!WorldPreparationProjection.Closed(s,"Q-602"))missing.Add("actual_closed_Q602");if(!WorldPreparationProjection.Closed(s,"Q-603"))missing.Add("actual_closed_Q603");if(WorldPreparationProjection.Vessel(s) is null)missing.Add("actual_glimmer_use_transfer");
        if(!view.BasePrepared)missing.AddRange(view.Options.Single(o=>o.Choice==EndingChoice.Central).Missing.Where(m=>m is not("central_dangerous_program_stabilized" or "actual_final_decision_node")));missing.AddRange(RiskMissing(s));return missing.Distinct().ToImmutableArray();
    }
    public async ValueTask<FinalReadinessView> ViewAsync(CancellationToken cancellationToken=default)
    {
        var s=await repository.SnapshotAsync(cancellationToken);var ending=Ending(s);if(!WorldPreparationProjection.Closed(s,"Q-404"))return new(false,null,["preparation_not_public"],null,ending);
        var frozen=Frozen(s);var preparation=frozen is null?_projection.Project(s):Prepared(frozen,s);var options=EndingRules.Evaluate(preparation,WorldPreparationProjection.CentralSafe(s),Active(s,"Q-F04")&&WorldPreparationProjection.Closed(s,"Q-604"));
        return new(true,options,DepartureMissing(s,options),frozen,ending);
    }
    public static string SealedAccount(Guid preparation,string installedAccount)=>"ending:sealed:"+preparation+":"+installedAccount;
    public async ValueTask<CommandResult> SealPreparationAsync(CancellationToken cancellationToken=default)
    {
        var session=repository.Session;var s=await repository.SnapshotAsync(cancellationToken);BranchOperations.Safe(s);if(Frozen(s) is not null)return new(CommitStatus.AlreadyCommitted,null,[]);
        if(!WorldPreparationProjection.Closed(s,"Q-404"))throw new InvalidOperationException("preparation_not_public");var preparation=_projection.Project(s);var options=EndingRules.Evaluate(preparation,false,false);var missing=DepartureMissing(s,options);if(missing.Length>0)throw new InvalidOperationException(string.Join(',',missing));
        if(checkpoints is null)throw new InvalidOperationException("actual_protected_checkpoint_archive_unavailable");Guid id=Guid.NewGuid();var checkpoint=await checkpoints.SavePreFinalAsync(session,s.Sequence,id,cancellationToken);var persisted=await checkpoints.FindAsync(checkpoint.Id,cancellationToken);
        if(persisted is null||!Same(persisted,checkpoint)||checkpoint.Slot!="protected:pre-final:"+id||checkpoint.Profile!=session.Profile||checkpoint.SourceBranch!=session.Branch||checkpoint.SourceLoad!=session.Load||checkpoint.Release!=session.ReleaseId||checkpoint.WorldSequence!=s.Sequence)throw new InvalidOperationException("actual_prefinal_checkpoint_provenance_mismatch");
        var after=await repository.SnapshotAsync(cancellationToken);if(repository.Session.Profile!=session.Profile||repository.Session.Branch!=session.Branch||repository.Session.Load!=session.Load||after.Sequence!=s.Sequence)throw new StoreConflictException("world_changed_while_preserving_prefinal_checkpoint");
        var installed=s.Balances.Where(p=>p.Key.StartsWith("engineering:installed:",StringComparison.Ordinal)&&p.Value>0).ToArray();var resources=installed.ToImmutableDictionary(p=>SealedAccount(id,p.Key),p=>p.Value,StringComparer.Ordinal);var vessel=WorldPreparationProjection.Vessel(s)!;
        var frozen=new FrozenPreparation(id,session.Branch,session.ReleaseId,Quest(s,"Q-604")!.Instance,checkpoint.Id,preparation,vessel.Instance,resources);
        var balances=installed.SelectMany(p=>new[]{new BalanceChange(p.Key,-p.Value),new BalanceChange(SealedAccount(id,p.Key),p.Value)}).ToImmutableArray();
        return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,"Q-604:preparation-sealed","seal_final_preparation",[BranchOperations.Change(s,FrozenId,"frozen_preparation",frozen)],RelevantReads(s),frozen,balances:balances),cancellationToken);
    }
    private static bool AtCurrentControl(BranchSnapshot s)
    {
        var access=Read<FinalControlAccess>(s,"final:control-access");var actor=Read<ActorState>(s,"actor");var quest=Quest(s,"Q-F04");
        if(access is null||actor is null||quest is null||access.QuestInstance!=quest.Instance||!access.Allowed||access.Scene!="RF-S07"||!double.IsFinite(access.X)||!double.IsFinite(access.Y)||actor.Scene!=access.Scene||actor.Position.Distance(new(access.X,access.Y))>2.5)return false;
        var source=WorldPreparationProjection.Receipt(s,access.SourceReceipt,"final_control_access_verified");return source is not null&&Same((StrictJson.Deserialize<FinalControlAccess>(source.ResultJson) with{SourceReceipt=access.SourceReceipt}),access);
    }
    public async ValueTask<CommandResult> ConfirmPolicyAsync(EndingChoice choice,bool structuredAssistance,CancellationToken cancellationToken=default)
    {
        if(!Enum.IsDefined(choice))throw new InvalidOperationException("unknown_ending");var s=await repository.SnapshotAsync(cancellationToken);BranchOperations.Safe(s);
        if(!Active(s,"Q-F04")||!WorldPreparationProjection.Closed(s,"Q-604")||!WorldPreparationProjection.CentralSafe(s)||!AtCurrentControl(s)||Frozen(s) is null)throw new InvalidOperationException("actual_final_decision_not_available");
        var draft=await repository.GetDraftAsync(DraftTask);if(!structuredAssistance&&(draft is null||draft.Deleted||string.IsNullOrWhiteSpace(draft.Body)))throw new InvalidOperationException("draft_or_explicit_structured_confirmation_required");
        var old=Read<FinalPolicyProposal>(s,PolicyId);var policy=new FinalPolicyProposal(checked((old?.Version??0)+1),Quest(s,"Q-F04")!.Instance,choice,draft?.Hash,structuredAssistance,true);var stamp=new DraftStamp(DraftTask,draft?.Id,draft?.Revision??0,draft?.Hash);
        return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,"final-policy:"+policy.QuestInstance+":"+policy.Version,"confirm_final_policy",[BranchOperations.Change(s,PolicyId,"final_policy_proposal",policy)],RelevantReads(s),policy,draft:stamp),cancellationToken);
    }
    public async ValueTask<FinalActionPreview> PreviewAsync(CancellationToken cancellationToken=default)
    {
        var s=await repository.SnapshotAsync(cancellationToken);BranchOperations.Safe(s);var missing=ImmutableArray.CreateBuilder<string>();var frozen=Frozen(s);var policy=Read<FinalPolicyProposal>(s,PolicyId);var draft=await repository.GetDraftAsync(DraftTask);var quest=Quest(s,"Q-F04");
        if(!WorldPreparationProjection.Closed(s,"Q-604"))missing.Add("actual_closed_Q604");if(!Active(s,"Q-F04"))missing.Add("actual_active_QF04");if(!AtCurrentControl(s))missing.Add("actual_current_final_control_position_and_authority");if(frozen is null)missing.Add("actual_sealed_preparation");
        if(policy is null||policy.QuestInstance!=quest?.Instance||!policy.Confirmed||policy.DraftHash!=draft?.Hash||!Enum.IsDefined(policy.Choice))missing.Add("current_explicit_policy_confirmation");
        else{var proof=Receipt(s,"final-policy:"+policy.QuestInstance+":"+policy.Version,"confirm_final_policy");if(proof is null||!Same(StrictJson.Deserialize<FinalPolicyProposal>(proof.ResultJson),policy))missing.Add("actual_policy_confirmation_receipt");}
        if(frozen is not null)
        {
            var option=EndingRules.Evaluate(Prepared(frozen,s),WorldPreparationProjection.CentralSafe(s),Active(s,"Q-F04")).Options.SingleOrDefault(o=>o.Choice==policy?.Choice);if(option is not null)missing.AddRange(option.Missing);
            if(frozen.ReservedResources.Any(p=>s.Balances.GetValueOrDefault(p.Key)<p.Value))missing.Add("sealed_resources_intact");
            var checkpoint=checkpoints is null?null:await checkpoints.FindAsync(frozen.PreFinalCheckpoint,cancellationToken);if(checkpoint is null||checkpoint.Profile!=repository.Session.Profile||checkpoint.SourceBranch!=frozen.SourceBranch||checkpoint.Release!=frozen.Release)missing.Add("actual_prefinal_checkpoint_still_available");
        }
        Guid token=Guid.NewGuid();if(missing.Count==0)
        {
            var currentQuest=quest!;var currentPreparation=frozen!;var currentPolicy=policy!;
            var history=s.Receipts.OrderBy(r=>r.Sequence).Select(r=>new EndingHistoryRecord(r.SemanticKey,"receipt:"+r.ActionType,r.ResultJson)).Concat(s.Entities.Where(e=>e.Id is not(EndingId or PolicyId)).Select(e=>new EndingHistoryRecord(e.Id,e.Kind,e.Json))).ToImmutableArray();
            var ending=new FinalEndingSnapshot(Guid.NewGuid(),repository.Session.Branch,repository.Session.ReleaseId,currentQuest.Instance,currentPreparation.Id,currentPolicy.Choice,EndingRules.Id(currentPolicy.Choice),s.Sequence,history,EndingRules.EpilogueModules(currentPolicy.Choice));var reward=catalog.Quests.Single(q=>q.Id=="Q-F04").Reward;
            if(reward.Items.Length!=0||reward.Bundle.Experience!=600||reward.Bundle.Marks!=200||reward.Bundle.Parts!=0)throw new InvalidDataException("final_reward_differs_from_frozen_source");
            var balances=currentPreparation.ReservedResources.SelectMany(p=>new[]{new BalanceChange(p.Key,-p.Value),new BalanceChange("ending:committed:"+ending.Id+":"+p.Key,p.Value)}).ToImmutableArray().Add(new("experience",reward.Bundle.Experience)).Add(new("marks",reward.Bundle.Marks));
            _previews[token]=BranchOperations.Plan(repository,s,"Q-F04:resolution","final_policy_execution",[BranchOperations.Change(s,"quest:Q-F04","quest",currentQuest.Close(QuestOutcome.Completed)),BranchOperations.Change(s,EndingId,"ending_snapshot",ending)],RelevantReads(s),ending,balances:balances,draft:new(DraftTask,draft?.Id,draft?.Revision??0,draft?.Hash));
        }
        return new(token,missing.Count==0,missing.Distinct().ToImmutableArray(),policy?.Choice);
    }
    public void ClosePreview(Guid token)=>_previews.Remove(token);
    public ValueTask<CommandResult> ConfirmAsync(Guid token,CancellationToken cancellationToken=default)
    {
        if(!_previews.TryGetValue(token,out var command))return ValueTask.FromResult(new CommandResult(CommitStatus.Rejected,null,[new("preview_not_ready_or_closed","", "请重新核对最终行动。") ]));
        // The identical immutable request survives Busy/OutcomeUnknown. SQLite validates actual
        // load, source revisions, authority and draft in the same transaction as the one reward.
        return repository.ExecuteAsync(command,cancellationToken);
    }
    public async ValueTask<FinalEndingSnapshot?> SnapshotAsync(CancellationToken cancellationToken=default)=>Ending(await repository.SnapshotAsync(cancellationToken));
    public async ValueTask<EndingPresentation?> PresentAsync(CancellationToken cancellationToken=default)
    {var snapshot=await SnapshotAsync(cancellationToken);return snapshot is null?null:new(snapshot.EndingId,snapshot.EpilogueModules);}
}

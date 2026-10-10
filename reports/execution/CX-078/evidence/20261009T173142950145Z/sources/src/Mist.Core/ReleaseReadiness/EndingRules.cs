using System.Collections.Immutable;
using Mist.Core.Contracts;

namespace Mist.Core.ReleaseReadiness;

public enum EndingChoice { Central,Island,Distributed }
public sealed record PreparedTower(string Tower,bool Safe,bool Baseline,bool Operators,string? EnhancedVersion,bool Transition,string? TrialVersion,bool Agreement,string? ProtocolVersion,ImmutableArray<string> SourceReceipts);
public sealed record WorldPreparation(string Version,string? TechnicalVersion,bool TechnicalVerified,string ProtocolVersion,ImmutableArray<PreparedTower> Towers);
public sealed record EndingOption(EndingChoice Choice,bool Prepared,bool Executable,ImmutableArray<string> Missing);
public sealed record EndingRulesView(bool BasePrepared,ImmutableArray<EndingOption> Options);

public static class EndingRules
{
    public static ImmutableArray<string> Towers {get;}=["TWR-R1","TWR-R2","TWR-R3","TWR-R4","TWR-R5","TWR-R6"];
    // No score, help count, companion, private arc, difficulty, money or equipment input exists.
    public static EndingRulesView Evaluate(WorldPreparation preparation,bool centralSafe,bool finalDecisionActive)
    {
        var missing=ImmutableArray.CreateBuilder<string>();var actual=preparation.Towers.IsDefault?[]:preparation.Towers;
        bool exact=actual.Length==6&&actual.Select(t=>t.Tower).Distinct(StringComparer.Ordinal).Count()==6&&Towers.All(id=>actual.Any(t=>t.Tower==id));
        if(!exact)missing.Add("six_distinct_towers");
        foreach(string id in Towers)
        {
            var matches=actual.Where(t=>t.Tower==id).ToArray();if(matches.Length!=1)continue;var tower=matches[0];
            if(!tower.Safe)missing.Add(id+":current_safety");if(!tower.Baseline)missing.Add(id+":baseline_preservation");if(!tower.Operators)missing.Add(id+":actual_local_operators");
        }
        bool baseReady=missing.Count==0;var enhanced=missing.ToImmutable().ToBuilder();
        if(!preparation.TechnicalVerified||string.IsNullOrWhiteSpace(preparation.TechnicalVersion))enhanced.Add("verified_technical_package");
        foreach(string id in Towers)
        {
            var matches=actual.Where(t=>t.Tower==id).ToArray();if(matches.Length!=1)continue;var tower=matches[0];
            if(tower.EnhancedVersion!=preparation.TechnicalVersion||string.IsNullOrWhiteSpace(tower.EnhancedVersion))enhanced.Add(id+":enhanced_installation");
            if(!tower.Transition)enhanced.Add(id+":transition_supply");
            if(!tower.Agreement||tower.ProtocolVersion!=preparation.ProtocolVersion)enhanced.Add(id+":current_operator_and_representative_agreement");
            if(tower.TrialVersion!=tower.EnhancedVersion||string.IsNullOrWhiteSpace(tower.TrialVersion))enhanced.Add(id+":current_field_trial");
        }
        ImmutableArray<string> FinalMissing(ImmutableArray<string> prepared)
        {var result=prepared.ToBuilder();if(!centralSafe)result.Add("central_dangerous_program_stabilized");if(!finalDecisionActive)result.Add("actual_final_decision_node");return result.ToImmutable();}
        var basic=FinalMissing(missing.ToImmutable());var distributed=FinalMissing(enhanced.ToImmutable());
        return new(baseReady,[new(EndingChoice.Central,baseReady,basic.Length==0,basic),new(EndingChoice.Island,baseReady,basic.Length==0,basic),new(EndingChoice.Distributed,enhanced.Count==0,distributed.Length==0,distributed)]);
    }
    public static string Id(EndingChoice choice)=>choice switch{EndingChoice.Central=>"E-CENTRAL",EndingChoice.Island=>"E-ISLAND",EndingChoice.Distributed=>"E-DISTRIBUTED",_=>throw new InvalidOperationException("unknown_ending")};
    public static ImmutableArray<string> EpilogueModules(EndingChoice choice)=>[Id(choice)+":immediate",Id(choice)+":six_months","history:responsibility_retained","world:natural_fog_remains"];
}
public sealed record MainQuestResolution(string Quest,Guid Instance,string Outcome);
public sealed record GlimmerUseTransfer(Guid VesselInstance,Guid Q104Instance,string Owner,string WatchOfficer);
public sealed record CentralSafetyState(Guid Instance,long Configuration,bool Stable,bool AutomaticRestartDisabled,bool CommandChainReleased,string SourceReceipt);
public sealed record CentralStabilityResult(Guid Instance,long Configuration,bool Stable,bool AutomaticRestartDisabled,bool CommandChainReleased);
public sealed record FinalControlAccess(Guid QuestInstance,string Scene,double X,double Y,bool Allowed,string SourceReceipt);
public sealed record HighRiskCommitment(string Id,bool Accepted,bool OnsiteRequired,bool SafelyHandedOver,bool ExplicitlySuspended);
public sealed record FrozenPreparation(Guid Id,BranchId SourceBranch,string Release,Guid Q604Instance,string PreFinalCheckpoint,WorldPreparation Preparation,Guid VesselInstance,ImmutableDictionary<string,long> ReservedResources);
public sealed record FinalPolicyProposal(long Version,Guid QuestInstance,EndingChoice Choice,string? DraftHash,bool StructuredAssistance,bool Confirmed);
public sealed record EndingHistoryRecord(string Id,string Kind,string Json);
public sealed record FinalEndingSnapshot(Guid Id,BranchId Branch,string Release,Guid QF04Instance,Guid PreparationId,EndingChoice Choice,string EndingId,long CapturedWorldSequence,ImmutableArray<EndingHistoryRecord> History,ImmutableArray<string> EpilogueModules);

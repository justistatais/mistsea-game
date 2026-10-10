using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Mist.Core.ReleaseReadiness;

public enum EvidenceTrust { Unknown,IntegrityOnly,TrustedMachineExecution,TrustedHumanVerification,TrustedProjectApproval,ExplicitOfflineFixture }
public enum ReadinessState { Blocked,Ready,OptionalDisabled }
public sealed record ReleaseRequirement(string Id,string Kind,ImmutableArray<string> Dependencies,string? Feature=null);
public sealed record VerifiedReleaseEvidence(string Requirement,string State,string Release,string SourceVersion,string ArtifactSha256,EvidenceTrust Trust);
public sealed record RequirementReadiness(string Id,ReadinessState State,ImmutableArray<string> Missing);
public sealed record ReleaseReadinessResult(string Release,ImmutableArray<RequirementReadiness> Requirements)
{
    public bool ReadyForLocalFormalHandoff=>Requirements.Any(r=>r.Id=="G7"&&r.State==ReadinessState.Ready);
    public bool ExternalPublicationAuthorized=>false;
    public bool OfficialExamCertification=>false;
}
public static class ReleaseGateRules
{
    // Verified evidence is an external authority projection, never a parsed approved=true claim.
    // Hash integrity alone and explicitly simulated fixtures do not grant release acceptance.
    public static ReleaseReadinessResult Evaluate(string release,string sourceVersion,IEnumerable<ReleaseRequirement> requirements,IEnumerable<VerifiedReleaseEvidence> evidence,bool onlineModel=false)
    {
        var definitions=requirements.ToImmutableArray();var records=evidence.ToImmutableArray();var result=new Dictionary<string,RequirementReadiness>(StringComparer.Ordinal);var visiting=new HashSet<string>(StringComparer.Ordinal);
        RequirementReadiness Read(string id)
        {
            if(result.TryGetValue(id,out var ready))return ready;var matches=definitions.Where(r=>r.Id==id).ToArray();if(matches.Length!=1)return new(id,ReadinessState.Blocked,["unknown_or_duplicate_requirement"]);var definition=matches[0];
            if(definition.Feature=="online_model"&&!onlineModel){var disabled=new RequirementReadiness(id,ReadinessState.OptionalDisabled,[]);result[id]=disabled;return disabled;}
            if(!visiting.Add(id))return new(id,ReadinessState.Blocked,["dependency_cycle"]);var missing=ImmutableArray.CreateBuilder<string>();
            foreach(string dependency in definition.Dependencies){var status=Read(dependency);if(status.State==ReadinessState.Blocked)missing.Add(dependency);}
            if(definition.Kind!="gate")
            {
                var candidates=records.Where(e=>e.Requirement==id&&e.State=="accepted"&&e.Release==release&&e.SourceVersion==sourceVersion&&Regex.IsMatch(e.ArtifactSha256,"^[0-9a-f]{64}$",RegexOptions.CultureInvariant)).ToArray();
                bool Trust(VerifiedReleaseEvidence e)=>definition.Kind switch{"human_verification" when id=="HV-007"=>e.Trust==EvidenceTrust.TrustedProjectApproval,"human_verification" or "human_calibration"=>e.Trust==EvidenceTrust.TrustedHumanVerification,_=>e.Trust==EvidenceTrust.TrustedMachineExecution};
                if(candidates.Count(Trust)!=1)missing.Add(definition.Kind is "human_verification" or "human_calibration"?"actual_human_authority_and_versioned_evidence":"accepted_actual_runtime_evidence");
            }
            visiting.Remove(id);var next=new RequirementReadiness(id,missing.Count==0?ReadinessState.Ready:ReadinessState.Blocked,missing.ToImmutable());result[id]=next;return next;
        }
        foreach(var definition in definitions)Read(definition.Id);return new(release,definitions.Select(d=>result.GetValueOrDefault(d.Id)??Read(d.Id)).ToImmutableArray());
    }
}

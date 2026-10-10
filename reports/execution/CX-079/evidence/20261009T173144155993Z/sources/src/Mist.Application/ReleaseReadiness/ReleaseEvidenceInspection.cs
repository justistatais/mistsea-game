using System.Collections.Immutable;
using System.Text.Json;
using Mist.Core.Contracts;
using Mist.Core.ReleaseReadiness;

namespace Mist.Application.ReleaseReadiness;

public interface ITrustedReleaseEvidenceProjection
{ValueTask<ImmutableArray<VerifiedReleaseEvidence>> ReadVerifiedAsync(string release,string sourceVersion,CancellationToken cancellationToken=default);}
public sealed class NoTrustedReleaseEvidence:ITrustedReleaseEvidenceProjection
{
    public ValueTask<ImmutableArray<VerifiedReleaseEvidence>> ReadVerifiedAsync(string release,string sourceVersion,CancellationToken cancellationToken=default)
    {cancellationToken.ThrowIfCancellationRequested();return ValueTask.FromResult(ImmutableArray<VerifiedReleaseEvidence>.Empty);}
}
// Inspection only. It cannot edit the execution ledger, grant human authority, sign, package,
// activate a supplier, publish, or infer authenticity merely from a matching SHA-256 string.
public sealed class ReleaseEvidenceInspection(ImmutableArray<ReleaseRequirement> requirements,ITrustedReleaseEvidenceProjection? evidence=null)
{
    private readonly ITrustedReleaseEvidenceProjection _evidence=evidence??new NoTrustedReleaseEvidence();
    public async ValueTask<ReleaseReadinessResult> InspectAsync(string release,string sourceVersion,bool onlineModel=false,CancellationToken cancellationToken=default)=>ReleaseGateRules.Evaluate(release,sourceVersion,requirements,await _evidence.ReadVerifiedAsync(release,sourceVersion,cancellationToken),onlineModel);
    public static ImmutableArray<ReleaseRequirement> ReadFrozenPlan(string json,bool onlineModel=false)
    {
        using var document=JsonDocument.Parse(json);StrictJson.CheckDuplicates(document.RootElement);var root=document.RootElement;var results=ImmutableArray.CreateBuilder<ReleaseRequirement>();
        foreach(var task in root.GetProperty("tasks").EnumerateArray())results.Add(new(task.GetProperty("id").GetString()!,task.GetProperty("kind").GetString()!,task.GetProperty("depends_on").EnumerateArray().Select(e=>e.GetString()!).ToImmutableArray(),task.TryGetProperty("feature",out var f)&&f.ValueKind==JsonValueKind.String?f.GetString():null));
        foreach(var gate in root.GetProperty("gates").EnumerateArray())
        {
            var dependencies=gate.GetProperty("depends_on").EnumerateArray().Select(e=>e.GetString()!).ToImmutableArray();if(onlineModel&&gate.TryGetProperty("optional_when_enabled",out var optional)&&optional.TryGetProperty("online_model",out var online))dependencies=dependencies.AddRange(online.EnumerateArray().Select(e=>e.GetString()!));results.Add(new(gate.GetProperty("id").GetString()!,"gate",dependencies));
        }
        return results.ToImmutable();
    }
}

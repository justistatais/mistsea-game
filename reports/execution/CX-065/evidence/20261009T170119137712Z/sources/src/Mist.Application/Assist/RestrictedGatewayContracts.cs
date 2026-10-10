using System.Collections.Immutable;
using Mist.Core.Assistance;
using Mist.Core.Contracts;

namespace Mist.Application.Assist;

public sealed record AssistanceAuthorizationState(AssistanceScope Scope,bool ExplicitConsent,bool ExternalAllowed,ImmutableArray<AssistancePurpose> Purposes,AssistanceHelpLevel MaximumHelpLevel,ImmutableArray<AuthorizedSource> Sources,AssistanceDraft? CurrentDraft,bool ReleaseQuarantined=false);
// A read-only, pre-filtered authorization projection. The gateway receives no world commands,
// storage connections, full plot index, keys, or arbitrary file/network tools through this port.
public interface IAssistanceAuthorization
{ValueTask<AssistanceAuthorizationState> ReadAsync(string taskInstance,CancellationToken cancellationToken=default);}
public interface IRestrictedAssistanceProvider
{
    AssistanceProviderIdentity Identity {get;}
    ValueTask<AssistanceProviderReply> RequestAsync(ScopedAssistanceRequest request,CancellationToken cancellationToken=default);
}
public sealed record AssistanceDraftSelection(DraftId Id,long Revision,string Hash);
public sealed record AssistanceSelection(string RequestId,AssistanceScope Scope,string TaskInstance,AssistancePurpose Purpose,AssistanceHelpLevel HelpLevel,ImmutableArray<SourceSelection> Sources,AssistanceDraftSelection? Draft,string PromptVersion);
public sealed record RestrictedGatewayLimits(int TimeoutMilliseconds=30000,int MaximumAttempts=2,int MaximumRequestBytes=65536,int MaximumReplyBytes=65536,long BudgetMinor=0,long ReservePerAttemptMinor=0,string? Currency=null)
{
    public void Validate()
    {
        if(TimeoutMilliseconds is <1 or >30000||MaximumAttempts is <1 or >2||MaximumRequestBytes is <1 or >65536||MaximumReplyBytes is <1 or >65536||BudgetMinor<0||ReservePerAttemptMinor<0||ReservePerAttemptMinor>BudgetMinor||BudgetMinor>0&&string.IsNullOrWhiteSpace(Currency))throw new ArgumentException("invalid_gateway_limits");
    }
}
public sealed record AssistanceAttempt(string RequestId,int Number,string ProviderVersion,string Status,long ReservedMinor,AssistanceUsage Usage);
public sealed record AssistanceBudgetView(long LimitMinor,long ChargedMinor,long ReservedMinor,bool Stopped,string? Currency,ImmutableArray<AssistanceAttempt> Attempts)
{
    public bool HasActualOnlineMeasurement=>Attempts.Any(a=>a.Usage.Origin=="provider_reported");
}
public sealed record RestrictedGatewayResult(string RequestId,string Code,string Route,RestrictedCandidate? Candidate,RestrictedCandidate? OldDraftCandidate,bool CacheHit,AssistanceProviderKind ProviderKind)
{
    public double? Band=>null;
    public string AssessmentStatus=>"unassessed";
    public bool WorldWriteCapability=>false;
    public string SafeSummary=>Code;
}

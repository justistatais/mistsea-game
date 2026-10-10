using System.Collections.Immutable;
using Mist.Application.Assistance;
using Mist.Application.Contracts;
using Mist.Core.Assistance;

namespace Mist.Application.Assist;

// Reuses the existing default provider. This adapter adds protocol scope without creating
// an online integration or manufacturing a language evaluation.
public sealed class RestrictedDisabledProvider:IRestrictedAssistanceProvider
{
    private readonly DisabledProvider _disabled=new();
    public AssistanceProviderIdentity Identity {get;}=new("disabled","disabled-v1",AssistanceProviderKind.Disabled);
    public async ValueTask<AssistanceProviderReply> RequestAsync(ScopedAssistanceRequest request,CancellationToken cancellationToken=default)
    {
        var segments=request.Sources.Select(s=>new VisibleSegment(s.AssetId,s.Version,s.SegmentId,s.Start,s.End,s.Text,s.Hash)).ToImmutableArray();
        var result=await _disabled.RequestAsync(new(request.RequestId,request.Scope.Load,request.Draft?.Id,request.Draft?.Revision,request.Scope.PrivacyEpoch,request.Scope.ConsentVersion,RestrictedReplyValidator.Mode(request.Purpose),segments,request.Draft?.Text),cancellationToken);
        return new(result.Status,null,AssistanceUsage.Disabled);
    }
}

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Mist.Core.Assistance;
using Mist.Core.Contracts;

namespace Mist.Application.Assist;

public sealed class RestrictedAssistanceGateway:IDisposable
{
    private readonly IAssistanceAuthorization _authorization;
    private readonly IRestrictedAssistanceProvider _provider;
    private readonly RestrictedGatewayLimits _limits;
    private readonly bool _allowFixture;
    private readonly object _sync=new();
    private readonly Dictionary<string,(string Hash,AssistanceSelection Selection,Task<RestrictedGatewayResult> Task)> _operations=new(StringComparer.Ordinal);
    private readonly Dictionary<string,(ProfileId Profile,RestrictedCandidate Candidate)> _cache=new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeFeedback=new(StringComparer.Ordinal);
    private readonly List<AssistanceAttempt> _attempts=[];
    private readonly CancellationTokenSource _shutdown=new();
    private long _reserved,_charged;
    private bool _stopped;
    public RestrictedAssistanceGateway(IAssistanceAuthorization authorization,IRestrictedAssistanceProvider? provider=null,RestrictedGatewayLimits? limits=null,bool allowExplicitOfflineFixture=false)
    {
        _authorization=authorization;_provider=provider??new RestrictedDisabledProvider();_limits=limits??new();_limits.Validate();_allowFixture=allowExplicitOfflineFixture;
    }
    public AssistanceBudgetView Budget {get{lock(_sync)return new(_limits.BudgetMinor,_charged,_reserved,_stopped,_limits.Currency,_attempts.ToImmutableArray());}}
    public async ValueTask<RestrictedGatewayResult> RequestAsync(AssistanceSelection selection,CancellationToken cancellationToken=default)
    {
        if(cancellationToken.IsCancellationRequested)return Result(selection,"cancelled");
        if(!BasicSelection(selection))return Result(selection,"invalid_request");
        string hash=Hash(StrictJson.Serialize(selection));Task<RestrictedGatewayResult> operation;
        lock(_sync)
        {
            if(_operations.TryGetValue(selection.RequestId,out var previous))
            {
                if(previous.Hash!=hash)return Result(selection,"request_id_reused");operation=previous.Task;
            }
            else{operation=RunAsync(selection,cancellationToken);_operations.Add(selection.RequestId,(hash,selection,operation));}
        }
        try
        {
            var result=await operation.WaitAsync(cancellationToken);return await ReauthorizeAsync(selection,result,cancellationToken);
        }
        catch(OperationCanceledException){return Result(selection,"cancelled");}
        catch(Exception){return Result(selection,"scope_unavailable");}
    }
    private async Task<RestrictedGatewayResult> RunAsync(AssistanceSelection selection,CancellationToken cancellationToken)
    {
        string feedbackKey=Hash(StrictJson.Serialize(new{selection.Scope.Profile,selection.Scope.Branch,selection.TaskInstance,selection.Draft}));bool holdsFeedback=false;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,_shutdown.Token);
        try
        {
            var state=await _authorization.ReadAsync(selection.TaskInstance,linked.Token);if(!Authorized(selection,state,checkCurrentDraft:true))return Result(selection,"not_authorized");
            var request=Project(selection,state);if(request is null)return Result(selection,"source_not_authorized");
            if(Encoding.UTF8.GetByteCount(StrictJson.Serialize(request))>_limits.MaximumRequestBytes)return Result(selection,"request_too_large");
            if(_provider.Identity.Kind==AssistanceProviderKind.Online)return Result(selection,"online_provider_not_enabled");
            if(_provider.Identity.Kind==AssistanceProviderKind.ExplicitOfflineFixture&&!_allowFixture)return Result(selection,"fixture_requires_explicit_test_mode");
            if(!Enum.IsDefined(_provider.Identity.Kind))return Result(selection,"invalid_provider");
            string requestHash=Hash(StrictJson.Serialize(request));string cacheKey=Hash(StrictJson.Serialize(request with{RequestId=new string('0',64)}));
            lock(_sync)
            {
                if(_cache.TryGetValue(cacheKey,out var cached))return Result(selection,"unverified_candidate",cached.Candidate with{RequestId=selection.RequestId},cacheHit:true);
                if(selection.Purpose==AssistancePurpose.Feedback){if(!_activeFeedback.Add(feedbackKey))return Result(selection,"evaluation_already_pending");holdsFeedback=true;}
            }
            for(int attempt=1;attempt<=_limits.MaximumAttempts;attempt++)
            {
                linked.Token.ThrowIfCancellationRequested();state=await _authorization.ReadAsync(selection.TaskInstance,linked.Token);var projected=Project(selection,state);if(!Authorized(selection,state,checkCurrentDraft:true)||projected is null||Hash(StrictJson.Serialize(projected))!=requestHash)return Result(selection,"not_authorized");
                if(!Reserve())return Result(selection,"budget_unavailable");
                AssistanceProviderReply reply;string attemptStatus="provider_error";AssistanceUsage usage=AssistanceUsage.Unreported;
                try
                {
                    var pending=_provider.RequestAsync(request,linked.Token).AsTask();
                    try{reply=await pending.WaitAsync(TimeSpan.FromMilliseconds(_limits.TimeoutMilliseconds),linked.Token);}
                    catch(TimeoutException){linked.Cancel();ObserveLate(pending);attemptStatus="timeout";return Result(selection,"timeout");}
                    catch(OperationCanceledException){ObserveLate(pending);attemptStatus="cancelled";throw;}
                    usage=reply.Usage;attemptStatus=reply.Status;
                }
                catch(OperationCanceledException){throw;}
                catch(Exception){attemptStatus="provider_error";return Result(selection,"provider_error");}
                finally{Record(selection,attempt,attemptStatus,usage);}
                if(reply.Status is "transient_error" or "rate_limited")
                {
                    if(attempt==_limits.MaximumAttempts||Budget.Stopped)return Result(selection,"service_unavailable");continue;
                }
                if(reply.Status!="candidate"||reply.CandidateJson is null)return Result(selection,reply.Status=="provider_disabled"?"provider_disabled":"service_unavailable");
                var validation=RestrictedReplyValidator.Validate(request,reply.CandidateJson,_limits.MaximumReplyBytes);if(!validation.Valid||validation.Candidate is null)return Result(selection,validation.Code);
                // Complete response only. There is no token streaming or direct draft/world update path.
                var candidate=validation.Candidate;var outcome=await ReauthorizeAsync(selection,Result(selection,"unverified_candidate",candidate),linked.Token);
                if(outcome.Route=="current"&&outcome.Candidate is not null){lock(_sync)_cache[cacheKey]=(selection.Scope.Profile,candidate);}
                return outcome;
            }
            return Result(selection,"service_unavailable");
        }
        catch(OperationCanceledException){return Result(selection,"cancelled");}
        catch(Exception){return Result(selection,"scope_unavailable");}
        finally{if(holdsFeedback)lock(_sync)_activeFeedback.Remove(feedbackKey);}
    }
    private async ValueTask<RestrictedGatewayResult> ReauthorizeAsync(AssistanceSelection selection,RestrictedGatewayResult result,CancellationToken cancellationToken)
    {
        if(result.Candidate is null&&result.OldDraftCandidate is null)return result;
        var state=await _authorization.ReadAsync(selection.TaskInstance,cancellationToken);var expected=selection.Scope;
        if(!state.ExplicitConsent||!state.ExternalAllowed||state.ReleaseQuarantined||state.Scope.Profile!=expected.Profile||state.Scope.PrivacyEpoch!=expected.PrivacyEpoch||state.Scope.ConsentVersion!=expected.ConsentVersion||state.Scope.PermissionVersion!=expected.PermissionVersion||state.Scope.Release!=expected.Release||state.Purposes.IsDefault||!state.Purposes.Contains(selection.Purpose)||state.MaximumHelpLevel<selection.HelpLevel)
        {ClearForProfile(expected.Profile);return Result(selection,"discarded");}
        if(state.Scope.Branch!=expected.Branch||state.Scope.Load!=expected.Load)return Result(selection,"archived_context",route:"history_only");
        var projected=Project(selection,state,allowOldDraft:true);if(projected is null){ClearForProfile(expected.Profile);return Result(selection,"discarded");}
        var candidate=result.Candidate??result.OldDraftCandidate;
        if(selection.Draft is {} draft&&(state.CurrentDraft is not{} current||current.Id!=draft.Id||current.Revision!=draft.Revision||current.Hash!=draft.Hash))return new(selection.RequestId,"old_draft","old_draft",null,candidate,result.CacheHit,_provider.Identity.Kind);
        return Result(selection,result.Code,candidate,result.CacheHit);
    }
    private static bool BasicSelection(AssistanceSelection selection)=>Regex.IsMatch(selection.RequestId,"^[0-9a-f]{64}$",RegexOptions.CultureInvariant)&&Enum.IsDefined(selection.Purpose)&&Enum.IsDefined(selection.HelpLevel)&&!string.IsNullOrWhiteSpace(selection.TaskInstance)&&selection.TaskInstance.Length<=200&&!string.IsNullOrWhiteSpace(selection.PromptVersion)&&selection.PromptVersion.Length<=200&&!selection.Sources.IsDefault&&selection.Sources.Length<=6&&(selection.Purpose==AssistancePurpose.Explain?selection.Sources.Length>0:selection.Draft is not null);
    private static bool Authorized(AssistanceSelection selection,AssistanceAuthorizationState state,bool checkCurrentDraft)=>state.ExplicitConsent&&state.ExternalAllowed&&!state.ReleaseQuarantined&&state.Scope==selection.Scope&&!state.Purposes.IsDefault&&state.Purposes.Contains(selection.Purpose)&&state.MaximumHelpLevel>=selection.HelpLevel&&(!checkCurrentDraft||selection.Draft is null||state.CurrentDraft is {} draft&&draft.Id==selection.Draft.Id&&draft.Revision==selection.Draft.Revision&&draft.Hash==selection.Draft.Hash);
    private ScopedAssistanceRequest? Project(AssistanceSelection selection,AssistanceAuthorizationState state,bool allowOldDraft=false)
    {
        if(state.Sources.IsDefault)return null;var sources=ImmutableArray.CreateBuilder<AssistanceSource>();var ids=new HashSet<SourceSelection>();
        foreach(var chosen in selection.Sources)
        {
            if(!ids.Add(chosen)||chosen.Start<0||chosen.End<=chosen.Start)return null;
            var matches=state.Sources.Where(s=>s.AssetId==chosen.AssetId&&s.Version==chosen.Version&&s.SegmentId==chosen.SegmentId&&s.Discovered&&s.ExternalAllowed&&s.MinimumHelpLevel<=selection.HelpLevel&&chosen.Start>=s.Start&&chosen.End<=s.End).ToArray();if(matches.Length!=1)return null;
            var source=matches[0];string text;
            try{if(source.End-source.Start!=source.Text.EnumerateRunes().Count())return null;text=UnicodeOffsets.Slice(source.Text,chosen.Start-source.Start,chosen.End-source.Start);}catch(ArgumentException){return null;}
            sources.Add(new(chosen.AssetId,chosen.Version,chosen.SegmentId,chosen.Start,chosen.End,text,Hash(text)));
        }
        AssistanceDraft? draft=null;
        if(selection.Draft is {} chosenDraft)
        {
            if(state.CurrentDraft is {} current&&current.Id==chosenDraft.Id&&current.Revision==chosenDraft.Revision&&current.Hash==chosenDraft.Hash&&Hash(current.Text)==current.Hash)draft=current;
            else if(!allowOldDraft)return null;
        }
        return new("mist-assist-v1",selection.RequestId,selection.Scope,selection.TaskInstance,selection.Purpose,selection.HelpLevel,sources.ToImmutable(),draft,_provider.Identity.Version,selection.PromptVersion);
    }
    private bool Reserve()
    {lock(_sync){if(_stopped||_charged+_reserved>_limits.BudgetMinor-_limits.ReservePerAttemptMinor)return false;_reserved=checked(_reserved+_limits.ReservePerAttemptMinor);return true;}}
    private void Record(AssistanceSelection selection,int attempt,string status,AssistanceUsage usage)
    {
        lock(_sync)
        {
            _reserved-=_limits.ReservePerAttemptMinor;bool invalidMetering=false;
            if(usage.ChargedMinor is {} cost)
            {
                bool valid=cost>=0&&(usage.InputTokens is null or >=0)&&(usage.OutputTokens is null or >=0)&&(cost==0||usage.Currency==_limits.Currency)&&(_provider.Identity.Kind==AssistanceProviderKind.ExplicitOfflineFixture?usage.Origin=="explicit_offline_fixture":usage.Origin=="disabled_no_model_call");
                if(!valid){_stopped=true;invalidMetering=true;}else{try{_charged=checked(_charged+cost);}catch(OverflowException){_stopped=true;}if(_charged>_limits.BudgetMinor)_stopped=true;}
            }
            else if(_provider.Identity.Kind!=AssistanceProviderKind.Disabled)_stopped=true;
            string safeStatus=status is "candidate" or "transient_error" or "rate_limited" or "permission_denied" or "provider_disabled" or "provider_error" or "timeout" or "cancelled"?status:"unknown_provider_status";
            var recorded=invalidMetering?usage with{Currency=null,Origin="invalid_metering"}:usage.Origin is "explicit_offline_fixture" or "disabled_no_model_call" or "unreported"?usage:usage with{Currency=null,Origin="invalid_metering"};
            _attempts.Add(new(selection.RequestId,attempt,_provider.Identity.Version,safeStatus,_limits.ReservePerAttemptMinor,recorded));
        }
    }
    private RestrictedGatewayResult Result(AssistanceSelection selection,string code,RestrictedCandidate? candidate=null,bool cacheHit=false,string route="current")=>new(selection.RequestId,code,route,candidate,null,cacheHit,_provider.Identity.Kind);
    public void ClearForProfile(ProfileId profile)
    {lock(_sync){foreach(string key in _cache.Where(p=>p.Value.Profile==profile).Select(p=>p.Key).ToArray())_cache.Remove(key);foreach(string key in _operations.Where(p=>p.Value.Selection.Scope.Profile==profile).Select(p=>p.Key).ToArray())_operations.Remove(key);}}
    public void Dispose(){_shutdown.Cancel();lock(_sync){_cache.Clear();_operations.Clear();}_shutdown.Dispose();}
    public static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static void ObserveLate(Task<AssistanceProviderReply> task){_ =task.ContinueWith(t=>{_ =t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);}
}

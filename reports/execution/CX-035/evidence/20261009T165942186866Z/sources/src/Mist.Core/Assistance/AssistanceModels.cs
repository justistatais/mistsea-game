using System.Collections.Immutable;
using Mist.Core.Contracts;

namespace Mist.Core.Assistance;

public enum AssistancePurpose { Explain,Feedback,IntentCandidate }
public enum AssistanceHelpLevel { L1=1,L2=2,L3=3,L4=4 }
public enum AssistanceProviderKind { Disabled,ExplicitOfflineFixture,Online }
public enum CandidateSemanticStatus { Unverified }
public sealed record AssistanceScope(ProfileId Profile,BranchId Branch,LoadContext Load,string Release,long PrivacyEpoch,long ConsentVersion,long PermissionVersion);
public sealed record AssistanceDraft(DraftId Id,long Revision,string Hash,string Text);
public sealed record SourceSelection(string AssetId,string Version,string SegmentId,int Start,int End);
public sealed record AuthorizedSource(string AssetId,string Version,string SegmentId,int Start,int End,string Text,bool Discovered,bool ExternalAllowed,AssistanceHelpLevel MinimumHelpLevel);
public sealed record AssistanceSource(string AssetId,string Version,string SegmentId,int Start,int End,string Text,string Hash);
public sealed record ScopedAssistanceRequest(string Protocol,string RequestId,AssistanceScope Scope,string TaskInstance,AssistancePurpose Purpose,AssistanceHelpLevel HelpLevel,ImmutableArray<AssistanceSource> Sources,AssistanceDraft? Draft,string ProviderVersion,string PromptVersion);
public sealed record AssistanceProviderIdentity(string Id,string Version,AssistanceProviderKind Kind);
// Null usage means unreported, never zero billed usage. Fixture metering is explicitly simulated.
public sealed record AssistanceUsage(long? InputTokens,long? OutputTokens,long? ChargedMinor,string? Currency,string Origin)
{
    public static AssistanceUsage Unreported {get;}=new(null,null,null,null,"unreported");
    public static AssistanceUsage Disabled {get;}=new(0,0,0,null,"disabled_no_model_call");
}
public sealed record AssistanceProviderReply(string Status,string? CandidateJson,AssistanceUsage Usage);
public sealed record SourceCitation(string AssetId,string Version,string SegmentId,int Start,int End,string Quote);
public sealed record ExplanationCandidate(string Text,SourceCitation Citation);
public sealed record FeedbackCandidate(int Start,int End,string Quote,string Category,string Certainty,string Explanation,string? Suggestion);
public sealed record IntentCandidate(string Route,string MainGenerator,string BeaconPlan,ImmutableArray<string> Unresolved);
public sealed record RestrictedCandidate(string RequestId,AssistancePurpose Purpose,string Status,ImmutableArray<ExplanationCandidate> Explanations,ImmutableArray<FeedbackCandidate> Feedback,ImmutableArray<IntentCandidate> Intent)
{
    public CandidateSemanticStatus SemanticStatus=>CandidateSemanticStatus.Unverified;
    public bool MayPresentAsVerified=>false;
    public bool MayExecuteWorldAction=>false;
    public bool LocalFallbackRequired=>true;
    public double? Band=>null;
    public string AssessmentStatus=>"unassessed";
    public int PrimaryFeedbackCount=>Math.Min(3,Feedback.Length);
    public string Presentation=>"plain_text_only";
}
public sealed record CandidateValidation(bool Valid,string Code,RestrictedCandidate? Candidate);

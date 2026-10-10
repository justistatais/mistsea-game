using System.Collections.Immutable;
using Mist.Core.Contracts;
using Mist.Core.Learning;

namespace Mist.Core.ReviewCalibration;

public enum ReviewResultAuthority { UnverifiedClaim,UnverifiedModelCandidate,ExplicitOfflineFixture,VerifiedLocalObjectiveRule,VerifiedHumanOutcome }
public enum ReviewClockTrust { LocalWallClockOnly,Anomaly,VerifiedContinuity }
public sealed record ReviewClock(DateTimeOffset Utc,ReviewClockTrust Trust);
public sealed record ReviewSupport(string Kind,DateTimeOffset At,ImmutableArray<string> Goals,bool ScopeKnown);
public sealed record ReviewObservation(string Id,string Goal,InputModality Modality,string Material,string MaterialVersion,string MaterialHash,string FamilyRoot,string Context,string Session,string ResponseHash,DateTimeOffset SubmittedAt,long PrivacyEpoch,bool? Correct,bool ConditionsComplete,bool NonemptyResponse,bool ScopeSufficient,bool FamilySeenBefore,bool AnswerSeenBefore,bool AudioDelivered,bool AudioPlayed,int Replays,bool Paused,decimal Speed,ImmutableArray<ReviewSupport> Support,ReviewClockTrust ClockTrust,ReviewResultAuthority Authority,long EvaluationRevision=0);
public sealed record ReviewEvidenceView(EvidenceState State,string Condition,int SuccessFamilies,int SuccessSessions,int SuccessContexts,bool TimingUnverified,bool RepeatPracticeOnly)
{public decimal? Band=>null;public bool WorldWriteCapability=>false;}
public sealed record ReviewSchedule(string Goal,InputModality Modality,int Stage,DateTimeOffset? Due,ImmutableArray<string> AppliedObservations,ImmutableArray<string> IndependentFamilies,bool TimingUnverified=false);
public sealed record ReviewDecline(string Goal,DateTimeOffset DeclinedAt,DateTimeOffset? OfferAgainAfter);
public enum PracticePartition { Training,CalibrationHoldout,TransferHoldout,AuthorOnly,RegressionOnly }
public enum PracticeIndexAuthority { UnverifiedClaim,ExplicitOfflineFixture,VerifiedContentProjection }
public sealed record PracticeIndexEntry(string Id,string Version,string BodyHash,string Title,string Goal,InputModality Modality,string FamilyRoot,string Topic,string Release,decimal Target,PracticePartition Partition,bool Available,bool CalibratedForDeclaredConditions,PracticeIndexAuthority Authority,ImmutableArray<string> RequiredFacts,ImmutableArray<string> RequiredAccess);
public sealed record ReviewSelectionContext(string Release,long PrivacyEpoch,ReviewClock Clock,bool SafeWindow,bool InputProtected,ImmutableHashSet<string> KnownFacts,ImmutableHashSet<string> Access,ImmutableHashSet<string> ExplicitGoals,ImmutableHashSet<string> DifficultyGoals,ImmutableHashSet<string> MissingGoals,ImmutableArray<ReviewDecline> Declines);
public sealed record ReviewRecommendation(string Id,string Version,string Title,string Goal,InputModality Modality,string FamilyRoot,string Topic,string Reason,bool NewFamily);
public static class ReviewRules
{
    public static ImmutableArray<int> IntervalDays {get;}=[1,3,7,14,30];
    public static string Condition(ReviewObservation o)
    {
        if(o.Authority is not(ReviewResultAuthority.VerifiedLocalObjectiveRule or ReviewResultAuthority.VerifiedHumanOutcome)||o.Correct is null)return "unassessed";
        if(!o.NonemptyResponse||!o.ScopeSufficient)return "insufficient_evidence";if(!o.ConditionsComplete||o.Support.IsDefault)return "conditions_unknown";
        var applicable=o.Support.Where(s=>s.At<=o.SubmittedAt&&s.Kind!="ui").ToArray();if(applicable.Any(s=>!s.ScopeKnown||s.Goals.IsDefault))return "support_scope_unknown";
        applicable=applicable.Where(s=>s.Goals.Contains(o.Goal)).ToArray();if(o.Modality==InputModality.Listening&&(!o.AudioDelivered||!o.AudioPlayed))return "text_path_no_audio_evidence";
        if(o.AnswerSeenBefore)return "prior_answer_exposure";if(o.Modality==InputModality.Listening&&applicable.Any(s=>s.Kind is "transcript" or "translation"))return "text_mediated_listening";
        if(applicable.Length>0)return "supported";if(o.Modality is not(InputModality.Reading or InputModality.Listening or InputModality.Writing))return "structured_or_mixed_not_independent_language";
        if(o.FamilySeenBefore)return "independent_repeat_material";if(o.Modality==InputModality.Listening&&(o.Replays>0||o.Paused||o.Speed!=1m))return "independent_audio_review";return "independent_first_conditions";
    }
    private static bool Independent(ReviewObservation o)=>Condition(o) is "independent_first_conditions" or "independent_audio_review";
    private static bool Supported(ReviewObservation o)=>Condition(o) is "supported" or "text_mediated_listening" or "prior_answer_exposure" or "support_scope_unknown" or "text_path_no_audio_evidence";
    public static ImmutableArray<ReviewObservation> Normalize(IEnumerable<ReviewObservation> observations,long epoch)
    {
        var source=observations.Where(o=>o.PrivacyEpoch==epoch).ToArray();var result=ImmutableArray.CreateBuilder<ReviewObservation>();
        foreach(var group in source.GroupBy(o=>o.Id,StringComparer.Ordinal))
        {
            var first=group.First();if(string.IsNullOrWhiteSpace(first.Id)||string.IsNullOrWhiteSpace(first.Goal)||string.IsNullOrWhiteSpace(first.FamilyRoot)||string.IsNullOrWhiteSpace(first.Session)||string.IsNullOrWhiteSpace(first.Context)||!CalibrationRules.HashValid(first.MaterialHash)||!CalibrationRules.HashValid(first.ResponseHash)||first.Replays<0||first.Speed<=0||first.EvaluationRevision<0)throw new InvalidDataException("invalid_review_observation");
            string Identity(ReviewObservation o)
            {bool unknown=o.Support.IsDefault||!o.Support.IsDefault&&o.Support.Any(s=>s.Goals.IsDefault);var support=o.Support.IsDefault?[]:o.Support.Select(s=>s with{Goals=s.Goals.IsDefault?[]:s.Goals}).ToImmutableArray();return StrictJson.Serialize(new{unknown_support=unknown,observation=o with{Correct=null,Authority=ReviewResultAuthority.UnverifiedClaim,EvaluationRevision=0,Support=support}});}
            if(group.Any(o=>Identity(o)!=Identity(first)))throw new InvalidDataException("observation_identity_or_submit_conditions_changed");
            var latest=group.OrderBy(o=>o.EvaluationRevision).Last();if(group.Count(o=>o.EvaluationRevision==latest.EvaluationRevision)>1&&group.Where(o=>o.EvaluationRevision==latest.EvaluationRevision).Select(o=>StrictJson.Serialize(o)).Distinct().Count()!=1)throw new InvalidDataException("conflicting_evaluation_revision");result.Add(latest);
        }return result.OrderBy(o=>o.SubmittedAt).ThenBy(o=>o.Id,StringComparer.Ordinal).ToImmutableArray();
    }
    public static ReviewEvidenceView Evidence(IEnumerable<ReviewObservation> source,string goal,InputModality modality,long epoch)
    {
        var history=Normalize(source,epoch).Where(o=>o.Goal==goal&&o.Modality==modality).ToArray();var successes=history.Where(o=>Independent(o)&&o.Correct==true).ToArray();int families=successes.Select(o=>o.FamilyRoot).Distinct().Count(),sessions=successes.Select(o=>o.Session).Distinct().Count(),contexts=successes.Select(o=>o.Context).Distinct().Count();bool varied=families>=3&&sessions>=2&&contexts>=2;
        bool delayed=varied&&successes.Any(a=>a.ClockTrust==ReviewClockTrust.VerifiedContinuity&&successes.Any(b=>b.ClockTrust==ReviewClockTrust.VerifiedContinuity&&b.FamilyRoot!=a.FamilyRoot&&b.SubmittedAt-a.SubmittedAt>=TimeSpan.FromDays(7)));
        var latest=history.Where(Independent).GroupBy(o=>o.FamilyRoot).Select(g=>g.Last()).OrderBy(o=>o.SubmittedAt).TakeLast(2).ToArray();bool recheck=latest.Length==2&&latest.All(o=>o.Correct==false);bool supported=history.Any(o=>o.Correct==true&&Supported(o));bool repeat=history.Any(o=>o.Correct==true&&Condition(o)=="independent_repeat_material");
        var state=recheck?EvidenceState.ReviewSuggested:delayed?EvidenceState.DelayedNewContext:varied?EvidenceState.MultiContext:successes.Length>0?EvidenceState.IndependentSuccess:supported?EvidenceState.SupportedSuccess:EvidenceState.Insufficient;
        return new(state,history.Length==0?"unassessed":Condition(history[^1]),families,sessions,contexts,varied&&!delayed&&successes.Any(o=>o.ClockTrust!=ReviewClockTrust.VerifiedContinuity),repeat&&successes.Length==0);
    }
    public static ReviewSchedule Apply(ReviewSchedule state,ReviewObservation o,ReviewClock now)
    {
        if(state.Stage is <0 or >4||state.AppliedObservations.IsDefault||state.IndependentFamilies.IsDefault||o.Goal!=state.Goal||o.Modality!=state.Modality)throw new InvalidDataException("invalid_review_schedule");
        if(state.AppliedObservations.Contains(o.Id))return state;string condition=Condition(o);if(condition is "unassessed" or "insufficient_evidence" or "conditions_unknown" or "structured_or_mixed_not_independent_language")return state;
        if(now.Trust!=ReviewClockTrust.VerifiedContinuity||now.Utc<o.SubmittedAt)return state with{TimingUnverified=true};
        var next=state with{AppliedObservations=state.AppliedObservations.Add(o.Id),TimingUnverified=false};if(condition=="independent_repeat_material"||state.IndependentFamilies.Contains(o.FamilyRoot))return next;
        if(Independent(o)&&o.Correct==true){int stage=Math.Min(state.Stage+1,4);return next with{Stage=stage,Due=now.Utc.AddDays(IntervalDays[stage]),IndependentFamilies=state.IndependentFamilies.Add(o.FamilyRoot)};}
        if(Independent(o)&&o.Correct==false)return next with{Stage=Math.Max(state.Stage-1,0),Due=now.Utc.AddDays(1)};
        if(Supported(o)){var close=now.Utc.AddDays(1);return next with{Due=state.Due is{} d&&d<close?d:close};}return next;
    }
    public static ImmutableArray<ReviewRecommendation> Select(IEnumerable<PracticeIndexEntry> library,IEnumerable<ReviewObservation> observations,IEnumerable<ReviewSchedule> schedules,ReviewSelectionContext context,int maximum=3)
    {
        if(maximum<0)throw new ArgumentOutOfRangeException(nameof(maximum));maximum=Math.Min(maximum,3);if(maximum==0||!context.SafeWindow||context.InputProtected)return [];
        var seen=Normalize(observations,context.PrivacyEpoch).Select(o=>o.FamilyRoot).ToHashSet(StringComparer.Ordinal);var due=schedules.ToDictionary(s=>s.Goal+":"+s.Modality,StringComparer.Ordinal);var candidates=new List<(PracticeIndexEntry Asset,string Reason,bool Requested)>();
        foreach(var asset in library)
        {
            if(asset.Release!=context.Release||asset.Target!=6.5m||asset.Modality is not(InputModality.Reading or InputModality.Listening or InputModality.Writing)||asset.Partition!=PracticePartition.Training||!asset.Available||!asset.CalibratedForDeclaredConditions||asset.Authority!=PracticeIndexAuthority.VerifiedContentProjection||asset.RequiredFacts.IsDefault||asset.RequiredAccess.IsDefault||!asset.RequiredFacts.All(context.KnownFacts.Contains)||!asset.RequiredAccess.All(context.Access.Contains))continue;
            bool requested=context.ExplicitGoals.Contains(asset.Goal);if(!requested&&context.Declines.Any(d=>d.Goal==asset.Goal&&(context.Clock.Trust!=ReviewClockTrust.VerifiedContinuity||d.OfferAgainAfter is null||context.Clock.Utc<d.OfferAgainAfter)))continue;
            bool repeat=seen.Contains(asset.FamilyRoot);if(repeat&&!requested)continue;bool difficulty=context.DifficultyGoals.Contains(asset.Goal);bool isDue=context.Clock.Trust==ReviewClockTrust.VerifiedContinuity&&due.TryGetValue(asset.Goal+":"+asset.Modality,out var schedule)&&schedule.Due is{} dueAt&&dueAt<=context.Clock.Utc;bool missing=context.MissingGoals.Contains(asset.Goal);
            if(requested||difficulty||isDue||missing)candidates.Add((asset,requested?repeat?"explicit_repeat_practice":"explicit_request":difficulty?"confirmed_recent_difficulty":isDue?"due_review":"coverage_gap",requested));
        }
        var selected=ImmutableArray.CreateBuilder<ReviewRecommendation>();var families=new HashSet<string>(StringComparer.Ordinal);var goals=new HashSet<string>(StringComparer.Ordinal);var topics=new Dictionary<string,int>(StringComparer.Ordinal);
        while(selected.Count<maximum)
        {
            var next=candidates.Where(c=>!families.Contains(c.Asset.FamilyRoot)&&!goals.Contains(c.Asset.Goal)).OrderByDescending(c=>c.Requested).ThenBy(c=>c.Reason=="confirmed_recent_difficulty"?0:c.Reason=="due_review"?1:2).ThenBy(c=>topics.GetValueOrDefault(c.Asset.Topic)).ThenBy(c=>c.Asset.Id,StringComparer.Ordinal).FirstOrDefault();if(next.Asset is null)break;
            var a=next.Asset;selected.Add(new(a.Id,a.Version,a.Title,a.Goal,a.Modality,a.FamilyRoot,a.Topic,next.Reason,!seen.Contains(a.FamilyRoot)));families.Add(a.FamilyRoot);goals.Add(a.Goal);topics[a.Topic]=topics.GetValueOrDefault(a.Topic)+1;
        }return selected.ToImmutable();
    }
}

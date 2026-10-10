using System.Collections.Immutable;
using Mist.Application.Contracts;
using Mist.Core.Learning;

namespace Mist.Application.Learning;

/// <summary>Learning data only. No IWorldCommands, inventory, rewards, recruitment or ending permissions.</summary>
public interface IRegionalLearningRepository
{
    ValueTask<RegionalLearningSnapshot> RegionalLearningAsync();
    ValueTask<RegionalReviewEvent> AppendRegionalReviewAsync(LearningIdentity expected,RegionalReviewEvent observation);
}
public sealed class RegionalLearningService
{
    private readonly RegionalLexicon _lexicon;
    private readonly IRegionalLearningRepository _history;
    private readonly IClock _clock;
    public RegionalLearningService(RegionalLexicon lexicon,IRegionalLearningRepository history,IClock clock)
    {RegionalLexicon.Validate(lexicon);_lexicon=lexicon;_history=history;_clock=clock;}
    public ImmutableArray<LexicalLookupView> Lookup(string query,IReadOnlyCollection<LearningSourceAccess> scope)=>_lexicon.Lookup(query).Select(e=>
        new LexicalLookupView(e.Id,e.Tier,e.Term,e.SenseKey,e.EnglishDefinition,e.ChineseAid,
            e.Contexts.Where(c=>RegionalReviewRules.Permitted(c,scope)).ToImmutableArray(),e.ReviewStatus,"Pronunciation not recorded; human sense/context review pending.")).ToImmutableArray();
    public async ValueTask<ImmutableArray<RegionalReviewCard>> DueAsync(IReadOnlyCollection<LearningSourceAccess> scope,bool preferListening=false,int limit=3)
    {
        if(limit is <1 or >100)throw new ArgumentOutOfRangeException(nameof(limit));
        var snapshot=await _history.RegionalLearningAsync();var states=RegionalReviewRules.States(snapshot.Events.Where(e=>e.LexiconVersion==_lexicon.ContentVersion&&_lexicon.Entries.Any(s=>s.Id==e.SenseId)));var now=_clock.UtcNow;
        return states.Where(s=>s.Due<=now).Take(limit).Select(s=>RegionalReviewRules.Card(_lexicon.Entries.Single(e=>e.Id==s.SenseId),s,snapshot,scope,preferListening)).ToImmutableArray();
    }
    public ValueTask<RegionalReviewEvent> ObserveLookupAsync(LearningIdentity identity,string senseId,LexicalContext? context,
        IReadOnlyCollection<LearningSourceAccess> scope,string attempt)=>RecordAsync(identity,senseId,RegionalReviewKind.Lookup,"",context,scope,attempt,"",false,0,false);
    public ValueTask<RegionalReviewEvent> ReviewAsync(LearningIdentity identity,string senseId,string rating,LexicalContext? context,
        IReadOnlyCollection<LearningSourceAccess> scope,string attempt,string response,bool supportUsed,bool production=false,bool listeningRequested=false)=>
        RecordAsync(identity,senseId,production?RegionalReviewKind.Production:RegionalReviewKind.Recall,rating,context,scope,attempt,response,supportUsed,0,listeningRequested);
    public ValueTask<RegionalReviewEvent> DeferAsync(LearningIdentity identity,string senseId,int days,string attempt)=>
        RecordAsync(identity,senseId,RegionalReviewKind.Defer,"",null,[],attempt,"",false,days,false);
    private async ValueTask<RegionalReviewEvent> RecordAsync(LearningIdentity identity,string senseId,RegionalReviewKind kind,string rating,
        LexicalContext? context,IReadOnlyCollection<LearningSourceAccess> scope,string attempt,string response,bool supportUsed,int days,bool listeningRequested)
    {
        bool production=kind==RegionalReviewKind.Production;
        if(string.IsNullOrWhiteSpace(attempt)||attempt.Length>100||response.Length>10000)throw new ArgumentException("Bounded attempt identity and response are required.");
        var sense=_lexicon.Entries.SingleOrDefault(e=>e.Id==senseId)??throw new ArgumentException("Unknown lexical sense.");
        var snapshot=await _history.RegionalLearningAsync();if(snapshot.Identity!=identity)throw new InvalidOperationException("Learning load or deletion epoch changed; reopen the view.");
        if(context is not null&&(!sense.Contexts.Contains(context)||!RegionalReviewRules.Permitted(context,scope)))
            throw new InvalidOperationException("Source is not currently acquired at this exact version and fingerprint.");
        if(listeningRequested&&(production||context is null||context.Modality!="listening"||!scope.Any(a=>a.MaterialId==context.MaterialId&&a.Version==context.Version&&a.Fingerprint==context.Fingerprint&&a.AudioAvailable&&a.AudioPlaybackStarted)))
            throw new InvalidOperationException("Listening practice requires this actual permitted audio to have started; a script or preload is insufficient.");
        if(kind==RegionalReviewKind.Defer&&days is not (1 or 3 or 7))throw new ArgumentException("Explicit deferral is one, three or seven days.");
        if((kind is RegionalReviewKind.Recall or RegionalReviewKind.Production)&&string.IsNullOrWhiteSpace(response))
            throw new ArgumentException("Display or playback without a response is not a completed review.");
        var prior=RegionalReviewRules.States(snapshot.Events).SingleOrDefault(s=>s.SenseId==senseId);
        var previousAttempt=snapshot.Events.SingleOrDefault(e=>e.AttemptId==attempt);
        if(previousAttempt is not null)
        {
            if(previousAttempt.SenseId!=senseId||previousAttempt.Kind!=kind||previousAttempt.SelfRating!=rating||previousAttempt.Response!=response||
                previousAttempt.MaterialId!=(context?.MaterialId??"")||previousAttempt.SourceVersion!=(context?.Version??"")||previousAttempt.SourceFingerprint!=(context?.Fingerprint??"")||previousAttempt.SupportUsed!=supportUsed)
                throw new InvalidOperationException("An attempt identity cannot be reused for another observation.");
            if(kind==RegionalReviewKind.Defer&&previousAttempt.IntervalDays!=days||(kind is RegionalReviewKind.Recall or RegionalReviewKind.Production)&&previousAttempt.Modality!=(production?"writing":listeningRequested?"listening":context is null?"definition_only":"reading"))
                throw new InvalidOperationException("An attempt identity cannot change its modality or deferral.");
            return previousAttempt;
        }
        if(kind is RegionalReviewKind.Recall or RegionalReviewKind.Production)_=RegionalReviewRules.NextInterval(rating,prior?.IntervalDays??0);
        // Self-ratings are useful notes, not a reliable assessment of an English response. Without an
        // actual evaluated independent new context, "good" cannot advance the mastery schedule.
        int interval=kind==RegionalReviewKind.Defer?days:kind==RegionalReviewKind.Lookup?prior?.IntervalDays??1:
            rating=="again"?1:rating=="hard"?Math.Min(3,prior?.IntervalDays??1):prior?.IntervalDays??1;
        DateTimeOffset now=_clock.UtcNow,due=kind==RegionalReviewKind.Lookup&&prior is not null?prior.Due:now.AddDays(interval);
        var access=context is null?null:scope.First(a=>a.MaterialId==context.MaterialId&&a.Version==context.Version&&a.Fingerprint==context.Fingerprint);
        // The UI selects a reading/listening activity explicitly; reading a script never becomes audio evidence.
        string modality=production?"writing":listeningRequested?"listening":context is null?"definition_only":"reading";
        var entry=new RegionalReviewEvent(Guid.NewGuid().ToString(),attempt,sense.Id,kind,rating,now,due,context?.MaterialId??"",context?.Version??"",context?.Fingerprint??"",
            context?.FamilyId??"",modality,response,access?.AudioAvailable??false,access?.AudioPlaybackStarted??false,access?.SubtitlesViewed??false,supportUsed,interval,_lexicon.ContentVersion,
            kind==RegionalReviewKind.Defer?"explicit_user_deferral":kind==RegionalReviewKind.Lookup?"initial_exposure":"self_report_not_assessed");
        return await _history.AppendRegionalReviewAsync(identity,entry);
    }
    public ValueTask<RegionalLearningSnapshot> SnapshotAsync()=>_history.RegionalLearningAsync();
}

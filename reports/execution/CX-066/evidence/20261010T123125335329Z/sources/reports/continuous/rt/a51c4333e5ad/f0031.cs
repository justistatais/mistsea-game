using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Mist.Core.Contracts;

namespace Mist.Core.Learning;

public sealed record LexicalContext(string MaterialId,string Version,string Fingerprint,string ParagraphId,
    string ParagraphEnglish,string ParagraphChinese,string Region,string FamilyId,string Modality,
    string AccessClass,string SourceFile,string SourceFileSha256,int StartScalar,int EndScalar,string MatchedForm);
public sealed record LexicalSense(string Id,string Tier,string Term,string SenseKey,string EnglishDefinition,
    string ChineseAid,ImmutableArray<string> Aliases,ImmutableArray<LexicalContext> Contexts,string ReviewStatus,
    string HumanReview,string PronunciationAsset,string PronunciationReview,string SemanticAlignment);
public sealed record RegionalLexicon(string ContentVersion,bool PlayerProjection,string Scope,
    ImmutableDictionary<string,decimal> TargetBands,string EmpiricalCalibration,string HumanReview,
    string RightsReview,string CountPolicy,ImmutableArray<LexicalSense> Entries)
{
    public static RegionalLexicon Parse(string json)
    {
        var lexicon=StrictJson.Deserialize<RegionalLexicon>(json);Validate(lexicon);return lexicon;
    }
    public static void Validate(RegionalLexicon p)
    {
        if(!p.PlayerProjection||p.Scope!="authored_offline_candidate"||p.EmpiricalCalibration!="not_run"||p.HumanReview!="human_pending"||p.Entries.IsDefaultOrEmpty||p.Entries.Length!=720||
            p.Entries.Count(e=>e.Tier=="core")!=360||p.Entries.Count(e=>e.Tier=="extended")!=360||p.TargetBands.Count!=3||
            !new[]{"reading","listening","writing"}.All(k=>p.TargetBands.TryGetValue(k,out var band)&&band==6.5m))
            throw new InvalidDataException("Lexicon requires 360 CORE + 360 EXTENDED original candidates; empirical and human gates remain pending.");
        if(p.Entries.Select(e=>e.Id).Distinct(StringComparer.Ordinal).Count()!=720||p.Entries.Select(e=>e.Term+"|"+e.SenseKey).Distinct(StringComparer.Ordinal).Count()!=720)
            throw new InvalidDataException("Inflections, modalities and duplicate senses cannot inflate the count.");
        foreach(var e in p.Entries)
        {
            if(string.IsNullOrWhiteSpace(e.Term)||string.IsNullOrWhiteSpace(e.EnglishDefinition)||string.IsNullOrWhiteSpace(e.ChineseAid)||e.Contexts.IsDefaultOrEmpty||
                e.ReviewStatus!="original_unreviewed"||e.HumanReview!="human_pending"||e.SemanticAlignment!="candidate_requires_human_review"||
                e.PronunciationAsset!=""||e.PronunciationReview!="not_recorded")throw new InvalidDataException("A sense needs original definitions, actual prose and honest review state.");
            foreach(var c in e.Contexts)
            {
                if(string.IsNullOrWhiteSpace(c.MaterialId)||string.IsNullOrWhiteSpace(c.Version)||string.IsNullOrWhiteSpace(c.FamilyId)||
                    !Regex.IsMatch(c.Fingerprint,"^[a-f0-9]{64}$",RegexOptions.CultureInvariant)||!Regex.IsMatch(c.SourceFileSha256,"^[a-f0-9]{64}$",RegexOptions.CultureInvariant)||
                    c.Modality is not ("reading" or "listening")||!c.SourceFile.StartsWith("content/source/",StringComparison.Ordinal)||c.SourceFile.Contains("author_only",StringComparison.OrdinalIgnoreCase)||
                    !string.Equals(UnicodeOffsets.Slice(c.ParagraphEnglish,c.StartScalar,c.EndScalar),c.MatchedForm,StringComparison.OrdinalIgnoreCase)||
                    !e.Aliases.Append(e.Term).Any(f=>string.Equals(f,c.MatchedForm,StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Source binding, scalar offsets or matched form is invalid.");
            }
        }
    }
    public ImmutableArray<LexicalSense> Lookup(string query)
    {
        string term=query.Trim();if(term.Length==0||term.Length>100)return [];
        return Entries.Where(e=>string.Equals(e.Term,term,StringComparison.OrdinalIgnoreCase)||e.Aliases.Any(a=>string.Equals(a,term,StringComparison.OrdinalIgnoreCase))).ToImmutableArray();
    }
}

/// <summary>Issued by a current permission read, not by entering a chapter or trusting a cached title.</summary>
public sealed record LearningSourceAccess(string MaterialId,string Version,string Fingerprint,bool AudioAvailable=false,
    bool AudioPlaybackStarted=false,bool SubtitlesViewed=false);
public sealed record LearningIdentity(ProfileId Profile,BranchId Branch,LoadContext Load,long PrivacyEpoch,long WorldSequence);
public enum RegionalReviewKind { Lookup, Recall, Production, Defer }
public sealed record RegionalReviewEvent(string Id,string AttemptId,string SenseId,RegionalReviewKind Kind,
    string SelfRating,DateTimeOffset At,DateTimeOffset Due,string MaterialId,string SourceVersion,string SourceFingerprint,
    string FamilyId,string Modality,string Response,bool AudioAvailable,bool AudioPlaybackStarted,bool SubtitlesViewed,
    bool SupportUsed,int IntervalDays,string LexiconVersion,string ScheduleBasis);
public sealed record RegionalLearningSnapshot(LearningIdentity Identity,ImmutableArray<RegionalReviewEvent> Events,
    ImmutableArray<string> SeenFamilies);
public sealed record RegionalReviewState(string SenseId,DateTimeOffset Due,int IntervalDays,string LastRating,
    int RecallAttempts,int ProductionAttempts,bool Deferred);
public sealed record LexicalLookupView(string Id,string Tier,string Term,string SenseKey,string EnglishDefinition,
    string ChineseAid,ImmutableArray<LexicalContext> PermittedContexts,string ReviewStatus,string PronunciationStatus);
public sealed record RegionalReviewCard(string SenseId,string Term,string EnglishDefinition,string ChineseAid,
    LexicalContext? Context,string Activity,bool UnfamiliarFamily,bool ScriptOnly,DateTimeOffset Due,string Limitation);

public static class RegionalReviewRules
{
    public static bool Permitted(LexicalContext c,IReadOnlyCollection<LearningSourceAccess> scope)=>scope.Any(a=>
        a.MaterialId==c.MaterialId&&a.Version==c.Version&&a.Fingerprint==c.Fingerprint);
    public static ImmutableArray<RegionalReviewState> States(IEnumerable<RegionalReviewEvent> events)
    {
        return events.GroupBy(e=>e.SenseId,StringComparer.Ordinal).Select(g=>
        {
            var ordered=g.OrderBy(e=>e.At).ToArray();var last=ordered[^1];
            return new RegionalReviewState(g.Key,last.Due,last.IntervalDays,last.SelfRating,
                ordered.Count(e=>e.Kind==RegionalReviewKind.Recall),ordered.Count(e=>e.Kind==RegionalReviewKind.Production),last.Kind==RegionalReviewKind.Defer);
        }).OrderBy(e=>e.Due).ThenBy(e=>e.SenseId,StringComparer.Ordinal).ToImmutableArray();
    }
    public static int NextInterval(string rating,int previous)=>rating switch
    {
        "again"=>1,"hard"=>3,"good"=>Math.Min(30,Math.Max(7,checked(previous*2))),
        _=>throw new ArgumentException("A self-rating is again, hard or good; it is not a score or band estimate.")
    };
    public static RegionalReviewCard Card(LexicalSense sense,RegionalReviewState state,RegionalLearningSnapshot snapshot,
        IReadOnlyCollection<LearningSourceAccess> scope,bool preferListening)
    {
        var eligible=sense.Contexts.Where(c=>Permitted(c,scope)).ToArray();
        var listening=eligible.Where(c=>c.Modality=="listening"&&scope.Any(a=>a.MaterialId==c.MaterialId&&a.Version==c.Version&&a.Fingerprint==c.Fingerprint&&a.AudioAvailable)).ToArray();
        var pool=preferListening&&listening.Length>0?listening:eligible;
        var context=pool.OrderBy(c=>snapshot.SeenFamilies.Contains(c.FamilyId,StringComparer.Ordinal)?1:0)
            .ThenBy(c=>c.Modality=="reading"?0:1).ThenBy(c=>c.MaterialId,StringComparer.Ordinal).FirstOrDefault();
        bool unfamiliar=context is not null&&!snapshot.SeenFamilies.Contains(context.FamilyId,StringComparer.Ordinal);
        bool script=context?.Modality=="listening"&&!scope.Any(a=>a.MaterialId==context.MaterialId&&a.Version==context.Version&&a.Fingerprint==context.Fingerprint&&a.AudioAvailable);
        string activity=context is null?"definition_recall_without_source":context.Modality=="listening"&&!script&&preferListening?"listening_recall":"reading_recall";
        return new(sense.Id,sense.Term,sense.EnglishDefinition,sense.ChineseAid,context,activity,unfamiliar,script,state.Due,
            "Local practice and self-rating only; no band estimate, world reward, task unlock or human certification.");
    }
}

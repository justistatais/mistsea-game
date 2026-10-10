using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mist.Application.Contracts;
using Mist.Application.Learning;
using Mist.Core.Contracts;
using Mist.Core.Learning;
using Mist.Infrastructure.Storage;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.Learning;

public static class RegionalLearningCases
{
    private static string ProjectRoot=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    public static RegionalLexicon Package()=>RegionalLexicon.Parse(File.ReadAllText(Path.Combine(ProjectRoot,"content/source/lexicon/learner_lexicon.json")));
    private static readonly Lazy<RegionalLexicon> Data=new(Package);
    private sealed class Clock : IClock { public DateTimeOffset UtcNow {get;set;}=new(2026,10,10,0,0,0,TimeSpan.Zero); }
    private static ProfileStore Store()=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"learning-"+Guid.NewGuid()),"mistsea-internal-0.1");
    private static RegionalLearningService Service(ProfileStore store,Clock clock)=>new(Data.Value,store,clock);
    private static LearningIdentity Identity(ProfileStore store)=>Done(store.RegionalLearningAsync()).Identity;
    private static LearningSourceAccess Scope(LexicalContext c,bool audio=false,bool played=false,bool subtitles=false)=>new(c.MaterialId,c.Version,c.Fingerprint,audio,played,subtitles);
    private static LexicalSense Sense(string word)=>Data.Value.Entries.Single(e=>e.Term==word);
    private static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    public static void Register()
    {
        Add("CX-066","lexicon_has_360_core_360_extended_real_unique_source_bound_senses",()=>
        {
            var p=Data.Value;Require(p.Entries.Length==720&&p.Entries.Count(e=>e.Tier=="core")==360&&p.Entries.Count(e=>e.Tier=="extended")==360);
            Require(p.Entries.Select(e=>e.Term+"|"+e.SenseKey).Distinct().Count()==720&&p.Entries.All(e=>e.Contexts.Length>0&&e.EnglishDefinition.Length>12&&e.ChineseAid.Length>0));
            Require(p.EmpiricalCalibration=="not_run"&&p.Entries.All(e=>e.HumanReview=="human_pending"&&e.PronunciationAsset==""));
        });
        Add("CX-066","lexicon_all_contexts_match_exact_actual_learner_paragraphs_versions_hashes_and_unicode_coordinates",()=>
        {
            var docs=new Dictionary<string,JsonDocument>();
            try
            {
                foreach(var c in Data.Value.Entries.SelectMany(e=>e.Contexts))
                {
                    if(!docs.TryGetValue(c.SourceFile,out var d)){d=JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectRoot,c.SourceFile)));docs.Add(c.SourceFile,d);}
                    var (body,paragraph,version)=RegionalLexicalSourceProjection.Read(d.RootElement,c);
                    Require(Hash(body)==c.Fingerprint&&paragraph==c.ParagraphEnglish&&UnicodeOffsets.Slice(paragraph,c.StartScalar,c.EndScalar)==c.MatchedForm,c.MaterialId+" source mismatch");
                    Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(ProjectRoot,c.SourceFile)))).ToLowerInvariant()==c.SourceFileSha256,c.SourceFile+" fingerprint drift; rebuild candidate bindings rather than accepting stale prose");
                    Require(version==c.Version);
                }
            }
            finally{foreach(var doc in docs.Values)doc.Dispose();}
        });
        Add("CX-066","lexicon_existing_senses_include_actual_life_first_field_credit_study_source_bodies_without_new_senses_or_permission",()=>
        {
            var p=Data.Value;Require(p.ContentVersion=="LEX-20261010-v3"&&p.Entries.Length==720&&p.Entries.Count(e=>e.Tier=="core")==360&&p.Entries.Count(e=>e.Tier=="extended")==360);
            string[] required=["R1-ROOM-R-OLD","R1-ROOM-L-GUESTS","R1-SHOW-L-CREW","R2-O01-R-LETTER","R2-O02-R-SEEDS","R0-APP-R-RELAY","R1-APP-R-SIDE","R1-APP-R-OUTER","R6-FIELD-RELAY-R-SCOPE","R4-O02-CREDIT-R-LABEL","R4-O02-CREDIT-R-BOOK","R4-O02-CREDIT-R-PERMIT","R4-O01-STUDY-R-PROCEDURE","R4-O01-STUDY-R-REPLY-SCOPE"];
            foreach(string id in required)
            {
                var sense=p.Entries.First(e=>e.Contexts.Any(c=>c.MaterialId==id));var context=sense.Contexts.First(c=>c.MaterialId==id);using var store=Store();var app=Service(store,new());
                Require(app.Lookup(sense.Term,[]).Single(e=>e.Id==sense.Id).PermittedContexts.IsEmpty);
                Require(app.Lookup(sense.Term,[Scope(context)]).Single(e=>e.Id==sense.Id).PermittedContexts.Any(c=>c.MaterialId==id));
                Require(app.Lookup(sense.Term,[Scope(context) with{Fingerprint=new string('0',64)}]).All(e=>e.PermittedContexts.IsEmpty));
            }
            var old=RegionalLexicon.Parse(File.ReadAllText(Path.Combine(ProjectRoot,"content/source/lexicon/history.author_only/LEX-20261010-v1.json")));
            Require(old.Entries.Select(e=>StrictJson.Serialize(e with{Contexts=[]})).SequenceEqual(p.Entries.Select(e=>StrictJson.Serialize(e with{Contexts=[]}))));
        });
        Add("CX-066","lexicon_v3_keeps_v2_senses_and_binds_actual_lina_shift_optional_settlement_oldcity_and_received_reply_bodies",()=>
        {
            var p=Data.Value;var old=RegionalLexicon.Parse(File.ReadAllText(Path.Combine(ProjectRoot,"content/source/lexicon/history.author_only/LEX-20261010-v2.json")));
            Require(p.ContentVersion=="LEX-20261010-v3"&&p.Entries.Length==720&&old.Entries.Select(e=>StrictJson.Serialize(e with{Contexts=[]})).SequenceEqual(p.Entries.Select(e=>StrictJson.Serialize(e with{Contexts=[]}))));
            string[] required=["LI01-APPENDIX","LI01-INDEX","LI02-RELAY","LI02-DIRECT","LI02-DELIVERY","LI03-REPLY","LI03-MEETING","LI03-TOPICS","R3-SHIFT-R-BOARD","R3-SHIFT-R-PROMISE","R3-SHIFT-R-SCOPE","R3-SHIFT-R-SELA","R3-SHIFT-R-TOBIN","R3-SHIFT-R-NIA","settlement:O-R6-01","settlement:O-R6-02","old-city-life:SC-R5-SIGN","old-city-life:SC-R5-LOST","SP-CP-MIRA-RENEW","SP-W04-RENEW","SP-CP-MIRA-DENY","SP-W04-DENY","SP-LOCAL-RECEIVER","SP-REMOTE-REVIEW"];
            foreach(string id in required.Concat(Enumerable.Range(1,6).SelectMany(region=>new[]{"R"+region+"-OPT-O01-R-CONTRACT","R"+region+"-OPT-O02-R-CONTRACT"})))
            {
                var sense=p.Entries.First(e=>e.Contexts.Any(c=>c.MaterialId==id));var context=sense.Contexts.First(c=>c.MaterialId==id);using var store=Store();var app=Service(store,new());
                Require(app.Lookup(sense.Term,[]).All(v=>v.PermittedContexts.IsEmpty));
                var permitted=app.Lookup(sense.Term,[Scope(context)]).Single(v=>v.Id==sense.Id).PermittedContexts;
                Require(!permitted.IsEmpty&&permitted.All(c=>c.MaterialId==id&&c.Version==context.Version&&c.Fingerprint==context.Fingerprint));
                Require(app.Lookup(sense.Term,[Scope(context) with{Version="previous-unrenewed"}]).All(v=>v.PermittedContexts.IsEmpty));
                Require(app.Lookup(sense.Term,[Scope(context) with{Fingerprint=new string('0',64)}]).All(v=>v.PermittedContexts.IsEmpty));
            }
        });
        Add("CX-066","lexicon_npc_current_turn_scope_never_opens_future_turn_or_completed_replay",()=>
        {
            var p=Data.Value;var sense=p.Entries.First(e=>e.Contexts.Any(c=>c.AccessClass=="current_line_permission"));var turn=sense.Contexts.First(c=>c.AccessClass=="current_line_permission");
            string replayId=turn.MaterialId[..turn.MaterialId.IndexOf(':')]+":replay";var replay=sense.Contexts.Single(c=>c.MaterialId==replayId);
            Require(turn.Fingerprint!=replay.Fingerprint&&turn.Modality=="reading"&&replay.Modality=="reading");using var store=Store();var app=Service(store,new());
            var current=app.Lookup(sense.Term,[Scope(turn)]).Single(v=>v.Id==sense.Id);Require(current.PermittedContexts.Length==1&&current.PermittedContexts[0].MaterialId==turn.MaterialId);
            var complete=app.Lookup(sense.Term,[Scope(replay)]).Single(v=>v.Id==sense.Id);Require(complete.PermittedContexts.Length==1&&complete.PermittedContexts[0].MaterialId==replayId);
            Require(app.Lookup(sense.Term,[Scope(turn) with{Fingerprint=replay.Fingerprint}]).All(v=>v.PermittedContexts.IsEmpty));
            Require(app.Lookup(sense.Term,[]).All(v=>v.PermittedContexts.IsEmpty)&&Done(store.SnapshotAsync()).Sequence==0);
        });
        Add("CX-066","lexicon_unknown_json_fields_and_author_projection_are_rejected",()=>
        {
            string json=StrictJson.Serialize(Data.Value);Throws<JsonException>(()=>RegionalLexicon.Parse(json[..^1]+",\"trusted\":true}"));
            Throws<InvalidDataException>(()=>RegionalLexicon.Validate(Data.Value with{PlayerProjection=false}));
        });
        Add("CX-066","lexicon_rejects_mismatched_scalar_excerpt_and_claimed_audio",()=>
        {
            var p=Data.Value;var e=p.Entries[0];var c=e.Contexts[0];
            Throws<InvalidDataException>(()=>RegionalLexicon.Validate(p with{Entries=p.Entries.SetItem(0,e with{Contexts=e.Contexts.SetItem(0,c with{MatchedForm="invented"})})}));
            Throws<InvalidDataException>(()=>RegionalLexicon.Validate(p with{Entries=p.Entries.SetItem(0,e with{PronunciationAsset="fake.wav"})}));
        });
        Add("CX-066","dictionary_draft_shipping_and_prose_senses_share_alias_without_inflating_counts",()=>
        {
            var entries=Data.Value.Lookup(" draft ");Require(entries.Length==2&&entries.Any(e=>e.SenseKey=="shipping_depth")&&entries.Any(e=>e.SenseKey=="preliminary_writing"));
            Require(Data.Value.Lookup("not-a-listed-term").IsEmpty);
        });
        Add("CX-066","dictionary_future_and_private_source_body_never_leaks_without_current_exact_permission",()=>
        {
            using var s=Store();var app=Service(s,new());var e=Data.Value.Entries.First(e=>e.Contexts.Any(c=>c.AccessClass=="individual_permission"));
            var none=app.Lookup(e.Term,[]).Single(v=>v.Id==e.Id);Require(none.PermittedContexts.IsEmpty&&!string.IsNullOrEmpty(none.EnglishDefinition));
            var c=e.Contexts.First(c=>c.AccessClass=="individual_permission");var view=app.Lookup(e.Term,[Scope(c)]).Single(v=>v.Id==e.Id);Require(view.PermittedContexts.All(p=>p.MaterialId==c.MaterialId&&p.Version==c.Version&&p.Fingerprint==c.Fingerprint));
            Require(app.Lookup(e.Term,[Scope(c) with{Version="stale"}]).All(v=>v.PermittedContexts.IsEmpty));
        });
        Add("CX-066","lookup_is_initial_exposure_due_next_real_day_without_world_mutation",()=>
        {
            using var s=Store();var clock=new Clock();var app=Service(s,clock);var before=StrictJson.Serialize(Done(s.SnapshotAsync()));var e=Sense("route");var c=e.Contexts[0];
            var result=Done(app.ObserveLookupAsync(Identity(s),e.Id,c,[Scope(c)],"lookup-first"));Require(result.Due==clock.UtcNow.AddDays(1));Require(Done(app.DueAsync([Scope(c)])).IsEmpty);
            clock.UtcNow=clock.UtcNow.AddDays(1);Require(Done(app.DueAsync([Scope(c)])).Length==1);Require(StrictJson.Serialize(Done(s.SnapshotAsync()))==before);
        });
        Add("CX-066","repeated_lookup_does_not_postpone_due_or_grant_new_mastery",()=>
        {
            using var s=Store();var clock=new Clock();var app=Service(s,clock);var e=Sense("route");var c=e.Contexts[0];var first=Done(app.ObserveLookupAsync(Identity(s),e.Id,c,[Scope(c)],"first"));
            clock.UtcNow=clock.UtcNow.AddHours(12);var second=Done(app.ObserveLookupAsync(Identity(s),e.Id,c,[Scope(c)],"second"));Require(second.Due==first.Due&&second.IntervalDays==1);
        });
        Add("CX-066","same_attempt_replay_writes_once_and_different_payload_is_rejected",()=>
        {
            using var s=Store();var app=Service(s,new());var e=Sense("route");var c=e.Contexts[0];var identity=Identity(s);
            var first=Done(app.ObserveLookupAsync(identity,e.Id,c,[Scope(c)],"one"));var second=Done(app.ObserveLookupAsync(identity,e.Id,c,[Scope(c)],"one"));Require(first==second&&Done(s.RegionalLearningAsync()).Events.Length==1);
            Throws<InvalidOperationException>(()=>Done(app.ObserveLookupAsync(identity,Sense("record").Id,null,[],"one")));
        });
        Add("CX-066","good_self_rating_and_dictionary_help_do_not_advance_unassessed_mastery_schedule",()=>
        {
            using var s=Store();var clock=new Clock();var app=Service(s,clock);var e=Sense("route");var c=e.Contexts[0];Done(app.ObserveLookupAsync(Identity(s),e.Id,c,[Scope(c)],"lookup"));clock.UtcNow=clock.UtcNow.AddDays(1);
            var review=Done(app.ReviewAsync(Identity(s),e.Id,"good",c,[Scope(c)],"helped","A path between places.",true));Require(review.IntervalDays==1&&review.ScheduleBasis=="self_report_not_assessed"&&review.SupportUsed);
        });
        Add("CX-066","production_saves_actual_english_response_without_automatic_grade_or_rewards",()=>
        {
            using var s=Store();var clock=new Clock();var app=Service(s,clock);var before=StrictJson.Serialize(Done(s.SnapshotAsync()));var e=Sense("route");
            var review=Done(app.ReviewAsync(Identity(s),e.Id,"hard",null,[],"production","We checked the rescue route before departing.",false,true));
            Require(review.Modality=="writing"&&review.Response.Contains("rescue route",StringComparison.Ordinal)&&review.ScheduleBasis=="self_report_not_assessed"&&review.IntervalDays==1);
            Require(StrictJson.Serialize(Done(s.SnapshotAsync()))==before);
        });
        Add("CX-066","empty_response_or_invalid_rating_is_not_completed_review",()=>
        {
            using var s=Store();var app=Service(s,new());var e=Sense("route");Throws<ArgumentException>(()=>Done(app.ReviewAsync(Identity(s),e.Id,"good",null,[],"empty","",false)));
            Throws<ArgumentException>(()=>Done(app.ReviewAsync(Identity(s),e.Id,"band_7",null,[],"bad-rating","text",false)));Require(Done(s.RegionalLearningAsync()).Events.IsEmpty);
        });
        Add("CX-066","defer_is_explicit_bounded_preference_and_survives_reopen",()=>
        {
            using var s=Store();var clock=new Clock();var app=Service(s,clock);var e=Sense("route");var identity=Identity(s);var result=Done(app.DeferAsync(identity,e.Id,3,"later"));
            Require(result.Due==clock.UtcNow.AddDays(3)&&result.ScheduleBasis=="explicit_user_deferral");Throws<ArgumentException>(()=>Done(app.DeferAsync(identity,e.Id,300,"too-far")));
            Throws<InvalidOperationException>(()=>Done(app.DeferAsync(identity,e.Id,7,"later")));Done(s.ContinueAsync());Require(Done(s.RegionalLearningAsync()).Events.Single()==result);
        });
        Add("CX-066","default_due_recommendation_is_three_and_never_interrupts_world",()=>
        {
            using var s=Store();var clock=new Clock();var app=Service(s,clock);foreach(var e in Data.Value.Entries.Take(6))Done(app.ObserveLookupAsync(Identity(s),e.Id,null,[],e.Id));
            clock.UtcNow=clock.UtcNow.AddDays(1);Require(Done(app.DueAsync([])).Length==3&&Done(app.DueAsync([],limit:100)).Length==6);
        });
        Add("CX-066","review_prefers_current_compatible_unseen_family_and_keeps_familiar_label",()=>
        {
            using var s=Store();var clock=new Clock();var app=Service(s,clock);var e=Data.Value.Entries.First(e=>e.Contexts.Select(c=>c.FamilyId).Distinct().Count()>1);var c=e.Contexts[0];
            Done(app.ObserveLookupAsync(Identity(s),e.Id,c,[Scope(c)],"first"));clock.UtcNow=clock.UtcNow.AddDays(1);
            var all=e.Contexts.Select(c=>Scope(c)).Distinct().ToArray();var card=Done(app.DueAsync(all)).Single();Require(card.UnfamiliarFamily&&card.Context is not null&&card.Context.FamilyId!=c.FamilyId);
            var familiar=Done(app.DueAsync([Scope(c)])).Single();Require(!familiar.UnfamiliarFamily&&familiar.Context?.FamilyId==c.FamilyId);
        });
        Add("CX-066","missing_audio_selects_script_reading_and_cannot_be_listening_evidence",()=>
        {
            using var s=Store();var clock=new Clock();var app=Service(s,clock);var e=Data.Value.Entries.First(e=>e.Contexts.Any(c=>c.Modality=="listening"));var c=e.Contexts.First(c=>c.Modality=="listening");
            Done(app.ObserveLookupAsync(Identity(s),e.Id,c,[Scope(c)],"lookup"));clock.UtcNow=clock.UtcNow.AddDays(1);var card=Done(app.DueAsync([Scope(c)],true)).Single();Require(card.ScriptOnly&&card.Activity=="reading_recall");
            Throws<InvalidOperationException>(()=>Done(app.ReviewAsync(Identity(s),e.Id,"good",c,[Scope(c)],"fake-listen","response",false,listeningRequested:true)));
            var reading=Done(app.ReviewAsync(Identity(s),e.Id,"good",c,[Scope(c)],"read-script","response",false));Require(reading.Modality=="reading"&&!reading.AudioPlaybackStarted);
        });
        Add("CX-066","audio_preload_cannot_count_as_listening_and_subtitles_remain_recorded",()=>
        {
            using var s=Store();var app=Service(s,new());var e=Data.Value.Entries.First(e=>e.Contexts.Any(c=>c.Modality=="listening"));var c=e.Contexts.First(c=>c.Modality=="listening");
            Throws<InvalidOperationException>(()=>Done(app.ReviewAsync(Identity(s),e.Id,"good",c,[Scope(c,true)],"preload","response",false,listeningRequested:true)));
            // Explicit engineering audio-state fixture: this tests routing facts, not human listening or real waveform quality.
            var observed=Done(app.ReviewAsync(Identity(s),e.Id,"good",c,[Scope(c,true,true,true)],"actual-fixture-play","response",false,listeningRequested:true));Require(observed.Modality=="listening"&&observed.SubtitlesViewed&&observed.ScheduleBasis=="self_report_not_assessed");
        });
        Add("CX-066","withdrawn_or_changed_source_scope_rejects_review_without_writing",()=>
        {
            using var s=Store();var app=Service(s,new());var e=Sense("route");var c=e.Contexts[0];Throws<InvalidOperationException>(()=>Done(app.ReviewAsync(Identity(s),e.Id,"good",c,[],"withdrawn","response",false)));
            Throws<InvalidOperationException>(()=>Done(app.ReviewAsync(Identity(s),e.Id,"good",c,[Scope(c) with{Fingerprint=new string('0',64)}],"changed","response",false)));Require(Done(s.RegionalLearningAsync()).Events.IsEmpty);
        });
        Add("CX-066","changed_world_sequence_invalidates_source_context_even_when_load_same",()=>
        {
            using var s=Store();var app=Service(s,new());var identity=Identity(s);Done(s.ExecuteAsync(Plan(s,"fixture:permission-withdrawn",0)));
            Throws<InvalidOperationException>(()=>Done(app.ObserveLookupAsync(identity,Sense("route").Id,null,[],"late")));Require(Done(s.RegionalLearningAsync()).Events.IsEmpty);
        });
        Add("CX-066","load_checkpoint_rejects_old_view_without_erasing_current_epoch_history",()=>
        {
            using var s=Store();var app=Service(s,new());var checkpoint=Done(s.CreateCheckpointAsync("manual"));var identity=Identity(s);Done(app.ObserveLookupAsync(identity,Sense("route").Id,null,[],"before-load"));Done(s.LoadCheckpointAsync(checkpoint));
            Throws<InvalidOperationException>(()=>Done(app.ObserveLookupAsync(identity,Sense("record").Id,null,[],"late")));Require(Done(s.RegionalLearningAsync()).Events.Length==1);
        });
        Add("CX-066","learning_erasure_removes_reviews_answers_and_old_backup_cannot_revive_them",()=>
        {
            using var s=Store();var app=Service(s,new());var identity=Identity(s);Done(app.ReviewAsync(identity,Sense("route").Id,"good",null,[],"secret","private response to erase",false));var backup=Done(s.BackupAsync());Done(s.EraseAsync("learning"));
            Require(Done(s.RegionalLearningAsync()).Events.IsEmpty);Done(s.RestoreBackupAsync(backup.Id));Require(Done(s.RegionalLearningAsync()).Events.IsEmpty&&Done(s.ObservationCountAsync())==0);
            Throws<InvalidOperationException>(()=>Done(app.ObserveLookupAsync(identity,Sense("route").Id,null,[],"late-erased")));
        });
        Add("CX-066","queued_stale_epoch_append_is_rejected_after_erasure_without_partial_row",()=>
        {
            using var s=Store();var app=Service(s,new());var identity=Identity(s);var e=Done(app.ObserveLookupAsync(identity,Sense("route").Id,null,[],"before"));Done(s.EraseAsync("learning"));
            var stale=e with{Id=Guid.NewGuid().ToString(),AttemptId="late-queued"};Throws<InvalidOperationException>(()=>Done(s.AppendRegionalReviewAsync(identity,stale)));Require(Done(s.RegionalLearningAsync()).Events.IsEmpty);
        });
        Add("CX-066","delete_and_concurrent_local_append_serialize_without_old_epoch_resurrection",()=>
        {
            using var s=Store();var app=Service(s,new());var identity=Identity(s);var first=Done(app.ObserveLookupAsync(identity,Sense("route").Id,null,[],"first"));
            var writes=Enumerable.Range(0,8).Select(i=>s.AppendRegionalReviewAsync(identity,first with{Id=Guid.NewGuid().ToString(),AttemptId="racing-"+i}).AsTask()).ToArray();
            var erased=s.EraseAsync("learning").AsTask();Task.WhenAll(writes).GetAwaiter().GetResult();erased.GetAwaiter().GetResult();Require(Done(s.RegionalLearningAsync()).Events.IsEmpty);
            Throws<InvalidOperationException>(()=>Done(s.AppendRegionalReviewAsync(identity,first with{Id=Guid.NewGuid().ToString(),AttemptId="post-erasure"})));
        });
        Add("CX-066","regional_learning_reopen_preserves_due_history_but_never_settings_or_world_rewards",()=>
        {
            string root;RegionalReviewEvent entry;
            using(var s=Store()){root=s.RootPath;var app=Service(s,new());entry=Done(app.ReviewAsync(Identity(s),Sense("route").Id,"again",null,[],"persisted","We followed the marked route.",false));}
            using var reopen=new ProfileStore(root,"mistsea-internal-0.1");Require(Done(reopen.RegionalLearningAsync()).Events.Single()==entry);Require(Done(reopen.SnapshotAsync()).Balances.Count==0&&Done(reopen.SnapshotAsync()).Receipts.IsEmpty);
        });
    }
}

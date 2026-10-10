using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mist.Application.ReviewCalibration;
using Mist.Core.Contracts;
using Mist.Core.Learning;
using Mist.Core.ReviewCalibration;
using Mist.Infrastructure.Storage;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.ReviewCalibration;

public static class ReviewCalibrationCases
{
    // All authority/participant examples below are explicit engineering rule inputs. They are
    // not actual human reviews, target-group measurements, English scoring or online calls.
    private static readonly DateTimeOffset Start=new(2026,1,1,0,0,0,TimeSpan.Zero);
    private const string Release="mistsea-internal-0.1";
    private static string H(string text)=>CalibrationRules.Hash(text);
    private static string SourceJson()=>File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../reference/part05/data/Original_Materials_v0.1.json")));
    private static ReviewObservation O(string id,string family="family1",bool? correct=true,InputModality modality=InputModality.Reading)=>new(id,"R03",modality,"fixture-material-"+family,"fixture-v1",H(family),family,"context1","session1",H("fixture-response-"+id),Start,0,correct,true,true,true,false,false,true,true,0,false,1m,[],ReviewClockTrust.VerifiedContinuity,ReviewResultAuthority.VerifiedLocalObjectiveRule);
    private static PracticeIndexEntry Asset(string id,string goal="R03",string? family=null,string topic="transport")=>new(id,"fixture-v1",H(id),"Explicit fixture "+id,goal,InputModality.Reading,family??"family-"+id,topic,Release,6.5m,PracticePartition.Training,true,true,PracticeIndexAuthority.VerifiedContentProjection,[],[]);
    private static ReviewSelectionContext Context()=>new(Release,0,new(Start.AddDays(10),ReviewClockTrust.VerifiedContinuity),true,false,ImmutableHashSet<string>.Empty,ImmutableHashSet<string>.Empty,ImmutableHashSet<string>.Empty,ImmutableHashSet<string>.Empty,ImmutableHashSet.Create("R01","R02","R03","R04","R05"),[]);
    private static ReviewSchedule Schedule(int stage=0)=>new("R03",InputModality.Reading,stage,null,[],[]);
    private static CalibrationBinding Binding(AssessmentTaskKind kind=AssessmentTaskKind.AcademicTask2,InputModality modality=InputModality.Writing)=>new("fixture-calibration-material","fixture-v1",modality,kind,H("text"),H("questions"),modality==InputModality.Listening?H("audio"):"not_applicable",H("support"),H("order"),H("tools"),"fixture-skills-v1","fixture-evaluator-disabled","fixture-rules-v1");
    private static CalibrationProtocol Protocol(CalibrationBinding? binding=null)
    {var b=binding??Binding();return new(b,"IELTS Academic","explicit-fixture-reference-version",b.Modality.ToString().ToLowerInvariant(),"relevant section 6.0/6.5/7.0; planning only","predeclared exclusions; fixture protocol","explicit fixture conditions; not externally verified");}
    private static CalibrationRecord Record(CalibrationRole role,CalibrationBinding binding,CalibrationAuthority authority=CalibrationAuthority.VerifiedHumanAuthority,string? person=null)=>new("fixture-record-"+role,role,person??"hypothetical-verified-human-"+role,CalibrationRules.BindingHash(binding),H("fixture-evidence-"+role),"accepted",authority);
    private static PilotRow Row(string person,string attempt,CalibrationBinding binding,int correct=6,int incorrect=2,int unanswered=2)=>new(person,attempt,CalibrationRules.BindingHash(binding),binding.Modality,6.5m,SectionReferenceTrust.VerifiedRelevantSection,true,Start,correct,incorrect,unanswered,false,true,false,"unassisted fixture");
    private static BlindWritingRating Rating(string person,CalibrationBinding binding,string response,decimal score=6)=>new(person,CalibrationRules.BindingHash(binding),response,true,true,CalibrationAuthority.VerifiedHumanAuthority,new Dictionary<string,decimal>{{binding.TaskKind==AssessmentTaskKind.AcademicTask1?"task_achievement":"task_response",score},{"coherence_cohesion",score},{"lexical_resource",score},{"grammatical_range_accuracy",score}}.ToImmutableDictionary());
    private sealed class SqliteProjection(ProfileStore store):IReadOnlyReviewProjection
    {
        public int Reads {get;private set;}public bool EraseOnSecondRead {get;set;}public bool ContinueOnSecondRead {get;set;}
        public ReviewAuthorityScope Scope(){var p=Done(store.PrivacyAsync());var s=store.Session;return new(s.Profile,s.Branch,s.Load,p.Epoch,1,s.ReleaseId);}
        public ValueTask<ReviewProjection> ReadAsync(ReviewAuthorityScope expected,CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested();Reads++;if(Reads==2&&EraseOnSecondRead)Done(store.EraseAsync("learning"));if(Reads==2&&ContinueOnSecondRead)Done(store.ContinueAsync());var scope=Scope();return ValueTask.FromResult(new ReviewProjection(scope,Context() with{PrivacyEpoch=scope.PrivacyEpoch},[Asset("sqlite-projection-fixture")],[],[],true));
        }
    }
    public static void Register()
    {
        Add("CX-070","Unbound real review sources yield an honest empty advisory report with no reward or adventure penalty",()=>
        {
            var scope=new ReviewAuthorityScope(new(Guid.NewGuid()),new(Guid.NewGuid()),new(Guid.NewGuid()),0,1,Release);var report=Done(new ReadOnlyReviewService().RecommendAsync(scope));Require(report.Status=="review_sources_unbound"&&report.Recommendations.Length==0&&report.Missing.Length==1&&!report.WorldWriteCapability&&report.ExperienceReward==0&&!report.AdventureBlocked&&!report.AttributeDecay&&report.Band is null);
        });
        Add("CX-070","Known facts, current access, release, 6.5 target and training partition filter before any title is returned",()=>
        {
            var valid=Asset("safe");var unknown=valid with{Id="future",Title="hidden mentor location fixture",RequiredFacts=["future-fact"]};var denied=valid with{Id="private",RequiredAccess=["private-consent"]};var wrongRelease=valid with{Id="wrong-release",Release="future-release"};var wrongTarget=valid with{Id="seven",Target=7m};var holdout=valid with{Id="holdout",Partition=PracticePartition.TransferHoldout};var author=valid with{Id="author",Partition=PracticePartition.AuthorOnly};
            var selected=ReviewRules.Select([valid,unknown,denied,wrongRelease,wrongTarget,holdout,author],[],[],Context());Require(selected.Length==1&&selected[0].Id=="safe"&&!StrictJson.Serialize(selected).Contains("hidden mentor"));
        });
        Add("CX-070","Uncalibrated, unavailable, synthetic-authority and regression-only assets cannot masquerade as verified practice",()=>
        {
            var a=Asset("fixture");var selected=ReviewRules.Select([a with{CalibratedForDeclaredConditions=false},a with{Available=false},a with{Authority=PracticeIndexAuthority.ExplicitOfflineFixture},a with{Authority=PracticeIndexAuthority.UnverifiedClaim},a with{Partition=PracticePartition.RegressionOnly},a with{Modality=InputModality.Mixed}],[],[],Context());Require(selected.Length==0);
        });
        Add("CX-070","At most three recommendations preserve distinct family, goal and topic; an explicit request has first priority",()=>
        {
            var library=new[]{Asset("a","R01","fa","transport"),Asset("b","R02","fb","transport"),Asset("c","R03","fc","archive"),Asset("d","R04","fd","health"),Asset("e","R05","fa","housing")};var selected=ReviewRules.Select(library,[],[],Context(),100);Require(selected.Length==3&&selected.Select(r=>r.FamilyRoot).Distinct().Count()==3&&selected.Select(r=>r.Goal).Distinct().Count()==3&&selected.Select(r=>r.Topic).Distinct().Count()==3);
            selected=ReviewRules.Select(library,[],[],Context() with{ExplicitGoals=ImmutableHashSet.Create("R04")});Require(selected[0].Goal=="R04");Require(ReviewRules.Select(library,[],[],Context(),0).Length==0);
        });
        Add("CX-070","Refusal survives the next safe opportunity and uncertain clock; only a deliberate new request overrides it",()=>
        {
            var a=Asset("first");var declined=Context() with{Clock=new(Start,ReviewClockTrust.VerifiedContinuity),Declines=[new("R03",Start,Start.AddDays(1))]};Require(ReviewRules.Select([a],[],[],declined).Length==0&&ReviewRules.Select([a with{Id="renamed"}],[],[],declined with{Clock=new(Start.AddHours(1),ReviewClockTrust.VerifiedContinuity)}).Length==0);
            Require(ReviewRules.Select([a],[],[],declined with{Clock=new(Start.AddDays(100),ReviewClockTrust.Anomaly)}).Length==0);Require(ReviewRules.Select([a],[],[],declined with{ExplicitGoals=ImmutableHashSet.Create("R03")}).Length==1);
        });
        Add("CX-070","Protected input and unsafe windows suppress all suggestions, including explicit requests",()=>
        {
            var context=Context() with{ExplicitGoals=ImmutableHashSet.Create("R03")};Require(ReviewRules.Select([Asset("a")],[],[],context with{InputProtected=true}).Length==0&&ReviewRules.Select([Asset("a")],[],[],context with{SafeWindow=false}).Length==0);
        });
        Add("CX-070","Automatic practice excludes exposed families; deliberate repetition remains practice and never a new transfer source",()=>
        {
            var a=Asset("renamed","R03","already-exposed-root");var observation=O("earlier","already-exposed-root");Require(ReviewRules.Select([a],[observation],[],Context()).Length==0);var result=ReviewRules.Select([a],[observation],[],Context() with{ExplicitGoals=ImmutableHashSet.Create("R03")});Require(result.Length==1&&!result[0].NewFamily&&result[0].Reason=="explicit_repeat_practice");
        });
        Add("CX-070","Intervals advance only once per independently successful new family; repeats and unassessed outcomes cannot postpone indefinitely",()=>
        {
            var now=new ReviewClock(Start,ReviewClockTrust.VerifiedContinuity);var first=ReviewRules.Apply(Schedule(),O("one"),now);Require(first.Stage==1&&first.Due==Start.AddDays(3));Require(ReviewRules.Apply(first,O("one"),now)==first);
            var repeated=ReviewRules.Apply(first,O("renamed"),new(Start.AddDays(20),ReviewClockTrust.VerifiedContinuity));Require(repeated.Stage==1&&repeated.Due==first.Due);var unassessed=ReviewRules.Apply(first,O("pending","family2",null),now);Require(unassessed==first);
            var supported=ReviewRules.Apply(first,O("supported","family2") with{Support=[new("translation",Start,["R03"],true)]},now);Require(supported.Stage==1&&supported.Due==Start.AddDays(1));var wrong=ReviewRules.Apply(first,O("wrong","family3",false),now);Require(wrong.Stage==0&&wrong.Due==Start.AddDays(1)&&wrong.IndependentFamilies.Contains("family1"));
        });
        Add("CX-070","Clock anomalies leave outcome history intact and timing unverified, without offline-day decay",()=>
        {
            var state=Schedule(3) with{Due=Start.AddDays(14)};var uncertain=ReviewRules.Apply(state,O("later"),new(Start.AddDays(200),ReviewClockTrust.LocalWallClockOnly));Require(uncertain.Stage==3&&uncertain.Due==state.Due&&uncertain.TimingUnverified);var rollback=ReviewRules.Apply(state,O("later") with{SubmittedAt=Start.AddDays(1)},new(Start,ReviewClockTrust.VerifiedContinuity));Require(rollback.TimingUnverified&&rollback.Stage==3);
        });
        Add("CX-070","Supports bind submission time and target; text paths and structured confirmation remain distinct from independent language",()=>
        {
            var first=O("first");Require(ReviewRules.Condition(first with{Support=[new("translation",Start.AddSeconds(1),["R03"],true)]})=="independent_first_conditions");Require(ReviewRules.Condition(first with{Support=[new("translation",Start,["W01"],true)]})=="independent_first_conditions");Require(ReviewRules.Condition(first with{Support=[new("hint",Start,[],false)]})=="support_scope_unknown");
            var listening=first with{Modality=InputModality.Listening};Require(ReviewRules.Condition(listening with{AudioPlayed=false})=="text_path_no_audio_evidence"&&ReviewRules.Condition(listening with{Replays=1})=="independent_audio_review"&&ReviewRules.Condition(listening with{Support=[new("transcript",Start,["R03"],true)]})=="text_mediated_listening");Require(ReviewRules.Condition(first with{Modality=InputModality.StructuredChoice})=="structured_or_mixed_not_independent_language");
        });
        Add("CX-070","Canonical families defeat renaming and hashes; delayed transfer requires three families and verified real elapsed time",()=>
        {
            var one=O("one");var two=O("two","family2") with{Session="session2",Context="context2",SubmittedAt=Start.AddDays(1)};var three=O("three","family3") with{SubmittedAt=Start.AddDays(8)};var view=ReviewRules.Evidence([one,two,three],"R03",InputModality.Reading,0);Require(view.State==EvidenceState.DelayedNewContext&&view.SuccessFamilies==3&&view.Band is null);
            view=ReviewRules.Evidence([one,two,three with{ClockTrust=ReviewClockTrust.LocalWallClockOnly}],"R03",InputModality.Reading,0);Require(view.State==EvidenceState.MultiContext&&view.TimingUnverified);view=ReviewRules.Evidence([one,two with{FamilyRoot="family1"},three with{FamilyRoot="family1"}],"R03",InputModality.Reading,0);Require(view.State==EvidenceState.IndependentSuccess&&view.SuccessFamilies==1);Require(ReviewRules.Evidence([one],"R03",InputModality.Writing,0).State==EvidenceState.Insufficient);
        });
        Add("CX-070","Two distinct recent confirmed difficulties suggest review without erasing successes; one-family retries do not",()=>
        {
            var old=O("old-success");var bad1=O("bad1","family2",false) with{SubmittedAt=Start.AddDays(1)};var bad2=O("bad2","family3",false) with{SubmittedAt=Start.AddDays(2)};var view=ReviewRules.Evidence([old,bad1,bad2],"R03",InputModality.Reading,0);Require(view.State==EvidenceState.ReviewSuggested&&view.SuccessFamilies==1);Require(ReviewRules.Evidence([old,bad1,bad2 with{FamilyRoot="family2"}],"R03",InputModality.Reading,0).State==EvidenceState.IndependentSuccess);
        });
        Add("CX-070","Actual privacy erasure or load change while projecting suggestions discards output with no world transaction",()=>
        {
            foreach(bool erase in new[]{true,false})
            {
                using var store=new ProfileStore(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"review-fixture-"+Guid.NewGuid()),Release);var projection=new SqliteProjection(store){EraseOnSecondRead=erase,ContinueOnSecondRead=!erase};var scope=projection.Scope();long seq=Done(store.SnapshotAsync()).Sequence;var report=Done(new ReadOnlyReviewService(projection).RecommendAsync(scope));Require(report.Status=="stale_or_deleted_profile_scope"&&report.Recommendations.Length==0&&Done(store.SnapshotAsync()).Sequence==seq);
            }
        });
        Add("CX-071","Frozen source yields four complete task candidate routes and a short local-feedback route, without inventing audio or calibration",()=>
        {
            var inventory=OfflineAssessmentInventory.InspectSource(SourceJson());Require(inventory.Report.CompleteTaskRouteIds.Length==4&&inventory.Report.CompleteTaskRouteIds.Contains("LAB-R-001")&&inventory.Report.CompleteTaskRouteIds.Contains("LAB-L-001")&&inventory.Report.CompleteTaskRouteIds.Contains("LAB-W1-001")&&inventory.Report.CompleteTaskRouteIds.Contains("LAB-W2-001"));Require(inventory.Report.Materials.All(m=>m.Target==6.5m&&m.ActualAudioSeconds is null)&&!inventory.Report.DifficultyEmpiricallyCalibrated&&!inventory.Report.AudioAuditionComplete&&!inventory.Report.ActualParticipantsRegistered&&inventory.Report.PredictedBand is null);
            var shortTask=inventory.OpenLocalRoute("Q102-W-PLAN");Require(shortTask.Kind==AssessmentTaskKind.FunctionalShort&&!shortTask.FullBandEnabled&&!shortTask.TimerEnforced&&!shortTask.WorldWriteCapability);Require(inventory.OpenLocalRoute("LAB-R-001").Questions.Length==6&&inventory.OpenLocalRoute("LAB-L-001").Questions.Length==5&&!inventory.OpenLocalRoute("LAB-L-001").AudioEvidenceAvailable);
        });
        Add("CX-071","Task1 table and Task2 prompt preserve original data while author answers and world policy writes remain excluded",()=>
        {
            var inventory=OfflineAssessmentInventory.InspectSource(SourceJson());var visual=inventory.OpenLocalRoute("LAB-W1-001");var argument=inventory.OpenLocalRoute("LAB-W2-001");Require(visual.SuggestedMinimumWords==150&&argument.SuggestedMinimumWords==250&&visual.VisualDataJson!.Contains("Harbour")&&visual.VisualDataJson!.Contains("70")&&argument.Prompt.Contains("To what extent"));string routes=StrictJson.Serialize(inventory.Routes);Require(!routes.Contains("essential_features")&&!routes.Contains("illustrative_response")&&!routes.Contains("forbidden_inventions")&&!routes.Contains("evidence_paragraphs")&&!argument.ContentCalibrated&&argument.Target==6.5m);
        });
        Add("CX-071","Strict calibration protocol rejects duplicate fields, self-approved claims, overall section and changed target",()=>
        {
            var protocol=Protocol();string json=StrictJson.Serialize(protocol);Require(CalibrationRules.ParseProtocol(json)==protocol);var objectNode=JsonNode.Parse(json)!.AsObject();objectNode["approved"]=true;Throws<JsonException>(()=>CalibrationRules.ParseProtocol(objectNode.ToJsonString()));
            Throws<JsonException>(()=>CalibrationRules.ParseProtocol(json.Insert(1,"\"exam_reference\":\"IELTS Academic\",")));Throws<InvalidDataException>(()=>CalibrationRules.ParseProtocol(StrictJson.Serialize(protocol with{RelevantSection="overall"})));Throws<InvalidDataException>(()=>CalibrationRules.ParseProtocol(StrictJson.Serialize(protocol with{Binding=protocol.Binding with{Target=7m}})));
        });
        Add("CX-071","Default calibration inspector accepts no model, fixture or hash-integrity self-signature as a human decision",()=>
        {
            var protocol=Protocol();var actual=Done(new ReadOnlyCalibrationService().InspectAsync(protocol));Require(actual.Stage=="draft_target_65"&&actual.Missing.Length>0&&!actual.FullBandOutputEnabled&&!actual.LearningEffectProved&&!actual.OfficialCertification&&actual.PredictedBand is null);
            foreach(var authority in new[]{CalibrationAuthority.UnverifiedClaim,CalibrationAuthority.HashIntegrityOnly,CalibrationAuthority.ExplicitOfflineFixture}){var records=Enum.GetValues<CalibrationRole>().Select(r=>Record(r,protocol.Binding,authority));Require(CalibrationRules.Inspect(protocol,records).Stage=="draft_target_65");}
        });
        Add("CX-071","Material, question, support, audio, order, tool and evaluator changes invalidate only exact bound calibration records",()=>
        {
            var binding=Binding(AssessmentTaskKind.LongListening,InputModality.Listening);var records=Enum.GetValues<CalibrationRole>().Select(r=>Record(r,binding)).ToArray();Require(CalibrationRules.Inspect(Protocol(binding),records).Stage=="monitored");
            var changed=new[]{binding with{Version="new"},binding with{TextHash=H("new-text")},binding with{QuestionsHash=H("new-questions")},binding with{AudioHash=H("new-audio")},binding with{SupportHash=H("new-support")},binding with{ViewingOrderHash=H("new-order")},binding with{TimeAndToolsHash=H("new-tools")},binding with{EvaluatorVersion="new-evaluator"},binding with{RulesVersion="new-rules"}};foreach(var version in changed)Require(CalibrationRules.Inspect(Protocol(version),records).Stage=="draft_target_65");
        });
        Add("CX-071","Two editor roles require two actual authorities; a script hash never supplies final full audio audition",()=>
        {
            var writing=Binding();var samePerson=new[]{Record(CalibrationRole.LanguageEditor,writing,person:"same-person"),Record(CalibrationRole.QuestionEditor,writing,person:"same-person")};Require(CalibrationRules.Inspect(Protocol(writing),samePerson).Stage=="draft_target_65");
            var listening=Binding(AssessmentTaskKind.LongListening,InputModality.Listening);var exceptAudio=Enum.GetValues<CalibrationRole>().Where(r=>r!=CalibrationRole.AudioAudition).Select(r=>Record(r,listening));var result=CalibrationRules.Inspect(Protocol(listening),exceptAudio);Require(result.Stage=="draft_target_65"&&result.Missing.Contains("actual_final_audio_full_audition"));Throws<InvalidDataException>(()=>CalibrationRules.BindingHash(listening with{AudioHash="script_only"}));
        });
        Add("CX-071","Pilot statistics aggregate by participant and keep unanswered items visible; correlated retries do not inflate subjects",()=>
        {
            var binding=Binding();var rows=Enumerable.Range(0,100).Select(i=>Row("explicit-fixture-person","attempt-"+i,binding) with{SubmittedAt=Start.AddSeconds(i)});var result=CalibrationRules.Describe(rows,binding);Require(result.UniqueParticipants==1&&result.Rows==100&&result.PrimaryRows==1&&result.RepeatedRows==99&&result.Strata.Single().Participants==1&&result.Strata.Single().Unanswered==2&&result.Strata.Single().MeanParticipantCorrectFraction==.6m&&result.DescriptiveOnly&&!result.DifficultyCalibrated&&!result.LearningEffectProved&&result.Band is null);
        });
        Add("CX-071","Relevant section verification, consent, exposure and declared support prevent misleading pooled pilot results",()=>
        {
            var b=Binding();PilotRow[] rows=[Row("p1","a",b),Row("p2","b",b,4,6,0) with{SupportCondition="L4 fixture"},Row("p3","c",b) with{ReferenceTrust=SectionReferenceTrust.OverallOnly},Row("p4","d",b) with{Consent=false},Row("p5","e",b) with{Abandoned=true},Row("p6","f",b) with{PriorFamilyExposure=true},Row("p7","g",b) with{BindingHash=H("old"),Section=InputModality.Listening}];var report=CalibrationRules.Describe(rows,b);Require(report.PrimaryRows==2&&report.Strata.Length==2&&report.Strata.Single(s=>s.SupportCondition=="L4 fixture").InitialDifficultyWarning&&report.SelfReportedOrOverallOnly==1&&report.Unconsented==1&&report.AbandonedRows==1&&report.ScopeOrVersionMismatch==1&&report.IncompleteOrExposed==2&&!report.DifficultyCalibrated);
            Throws<InvalidDataException>(()=>CalibrationRules.Describe([Row("p","a",b) with{Correct=-1}],b));Require(CalibrationRules.Describe([],b).Strata.Length==0);
        });
        Add("CX-071","Complete writing needs two distinct trained blind human raters; short text and simulated raters never produce a full band",()=>
        {
            var b=Binding();string response=H("explicit fixture response");var first=Rating("rater1",b,response);var second=Rating("rater2",b,response);var result=WritingRatingRules.Inspect(b,response,[first,second],.5m);Require(result.PairValid&&!result.ThirdReviewerRequired&&result.Band is null&&!result.OfficialCertification);
            Require(!WritingRatingRules.Inspect(b,response,[first,second with{ReviewerAuthority="rater1"}],.5m).PairValid&&!WritingRatingRules.Inspect(b,response,[first,second with{Blind=false}],.5m).PairValid&&!WritingRatingRules.Inspect(b,response,[first,second with{Authority=CalibrationAuthority.ExplicitOfflineFixture}],.5m).PairValid);
            Require(!WritingRatingRules.Inspect(Binding(AssessmentTaskKind.FunctionalShort),response,[first,second],.5m).PairValid);
        });
        Add("CX-071","Predeclared disagreement tolerance requires third review, and revised response or evaluator invalidates the old rater pair",()=>
        {
            var b=Binding();string response=H("fixture body");var ratings=new[]{Rating("r1",b,response,6),Rating("r2",b,response,7)};var result=WritingRatingRules.Inspect(b,response,ratings,.5m);Require(result.PairValid&&result.ThirdReviewerRequired&&result.Missing.Contains("third_independent_adjudication_required")&&result.Band is null);
            Require(!WritingRatingRules.Inspect(b,H("revised body"),ratings,.5m).PairValid&&!WritingRatingRules.Inspect(b with{EvaluatorVersion="changed"},response,ratings,.5m).PairValid);Throws<InvalidDataException>(()=>WritingRatingRules.Inspect(b,response,ratings,0));
        });
        Add("CX-071","Observation revision replaces one interpretation, never another attempt; export contains hashes and excludes erased epochs",()=>
        {
            var old=O("stable-attempt") with{Correct=null,Authority=ReviewResultAuthority.UnverifiedModelCandidate};var assessed=old with{Correct=true,Authority=ReviewResultAuthority.VerifiedLocalObjectiveRule,EvaluationRevision=1};var normalized=ReviewRules.Normalize([old,assessed],0);Require(normalized.Length==1&&normalized[0].Correct==true);Throws<InvalidDataException>(()=>ReviewRules.Normalize([old,assessed with{ResponseHash=H("new body")}],0));
            var export=ReadOnlyObservationExport.Create([old,assessed],0);Require(export.Rows.Length==1&&!export.ContainsResponseBodies&&!export.ContainsAuthorAnswers&&export.Band is null&&!export.OfficialCertification&&ReadOnlyObservationExport.Create([assessed],1).Rows.Length==0);Throws<InvalidDataException>(()=>ReadOnlyObservationExport.Create([assessed with{ResponseHash="arbitrary private prose"}],0));
        });
        Add("CX-071","Unverified natural language candidates remain unassessed and cannot alter review success or failure schedules",()=>
        {
            var candidate=O("model-candidate") with{Authority=ReviewResultAuthority.UnverifiedModelCandidate,Correct=true};var state=Schedule();Require(ReviewRules.Condition(candidate)=="unassessed"&&ReviewRules.Evidence([candidate],"R03",InputModality.Reading,0).State==EvidenceState.Insufficient&&ReviewRules.Apply(state,candidate,new(Start,ReviewClockTrust.VerifiedContinuity))==state);
        });
    }
}

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Mist.Core.Contracts;
using Mist.Core.Learning;

namespace Mist.Core.ReviewCalibration;

public enum AssessmentTaskKind { LongReading,LongListening,AcademicTask1,AcademicTask2,FunctionalShort }
public sealed record CalibrationBinding(string Material,string Version,InputModality Modality,AssessmentTaskKind TaskKind,string TextHash,string QuestionsHash,string AudioHash,string SupportHash,string ViewingOrderHash,string TimeAndToolsHash,string SkillsVersion,string EvaluatorVersion,string RulesVersion,decimal Target=6.5m);
public enum CalibrationAuthority { UnverifiedClaim,HashIntegrityOnly,ExplicitOfflineFixture,VerifiedHumanAuthority }
public enum CalibrationRole { LanguageEditor,QuestionEditor,AudioAudition,TargetPilotComparison,CalibrationDecision,Monitoring }
public sealed record CalibrationRecord(string Id,CalibrationRole Role,string PersonAuthority,string BindingHash,string EvidenceHash,string State,CalibrationAuthority Authority);
public sealed record CalibrationProtocol(CalibrationBinding Binding,string ExamReference,string ExamReferenceVersion,string RelevantSection,string ParticipantStratification,string ExclusionRules,string ConditionsNote);
public sealed record CalibrationInspection(string BindingHash,string Stage,ImmutableArray<string> Missing,bool CompleteTaskRegistered)
{public bool OfficialCertification=>false;public bool LearningEffectProved=>false;public bool FullBandOutputEnabled=>false;public decimal? PredictedBand=>null;}
public enum SectionReferenceTrust { SelfReported,OverallOnly,VerifiedRelevantSection }
public sealed record PilotRow(string Participant,string Attempt,string BindingHash,InputModality Section,decimal ReferenceBand,SectionReferenceTrust ReferenceTrust,bool Consent,DateTimeOffset SubmittedAt,int Correct,int Incorrect,int Unanswered,bool Abandoned,bool ConditionsComplete,bool PriorFamilyExposure,string SupportCondition);
public sealed record PilotStratum(decimal ReferenceBand,string SupportCondition,int Participants,int Items,int Correct,int Incorrect,int Unanswered,decimal? MeanParticipantCorrectFraction,bool InitialDifficultyWarning);
public sealed record PilotDescription(int UniqueParticipants,int Rows,int PrimaryRows,int RepeatedRows,int SelfReportedOrOverallOnly,int Unconsented,int IncompleteOrExposed,ImmutableArray<PilotStratum> Strata,int AbandonedRows,int ScopeOrVersionMismatch)
{public bool DescriptiveOnly=>true;public bool DifficultyCalibrated=>false;public bool LearningEffectProved=>false;public decimal? Band=>null;}
public static class CalibrationRules
{
    public static bool HashValid(string? value)=>value is not null&&Regex.IsMatch(value,"^[0-9a-f]{64}$",RegexOptions.CultureInvariant);
    public static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    public static string BindingHash(CalibrationBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if(binding.Target!=6.5m||binding.Modality is not(InputModality.Reading or InputModality.Listening or InputModality.Writing)||!Enum.IsDefined(binding.TaskKind)||new[]{binding.Material,binding.Version,binding.SkillsVersion,binding.EvaluatorVersion,binding.RulesVersion}.Any(string.IsNullOrWhiteSpace)||new[]{binding.TextHash,binding.QuestionsHash,binding.SupportHash,binding.ViewingOrderHash,binding.TimeAndToolsHash}.Any(h=>!HashValid(h))||binding.Modality==InputModality.Listening&&!HashValid(binding.AudioHash)||binding.Modality!=InputModality.Listening&&binding.AudioHash!="not_applicable")throw new InvalidDataException("incomplete_or_invalid_calibration_binding");
        if(binding.TaskKind==AssessmentTaskKind.LongReading&&binding.Modality!=InputModality.Reading||binding.TaskKind==AssessmentTaskKind.LongListening&&binding.Modality!=InputModality.Listening||binding.TaskKind is AssessmentTaskKind.AcademicTask1 or AssessmentTaskKind.AcademicTask2 or AssessmentTaskKind.FunctionalShort&&binding.Modality!=InputModality.Writing)throw new InvalidDataException("task_kind_and_modality_mismatch");return Hash(StrictJson.Serialize(binding));
    }
    public static CalibrationProtocol ParseProtocol(string json)
    {
        var protocol=StrictJson.Deserialize<CalibrationProtocol>(json);BindingHash(protocol.Binding);
        if(protocol.ExamReference!="IELTS Academic"||new[]{protocol.ExamReferenceVersion,protocol.ParticipantStratification,protocol.ExclusionRules,protocol.ConditionsNote}.Any(string.IsNullOrWhiteSpace)||protocol.RelevantSection!=protocol.Binding.Modality.ToString().ToLowerInvariant())throw new InvalidDataException("incomplete_section_specific_protocol");return protocol;
    }
    public static CalibrationInspection Inspect(CalibrationProtocol protocol,IEnumerable<CalibrationRecord> records)
    {
        string hash=BindingHash(protocol.Binding);var valid=records.Where(r=>r.BindingHash==hash&&r.State=="accepted"&&r.Authority==CalibrationAuthority.VerifiedHumanAuthority&&HashValid(r.EvidenceHash)&&!string.IsNullOrWhiteSpace(r.PersonAuthority)).ToArray();var missing=ImmutableArray.CreateBuilder<string>();
        bool Has(CalibrationRole role)=>valid.Where(r=>r.Role==role).Select(r=>r.PersonAuthority).Distinct(StringComparer.Ordinal).Count()==1;
        bool editors=Has(CalibrationRole.LanguageEditor)&&Has(CalibrationRole.QuestionEditor)&&valid.Where(r=>r.Role is CalibrationRole.LanguageEditor or CalibrationRole.QuestionEditor).Select(r=>r.PersonAuthority).Distinct(StringComparer.Ordinal).Count()>=2;
        if(!editors)missing.Add("two_independent_actual_editor_reviews");bool audio=protocol.Binding.Modality!=InputModality.Listening||Has(CalibrationRole.AudioAudition);if(!audio)missing.Add("actual_final_audio_full_audition");if(!Has(CalibrationRole.TargetPilotComparison))missing.Add("actual_section_stratified_pilot_and_comparison");if(!Has(CalibrationRole.CalibrationDecision))missing.Add("actual_declared_conditions_calibration_decision");if(!Has(CalibrationRole.Monitoring))missing.Add("versioned_monitoring_plan_or_evidence");
        bool complete=protocol.Binding.TaskKind!=AssessmentTaskKind.FunctionalShort;string stage=!editors||!audio?"draft_target_65":!Has(CalibrationRole.TargetPilotComparison)?"editorial_reviewed":!Has(CalibrationRole.CalibrationDecision)?"pilot_tested":!Has(CalibrationRole.Monitoring)?"calibrated_for_declared_conditions":"monitored";
        if(!complete)missing.Add("short_task_has_local_feedback_scope_only");return new(hash,stage,missing.ToImmutable(),complete);
    }
    public static PilotDescription Describe(IEnumerable<PilotRow> source,CalibrationBinding binding)
    {
        string hash=BindingHash(binding);var rows=source.ToArray();if(rows.Any(r=>string.IsNullOrWhiteSpace(r.Participant)||string.IsNullOrWhiteSpace(r.Attempt)||r.Correct<0||r.Incorrect<0||r.Unanswered<0||string.IsNullOrWhiteSpace(r.SupportCondition)))throw new InvalidDataException("invalid_pilot_row");
        var primary=rows.Where(r=>r.Consent&&r.ReferenceTrust==SectionReferenceTrust.VerifiedRelevantSection&&r.BindingHash==hash&&r.Section==binding.Modality&&r.ConditionsComplete&&!r.Abandoned&&!r.PriorFamilyExposure&&r.Correct+r.Incorrect+r.Unanswered>0&&r.ReferenceBand is 6m or 6.5m or 7m).OrderBy(r=>r.SubmittedAt).ThenBy(r=>r.Attempt,StringComparer.Ordinal).GroupBy(r=>r.Participant,StringComparer.Ordinal).Select(g=>g.First()).ToArray();
        var strata=primary.GroupBy(r=>(r.ReferenceBand,r.SupportCondition)).OrderBy(g=>g.Key.ReferenceBand).ThenBy(g=>g.Key.SupportCondition,StringComparer.Ordinal).Select(g=>{decimal mean=g.Average(r=>r.Correct/(decimal)(r.Correct+r.Incorrect+r.Unanswered));return new PilotStratum(g.Key.ReferenceBand,g.Key.SupportCondition,g.Count(),g.Sum(r=>r.Correct+r.Incorrect+r.Unanswered),g.Sum(r=>r.Correct),g.Sum(r=>r.Incorrect),g.Sum(r=>r.Unanswered),mean,g.Key.ReferenceBand==6.5m&&(mean<.6m||mean>.8m));}).ToImmutableArray();
        int eligible=rows.Count(r=>r.Consent&&r.ReferenceTrust==SectionReferenceTrust.VerifiedRelevantSection&&r.BindingHash==hash&&r.Section==binding.Modality&&r.ConditionsComplete&&!r.Abandoned&&!r.PriorFamilyExposure&&r.Correct+r.Incorrect+r.Unanswered>0&&r.ReferenceBand is 6m or 6.5m or 7m);
        return new(rows.Select(r=>r.Participant).Distinct(StringComparer.Ordinal).Count(),rows.Length,primary.Length,Math.Max(0,eligible-primary.Length),rows.Count(r=>r.ReferenceTrust!=SectionReferenceTrust.VerifiedRelevantSection),rows.Count(r=>!r.Consent),rows.Count(r=>!r.ConditionsComplete||r.Abandoned||r.PriorFamilyExposure),strata,rows.Count(r=>r.Abandoned),rows.Count(r=>r.BindingHash!=hash||r.Section!=binding.Modality));
    }
}

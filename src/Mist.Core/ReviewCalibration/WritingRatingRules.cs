using System.Collections.Immutable;

namespace Mist.Core.ReviewCalibration;

public sealed record BlindWritingRating(string ReviewerAuthority,string BindingHash,string ResponseHash,bool Blind,bool TrainingVerified,CalibrationAuthority Authority,ImmutableDictionary<string,decimal> Criteria);
public sealed record WritingRatingInspection(bool PairValid,bool ThirdReviewerRequired,ImmutableArray<string> Missing)
{public decimal? Band=>null;public bool OfficialCertification=>false;}
public static class WritingRatingRules
{
    // Scores are externally supplied study observations, not generated grades. This checks the
    // declared pair and adjudication protocol; it does not combine four criteria into a band.
    public static WritingRatingInspection Inspect(CalibrationBinding binding,string responseHash,IEnumerable<BlindWritingRating> input,decimal declaredDisagreementTolerance)
    {
        string hash=CalibrationRules.BindingHash(binding);if(!CalibrationRules.HashValid(responseHash)||declaredDisagreementTolerance is <=0 or >9)throw new InvalidDataException("invalid_writing_rating_protocol");
        if(binding.TaskKind is not(AssessmentTaskKind.AcademicTask1 or AssessmentTaskKind.AcademicTask2))return new(false,false,["full_task1_or_task2_required"]);
        string task=binding.TaskKind==AssessmentTaskKind.AcademicTask1?"task_achievement":"task_response";string[] criteria=[task,"coherence_cohesion","lexical_resource","grammatical_range_accuracy"];
        var rows=input.Where(r=>r.BindingHash==hash&&r.ResponseHash==responseHash&&r.Authority==CalibrationAuthority.VerifiedHumanAuthority&&r.Blind&&r.TrainingVerified&&!string.IsNullOrWhiteSpace(r.ReviewerAuthority)&&r.Criteria.Count==4&&criteria.All(c=>r.Criteria.TryGetValue(c,out decimal score)&&score is >=0 and <=9)).ToArray();
        if(rows.Length!=2||rows.Select(r=>r.ReviewerAuthority).Distinct(StringComparer.Ordinal).Count()!=2)return new(false,false,["two_distinct_trained_blind_verified_human_raters"]);
        bool difference=criteria.Any(c=>Math.Abs(rows[0].Criteria[c]-rows[1].Criteria[c])>declaredDisagreementTolerance);return new(true,difference,difference?["third_independent_adjudication_required"]:[]);
    }
}

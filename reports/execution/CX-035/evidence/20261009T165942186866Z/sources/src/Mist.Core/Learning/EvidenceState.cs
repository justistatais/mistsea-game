namespace Mist.Core.Learning;

public enum EvidenceState { Insufficient, SupportedSuccess, IndependentSuccess, MultiContext, DelayedNewContext, ReviewSuggested }
public sealed record ReviewedEvidence(string Skill,string Modality,string Family,string Session,string Context,DateTimeOffset SubmittedAt,bool? Success,bool ConditionsComplete,bool Supported,string AssessmentSource);
public static class EvidenceStateRules
{
    public static string[] Labels=>["证据不足","借助支持完成","本次独立完成","多情境独立表现","延迟新情境验证","建议复核"];
    public static EvidenceState Project(IEnumerable<ReviewedEvidence> input,string skill,string modality)
    {
        var valid=input.Where(e=>e.Skill==skill&&e.Modality==modality&&e.ConditionsComplete&&e.Success is not null&&!string.IsNullOrWhiteSpace(e.AssessmentSource)).OrderBy(e=>e.SubmittedAt).ToArray();
        if(valid.TakeLast(2).Count(e=>e.Success==false)==2)return EvidenceState.ReviewSuggested;
        var successes=valid.Where(e=>e.Success==true).ToArray();var independent=successes.Where(e=>!e.Supported).ToArray();
        if(independent.Length==0)return successes.Length>0?EvidenceState.SupportedSuccess:EvidenceState.Insufficient;
        bool varied=independent.Select(e=>e.Family).Distinct().Count()>=3&&independent.Select(e=>e.Session).Distinct().Count()>=2&&independent.Select(e=>e.Context).Distinct().Count()>=2;
        if(varied&&independent.Skip(1).Any(e=>e.SubmittedAt-independent[0].SubmittedAt>=TimeSpan.FromDays(7)&&e.Family!=independent[0].Family))return EvidenceState.DelayedNewContext;
        return varied?EvidenceState.MultiContext:EvidenceState.IndependentSuccess;
    }
}

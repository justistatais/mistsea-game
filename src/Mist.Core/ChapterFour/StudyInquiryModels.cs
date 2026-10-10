using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Mist.Core.Contracts;

namespace Mist.Core.ChapterFour;

public sealed record StudyInquiryMaterial(string Id,string Title,string Version,string Spot,string SourceRoot,string English,string Chinese,string AccessClass,string ReviewStatus,string HumanReview)
{
    [JsonIgnore] public string Fingerprint=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(English))).ToLowerInvariant();
}
public sealed record StudyInquiryPackage(string Version,bool PlayerProjection,string ReviewStatus,string HumanReview,
    string OriginalMaterial,string DraftTask,string Desk,string PermitVersion,ImmutableArray<StudyInquiryMaterial> Materials)
{
    public static StudyInquiryPackage ParseJson(string json){var p=StrictJson.Deserialize<StudyInquiryPackage>(json);Validate(p);return p;}
    public static void Validate(StudyInquiryPackage p)
    {
        if(!p.PlayerProjection||p.Version!="r4-study-inquiry-candidate-1"||p.ReviewStatus!="original_unreviewed"||p.HumanReview!="human_pending"||
            p.OriginalMaterial!="R4-O01-R-NOTICES"||p.DraftTask!="R4-R4OPTIONALSTUDY-W"||p.Desk!="ROLE-R4-COURSE-DESK"||p.PermitVersion!="SC-PUBLIC-01"||
            !p.Materials.Select(m=>m.Id).Order().SequenceEqual(new[]{"R4-O01-STUDY-R-PROCEDURE","R4-O01-STUDY-R-REPLY-SCOPE"}.Order()))throw new InvalidDataException("A bounded public study inquiry candidate is required; no reward budget is defined.");
        foreach(var m in p.Materials)if(m.Version!=p.Version||m.Spot!="R4-O01.inquiry"||m.AccessClass!="public"||m.ReviewStatus!=p.ReviewStatus||m.HumanReview!=p.HumanReview||string.IsNullOrWhiteSpace(m.English)||string.IsNullOrWhiteSpace(m.Chinese)||string.IsNullOrWhiteSpace(m.SourceRoot))throw new InvalidDataException("Study inquiry materials need current public sources, safe stands and pending human status.");
    }
}
public enum StudyInquiryStage { DeskStock, CarriedBlank, Sealed, Received, Acknowledged }
public sealed record StudyInquiryAccess(string Id,string Version,string Fingerprint,string SourceRoot);
public sealed record StudyInquiryPermit(Guid Instance,string Version,string Grantor,string Recipient,string Purpose,
    ImmutableArray<string> Fields,ImmutableDictionary<string,string> SourceVersions);
public sealed record StudyNoticeComparison(string OriginalRoom,string CorrectedRoom,string Time,string CorrectionScope,bool CourseCancelled,bool TimeConflictResolved);
public sealed record StudyInquiryMeaning(string OriginalRoom,string CorrectedRoom,string Time,string CorrectionScope,string Request,string HistoricalRecords,string SafetyScope);
public sealed record StudyInquiryProposal(string? DraftHash,bool StructuredAssistance,StudyInquiryMeaning Meaning);
public sealed record StudyInquiryState(Guid Instance,string ContentVersion,StudyInquiryStage Stage,bool Compared=false,long Revision=0);
public sealed record StudySealedInquiry(Guid Instance,string Paper,long Revision,string? DraftHash,string? UnassessedEnglish,
    bool StructuredAssistance,StudyInquiryMeaning Meaning,ImmutableDictionary<string,string> SourceVersions,bool EnglishAssessed=false);
public sealed record StudyDeskReply(Guid Instance,string Paper,string Request,string English,string Chinese,string ActualConsequence,
    ImmutableDictionary<string,string> SourceVersions,bool TimetableChanged=false,bool AttendanceCertified=false,bool AlternateSessionApproved=false);
public sealed record StudyDeskEntry(Guid Instance,string Paper,long Revision,string Recipient,string Request,string? DraftHash,
    ImmutableDictionary<string,string> SourceVersions,string NoticePolicy,string ScheduleStatus,string ResponseStatus);
public sealed record StudyInquiryReceipt(Guid Instance,string InquiryPaper,string ReplyPaper,string Recipient,string Request,string Consequence,
    string? DeliveredDraftHash,ImmutableDictionary<string,string> SourceVersions,bool EnglishAssessed=false,int ExperienceAwarded=0,int MarksAwarded=0,int PartsAwarded=0);

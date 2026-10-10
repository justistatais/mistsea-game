using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Mist.Core.Contracts;

namespace Mist.Core.ChapterFour;

public sealed record ExhibitionCreditMaterial(string Id,string Title,string Version,string Scene,string Spot,string SourceRoot,
    string English,string Chinese,string AccessClass,string ReviewStatus,string HumanReview)
{
    [JsonIgnore] public string Fingerprint=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(English))).ToLowerInvariant();
}
public sealed record ExhibitionCreditPackage(string Version,bool PlayerProjection,string ReviewStatus,string HumanReview,
    ImmutableArray<ExhibitionCreditMaterial> Materials,string DraftTask,string Recipient,string PermitVersion,string CardDefinition)
{
    public static ExhibitionCreditPackage ParseJson(string json){var p=StrictJson.Deserialize<ExhibitionCreditPackage>(json);Validate(p);return p;}
    public static void Validate(ExhibitionCreditPackage p)
    {
        if(!p.PlayerProjection||p.Version!="r4-exhibition-credit-candidate-1"||p.ReviewStatus!="original_unreviewed"||p.HumanReview!="human_pending"||
            p.DraftTask!="R4-R4OPTIONALCREDIT-W"||p.Recipient!="ROLE-R4-EXHIBITION-ORGANISER"||p.PermitVersion!="CR-REVISION-01"||p.CardDefinition!="R4-LOCAL-EXHIBITION-CARD"||
            !p.Materials.Select(m=>m.Id).Order().SequenceEqual(new[]{"R4-O02-CREDIT-R-BOOK","R4-O02-CREDIT-R-LABEL","R4-O02-CREDIT-R-PERMIT"}.Order()))
            throw new InvalidDataException("A bounded three-source public exhibition candidate is required; no reward allocation is defined.");
        foreach(var m in p.Materials)if(m.Version!=p.Version||m.Scene!="R4-O02"||m.Spot is not("R4-O02.label" or "R4-O02.revision")||m.AccessClass!="public"||
            m.ReviewStatus!=p.ReviewStatus||m.HumanReview!=p.HumanReview||string.IsNullOrWhiteSpace(m.SourceRoot)||string.IsNullOrWhiteSpace(m.English)||string.IsNullOrWhiteSpace(m.Chinese))
            throw new InvalidDataException("Exhibition material must retain a real safe stand, bilingual prose and pending human status.");
        if(p.Materials.Single(m=>m.Id=="R4-O02-CREDIT-R-LABEL").Spot!="R4-O02.label")throw new InvalidDataException("The retained label must be inspected at its actual exhibit stand.");
    }
}
public enum ExhibitionCardStage { DeskStock, PlayerBlank, Printed, Mounted, Received }
public enum ExhibitionCreditOperation { Print, Receive }
public sealed record ExhibitionCreditAccess(string Id,string Version,string Fingerprint,string SourceRoot);
public sealed record ExhibitionRevisionPermit(Guid Instance,string Version,string Grantor,string Recipient,string Purpose,
    ImmutableArray<string> Fields,ImmutableDictionary<string,string> SourceVersions);
public sealed record ExhibitionContributionComparison(string Construction,string Calibration,int Calibrators,string Presentation,
    bool SpeakerOnlyContributor,bool ChangesTrialOutput);
public sealed record ExhibitionCreditMeaning(string Construction,int Calibrators,string Presentation,string UnknownClaims,
    string HistoricalRecords,string SafetyScope);
public sealed record ExhibitionCreditProposal(string? DraftHash,bool StructuredAssistance,ExhibitionCreditMeaning Meaning);
public sealed record ExhibitionCreditState(Guid Instance,string ContentVersion,ExhibitionCardStage Stage,bool Compared=false,
    long CardRevision=0,long? InspectionRevision=null);
public sealed record ExhibitionPrintedCard(Guid Instance,string CardId,long Revision,string? DraftHash,string PublicText,
    ImmutableDictionary<string,string> SourceVersions,bool ProposalEnglishAssessed=false);
public sealed record ExhibitionCreditReceipt(Guid Instance,string CardId,long CardRevision,string Recipient,string Scene,string Spot,
    string PublicText,string? DraftHash,ImmutableDictionary<string,string> SourceVersions,string OriginalLabelPolicy,
    string TrialSafetyScope,int ExperienceAwarded=0,int MarksAwarded=0,int PartsAwarded=0);

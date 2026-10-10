using System.Collections.Immutable;
using Mist.Core.Contracts;

namespace Mist.Core.R2Life;

public sealed record R2LifeBands(decimal Reading,decimal Listening,decimal Writing);
public sealed record LocalLifeRoles(string Writer,string Grower,string Custodian);
public sealed record LocalPublicParagraph(string Id,string En,string Zh);
public sealed record R2LifeContract(string ContentVersion,bool PlayerProjection,string ReviewStatus,string HumanReview,R2LifeBands TargetBands,
    string SourcePackage,ImmutableArray<string> SourceMaterials,string NewLocalDetails,LocalLifeRoles Roles,ImmutableArray<string> BedIds,ImmutableArray<LocalPublicParagraph> Paragraphs)
{
    public static R2LifeContract Parse(string json){var c=StrictJson.Deserialize<R2LifeContract>(json);Validate(c);return c;}
    public static void Validate(R2LifeContract c)
    {
        if(!c.PlayerProjection||c.ReviewStatus!="original_unreviewed"||c.HumanReview!="human_pending"||c.TargetBands!=new R2LifeBands(6.5m,6.5m,6.5m)||
            c.SourcePackage!="chapter_two/learner_package.json"||!c.SourceMaterials.SequenceEqual(new[]{"R2-O01-R-LETTER","R2-O02-R-SEEDS"})||
            c.Roles!=new LocalLifeRoles("ROLE-R2-LOCAL-WRITER","ROLE-R2-LOCAL-GROWER","ROLE-R2-LOCAL-POST-CUSTODIAN")||
            !c.BedIds.SequenceEqual(Enumerable.Range(1,6).Select(i=>"R2-TRIAL-BED-"+i))||c.Paragraphs.Length!=4||c.Paragraphs.Any(p=>string.IsNullOrWhiteSpace(p.En)||string.IsNullOrWhiteSpace(p.Zh)))
            throw new InvalidDataException("Two source-bound optional stories need exact six public beds, local roles and pending human review.");
    }
}
public enum TrialCrop { QuickLeaves,StorageRoots }
public enum ReplyDecision { SendShortAcknowledgement,KeepRevision }
public sealed record BedAssignment(string Bed,TrialCrop Crop);
public sealed record PlantingRecord(string Bed,TrialCrop Crop,string LayoutHash,long WorkClock,string GrowerReceipt);
public sealed record PlantingEstimate(string Arrangement,int EarlyServingBaskets,int LaterStorageBaskets,string Scope="conditional_local_trial_estimate_not_stock");
public sealed record PrivateReplyMeaning(string Invitation,string Visit,string Address,string Delivery);
public sealed record LocalMeaning(string Quest,string DraftHash,string PhysicalHash,bool StructuredAssistance,PrivateReplyMeaning? Reply=null);
public sealed record LetterScope(string Actor,Guid QuestInstance,ImmutableArray<string> Fields,string Recipient,string Purpose,string Version,bool Allowed,long Generation);
public sealed record WriterReview(string Actor,Guid QuestInstance,string DraftHash,long ConsentGeneration,ReplyDecision Decision,string Delivery,string ReviewReceiptKey);
public sealed record GrowerApproval(string Actor,Guid QuestInstance,string LayoutHash,string ReceiptKey,long WorkClock);
public sealed record R2LifeState
{
    public required string ContentVersion {get;init;}
    public bool LetterPaused {get;init;}
    public bool SeedsPaused {get;init;}
    public ImmutableArray<BedAssignment> Layout {get;init;}=[];
    public ImmutableArray<string> InspectedBeds {get;init;}=[];
    public GrowerApproval? Approval {get;init;}
    public ImmutableArray<PlantingRecord> Planted {get;init;}=[];
    public string RevisionRequest {get;init;}="";
}
public static class R2LifeRules
{
    public const string Reply="R2-O01",Seeds="R2-O02",LetterMaterial="R2-O01-R-LETTER",SeedMaterial="R2-O02-R-SEEDS";
    public static bool ValidReply(PrivateReplyMeaning m)=>m is{Invitation:"correspondence_only",Visit:"not_promised",Address:"not_public",Delivery:"custodian_existing_room_code"};
    public static bool ValidLayout(R2LifeContract c,ImmutableArray<BedAssignment> layout)=>layout.Length==6&&layout.Select(x=>x.Bed).Order().SequenceEqual(c.BedIds.Order())&&layout.All(x=>Enum.IsDefined(x.Crop))&&layout.Count(x=>x.Crop==TrialCrop.QuickLeaves)is 2 or 4;
    public static string LayoutHash(ImmutableArray<BedAssignment> layout)=>Mist.Core.Assets.AssetText.Hash(StrictJson.Serialize(layout.OrderBy(x=>x.Bed).ToImmutableArray()));
    public static PlantingEstimate Estimate(ImmutableArray<BedAssignment> layout)=>layout.Count(x=>x.Crop==TrialCrop.QuickLeaves)==4?new("Leaf",8,4):new("Root",4,8);
    public static bool PlantedAll(R2LifeContract c,R2LifeState s)=>ValidLayout(c,s.Layout)&&s.InspectedBeds.Order().SequenceEqual(c.BedIds.Order())&&s.Approval is{} approval&&approval.LayoutHash==LayoutHash(s.Layout)&&
        s.Planted.Length==6&&s.Layout.All(a=>s.Planted.Count(p=>p.Bed==a.Bed&&p.Crop==a.Crop&&p.LayoutHash==approval.LayoutHash&&p.GrowerReceipt==approval.ReceiptKey)==1);
}

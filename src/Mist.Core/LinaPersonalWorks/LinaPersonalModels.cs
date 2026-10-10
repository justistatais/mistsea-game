using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Mist.Core.Contracts;

namespace Mist.Core.LinaPersonalWorks;

public sealed record LinaMaterial(string Id,string Node,string Version,string English,string Chinese);
public sealed record LinaWorkPackage(string Version,string ReviewStatus,ImmutableArray<LinaMaterial> Materials)
{
    public static LinaWorkPackage Parse(string json)
    {
        var p=StrictJson.Deserialize<LinaWorkPackage>(json);
        if(p.Version!="LINA-WORK-20261010-v1"||p.ReviewStatus!="original_unreviewed_human_pending"||p.Materials.IsDefaultOrEmpty||p.Materials.Length!=8||p.Materials.Select(m=>m.Id).Distinct().Count()!=8||p.Materials.Any(m=>m.Version!=p.Version||m.Node is not("CQ-LINA-01" or "CQ-LINA-02" or "CQ-LINA-03")||string.IsNullOrWhiteSpace(m.English)||string.IsNullOrWhiteSpace(m.Chinese)))throw new InvalidDataException("莉娜个人工作包缺失、同版变化或范围未登记。");
        return p;
    }
    public static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static string BodyHash(LinaMaterial m)=>Hash(m.English+"\n"+m.Chinese);
}
public sealed record LinaLocalNode(string Node,Guid Instance,ImmutableArray<string> Acquired,bool TransferChecked=false,string? LetterHash=null,string? RelayPath=null,bool LetterDispatched=false,bool DeliveryAcknowledged=false,bool ReplyVerified=false,string? Outcome=null,string? WorkReceipt=null,string? ResultReceipt=null);
public sealed record LinaMaterialAccess(string Id,string Version,string BodyHash,long InvitationGeneration);
public sealed record LinaInvitationSource(string Node,bool Allowed,long Generation,string Version,string SourceHash);
public sealed record LinaReceivedReply(Guid NodeInstance,string MaterialId,string Version,string BodyHash,string ReceiptSemantic,string Sender,ImmutableArray<string> AllowedPurposes);
public sealed record LinaPublicTopicScope(string Actor,string SourceReceipt,string Version,bool Allowed,string Purpose,ImmutableArray<string> Recipients,long Generation);
public sealed record LinaLetterMeaning(string Recipient,string Purpose,string Address,string Visit,string DraftHash);
public sealed record LinaDeliveryAcknowledgement(Guid NodeInstance,string Serial,string Path,string DraftHash,string Recipient,string SourceVersion,string SourceBodyHash);
public sealed record LinaWorkPreview(Guid Token,bool Ready,ImmutableArray<string> Issues,string? Outcome);

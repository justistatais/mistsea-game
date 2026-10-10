using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mist.Core.Contracts;
namespace Mist.Core.OptionalLocalWorks;

public sealed record SettlementSupportSection(string Id,string Scene,string Spot,ImmutableArray<FieldRelayParagraph> Paragraphs);
public sealed record SettlementSupportContract(string Version,string Fingerprint,ImmutableArray<SettlementSupportSection> Sections)
{
    public static SettlementSupportContract ParseJson(string text)
    {
        using var d=JsonDocument.Parse(text);StrictJson.CheckDuplicates(d.RootElement);var r=d.RootElement;static string S(JsonElement j,string k)=>j.GetProperty(k).GetString()??throw new InvalidDataException("Missing settlement source.");
        if(!r.GetProperty("player_projection").GetBoolean()||S(r,"version")!="r6-settlement-support-candidate-2"||S(r,"review_status")!="original_unreviewed_human_pending")throw new InvalidDataException("Unbound local settlement source.");
        var sections=r.GetProperty("sections").EnumerateArray().Select(j=>new SettlementSupportSection(S(j,"id"),S(j,"scene"),S(j,"spot"),j.GetProperty("paragraphs").EnumerateArray().Select(p=>new FieldRelayParagraph(S(p,"id"),S(p,"en"),S(p,"zh"))).ToImmutableArray())).ToImmutableArray();
        if(sections.Length!=2||!sections.Select(s=>s.Id).SequenceEqual(new[]{"O-R6-01","O-R6-02"})||sections.Any(s=>s.Scene!=(s.Id=="O-R6-01"?"R6-O01":"R6-O02")||s.Spot!=s.Scene+".notice"||s.Paragraphs.Length<3||s.Paragraphs.Any(p=>string.IsNullOrWhiteSpace(p.English)||string.IsNullOrWhiteSpace(p.Chinese))))throw new InvalidDataException("Scene, public source or complete paragraphs not bound.");
        return new(S(r,"version"),Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant(),sections);
    }
}
public sealed record SettlementPacketState(Guid Instance,string Fingerprint,string Decision,bool Comparison=false,bool StockInspected=false,bool Dispatched=false,long DispatchSequence=0,long DispatchClock=0,bool Delivered=false);
public sealed record SettlementPacketDelivery(Guid Instance,string Fingerprint,string Recipient,ImmutableArray<string> Items,ImmutableArray<string> TraversalReceipts,string DispatchReceipt,string ComparisonReceipt,string DraftHash,string Scope,int Experience,int Marks,int Parts);
public sealed record SharedPressState(Guid Instance,string Fingerprint,string Decision,bool Permission=false,bool ReserveChecked=false,string Input="LIVE",bool JawLatched=false,long Configuration=0,long? ZeroCheck=null,bool Completed=false);
public sealed record SharedPressCheck(string Serial,string Channel,int Observed,long Configuration,bool InputOff,bool JawLatched,string Fallback);
public sealed record SharedPressResponsibility(Guid Instance,string Fingerprint,string Serial,long Configuration,string Window,string Operator,string Reviewer,string Fallback,string PermissionReceipt,string ReserveReceipt,string ZeroReceipt,string DraftHash,string Scope,int Experience,int Marks,int Parts);
public sealed record BorrowedLampState(Guid Instance,string Fingerprint,string Decision,bool Permission=false,bool Inspected=false,bool Isolated=false,bool CoverLatched=false,long Configuration=0,long? ZeroCheck=null,bool Serviced=false,bool Carried=false,long DispatchSequence=0,bool Returned=false,string Arrangement="",bool OwnerConfirmed=false,bool Completed=false);
public sealed record BorrowedLampResult(Guid Instance,string Fingerprint,string Serial,string OriginalOwner,string Borrower,string Arrangement,bool Serviced,bool Returned,ImmutableArray<string> TraversalReceipts,string DraftHash,string Scope,int Experience,int Marks,int Parts);
public sealed record SettlementNoteMeaning(string DraftHash,bool Assisted,ImmutableDictionary<string,string> Fields);

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mist.Core.Contracts;
namespace Mist.Core.OptionalLocalWorks;
public sealed record OldCityLifeContract(string Version,string Fingerprint,ImmutableArray<SettlementSupportSection> Sections)
{
    public static OldCityLifeContract ParseJson(string text)
    {using var d=JsonDocument.Parse(text);StrictJson.CheckDuplicates(d.RootElement);var r=d.RootElement;static string S(JsonElement x,string k)=>x.GetProperty(k).GetString()??throw new InvalidDataException("Missing local life source.");if(!r.GetProperty("player_projection").GetBoolean()||S(r,"version")!="r5-local-life-candidate-1"||S(r,"review_status")!="original_unreviewed_human_pending")throw new InvalidDataException("Unbound R5 local candidate.");var sections=r.GetProperty("sections").EnumerateArray().Select(s=>new SettlementSupportSection(S(s,"id"),S(s,"scene"),S(s,"spot"),s.GetProperty("paragraphs").EnumerateArray().Select(p=>new FieldRelayParagraph(S(p,"id"),S(p,"en"),S(p,"zh"))).ToImmutableArray())).ToImmutableArray();if(!sections.Select(s=>s.Id).SequenceEqual(new[]{"SC-R5-SIGN","SC-R5-LOST"})||sections[0].Spot!="R5-O01.proposal"||sections[1].Spot!="R5-O02.claim"||sections.Any(s=>s.Paragraphs.Length<3||s.Paragraphs.Any(p=>string.IsNullOrWhiteSpace(p.English)||string.IsNullOrWhiteSpace(p.Chinese))))throw new InvalidDataException("Actual local public source not bound.");return new(S(r,"version"),Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant(),sections);}
}
public sealed record LocalSignState(Guid Instance,string Fingerprint,string Decision,bool Consulted=false,string Main="",string Other="",string Board="",bool StockInspected=false,bool Installed=false,long Configuration=0,long? VisibleCheck=null,bool Completed=false);
public sealed record LocalSignDisplay(string Serial,string MainLine,string OtherLine,string Maintainer,long Configuration,string Scope);
public sealed record LocalSignResult(Guid Instance,string Fingerprint,string Board,string MainLine,string OtherLine,string Maintainer,string ConsultationReceipt,string VisibleCheckReceipt,string DraftHash,string Scope,int Experience,int Marks,int Parts);
public sealed record LocalPropertyState(Guid Instance,string Fingerprint,string Decision,bool StockInspected=false,bool Scope=false,long PermissionVersion=0,bool PotChecked=false,bool PotMatched=false,bool WrapperPermission=false,bool WrapperChecked=false,bool OwnerMatched=false,bool PotReturned=false,bool SketchReturned=false,bool Corrected=false,bool Completed=false,long WrapperVersion=0);
public sealed record LocalPropertyScope(Guid Instance,string Claimant,string Purpose,ImmutableArray<string> Fields,long Version,string Fingerprint);
public sealed record LocalMementoPermission(Guid Instance,string Owner,string Scope,long Version,string Fingerprint,bool ContentsReadable);
public sealed record LocalPropertyResult(Guid Instance,string Fingerprint,string Pot,string PotRecipient,string Sketch,string SketchRecipient,string OriginalTag,string Correction,string PotCheckReceipt,string TitleCheckReceipt,string DraftHash,string Privacy,string Scope,int Experience,int Marks,int Parts);

public sealed record LocalPotExterior(string Serial,string Handle,string Stamp);
public sealed record LocalMementoExterior(string Serial,string Seal,string Referral,string Owner,bool ContentsSupplied);

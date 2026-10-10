using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mist.Core.Contracts;
using Mist.Core.Exploration;

namespace Mist.Core.ScopedPersonalWorks;
public sealed record ScopedPersonalResponse(string Id,string Actor,string Purpose,string English,string Chinese)
{[JsonIgnore] public string Fingerprint=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(StrictJson.Serialize(this)))).ToLowerInvariant();}
public sealed record ScopedPersonalPackage(string Version,ImmutableArray<ScopedPersonalResponse> Responses)
{
    public static ScopedPersonalPackage ParseJson(string json)
    {using var d=JsonDocument.Parse(json);var r=d.RootElement;StrictJson.CheckDuplicates(r);if(!r.GetProperty("player_projection").GetBoolean()||r.GetProperty("scope").GetString()!="existing_mira_oren_production_detail_candidate"||r.GetProperty("review_status").GetString()!="original_unreviewed"||r.GetProperty("human_review").GetString()!="human_pending"||r.GetProperty("audio_asset").GetString()!="")throw new InvalidDataException("Actual personal work detail remains an unreviewed source candidate, without audio.");var p=new ScopedPersonalPackage(r.GetProperty("version").GetString()!,r.GetProperty("responses").EnumerateArray().Select(j=>new ScopedPersonalResponse(j.GetProperty("id").GetString()!,j.GetProperty("actor").GetString()!,j.GetProperty("purpose").GetString()!,j.GetProperty("en").GetString()!,j.GetProperty("zh").GetString()!)).ToImmutableArray());if(p.Responses.Length!=6||p.Responses.Any(x=>string.IsNullOrWhiteSpace(x.English)||string.IsNullOrWhiteSpace(x.Chinese))||p.Responses.Select(x=>x.Actor+":"+x.Purpose).Distinct().Count()!=6)throw new InvalidDataException("Full finite named responses required.");return p;}
}
public sealed record MiraReviewFields(int Observers,int Service,string SourceVersion,string SourceRoot,string Recipient,string Purpose,bool IdentitiesIncluded);
public sealed record PhysicalMiraReviewPaper(Guid NodeInstance,string Paper,string Status,MiraReviewFields Fields,string SourceMaterialVersion,string SourceMaterialHash);
public sealed record ActualMiraRenewalAnswer(Guid NodeInstance,string Actor,string SourceCopy,string Mode,string Version,ImmutableArray<string> Fields,ImmutableArray<string> Recipients,bool Allowed,long Generation,string ResponseHash,string InvitationReceipt);
public sealed record ActualMiraDrill(Guid NodeInstance,string Mode,ImmutableArray<string> Copies,string OriginalSourceRoot,string OriginalVersion,string MainCustody,string Stage,long Configuration);
public sealed record MiraRestrictedRead(Guid NodeInstance,string Recipient,ImmutableArray<string> Fields,bool Allowed,string Reason,string SourceRoot,string SourceVersion,long Configuration,ImmutableDictionary<string,long> ConsentGenerations);
public sealed record MiraFutureExit(Guid NodeInstance,bool FutureContactAllowed,bool ActualTestDenied,string OriginalReviewCopyRetained,string MainCustody);
public sealed record MiraCorrection(Guid NodeInstance,string EarlierInterpretation,string EarlierVersion,string CorrectedInterpretation,string CorrectedVersion,string OriginalCopy,string OriginalSourceRoot,bool OriginalRetained,bool IndependentSource);
public sealed record ActualOrenWork(Guid NodeInstance,string Mode,string Kit,long DeviceConfiguration,string IndependentReviewReceipt,string Stage,string TechnicalAnswer,string SafeWindow,string OriginalResponsibility);
public sealed record OrenNamedReceiving(Guid NodeInstance,string Receiver,string Supervisor,string IncomingWorker,string Kit,long DeviceConfiguration,string WorkReceipt,string SafeWindow,bool Accepted,string Version,ImmutableDictionary<string,string> ResponseHashes,string InvitationReceipt,long Generation);
public sealed record OrenActualWalk(string FromScene,Point2 From,string ToScene,Point2 To,string ExitPort,string EntryPort,ImmutableArray<Point2> Path);
public sealed record OrenActualPosition(Guid NodeInstance,string Actor,string Scene,Point2 Position,ImmutableArray<OrenActualWalk> Walks,string ShipInstance,string SourceRosterJson,ImmutableDictionary<string,string> GeometryHashes);
public sealed record OrenActualHandover(Guid NodeInstance,string Outgoing,string Incoming,string Receiver,string Supervisor,string Kit,long DeviceConfiguration,string SafeWindow,string OriginalResponsibility,bool ReleasedWorkCanResumeBattle);
public sealed record MiraPolicyProbe(Guid NodeInstance,string Kind,string Recipient,ImmutableArray<string> Fields,bool Allowed,string Reason,long Configuration,ImmutableDictionary<string,long> ConsentGenerations);
public sealed record OrenDeviceCheck(Guid NodeInstance,string Kit,long SourceConfiguration,bool InputIsolated,bool PassiveTestPassed,bool Restored,string TestIndicator);
public sealed record PersonalReadableResponse(string Id,string Version,string BodyHash,string English,string Chinese,string Node,string SourceReceipt,string PermissionReceipt,long Generation);


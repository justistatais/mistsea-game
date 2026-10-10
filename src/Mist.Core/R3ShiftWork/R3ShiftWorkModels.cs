using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mist.Core.Contracts;

namespace Mist.Core.R3ShiftWork;
public sealed record ShiftMaterial(string Id,string Title,string Spot,string English,string Chinese,string ResponseActor,string Version)
{[System.Text.Json.Serialization.JsonIgnore] public string Fingerprint=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(StrictJson.Serialize(this)))).ToLowerInvariant();}
public sealed record ShiftStaff(string Actor,string Name,string Spot,string Window,string Response);
public sealed record R3ShiftPackage(string Version,string Scene,string BoardSpot,string HandoverSpot,string ReceiptSpot,string Window,ImmutableArray<ShiftStaff> Staff,ImmutableArray<ShiftMaterial> Materials,string DraftTask,string PromptEnglish,string PromptChinese,ImmutableArray<string> Hints)
{
    public static R3ShiftPackage ParseJson(string json)
    {
        using var d=JsonDocument.Parse(json);var r=d.RootElement;StrictJson.CheckDuplicates(r);if(!r.GetProperty("player_projection").GetBoolean()||r.GetProperty("scope").GetString()!="original_shift_work_candidate"||r.GetProperty("review_status").GetString()!="original_unreviewed"||r.GetProperty("human_review").GetString()!="human_pending")throw new InvalidDataException("Shift work candidate scope must be explicit.");
        static string S(JsonElement j,string k)=>j.GetProperty(k).GetString()!;
        var p=new R3ShiftPackage(S(r,"version"),S(r,"scene"),S(r,"board_spot"),S(r,"handover_spot"),S(r,"receipt_spot"),S(r,"window"),r.GetProperty("staff").EnumerateArray().Select(j=>new ShiftStaff(S(j,"actor"),S(j,"name"),S(j,"spot"),S(j,"window"),S(j,"response"))).ToImmutableArray(),r.GetProperty("materials").EnumerateArray().Select(j=>new ShiftMaterial(S(j,"id"),S(j,"title"),S(j,"spot"),S(j,"english"),S(j,"chinese"),S(j,"response_actor"),S(j,"version"))).ToImmutableArray(),S(r,"draft_task"),S(r,"prompt_en"),S(r,"prompt_zh"),r.GetProperty("hints").EnumerateArray().Select(x=>x.GetString()!).ToImmutableArray());Validate(p);return p;
    }
    public static void Validate(R3ShiftPackage p)
    {
        if(p.Scene!="R3-S08"||p.Window!="R3-WATCH-S1"||p.Staff.Length!=3||!p.Staff.Select(x=>x.Actor).Order().SequenceEqual(new[]{"ROLE-R3-SHIFT-SELA","ROLE-R3-SHIFT-TOBIN","ROLE-R3-SHIFT-NIA"}.Order())||p.Materials.Length!=6||p.Hints.Length!=3||p.Materials.Any(m=>m.Version!=p.Version||string.IsNullOrWhiteSpace(m.English)||string.IsNullOrWhiteSpace(m.Chinese)||!m.Spot.StartsWith(p.Scene+".",StringComparison.Ordinal))||p.Staff.Any(a=>!p.Materials.Any(m=>m.Id==a.Response&&m.ResponseActor==a.Actor&&m.Spot==a.Spot)))throw new InvalidDataException("Current finite shift, three local actors, full original material and three hints are required.");
    }
}
public sealed record ShiftAccess(string Id,string Version,string Fingerprint);
public sealed record ShiftWorkState(Guid Instance,string Version,bool BoardTaken=false,long Configuration=0,bool Posted=false,bool MorningReceived=false,bool HandoverChecked=false,bool AfternoonReceived=false,bool Completed=false);
public sealed record ShiftPersonPosition(string Actor,string Scene,string Spot,string PublicRosterSource);
public sealed record ShiftAvailability(Guid Instance,string Actor,string Window,string WorkWindow,string Post,string Scene,string Spot,string Version,string SourceHash,bool Allowed,long Generation,ImmutableArray<string> Fields);
public sealed record ShiftPlan(string Morning,string Afternoon,string Path,string Window,string PrivateFields,string OldPromise,string UnfinishedChecks,string NextWatch);
public sealed record ShiftProposal(string? DraftHash,bool Assisted,ShiftPlan Plan,ImmutableDictionary<string,long> AvailabilityGenerations);
public sealed record ShiftPosted(Guid Instance,string Board,long Configuration,ShiftPlan Plan,string? DraftHash,ImmutableDictionary<string,string> SourceVersions);
public sealed record ShiftLocalPost(Guid Instance,string Actor,string Post,string Window,string WorkWindow,string SourceReceipt,bool Accepted);
public sealed record ShiftReception(Guid Instance,string Actor,string WorkWindow,string Board,long Configuration,bool UnfinishedChecksAcknowledged);
public sealed record ShiftResolution(Guid Instance,string Board,ShiftPlan Plan,ImmutableArray<string> ActualReceptionReceipts,ImmutableDictionary<string,string> SourceVersions,string OldPromiseRetained,string NextWatch,int Experience=0,int Marks=0,int Parts=0);

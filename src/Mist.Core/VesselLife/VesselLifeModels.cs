using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mist.Core.Exploration;

namespace Mist.Core.VesselLife;
public sealed record BanterLine(string Id,string Actor,string English,string Chinese);
public sealed record BanterScript(string Id,string Version,string Title,string Scene,string Spot,ImmutableArray<string> Actors,string Scope,string ProgressGate,ImmutableArray<BanterLine> Lines,string Consequence,string SourceOrigin)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string Fingerprint=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Mist.Core.Contracts.StrictJson.Serialize(this)))).ToLowerInvariant();
}
public sealed record VesselLifePackage(string Version,ImmutableArray<BanterScript> Scripts)
{
    public static VesselLifePackage ParseJson(string json)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;Mist.Core.Contracts.StrictJson.CheckDuplicates(root);
        if(!root.GetProperty("player_projection").GetBoolean()||root.GetProperty("scope").GetString()!="original_ship_life_candidate"||root.GetProperty("review_status").GetString()!="original_unreviewed"||root.GetProperty("human_review").GetString()!="human_pending")throw new InvalidDataException("Ship life candidate requires explicit learner scope and review limits.");
        static string S(JsonElement j,string k)=>j.GetProperty(k).GetString()??throw new InvalidDataException(k);
        var scripts=root.GetProperty("scripts").EnumerateArray().Select(j=>
        {
            if(j.GetProperty("reward").GetInt32()!=0||j.GetProperty("private_text_included").GetBoolean()||S(j,"audio_asset")!="")throw new InvalidDataException("Life conversations grant no world rewards or private letter contents and make no audio claim.");
            return new BanterScript(S(j,"id"),S(j,"content_version"),S(j,"title_zh"),S(j,"scene"),S(j,"spot"),j.GetProperty("actors").EnumerateArray().Select(x=>x.GetString()!).ToImmutableArray(),S(j,"scope"),S(j,"progress_gate"),j.GetProperty("paragraphs").EnumerateArray().Select(p=>new BanterLine(S(p,"id"),S(p,"actor"),S(p,"en"),S(p,"zh"))).ToImmutableArray(),S(j,"consequence"),S(j,"source_origin"));
        }).ToImmutableArray();var p=new VesselLifePackage(S(root,"content_version"),scripts);Validate(p);return p;
    }
    public static void Validate(VesselLifePackage p)
    {
        string[] singles=["LINA","OREN","MIRA","TESS"];var expected=singles.SelectMany(x=>Enumerable.Range(1,3).Select(n=>"BAN-"+x+"-"+n)).Concat(new[]{"LINA-OREN","LINA-MIRA","LINA-TESS","OREN-MIRA","OREN-TESS","MIRA-TESS"}.SelectMany(x=>Enumerable.Range(1,2).Select(n=>"BAN-"+x+"-"+n))).Order().ToArray();
        if(string.IsNullOrWhiteSpace(p.Version)||!p.Scripts.Select(x=>x.Id).Order().SequenceEqual(expected))throw new InvalidDataException("Exactly the original twenty-four ship life seeds must be represented.");
        foreach(var s in p.Scripts)
        {
            string scene=s.Actors.Length==1&&s.Id.StartsWith("BAN-OREN-",StringComparison.Ordinal)?"HUB-S03":s.Actors.Length==1&&s.Id.StartsWith("BAN-MIRA-",StringComparison.Ordinal)?"HUB-S02":"HUB-S01";
            string[] actors=s.Id.Split('-')[1..^1].Select(x=>"CP-"+x).ToArray();
            if(s.Scene!=scene||!s.Spot.StartsWith(scene+".",StringComparison.Ordinal)||!s.Actors.SequenceEqual(actors)||s.Lines.Length<6||s.Lines.Select(x=>x.Id).Distinct().Count()!=s.Lines.Length||s.Lines.Any(x=>!s.Actors.Add("HERO").Contains(x.Actor)||string.IsNullOrWhiteSpace(x.English)||string.IsNullOrWhiteSpace(x.Chinese))||s.Scope is not("public" or "lina_reply_folding" or "lina_reply_retention"))throw new InvalidDataException("Original scene, real actors and full dialogue are required.");
        }
    }
}
public sealed record BanterWalk(string FromScene,Point2 From,string ToScene,Point2 To,string ExitPort,string EntryPort,ImmutableArray<Point2> Path);
public sealed record BanterPresence(string Actor,Guid VesselInstance,string Scene,Point2 Position,string AcceptedInvitationReceipt,ImmutableArray<BanterWalk> ActualWalk);
public sealed record BanterInvitation(string Id,string Version,string Fingerprint,Guid VesselInstance,ImmutableArray<string> Actors,bool Allowed,long Generation,ImmutableArray<string> SourceReceipts);
public sealed record BanterState(string Id,string Version,string Fingerprint,Guid VesselInstance,string EventInstance,int NextLine,string BoardingReceipt,bool Completed,bool Paused);
public sealed record BanterCompletion(string Id,string Version,string Fingerprint,Guid VesselInstance,string BoardingReceipt,string Consequence,ImmutableArray<string> Participants,int WorldReward=0);
public sealed record BanterBoardingUse(string Id,string BoardingReceipt);

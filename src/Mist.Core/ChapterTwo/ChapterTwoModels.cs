using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mist.Core.Exploration;

namespace Mist.Core.ChapterTwo;

public sealed record RegionDanger(string Id,Bounds Area,string ReceiptCondition);
public sealed record ChapterScene(SceneGeometry Geometry,ImmutableArray<RegionDanger> Hazards);
public sealed record ChapterParagraph(string Id,string English,string Chinese);
public sealed record ChapterMaterial(string Id,string Version,string TranslationVersion,string Title,string Quest,string Kind,
    string Scene,string Spot,string SourceRoot,string AccessClass,string ReviewStatus,string HumanReview,
    string AudioAsset,ImmutableArray<ChapterParagraph> Paragraphs)
{
    [JsonIgnore] public string English=>string.Join("\n\n",Paragraphs.Select(p=>p.English));
    [JsonIgnore] public string Chinese=>string.Join("\n\n",Paragraphs.Select(p=>p.Chinese));
    [JsonIgnore] public string Fingerprint=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(English))).ToLowerInvariant();
}
public sealed record ChapterStation(string Id,string Spot,string Label,string Action);
public sealed record ChapterVisualRow(string Label,int RollEntries,int ResidenceConfirmations);
public sealed record ChapterVisualTable(string Title,ImmutableArray<string> Columns,ImmutableArray<ChapterVisualRow> Rows,string Availability);
public sealed record ChapterWritingTask(string Id,string Quest,string Kind,string Spot,string EnglishPrompt,string ChinesePrompt,ChapterVisualTable? Visual=null);
public sealed record ChapterTwoPackage(string Version,string Scope,string ReviewStatus,string HumanReview,
    ImmutableArray<ChapterScene> Scenes,ImmutableArray<ChapterMaterial> Materials,ImmutableArray<ChapterStation> Stations,
    ImmutableArray<ChapterWritingTask> WritingTasks,ImmutableDictionary<string,string> PortGates)
{
    // Only the learner package is parsed. Author cards and answer files are never model input.
    public static ChapterTwoPackage ParseJson(string json)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;Mist.Core.Contracts.StrictJson.CheckDuplicates(root);
        if(!root.GetProperty("player_projection").GetBoolean())throw new InvalidDataException("Author-only files cannot become a learner runtime package.");
        foreach(var band in root.GetProperty("target_bands").EnumerateObject())if(band.Value.GetDecimal()!=6.5m)throw new InvalidDataException("R2 learner targets remain reading/listening/writing 6.5.");
        static string S(JsonElement j,string k)=>j.GetProperty(k).GetString()??throw new InvalidDataException("Missing content text: "+k);
        static string O(JsonElement j,string k)=>j.TryGetProperty(k,out var v)?v.GetString()??"":"";
        static Point2 Point(JsonElement p)=>new(p.GetProperty("x").GetDouble(),p.GetProperty("y").GetDouble());
        static Bounds Rect(JsonElement b)=>new(b.GetProperty("left").GetDouble(),b.GetProperty("top").GetDouble(),b.GetProperty("right").GetDouble(),b.GetProperty("bottom").GetDouble());
        var gates=ImmutableDictionary.CreateBuilder<string,string>();var scenes=ImmutableArray.CreateBuilder<ChapterScene>();
        foreach(var j in root.GetProperty("scenes").EnumerateArray())
        {
            var ports=j.GetProperty("ports").EnumerateArray().Select(p=>{gates.Add(S(p,"id"),S(p,"gate"));return new Port(S(p,"id"),S(p,"target_scene"),S(p,"target_port"),Point(p.GetProperty("stand")));}).ToImmutableArray();
            var danger=j.GetProperty("danger").EnumerateArray().Select(d=>new RegionDanger(S(d,"id"),Rect(d.GetProperty("area")),S(d,"active_until_receipt"))).ToImmutableArray();
            var geo=new SceneGeometry(S(j,"id"),Rect(j.GetProperty("walk")),j.GetProperty("blocked").EnumerateArray().Select(Rect).ToImmutableArray(),danger.Select(d=>d.Area).ToImmutableArray(),ports,
                j.GetProperty("spots").EnumerateArray().Select(p=>new Spot(S(p,"id"),Point(p.GetProperty("stand")),S(p,"purpose_zh"))).ToImmutableArray());
            scenes.Add(new(geo,danger));
        }
        var materials=root.GetProperty("materials").EnumerateArray().Select(j=>new ChapterMaterial(S(j,"id"),S(j,"content_version"),O(j,"translation_version"),S(j,"title_zh"),S(j,"quest_ref"),S(j,"kind"),
            S(j.GetProperty("location"),"scene"),S(j.GetProperty("location"),"spot"),S(j,"source_root"),S(j,"access_class"),S(j,"review_status"),S(j,"human_review"),O(j,"audio_asset"),
            j.GetProperty("paragraphs").EnumerateArray().Select(p=>new ChapterParagraph(S(p,"id"),S(p,"en"),S(p,"zh"))).ToImmutableArray())).ToImmutableArray();
        var stations=root.GetProperty("stations").EnumerateArray().Select(j=>new ChapterStation(S(j,"id"),S(j,"spot"),S(j,"label_zh"),S(j,"action"))).ToImmutableArray();
        var writes=root.GetProperty("writing_tasks").EnumerateArray().Select(j=>
        {
            ChapterVisualTable? visual=null;if(j.TryGetProperty("visual",out var table))visual=new(S(table,"title_en"),table.GetProperty("columns_en").EnumerateArray().Select(x=>x.GetString()!).ToImmutableArray(),table.GetProperty("rows").EnumerateArray().Select(row=>new ChapterVisualRow(row[0].GetString()!,row[1].GetInt32(),row[2].GetInt32())).ToImmutableArray(),S(table,"availability"));
            return new ChapterWritingTask(S(j,"id"),S(j,"quest_ref"),S(j,"kind"),S(j,"workspace_spot"),S(j,"prompt_en"),S(j,"prompt_zh"),visual);
        }).ToImmutableArray();
        var package=new ChapterTwoPackage(S(root,"content_version"),S(root,"scope"),S(root,"review_status"),S(root,"human_review"),scenes.ToImmutable(),materials,stations,writes,gates.ToImmutable());Validate(package);return package;
    }
    public static void Validate(ChapterTwoPackage p)
    {
        string[] expected=["R2-S01","R2-S02","R2-S03","R2-S04","R2-S05","R2-S06","R2-S07","R2-S08","R2-O01","R2-O02"];
        if(p.Scope!="authoring_candidate_not_runtime"||p.ReviewStatus!="original_unreviewed"||p.HumanReview!="human_pending"||string.IsNullOrWhiteSpace(p.Version)||!p.Scenes.Select(s=>s.Geometry.Id).Order().SequenceEqual(expected.Order()))throw new InvalidDataException("R2 scope, ten registered scenes and review status must be explicit.");
        var scenes=p.Scenes.ToDictionary(s=>s.Geometry.Id);var ports=scenes.Values.Sum(s=>s.Geometry.Ports.Length);if(ports!=20)throw new InvalidDataException("R2 has exactly twenty reciprocal ports.");
        var expectedEdges=new HashSet<string>(["R2-S01|R2-S02","R2-S02|R2-S03","R2-S02|R2-S04","R2-S02|R2-S05","R2-S05|R2-S06","R2-S06|R2-S07","R2-S03|R2-S07","R2-S02|R2-S08","R2-O01|R2-S03","R2-O02|R2-S05"]);
        var seen=new HashSet<string>();
        foreach(var scene in scenes.Values)
        {
            var nav=new NavigationGrid(scene.Geometry);if(scene.Geometry.Ports.Select(x=>x.Id).Distinct().Count()!=scene.Geometry.Ports.Length||scene.Geometry.Spots.Select(x=>x.Id).Distinct().Count()!=scene.Geometry.Spots.Length)throw new InvalidDataException("Duplicate R2 stand.");
            foreach(var port in scene.Geometry.Ports)
            {
                if(!scenes.TryGetValue(port.TargetScene,out var target)||!nav.CanStand(port.Stand))throw new InvalidDataException("Invalid R2 port stand.");
                var pair=target.Geometry.Ports.SingleOrDefault(x=>x.Id==port.TargetPort);
                if(pair is null||pair.TargetScene!=scene.Geometry.Id||pair.TargetPort!=port.Id||!new NavigationGrid(target.Geometry).CanStand(pair.Stand)||!p.PortGates.ContainsKey(port.Id)||p.PortGates[port.Id]!=p.PortGates[pair.Id])throw new InvalidDataException("Unpaired R2 port.");
                seen.Add(string.Join("|",new[]{scene.Geometry.Id,port.TargetScene}.Order(StringComparer.Ordinal)));
            }
            foreach(var spot in scene.Geometry.Spots)if(!nav.CanStand(spot.Stand,true))throw new InvalidDataException("R2 actions need a safe external work stand.");
        }
        if(!seen.SetEquals(expectedEdges)||p.Materials.Select(m=>m.Id).Distinct().Count()!=p.Materials.Length||p.Stations.Select(x=>x.Action).Distinct().Count()!=p.Stations.Length)throw new InvalidDataException("R2 graph/material/action identities changed.");
        bool SpotExists(string scene,string spot)=>scenes.TryGetValue(scene,out var s)&&s.Geometry.Spots.Any(x=>x.Id==spot);
        foreach(var m in p.Materials)if(!SpotExists(m.Scene,m.Spot)||m.ReviewStatus!="original_unreviewed"||m.HumanReview!="human_pending"||m.Paragraphs.IsDefaultOrEmpty||m.Paragraphs.Any(x=>string.IsNullOrWhiteSpace(x.English)||string.IsNullOrWhiteSpace(x.Chinese))||m.Kind=="listening_script"&&!string.IsNullOrEmpty(m.AudioAsset))throw new InvalidDataException("Unreviewed R2 materials cannot claim recorded audio.");
        foreach(var st in p.Stations)if(!scenes.Values.Any(s=>s.Geometry.Spots.Any(x=>x.Id==st.Spot)))throw new InvalidDataException("Missing R2 action stand.");
        foreach(var wt in p.WritingTasks)if(!scenes.Values.Any(s=>s.Geometry.Spots.Any(x=>x.Id==wt.Spot)))throw new InvalidDataException("Missing writing stand.");
    }
}

public enum LocalLoad { Unselected, Kitchen, Wash, Alternating, Concurrent, TowerOutlet }
public sealed record ChapterTwoState
{
    public required string ContentVersion {get;init;}
    public bool Landing {get;init;}
    public bool Reception {get;init;}
    public bool VisitA {get;init;}
    public bool VisitB {get;init;}
    public bool VisitC {get;init;}
    public bool BasicArchive {get;init;}
    public bool InspectionScope {get;init;}
    public bool ControlledReview {get;init;}
    public bool HistoricalReview {get;init;}
    public bool M12Verified {get;init;}
    public bool D1Verified {get;init;}
    public bool StairSafe {get;init;}
    public bool ConveyorIsolated {get;init;}
    public bool GuardOpen {get;init;}
    public bool JamReleased {get;init;}
    public bool GuardSecured {get;init;}
    public bool ClampFixed {get;init;}
    public bool Drained {get;init;}
    public long ConveyorRevision {get;init;}
    public long? UpperCheckRevision {get;init;}
    public bool BranchASeen {get;init;}
    public bool BranchBSeen {get;init;}
    public bool CoreA {get;init;}
    public bool CoreB {get;init;}
    public string CoreMethod {get;init;}="";
    public bool CoreSampleTaken {get;init;}
    public bool CoreWindingCleared {get;init;}
    public bool CoreValvesChecked {get;init;}
    public bool CoreValveA {get;init;}
    public bool CoreValveB {get;init;}
    public bool ReserveIsolated {get;init;}=true;
    public bool ConnectorSeated {get;init;}
    public bool ReservePowered {get;init;}
    public LocalLoad Load {get;init;}
    public long ReserveRevision {get;init;}
    public long? TestRevision {get;init;}
    public bool KitchenFeed {get;init;}
    public bool InnerLatch {get;init;}
    public bool TowerPermit {get;init;}
    public bool TowerDuty {get;init;}
    public bool TowerIsolation {get;init;}
    public bool TowerReturn {get;init;}
    public bool W04Invited {get;init;}
    public bool W04Scope {get;init;}
    [JsonIgnore] public bool UpperSafe=>ConveyorIsolated&&GuardSecured&&ClampFixed&&Drained&&UpperCheckRevision==ConveyorRevision;
    [JsonIgnore] public bool LocalStable=>ReservePowered&&ConnectorSeated&&TestRevision==ReserveRevision&&Load is LocalLoad.Kitchen or LocalLoad.Alternating;
}
public sealed record LocalResidentGroup(string Room,ImmutableArray<string> People,string Scene,string Spot,bool LocalCheckConsent);
public sealed record ResidenceObservation(string Room,ImmutableArray<string> People,string SourceRoot,long ActionClock,string Purpose);
public sealed record ChapterMaterialAccess(string Id,string Version,string TranslationVersion,string Fingerprint,string SourceRoot,string AcquiredAt);
public sealed record ChapterWorldMeaning(string Quest,string? DraftHash,bool StructuredAssistance,ImmutableDictionary<string,string> Fields);
public sealed record ChapterRecordScope(string Record,string Version,ImmutableArray<string> Fields,string Recipient,string Purpose,bool IdentityPublication,bool IndividualConsent,string Grantor);

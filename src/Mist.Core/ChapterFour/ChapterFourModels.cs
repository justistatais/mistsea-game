using System.Collections.Immutable;
using System.Text.Json;
using Mist.Core.ChapterTwo;
using Mist.Core.Exploration;

namespace Mist.Core.ChapterFour;
public sealed record ChapterFourPackage(string Version,string Scope,string ReviewStatus,string HumanReview,
    ImmutableArray<ChapterScene> Scenes,ImmutableArray<ChapterMaterial> Materials,ImmutableArray<ChapterStation> Stations,
    ImmutableArray<ChapterWritingTask> WritingTasks,ImmutableDictionary<string,string> PortGates)
{
    // Only the learner package is parsed. Author cards and answer files are never model input.
    public static ChapterFourPackage ParseJson(string json)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;Mist.Core.Contracts.StrictJson.CheckDuplicates(root);
        if(!root.GetProperty("player_projection").GetBoolean())throw new InvalidDataException("Author-only files cannot become a learner runtime package.");
        foreach(var band in root.GetProperty("target_bands").EnumerateObject())if(band.Value.GetDecimal()!=6.5m)throw new InvalidDataException("R4 learner targets remain reading/listening/writing 6.5.");
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
        var package=new ChapterFourPackage(S(root,"content_version"),S(root,"scope"),S(root,"review_status"),S(root,"human_review"),scenes.ToImmutable(),materials,stations,writes,gates.ToImmutable());Validate(package);return package;
    }
    public static void Validate(ChapterFourPackage p)
    {
        string[] expected=["R4-S01","R4-S02","R4-S03","R4-S04","R4-S05","R4-S06","R4-S07","R4-S08","R4-O01","R4-O02"];
        if(p.Scope!="authoring_candidate_not_runtime"||p.ReviewStatus!="original_unreviewed"||p.HumanReview!="human_pending"||string.IsNullOrWhiteSpace(p.Version)||!p.Scenes.Select(s=>s.Geometry.Id).Order().SequenceEqual(expected.Order()))throw new InvalidDataException("R4 scope, ten registered scenes and review status must be explicit.");
        var scenes=p.Scenes.ToDictionary(s=>s.Geometry.Id);var ports=scenes.Values.Sum(s=>s.Geometry.Ports.Length);if(ports!=22)throw new InvalidDataException("R4 has exactly twenty-two reciprocal ports.");
        var expectedEdges=new HashSet<string>(["R4-S01|R4-S02","R4-S02|R4-S03","R4-S03|R4-S04","R4-S02|R4-S05","R4-S05|R4-S06","R4-S06|R4-S07","R4-S02|R4-S07","R4-S02|R4-S08","R4-S04|R4-S08","R4-O01|R4-S02","R4-O02|R4-S03"]);
        var seen=new HashSet<string>();
        foreach(var scene in scenes.Values)
        {
            var nav=new NavigationGrid(scene.Geometry);if(scene.Geometry.Ports.Select(x=>x.Id).Distinct().Count()!=scene.Geometry.Ports.Length||scene.Geometry.Spots.Select(x=>x.Id).Distinct().Count()!=scene.Geometry.Spots.Length)throw new InvalidDataException("Duplicate R4 stand.");
            foreach(var port in scene.Geometry.Ports)
            {
                if(!scenes.TryGetValue(port.TargetScene,out var target)||!nav.CanStand(port.Stand))throw new InvalidDataException("Invalid R4 port stand.");
                var pair=target.Geometry.Ports.SingleOrDefault(x=>x.Id==port.TargetPort);
                if(pair is null||pair.TargetScene!=scene.Geometry.Id||pair.TargetPort!=port.Id||!new NavigationGrid(target.Geometry).CanStand(pair.Stand)||!p.PortGates.ContainsKey(port.Id)||p.PortGates[port.Id]!=p.PortGates[pair.Id])throw new InvalidDataException("Unpaired R4 port.");
                seen.Add(string.Join("|",new[]{scene.Geometry.Id,port.TargetScene}.Order(StringComparer.Ordinal)));
            }
            foreach(var spot in scene.Geometry.Spots)if(!nav.CanStand(spot.Stand,true))throw new InvalidDataException("R4 actions need a safe external work stand.");
        }
        if(!seen.SetEquals(expectedEdges)||p.Materials.Select(m=>m.Id).Distinct().Count()!=p.Materials.Length||p.Stations.Select(x=>x.Action).Distinct().Count()!=p.Stations.Length)throw new InvalidDataException("R4 graph/material/action identities changed.");
        bool SpotExists(string scene,string spot)=>scenes.TryGetValue(scene,out var s)&&s.Geometry.Spots.Any(x=>x.Id==spot);
        foreach(var m in p.Materials)if(!SpotExists(m.Scene,m.Spot)||m.ReviewStatus!="original_unreviewed"||m.HumanReview!="human_pending"||m.Paragraphs.IsDefaultOrEmpty||m.Paragraphs.Any(x=>string.IsNullOrWhiteSpace(x.English)||string.IsNullOrWhiteSpace(x.Chinese))||m.Kind=="listening_script"&&!string.IsNullOrEmpty(m.AudioAsset))throw new InvalidDataException("Unreviewed R4 materials cannot claim recorded audio.");
        foreach(var st in p.Stations)if(!scenes.Values.Any(s=>s.Geometry.Spots.Any(x=>x.Id==st.Spot)))throw new InvalidDataException("Missing R4 action stand.");
        foreach(var wt in p.WritingTasks)if(!scenes.Values.Any(s=>s.Geometry.Spots.Any(x=>x.Id==wt.Spot)))throw new InvalidDataException("Missing writing stand.");
    }
}

public sealed record GlasshavenGate(string Name,bool Allowed,string ContentVersion);
public sealed record GlasshavenRecordScope(string Report,string Version,ImmutableArray<string> Fields,string Recipient,string Purpose,string Grantor);
public sealed record FieldSourceComparison(bool IndependentTeams,string Window,string Units,string Causality,string UncontrolledConditions);
public sealed record ReportVersionComparison(string Manuscript,string Issued,string Register,string ReportingVersion,ImmutableArray<string> RemovedRows,bool CopyIndependent);
public sealed record TrialConfiguration(string Controller,int Load,bool Reserve,bool Isolation,string Contact,string Operator,string Reviewer,long Revision,long OutputRevision);
public sealed record TrialObservation(int Minute,string Result);
public sealed record ExperimentRun(Guid Id,TrialConfiguration Configuration,int Minutes,ImmutableArray<TrialObservation> Observations,string Outcome,string Kind,string SourceRoot);
public sealed record TrialPair(string Question,string A,string B);
public sealed record GlasshavenTowerLoad(int Protection,int Calibration,int Demonstration)
{
    public void Validate(){if(Protection!=2||Calibration is <0 or >1||Demonstration!=0)throw new InvalidOperationException("棱镜当前班必须保留两单位保护，并停止展示负荷；校准服务可留一或明确交接延后。");}
}
public sealed record TechnicalHandover(string Destination,string Scope,string Evidence,string Location,string MaraCurrentStatus,ImmutableArray<string> PublicPreparation,string PolicyDecision);
public sealed record DerivedReviewCopy(string Version,int Observers,int Service,string SourceRoot,string Recipient,string Purpose,bool IdentitiesIncluded);
public sealed record ChapterFourState
{
    public required string ContentVersion {get;init;}
    public bool SampleReceived {get;init;}
    public bool PublicAssignment {get;init;}
    public bool ReportScope {get;init;}
    public bool FieldCompared {get;init;}
    public bool VersionsCompared {get;init;}
    public bool OldPermit {get;init;}
    public bool OldIsolated {get;init;}
    public bool ReserveCaseOpen {get;init;}
    public bool ReserveCaseCleared {get;init;}
    public bool ReserveCaseSecured {get;init;}
    public bool OldGuard {get;init;}
    public bool OldSocketChecked {get;init;}
    public bool OldReserve {get;init;}
    public int OldLoad {get;init;}=40;
    public long OldRevision {get;init;}
    public long InstrumentRevision {get;init;}
    public long? ZeroCheckRevision {get;init;}
    public long? ReferenceCheckRevision {get;init;}
    public bool IslandIsolation {get;init;}
    public bool IslandReserve {get;init;}
    public bool IslandGuard {get;init;}
    public string TrialOperator {get;init;}="";
    public string TrialReviewer {get;init;}="";
    public int IslandLoad {get;init;}=40;
    public string IslandContact {get;init;}="unselected";
    public long IslandRevision {get;init;}
    public long? ConfigurationCheckRevision {get;init;}
    public bool FieldEntryAuthorized {get;init;}
    public bool OutputIsolated {get;init;}
    public string OutputRoute {get;init;}="unselected";
    public bool CouplerSeated {get;init;}
    public bool OutputGuard {get;init;}
    public long OutputRevision {get;init;}
    public long? OutputCheckRevision {get;init;}
    public bool LogisticsLatch {get;init;}
    public bool PrismPermit {get;init;}
    public GlasshavenTowerLoad PrismLoad {get;init;}=new(2,1,1);
    public long PrismRevision {get;init;}
    public bool PrismDuty {get;init;}
    public bool PrismIsolation {get;init;}
    public long? PrismCheckRevision {get;init;}
    public bool PrismReturn {get;init;}
    public bool NorthLocationVerified {get;init;}
    public bool VossExchange {get;init;}
    public bool PreparationPublic {get;init;}
    public bool MiraReviewInvited {get;init;}
    public bool ReviewCopyGenerated {get;init;}
}

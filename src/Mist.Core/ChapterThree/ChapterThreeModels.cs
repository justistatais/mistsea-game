using System.Collections.Immutable;
using System.Text.Json;
using Mist.Core.ChapterTwo;
using Mist.Core.Exploration;

namespace Mist.Core.ChapterThree;
public sealed record ChapterThreePackage(string Version,string Scope,string ReviewStatus,string HumanReview,
    ImmutableArray<ChapterScene> Scenes,ImmutableArray<ChapterMaterial> Materials,ImmutableArray<ChapterStation> Stations,
    ImmutableArray<ChapterWritingTask> WritingTasks,ImmutableDictionary<string,string> PortGates)
{
    // Only the learner package is parsed. Author cards and answer files are never model input.
    public static ChapterThreePackage ParseJson(string json)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;Mist.Core.Contracts.StrictJson.CheckDuplicates(root);
        if(!root.GetProperty("player_projection").GetBoolean())throw new InvalidDataException("Author-only files cannot become a learner runtime package.");
        foreach(var band in root.GetProperty("target_bands").EnumerateObject())if(band.Value.GetDecimal()!=6.5m)throw new InvalidDataException("R3 learner targets remain reading/listening/writing 6.5.");
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
        var package=new ChapterThreePackage(S(root,"content_version"),S(root,"scope"),S(root,"review_status"),S(root,"human_review"),scenes.ToImmutable(),materials,stations,writes,gates.ToImmutable());Validate(package);return package;
    }
    public static void Validate(ChapterThreePackage p)
    {
        string[] expected=["R3-S01","R3-S02","R3-S03","R3-S04","R3-S05","R3-S06","R3-S07","R3-S08","R3-S09","R3-O01","R3-O02"];
        if(p.Scope!="authoring_candidate_not_runtime"||p.ReviewStatus!="original_unreviewed"||p.HumanReview!="human_pending"||string.IsNullOrWhiteSpace(p.Version)||!p.Scenes.Select(s=>s.Geometry.Id).Order().SequenceEqual(expected.Order()))throw new InvalidDataException("R3 scope, eleven registered scenes and review status must be explicit.");
        var scenes=p.Scenes.ToDictionary(s=>s.Geometry.Id);var ports=scenes.Values.Sum(s=>s.Geometry.Ports.Length);if(ports!=22)throw new InvalidDataException("R3 has exactly twenty-two reciprocal ports.");
        var expectedEdges=new HashSet<string>(["R3-S01|R3-S02","R3-S02|R3-S03","R3-S02|R3-S04","R3-S04|R3-S05","R3-S05|R3-S06","R3-S06|R3-S07","R3-S04|R3-S07","R3-S02|R3-S08","R3-S08|R3-S09","R3-O01|R3-S02","R3-O02|R3-S05"]);
        var seen=new HashSet<string>();
        foreach(var scene in scenes.Values)
        {
            var nav=new NavigationGrid(scene.Geometry);if(scene.Geometry.Ports.Select(x=>x.Id).Distinct().Count()!=scene.Geometry.Ports.Length||scene.Geometry.Spots.Select(x=>x.Id).Distinct().Count()!=scene.Geometry.Spots.Length)throw new InvalidDataException("Duplicate R3 stand.");
            foreach(var port in scene.Geometry.Ports)
            {
                if(!scenes.TryGetValue(port.TargetScene,out var target)||!nav.CanStand(port.Stand))throw new InvalidDataException("Invalid R3 port stand.");
                var pair=target.Geometry.Ports.SingleOrDefault(x=>x.Id==port.TargetPort);
                if(pair is null||pair.TargetScene!=scene.Geometry.Id||pair.TargetPort!=port.Id||!new NavigationGrid(target.Geometry).CanStand(pair.Stand)||!p.PortGates.ContainsKey(port.Id)||p.PortGates[port.Id]!=p.PortGates[pair.Id])throw new InvalidDataException("Unpaired R3 port.");
                seen.Add(string.Join("|",new[]{scene.Geometry.Id,port.TargetScene}.Order(StringComparer.Ordinal)));
            }
            foreach(var spot in scene.Geometry.Spots)if(!nav.CanStand(spot.Stand,true))throw new InvalidDataException("R3 actions need a safe external work stand.");
        }
        if(!seen.SetEquals(expectedEdges)||p.Materials.Select(m=>m.Id).Distinct().Count()!=p.Materials.Length||p.Stations.Select(x=>x.Action).Distinct().Count()!=p.Stations.Length)throw new InvalidDataException("R3 graph/material/action identities changed.");
        bool SpotExists(string scene,string spot)=>scenes.TryGetValue(scene,out var s)&&s.Geometry.Spots.Any(x=>x.Id==spot);
        foreach(var m in p.Materials)if(!SpotExists(m.Scene,m.Spot)||m.ReviewStatus!="original_unreviewed"||m.HumanReview!="human_pending"||m.Paragraphs.IsDefaultOrEmpty||m.Paragraphs.Any(x=>string.IsNullOrWhiteSpace(x.English)||string.IsNullOrWhiteSpace(x.Chinese))||m.Kind=="listening_script"&&!string.IsNullOrEmpty(m.AudioAsset))throw new InvalidDataException("Unreviewed R3 materials cannot claim recorded audio.");
        foreach(var st in p.Stations)if(!scenes.Values.Any(s=>s.Geometry.Spots.Any(x=>x.Id==st.Spot)))throw new InvalidDataException("Missing R3 action stand.");
        foreach(var wt in p.WritingTasks)if(!scenes.Values.Any(s=>s.Geometry.Spots.Any(x=>x.Id==wt.Spot)))throw new InvalidDataException("Missing writing stand.");
    }
}

public sealed record MiningAllocation(int Shelter,int Retrofit,int Production)
{
    public void Validate()
    {if(Shelter<2||Retrofit<1||Production is <0 or >3||Shelter+Retrofit+Production!=6)throw new InvalidOperationException("Six local work packages require at least two shelter and one retrofit; production is bounded at three.");}
}
public sealed record MiningLoadPolicy(string Mode,int Extraction,string Window)
{
    public void Validate()
    {if(Mode=="early_reduce"&&Extraction==0&&Window=="none")return;if(Mode=="time_limited_transition"&&Extraction is >=1 and <=3&&Window=="current_watch_only")return;throw new InvalidOperationException("Essential load is seven of ten; extraction requires a bounded current watch or an immediate reduction.");}
}
public sealed record MiningDuty(string Post,string Officer,string Shift);
public sealed record MiningWorker(string Id,string Scene,string Spot,string Status,long ActionClock);
public sealed record MiningBatchComparison(string Batch,string RawVersion,string CopyVersion,string AcceptanceVersion,
    ImmutableArray<string> IncompleteChecks,bool CopyIndependent,string OrenResponsibility,string FullCausality,string MaraCurrentStatus);
public sealed record MiningTowerHandover(string Tower,string Destination,string Batch,string SampleSeal,
    ImmutableDictionary<string,string> SourceVersions,long SafeConfiguration,string Recipient,string Purpose,
    string Causality,string MaraCurrentStatus);
public sealed record MiningGate(string Name,bool Allowed,string ContentVersion);
public sealed record MiningEncounterResult(string Encounter,bool Completed,Guid Instance,Guid Attempt,string Method,long Configuration);
public sealed record ChapterThreeState
{
    public required string ContentVersion {get;init;}
    public bool Landing {get;init;}
    public bool Commission {get;init;}
    public bool Households {get;init;}
    public bool BasicDelivered {get;init;}
    public bool EntryInstructions {get;init;}
    public bool BasicGear {get;init;}
    public bool LadderSafe {get;init;}
    public bool LiftInspected {get;init;}
    public bool SignalLocated {get;init;}
    public bool PumpIsolated {get;init;}
    public string PumpMode {get;init;}="";
    public bool Drained {get;init;}
    public long PumpRevision {get;init;}
    public long? WaterCheck {get;init;}
    public bool WorkersLocated {get;init;}
    public bool WorkersReceived {get;init;}
    public bool FieldInstrument {get;init;}
    public bool BypassInspected {get;init;}
    public bool FreightLiftRepaired {get;init;}
    public bool FreightLiftTested {get;init;}
    public bool WorkshopCopy {get;init;}
    public bool SignedAcceptance {get;init;}
    public bool Comparison {get;init;}
    public bool Accountability {get;init;}
    public bool StoresInspected {get;init;}
    public MiningAllocation? Allocation {get;init;}
    public bool ExtraDelivered {get;init;}
    public ImmutableArray<MiningDuty> Duties {get;init;}=[];
    public MiningLoadPolicy? Policy {get;init;}
    public bool ShiftClosed {get;init;}
    public long Configuration {get;init;}
    public int TowerExtraction {get;init;}
    public long? TowerTest {get;init;}
    public bool FeedStopped {get;init;}
    public bool ArmBraced {get;init;}
    public bool TowerDuty {get;init;}
    public bool TowerReturn {get;init;}
    public bool SamplePacked {get;init;}
    public string AlarmInvitation {get;init;}="unoffered";
    public bool AlarmInspected {get;init;}
    public bool AlarmPromise {get;init;}
    public bool AlarmRepair {get;init;}
    public bool AlarmReviewRequested {get;init;}
    public bool AlarmReviewed {get;init;}
    public long AlarmRevision {get;init;}
    public long? AlarmReviewRevision {get;init;}
    public bool TowerPermit {get;init;}
    [System.Text.Json.Serialization.JsonIgnore] public bool MineRouteSafe=>PumpIsolated&&Drained&&WaterCheck==PumpRevision;
    [System.Text.Json.Serialization.JsonIgnore] public int CurrentExtraction=>Policy is null||Policy.Mode=="early_reduce"||ShiftClosed?0:Policy.Extraction;
    [System.Text.Json.Serialization.JsonIgnore] public bool CurrentLoadAllowed=>Policy is not null&&7+CurrentExtraction<=10&&(CurrentExtraction==0||Duties.Length==2&&Duties.Select(x=>x.Officer).Distinct().Count()==2&&Duties.Select(x=>x.Post).Distinct().Count()==2&&Duties.All(x=>x.Shift=="current_watch_only"));
    [System.Text.Json.Serialization.JsonIgnore] public bool TowerCurrent=>CurrentLoadAllowed&&TowerExtraction==CurrentExtraction&&TowerTest==Configuration&&FeedStopped&&ArmBraced&&TowerDuty&&TowerReturn;
}



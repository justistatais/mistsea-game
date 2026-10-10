using System.Collections.Immutable;
using System.Text.Json;
using Mist.Core.ChapterTwo;
using Mist.Core.Exploration;

namespace Mist.Core.ChapterSix;
public sealed record ChapterSixPackage(string Version,string Scope,string ReviewStatus,string HumanReview,
    ImmutableArray<ChapterScene> Scenes,ImmutableArray<ChapterMaterial> Materials,ImmutableArray<ChapterStation> Stations,
    ImmutableArray<ChapterWritingTask> WritingTasks,ImmutableDictionary<string,string> PortGates,ImmutableArray<FacilityConservationContract> Facilities)
{
    // Only the learner package is parsed. Author cards and answer files are never model input.
    public static ChapterSixPackage ParseJson(string json)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;Mist.Core.Contracts.StrictJson.CheckDuplicates(root);
        if(!root.GetProperty("player_projection").GetBoolean())throw new InvalidDataException("Author-only files cannot become a learner runtime package.");
        foreach(var band in root.GetProperty("target_bands").EnumerateObject())if(band.Value.GetDecimal()!=6.5m)throw new InvalidDataException("R6 learner targets remain reading/listening/writing 6.5.");
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
        var facilities=root.GetProperty("facility_contracts").EnumerateArray().Select(j=>new FacilityConservationContract(S(j,"region"),S(j,"tower"),S(j,"prior_quest"),S(j,"physical_spot"),S(j,"version"),S(j,"installer"),j.GetProperty("watch_posts").EnumerateArray().Select(x=>x.GetString()!).ToImmutableArray(),j.GetProperty("stock").EnumerateObject().ToImmutableDictionary(x=>x.Name,x=>x.Value.GetInt32()))).ToImmutableArray();
        var package=new ChapterSixPackage(S(root,"content_version"),S(root,"scope"),S(root,"review_status"),S(root,"human_review"),scenes.ToImmutable(),materials,stations,writes,gates.ToImmutable(),facilities);Validate(package);return package;
    }
    public static void Validate(ChapterSixPackage p)
    {
        string[] expected=["R6-S01","R6-S02","R6-S03","R6-S04","R6-S05","R6-S06","R6-S07","R6-S08","R6-O01","R6-O02"];
        if(p.Scope!="authoring_candidate_not_runtime"||p.ReviewStatus!="original_unreviewed"||p.HumanReview!="human_pending"||string.IsNullOrWhiteSpace(p.Version)||!p.Scenes.Select(s=>s.Geometry.Id).Order().SequenceEqual(expected.Order()))throw new InvalidDataException("R6 scope, ten registered scenes and review status must be explicit.");
        var scenes=p.Scenes.ToDictionary(s=>s.Geometry.Id);var ports=scenes.Values.Sum(s=>s.Geometry.Ports.Length);if(ports!=22)throw new InvalidDataException("R6 has exactly twenty-two reciprocal ports.");
        var expectedEdges=new HashSet<string>(["R6-S01|R6-S02","R6-S02|R6-S03","R6-S03|R6-S04","R6-S02|R6-S04","R6-S02|R6-S05","R6-S05|R6-S06","R6-S06|R6-S07","R6-S07|R6-S08","R6-S02|R6-S08","R6-O01|R6-S03","R6-O02|R6-S05"]);
        var seen=new HashSet<string>();
        foreach(var scene in scenes.Values)
        {
            var nav=new NavigationGrid(scene.Geometry);if(scene.Geometry.Ports.Select(x=>x.Id).Distinct().Count()!=scene.Geometry.Ports.Length||scene.Geometry.Spots.Select(x=>x.Id).Distinct().Count()!=scene.Geometry.Spots.Length)throw new InvalidDataException("Duplicate R6 stand.");
            foreach(var port in scene.Geometry.Ports)
            {
                if(!scenes.TryGetValue(port.TargetScene,out var target)||!nav.CanStand(port.Stand))throw new InvalidDataException("Invalid R6 port stand.");
                var pair=target.Geometry.Ports.SingleOrDefault(x=>x.Id==port.TargetPort);
                if(pair is null||pair.TargetScene!=scene.Geometry.Id||pair.TargetPort!=port.Id||!new NavigationGrid(target.Geometry).CanStand(pair.Stand)||!p.PortGates.ContainsKey(port.Id)||p.PortGates[port.Id]!=p.PortGates[pair.Id])throw new InvalidDataException("Unpaired R6 port.");
                seen.Add(string.Join("|",new[]{scene.Geometry.Id,port.TargetScene}.Order(StringComparer.Ordinal)));
            }
            foreach(var spot in scene.Geometry.Spots)if(!nav.CanStand(spot.Stand,true))throw new InvalidDataException("R6 actions need a safe external work stand.");
        }
        if(!seen.SetEquals(expectedEdges)||p.Materials.Select(m=>m.Id).Distinct().Count()!=p.Materials.Length||p.Stations.Select(x=>x.Action).Distinct().Count()!=p.Stations.Length)throw new InvalidDataException("R6 graph/material/action identities changed.");
        bool SpotExists(string scene,string spot)=>scenes.TryGetValue(scene,out var s)&&s.Geometry.Spots.Any(x=>x.Id==spot);
        foreach(var m in p.Materials)if(!SpotExists(m.Scene,m.Spot)||m.ReviewStatus!="original_unreviewed"||m.HumanReview!="human_pending"||m.Paragraphs.IsDefaultOrEmpty||m.Paragraphs.Any(x=>string.IsNullOrWhiteSpace(x.English)||string.IsNullOrWhiteSpace(x.Chinese))||m.Kind=="listening_script"&&!string.IsNullOrEmpty(m.AudioAsset))throw new InvalidDataException("Unreviewed R6 materials cannot claim recorded audio.");
        foreach(var st in p.Stations)if(!scenes.Values.Any(s=>s.Geometry.Spots.Any(x=>x.Id==st.Spot)))throw new InvalidDataException("Missing R6 action stand.");
        foreach(var wt in p.WritingTasks)if(!scenes.Values.Any(s=>s.Geometry.Spots.Any(x=>x.Id==wt.Spot)))throw new InvalidDataException("Missing writing stand.");
        if(p.Facilities.Length!=6||!p.Facilities.Select(f=>f.Region).Order().SequenceEqual(Enumerable.Range(1,6).Select(i=>"R"+i))||p.Facilities.Any(f=>f.Tower!="TWR-"+f.Region||f.WatchPosts.Length!=2||f.WatchPosts.Distinct().Count()!=2||f.Stock.Count!=3||f.Stock.GetValueOrDefault("base_isolator")!=1||f.Stock.GetValueOrDefault("local_backup")!=1||f.Stock.GetValueOrDefault("base_provision")!=2))throw new InvalidDataException("Six bounded original facility sources and two qualified local posts must be declared.");
    }
}


// All numbers/functional local posts below are bounded original candidates disclosed in the
// learner source. They are not new core characters or automatically completed work.
public sealed record FrontierGate(string Name,bool Allowed,string ContentVersion);
public sealed record FrontierEncounterResult(string Encounter,bool Completed,Guid Instance,Guid Attempt,string Method,long Configuration);
public sealed record SurveyConfiguration(string Corridor,string ZeroChannel,int ZeroReading,string ReferenceChannel,int ReferenceReading,string RiskScope,string Window,long Revision);
public sealed record FrontierCargoPlan(string Tower,ImmutableArray<string> Resources,ImmutableArray<string> Escorts,string Destination,string Fallback,string Segment);
public sealed record FrontierCargoState(string Stage,string Scene,ImmutableArray<string> ItemIds,long ActorClock,bool SafelyDeposited);
public sealed record FacilityConservationContract(string Region,string Tower,string PriorQuest,string PhysicalSpot,string Version,string Installer,ImmutableArray<string> WatchPosts,ImmutableDictionary<string,int> Stock);
public sealed record FacilityInspection(string Region,string ContractVersion,string PriorQuestReceipt,string CurrentSafetyReceipt,ImmutableArray<string> StockIds,ImmutableArray<string> QualifiedPosts,long SafetyRevision);
public sealed record EngineeringPhysicalOrigin(string Region,string Scene,string Spot,Point2 Position,string GeometrySha256,string PriorQuestReceipt,long SafetyRevision,long ActionClock);
public sealed record PhysicalProcurement(string Source,int Step,EngineeringPhysicalOrigin Origin,ImmutableArray<string> SerialIds,string WorkReceipt);
public sealed record FacilityWorkStep(string Tower,string Type,int Step,string Task,EngineeringPhysicalOrigin Origin,string ContractVersion);
public sealed record FrontierEncounterState(string Encounter,Mist.Core.Combat.BattleSnapshot Battle,Mist.Core.Combat.BattleSnapshot Entry,Mist.Core.Devices.ActorState ReturnActor,bool ThreatControlled=false);
public sealed record FrontierFieldTest(string Tower,string TechnicalVersion,long Configuration,string Stage,string LoadMode,bool Isolation,bool Backup,bool LocalReturn,bool Passed);
public sealed record FrontierSurveyResult(string Risk,string SupplyBoundary,string Window,string ReturnRoute,long Configuration);
public sealed record FrontierLocalResult(string Tower,string CargoReceipt,string BaselineWork,string SourceSafety,string Isolation,string ReturnRoute);
public sealed record FinaleDeparture(Guid VesselInstance,Guid Q604Instance,Guid PreparationId,string PreFinalCheckpoint,string SourceRegion,string Destination,string SourceBerthScene,Point2 SourceBoarding,string SourceBerthReceipt,string DestinationScene,Point2 Landing,string DestinationGeometrySha256,ImmutableArray<string> SourceReceipts,string RiskRegisterReceipt);
public sealed record ChapterSixState
{
    public required string ContentVersion {get;init;}
    public bool Landing {get;init;}
    public bool Briefing {get;init;}
    public bool BasicGear {get;init;}
    public string Corridor {get;init;}="";
    public bool GuideMarkers {get;init;}
    public bool ReturnAnchor {get;init;}
    public bool SurveyIsolation {get;init;}
    public SurveyConfiguration? Survey {get;init;}
    public long SurveyRevision {get;init;}
    public long? SurveyCheck {get;init;}
    public bool SurveyReported {get;init;}
    public FrontierCargoPlan? CargoPlan {get;init;}
    public string CargoStage {get;init;}="not_started";
    public bool CargoDelivered {get;init;}
    public bool FieldGuardControlled {get;init;}
    public int FieldDeviceStep {get;init;}
    public bool CargoProtected {get;init;}
    public bool PlatformIsolated {get;init;}
    public bool TowerReturn {get;init;}
    public long TowerRevision {get;init;}
    public long? TowerCheck {get;init;}
    public bool QualificationViewed {get;init;}
    public string PreparationDecision {get;init;}="";
    public string TessInvitation {get;init;}="unoffered";
    public bool TessRecipients {get;init;}
    public string SupplementDecision {get;init;}="unoffered";
    public ImmutableDictionary<string,int> SupplementTrips {get;init;}=ImmutableDictionary<string,int>.Empty;
    public int SupplementDelivered {get;init;}
    public bool TessMessage {get;init;}
    public bool TessSecondContact {get;init;}
    public bool TessReceivingConfirmed {get;init;}
    [System.Text.Json.Serialization.JsonIgnore] public bool SurveyCurrent=>SurveyIsolation&&Survey is not null&&SurveyCheck==SurveyRevision;
    [System.Text.Json.Serialization.JsonIgnore] public bool RidgeSafe=>CargoDelivered&&PlatformIsolated&&TowerReturn&&TowerCheck==TowerRevision;
}

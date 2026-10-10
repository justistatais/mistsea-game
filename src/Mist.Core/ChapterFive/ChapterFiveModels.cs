using System.Collections.Immutable;
using System.Text.Json;
using Mist.Core.ChapterTwo;
using Mist.Core.Exploration;

namespace Mist.Core.ChapterFive;
public sealed record ChapterFivePackage(string Version,string Scope,string ReviewStatus,string HumanReview,
    ImmutableArray<ChapterScene> Scenes,ImmutableArray<ChapterMaterial> Materials,ImmutableArray<ChapterStation> Stations,
    ImmutableArray<ChapterWritingTask> WritingTasks,ImmutableDictionary<string,string> PortGates)
{
    // Only the learner package is parsed. Author cards and answer files are never model input.
    public static ChapterFivePackage ParseJson(string json)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;Mist.Core.Contracts.StrictJson.CheckDuplicates(root);
        if(!root.GetProperty("player_projection").GetBoolean())throw new InvalidDataException("Author-only files cannot become a learner runtime package.");
        foreach(var band in root.GetProperty("target_bands").EnumerateObject())if(band.Value.GetDecimal()!=6.5m)throw new InvalidDataException("R5 learner targets remain reading/listening/writing 6.5.");
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
        var package=new ChapterFivePackage(S(root,"content_version"),S(root,"scope"),S(root,"review_status"),S(root,"human_review"),scenes.ToImmutable(),materials,stations,writes,gates.ToImmutable());Validate(package);return package;
    }
    public static void Validate(ChapterFivePackage p)
    {
        string[] expected=["R5-S01","R5-S02","R5-S03","R5-S04","R5-S05","R5-S06","R5-S07","R5-S08","R5-O01","R5-O02"];
        if(p.Scope!="authoring_candidate_not_runtime"||p.ReviewStatus!="original_unreviewed"||p.HumanReview!="human_pending"||string.IsNullOrWhiteSpace(p.Version)||!p.Scenes.Select(s=>s.Geometry.Id).Order().SequenceEqual(expected.Order()))throw new InvalidDataException("R5 scope, ten registered scenes and review status must be explicit.");
        var scenes=p.Scenes.ToDictionary(s=>s.Geometry.Id);var ports=scenes.Values.Sum(s=>s.Geometry.Ports.Length);if(ports!=22)throw new InvalidDataException("R5 has exactly twenty-two reciprocal ports.");
        var expectedEdges=new HashSet<string>(["R5-S01|R5-S02","R5-S02|R5-S03","R5-S03|R5-S05","R5-S03|R5-S04","R5-S04|R5-S05","R5-S05|R5-S06","R5-S02|R5-S06","R5-S02|R5-S07","R5-S07|R5-S08","R5-O01|R5-S02","R5-O02|R5-S02"]);
        var seen=new HashSet<string>();
        foreach(var scene in scenes.Values)
        {
            var nav=new NavigationGrid(scene.Geometry);if(scene.Geometry.Ports.Select(x=>x.Id).Distinct().Count()!=scene.Geometry.Ports.Length||scene.Geometry.Spots.Select(x=>x.Id).Distinct().Count()!=scene.Geometry.Spots.Length)throw new InvalidDataException("Duplicate R5 stand.");
            foreach(var port in scene.Geometry.Ports)
            {
                if(!scenes.TryGetValue(port.TargetScene,out var target)||!nav.CanStand(port.Stand))throw new InvalidDataException("Invalid R5 port stand.");
                var pair=target.Geometry.Ports.SingleOrDefault(x=>x.Id==port.TargetPort);
                if(pair is null||pair.TargetScene!=scene.Geometry.Id||pair.TargetPort!=port.Id||!new NavigationGrid(target.Geometry).CanStand(pair.Stand)||!p.PortGates.ContainsKey(port.Id)||p.PortGates[port.Id]!=p.PortGates[pair.Id])throw new InvalidDataException("Unpaired R5 port.");
                seen.Add(string.Join("|",new[]{scene.Geometry.Id,port.TargetScene}.Order(StringComparer.Ordinal)));
            }
            foreach(var spot in scene.Geometry.Spots)if(!nav.CanStand(spot.Stand,true))throw new InvalidDataException("R5 actions need a safe external work stand.");
        }
        if(!seen.SetEquals(expectedEdges)||p.Materials.Select(m=>m.Id).Distinct().Count()!=p.Materials.Length||p.Stations.Select(x=>x.Action).Distinct().Count()!=p.Stations.Length)throw new InvalidDataException("R5 graph/material/action identities changed.");
        bool SpotExists(string scene,string spot)=>scenes.TryGetValue(scene,out var s)&&s.Geometry.Spots.Any(x=>x.Id==spot);
        foreach(var m in p.Materials)if(!SpotExists(m.Scene,m.Spot)||m.ReviewStatus!="original_unreviewed"||m.HumanReview!="human_pending"||m.Paragraphs.IsDefaultOrEmpty||m.Paragraphs.Any(x=>string.IsNullOrWhiteSpace(x.English)||string.IsNullOrWhiteSpace(x.Chinese))||m.Kind=="listening_script"&&!string.IsNullOrEmpty(m.AudioAsset))throw new InvalidDataException("Unreviewed R5 materials cannot claim recorded audio.");
        foreach(var st in p.Stations)if(!scenes.Values.Any(s=>s.Geometry.Spots.Any(x=>x.Id==st.Spot)))throw new InvalidDataException("Missing R5 action stand.");
        foreach(var wt in p.WritingTasks)if(!scenes.Values.Any(s=>s.Geometry.Spots.Any(x=>x.Id==wt.Spot)))throw new InvalidDataException("Missing writing stand.");
    }
}

public sealed record OldCityGate(string Name,bool Allowed,string ContentVersion);
public sealed record OldCityRecordScope(string Version,ImmutableArray<string> Fields,string Recipient,string Purpose,string Grantor);
public sealed record OldCityRouteComparison(string Historic,string Current,string Destination,bool HistoricalLineGrantsAccess);
public sealed record OldCityResponsibilityComparison(string DesignVersion,string RevisionVersion,string CorrectionVersion,string PolicyVersion,string TestVersion,string OverrideVersion,string DetentionVersion,string CopySource,string DesignResponsibility,string PolicyResponsibility,string ExecutionResponsibility);
public sealed record OldCityResponse(string Party,string SourceVersion,string Channel,string Response,string Scope);
public sealed record OldCityCustody(string Mode,string Publication,string OriginalCase,ImmutableArray<string> Custodians,bool OriginalsPreserved,bool IdentitiesReleased);
public sealed record OldCityTowerLoad(int Protection,int Preservation,int Reproduction)
{
    public void Validate(){if(Protection!=3||Preservation!=1||Reproduction!=0)throw new InvalidOperationException("归档当班需保护三、保存一、非必要复制零，容量四；不能挪六塔物资代替。");}
}
public sealed record OldCityHandover(string Destination,string Tower,string MaraCurrentStatus,string ResponsibilityScope,string Custody,string Publication,ImmutableArray<string> Supervisors,long TowerConfiguration,string Authority,string RookStatus,string PolicyDecision);
public sealed record MaraRescue(string Status,string Scene,string Source,string Reception,long ActionClock);
public sealed record PersonalAccessExercise(string SourceCopy,string Mode,ImmutableArray<string> Steps,bool SubjectAccepted,bool MiraAccepted,bool IdentitiesIncluded,string MainCustodyUnchanged);
public sealed record OrenMaintenancePost(string Mode,string Actor,string Receiver,string Supervisor,string Window,string Status,bool OriginalResponsibilityPreserved);
public sealed record ChapterFiveState
{
    public required string ContentVersion {get;init;}
    public bool Landing {get;init;}
    public bool BasicKit {get;init;}
    public bool MapCompared {get;init;}
    public string UsedRoute {get;init;}="";
    public bool CredentialChecked {get;init;}
    public bool CheckpointPassageChecked {get;init;}
    public bool TunnelIsolated {get;init;}
    public bool TunnelBraced {get;init;}
    public bool TunnelObstructionCleared {get;init;}
    public bool TunnelZeroChecked {get;init;}
    public bool TunnelPassageChecked {get;init;}
    public bool RescueScope {get;init;}
    public bool DetentionRecordsChecked {get;init;}
    public bool DoorDriveIsolated {get;init;}
    public bool DutyObstructionCleared {get;init;}
    public bool DetentionDoorChecked {get;init;}
    public bool MaraReleased {get;init;}
    public bool EvacuationBoltOpen {get;init;}
    public bool EvacuationChecked {get;init;}
    public bool MaraEscorted {get;init;}
    public bool MaraReceived {get;init;}
    public bool ResponsibilityScope {get;init;}
    public bool ResponsibilitiesCompared {get;init;}
    public string ReviewProcedure {get;init;}="";
    public ImmutableArray<string> ResponseParties {get;init;}=[];
    public OldCityCustody? Custody {get;init;}
    public ImmutableArray<string> Supervisors {get;init;}=[];
    public bool JointPermission {get;init;}
    public bool ArchiveRelayIsolated {get;init;}
    public bool ArchiveRackSecured {get;init;}
    public OldCityTowerLoad Load {get;init;}=new(3,1,1);
    public string Operator {get;init;}="";
    public long TowerRevision {get;init;}
    public long? TowerCheckRevision {get;init;}
    public bool TowerReturnChecked {get;init;}
    public bool OriginalCasePreserved {get;init;}
    public bool TransferDemandSent {get;init;}
    public bool RookRefusalRecorded {get;init;}
    public bool MiraInvited {get;init;}
    public bool OrenInvited {get;init;}
    public bool MiraExerciseComplete {get;init;}
    public bool MiraArrangementAccepted {get;init;}
    public bool OrenDutyAccepted {get;init;}
}

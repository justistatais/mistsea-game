using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Mist.Core.Exploration;
using Mist.Core.Quests;

namespace Mist.Core.Campaign;

public enum RescueLanding { Unselected, SouthMarker, NorthReach }
public enum CoastTimeline { Unconfirmed, DriftBeforeLightLoss, LightLossBeforeDrift, InsufficientEvidence }
public sealed record CampaignState(string Scope,string ContentVersion,bool ProloguePlayed=false,bool Q104Closed=false,bool GlimmerOwned=false,bool Q102Ready=false);
public sealed record PrologueState(
    bool ToolsAcquired=false,bool PathReinforced=false,CoastTimeline Timeline=CoastTimeline.Unconfirmed,
    bool PeopleOnShore=false,RescueLanding Landing=RescueLanding.Unselected,bool ShoreSafe=false,
    bool WinchIsolated=false,bool WinchCableSeated=false,bool WinchPowered=false,bool RopeFixed=false,
    long WinchRevision=0,long? WinchTestRevision=null,bool SurvivorsSheltered=false,bool SurvivorsRegistered=false,
    bool RelayEntranceFound=false,bool RelayIsolated=false,bool RelayLeadSeated=false,bool RelayPowered=false,
    long RelayRevision=0,long? RelayTestRevision=null,bool RelayBackDoorOpen=false,
    bool RecorderCollected=false,bool MailbagCollected=false,bool PickupAcknowledged=false,bool HarbourReceived=false)
{
    [JsonIgnore] public bool WinchReady=>WinchPowered&&WinchCableSeated&&RopeFixed&&Landing==RescueLanding.NorthReach&&WinchTestRevision==WinchRevision;
    [JsonIgnore] public bool RelayReady=>RelayPowered&&RelayLeadSeated&&RelayTestRevision==RelayRevision;
}
public sealed record RescueMessageMeaning(string Location,int People,string Need,string ApproachConstraint);
public sealed record HarbourRequestMeaning(string Purpose,string WorkScope,string PowerBoundary);
public sealed record CoastSurvivorGroup(ImmutableArray<string> People,string Location,string Phase);
// This is an explicitly confirmed structured interpretation, never automatic free English understanding.
public sealed record CampaignMeaning<T>(long Version,bool Confirmed,string? DraftHash,bool StructuredAssistance,T? Meaning);
public sealed record CampaignMaterial(string Id,string Title,string Scene,string Spot,string Modality,string English,string Chinese,
    string ReviewStatus,string CalibrationStatus,string AudioAsset="",string Version="candidate-1");
public sealed record CampaignStation(string Spot,string Label,ImmutableArray<string> Materials,ImmutableArray<string> Actions);
public sealed record ShoreEncounterDefinition(string Id,int Hp,int ArmorBp,int PressureThreshold,int Initiative,ImmutableArray<ShoreEnemyIntent> Pattern,string BalanceStatus);
public sealed record ShoreEnemyIntent(string Name,int Damage,string Target,bool Interruptible);
public sealed record CampaignPackage(string Version,string Scope,ImmutableArray<SceneGeometry> Scenes,
    ImmutableArray<CampaignMaterial> Materials,ImmutableArray<CampaignStation> Stations,ShoreEncounterDefinition ShoreEncounter,
    ImmutableArray<string> SourceRules,ImmutableArray<string> OriginalLocalDetails,ImmutableArray<string> Remaining);
public static class CampaignRules
{
    public const string Scope="normal_first_entry_candidate";
    public const int Survivors=4;
    public static readonly ImmutableArray<string> Quests=["Q-P01","Q-P02","Q-P03","Q-101"];
    public static string CurrentQuest(IEnumerable<QuestState> quests)=>quests.FirstOrDefault(q=>q.Lifecycle==QuestLifecycle.Active)?.Id??"Q-102";
    public static void Validate(CampaignPackage package)
    {
        string[] expected=["R0-S01","R0-S02","R0-S03","R0-S04","R0-S05","R1-S01","R1-S02","R1-S03"];
        if(package.Scope!=Scope||string.IsNullOrWhiteSpace(package.Version)||!package.Scenes.Select(s=>s.Id).Order().SequenceEqual(expected.Order())||package.Materials.Select(m=>m.Id).Distinct().Count()!=package.Materials.Length||package.Stations.Select(s=>s.Spot).Distinct().Count()!=package.Stations.Length)throw new InvalidDataException("序章候选包的范围或身份无效。");
        var scenes=package.Scenes.ToDictionary(s=>s.Id);
        foreach(var scene in package.Scenes)
        {
            var nav=new NavigationGrid(scene);
            if(scene.Ports.Select(p=>p.Id).Distinct().Count()!=scene.Ports.Length||scene.Spots.Select(p=>p.Id).Distinct().Count()!=scene.Spots.Length)throw new InvalidDataException("序章站位重复。");
            foreach(var port in scene.Ports)
            {
                if(!nav.CanStand(port.Stand)||!scenes.TryGetValue(port.TargetScene,out var other))throw new InvalidDataException("序章出口未配对。");
                var pair=other.Ports.SingleOrDefault(p=>p.Id==port.TargetPort);
                if(pair is null||pair.TargetScene!=scene.Id||pair.TargetPort!=port.Id||!new NavigationGrid(other).CanStand(pair.Stand))throw new InvalidDataException("序章出口反向身份不一致。");
            }
            foreach(var spot in scene.Spots)if(!nav.CanStand(spot.Stand))throw new InvalidDataException("序章交互站位不能抵达。");
        }
        foreach(var material in package.Materials)
            if(!scenes.TryGetValue(material.Scene,out var scene)||!scene.Spots.Any(s=>s.Id==material.Spot)||string.IsNullOrWhiteSpace(material.English)||material.ReviewStatus!="original_unreviewed"||material.CalibrationStatus!="human_pending"||material.Modality is not("reading" or "listening_script"))throw new InvalidDataException("材料不得把缺失正文、音轨或人工校准当作已完成。");
        foreach(var station in package.Stations)
            if(!package.Scenes.Any(s=>s.Spots.Any(p=>p.Id==station.Spot))||station.Materials.Any(m=>!package.Materials.Any(p=>p.Id==m&&p.Spot==station.Spot)))throw new InvalidDataException("材料投影与实际站位不一致。");
        var enemy=package.ShoreEncounter;
        if(enemy.Id!="SHORE-CREATURE"||enemy.Hp is <1 or >500||enemy.ArmorBp is <0 or >4500||enemy.PressureThreshold is <1 or >20||enemy.Pattern.IsDefaultOrEmpty||enemy.Pattern.Any(p=>p.Damage is <0 or >100||p.Target is not("unit" or "column" or "all"))||enemy.BalanceStatus!="candidate_unbalanced")throw new InvalidDataException("近岸遭遇必须有明确有限的候选参数。");
    }
}

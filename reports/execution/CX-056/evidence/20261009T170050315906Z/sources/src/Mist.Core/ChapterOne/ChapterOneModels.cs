using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Mist.Core.Campaign;
using Mist.Core.Exploration;

namespace Mist.Core.ChapterOne;

public sealed record ChapterOnePackage(string Version,string Scope,ImmutableArray<SceneGeometry> Scenes,ImmutableArray<CampaignMaterial> Materials,
    ImmutableArray<string> SourceRules,ImmutableArray<string> OriginalLocalDetails,ImmutableArray<string> Remaining);
public sealed record AccidentFindingMeaning(string TestOrigin,string ClockComparison,string RemovedProtection,string NoticeLimit,string ResponsibilityLimit);
public sealed record AccidentProposal(long Version,bool Confirmed,string? DraftHash,bool StructuredAssistance,AccidentFindingMeaning? Meaning);
public sealed record ChapterMaterialAccess(string Id,string Version,string BodySha256,string SourceRoot);
public sealed record TidegateLocalState(long Configuration,bool SupplyAvailable,long? InspectionRevision,bool Safe=false,string? SafetyReceipt=null,bool FinalInspection=false)
{
    [JsonIgnore] public bool Prepared=>SupplyAvailable&&InspectionRevision==Configuration;
}
public sealed record TidegateHandover(string VesselInstance,string VesselDefinition,string WatchOfficer,string[] Spaces,string Destination,string EvidenceFinding);
public static class ChapterOneRules
{
    public const string Scope="first_chapter_extension_candidate";
    public static readonly ImmutableArray<string> Evidence=["Q103-R-NOTICE","Q103-R-TEST","Q103-R-DUTY","Q103-R-CLOCK"];
    public static bool Admissible(AccidentFindingMeaning? m)=>m is{TestOrigin:"approved_preplanned_test",ClockComparison:"within_recorded_clock_uncertainty",RemovedProtection:"second_check_removed_before_test",NoticeLimit:"insufficient_on_its_own",ResponsibilityLimit:"specific_higher_intent_not_established"};
    public static void Validate(ChapterOnePackage p)
    {
        if(p.Scope!=Scope||string.IsNullOrWhiteSpace(p.Version)||!p.Scenes.Select(s=>s.Id).Order().SequenceEqual(new[]{"R1-S08","R1-S09","R1-S10"})||p.Materials.Select(m=>m.Id).Distinct().Count()!=p.Materials.Length)throw new InvalidDataException("首章扩展候选的范围或身份不符。");
        foreach(var scene in p.Scenes)
        {
            var nav=new NavigationGrid(scene);if(scene.Ports.Select(x=>x.Id).Distinct().Count()!=scene.Ports.Length||scene.Spots.Select(x=>x.Id).Distinct().Count()!=scene.Spots.Length)throw new InvalidDataException("首章实际站位重复。");
            if(scene.Ports.Any(x=>!nav.CanStand(x.Stand))||scene.Spots.Any(x=>!nav.CanStand(x.Stand)))throw new InvalidDataException("首章落点或工作站位不可站立。");
            foreach(var port in scene.Ports.Where(x=>p.Scenes.Any(s=>s.Id==x.TargetScene)))
            {var target=p.Scenes.Single(s=>s.Id==port.TargetScene).Ports.SingleOrDefault(x=>x.Id==port.TargetPort);if(target is null||target.TargetScene!=scene.Id||target.TargetPort!=port.Id)throw new InvalidDataException("首章内部出口未成对。");}
        }
        foreach(var m in p.Materials)if(!p.Scenes.Any(s=>s.Id==m.Scene&&s.Spots.Any(x=>x.Id==m.Spot))||string.IsNullOrWhiteSpace(m.English)||m.ReviewStatus!="original_unreviewed"||m.CalibrationStatus!="human_pending"||m.Modality!="reading")throw new InvalidDataException("正文、实际来源、审校与校准状态必须如实登记。");
        if(Evidence.Any(id=>!p.Materials.Any(m=>m.Id==id)))throw new InvalidDataException("事故核验所需来源不完整。");
    }
}

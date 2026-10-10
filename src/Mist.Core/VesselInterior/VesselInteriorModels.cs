using System.Collections.Immutable;
using Mist.Core.Exploration;

namespace Mist.Core.VesselInterior;

public enum VesselStationKind { Gangway,Deckwatch,HarborView,Archive,Correspondence,DraftDesk,Chart,Collection,Workbench,Roster,WorkOrders }
public sealed record VesselStation(string Scene,string Spot,VesselStationKind Kind,string Label);
public sealed record VesselInteriorPackage(string Version,string Scope,ImmutableArray<SceneGeometry> Scenes,
    ImmutableArray<VesselStation> Stations,ImmutableArray<string> Sources,ImmutableArray<string> OriginalLocalDetails,ImmutableArray<string> Remaining);
public sealed record VesselInteriorBinding(Guid Instance,string PackageVersion,string GeometrySha256,string HandoverReceipt);

public static class VesselInteriorRules
{
    public const string Scope="shared_glimmer_interior_candidate";
    public static readonly ImmutableArray<string> Spaces=["HUB-S01","HUB-S02","HUB-S03"];
    public static void Validate(VesselInteriorPackage package)
    {
        if(package.Scope!=Scope||string.IsNullOrWhiteSpace(package.Version)||!package.Scenes.Select(s=>s.Id).Order().SequenceEqual(Spaces.Order()))
            throw new InvalidDataException("船舱候选只制作共享微光号的三个空间。");
        var scenes=package.Scenes.ToDictionary(s=>s.Id);
        var roles=new Dictionary<VesselStationKind,string>
        {
            [VesselStationKind.Gangway]="HUB-S01.gangway",[VesselStationKind.Deckwatch]="HUB-S01.deckwatch",[VesselStationKind.HarborView]="HUB-S01.harbor_view",
            [VesselStationKind.Archive]="HUB-S02.archive",[VesselStationKind.Correspondence]="HUB-S02.correspondence",[VesselStationKind.DraftDesk]="HUB-S02.draft_desk",[VesselStationKind.Chart]="HUB-S02.chart",
            [VesselStationKind.Collection]="HUB-S03.collection",[VesselStationKind.Workbench]="HUB-S03.workbench",[VesselStationKind.Roster]="HUB-S03.roster",[VesselStationKind.WorkOrders]="HUB-S03.work_orders"
        };
        if(package.Stations.Length!=roles.Count||package.Stations.Select(s=>s.Kind).Distinct().Count()!=roles.Count||package.Stations.Any(s=>!roles.TryGetValue(s.Kind,out string? id)||s.Spot!=id||!s.Spot.StartsWith(s.Scene+".",StringComparison.Ordinal)||string.IsNullOrWhiteSpace(s.Label)))
            throw new InvalidDataException("船舱工位必须对应已有共享功能，不得新增世界写权限。");
        foreach(var scene in package.Scenes)
        {
            if(scene.Danger.Length!=0||scene.Ports.Select(p=>p.Id).Distinct().Count()!=scene.Ports.Length||scene.Spots.Select(s=>s.Id).Distinct().Count()!=scene.Spots.Length)
                throw new InvalidDataException("本船基础空间不能假设未制作的危险解除或重复工位。");
            var nav=new NavigationGrid(scene);if(scene.Ports.Length!=(scene.Id=="HUB-S02"?2:1))throw new InvalidDataException("船舱只保留甲板—档案室—检修舱两条连接。");
            foreach(var port in scene.Ports)
            {
                if(!scenes.TryGetValue(port.TargetScene,out var target)||!nav.CanStand(port.Stand,true))throw new InvalidDataException("船舱出口未定义或不可抵达。");
                var reverse=target.Ports.SingleOrDefault(p=>p.Id==port.TargetPort);
                if(reverse is null||reverse.TargetScene!=scene.Id||reverse.TargetPort!=port.Id||!new NavigationGrid(target).CanStand(reverse.Stand,true)||Math.Abs(Array.IndexOf(Spaces.ToArray(),scene.Id)-Array.IndexOf(Spaces.ToArray(),target.Id))!=1)
                    throw new InvalidDataException("船舱出口必须与相邻同船空间真实配对。");
                foreach(var spot in scene.Spots)if(nav.FindPath(port.Stand,spot.Stand,true).Length==0)throw new InvalidDataException("每个船舱工位都必须由真实导航抵达。");
            }
            foreach(var station in package.Stations.Where(s=>s.Scene==scene.Id))if(!scene.Spots.Any(s=>s.Id==station.Spot))throw new InvalidDataException("船舱功能没有实际站位。");
        }
        if(package.Sources.IsDefaultOrEmpty||package.OriginalLocalDetails.IsDefaultOrEmpty||package.Remaining.IsDefaultOrEmpty)throw new InvalidDataException("船舱原文、候选几何与未接入事项必须分别登记。");
    }
}

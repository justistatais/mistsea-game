using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Mist.Core.Contracts;
using Mist.Core.Exploration;

namespace Mist.Core.Voyage;

public sealed record VoyageSource(string Path,string Sha256,string Section);
public sealed record BerthDutyDefinition(string Id,string Region,string Scene,string BerthSpot,string Scope,string IssuanceBasis,string ReviewStatus);
public sealed record VoyagePackage(string Version,string Scope,string ReviewStatus,string From,string To,string ChartScene,string ChartSpot,
    BerthDutyDefinition DestinationDuty,ImmutableArray<VoyageSource> Sources,ImmutableArray<string> OriginalLocalDetails,ImmutableArray<string> Remaining);
public sealed record VoyageRequest(string Destination,string LandingScene,string LandingSpot,string AdmissionScope,string Purpose,string MentorCurrentStatus);
public sealed record ArrivalHandoff(Guid VesselInstance,string VesselDefinition,string From,string To,string Issuer,string DutyScope,string BerthScene,string BerthSpot,
    string HandoverReceipt,string CommissionSha256,long CommissionRevision,string DestinationGeometrySha256,string Version,string ReviewStatus,
    string CheckedBy,string InspectionScene,Point2 InspectionPosition,long InspectionActionClock);
public sealed record VoyageSafetyBinding(Guid VesselInstance,string From,string To,string PackageVersion,string PackageSha256,string HandoverReceipt,
    string HandoffEntity,string ChartGeometrySha256,string OriginGeometrySha256,string DestinationGeometrySha256);
public sealed record VesselArrivalProof(Guid VesselInstance,string From,string To,string Scene,Point2 Position,long ActionClock,
    string SailReceipt,string RouteReceipt,string BerthReceipt,string HandoffReceipt,string GeometrySha256,string PackageVersion,string AuthorityScope);

public static class VoyageRules
{
    public const string Scope="r1_r2_actual_voyage_bridge_candidate";
    public static readonly VoyageRequest R2=new("R2","R2-S01","R2-S01.berth","public_landing_only","receiving_record_cross_check","unknown");
    public static string Hash(string text)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static string Hash<T>(T value)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(StrictJson.Serialize(value))));
    public static void Validate(VoyagePackage p)
    {
        if(string.IsNullOrWhiteSpace(p.Version)||p.Scope!=Scope||p.ReviewStatus!="original_unreviewed_human_pending"||p.From!="R1"||p.To!="R2"||p.ChartScene!="HUB-S02"||p.ChartSpot!="HUB-S02.chart") throw new InvalidDataException("This candidate opens only the bounded first R1/R2 loan voyage.");
        var d=p.DestinationDuty;
        if(d.Id!="R2-LOCAL-BERTH-DUTY"||d.Region!=p.To||d.Scene!=R2.LandingScene||d.BerthSpot!=R2.LandingSpot||d.Scope!=R2.AdmissionScope||d.IssuanceBasis!="Q104_loan_and_cross_region_commission"||d.ReviewStatus!=p.ReviewStatus) throw new InvalidDataException("The local duty can issue only public-berth handoff for this commissioned ship.");
        if(p.Sources.IsDefaultOrEmpty||p.Sources.Any(s=>string.IsNullOrWhiteSpace(s.Section)||s.Sha256.Length!=64)||p.OriginalLocalDetails.IsDefaultOrEmpty||p.Remaining.IsDefaultOrEmpty) throw new InvalidDataException("Separate immutable design sources, new local duty details and remaining human work.");
    }
    public static void Request(VoyageRequest request)
    {
        if(request!=R2) throw new InvalidOperationException("本委托只支持R2公开接驳泊位与接收记录核查；不授予档案、私人身份或其他地区访问权，也不证明导师当前去向。");
    }
    public static Spot SafeBerth(SceneGeometry geometry,string spotId)
    {
        var spot=geometry.Spots.SingleOrDefault(s=>s.Id==spotId)??throw new InvalidOperationException("当前源几何没有登记该靠泊工位。");
        var nav=new NavigationGrid(geometry);
        if(!nav.CanStand(spot.Stand,true)||geometry.Ports.IsDefaultOrEmpty||!geometry.Ports.Any(p=>nav.FindPath(spot.Stand,p.Stand,true).Length>0)) throw new InvalidOperationException("靠泊点没有实际安全站位及公开接驳出口路径。");
        return spot;
    }
}

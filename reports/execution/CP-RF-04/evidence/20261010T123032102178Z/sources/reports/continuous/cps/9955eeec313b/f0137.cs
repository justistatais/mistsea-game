using System.Collections.Immutable;
using System.Text.Json;
using Mist.Application.Contracts;
using Mist.Application.Factions;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Application.WorkOrders;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Engineering;
using Mist.Core.Quests;
using Mist.Core.ReleaseReadiness;

namespace Mist.Application.ReleaseReadiness;

internal sealed class WorldPreparationProjection(RuntimeCatalog catalog)
{
    private readonly EngineeringRules _engineering=new(catalog);
    internal const string Protocol="protocol-d3-v1";
    internal static StoredEntity? Entity(BranchSnapshot s,string id)=>s.Entities.SingleOrDefault(e=>e.Id==id);
    internal static T? Read<T>(BranchSnapshot s,string id) where T:class=>Entity(s,id) is{} e?StrictJson.Deserialize<T>(e.Json):null;
    internal static StoredReceipt? Receipt(BranchSnapshot s,string id,string action)=>s.Receipts.SingleOrDefault(r=>r.ReceiptId==id&&r.ActionType==action);
    internal static QuestState? Quest(BranchSnapshot s,string id)=>Read<QuestState>(s,"quest:"+id);
    internal static bool Closed(BranchSnapshot s,string id)
    {
        var q=Quest(s,id);if(q is null||q.Id!=id||q.Instance==Guid.Empty||q.Lifecycle!=QuestLifecycle.Closed||q.Outcome is not(QuestOutcome.Completed or QuestOutcome.CompletedWithConsequence))return false;
        var receipt=s.Receipts.SingleOrDefault(r=>r.SemanticKey==id+":resolution");if(receipt is null)return false;
        if(receipt.ActionType=="main_quest_resolution")
        {
            var actual=StrictJson.Deserialize<MainQuestResolution>(receipt.ResultJson);return actual.Quest==id&&actual.Instance==q.Instance&&actual.Outcome==(q.Outcome==QuestOutcome.Completed?"completed":"completed_with_consequence");
        }
        if(q.Outcome!=QuestOutcome.Completed)return false;
        if(id=="Q-104"&&receipt.ActionType=="tidegate_vessel_handover")
        {
            var actual=StrictJson.Deserialize<Mist.Core.ChapterOne.TidegateHandover>(receipt.ResultJson);var ship=Read<SharedVessel>(s,"ship:glimmer");
            return ship is not null&&actual.VesselInstance==ship.Instance.ToString()&&actual.VesselDefinition=="GLIMMER"&&actual.WatchOfficer==ship.WatchOfficer&&actual.WatchOfficer=="N-DECKWATCH"&&actual.Spaces.SequenceEqual(ship.Spaces)&&actual.Destination=="R2"&&actual.EvidenceFinding=="E-01"&&s.Entities.Count(e=>e.Kind=="shared_vessel")==1;
        }
        if(id=="Q-404"&&receipt.ActionType=="glasshaven_technical_handover_received")
        {
            var handover=Entity(s,"R4:actual_handover");if(handover?.Json!=receipt.ResultJson)return false;
            var actual=StrictJson.Deserialize<Mist.Core.ChapterFour.TechnicalHandover>(receipt.ResultJson);
            return actual.Destination=="R5"&&actual.Scope=="bounded_validated_alternative"&&actual.Evidence=="E-06/E-07/E-11"&&actual.Location=="old_city_north_station"&&actual.MaraCurrentStatus=="unknown"&&actual.PolicyDecision=="not_selected"&&actual.PublicPreparation.SequenceEqual(new[]{"verified_scheme","six_tower_changes","transition_supply","common_protocol","field_trials"});
        }
        if(id=="Q-604"&&receipt.ActionType=="network_finale_departure")
        {
            var entity=Entity(s,"final:actual-departure");if(entity is not{Kind:"finale_departure"}||entity.Json!=receipt.ResultJson)return false;
            var departure=StrictJson.Deserialize<Mist.Core.ChapterSix.FinaleDeparture>(receipt.ResultJson);
            var frozen=Read<FrozenPreparation>(s,"final:preparation");var ship=Vessel(s);
            if(frozen is null||ship is null||departure.VesselInstance!=ship.Instance||departure.Q604Instance!=q.Instance||departure.PreparationId!=frozen.Id||departure.PreFinalCheckpoint!=frozen.PreFinalCheckpoint||frozen.VesselInstance!=ship.Instance||frozen.Q604Instance!=q.Instance||departure.SourceRegion!="R6"||departure.Destination!="RF"||departure.SourceBerthScene!="R6-S01"||departure.DestinationScene!="RF-S01"||departure.Landing!=Mist.Core.Finale.FinalePackage.LandingStand||!System.Text.RegularExpressions.Regex.IsMatch(departure.DestinationGeometrySha256,"^[0-9a-f]{64}$"))return false;
            var sealedReceipt=s.Receipts.SingleOrDefault(r=>r.SemanticKey=="Q-604:preparation-sealed"&&r.ActionType=="seal_final_preparation");
            var berthReceipt=Receipt(s,departure.SourceBerthReceipt,"vessel_berth_verified");var riskReceipt=Receipt(s,departure.RiskRegisterReceipt,"high_risk_register_verified");
            if(sealedReceipt is null||berthReceipt is null||riskReceipt is null||StrictJson.Serialize(StrictJson.Deserialize<FrozenPreparation>(sealedReceipt.ResultJson))!=StrictJson.Serialize(frozen)||departure.SourceReceipts.IsDefaultOrEmpty||departure.SourceReceipts.Distinct(StringComparer.Ordinal).Count()!=departure.SourceReceipts.Length||departure.SourceReceipts.Any(r=>!s.Receipts.Any(source=>source.ReceiptId==r)))return false;
            var berth=StrictJson.Deserialize<VesselBerth>(berthReceipt.ResultJson);
            if(berth.Instance!=ship.Instance||berth.Region!="R6"||berth.Scene!=departure.SourceBerthScene||berth.Boarding!=departure.SourceBoarding)return false;
            string[] required=["Q-104","Q-404","Q-602","Q-603"];
            return required.All(quest=>Closed(s,quest)&&s.Receipts.Any(r=>r.SemanticKey==quest+":resolution"&&departure.SourceReceipts.Contains(r.ReceiptId)))&&departure.SourceReceipts.Contains(sealedReceipt.ReceiptId)&&departure.SourceReceipts.Contains(berthReceipt.ReceiptId)&&departure.SourceReceipts.Contains(riskReceipt.ReceiptId);
        }
        return false;
    }
    internal static bool Technical(BranchSnapshot s,out string? version)
    {
        var p=Read<TechnicalPackage>(s,"technical-package");version=p?.Version;if(p is not{Verified:true}||string.IsNullOrWhiteSpace(p.Version)||Receipt(s,p.SourceReceipt,"technical_package_verified") is not{} source)return false;using var d=JsonDocument.Parse(source.ResultJson);return d.RootElement.GetProperty("version").GetString()==p.Version;
    }
    internal static TowerWork? Work(BranchSnapshot s,string tower,WorkType type)
    {
        string key=WorkOrderService.Preparation+":"+tower+":"+type;var work=Read<TowerWork>(s,"work:"+key);if(work is null||work.Preparation!=WorkOrderService.Preparation||work.Tower!=tower||work.Type!=type||work.Stage!=WorkStage.Verified||work.Step!=4)return null;
        foreach(var r in s.Receipts.Where(r=>r.ActionType=="engineering_work_result")){using var d=JsonDocument.Parse(r.ResultJson);var j=d.RootElement;if(j.GetProperty("work").GetString()==key&&j.GetProperty("stage").GetString()=="verified"&&j.GetProperty("configuration").GetString()==work.Configuration&&j.GetProperty("operators").EnumerateArray().Select(p=>p.GetString()!).SequenceEqual(work.Operators))return work;}return null;
    }
    private static bool Operators(BranchSnapshot s,string tower,TowerWork? baseline)
    {
        if(baseline is null||baseline.Operators.Length!=2||baseline.Operators.Distinct(StringComparer.Ordinal).Count()!=2||baseline.Operators.Any(a=>a.StartsWith("AUX-",StringComparison.Ordinal)))return false;
        var team=Read<QualifiedWorkTeam>(s,"engineering-team:"+baseline.Crew);if(team is null||team.Region!=tower[4..]||!team.InstallationQualified||!team.OperatorsQualified||!team.Operators.SequenceEqual(baseline.Operators)||Receipt(s,team.AuthorityReceipt,"engineering_team_verified") is not{} authority)return false;
        var actual=StrictJson.Deserialize<WorkTeamAuthority>(authority.ResultJson);if(actual.Id!=team.Id||actual.Region!=team.Region||!actual.InstallationQualified||!actual.OperatorsQualified||!actual.Operators.SequenceEqual(team.Operators))return false;
        foreach(string actor in baseline.Operators)
        {
            var duty=Entity(s,"duty:"+actor);if(duty is not{Kind:"operator_duty"})return false;using var d=JsonDocument.Parse(duty.Json);var j=d.RootElement;string? source=j.GetProperty("source_work").GetString();
            if(j.GetProperty("tower").GetString()!=tower||j.GetProperty("actor").GetString()!=actor)return false;
            var assignment=Work(s,tower,WorkType.OperatorAssignment);if(source!=WorkOrderService.Preparation+":"+tower+":"+WorkType.BaselineCheck&&(assignment is null||source!=WorkOrderService.Preparation+":"+tower+":"+WorkType.OperatorAssignment))return false;
        }return true;
    }
    private bool Installed(BranchSnapshot s,string tower,WorkType type,TowerWork? work)
    {
        if(work is null)return false;var expected=_engineering.Required(tower,type);return work.Resources.Count==expected.Count&&expected.All(r=>work.Resources.GetValueOrDefault(r.Key)==r.Value&&s.Balances.GetValueOrDefault(EngineeringRules.Installed(tower,r.Key))>=r.Value);
    }
    private static bool Agreement(BranchSnapshot s,string region)
    {
        var a=Read<RegionalAgreement>(s,"agreement:"+region);if(a is null||a.Region!=region||a.ProtocolVersion!=Protocol||a.Operator is null||a.Representative is null||a.Operator.AppointmentInstance==a.Representative.AppointmentInstance)return false;
        foreach(var signature in new[]{a.Operator!,a.Representative!})
        {
            var p=Read<AppointmentHistory>(s,"mandate:"+region+":"+signature.Role)?.Current;if(p is null||p.Region!=region||p.ProtocolVersion!=Protocol||p.Role!=signature.Role||p.Instance!=signature.AppointmentInstance||p.Holder!=signature.Holder||signature.Region!=region||signature.ProtocolVersion!=Protocol)return false;
            if(Receipt(s,p.SourceReceipt,"regional_appointment_verified") is not{} pr||Receipt(s,signature.SourceReceipt,"regional_signature_verified") is not{} sr)return false;
            if((StrictJson.Deserialize<RegionalAppointment>(pr.ResultJson) with{SourceReceipt=p.SourceReceipt})!=p||(StrictJson.Deserialize<AgreementSignature>(sr.ResultJson) with{SourceReceipt=signature.SourceReceipt})!=signature)return false;
        }return a.Operator.Role==MandateRole.Operator&&a.Representative.Role==MandateRole.Representative;
    }
    internal WorldPreparation Project(BranchSnapshot s)
    {
        bool technical=Technical(s,out string? version);var towers=ImmutableArray.CreateBuilder<PreparedTower>();
        foreach(string tower in EndingRules.Towers)
        {
            var ready=Read<TowerReadiness>(s,"readiness:"+tower)??new(tower,false,false,null,false,false,false,null);bool safety=false;
            foreach(var r in s.Receipts.Where(r=>r.ActionType=="register_actual_tower_safety")){using var d=JsonDocument.Parse(r.ResultJson);var source=Receipt(s,d.RootElement.GetProperty("source_receipt").GetString()!,"tower_safety_verified");if(source is null)continue;var record=StrictJson.Deserialize<TowerSafety>(source.ResultJson);if(record.Tower==tower&&record.Safe)safety=true;}
            var baseline=Work(s,tower,WorkType.BaselineCheck);var enhanced=Work(s,tower,WorkType.EnhancedInstall);var transition=Work(s,tower,WorkType.TransitionSupply);var trial=Work(s,tower,WorkType.FieldTrial);
            bool installed=ready.EnhancedVersion==version&&enhanced?.Configuration==version&&Installed(s,tower,WorkType.EnhancedInstall,enhanced);
            bool tested=ready.Trial&&ready.TrialVersion==ready.EnhancedVersion&&trial is not null&&trial.Configuration==ready.EnhancedVersion&&trial.Resources.GetValueOrDefault("trial_fixture")==1;
            towers.Add(new(tower,ready.Tower==tower&&ready.Safe&&safety,ready.Baseline&&Installed(s,tower,WorkType.BaselineCheck,baseline),ready.Operators&&Operators(s,tower,baseline),installed?ready.EnhancedVersion:null,ready.Transition&&Installed(s,tower,WorkType.TransitionSupply,transition),tested?ready.TrialVersion:null,Agreement(s,tower[4..]),Protocol,s.Receipts.Where(r=>r.ActionType is "engineering_work_result" or "register_actual_tower_safety" or "accept_regional_signature").Select(r=>r.ReceiptId).ToImmutableArray()));
        }return new(WorkOrderService.Preparation,version,technical,Protocol,towers.ToImmutable());
    }
    internal static SharedVessel? Vessel(BranchSnapshot s)
    {
        var ship=Read<SharedVessel>(s,"ship:glimmer");var q104=Quest(s,"Q-104");if(ship is null||ship.Instance==Guid.Empty||ship.WatchOfficer!="N-DECKWATCH"||!ship.Spaces.SequenceEqual(new[]{"HUB-S01","HUB-S02","HUB-S03"})||!Closed(s,"Q-104")||q104 is null)return null;
        var actual=s.Receipts.SingleOrDefault(r=>r.SemanticKey=="Q-104:resolution"&&r.ActionType=="tidegate_vessel_handover");
        if(actual is not null)
        {
            var transfer=StrictJson.Deserialize<Mist.Core.ChapterOne.TidegateHandover>(actual.ResultJson);
            if(transfer.VesselInstance==ship.Instance.ToString()&&transfer.VesselDefinition=="GLIMMER"&&transfer.WatchOfficer==ship.WatchOfficer&&transfer.Spaces.SequenceEqual(ship.Spaces)&&s.Entities.Count(e=>e.Kind=="shared_vessel")==1)return ship;
        }
        foreach(var receipt in s.Receipts.Where(r=>r.ActionType=="vessel_use_transfer_verified")){var transfer=StrictJson.Deserialize<GlimmerUseTransfer>(receipt.ResultJson);if(transfer.VesselInstance==ship.Instance&&transfer.Q104Instance==q104.Instance&&transfer.Owner=="player"&&transfer.WatchOfficer==ship.WatchOfficer)return ship;}return null;
    }
    internal static bool CentralSafe(BranchSnapshot s)
    {
        var state=Read<CentralSafetyState>(s,"final:central-safety");if(state is null||state.Instance==Guid.Empty||state.Configuration<0||!state.Stable||!state.AutomaticRestartDisabled||!state.CommandChainReleased||!Closed(s,"Q-F03")||Receipt(s,state.SourceReceipt,"central_stability_verified") is not{} receipt)return false;
        var actual=StrictJson.Deserialize<CentralStabilityResult>(receipt.ResultJson);return actual==new CentralStabilityResult(state.Instance,state.Configuration,true,true,true);
    }
}

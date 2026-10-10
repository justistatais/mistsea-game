using System.Collections.Immutable;

namespace Mist.Core.Voyage;

public sealed record RegionalVoyageDefinition(string Version,string From,string To,string BerthSpot,string SourceQuest,
    string SourceAction,string CommissionPurpose,string Duty,string AuthorityScope,string ReviewStatus,
    ImmutableArray<VoyageSource> Sources,ImmutableArray<string> OriginalLocalDetails);
public sealed record RegionalVoyageBinding(Guid VesselInstance,string From,string To,string SourceQuestReceipt,
    string SourceCommissionJson,string SourceHandoverSha256,string SourceGeometrySha256,string DestinationGeometrySha256,
    string ChartGeometrySha256,string DefinitionSha256,string Duty,string AuthorityScope,string CheckedScene,
    Mist.Core.Exploration.Point2 CheckedPosition,long CheckedActionClock,string ReviewStatus);

public static class RegionalVoyageRules
{
    public static void Validate(RegionalVoyageDefinition d)
    {
        bool known=(d.Version,d.From,d.To,d.BerthSpot,d.SourceQuest,d.SourceAction,d.CommissionPurpose,d.Duty) is
            ("r2-r3-voyage-candidate-1","R2","R3","R3-S01.berth","Q-204","chapter_two_archive_handover_received","verify_MC-2_original_instruments_and_acceptance","R3-LOCAL-BERTH-DUTY") or
            ("r3-r4-voyage-candidate-1","R3","R4","R4-S01.berth","Q-304","chapter_three_tower_handover_received","verify_materials_and_signatory_scope","R4-LOCAL-BERTH-DUTY") or
            ("r4-r5-voyage-candidate-1","R4","R5","R5-S01.berth","Q-404","glasshaven_technical_handover_received","verify_north_station_and_legal_access","R5-LOCAL-BERTH-DUTY") or
            ("r5-r6-voyage-candidate-1","R5","R6","R6-S01.berth","Q-504","old_city_joint_handover_received","verify_current_network_risk_and_preparation","R6-LOCAL-BERTH-DUTY");
        if(!known||d.AuthorityScope!="public_landing_only"||d.ReviewStatus!="original_unreviewed_human_pending")
            throw new InvalidDataException("Only the exact source-bound public-landing voyage contracts are implemented.");
        if(d.Sources.IsDefaultOrEmpty||d.OriginalLocalDetails.IsDefaultOrEmpty||d.Sources.Any(s=>s.Sha256.Length!=64||string.IsNullOrWhiteSpace(s.Section)))
            throw new InvalidDataException("Original design sources and new bounded functional-duty details must remain separate.");
    }
}

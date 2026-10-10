using System.Collections.Immutable;
using Mist.Core.ReviewCalibration;

namespace Mist.Application.ReviewCalibration;

public sealed record ObservationMetadataRow(string Observation,string Goal,string Modality,string Material,string MaterialVersion,string MaterialHash,string Family,string Context,string Session,string ResponseHash,DateTimeOffset SubmittedAt,string Condition,string ResultSource,long EvaluationRevision);
public sealed record ObservationMetadataExport(long PrivacyEpoch,ImmutableArray<ObservationMetadataRow> Rows)
{public bool ContainsResponseBodies=>false;public bool ContainsAuthorAnswers=>false;public decimal? Band=>null;public bool OfficialCertification=>false;}
public static class ReadOnlyObservationExport
{
    public static ObservationMetadataExport Create(IEnumerable<ReviewObservation> records,long currentPrivacyEpoch)
    {var actual=ReviewRules.Normalize(records,currentPrivacyEpoch);return new(currentPrivacyEpoch,actual.Select(o=>new ObservationMetadataRow(o.Id,o.Goal,o.Modality.ToString(),o.Material,o.MaterialVersion,o.MaterialHash,o.FamilyRoot,o.Context,o.Session,o.ResponseHash,o.SubmittedAt,ReviewRules.Condition(o),o.Authority.ToString(),o.EvaluationRevision)).ToImmutableArray());}
}

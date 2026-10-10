using System.Collections.Immutable;
using Mist.Core.Contracts;
using Mist.Core.ReviewCalibration;

namespace Mist.Application.ReviewCalibration;

public sealed record ReviewAuthorityScope(ProfileId Profile,BranchId Branch,LoadContext Load,long PrivacyEpoch,long PermissionVersion,string Release);
public sealed record ReviewProjection(ReviewAuthorityScope Scope,ReviewSelectionContext Context,ImmutableArray<PracticeIndexEntry> Library,ImmutableArray<ReviewObservation> Observations,ImmutableArray<ReviewSchedule> Schedules,bool SourcesBound);
public interface IReadOnlyReviewProjection
{ValueTask<ReviewProjection> ReadAsync(ReviewAuthorityScope expected,CancellationToken cancellationToken=default);}
public sealed record ReviewReport(string Status,ImmutableArray<ReviewRecommendation> Recommendations,ImmutableArray<string> Missing)
{public bool WorldWriteCapability=>false;public int ExperienceReward=>0;public decimal? Band=>null;public bool AdventureBlocked=>false;public bool AttributeDecay=>false;}
// This receives only an authorised, privacy-scoped projection. It has no repository, event
// scheduler, provider, world command, reward command, draft body or network capability.
public sealed class ReadOnlyReviewService(IReadOnlyReviewProjection? projection=null)
{
    public async ValueTask<ReviewReport> RecommendAsync(ReviewAuthorityScope expected,int maximum=3,CancellationToken cancellationToken=default)
    {
        if(projection is null)return new("review_sources_unbound",[],["verified_offline_practice_index_and_profile_projection"]);
        var state=await projection.ReadAsync(expected,cancellationToken);if(state.Scope!=expected)return new("stale_or_deleted_profile_scope",[],[]);if(!state.SourcesBound)return new("review_sources_unbound",[],["verified_offline_practice_index_and_profile_projection"]);
        if(state.Context.Release!=expected.Release||state.Context.PrivacyEpoch!=expected.PrivacyEpoch)throw new InvalidDataException("review_projection_scope_mismatch");
        var recommendations=ReviewRules.Select(state.Library,state.Observations,state.Schedules,state.Context,maximum);var current=await projection.ReadAsync(expected,cancellationToken);if(current.Scope!=expected||current.Context.Release!=expected.Release||current.Context.PrivacyEpoch!=expected.PrivacyEpoch)return new("stale_or_deleted_profile_scope",[],[]);
        if(!current.SourcesBound)return new("review_sources_unbound",[],["verified_offline_practice_index_and_profile_projection"]);
        var currentRecommendations=ReviewRules.Select(current.Library,current.Observations,current.Schedules,current.Context,maximum);
        bool sameSource=recommendations.All(r=>state.Library.Any(a=>a.Id==r.Id&&a.Version==r.Version&&current.Library.Any(b=>b.Id==a.Id&&b.Version==a.Version&&b.BodyHash==a.BodyHash)));
        if(!sameSource||StrictJson.Serialize(recommendations)!=StrictJson.Serialize(currentRecommendations))return new("review_authorization_or_source_changed",[],[]);
        return new(recommendations.Length==0?"no_compatible_verified_practice":"advisory_only",recommendations,[]);
    }
}
public interface ITrustedCalibrationProjection
{ValueTask<ImmutableArray<CalibrationRecord>> ReadVerifiedAsync(string bindingHash,CancellationToken cancellationToken=default);}
public sealed class NoTrustedCalibrationRecords:ITrustedCalibrationProjection
{public ValueTask<ImmutableArray<CalibrationRecord>> ReadVerifiedAsync(string bindingHash,CancellationToken cancellationToken=default){cancellationToken.ThrowIfCancellationRequested();return ValueTask.FromResult(ImmutableArray<CalibrationRecord>.Empty);}}
public sealed class ReadOnlyCalibrationService(ITrustedCalibrationProjection? projection=null)
{
    private readonly ITrustedCalibrationProjection _projection=projection??new NoTrustedCalibrationRecords();
    public async ValueTask<CalibrationInspection> InspectAsync(CalibrationProtocol protocol,CancellationToken cancellationToken=default)
    {string hash=CalibrationRules.BindingHash(protocol.Binding);return CalibrationRules.Inspect(protocol,await _projection.ReadVerifiedAsync(hash,cancellationToken));}
}

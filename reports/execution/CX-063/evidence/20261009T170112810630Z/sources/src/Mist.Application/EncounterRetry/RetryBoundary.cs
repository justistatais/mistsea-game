using System.Collections.Immutable;

namespace Mist.Application.EncounterRetry;

public sealed record EncounterEntry(string InstanceId, int Attempt, ImmutableDictionary<string, int> PartyHp,
    ImmutableDictionary<string, int> Consumables, ImmutableDictionary<string, string> EncounterLocal,
    ImmutableHashSet<string> SuccessfulRewards);
public sealed record RetryDifference(EncounterEntry Restored, ImmutableArray<string> Changes);
public static class RetryBoundary
{
    public static RetryDifference Restore(EncounterEntry entry, EncounterEntry failed)
    {
        if (entry.InstanceId != failed.InstanceId) throw new InvalidOperationException("Retry cannot cross encounter instances.");
        if (!failed.SuccessfulRewards.SetEquals(entry.SuccessfulRewards)) throw new InvalidOperationException("Successful settlement cannot be retried with restored consumables.");
        return new(entry with { Attempt = checked(failed.Attempt + 1) }, ["party_hp", "encounter_consumables", "encounter_local", "attempt_number"]);
    }
    // World inventory, delivered transportation, drafts, and learning do not occur in this boundary.
}

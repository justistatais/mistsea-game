using System.Collections.Immutable;
using Mist.Application.Storage;
using Mist.Core.Contracts;

namespace Mist.Application.EncounterRetry;

public static class RetryPlanFactory
{
    public static ValidatedPlan Create(BranchSnapshot world, EncounterEntry entry, EncounterEntry failed)
    {
        var restored = RetryBoundary.Restore(entry, failed).Restored;
        string entityId = "encounter:" + entry.InstanceId;
        var entity = world.Entities.SingleOrDefault(e => e.Id == entityId) ?? throw new InvalidOperationException("Current encounter is missing.");
        var current = StrictJson.Deserialize<EncounterEntry>(entity.Json);
        if (current != failed && StrictJson.Serialize(current) != StrictJson.Serialize(failed)) throw new InvalidOperationException("Retry input does not match current encounter.");
        if (world.Receipts.Any(r => r.SemanticKey == "encounter:" + entry.InstanceId + ":settlement")) throw new InvalidOperationException("Settled encounter cannot restore consumed resources.");
        var refunds = entry.Consumables.Select(pair =>
        {
            if (!failed.Consumables.TryGetValue(pair.Key, out int remaining) || remaining < 0 || remaining > pair.Value) throw new InvalidOperationException("Invalid attempt consumption.");
            return new BalanceChange("inventory:" + pair.Key, pair.Value - remaining);
        }).ToImmutableArray();
        return new("encounter:" + entry.InstanceId + ":retry:" + restored.Attempt, "encounter_retry", [new(entityId, entity.Kind, entity.Revision, StrictJson.Serialize(restored))], [], refunds, "{\"retry\":true}");
    }
}

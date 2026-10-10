using System.Collections.Immutable;
using Mist.Application.Contracts;
using Mist.Core.Contracts;

namespace Mist.Application.Commands;

public enum PreviewStatus { Ready, NeedsPreparation, NeedsClarification, StateUnavailable }
public sealed record RelevantReadSet(LoadContext Load, ImmutableArray<EntityStamp> Stamps)
{
    public bool Matches(LoadContext current, ImmutableDictionary<string, long> revisions) => Load == current &&
        Stamps.All(stamp => revisions.TryGetValue(stamp.EntityId, out long revision) && revision == stamp.Revision);
}
public sealed record ActionPreview(string Id, PreviewStatus Status, RelevantReadSet ReadSet, ImmutableArray<RuleIssue> Issues, bool IndependentWritingEvidence);
public static class PreviewFactory
{
    public static ActionPreview Create(string actionId, LoadContext load, ImmutableDictionary<string, long> revisions,
        IEnumerable<string> relevantEntities, string draft, bool structuredFallback, ImmutableArray<RuleIssue> preparationIssues)
    {
        var required = relevantEntities.Distinct(StringComparer.Ordinal).ToArray();
        var missing = required.Where(id => !revisions.ContainsKey(id)).Select(id => new RuleIssue("state_unavailable", id, "状态无法读取，请重试或恢复数据。"));
        var issues = preparationIssues.AddRange(missing);
        var status = issues.Any(i => i.Code == "state_unavailable") ? PreviewStatus.StateUnavailable :
            issues.Length > 0 ? PreviewStatus.NeedsPreparation :
            string.IsNullOrWhiteSpace(draft) && !structuredFallback ? PreviewStatus.NeedsClarification : PreviewStatus.Ready;
        return new(actionId, status, new(load, required.Where(revisions.ContainsKey).Select(id => new EntityStamp(id, revisions[id])).ToImmutableArray()), issues,
            !string.IsNullOrWhiteSpace(draft) && !structuredFallback);
    }
}

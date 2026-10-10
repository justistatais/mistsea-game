using System.Collections.Immutable;

namespace Mist.Core.Conditions;

public enum TruthValue { True, False, InsufficientInformation }
public enum DataAvailability { Available, SystemStateUnknown, InvalidData }
public sealed record FactValue(DataAvailability Availability, string? Text = null, decimal? Number = null, bool? Flag = null);
public sealed record ConditionResult(TruthValue Truth, DataAvailability Availability, ImmutableArray<string> Issues)
{
    public bool Admissible => Availability == DataAvailability.Available && Truth == TruthValue.True;
}
public abstract record Condition;
public sealed record FactEquals(string Key, FactValue Expected) : Condition;
public sealed record AllConditions(ImmutableArray<Condition> Children) : Condition;
public sealed record AnyConditions(ImmutableArray<Condition> Children) : Condition;
public sealed record InformationUnspecified(string Key) : Condition;

public static class ConditionEvaluator
{
    public static ConditionResult Evaluate(Condition condition, ImmutableDictionary<string, FactValue> facts) => condition switch
    {
        FactEquals equal => !facts.TryGetValue(equal.Key, out var value)
            ? new(TruthValue.InsufficientInformation, DataAvailability.SystemStateUnknown, ["missing_state:" + equal.Key])
            : value.Availability != DataAvailability.Available
                ? new(TruthValue.InsufficientInformation, value.Availability, ["unavailable_state:" + equal.Key])
                : new(value == equal.Expected ? TruthValue.True : TruthValue.False, DataAvailability.Available, []),
        InformationUnspecified => new(TruthValue.InsufficientInformation, DataAvailability.Available, []),
        AllConditions all => Combine(all.Children, facts, requireAll: true),
        AnyConditions any => Combine(any.Children, facts, requireAll: false),
        _ => new(TruthValue.InsufficientInformation, DataAvailability.InvalidData, ["unsupported_condition"])
    };
    private static ConditionResult Combine(ImmutableArray<Condition> children, ImmutableDictionary<string, FactValue> facts, bool requireAll)
    {
        if (children.IsDefaultOrEmpty) return new(TruthValue.InsufficientInformation, DataAvailability.InvalidData, ["empty_condition_group"]);
        var results = children.Select(c => Evaluate(c, facts)).ToArray();
        if (results.Any(r => r.Availability != DataAvailability.Available))
            return new(TruthValue.InsufficientInformation, DataAvailability.SystemStateUnknown, results.SelectMany(r => r.Issues).ToImmutableArray());
        var truth = requireAll
            ? results.Any(r => r.Truth == TruthValue.False) ? TruthValue.False : results.All(r => r.Truth == TruthValue.True) ? TruthValue.True : TruthValue.InsufficientInformation
            : results.Any(r => r.Truth == TruthValue.True) ? TruthValue.True : results.All(r => r.Truth == TruthValue.False) ? TruthValue.False : TruthValue.InsufficientInformation;
        return new(truth, DataAvailability.Available, []);
    }
}

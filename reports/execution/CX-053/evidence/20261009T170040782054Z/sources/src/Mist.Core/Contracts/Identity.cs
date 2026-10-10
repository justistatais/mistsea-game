using System.Text.RegularExpressions;

namespace Mist.Core.Contracts;

public static class IdRules
{
    public static Guid Guid(Guid value) => value == System.Guid.Empty ? throw new ArgumentException("Identity cannot be empty.") : value;
    public static string Name(string value)
    {
        if (value is null || !Regex.IsMatch(value, "^[A-Z][A-Z0-9_.-]{1,63}$", RegexOptions.CultureInvariant)) throw new ArgumentException("Invalid stable definition identity.");
        return value;
    }
}

public readonly record struct ProfileId { public Guid Value { get; } [System.Text.Json.Serialization.JsonConstructor] public ProfileId(Guid value) => Value = IdRules.Guid(value); }
public readonly record struct CampaignId { public Guid Value { get; } [System.Text.Json.Serialization.JsonConstructor] public CampaignId(Guid value) => Value = IdRules.Guid(value); }
public readonly record struct BranchId { public Guid Value { get; } [System.Text.Json.Serialization.JsonConstructor] public BranchId(Guid value) => Value = IdRules.Guid(value); }
public readonly record struct InstanceId { public Guid Value { get; } [System.Text.Json.Serialization.JsonConstructor] public InstanceId(Guid value) => Value = IdRules.Guid(value); }
public readonly record struct AttemptId { public Guid Value { get; } [System.Text.Json.Serialization.JsonConstructor] public AttemptId(Guid value) => Value = IdRules.Guid(value); }
public readonly record struct CommandId { public Guid Value { get; } [System.Text.Json.Serialization.JsonConstructor] public CommandId(Guid value) => Value = IdRules.Guid(value); }
public readonly record struct ReceiptId { public Guid Value { get; } [System.Text.Json.Serialization.JsonConstructor] public ReceiptId(Guid value) => Value = IdRules.Guid(value); }
public readonly record struct DraftId { public Guid Value { get; } [System.Text.Json.Serialization.JsonConstructor] public DraftId(Guid value) => Value = IdRules.Guid(value); }
public readonly record struct LoadContext { public Guid Value { get; } [System.Text.Json.Serialization.JsonConstructor] public LoadContext(Guid value) => Value = IdRules.Guid(value); }
public readonly record struct DefinitionId { public string Value { get; } [System.Text.Json.Serialization.JsonConstructor] public DefinitionId(string value) => Value = IdRules.Name(value); }
public readonly record struct DefinitionVersion { public string Value { get; } [System.Text.Json.Serialization.JsonConstructor] public DefinitionVersion(string value) => Value = !string.IsNullOrWhiteSpace(value) && value.Length <= 64 ? value : throw new ArgumentException("Version required."); }
public readonly record struct PrivacyEpoch { public long Value { get; } [System.Text.Json.Serialization.JsonConstructor] public PrivacyEpoch(long value) => Value = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value)); }
public readonly record struct SemanticSlot
{
    public string Value { get; }
    [System.Text.Json.Serialization.JsonConstructor] public SemanticSlot(string value) => Value = !string.IsNullOrWhiteSpace(value) && value.Length <= 160 ? value : throw new ArgumentException("Settlement slot required.");
}

public sealed record DomainClock(long ActionSequence, long CombatSlotSequence)
{
    public DomainClock AdvanceAction() => this with { ActionSequence = checked(ActionSequence + 1) };
    public DomainClock AdvanceCombatSlot() => this with { CombatSlotSequence = checked(CombatSlotSequence + 1) };
}
public sealed record WallTimeObservation(DateTimeOffset ObservedAt, string Purpose);
public sealed record LanguageTarget(decimal Reading = 6.5m, decimal Listening = 6.5m, decimal Writing = 6.5m, decimal? Speaking = null, bool AutoRaise = false)
{
    public void Validate()
    {
        if (Reading != 6.5m || Listening != 6.5m || Writing != 6.5m || Speaking is not null || AutoRaise) throw new InvalidDataException("Language target must remain R/L/W 6.5, speaking null, auto_raise false.");
    }
}

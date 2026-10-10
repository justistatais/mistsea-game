using System.Collections.Immutable;
using System.Text.Json;
using Mist.Core.Contracts;
using Mist.Application;
using Mist.Application.Assistance;
using Mist.Application.Contracts;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.Contracts;

public static class ContractCases
{
    public static void Register()
    {
        Add("CX-006", "Core assembly has no Godot, SQLite or HTTP dependency", () =>
        {
            var references = typeof(LanguageTarget).Assembly.GetReferencedAssemblies().Select(a => a.Name!);
            Require(!references.Any(n => n.Contains("Godot") || n.Contains("Sqlite") || n.Contains("Http")));
        });
        Add("CX-006", "Assistance capability cannot execute world commands", () =>
        {
            Require(!typeof(IWorldCommands).IsAssignableFrom(typeof(DisabledProvider)));
            Require(typeof(IAssistanceProvider).GetMethods().All(m => m.Name == "RequestAsync"));
            var request = new AssistanceRequest("case", new LoadContext(Guid.NewGuid()), null, null, 0, 0, "explanation", ImmutableArray<VisibleSegment>.Empty, null);
            var result = new DisabledProvider().RequestAsync(request).GetAwaiter().GetResult();
            Require(result.Status == "provider_disabled" && result.CandidateJson is null && !BuildIdentity.OnlineProviderEnabled);
        });
        Add("CX-008", "Duplicate JSON fields are rejected recursively", () => Throws<JsonException>(() => StrictJson.Deserialize<LanguageTarget>("{\"reading\":6.5,\"reading\":7}")));
        Add("CX-008", "Unknown enum and integer enum are rejected", () =>
        {
            Throws<JsonException>(() => StrictJson.Deserialize<CommitStatus>("\"invented\""));
            Throws<JsonException>(() => StrictJson.Deserialize<CommitStatus>("1"));
        });
        Add("CX-008", "Typed identity serialization preserves its exact GUID", () =>
        {
            var original = new ProfileId(Guid.NewGuid());
            Require(StrictJson.Deserialize<ProfileId>(StrictJson.Serialize(original)) == original);
            Throws<ArgumentException>(() => new ProfileId(Guid.Empty));
            Throws<ArgumentException>(() => new DefinitionId("../../unsafe"));
        });
        Add("CX-008", "Unicode scalar ranges preserve emoji, combining marks and Chinese", () =>
        {
            string text = "船🚢e\u0301\nZ";
            Require(UnicodeOffsets.Slice(text, 1, 2) == "🚢");
            Require(UnicodeOffsets.Slice(text, 2, 4) == "e\u0301");
            Require(UnicodeOffsets.ToUtf16(text, 2) == 3);
            Throws<ArgumentOutOfRangeException>(() => UnicodeOffsets.ToUtf16(text, 20));
            Require(UnicodeOffsets.NormalizeNewlines("a\r\nb\rc") == "a\nb\nc");
        });
        Add("CX-008", "Wall time observations cannot advance action or combat clocks", () =>
        {
            var clock = new DomainClock(8, 3);
            var observation = new WallTimeObservation(DateTimeOffset.UnixEpoch.AddDays(900), "review");
            Require(clock.ActionSequence == 8 && clock.CombatSlotSequence == 3 && observation.Purpose == "review");
            Require(clock.AdvanceAction() == new DomainClock(9, 3));
        });
        Add("CX-008", "Language target cannot silently rise or introduce speaking scores", () =>
        {
            new LanguageTarget().Validate();
            Throws<InvalidDataException>(() => new LanguageTarget(Reading: 7).Validate());
            Throws<InvalidDataException>(() => new LanguageTarget(Speaking: 6.5m).Validate());
        });
    }
}

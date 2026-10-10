using System.Collections.Immutable;
using Mist.Core.Contracts;

namespace Mist.Core.Content;

public sealed record SourceProvenance(int Part, string Path, string Sha256, string Adapter);
public sealed record SceneDefinition(string Id, string Region, string Name, string Actions, ImmutableArray<string> StoryNodes);
public sealed record ConnectionDefinition(string A, string B, string Gate, string Return);
public sealed record RewardDefinition(string QuestId, string LegacyReference, RewardBundle Bundle, ImmutableArray<string> Items);
public sealed record QuestDefinition(string Id, string Title, ImmutableArray<string> ActivationAfter, ImmutableArray<string> CloseAfter,
    ImmutableArray<string> Scenes, string CloseContract, RewardDefinition Reward);
public sealed record ParagraphDefinition(string Id, string Text, string Sha256);
public sealed record MaterialDefinition(string Id, string Version, string Title, string Modality, decimal TargetBand, string DifficultyStatus,
    string FamilyId, string? QuestId, ImmutableArray<ParagraphDefinition> Paragraphs, string AudioAsset, string BodySha256);
public sealed record NamedDefinition(string Id, string Name, string Kind, string PayloadJson);
public sealed record RuntimeCatalog(int SchemaVersion, string ReleaseId, LanguageTarget Target, ImmutableArray<SourceProvenance> Sources,
    ImmutableArray<SceneDefinition> Scenes, ImmutableArray<ConnectionDefinition> Connections, ImmutableArray<QuestDefinition> Quests,
    ImmutableArray<MaterialDefinition> Materials, ImmutableDictionary<string, ImmutableArray<NamedDefinition>> Definitions,
    string Q102GeometryJson, string Q102InitialJson, string Q102FactsJson, string Q102WritingPrompt);
public sealed record ReleaseAsset(string Path, string Sha256, long Bytes, string Kind);
public sealed record AudioBinding(string MaterialId, string MaterialVersion, string TextSha256, string AudioPath, string AudioSha256);
public sealed record ReleaseManifest(int SchemaVersion, string ReleaseId, string RulesVersion, string EngineContract,
    LanguageTarget Target, string BuildKind, ImmutableArray<ReleaseAsset> Assets, ImmutableArray<string> CalibrationRecords,
    ImmutableArray<string> EditorialRecords, string PublisherKeyId, string? Signature, ImmutableArray<AudioBinding> AudioBindings);
public sealed class ContentException(string message) : Exception(message);

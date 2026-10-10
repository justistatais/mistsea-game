// Typed API contract draft. This turn did NOT compile or run this C# project.
// No Godot, SQL, provider API, or scene side effects belong in these contracts.
namespace Mist.Contracts;

public readonly record struct ProfileId(Guid Value);
public readonly record struct BranchId(Guid Value);
public readonly record struct CommandId(Guid Value);
public readonly record struct ReceiptId(Guid Value);
public readonly record struct DefinitionId(string Value);
public readonly record struct DraftId(Guid Value);
public readonly record struct LoadContext(Guid Value);

public sealed record EntityStamp(string EntityId, long Revision);
public sealed record CommandContext(
    ProfileId Profile, BranchId Branch, LoadContext Load,
    CommandId Command, string ReleaseId, IReadOnlyList<EntityStamp> Expected);

public interface IWorldCommand { CommandContext Context { get; } }
public sealed record DepartFerry(
    CommandContext Context, string VesselId, string QuestInstanceId,
    string ConfirmedIntentId, string PreviewId) : IWorldCommand;

public enum CommitStatus { Committed, AlreadyCommitted, Rejected, OutcomeUnknown }
public sealed record RuleIssue(string Code, string EntityId, string SafeMessageKey);
public sealed record WorldReceipt(
    ReceiptId Id, BranchId Branch, string SemanticSlot,
    string ActionType, long CommitSequence);
public sealed record CommandResult(
    CommitStatus Status, WorldReceipt? Receipt, IReadOnlyList<RuleIssue> Issues);

public interface IWorldCommands
{
    // Never route AI response JSON directly into this method.
    ValueTask<CommandResult> ExecuteAsync(IWorldCommand command, CancellationToken cancellationToken);
    // A timeout must query this receipt, not invent a new command ID.
    ValueTask<WorldReceipt?> FindReceiptAsync(BranchId branch, CommandId command, CancellationToken cancellationToken);
}

public sealed record SaveDraft(
    ProfileId Profile, BranchId Branch, LoadContext Load,
    string TaskInstanceId, DraftId? Draft, long ExpectedHeadRevision,
    string Text, string MaterialVersion, string SessionEditId);
public sealed record DraftSaved(DraftId Draft, long Revision, string Utf8Sha256);
public interface IDraftStore
{
    ValueTask<DraftSaved> SaveAsync(SaveDraft command, CancellationToken cancellationToken);
}

// Domain state, resources, and state machine data must be copied, never Node references.
public interface IStableSnapshot { int SchemaVersion { get; } string RulesVersion { get; } }
public interface ITransition<TState, in TAction> where TState : IStableSnapshot
{
    // Pure: no disk, network, DateTime.Now, frame delta, random global state or UI.
    TState Apply(TState state, TAction action);
}

public sealed record VisibilityScope(
    ProfileId Profile, BranchId Branch, string ActorId, string Purpose,
    long PermissionRevision, long ConsentRevision, long PrivacyEpoch);
public sealed record VisibleSegment(
    string MaterialId, string Version, string ParagraphId,
    int StartScalarInclusive, int EndScalarExclusive, string Text, string TextHash);
public interface IVisibleContent
{
    IReadOnlyList<VisibleSegment> Select(VisibilityScope scope, IReadOnlyList<string> paragraphIds);
}

public sealed record AssistanceRequest(
    string RequestId, VisibilityScope Scope, LoadContext Load,
    DraftId? Draft, long? DraftRevision, string ServiceKind,
    IReadOnlyList<VisibleSegment> Segments, string? ExplicitDraftText);
public sealed record AssistanceCandidate(string RequestId, string CandidateJson);
public interface IAssistanceProvider
{
    // Output is still untrusted. This contract exposes no world mutation capability.
    ValueTask<AssistanceCandidate> RequestAsync(AssistanceRequest request, CancellationToken cancellationToken);
}

public interface IPauseToken : IDisposable { string Reason { get; } }
public interface IPauseService { IPauseToken Acquire(string reason); bool IsWorldPaused { get; } }

using System.Collections.Immutable;
using Mist.Core.Contracts;

namespace Mist.Application.Contracts;

public enum CommitStatus { Committed, AlreadyCommitted, Rejected, Busy, OutcomeUnknown }
public sealed record EntityStamp(string EntityId, long Revision);
public sealed record DraftStamp(string TaskId, DraftId? Draft, long Revision, string? Hash);
public sealed record CommandContext(ProfileId Profile, BranchId Branch, LoadContext Load, CommandId Command, string ReleaseId, ImmutableArray<EntityStamp> Expected, DraftStamp? ExpectedDraft = null);
public sealed record RuleIssue(string Code, string EntityId, string SafeMessage);
public sealed record WorldReceipt(ReceiptId Id, BranchId Branch, SemanticSlot Slot, string ActionType, long CommitSequence);
public sealed record CommandResult(CommitStatus Status, WorldReceipt? Receipt, ImmutableArray<RuleIssue> Issues);
public interface IWorldCommand { CommandContext Context { get; } }
public interface IWorldCommands
{
    ValueTask<CommandResult> ExecuteAsync(IWorldCommand command, CancellationToken cancellationToken = default);
    ValueTask<WorldReceipt?> FindReceiptAsync(BranchId branch, CommandId command, CancellationToken cancellationToken = default);
}
public sealed record SaveDraft(ProfileId Profile, BranchId Branch, LoadContext Load, string TaskInstanceId, DraftId? Draft, long ExpectedHeadRevision, string Text, string MaterialVersion);
public sealed record DraftSaved(DraftId Draft, long Revision, string Utf8Sha256);
public interface IDraftStore { ValueTask<DraftSaved> SaveAsync(SaveDraft command, CancellationToken cancellationToken = default); }
public interface IClock { DateTimeOffset UtcNow { get; } }
public sealed record VisibleSegment(string MaterialId, string Version, string ParagraphId, int StartScalarInclusive, int EndScalarExclusive, string Text, string TextHash);
public sealed record AssistanceRequest(string RequestId, LoadContext Load, DraftId? Draft, long? Revision, long PrivacyEpoch, long ConsentVersion, string ServiceKind, ImmutableArray<VisibleSegment> Segments, string? ExplicitDraftText);
public sealed record AssistanceResult(string RequestId, string Status, string? CandidateJson);
public interface IAssistanceProvider
{
    ValueTask<AssistanceResult> RequestAsync(AssistanceRequest request, CancellationToken cancellationToken = default);
}

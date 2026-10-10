using System.Collections.Immutable;
using Mist.Core.Contracts;

namespace Mist.Application.Storage;

public sealed record StoredEntity(string Id, string Kind, long Revision, string Json);
public sealed record StoredItem(string Id, string DefinitionId, string OwnerKind, string OwnerId, string? Slot);
public sealed record StoredReceipt(string SemanticKey, string ReceiptId, string ActionType, string EffectHash, string ResultJson, long Sequence);
public sealed record StoredCommand(string CommandId, string RequestHash, string ReceiptId);
public sealed record StoredOutbox(string EventId, string ReceiptId, string EventType, string Json, bool Displayed);
public sealed record StoredHead(string TaskId, string DraftId, long Revision);
public sealed record StoredPermission(string SubjectId, long Revision, string Json);
public sealed record BranchSnapshot(int SchemaVersion, string ReleaseId, long Sequence,
    ImmutableArray<StoredEntity> Entities, ImmutableArray<StoredItem> Items, ImmutableDictionary<string, long> Balances,
    ImmutableArray<StoredReceipt> Receipts, ImmutableArray<StoredCommand> Commands, ImmutableArray<StoredOutbox> Outbox,
    ImmutableArray<StoredHead> DraftHeads, ImmutableArray<StoredPermission> Permissions);
public sealed record SessionView(ProfileId Profile, BranchId Branch, LoadContext Load, string ReleaseId, long Sequence);
public sealed record EntityChange(string Id, string Kind, long ExpectedRevision, string Json);
public sealed record ItemChange(string Id, string DefinitionId, string ExpectedOwnerKind, string ExpectedOwnerId, string OwnerKind, string OwnerId, string? Slot);
public sealed record BalanceChange(string Account, long Delta);
public sealed record ValidatedPlan(string SemanticKey, string ActionType, ImmutableArray<EntityChange> Entities,
    ImmutableArray<ItemChange> Items, ImmutableArray<BalanceChange> Balances, string ResultJson);
public sealed record DraftView(DraftId Id, long Revision, string? Body, string Hash, bool Deleted);
public sealed class StoreConflictException(string message) : Exception(message);
public sealed class MissingControlException(string message) : Exception(message);
public sealed class StoreVersionException(string message) : Exception(message);
public sealed record CommitPlanCommand(Mist.Application.Contracts.CommandContext Context, ValidatedPlan Plan) : Mist.Application.Contracts.IWorldCommand;
public sealed record PrivacyControl(long Epoch, long ConsentVersion);
public sealed record BackupInfo(string Id, string Path, string Sha256, long Sequence);
public sealed record StorageDiagnostics(string SqliteVersion, string JournalMode, long Synchronous, long ForeignKeys, long BusyTimeout, long Schema, string WriterThread);

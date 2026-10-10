using Mist.Application.Contracts;

namespace Mist.Application.Storage;

public interface IAdventureRepository : IWorldCommands, IDraftStore
{
    SessionView Session { get; }
    ValueTask<BranchSnapshot> SnapshotAsync(CancellationToken cancellationToken = default);
    ValueTask<DraftView?> GetDraftAsync(string taskId);
    ValueTask EnsureInventorySchemaAsync();
}

using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.NPC;

namespace Mist.Application.Relationships;

public sealed class RelationshipService(IAdventureRepository repository,RuntimeCatalog catalog)
{
    private readonly KnowledgeProjection _people=new(catalog);
    public async ValueTask<RelationshipState> ViewAsync(string actor)=>BranchOperations.Read<RelationshipState>(await repository.SnapshotAsync(),"relationship:"+actor)??RelationshipState.Empty;
    // Script/QEV owners create verified relationship events. Neither prose nor scores can call this with a new event.
    public async ValueTask<CommandResult> ConsumeAsync(string sourceReceipt)
    {
        var s=await repository.SnapshotAsync();var receipt=BranchOperations.Evidence(s,sourceReceipt,"relationship_event_verified");var e=StrictJson.Deserialize<RelationshipEvent>(receipt.ResultJson);
        if(!_people.Actors.ContainsKey(e.Actor)||!s.Receipts.Any(r=>r.ReceiptId==e.SourceReceipt&&r.Sequence<receipt.Sequence))throw new InvalidOperationException("关系事件需要当前分支中的本人和先前已提交的实际来源。");
        var state=BranchOperations.Read<RelationshipState>(s,"relationship:"+e.Actor)??RelationshipState.Empty;var next=state.Apply(e);
        return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,"relationship:event:"+e.Id,"consume_relationship_event",[BranchOperations.Change(s,"relationship:"+e.Actor,"relationship",next)],["relationship:"+e.Actor],new{source_receipt=sourceReceipt,event_id=e.Id}));
    }
}

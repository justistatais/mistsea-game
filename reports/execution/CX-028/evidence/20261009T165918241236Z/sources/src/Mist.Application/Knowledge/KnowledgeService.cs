using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.Content;
using Mist.Core.NPC;

namespace Mist.Application.Knowledge;

public sealed record VerifiedFactRecord(string FactId,string SourceRoot,string TimeScope,KnowledgeKind Kind);
public sealed class KnowledgeService(IAdventureRepository repository,RuntimeCatalog catalog)
{
    public KnowledgeProjection Projection {get;}=new(catalog);
    private static KnowledgeState Read(BranchSnapshot s,string actor)=>BranchOperations.Read<KnowledgeState>(s,"knowledge:"+actor)??new([]);
    public async ValueTask<ImmutableArray<SafeTopic>> TopicsAsync(string actor,IReadOnlySet<string>? personal=null)
    {
        var s=await repository.SnapshotAsync();if(actor=="N-MARA"&&!BranchOperations.Receipt(s,"Q-502"))return [];
        return Projection.Topics(actor,Read(s,actor),Read(s,"player"),personal??new HashSet<string>());
    }
    public async ValueTask<CommandResult> DeliverAsync(string actor,string receiptId)
    {
        var s=await repository.SnapshotAsync();if(actor!="player"&&!Projection.Actors.ContainsKey(actor))throw new InvalidOperationException("不能建立第二个人物身份。");
        var receipt=BranchOperations.Evidence(s,receiptId,"verified_fact");var record=Mist.Core.Contracts.StrictJson.Deserialize<VerifiedFactRecord>(receipt.ResultJson);
        if(!Projection.Facts.TryGetValue(record.FactId,out var fact)||fact.SourceRoot!=record.SourceRoot||fact.TimeScope!=record.TimeScope||fact.Kind!=record.Kind)throw new InvalidOperationException("回执与该版本知识来源不符。");
        // Personal delivery is handled only by the field-scoped disclosure workflow.
        if(fact.Personal)throw new InvalidOperationException("私人字段必须经许可预览与受控发送。");
        string id="knowledge:"+actor;var state=Read(s,actor);
        var evidence=new KnowledgeEvidence(record.FactId,record.SourceRoot,receiptId,record.TimeScope,record.Kind);
        var changed=state with{Evidence=state.Evidence.Add(evidence)};
        return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,"knowledge:"+actor+":"+record.FactId+":"+record.SourceRoot,"knowledge_delivered",[BranchOperations.Change(s,id,"knowledge",changed)],[id],new{actor,source_receipt=receiptId}));
    }
}

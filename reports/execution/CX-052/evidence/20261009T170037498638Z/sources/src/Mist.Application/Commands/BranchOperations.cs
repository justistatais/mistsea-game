using System.Collections.Immutable;
using System.Text.Json;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.Contracts;

namespace Mist.Application.Commands;

// Internal application utility. Views and assistance providers cannot issue arbitrary entity writes through it.
internal static class BranchOperations
{
    public static StoredEntity? Entity(BranchSnapshot s,string id)=>s.Entities.SingleOrDefault(e=>e.Id==id);
    public static T? Read<T>(BranchSnapshot s,string id) where T:class=>Entity(s,id) is {} e?StrictJson.Deserialize<T>(e.Json):null;
    public static EntityChange Change<T>(BranchSnapshot s,string id,string kind,T value)=>new(id,kind,Entity(s,id)?.Revision??-1,StrictJson.Serialize(value));
    public static bool Receipt(BranchSnapshot s,string key)=>s.Receipts.Any(r=>r.SemanticKey==key||r.SemanticKey==key+":resolution");
    public static StoredReceipt Evidence(BranchSnapshot s,string receiptId,string action)
    {
        var r=s.Receipts.SingleOrDefault(r=>r.ReceiptId==receiptId&&r.ActionType==action);return r??throw new InvalidOperationException("所引用的工作回执尚未可靠提交。");
    }
    public static void Safe(BranchSnapshot s)
    {
        foreach(var e in s.Entities.Where(e=>e.Kind=="combat"))
        {
            using var doc=JsonDocument.Parse(e.Json);var j=doc.RootElement.TryGetProperty("battle",out var b)?b:doc.RootElement;
            if(j.GetProperty("outcome").GetString() is "active" or "defeat")throw new InvalidOperationException("当前遭遇尚未结束，请在安全交接后执行。");
        }
    }
    public static CommitPlanCommand Plan(IAdventureRepository repository,BranchSnapshot s,string key,string action,ImmutableArray<EntityChange> changes,
        string[] reads,object result,ImmutableArray<ItemChange> items=default,ImmutableArray<BalanceChange> balances=default,CommandId? request=null,DraftStamp? draft=null)
    {
        var session=repository.Session;var required=reads.Concat(changes.Select(c=>c.Id)).Concat(s.Entities.Where(e=>e.Kind=="combat").Select(e=>e.Id)).Concat(new[]{"combat:workshop","combat:tower"}).Distinct(StringComparer.Ordinal).Select(id=>new EntityStamp(id,Entity(s,id)?.Revision??-1)).ToImmutableArray();
        return new(new(session.Profile,session.Branch,session.Load,request??new(Guid.NewGuid()),session.ReleaseId,required,draft),new(key,action,changes,items.IsDefault?[]:items,balances.IsDefault?[]:balances,StrictJson.Serialize(result)));
    }
}

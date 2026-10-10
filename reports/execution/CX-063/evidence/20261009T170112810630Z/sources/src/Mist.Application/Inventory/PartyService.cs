using System.Collections.Immutable;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Inventory;
using Mist.Core.Progression;
using Mist.Core.Skills;

namespace Mist.Application.Inventory;

public sealed record CharacterView(int Level,int Grade,BuildState Build,DerivedStats Derived,string Weapon,string Armor,ImmutableArray<string> Relics,int UnspentAttributes,int UnspentSkills);
public sealed class PartyService(IAdventureRepository repository,RuntimeCatalog catalog)
{
    public ItemCatalog Definitions {get;}=new(catalog);
    private readonly ImmutableDictionary<string,TalentDefinition> _talents=TalentRules.Read(catalog);
    private ValueTask<CommandResult> Commit(BranchSnapshot snapshot,string action,ImmutableArray<EntityChange> entities,ImmutableArray<ItemChange> items,ImmutableArray<BalanceChange> balances,string? semantic=null,CommandId? command=null,string? payload=null)
    {
        var s=repository.Session;var ids=snapshot.Entities.Where(e=>e.Id is "actor" or "hero-build"||e.Kind is "combat" or "roster" or "work_post" or "operator_duty").Select(e=>e.Id).Concat(new[]{"combat:workshop","combat:tower","combat:shore"}).Distinct();var reads=ids.Select(id=>new EntityStamp(id,snapshot.Entities.SingleOrDefault(e=>e.Id==id)?.Revision??-1)).ToImmutableArray();
        return repository.ExecuteAsync(new CommitPlanCommand(new(s.Profile,s.Branch,s.Load,command??new(Guid.NewGuid()),s.ReleaseId,reads),new(semantic??action+":"+Guid.NewGuid(),action,entities,items,balances,payload is null?"{}":StrictJson.Serialize(new{request_payload_sha256=PayloadHash(payload)}))));
    }
    private static string PayloadHash(string payload)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    private async ValueTask<CommandResult?> Replay(BranchSnapshot snapshot,CommandId request,string payload)
    {
        var known=snapshot.Commands.SingleOrDefault(c=>c.CommandId==request.Value.ToString());if(known is null)return null;
        var receipt=snapshot.Receipts.Single(r=>r.ReceiptId==known.ReceiptId);using var doc=JsonDocument.Parse(receipt.ResultJson);
        if(!doc.RootElement.TryGetProperty("request_payload_sha256",out var hash)||hash.GetString()!=PayloadHash(payload))throw new InvalidOperationException("请求身份不能用于另一个采购或制作提案。");
        return new(CommitStatus.AlreadyCommitted,await repository.FindReceiptAsync(repository.Session.Branch,request),[]);
    }
    private static void Safe(BranchSnapshot snapshot)
    {
        var actor=StrictJson.Deserialize<ActorState>(snapshot.Entities.Single(e=>e.Id=="actor").Json);
        if(actor.Scene is not("R1-S02" or "R1-S03"))throw new InvalidOperationException("请回到广场或事务所安全点再调整构筑、装备或采购。");
        foreach(var e in snapshot.Entities.Where(e=>e.Kind=="combat"))
        {
            using var doc=JsonDocument.Parse(e.Json);var state=doc.RootElement.TryGetProperty("battle",out var battle)?battle:doc.RootElement;if(state.GetProperty("outcome").GetString() is not("controlled" or "withdrawn"))throw new InvalidOperationException("当前遭遇尚未结束；构筑与装备只读。");
        }
    }
    public async ValueTask InitializeStarterCollectionAsync()
    {
        await repository.EnsureInventorySchemaAsync();
        var snapshot=await repository.SnapshotAsync();if(snapshot.Entities.Any(e=>e.Id=="hero-build"))return;
        BuildState build=new(new(),[],[]);
        var items=new[]{"WP-01","WP-03","WP-05","AR-01"}.Where(id=>!snapshot.Items.Any(i=>i.DefinitionId==id)).Select(id=>new ItemChange("collection:"+id,id,"new","",id is "WP-01" or "AR-01"?"equipped":"player","hero",id=="WP-01"?"weapon":id=="AR-01"?"armor":null)).ToImmutableArray();
        var result=await Commit(snapshot,"initialize_starter_collection",[new("hero-build","build",-1,StrictJson.Serialize(build))],items,[],"starter:collection");if(result.Status is not(CommitStatus.Committed or CommitStatus.AlreadyCommitted))throw new InvalidOperationException("起始收藏初始化未提交。");
    }
    public async ValueTask<CharacterView> InspectAsync()
    {
        var snapshot=await repository.SnapshotAsync();return Inspect(snapshot);
    }
    public CharacterView Inspect(BranchSnapshot snapshot)
    {
        int level=ProgressionRules.Level(snapshot.Balances.GetValueOrDefault("experience")),grade=ProgressionRules.Grade(snapshot.Receipts.Select(r=>r.SemanticKey));
        var build=StrictJson.Deserialize<BuildState>(snapshot.Entities.Single(e=>e.Id=="hero-build").Json);TalentRules.Validate(level,build,_talents);
        var equipped=snapshot.Items.Where(i=>i.OwnerKind=="equipped"&&i.OwnerId=="hero").ToArray();var weapon=equipped.Single(i=>i.Slot=="weapon");var armor=equipped.Single(i=>i.Slot=="armor");var relics=equipped.Where(i=>i.Slot is "relic1" or "relic2").Select(i=>i.DefinitionId).ToImmutableArray();
        var modules=snapshot.Items.Where(i=>i.OwnerKind=="module_installed"&&equipped.Any(e=>e.Id==i.OwnerId)).Select(i=>Definitions.Modules[i.DefinitionId]);
        return new(level,grade,build,ProgressionRules.Derive(level,grade,build.Attributes,Definitions.Items[weapon.DefinitionId],Definitions.Items[armor.DefinitionId],relics,modules),weapon.DefinitionId,armor.DefinitionId,relics,ProgressionRules.AttributeBudget(level)-build.Attributes.Values.Sum(v=>v-5),ProgressionRules.SkillBudget(level)-build.Learned.Length);
    }
    public async ValueTask<CommandResult> ApplyBuildAsync(BuildState build)
    {
        var snapshot=await repository.SnapshotAsync();Safe(snapshot);TalentRules.Validate(ProgressionRules.Level(snapshot.Balances.GetValueOrDefault("experience")),build,_talents);var old=snapshot.Entities.Single(e=>e.Id=="hero-build");
        return await Commit(snapshot,"apply_build",[new(old.Id,old.Kind,old.Revision,StrictJson.Serialize(build))],[],[]);
    }
    public async ValueTask<CommandResult> EquipAsync(string itemId,string slot,string actor="hero")
    {
        var snapshot=await repository.SnapshotAsync();Safe(snapshot);if(actor!="hero")
        {
            var member=Mist.Application.Commands.BranchOperations.Read<Mist.Application.Roster.RosterEntry>(snapshot,"roster:"+actor);
            if(member is not{Membership:Mist.Application.Roster.Membership.Joined,Availability:Mist.Application.Roster.ActorAvailability.ShipStandby}||slot!="relic1"||snapshot.Entities.Any(e=>e.Id=="workpost:"+actor&&e.Kind=="work_post"||e.Id=="duty:"+actor&&e.Kind=="operator_duty"))throw new InvalidOperationException("只有已加入且未承担现场工作的同伴能借用玩家的一个遗物；制式装备固定。");
        }
        var item=snapshot.Items.Single(i=>i.Id==itemId);if(!Definitions.Items.TryGetValue(item.DefinitionId,out var definition)||item.OwnerKind is not("player" or "equipped")||item.OwnerId!="hero"&&item.OwnerId!=actor)throw new InvalidOperationException("此实物不在角色可装备的收藏中。");
        int grade=ProgressionRules.Grade(snapshot.Receipts.Select(r=>r.SemanticKey));string kind=slot.StartsWith("relic",StringComparison.Ordinal)?"relic":slot;
        if(kind!=definition.Slot||slot is not("weapon" or "armor" or "relic1" or "relic2" or "field_tool")||slot=="relic2"&&grade<2)throw new InvalidOperationException("槽位类型或解锁条件不符。");
        var displaced=snapshot.Items.SingleOrDefault(i=>i.OwnerKind=="equipped"&&i.OwnerId==actor&&i.Slot==slot);if(displaced?.Id==item.Id)throw new InvalidOperationException("该装备已在此槽。");
        var changes=ImmutableArray.CreateBuilder<ItemChange>();if(displaced is not null)changes.Add(new(displaced.Id,displaced.DefinitionId,"equipped",actor,"player","hero",null));
        changes.Add(new(item.Id,item.DefinitionId,item.OwnerKind,item.OwnerId,"equipped",actor,slot));return await Commit(snapshot,"equip_unique_instance",[],changes.ToImmutable(),[]);
    }
    private static string? UnlockQuest(string id)=>id switch
    {
        "RE-03" or "RE-04"=>"Q-104","WP-02" or "AR-04" or "RE-05" or "RE-06"=>"Q-204","WP-04" or "AR-05" or "RE-07" or "RE-08"=>"Q-304","WP-06" or "AR-06" or "RE-09" or "RE-10"=>"Q-404","RE-11" or "RE-12"=>"Q-504",_=>null
    };
    public async ValueTask<CommandResult> BuyCollectionAsync(string definitionId,CommandId request)
    {
        var snapshot=await repository.SnapshotAsync();string payload=StrictJson.Serialize(new{operation="buy_collection",definitionId});var replay=await Replay(snapshot,request,payload);if(replay is not null)return replay;Safe(snapshot);var item=Definitions.Items[definitionId];string? unlock=UnlockQuest(item.Id);
        if(item.MarksPrice<=0||unlock is null||!snapshot.Receipts.Any(r=>r.SemanticKey==unlock+":resolution"))throw new InvalidOperationException("此型号尚未开放公开目录；免费来源不能冒充付费购买。");
        if(snapshot.Items.Any(i=>i.DefinitionId==definitionId))throw new InvalidOperationException("这个型号已在收藏；重复购买不产生退款或副本。");
        return await Commit(snapshot,"buy_collection",[],[new("collection:"+item.Id,item.Id,"new","","player","hero",null)],[new("marks",-item.MarksPrice)],"purchase:collection:"+item.Id,request,payload);
    }
    public async ValueTask<CommandResult> CraftModuleAsync(string definitionId,string? oldInstance,CommandId request)
    {
        var snapshot=await repository.SnapshotAsync();string payload=StrictJson.Serialize(new{operation="craft_module",definitionId,oldInstance});var replay=await Replay(snapshot,request,payload);if(replay is not null)return replay;Safe(snapshot);var module=Definitions.Modules[definitionId];int grade=ProgressionRules.Grade(snapshot.Receipts.Select(r=>r.SemanticKey));
        if(grade<module.MinimumRank)throw new InvalidOperationException("改件阶位尚未解锁。");var changes=ImmutableArray.CreateBuilder<ItemChange>();
        if(module.ConsumesModule is not null)
        {
            var old=snapshot.Items.SingleOrDefault(i=>i.Id==oldInstance);if(old is null||old.DefinitionId!=module.ConsumesModule||!HeldModule(snapshot,old))throw new InvalidOperationException("升级必须消耗持有的对应原件。");
            changes.Add(new(old.Id,old.DefinitionId,old.OwnerKind,old.OwnerId,"consumed","upgrade:"+request.Value,null));
        }
        else if(oldInstance is not null)throw new InvalidOperationException("I级制作不接受原件参数。");
        changes.Add(new("module:"+request.Value,module.Id,"new","","player","hero",null));
        return await Commit(snapshot,"craft_module",[],changes.ToImmutable(),[new("marks",-module.Marks),new("parts",-module.Parts)],"craft:"+request.Value,request,payload);
    }
    private static bool HeldModule(BranchSnapshot snapshot,StoredItem module)=>module.OwnerKind=="player"&&module.OwnerId=="hero"||module.OwnerKind=="module_installed"&&snapshot.Items.Any(i=>i.Id==module.OwnerId&&i.OwnerId=="hero"&&i.OwnerKind is "player" or "equipped");
    public async ValueTask<CommandResult> InstallModuleAsync(string moduleId,string equipmentId)
    {
        var snapshot=await repository.SnapshotAsync();Safe(snapshot);var module=snapshot.Items.Single(i=>i.Id==moduleId);var equipment=snapshot.Items.Single(i=>i.Id==equipmentId);
        if(!Definitions.Modules.TryGetValue(module.DefinitionId,out var mod)||!Definitions.Items.TryGetValue(equipment.DefinitionId,out var eq)||!HeldModule(snapshot,module)||equipment.OwnerId!="hero"||equipment.OwnerKind is not("player" or "equipped")||mod.Slot!=eq.Slot)throw new InvalidOperationException("改件或目标装备归属/类型不符。");
        if(module.OwnerKind=="module_installed"&&module.OwnerId==equipment.Id)throw new InvalidOperationException("原件已经安装在这里。");
        var changes=ImmutableArray.CreateBuilder<ItemChange>();var existing=snapshot.Items.SingleOrDefault(i=>i.OwnerKind=="module_installed"&&i.OwnerId==equipment.Id);
        if(existing is not null)changes.Add(new(existing.Id,existing.DefinitionId,existing.OwnerKind,existing.OwnerId,"player","hero",null));changes.Add(new(module.Id,module.DefinitionId,module.OwnerKind,module.OwnerId,"module_installed",equipment.Id,"module"));
        return await Commit(snapshot,"transfer_module",[],changes.ToImmutable(),[]);
    }
    public async ValueTask<CommandResult> BuyConsumableAsync(string definition,CommandId request)
    {
        var snapshot=await repository.SnapshotAsync();string payload=StrictJson.Serialize(new{operation="buy_personal_supply",definition});var replay=await Replay(snapshot,request,payload);if(replay is not null)return replay;Safe(snapshot);var entry=catalog.Definitions["consumables"].Single(c=>c.Id==definition);using var doc=JsonDocument.Parse(entry.PayloadJson);int price=doc.RootElement.GetProperty("marks_price").GetInt32();
        return await Commit(snapshot,"buy_personal_supply",[],[new("supply:"+request.Value,definition,"new","","player","hero",null)],[new("marks",-price)],"supply-purchase:"+request.Value,request,payload);
    }
    public ValueTask<CommandResult> SellAsync(string itemId)=>SellAsync(itemId,new(Guid.NewGuid()));
    public async ValueTask<CommandResult> SellAsync(string itemId,CommandId request)
    {
        var snapshot=await repository.SnapshotAsync();string payload=StrictJson.Serialize(new{operation="sell_personal_supply",itemId});var replay=await Replay(snapshot,request,payload);if(replay is not null)return replay;Safe(snapshot);var item=snapshot.Items.Single(i=>i.Id==itemId);
        if(Definitions.Items.ContainsKey(item.DefinitionId)||Definitions.Modules.ContainsKey(item.DefinitionId)||item.OwnerKind!="player"||item.OwnerId!="hero")throw new InvalidOperationException("收藏、改件、免费配发、任务设备和工程物资不可出售。");
        string? paidId=item.Id.StartsWith("supply:",StringComparison.Ordinal)?item.Id[7..]:null;
        if(paidId is null||!Guid.TryParse(paidId,out _)||!snapshot.Receipts.Any(r=>r.SemanticKey=="supply-purchase:"+paidId&&r.ActionType=="buy_personal_supply"&&r.ResultJson==StrictJson.Serialize(new{request_payload_sha256=PayloadHash(StrictJson.Serialize(new{operation="buy_personal_supply",definition=item.DefinitionId}))})))throw new InvalidOperationException("只有保留该实物付费采购回执的个人增强补给可以出售。");
        var supply=catalog.Definitions["consumables"].SingleOrDefault(c=>c.Id==item.DefinitionId)??throw new InvalidOperationException("未登记个人出售规则。");using var doc=JsonDocument.Parse(supply.PayloadJson);int price=doc.RootElement.GetProperty("marks_price").GetInt32()/4;
        return await Commit(snapshot,"sell_personal_supply",[],[new(item.Id,item.DefinitionId,item.OwnerKind,item.OwnerId,"sold","shop",null)],[new("marks",price)],"supply-sale:"+item.Id,request,payload);
    }
}

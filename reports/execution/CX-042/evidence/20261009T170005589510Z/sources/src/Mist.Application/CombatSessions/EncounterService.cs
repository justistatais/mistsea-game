using System.Collections.Immutable;
using Mist.Application.Contracts;
using Mist.Application.Inventory;
using Mist.Application.Q102;
using Mist.Application.Storage;
using Mist.Core.Combat;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;

namespace Mist.Application.CombatSessions;

public sealed record EncounterState(BattleSnapshot Battle,BattleSnapshot Entry,ActorState ReturnActor,bool ResultApplied=false);
public sealed record EncounterActionPreview(Guid Token,ActionPreviewView Action,string Description);

public sealed class EncounterService(IAdventureRepository repository,RuntimeCatalog catalog,Func<string,ValueTask<string>>? protectEntry=null)
{
    public const string EntityId="combat:workshop";
    private readonly BattleDefinitions _definitions=new(catalog);
    private readonly Dictionary<Guid,CommitPlanCommand> _previews=new();
    public async ValueTask<EncounterState?> ViewAsync()
    {
        var snapshot=await repository.SnapshotAsync();return State(snapshot);
    }
    private static EncounterState? State(BranchSnapshot snapshot)=>snapshot.Entities.SingleOrDefault(e=>e.Id==EntityId) is { } entity?StrictJson.Deserialize<EncounterState>(entity.Json):null;
    private static EntityChange Change<T>(BranchSnapshot snapshot,string id,string kind,T state)=>new(id,kind,snapshot.Entities.SingleOrDefault(e=>e.Id==id)?.Revision??-1,StrictJson.Serialize(state));
    private CommitPlanCommand Command(BranchSnapshot snapshot,string action,ImmutableArray<EntityChange> changes,ImmutableArray<ItemChange> items,string semantic,CommandId request,string result="{}")
    {
        var session=repository.Session;var ids=new[]{EntityId,"actor","workshop","hero-build","quest:Q-102","permission:Q-102","party:formation"}.Concat(snapshot.Entities.Where(e=>e.Kind is "roster" or "work_post").Select(e=>e.Id));var reads=ids.Distinct().Select(id=>new EntityStamp(id,snapshot.Entities.SingleOrDefault(e=>e.Id==id)?.Revision??-1)).ToImmutableArray();
        return new(new(session.Profile,session.Branch,session.Load,request,session.ReleaseId,reads),new(semantic,action,changes,items,[],result));
    }
    public async ValueTask<CommandResult> BeginWorkshopAsync(string difficulty="standard",string[]? carriedSupplies=null)
    {
        var snapshot=await repository.SnapshotAsync();var existing=State(snapshot);if(existing is not null&&existing.Battle.Outcome!=CombatOutcome.Withdrawn)throw new InvalidOperationException("已有遭遇；请继续、重试或先明确撤回入口。");
        var actor=StrictJson.Deserialize<ActorState>(snapshot.Entities.Single(e=>e.Id=="actor").Json);var workshop=StrictJson.Deserialize<WorkshopState>(snapshot.Entities.Single(e=>e.Id=="workshop").Json);
        if(workshop.FrontSafe)throw new InvalidOperationException("前门已可安全进入，不需要生成新战斗。");
        var q=new Q102Service(repository,catalog);string other=actor.Scene=="R1-S05"?"R1-S06":actor.Scene=="R1-S06"?"R1-S05":throw new InvalidOperationException("请先走到工坊正门的危险入口。");var port=q.Geometry[actor.Scene].Ports.Single(p=>p.TargetScene==other);
        if(actor.Position.Distance(port.Stand)>2.5)throw new InvalidOperationException("请先走近正门入口。");
        var permission=snapshot.Entities.Single(e=>e.Id=="permission:Q-102");using(var p=System.Text.Json.JsonDocument.Parse(permission.Json))if(!p.RootElement.GetProperty("allowed").GetBoolean())throw new InvalidOperationException("当前现场授权不可用。");
        var party=new PartyService(repository,catalog);var hero=party.Inspect(snapshot);var equipped=snapshot.Items.Where(i=>i.OwnerKind=="equipped"&&i.OwnerId=="hero").ToArray();var modules=snapshot.Items.Where(i=>i.OwnerKind=="module_installed"&&equipped.Any(eq=>eq.Id==i.OwnerId)).Select(i=>party.Definitions.Modules[i.DefinitionId]);
        var roster=new Mist.Application.Roster.RosterService(repository,catalog);var helperIds=roster.Helpers(snapshot);
        var battle=_definitions.Create("BENCH-WORKSHOP",hero.Level,hero.Grade,hero.Build,hero.Weapon,hero.Armor,hero.Relics,modules,existing?.Battle.Instance??Guid.NewGuid(),Guid.NewGuid(),difficulty,helperIds.ToArray());
        foreach(var helper in battle.Units.Where(u=>u.Id.StartsWith("CP-",StringComparison.Ordinal)))
        {
            var member=StrictJson.Deserialize<Mist.Application.Roster.RosterEntry>(snapshot.Entities.Single(e=>e.Id=="roster:"+helper.Id).Json);helper.Variants=member.Tactics.ToDictionary();
            var relics=snapshot.Items.Where(i=>i.OwnerKind=="equipped"&&i.OwnerId==helper.Id&&i.Slot is "relic1" or "relic2").Select(i=>i.DefinitionId).ToArray();helper.Relics=relics;
            helper.MaxFocus=Math.Min(14,helper.MaxFocus+(relics.Contains("RE-09")?1:0));helper.Focus=helper.MaxFocus;helper.ArmorBp=Math.Min(4500,helper.ArmorBp+(relics.Contains("RE-03")?300:0));helper.Initiative+=relics.Contains("RE-04")?2:0;helper.ExposureBonus=Math.Min(4000,helper.ExposureBonus+(relics.Contains("RE-07")?500:0));helper.GuardBase=Math.Min(3500,helper.GuardBase+(relics.Contains("RE-11")?500:0));
        }
        carriedSupplies??=[];if(carriedSupplies.Length>2||carriedSupplies.Distinct().Count()!=carriedSupplies.Length)throw new InvalidOperationException("增强补给最多两个真实实例。");
        battle.Supplies=carriedSupplies.Select(id=>
        {
            var item=snapshot.Items.Single(i=>i.Id==id);if(item.OwnerKind!="player"||item.OwnerId!="hero"||item.DefinitionId is not("CON-01" or "CON-02" or "CON-03" or "CON-04"))throw new InvalidOperationException("增强补给未在当前个人持有物内。");return new CarriedSupply(item.Id,item.DefinitionId);
        }).ToList();
        var entry=StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(battle));var engine=new BattleEngine(_definitions,battle);engine.Start();var encounter=new EncounterState(engine.Snapshot(),entry,actor);
        var command=Command(snapshot,"begin_workshop_encounter",[Change(snapshot,EntityId,"combat",encounter)],[],"encounter:"+battle.Instance+":attempt:"+battle.Attempt+":entry",new(Guid.NewGuid()));
        if(protectEntry is not null)await protectEntry("encounter:"+battle.Instance+":entry");return await repository.ExecuteAsync(command);
    }
    public async ValueTask<EncounterActionPreview> PreviewAsync(BattleRequest request)
    {
        var snapshot=await repository.SnapshotAsync();var old=State(snapshot)??throw new InvalidOperationException("尚未建立遭遇。");var engine=new BattleEngine(_definitions,old.Battle);var view=engine.Preview(request);
        if(!view.Legal)return new(Guid.Empty,view,view.Reason??"行动不可用。");
        engine.Apply(request);var resolved=engine.Snapshot();var changes=ImmutableArray.CreateBuilder<EntityChange>();var items=ImmutableArray.CreateBuilder<ItemChange>();bool settled=old.ResultApplied;
        foreach(var used in resolved.Supplies.Where(i=>i.Used&&!old.Battle.Supplies.Single(s=>s.Instance==i.Instance).Used))
        {
            var item=snapshot.Items.Single(i=>i.Id==used.Instance);if(item.OwnerKind!="player"||item.OwnerId!="hero")throw new InvalidOperationException("入口补给归属已变化，请重新读取行动。");items.Add(new(item.Id,item.DefinitionId,"player","hero","consumed",resolved.Attempt.ToString(),null));
        }
        if(resolved.Outcome==CombatOutcome.Controlled&&!settled)
        {
            var workshop=StrictJson.Deserialize<WorkshopState>(snapshot.Entities.Single(e=>e.Id=="workshop").Json);changes.Add(Change(snapshot,"workshop","workshop",workshop with{Mode=WorkshopMode.Disabled}));settled=true;
        }
        changes.Add(Change(snapshot,EntityId,"combat",old with{Battle=resolved,ResultApplied=settled}));Guid token=Guid.NewGuid();
        _previews.Clear();_previews[token]=Command(snapshot,"combat_action",changes.ToImmutable(),items.ToImmutable(),"encounter:"+resolved.Instance+":attempt:"+resolved.Attempt+":action:"+request.Command.Value,request.Command,StrictJson.Serialize(new{request,outcome=resolved.Outcome}));
        return new(token,view,$"{request.Actor} · {request.Action} · AP {view.Ap} / 专注 {view.Focus}");
    }
    public ValueTask<CommandResult> ConfirmAsync(Guid token)
    {
        if(!_previews.TryGetValue(token,out var command))return ValueTask.FromResult(new CommandResult(CommitStatus.Rejected,null,[new("preview_closed",EntityId,"行动预览已取消，请重新选择。") ]));
        // Retain the exact immutable command for an uncertain-commit retry; the store checks load and request hashes.
        return repository.ExecuteAsync(command);
    }
    public void ClosePreview(Guid token)=>_previews.Remove(token);
    public async ValueTask<CommandResult> WithdrawAsync()
    {
        var snapshot=await repository.SnapshotAsync();var old=State(snapshot)??throw new InvalidOperationException("尚无遭遇。");if(old.ResultApplied||old.Battle.Outcome==CombatOutcome.Controlled)throw new InvalidOperationException("已制止威胁的结果不能通过战斗撤回抹掉。");
        var restored=StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(old.Entry));restored.Outcome=CombatOutcome.Withdrawn;restored.CurrentActor=null;
        var items=old.Battle.Supplies.Where(i=>i.Used).Select(i=>
        {
            var held=snapshot.Items.Single(x=>x.Id==i.Instance);if(held.OwnerKind!="consumed"||held.OwnerId!=old.Battle.Attempt.ToString())throw new InvalidOperationException("本次入口消耗的实物已出现外部变更。");return new ItemChange(held.Id,held.DefinitionId,held.OwnerKind,held.OwnerId,"player","hero",null);
        }).ToImmutableArray();
        _previews.Clear();return await repository.ExecuteAsync(Command(snapshot,"withdraw_to_encounter_entry",[Change(snapshot,EntityId,"combat",old with{Battle=restored}),Change(snapshot,"actor","actor",old.ReturnActor)],items,"encounter:"+old.Battle.Attempt+":withdraw",new(Guid.NewGuid())));
    }
    public async ValueTask<CommandResult> RetryAsync(string? difficulty=null)
    {
        var old=await ViewAsync()??throw new InvalidOperationException("尚无入口。");if(old.ResultApplied)throw new InvalidOperationException("成功结果不可重试刷奖励。");
        var withdraw=await WithdrawAsync();if(withdraw.Status is not(CommitStatus.Committed or CommitStatus.AlreadyCommitted))return withdraw;
        return await BeginWorkshopAsync(difficulty??old.Battle.Difficulty,old.Entry.Supplies.Select(i=>i.Instance).ToArray());
    }
}

using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.CombatSessions;
using Mist.Application.Contracts;
using Mist.Application.Inventory;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Application.WorkOrders;
using Mist.Core.Campaign;
using Mist.Core.ChapterOne;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Engineering;
using Mist.Core.Quests;

namespace Mist.Application.ChapterOne;

public sealed partial class ChapterOneService
{
    public const string TowerEncounter="combat:tower";
    public BattleDefinitions BattleDefinitions {get;}=new(catalog);
    private static EncounterState? Tower(BranchSnapshot s)=>BranchOperations.Read<EncounterState>(s,TowerEncounter);
    public async ValueTask<EncounterState?> TowerAsync()=>Tower(await repository.SnapshotAsync());
    private string[] TowerReads(BranchSnapshot s)=>new[]{"actor",TowerEncounter,"hero-build","tower:local","quest:Q-104","permission:Q-104","party:formation"}.Concat(s.Entities.Where(e=>e.Kind is "roster" or "work_post" or "operator_duty").Select(e=>e.Id)).ToArray();
    public async ValueTask<CommandResult> AcceptTowerWorkAsync()
    {
        var s=await repository.SnapshotAsync();BranchOperations.Safe(s);At(s,"R1-S09.warden");if(!BranchOperations.Receipt(s,"Q-103")||BranchOperations.Entity(s,"finding:E-01") is null)throw new InvalidOperationException("请先提交有原始来源支持的事故核查结论。");if(Quest(s,"Q-104") is{Lifecycle:QuestLifecycle.Active or QuestLifecycle.Closed})return new(CommitStatus.AlreadyCommitted,null,[]);
        return await Commit(s,"Q104:work:accepted","accept_tidegate_site_work",[BranchOperations.Change(s,"quest:Q-104","quest",new QuestState("Q-104",Guid.NewGuid(),QuestLifecycle.Active,null)),BranchOperations.Change(s,"permission:Q-104","permission",new{allowed=true,scope="tidegate_local_safety",basis="Q-103:resolution"}),BranchOperations.Change(s,"tower:local","tower_device",new TidegateLocalState(0,true,null))],["actor","quest:Q-104","permission:Q-104","tower:local","finding:E-01"],new{tower="TWR-R1",current_safety="not_verified",calibration_not_yet_awarded=true});
    }
    public async ValueTask<CommandResult> InspectLocalSupplyAsync()
    {
        var s=await repository.SnapshotAsync();BranchOperations.Safe(s);At(s,"R1-S09.supply_indicator");Permitted(s,"permission:Q-104");var local=BranchOperations.Read<TidegateLocalState>(s,"tower:local")??throw new InvalidOperationException("塔站设备未登记。");if(!local.SupplyAvailable)throw new InvalidOperationException("当前本塔局部供给不可用，请先处理现场设备；不能通兑渡轮或工程库存。");
        return await Commit(s,"Q104:inspect-local:"+local.Configuration,"inspect_tidegate_local_supply",[BranchOperations.Change(s,"tower:local","tower_device",local with{InspectionRevision=local.Configuration})],["actor","permission:Q-104","tower:local"],new{configuration=local.Configuration,source="tidegate_local_indicator",engineering_resources_changed=false});
    }
    public async ValueTask<CommandResult> BeginTowerAsync(string difficulty="standard",string[]? carriedSupplies=null)
    {
        var s=await repository.SnapshotAsync();BranchOperations.Safe(s);At(s,"R1-S10.encounter_entry");Permitted(s,"permission:Q-104");if(Quest(s,"Q-104") is not{Lifecycle:QuestLifecycle.Active})throw new InvalidOperationException("塔站工作尚未接受或已经结算。");var local=BranchOperations.Read<TidegateLocalState>(s,"tower:local");if(local is not{Prepared:true,Safe:false}||!Acquired(s,Package.Materials.Single(m=>m.Id=="Q104-R-CONTROLS")))throw new InvalidOperationException("进场前需要当前局部供给检查与现场操作说明。");var old=Tower(s);if(old is not null&&old.Battle.Outcome!=CombatOutcome.Withdrawn)throw new InvalidOperationException("已有塔站遭遇，请继续或按指定入口恢复。");
        var party=new PartyService(repository,catalog);var hero=party.Inspect(s);if(hero.Level!=5||hero.Grade!=0||hero.Armor!="AR-01")throw new InvalidOperationException("本次潮门塔候选必须使用真实5级、校准0及战前防具，不能预支Q104奖励。");
        var equipped=s.Items.Where(i=>i.OwnerKind=="equipped"&&i.OwnerId=="hero").ToArray();var modules=s.Items.Where(i=>i.OwnerKind=="module_installed"&&equipped.Any(eq=>eq.Id==i.OwnerId)).Select(i=>party.Definitions.Modules[i.DefinitionId]);var roster=new Mist.Application.Roster.RosterService(repository,catalog);var helpers=roster.Helpers(s);
        var battle=BattleDefinitions.Create("BENCH-TOWER",hero.Level,hero.Grade,hero.Build,hero.Weapon,hero.Armor,hero.Relics,modules,old?.Battle.Instance??Guid.NewGuid(),Guid.NewGuid(),difficulty,helpers.ToArray());
        foreach(var helper in battle.Units.Where(u=>u.Id.StartsWith("CP-",StringComparison.Ordinal)))
        {
            var member=BranchOperations.Read<Mist.Application.Roster.RosterEntry>(s,"roster:"+helper.Id)??throw new InvalidOperationException("实际同伴身份未登记。");helper.Variants=member.Tactics.ToDictionary();var relics=s.Items.Where(i=>i.OwnerKind=="equipped"&&i.OwnerId==helper.Id&&i.Slot=="relic1").Select(i=>i.DefinitionId).ToArray();helper.Relics=relics;
            helper.MaxFocus=Math.Min(14,helper.MaxFocus+(relics.Contains("RE-09")?1:0));helper.Focus=helper.MaxFocus;helper.ArmorBp=Math.Min(4500,helper.ArmorBp+(relics.Contains("RE-03")?300:0));helper.Initiative+=relics.Contains("RE-04")?2:0;helper.ExposureBonus=Math.Min(4000,helper.ExposureBonus+(relics.Contains("RE-07")?500:0));helper.GuardBase=Math.Min(3500,helper.GuardBase+(relics.Contains("RE-11")?500:0));
        }
        carriedSupplies??=[];if(carriedSupplies.Length>2||carriedSupplies.Distinct().Count()!=carriedSupplies.Length)throw new InvalidOperationException("进场增强补给最多两个真实实例。");foreach(string id in carriedSupplies){var item=s.Items.SingleOrDefault(i=>i.Id==id&&i.OwnerKind=="player"&&i.OwnerId=="hero"&&(i.DefinitionId is "CON-01" or "CON-02" or "CON-03" or "CON-04"))??throw new InvalidOperationException("所选补给尚未实际持有。");battle.Supplies.Add(new(item.Id,item.DefinitionId));}
        var entry=StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(battle));var engine=new BattleEngine(BattleDefinitions,battle);engine.Start();var encounter=new EncounterState(engine.Snapshot(),entry,Actor(s));if(protectEntry is not null)await protectEntry("tidegate:"+battle.Instance+":entry");
        return await Commit(s,"tower:"+battle.Instance+":attempt:"+battle.Attempt+":entry","begin_tidegate_encounter",[BranchOperations.Change(s,TowerEncounter,"combat",encounter)],TowerReads(s),new{entry_level=hero.Level,entry_grade=hero.Grade,entry_armor=hero.Armor,instance=battle.Instance,attempt=battle.Attempt,reward_not_advanced=true});
    }
    public async ValueTask<EncounterActionPreview> PreviewTowerActionAsync(BattleRequest request)
    {
        var s=await repository.SnapshotAsync();var old=Tower(s)??throw new InvalidOperationException("尚无塔站遭遇。");Permitted(s,"permission:Q-104");var engine=new BattleEngine(BattleDefinitions,old.Battle);var view=engine.Preview(request);if(!view.Legal)return new(Guid.Empty,view,view.Reason??"行动不可执行。");engine.Apply(request);var result=engine.Snapshot();var changes=ImmutableArray.CreateBuilder<EntityChange>();var items=ImmutableArray.CreateBuilder<ItemChange>();bool applied=old.ResultApplied;string action="tidegate_combat_action";object payload=new{request,outcome=result.Outcome};
        foreach(var used in result.Supplies.Where(i=>i.Used&&!old.Battle.Supplies.Single(x=>x.Instance==i.Instance).Used)){var held=s.Items.Single(i=>i.Id==used.Instance&&i.OwnerKind=="player"&&i.OwnerId=="hero");items.Add(new(held.Id,held.DefinitionId,"player","hero","consumed",result.Attempt.ToString(),null));}
        if(result.Outcome==CombatOutcome.Controlled&&!applied)
        {
            var local=BranchOperations.Read<TidegateLocalState>(s,"tower:local")!;if(!local.Prepared||!result.Controls.Values.All(v=>v)||result.Units.Any(u=>u.Side==CombatSide.Enemy&&u.Alive))throw new InvalidOperationException("两阀、隔离与威胁制止必须实际共同成立。");
            var ready=BranchOperations.Read<TowerReadiness>(s,"readiness:TWR-R1")??new TowerReadiness("TWR-R1",false,false,null,false,false,false,null);changes.Add(BranchOperations.Change(s,"tower:local","tower_device",local with{Safe=true}));changes.Add(BranchOperations.Change(s,"readiness:TWR-R1","tower_readiness",ready with{Safe=true}));applied=true;action="tower_safety_verified";payload=new TowerSafety("TWR-R1",true,"encounter:"+result.Instance+":attempt:"+result.Attempt);
        }
        changes.Add(BranchOperations.Change(s,TowerEncounter,"combat",old with{Battle=result,ResultApplied=applied}));var command=BranchOperations.Plan(repository,s,"tower:"+result.Instance+":attempt:"+result.Attempt+":action:"+request.Command.Value,action,changes.ToImmutable(),TowerReads(s).Append("readiness:TWR-R1").ToArray(),payload,items:items.ToImmutable(),request:request.Command);Guid token=Guid.NewGuid();_previews[token]=command;return new(token,view,$"{request.Actor} · {request.Action} · AP {view.Ap} / 专注 {view.Focus}");
    }
    public async ValueTask<CommandResult> WithdrawTowerAsync()
    {
        var s=await repository.SnapshotAsync();var old=Tower(s)??throw new InvalidOperationException("尚无塔站遭遇。");if(old.ResultApplied||old.Battle.Outcome==CombatOutcome.Controlled)throw new InvalidOperationException("已经形成的塔站安全结果不能撤回刷奖励。");var entry=StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(old.Entry));entry.Outcome=CombatOutcome.Withdrawn;entry.CurrentActor=null;
        var items=old.Battle.Supplies.Where(i=>i.Used).Select(i=>{var held=s.Items.Single(x=>x.Id==i.Instance&&x.OwnerKind=="consumed"&&x.OwnerId==old.Battle.Attempt.ToString());return new ItemChange(held.Id,held.DefinitionId,held.OwnerKind,held.OwnerId,"player","hero",null);}).ToImmutableArray();_previews.Clear();
        return await Commit(s,"tower:"+old.Battle.Attempt+":withdraw","withdraw_tidegate_entry",[BranchOperations.Change(s,TowerEncounter,"combat",old with{Battle=entry}),BranchOperations.Change(s,"actor","actor",old.ReturnActor)],TowerReads(s),new{materials_and_drafts_retained=true},items:items);
    }
    public async ValueTask<CommandResult> RetryTowerAsync(string? difficulty=null)
    {
        var old=Tower(await repository.SnapshotAsync())??throw new InvalidOperationException("尚无入口。");var r=await WithdrawTowerAsync();if(r.Status is not(CommitStatus.Committed or CommitStatus.AlreadyCommitted))return r;return await BeginTowerAsync(difficulty??old.Battle.Difficulty,old.Entry.Supplies.Select(i=>i.Instance).ToArray());
    }
    public async ValueTask<CommandResult> VerifyTowerAfterControlAsync()
    {
        var s=await repository.SnapshotAsync();BranchOperations.Safe(s);At(s,"R1-S10.safety_console");Permitted(s,"permission:Q-104");var battle=Tower(s);var local=BranchOperations.Read<TidegateLocalState>(s,"tower:local");if(battle is not{ResultApplied:true}||battle.Battle.Outcome!=CombatOutcome.Controlled||local is not{Safe:true,Prepared:true}||!battle.Battle.Controls.Values.All(v=>v))throw new InvalidOperationException("需先实际制止机械并建立全部安全联锁。");
        var safety=s.Receipts.SingleOrDefault(r=>r.ActionType=="tower_safety_verified"&&StrictJson.Deserialize<TowerSafety>(r.ResultJson).SourceWork=="encounter:"+battle.Battle.Instance+":attempt:"+battle.Battle.Attempt)??throw new InvalidOperationException("实际塔站安全回执尚未形成。");
        return await Commit(s,"Q104:post-control-inspection","verify_tidegate_local_result",[BranchOperations.Change(s,"tower:local","tower_device",local with{FinalInspection=true,SafetyReceipt=safety.ReceiptId})],TowerReads(s),new{source_safety_receipt=safety.ReceiptId,local_safety_only=true,baseline_not_automatically_ready=true});
    }
    public async ValueTask<ChapterOnePreview> PreviewHandoverAsync()
    {
        var s=await repository.SnapshotAsync();BranchOperations.Safe(s);var issues=new List<string>();var local=BranchOperations.Read<TidegateLocalState>(s,"tower:local");if(!Allowed(s,"permission:Q-104"))issues.Add("当前塔站工作许可不可用。");if(Quest(s,"Q-104") is not{Lifecycle:QuestLifecycle.Active})issues.Add("塔站委托尚未接受或已经结算。");if(local is not{Safe:true,FinalInspection:true}||Tower(s)?.Battle.Outcome!=CombatOutcome.Controlled)issues.Add("塔站制止及实际现场复核尚未完成。");if(!Acquired(s,Package.Materials.Single(m=>m.Id=="Q104-R-HANDOVER")))issues.Add("船务移交条款尚未实际取得。");try{At(s,"R1-S01.glimmer_handover");}catch(InvalidOperationException){issues.Add("请回接应泊位巡灯船移交站位。");}if(BranchOperations.Entity(s,"ship:glimmer") is not null)issues.Add("已有同一巡灯船的权威实例，请核对原移交记录。");if(issues.Count>0)return new(Guid.Empty,false,issues.ToArray(),"微光号使用权尚不能移交。");
        BranchOperations.Evidence(s,local!.SafetyReceipt!,"tower_safety_verified");var reward=catalog.Quests.Single(q=>q.Id=="Q-104").Reward;var ship=new SharedVessel(Guid.NewGuid(),"R1","N-DECKWATCH",["HUB-S01","HUB-S02","HUB-S03"]);var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(BranchOperations.Change(s,"quest:Q-104","quest",Quest(s,"Q-104")!.Close(QuestOutcome.Completed)));changes.Add(BranchOperations.Change(s,"ship:glimmer","shared_vessel",ship));changes.Add(BranchOperations.Change(s,"commission:cross-region","permission",new{allowed=true,next_region="R2",purpose="receiving_record_cross_check",basis="Q-104:resolution",mara_current_status="unknown"}));
        if(BranchOperations.Read<CampaignState>(s,"campaign") is{} campaign)changes.Add(BranchOperations.Change(s,"campaign","campaign",campaign with{Q104Closed=true,GlimmerOwned=true}));
        var items=reward.Items.Select(id=>new ItemChange("Q104:original:"+id,id,"new","","player","hero",null)).ToImmutableArray();var command=BranchOperations.Plan(repository,s,"Q-104:resolution","tidegate_vessel_handover",changes.ToImmutable(),TowerReads(s).Concat(new[]{"ship:glimmer","campaign","commission:cross-region","material:Q104-R-HANDOVER"}).ToArray(),new TidegateHandover(ship.Instance.ToString(),"GLIMMER","N-DECKWATCH",ship.Spaces.ToArray(),"R2","E-01"),items:items,balances:[new("experience",reward.Bundle.Experience),new("marks",reward.Bundle.Marks),new("parts",reward.Bundle.Parts)]);Guid token=Guid.NewGuid();_previews[token]=command;return new(token,true,[],"明确接收旧巡灯船使用权与跨区核查委托；本次结算第一校准及固定奖励。");
    }
}


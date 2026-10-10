using System.Collections.Immutable;
using Mist.Application.Storage;
using System.Text.Json;
using Mist.Application.Commands;
using Mist.Application.CombatSessions;
using Mist.Application.Contracts;
using Mist.Application.Inventory;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Finale;
using Mist.Core.Quests;

namespace Mist.Application.Finale;

public sealed partial class FinaleService
{
    public BattleDefinitions BattleDefinitions=>new(_catalog);
    public async ValueTask<FinaleEncounterState?> EncounterAsync(string id)=>BranchOperations.Read<FinaleEncounterState>(await _repository.SnapshotAsync(),"combat:"+id);
    public async ValueTask<CommandResult> BeginEncounterAsync(string id,string difficulty="standard",string[]? supplies=null)
    {
        var s=await _repository.SnapshotAsync();Safe(s);var st=State(s);Require(!Result(s,id),"该领域已完成，不能重打刷奖励或复制物资。");At(s,id switch{"ENC-RF-01"=>"RF-S03.coil","ENC-RF-02"=>"RF-S04.rook","ENC-RF-03"=>"RF-S05.relay","ENC-RF-04"=>"RF-S06.control",_=>throw new InvalidOperationException("终章独立领域未登记。")});
        switch(id)
        {
            case "ENC-RF-01":Require(Quest(s,"Q-F01") is{Lifecycle:QuestLifecycle.Active}&&st.ShaftHandle&&Sources(s,"RF-QF01-R-SHAFT","RF-QF01-R-COIL"),"基础手柄和明确SH-M对象来源未成立。");break;
            case "ENC-RF-02":Require(Closed(s,"Q-F01")&&st.ActualApproach is "formal" or "maintenance"&&Sources(s,"RF-QF02-R-COMMAND","RF-QF02-L-ROOK","RF-QF02-R-AUDIT"),"实际接管层抵达及局部两点指挥/独立核验来源未齐。");break;
            case "ENC-RF-03":Require(Closed(s,"Q-F02")&&st.CommandChainReleased&&Sources(s,"RF-QF03-R-RESTART","RF-QF03-R-ISOLATION"),"指挥链真实交接与本层资料未齐。");break;
            case "ENC-RF-04":Require(Closed(s,"Q-F02")&&st.CommandChainReleased&&LayerReady(st)&&st.MachineHallEntered&&Result(s,"ENC-RF-03")&&Sources(s,"RF-QF03-R-LINK","RF-QF03-R-RENEWAL","RF-QF03-R-DAMPING","RF-QF03-R-CLOCK","RF-QF03-L-OPERATOR"),"需要当前继电层、真实进入与三段各自的本地对象资料。");break;
        }
        var old=BranchOperations.Read<FinaleEncounterState>(s,"combat:"+id);Require(old is null||old.Battle.Outcome==CombatOutcome.Withdrawn,"请继续本次尝试，或先明确整体安全撤回入口。");var party=new PartyService(_repository,_catalog);var hero=party.Inspect(s);var equipped=s.Items.Where(i=>i.OwnerKind=="equipped"&&i.OwnerId=="hero").ToArray();var modules=s.Items.Where(i=>i.OwnerKind=="module_installed"&&equipped.Any(eq=>eq.Id==i.OwnerId)).Select(i=>party.Definitions.Modules[i.DefinitionId]);var roster=new Mist.Application.Roster.RosterService(_repository,_catalog);var helpers=roster.Helpers(s);
        var battle=BattleDefinitions.Create("BENCH-WORKSHOP",hero.Level,hero.Grade,hero.Build,hero.Weapon,hero.Armor,hero.Relics,modules,old?.Battle.Instance??Guid.NewGuid(),Guid.NewGuid(),difficulty,helpers.ToArray());
        foreach(var helper in battle.Units.Where(u=>u.Id.StartsWith("CP-",StringComparison.Ordinal)))
        {var member=BranchOperations.Read<Mist.Application.Roster.RosterEntry>(s,"roster:"+helper.Id)??throw new InvalidOperationException("实际同伴记录缺失。");helper.Variants=member.Tactics.ToDictionary();var relics=s.Items.Where(i=>i.OwnerKind=="equipped"&&i.OwnerId==helper.Id&&i.Slot is "relic1" or "relic2").Select(i=>i.DefinitionId).ToArray();helper.Relics=relics;helper.MaxFocus=Math.Min(14,helper.MaxFocus+(relics.Contains("RE-09")?1:0));helper.Focus=helper.MaxFocus;helper.ArmorBp=Math.Min(4500,helper.ArmorBp+(relics.Contains("RE-03")?300:0));helper.Initiative+=relics.Contains("RE-04")?2:0;helper.ExposureBonus=Math.Min(4000,helper.ExposureBonus+(relics.Contains("RE-07")?500:0));helper.GuardBase=Math.Min(3500,helper.GuardBase+(relics.Contains("RE-11")?500:0));}
        using var modes=JsonDocument.Parse(_catalog.Definitions["combat_difficulty"][0].PayloadJson);int hpBp=modes.RootElement.EnumerateArray().Single(m=>m.GetProperty("id").GetString()==difficulty).GetProperty("enemy_hp_bp").GetInt32();battle=FinaleEncounterFactory.Attach(battle,id,hpBp);
        supplies??=[];Require(supplies.Length<=2&&supplies.Distinct().Count()==supplies.Length,"只可携带最多两份实际独立补给。");foreach(string instance in supplies){var item=s.Items.SingleOrDefault(i=>i.Id==instance&&i.OwnerKind=="player"&&i.OwnerId=="hero"&&i.DefinitionId is "CON-01" or "CON-02" or "CON-03" or "CON-04")??throw new InvalidOperationException("入口没有实际持有该份补给。");battle.Supplies.Add(new(item.Id,item.DefinitionId));}
        var entry=StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(battle));var engine=new BattleEngine(BattleDefinitions,battle);engine.Start();return await Commit(s,"finale_encounter_entered",[BranchOperations.Change(s,"combat:"+id,"combat",new FinaleEncounterState(id,engine.Snapshot(),entry,Actor(s)))],new{encounter=id,instance=battle.Instance,attempt=battle.Attempt,actual_level=hero.Level,actual_grade=hero.Grade,numeric_version=FinaleEncounterFactory.NumericVersion,numeric_status=FinaleEncounterFactory.NumericStatus,kill_xp=0,core_internal_phases_same_attempt=true},"RF:"+id+":"+battle.Attempt+":entry");
    }
    public async ValueTask<EncounterActionPreview> PreviewEncounterActionAsync(string id,BattleRequest request)
    {
        var s=await _repository.SnapshotAsync();State(s);var old=BranchOperations.Read<FinaleEncounterState>(s,"combat:"+id)??throw new InvalidOperationException("尚未实际进入本领域。");Require(old.Encounter==id&&Actor(s).Scene==old.ReturnActor.Scene,"实际遭遇与角色场景不符。");
        if(request.Action=="CONTROL")Require(request.Device switch{"command_left" or "command_right"=>Sources(s,"RF-QF02-R-COMMAND"),"isolate_left" or "isolate_right" or "verify_link" or "reconnect_left"=>Sources(s,"RF-QF03-R-LINK"),"latch_bypass" or "verify_bypass" or "release_bypass"=>Sources(s,"RF-QF03-R-RENEWAL"),"set_damping" or "verify_damping" or "arm_controlled_stop" or "unset_damping"=>Sources(s,"RF-QF03-R-DAMPING"),_=>false},"该控制对象需要实际取得本地规程；不会根据英文词汇自动操作别的设备。");
        var engine=new BattleEngine(BattleDefinitions,old.Battle);var preview=engine.Preview(request);if(!preview.Legal)return new(Guid.Empty,preview,preview.Reason??"行动不合法。");engine.Apply(request);var battle=engine.Snapshot();bool controlled=old.ThreatControlled||battle.Outcome==CombatOutcome.Controlled;var changes=ImmutableArray.CreateBuilder<EntityChange>();var items=ImmutableArray.CreateBuilder<ItemChange>();string action="finale_combat_action",semantic="RF:"+id+":"+battle.Attempt+":"+request.Command.Value;object payload=new{request,outcome=battle.Outcome,kill_xp=0};
        foreach(var used in battle.Supplies.Where(i=>i.Used&&!old.Battle.Supplies.Single(x=>x.Instance==i.Instance).Used)){var item=s.Items.Single(i=>i.Id==used.Instance&&i.OwnerKind=="player"&&i.OwnerId=="hero");items.Add(new(item.Id,item.DefinitionId,item.OwnerKind,item.OwnerId,"consumed",battle.Attempt.ToString(),null));}
        if(controlled&&!old.ThreatControlled)
        {var f=battle.Finale;bool chain=id=="ENC-RF-02"&&f is{Kind:FinaleMechanismKind.RookCommand,CommandLeftClosed:true,CommandRightClosed:true};bool restart=id=="ENC-RF-04"&&f is{Kind:FinaleMechanismKind.CentralMachine,ControlledStop:true}&&f.VerifiedConfiguration==f.Configuration&&f.StopApproval==f.Configuration&&FinaleBattleRules.RelaysStopped(battle,f);Require(id!="ENC-RF-02"||chain,"指挥域不能绕过真实两点。");Require(id!="ENC-RF-04"||restart,"机组不能以进度归零跳过当前安全停止条件。");var result=new FinaleEncounterResult(id,true,battle.Instance,battle.Attempt,"actual_shared_combat",f?.Configuration??State(s).LayerRevision,chain,restart);changes.Add(BranchOperations.Change(s,"RF:result:"+id,"finale_encounter_result",result));payload=result;action="finale_encounter_result";semantic="RF:"+id+":result";}
        changes.Add(BranchOperations.Change(s,"combat:"+id,"combat",old with{Battle=battle,ThreatControlled=controlled}));var plan=BranchOperations.Plan(_repository,s,semantic,action,GateChanges(s,changes.ToImmutable()),Reads(s),payload,items:items.ToImmutable(),request:request.Command);Guid token=Guid.NewGuid();_previews[token]=plan;return new(token,preview,$"{request.Actor} · {request.Action} · AP {preview.Ap} / 专注 {preview.Focus}");
    }
    public async ValueTask<CommandResult> WithdrawEncounterAsync(string id)
    {var s=await _repository.SnapshotAsync();var old=BranchOperations.Read<FinaleEncounterState>(s,"combat:"+id)??throw new InvalidOperationException("独立领域未进入。");Require(!old.ThreatControlled&&!Result(s,id),"已可靠制止结果不能撤回复制物资。");var entry=StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(old.Entry));entry.Outcome=CombatOutcome.Withdrawn;entry.CurrentActor=null;var items=old.Battle.Supplies.Where(x=>x.Used).Select(x=>{var item=s.Items.Single(i=>i.Id==x.Instance&&i.OwnerKind=="consumed"&&i.OwnerId==old.Battle.Attempt.ToString());return new ItemChange(item.Id,item.DefinitionId,item.OwnerKind,item.OwnerId,"player","hero",null);}).ToImmutableArray();return await Commit(s,"finale_encounter_withdrawn",[BranchOperations.Change(s,"combat:"+id,"combat",old with{Battle=entry}),BranchOperations.Change(s,"actor","actor",old.ReturnActor)],new{encounter=id,whole_attempt_entry_restored=true,learning_sources_and_drafts_retained=true,external_frozen_preparation_unchanged=true},"RF:"+id+":"+old.Battle.Attempt+":withdraw",items);}
}

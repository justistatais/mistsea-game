using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.CombatSessions;
using Mist.Application.Contracts;
using Mist.Application.Inventory;
using Mist.Application.Storage;
using Mist.Core.ChapterTwo;
using Mist.Core.Combat;
using Mist.Core.Contracts;

namespace Mist.Application.ChapterTwo;

public sealed record ChapterEncounterResult(string Encounter,bool Completed,Guid Instance,Guid Attempt,string Method,long Configuration);
public sealed record ChapterEncounterState(string Encounter,BattleSnapshot Battle,BattleSnapshot Entry,
    Mist.Core.Devices.ActorState ReturnActor,bool ThreatControlled=false);
public sealed partial class ChapterTwoService
{
    public BattleDefinitions BattleDefinitions=>new(_catalog);
    private static string CombatId(string id)=>"combat:"+id;
    public async ValueTask<ChapterEncounterState?> EncounterAsync(string id)=>BranchOperations.Read<ChapterEncounterState>(await _repository.SnapshotAsync(),CombatId(id));
    public async ValueTask<CommandResult> ChooseCoreMethodAsync(string method)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R2-S07.core_entry");var st=State(s);Require(st.UpperSafe&&st.ConveyorIsolated&&st.BranchASeen&&st.BranchBSeen,"当前上层安全与两支路观察尚未成立。");Require(method is "sample" or "mechanical","核心处置方法未登记。");Require(!HasResult(s,"ENC-R2-03")&&BranchOperations.Read<ChapterEncounterState>(s,CombatId("ENC-R2-03")) is null,"已有核心尝试，不能用新方法重置处置进度。");
        return await Commit(s,"chapter_two_core_method_chosen",[Change(s,st with{CoreMethod=method,CoreA=false,CoreB=false,CoreValveA=false,CoreValveB=false,CoreSampleTaken=false,CoreWindingCleared=false})],new{method,selection_is_not_execution=true});
    }
    public async ValueTask<CommandResult> PerformCoreStepAsync(string step)
    {
        var s=await _repository.SnapshotAsync();Safe(s);var st=State(s);Require(st.UpperSafe&&st.ConveyorIsolated&&st.CoreMethod is "sample" or "mechanical","当前方法和隔离状态无效。");Require(!HasResult(s,"ENC-R2-03"),"核心结果已实际接受，不重演处置。");
        string spot=step is "secure_a" or "inspect_valve_a"?"R2-S07.branch_a":step is "secure_b" or "inspect_valve_b"?"R2-S07.branch_b":"R2-S07.core_entry";At(s,spot);
        var encounter=BranchOperations.Read<ChapterEncounterState>(s,CombatId("ENC-R2-03"));bool controlled=encounter is{ThreatControlled:true}&&encounter.Battle.Outcome==CombatOutcome.Controlled;
        ChapterTwoState next=step switch
        {"secure_a"when st.BranchASeen=>st with{CoreA=true},"secure_b"when st.BranchBSeen=>st with{CoreB=true},
         "take_sample"when st.CoreMethod=="sample"&&controlled&&st.CoreA&&st.CoreB=>st with{CoreSampleTaken=true},
         "clear_winding"when st.CoreMethod=="mechanical"&&controlled&&st.CoreA&&st.CoreB=>st with{CoreWindingCleared=true},
         "inspect_valve_a"when controlled&&(st.CoreSampleTaken||st.CoreWindingCleared)=>st with{CoreValveA=true},
         "inspect_valve_b"when controlled&&(st.CoreSampleTaken||st.CoreWindingCleared)=>st with{CoreValveB=true},_=>throw new InvalidOperationException("该核心现场步骤的前置动作/实际控制结果未成立。")};
        var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(Change(s,next));
        if(next.CoreValveA&&next.CoreValveB)
        {
            next=next with{CoreValvesChecked=true};changes[0]=Change(s,next);var result=new ChapterEncounterResult("ENC-R2-03",true,encounter!.Battle.Instance,encounter.Battle.Attempt,st.CoreMethod,st.ConveyorRevision);changes.Add(BranchOperations.Change(s,"R2:result:ENC-R2-03","chapter_encounter_result",result));return await Commit(s,"chapter_two_encounter_result",changes.ToImmutable(),result,"R2:ENC-R2-03:result");
        }
        return await Commit(s,"chapter_two_core_field_step",changes.ToImmutable(),new{step,method=st.CoreMethod,actual_stand=spot});
    }
    public async ValueTask<CommandResult> BeginEncounterAsync(string id,string difficulty="standard",string[]? supplies=null)
    {
        var s=await _repository.SnapshotAsync();Safe(s);var st=State(s);Require(st.InspectionScope&&!HasResult(s,id),"当前检修范围无效或该独立处置已经完成。");
        string spot=id switch{"ENC-R2-01"=>"R2-S05.root_route","ENC-R2-02"=>"R2-S06.conveyor","ENC-R2-03"=>"R2-S07.core_entry",_=>throw new InvalidOperationException("本章遭遇未登记。")};At(s,spot);
        if(id=="ENC-R2-02")Require(st.ConveyorIsolated,"先按地方范围实际隔离输送器。");if(id=="ENC-R2-03")Require(st.UpperSafe&&st.ConveyorIsolated&&st.BranchASeen&&st.BranchBSeen&&st.CoreA&&st.CoreB&&st.CoreMethod is "sample" or "mechanical","两支路固定和实际方法准备尚未成立。");
        var old=BranchOperations.Read<ChapterEncounterState>(s,CombatId(id));Require(old is null||old.Battle.Outcome==CombatOutcome.Withdrawn,"已有该领域尝试；请继续或按安全入口撤回。");
        var party=new PartyService(_repository,_catalog);var hero=party.Inspect(s);var equipped=s.Items.Where(i=>i.OwnerKind=="equipped"&&i.OwnerId=="hero").ToArray();var modules=s.Items.Where(i=>i.OwnerKind=="module_installed"&&equipped.Any(eq=>eq.Id==i.OwnerId)).Select(i=>party.Definitions.Modules[i.DefinitionId]);var roster=new Mist.Application.Roster.RosterService(_repository,_catalog);var helpers=roster.Helpers(s);
        // The existing factory supplies only actual hero/equipment/helper derivation; its benchmark enemies are replaced before entry.
        var battle=BattleDefinitions.Create("BENCH-WORKSHOP",hero.Level,hero.Grade,hero.Build,hero.Weapon,hero.Armor,hero.Relics,modules,old?.Battle.Instance??Guid.NewGuid(),Guid.NewGuid(),difficulty,helpers.ToArray());battle.Definition=id;battle.Mechanism=null;battle.Controls.Clear();battle.Units.RemoveAll(u=>u.Side==CombatSide.Enemy);
        foreach(var helper in battle.Units.Where(u=>u.Id.StartsWith("CP-",StringComparison.Ordinal)))
        {
            var member=BranchOperations.Read<Mist.Application.Roster.RosterEntry>(s,"roster:"+helper.Id)??throw new InvalidOperationException("实际同伴身份未登记。");helper.Variants=member.Tactics.ToDictionary();
            var relics=s.Items.Where(i=>i.OwnerKind=="equipped"&&i.OwnerId==helper.Id&&i.Slot is "relic1" or "relic2").Select(i=>i.DefinitionId).ToArray();helper.Relics=relics;
            helper.MaxFocus=Math.Min(14,helper.MaxFocus+(relics.Contains("RE-09")?1:0));helper.Focus=helper.MaxFocus;helper.ArmorBp=Math.Min(4500,helper.ArmorBp+(relics.Contains("RE-03")?300:0));helper.Initiative+=relics.Contains("RE-04")?2:0;helper.ExposureBonus=Math.Min(4000,helper.ExposureBonus+(relics.Contains("RE-07")?500:0));helper.GuardBase=Math.Min(3500,helper.GuardBase+(relics.Contains("RE-11")?500:0));
        }
        int hp=id=="ENC-R2-01"?210:id=="ENC-R2-02"?180:440,armor=id=="ENC-R2-03"?1000:0,pressure=id=="ENC-R2-03"?8:3;
        EnemyPattern[] pattern=id switch
        {"ENC-R2-01"=>[new("根道追击",35,TargetLock.Unit,true,true)],
         "ENC-R2-02"=>[new("输送器冲击",38,TargetLock.Unit,true,true),new("准备下一次冲击",0,TargetLock.Unit,false,true),new("输送器重击",55,TargetLock.Unit,true,true)],
         _=>[new("缠根收紧",65,TargetLock.Unit,true,true),new("根道列扫",40,TargetLock.Column,true,true)]};
        battle.Units.Add(new(){Id="r2-threat",Name=id=="ENC-R2-03"?"转供站缠根核心":id=="ENC-R2-02"?"失控输送器":"温室根道生物",Side=CombatSide.Enemy,Slot=0,MaxHp=hp,Hp=hp,ArmorBp=armor,PressureThreshold=pressure,Boss=id=="ENC-R2-03",Initiative=12,Pattern=pattern});
        supplies??=[];Require(supplies.Length<=2&&supplies.Distinct().Count()==supplies.Length,"最多携带两份不同的实际增强补给。");foreach(var supplied in supplies){var item=s.Items.SingleOrDefault(i=>i.Id==supplied&&i.OwnerKind=="player"&&i.OwnerId=="hero"&&i.DefinitionId is "CON-01" or "CON-02" or "CON-03" or "CON-04")??throw new InvalidOperationException("增强补给没有实际持有。");battle.Supplies.Add(new(item.Id,item.DefinitionId));}
        var entry=StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(battle));var engine=new BattleEngine(BattleDefinitions,battle);engine.Start();return await Commit(s,"chapter_two_encounter_entered",[BranchOperations.Change(s,CombatId(id),"combat",new ChapterEncounterState(id,engine.Snapshot(),entry,Actor(s)))],new{encounter=id,instance=battle.Instance,attempt=battle.Attempt,actual_level=hero.Level,actual_grade=hero.Grade,numeric_status="original_unreviewed_human_pending",kill_xp=0},"R2:"+id+":"+battle.Attempt+":entry");
    }
    public async ValueTask<EncounterActionPreview> PreviewEncounterActionAsync(string id,BattleRequest request)
    {
        var s=await _repository.SnapshotAsync();var old=BranchOperations.Read<ChapterEncounterState>(s,CombatId(id))??throw new InvalidOperationException("独立遭遇尚未进入。");Require(old.Encounter==id&&State(s).InspectionScope,"当前遭遇身份或范围不符。");var engine=new BattleEngine(BattleDefinitions,old.Battle);var preview=engine.Preview(request);if(!preview.Legal)return new(Guid.Empty,preview,preview.Reason??"行动不可执行。");engine.Apply(request);var battle=engine.Snapshot();bool controlled=old.ThreatControlled||battle.Outcome==CombatOutcome.Controlled;var changes=ImmutableArray.CreateBuilder<EntityChange>();var items=ImmutableArray.CreateBuilder<ItemChange>();string action="chapter_two_combat_action";object payload=new{request,outcome=battle.Outcome,kill_xp=0};string key="R2:"+id+":"+battle.Attempt+":"+request.Command.Value;
        foreach(var used in battle.Supplies.Where(i=>i.Used&&!old.Battle.Supplies.Single(x=>x.Instance==i.Instance).Used)){var item=s.Items.Single(i=>i.Id==used.Instance&&i.OwnerKind=="player"&&i.OwnerId=="hero");items.Add(new(item.Id,item.DefinitionId,item.OwnerKind,item.OwnerId,"consumed",battle.Attempt.ToString(),null));}
        if(controlled&&!old.ThreatControlled&&id!="ENC-R2-03")
        {var r=new ChapterEncounterResult(id,true,battle.Instance,battle.Attempt,"shared_combat",State(s).ConveyorRevision);changes.Add(BranchOperations.Change(s,"R2:result:"+id,"chapter_encounter_result",r));if(id=="ENC-R2-02")changes.Add(Change(s,State(s) with{GuardSecured=true,JamReleased=true}));action="chapter_two_encounter_result";payload=r;key="R2:"+id+":result";}
        changes.Add(BranchOperations.Change(s,CombatId(id),"combat",old with{Battle=battle,ThreatControlled=controlled}));var command=BranchOperations.Plan(_repository,s,key,action,GateChanges(s,changes.ToImmutable()),Reads(s),payload,items:items.ToImmutable(),request:request.Command);Guid token=Guid.NewGuid();_previews[token]=command;return new(token,preview,$"{request.Actor} · {request.Action} · AP {preview.Ap} / 专注 {preview.Focus}");
    }
    public async ValueTask<CommandResult> WithdrawEncounterAsync(string id)
    {
        var s=await _repository.SnapshotAsync();var old=BranchOperations.Read<ChapterEncounterState>(s,CombatId(id))??throw new InvalidOperationException("独立遭遇尚未进入。");Require(!old.ThreatControlled&&!HasResult(s,id),"已实际控制的结果不可撤回刷资源。");var entry=StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(old.Entry));entry.Outcome=CombatOutcome.Withdrawn;entry.CurrentActor=null;var items=old.Battle.Supplies.Where(x=>x.Used).Select(x=>{var item=s.Items.Single(i=>i.Id==x.Instance&&i.OwnerKind=="consumed"&&i.OwnerId==old.Battle.Attempt.ToString());return new ItemChange(item.Id,item.DefinitionId,item.OwnerKind,item.OwnerId,"player","hero",null);}).ToImmutableArray();
        return await Commit(s,"chapter_two_encounter_withdrawn",[BranchOperations.Change(s,CombatId(id),"combat",old with{Battle=entry}),BranchOperations.Change(s,"actor","actor",old.ReturnActor)],new{encounter=id,materials_and_drafts_retained=true},"R2:"+id+":"+old.Battle.Attempt+":withdraw",items);
    }
}

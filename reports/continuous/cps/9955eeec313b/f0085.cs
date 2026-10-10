using Mist.Application.Commands;
using System.Collections.Immutable;
using Mist.Application.CombatSessions;
using Mist.Application.Contracts;
using Mist.Application.Inventory;
using Mist.Application.Storage;
using Mist.Core.ChapterFour;
using Mist.Core.Combat;
using Mist.Core.Contracts;

namespace Mist.Application.ChapterFour;

public sealed record GlasshavenEncounterResult(string Encounter,bool Completed,Guid Instance,Guid Attempt,string Method,long Configuration);
public sealed record GlasshavenEncounterState(string Encounter,BattleSnapshot Battle,BattleSnapshot Entry,Mist.Core.Devices.ActorState ReturnActor,bool ThreatControlled=false);
public sealed partial class ChapterFourService
{
    public BattleDefinitions BattleDefinitions=>new(_catalog);
    private static string CombatId(string id)=>"combat:"+id;
    public async ValueTask<GlasshavenEncounterState?> EncounterAsync(string id)=>BranchOperations.Read<GlasshavenEncounterState>(await _repository.SnapshotAsync(),CombatId(id));
    public async ValueTask<CommandResult> BeginEncounterAsync(string id,string difficulty="standard",string[]? supplies=null)
    {
        var s=await _repository.SnapshotAsync();Safe(s);var st=State(s);Require(!Result(s,id),"该独立领域已实际完成，不重复制止刷奖。");string spot=id switch{"ENC-R4-01"=>"R4-S05.debris","ENC-R4-02"=>"R4-S05.reserve_case","ENC-R4-03"=>"R4-S06.defence","ENC-R4-04"=>"R4-S07.machine",_=>throw new InvalidOperationException("学城遭遇未登记。")};At(s,spot);
        if(id is "ENC-R4-01" or "ENC-R4-02")Require(st.OldPermit&&st.OldIsolated,"旧厅实际许可与隔离未成立。");else if(id=="ENC-R4-03")Require(Closed(s,"Q-401")&&Closed(s,"Q-402"),"校准防卫在前两实际核验报告后受理。");else Require(Prepared(s,st)&&st.FieldEntryAuthorized,"执行机需要当前孤网准备与实际进入许可。");
        var old=BranchOperations.Read<GlasshavenEncounterState>(s,CombatId(id));Require(old is null||old.Battle.Outcome==CombatOutcome.Withdrawn,"已有本领域尝试，请继续或明确安全撤回入口。");var party=new PartyService(_repository,_catalog);var hero=party.Inspect(s);var equipped=s.Items.Where(i=>i.OwnerKind=="equipped"&&i.OwnerId=="hero").ToArray();var modules=s.Items.Where(i=>i.OwnerKind=="module_installed"&&equipped.Any(eq=>eq.Id==i.OwnerId)).Select(i=>party.Definitions.Modules[i.DefinitionId]);var roster=new Mist.Application.Roster.RosterService(_repository,_catalog);var helpers=roster.Helpers(s);
        // Reuse only actual party/equipment derivation. The factory benchmark enemies are removed before entry.
        var battle=BattleDefinitions.Create("BENCH-WORKSHOP",hero.Level,hero.Grade,hero.Build,hero.Weapon,hero.Armor,hero.Relics,modules,old?.Battle.Instance??Guid.NewGuid(),Guid.NewGuid(),difficulty,helpers.ToArray());battle.Definition=id;battle.Mechanism=null;battle.Controls.Clear();battle.Units.RemoveAll(u=>u.Side==CombatSide.Enemy);
        foreach(var helper in battle.Units.Where(u=>u.Id.StartsWith("CP-",StringComparison.Ordinal)))
        {
            var member=BranchOperations.Read<Mist.Application.Roster.RosterEntry>(s,"roster:"+helper.Id)??throw new InvalidOperationException("实际同伴记录缺失。");helper.Variants=member.Tactics.ToDictionary();var relics=s.Items.Where(i=>i.OwnerKind=="equipped"&&i.OwnerId==helper.Id&&i.Slot is "relic1" or "relic2").Select(i=>i.DefinitionId).ToArray();helper.Relics=relics;helper.MaxFocus=Math.Min(14,helper.MaxFocus+(relics.Contains("RE-09")?1:0));helper.Focus=helper.MaxFocus;helper.ArmorBp=Math.Min(4500,helper.ArmorBp+(relics.Contains("RE-03")?300:0));helper.Initiative+=relics.Contains("RE-04")?2:0;helper.ExposureBonus=Math.Min(4000,helper.ExposureBonus+(relics.Contains("RE-07")?500:0));helper.GuardBase=Math.Min(3500,helper.GuardBase+(relics.Contains("RE-11")?500:0));
        }
        int hp=id switch{"ENC-R4-01"=>320,"ENC-R4-02"=>340,"ENC-R4-03"=>360,_=>760};EnemyPattern[] pattern=id switch{"ENC-R4-01"=>[new("试验残件冲击",60,TargetLock.Unit,true,true),new("残件准备",0,TargetLock.Unit,false,true),new("残件重击",90,TargetLock.Unit,true,true)],"ENC-R4-02"=>[new("备用段冲击",65,TargetLock.Unit,true,true),new("线圈列扫",40,TargetLock.Column,true,true)],"ENC-R4-03"=>[new("校准场击",60,TargetLock.Unit,true,true),new("防卫准备",0,TargetLock.Unit,false,true),new("防卫脉冲",75,TargetLock.Unit,true,true)],_=>[new("执行机冲击",110,TargetLock.Unit,true,true),new("执行机列扫",65,TargetLock.Column,true,true)]};battle.Units.Add(new(){Id="r4-threat",Name=id switch{"ENC-R4-01"=>"试验残件","ENC-R4-02"=>"备用段维护机","ENC-R4-03"=>"校准防卫",_=>"试验执行机"},Side=CombatSide.Enemy,Slot=0,MaxHp=hp,Hp=hp,ArmorBp=id=="ENC-R4-04"?1500:0,PressureThreshold=id=="ENC-R4-04"?8:3,Boss=id=="ENC-R4-04",Initiative=12,Pattern=pattern});
        supplies??=[];Require(supplies.Length<=2&&supplies.Distinct().Count()==supplies.Length,"最多两份实际不同补给。");foreach(string supplied in supplies){var item=s.Items.SingleOrDefault(i=>i.Id==supplied&&i.OwnerKind=="player"&&i.OwnerId=="hero"&&i.DefinitionId is "CON-01" or "CON-02" or "CON-03" or "CON-04")??throw new InvalidOperationException("补给没有实际持有。");battle.Supplies.Add(new(item.Id,item.DefinitionId));}var entry=StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(battle));var engine=new BattleEngine(BattleDefinitions,battle);engine.Start();return await Commit(s,"glasshaven_encounter_entered",[BranchOperations.Change(s,CombatId(id),"combat",new GlasshavenEncounterState(id,engine.Snapshot(),entry,Actor(s)))],new{encounter=id,instance=battle.Instance,attempt=battle.Attempt,actual_level=hero.Level,actual_grade=hero.Grade,numeric_status="original_unreviewed_human_pending",kill_xp=0},"R4:"+id+":"+battle.Attempt+":entry");
    }
    public async ValueTask<EncounterActionPreview> PreviewEncounterActionAsync(string id,BattleRequest request)
    {
        var s=await _repository.SnapshotAsync();var old=BranchOperations.Read<GlasshavenEncounterState>(s,CombatId(id))??throw new InvalidOperationException("独立遭遇未进入。");Require(old.Encounter==id&&State(s).OldPermit,"当前遭遇身份/范围不符。");var engine=new BattleEngine(BattleDefinitions,old.Battle);var preview=engine.Preview(request);if(!preview.Legal)return new(Guid.Empty,preview,preview.Reason??"行动不合法。");engine.Apply(request);var battle=engine.Snapshot();bool controlled=old.ThreatControlled||battle.Outcome==CombatOutcome.Controlled;var changes=ImmutableArray.CreateBuilder<EntityChange>();var items=ImmutableArray.CreateBuilder<ItemChange>();string action="glasshaven_combat_action",key="R4:"+id+":"+battle.Attempt+":"+request.Command.Value;object payload=new{request,outcome=battle.Outcome,kill_xp=0};foreach(var used in battle.Supplies.Where(i=>i.Used&&!old.Battle.Supplies.Single(x=>x.Instance==i.Instance).Used)){var item=s.Items.Single(i=>i.Id==used.Instance&&i.OwnerKind=="player"&&i.OwnerId=="hero");items.Add(new(item.Id,item.DefinitionId,item.OwnerKind,item.OwnerId,"consumed",battle.Attempt.ToString(),null));}
        if(controlled&&!old.ThreatControlled&&id!="ENC-R4-04"){var result=new GlasshavenEncounterResult(id,true,battle.Instance,battle.Attempt,"actual_shared_combat",id is "ENC-R4-01" or "ENC-R4-02"?State(s).OldRevision:State(s).InstrumentRevision);changes.Add(BranchOperations.Change(s,"R4:result:"+id,"chapter_encounter_result",result));action="glasshaven_encounter_result";payload=result;key="R4:"+id+":result";}
        changes.Add(BranchOperations.Change(s,CombatId(id),"combat",old with{Battle=battle,ThreatControlled=controlled}));var command=BranchOperations.Plan(_repository,s,key,action,GateChanges(s,changes.ToImmutable()),Reads(s),payload,items:items.ToImmutable(),request:request.Command);Guid token=Guid.NewGuid();_previews[token]=command;return new(token,preview,$"{request.Actor} · {request.Action} · AP {preview.Ap} / 专注 {preview.Focus}");
    }
    public async ValueTask<CommandResult> WithdrawEncounterAsync(string id)
    {
        var s=await _repository.SnapshotAsync();var old=BranchOperations.Read<GlasshavenEncounterState>(s,CombatId(id))??throw new InvalidOperationException("独立遭遇未进入。");Require(!old.ThreatControlled&&!Result(s,id),"已实际制止的结果不能撤回复制资源。");var entry=StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(old.Entry));entry.Outcome=CombatOutcome.Withdrawn;entry.CurrentActor=null;var items=old.Battle.Supplies.Where(x=>x.Used).Select(x=>{var item=s.Items.Single(i=>i.Id==x.Instance&&i.OwnerKind=="consumed"&&i.OwnerId==old.Battle.Attempt.ToString());return new ItemChange(item.Id,item.DefinitionId,item.OwnerKind,item.OwnerId,"player","hero",null);}).ToImmutableArray();return await Commit(s,"glasshaven_encounter_withdrawn",[BranchOperations.Change(s,CombatId(id),"combat",old with{Battle=entry}),BranchOperations.Change(s,"actor","actor",old.ReturnActor)],new{encounter=id,historical_experiments_and_sources_retained=true},"R4:"+id+":"+old.Battle.Attempt+":withdraw",items);
    }
}

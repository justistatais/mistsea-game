using System.Collections.Immutable;
using System.Text.Json;
using Mist.Application.Commands;
using Mist.Application.CombatSessions;
using Mist.Application.Contracts;
using Mist.Application.Inventory;
using Mist.Application.Storage;
using Mist.Core.ChapterThree;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Quests;

namespace Mist.Application.ChapterThree;

public sealed record MiningEncounterState(string Encounter,BattleSnapshot Battle,BattleSnapshot Entry,
    Mist.Core.Devices.ActorState ReturnActor,bool ThreatControlled=false);
public sealed partial class ChapterThreeService
{
    public BattleDefinitions BattleDefinitions=>new(_catalog);
    private static string CombatId(string id)=>"combat:"+id;
    public async ValueTask<MiningEncounterState?> EncounterAsync(string id)=>BranchOperations.Read<MiningEncounterState>(await _repository.SnapshotAsync(),CombatId(id));
    public async ValueTask<CommandResult> PerformInstrumentStepAsync(string step)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R3-S07.instrument_guard");var st=State(s);Require(st.Commission&&st.MineRouteSafe&&Sources(s,"R3-Q302-R-RAW")&&!HasResult(s,"ENC-R3-03"),"实际仪表现场、当前安全与原始隔离规则未取得。");
        var old=BranchOperations.Read<InstrumentMaintenance>(s,"R3:instrument_maintenance")??new(false,false,false);var next=step switch{"isolate"=>old with{Isolated=true},"brace"when old.Isolated=>old with{Braced=true},"inspect_zero_motion"when old.Isolated&&old.Braced=>old with{ZeroMotion=true},_=>throw new InvalidOperationException("须实际隔离、固定支架，再核对零能状态。")};
        var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(BranchOperations.Change(s,"R3:instrument_maintenance","instrument_maintenance",next));
        if(next.ZeroMotion){var r=new MiningEncounterResult("ENC-R3-03",true,Guid.NewGuid(),Guid.NewGuid(),"manual_isolation_brace_zero_motion",st.PumpRevision);changes.Add(BranchOperations.Change(s,"R3:result:ENC-R3-03","chapter_encounter_result",r));return await Commit(s,"chapter_three_encounter_result",changes.ToImmutable(),r,"R3:ENC-R3-03:result");}
        return await Commit(s,"chapter_three_instrument_step",changes.ToImmutable(),new{step,actual_stand="R3-S07.instrument_guard"});
    }
    private sealed record InstrumentMaintenance(bool Isolated,bool Braced,bool ZeroMotion);
    public async ValueTask<CommandResult> BeginEncounterAsync(string id,string difficulty="standard",string[]? supplies=null)
    {
        var s=await _repository.SnapshotAsync();Safe(s);var st=State(s);Require(st.Commission&&!HasResult(s,id),"地方工作范围无效或该独立结果已成立。");string spot=id switch{"ENC-R3-01"=>"R3-S05.rail_clear","ENC-R3-02"=>"R3-S06.pump_entrance","ENC-R3-03"=>"R3-S07.instrument_guard","ENC-R3-04"=>"R3-S05.escort","ENC-R3-05"=>"R3-S09.machine",_=>throw new InvalidOperationException("未注册可选ENC不推定战斗合同。")};At(s,spot);
        if(id=="ENC-R3-01")Require(st.LadderSafe&&st.BasicGear&&Sources(s,"R3-Q301-R-RAIL"),"实际轨道工作范围与独立梯道未成立。");
        if(id=="ENC-R3-02")Require(st.PumpIsolated&&Sources(s,"R3-Q301-R-PUMP"),"须先在现场隔离泵房。");
        if(id=="ENC-R3-03")Require(st.MineRouteSafe&&Sources(s,"R3-Q302-R-RAW"),"仪表支路须在当前安全排水后处理。");
        if(id=="ENC-R3-04")Require(st.LadderSafe&&BranchOperations.Read<Mist.Application.Travel.TransportSegment>(s,RescueSegment) is{CargoInTransit:true,Completed:false}&&new[]{"W1","W2","W3"}.All(w=>BranchOperations.Read<MiningWorker>(s,"R3:worker:"+w) is{Status:"escorted",Scene:"R3-S05"}),"须由实际救援组走到轨道返程护送点。");
        if(id=="ENC-R3-05")Require(Quest(s,"Q-304") is{Lifecycle:QuestLifecycle.Active}&&Closed(s,"Q-302")&&Closed(s,"Q-303")&&st.CurrentLoadAllowed&&st.FeedStopped&&st.ArmBraced&&Sources(s,"R3-Q304-R-SEQUENCE","R3-Q304-R-LOAD"),"正式塔站进场须核查与民生方案闭合、实际停送料和吊臂固定；压力不能代固定。");
        var old=BranchOperations.Read<MiningEncounterState>(s,CombatId(id));Require(old is null||old.Battle.Outcome==CombatOutcome.Withdrawn,"已有此领域尝试，请继续或撤回入口。");
        var party=new PartyService(_repository,_catalog);var hero=party.Inspect(s);Require(hero.Grade==1,"R3工作不得预支Q304第二次校准。");var equipped=s.Items.Where(i=>i.OwnerKind=="equipped"&&i.OwnerId=="hero").ToArray();var modules=s.Items.Where(i=>i.OwnerKind=="module_installed"&&equipped.Any(eq=>eq.Id==i.OwnerId)).Select(i=>party.Definitions.Modules[i.DefinitionId]);var roster=new Mist.Application.Roster.RosterService(_repository,_catalog);var helpers=roster.Helpers(s);
        var battle=BattleDefinitions.Create("BENCH-WORKSHOP",hero.Level,hero.Grade,hero.Build,hero.Weapon,hero.Armor,hero.Relics,modules,old?.Battle.Instance??Guid.NewGuid(),Guid.NewGuid(),difficulty,helpers.ToArray());battle.Definition=id;battle.Mechanism=null;battle.Controls.Clear();battle.Units.RemoveAll(u=>u.Side==CombatSide.Enemy);
        foreach(var helper in battle.Units.Where(u=>u.Id.StartsWith("CP-",StringComparison.Ordinal))){var member=BranchOperations.Read<Mist.Application.Roster.RosterEntry>(s,"roster:"+helper.Id)??throw new InvalidOperationException("实际同伴未登记。");Require(member.Membership==Mist.Application.Roster.Membership.Joined&&member.Availability==Mist.Application.Roster.ActorAvailability.ShipStandby&&!s.Entities.Any(e=>e.Id=="workpost:"+helper.Id&&e.Kind=="work_post"),"同伴正在实际工作，不得同时出战。");helper.Variants=member.Tactics.ToDictionary();var relics=s.Items.Where(i=>i.OwnerKind=="equipped"&&i.OwnerId==helper.Id&&i.Slot is "relic1" or "relic2").Select(i=>i.DefinitionId).ToArray();helper.Relics=relics;helper.MaxFocus=Math.Min(14,helper.MaxFocus+(relics.Contains("RE-09")?1:0));helper.Focus=helper.MaxFocus;helper.ArmorBp=Math.Min(4500,helper.ArmorBp+(relics.Contains("RE-03")?300:0));helper.Initiative+=relics.Contains("RE-04")?2:0;helper.ExposureBonus=Math.Min(4000,helper.ExposureBonus+(relics.Contains("RE-07")?500:0));helper.GuardBase=Math.Min(3500,helper.GuardBase+(relics.Contains("RE-11")?500:0));}
        using var modes=JsonDocument.Parse(_catalog.Definitions["combat_difficulty"][0].PayloadJson);int hpBp=modes.RootElement.EnumerateArray().Single(m=>m.GetProperty("id").GetString()==difficulty).GetProperty("enemy_hp_bp").GetInt32();
        int hp=(id=="ENC-R3-05"?620:id=="ENC-R3-03"?320:id=="ENC-R3-04"?260:210)*hpBp/10000;int armor=id=="ENC-R3-05"?1800:id=="ENC-R3-03"?1200:id=="ENC-R3-04"?500:0;int pressure=id=="ENC-R3-05"?8:id=="ENC-R3-03"?5:id=="ENC-R3-04"?4:3;
        EnemyPattern[] patterns=id switch
        {"ENC-R3-01"=>[new("食屑群攒动",42,TargetLock.Unit,true,true),new("轨道扫掠",26,TargetLock.Column,true,true)],
         "ENC-R3-02"=>[new("雾蚀刮擦",44,TargetLock.Unit,true,true),new("湿雾喷散",25,TargetLock.All,true,true)],
         "ENC-R3-03"=>[new("固守冲击",58,TargetLock.Unit,true,true),new("支架重击",68,TargetLock.Unit,true,true)],
         "ENC-R3-04"=>[new("一次明确增援信号",0,TargetLock.Unit,false,true,"r3-signal-aid"),new("护送拦阻",47,TargetLock.Unit,true,true)],
         _=>[new("开采机撞击",100,TargetLock.Unit,true,true),new("开采机全场冲击",45,TargetLock.All,true,true)]};
        battle.Units.Add(new(){Id="r3-threat",Name=id=="ENC-R3-05"?"失控开采机":id=="ENC-R3-04"?"返程信号兵":id=="ENC-R3-03"?"仪表固守机卫":id=="ENC-R3-02"?"泵房雾蚀生物":"轨道食屑群",Side=CombatSide.Enemy,Slot=0,MaxHp=hp,Hp=hp,ArmorBp=armor,PressureThreshold=pressure,Boss=id=="ENC-R3-05",Initiative=12,Pattern=patterns});
        if(id=="ENC-R3-04")battle.ReinforcementTemplates.Add("r3-signal-aid",new(){Id="r3-signal-aid",Name="一次地方机械支援",Side=CombatSide.Enemy,Slot=1,MaxHp=100*hpBp/10000,Hp=100*hpBp/10000,ArmorBp=0,PressureThreshold=3,Initiative=10,Pattern=[new("机械阻挡",28,TargetLock.Unit,true,true)]});
        supplies??=[];Require(supplies.Length<=2&&supplies.Distinct().Count()==supplies.Length,"最多两份不同实际增强补给。");foreach(string carried in supplies){var i=s.Items.SingleOrDefault(i=>i.Id==carried&&i.OwnerKind=="player"&&i.OwnerId=="hero"&&i.DefinitionId is "CON-01" or "CON-02" or "CON-03" or "CON-04")??throw new InvalidOperationException("增强补给未实际持有。");battle.Supplies.Add(new(i.Id,i.DefinitionId));}
        var entry=StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(battle));var engine=new BattleEngine(BattleDefinitions,battle);engine.Start();return await Commit(s,"chapter_three_encounter_entered",[BranchOperations.Change(s,CombatId(id),"combat",new MiningEncounterState(id,engine.Snapshot(),entry,Actor(s)))],new{encounter=id,instance=battle.Instance,attempt=battle.Attempt,actual_level=hero.Level,actual_grade=hero.Grade,numeric_status="original_unreviewed_human_pending",kill_xp=0},"R3:"+id+":"+battle.Attempt+":entry");
    }
    public async ValueTask<EncounterActionPreview> PreviewEncounterActionAsync(string id,BattleRequest request)
    {
        var s=await _repository.SnapshotAsync();var old=BranchOperations.Read<MiningEncounterState>(s,CombatId(id))??throw new InvalidOperationException("独立遭遇未进入。");Require(old.Encounter==id&&State(s).Commission&&Actor(s).Scene==old.ReturnActor.Scene&&Actor(s).Position==old.ReturnActor.Position,"当前遭遇身份、范围或实际入口改变。");var engine=new BattleEngine(BattleDefinitions,old.Battle);var preview=engine.Preview(request);if(!preview.Legal)return new(Guid.Empty,preview,preview.Reason??"行动不可执行。");engine.Apply(request);var battle=engine.Snapshot();bool controlled=old.ThreatControlled||battle.Outcome==CombatOutcome.Controlled;var changes=ImmutableArray.CreateBuilder<EntityChange>();var items=ImmutableArray.CreateBuilder<ItemChange>();string action="chapter_three_combat_action";object payload=new{request,outcome=battle.Outcome,kill_xp=0};string key="R3:"+id+":"+battle.Attempt+":"+request.Command.Value;
        foreach(var used in battle.Supplies.Where(i=>i.Used&&!old.Battle.Supplies.Single(x=>x.Instance==i.Instance).Used)){var item=s.Items.Single(i=>i.Id==used.Instance&&i.OwnerKind=="player"&&i.OwnerId=="hero");items.Add(new(item.Id,item.DefinitionId,item.OwnerKind,item.OwnerId,"consumed",battle.Attempt.ToString(),null));}
        if(controlled&&!old.ThreatControlled){if(id=="ENC-R3-05")Require(State(s).FeedStopped&&State(s).ArmBraced,"制止主机不会补齐未执行的停送料或吊臂固定。");var r=new MiningEncounterResult(id,true,battle.Instance,battle.Attempt,"shared_combat",State(s).Configuration);changes.Add(BranchOperations.Change(s,"R3:result:"+id,"chapter_encounter_result",r));action="chapter_three_encounter_result";payload=r;key="R3:"+id+":result";}
        changes.Add(BranchOperations.Change(s,CombatId(id),"combat",old with{Battle=battle,ThreatControlled=controlled}));var c=BranchOperations.Plan(_repository,s,key,action,GateChanges(s,changes.ToImmutable()),Reads(s),payload,items:items.ToImmutable(),request:request.Command);Guid token=Guid.NewGuid();_previews[token]=c;return new(token,preview,$"{request.Actor} · {request.Action} · AP {preview.Ap} / 专注 {preview.Focus}");
    }
    public async ValueTask<CommandResult> WithdrawEncounterAsync(string id)
    {
        var s=await _repository.SnapshotAsync();var old=BranchOperations.Read<MiningEncounterState>(s,CombatId(id))??throw new InvalidOperationException("独立遭遇未进入。");Require(!old.ThreatControlled&&!HasResult(s,id),"实际接受的结果不可撤回刷资源。");var entry=StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(old.Entry));entry.Outcome=CombatOutcome.Withdrawn;entry.CurrentActor=null;var items=old.Battle.Supplies.Where(x=>x.Used).Select(x=>{var i=s.Items.Single(i=>i.Id==x.Instance&&i.OwnerKind=="consumed"&&i.OwnerId==old.Battle.Attempt.ToString());return new ItemChange(i.Id,i.DefinitionId,i.OwnerKind,i.OwnerId,"player","hero",null);}).ToImmutableArray();_previews.Clear();
        return await Commit(s,"chapter_three_encounter_withdrawn",[BranchOperations.Change(s,CombatId(id),"combat",old with{Battle=entry}),BranchOperations.Change(s,"actor","actor",old.ReturnActor)],new{encounter=id,outside_state_and_drafts_retained=true},"R3:"+id+":"+old.Battle.Attempt+":withdraw",items);
    }
}


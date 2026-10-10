using System.Collections.Immutable;
using System.Text.Json;
using Mist.Application.Commands;
using Mist.Application.CombatSessions;
using Mist.Application.Contracts;
using Mist.Application.Inventory;
using Mist.Application.Storage;
using Mist.Core.ChapterSix;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Quests;

namespace Mist.Application.ChapterSix;

public sealed partial class ChapterSixService
{
    public BattleDefinitions BattleDefinitions=>new(_catalog);
    private static string CombatId(string id)=>"combat:"+id;
    public async ValueTask<FrontierEncounterState?> EncounterAsync(string id)=>BranchOperations.Read<FrontierEncounterState>(await _repository.SnapshotAsync(),CombatId(id));
    public async ValueTask<CommandResult> BeginEncounterAsync(string id,string difficulty="standard",string[]? supplies=null)
    {
        var s=await _repository.SnapshotAsync();Safe(s);Editable(s);var st=State(s);Require(st.Briefing&&!HasResult(s,id),"当前调查范围未成立或已有此独立结果。");string spot=id switch{"ENC-R6-01"=>"R6-S03.pass_entry","ENC-R6-02"=>"R6-S04.station_entry","ENC-R6-03"=>"R6-S05.first_entry","ENC-R6-04"=>"R6-S06.outer_entry","ENC-R6-05"=>"R6-S07.platform",_=>throw new InvalidOperationException("可选方法槽未完成注册，不冒充已制作遭遇。")};At(s,spot);
        if(id=="ENC-R6-01")Require(st.Corridor=="marked_leeward"&&st.GuideMarkers&&st.BasicGear,"当前预报/标记背风走廊/实际防护未共同成立。");
        if(id=="ENC-R6-02")Require(HasResult(s,"ENC-R6-01")&&Sources(s,"R6-Q601-R-INSTRUMENT"),"山口处置与测站原件尚未取得。");
        if(id=="ENC-R6-03")Require(Closed(s,"Q-601")&&st.CargoPlan is not null&&BranchOperations.Read<FrontierCargoState>(s,"R6:cargo") is{Stage:"inspected",ItemIds.Length:4},"首段需真实受检货组和明确段计划。");
        if(id=="ENC-R6-04")Require(st.CargoStage=="outer"&&BranchOperations.Read<Mist.Application.Travel.TransportSegment>(s,CargoSegment) is{CargoInTransit:true,Completed:false}&&BranchOperations.Read<FrontierCargoState>(s,"R6:cargo") is{Stage:"in_transit",Scene:"R6-S06"},"外线需实际沿配对路径抵达的当前货组。");
        if(id=="ENC-R6-05")Require(st.CargoStage=="ridge"&&st.FieldGuardControlled&&st.CargoProtected&&Sources(s,"R6-Q603-R-PLATFORM"),"须先实际制止护场器并保护本次货物；压力不代隔离。");
        var old=BranchOperations.Read<FrontierEncounterState>(s,CombatId(id));Require(old is null||old.Battle.Outcome==CombatOutcome.Withdrawn,"已有此领域尝试，请继续或撤回入口。");
        var party=new PartyService(_repository,_catalog);var hero=party.Inspect(s);Require(hero.Grade==3,"风暴边境不得预支Q604第四次校准。");var equipped=s.Items.Where(i=>i.OwnerKind=="equipped"&&i.OwnerId=="hero").ToArray();var modules=s.Items.Where(i=>i.OwnerKind=="module_installed"&&equipped.Any(eq=>eq.Id==i.OwnerId)).Select(i=>party.Definitions.Modules[i.DefinitionId]);var roster=new Mist.Application.Roster.RosterService(_repository,_catalog);var helpers=roster.Helpers(s);
        var battle=BattleDefinitions.Create("BENCH-WORKSHOP",hero.Level,hero.Grade,hero.Build,hero.Weapon,hero.Armor,hero.Relics,modules,old?.Battle.Instance??Guid.NewGuid(),Guid.NewGuid(),difficulty,helpers.ToArray());battle.Definition=id;battle.Mechanism=null;battle.Controls.Clear();battle.Units.RemoveAll(u=>u.Side==CombatSide.Enemy);
        foreach(var helper in battle.Units.Where(u=>u.Id.StartsWith("CP-",StringComparison.Ordinal))){var member=BranchOperations.Read<Mist.Application.Roster.RosterEntry>(s,"roster:"+helper.Id)??throw new InvalidOperationException("实际同伴未登记。");Require(member.Membership==Mist.Application.Roster.Membership.Joined&&member.Availability==Mist.Application.Roster.ActorAvailability.ShipStandby&&!s.Entities.Any(e=>e.Id=="workpost:"+helper.Id&&e.Kind=="work_post"),"同伴正在实际工作，不得同时出战。");helper.Variants=member.Tactics.ToDictionary();var relics=s.Items.Where(i=>i.OwnerKind=="equipped"&&i.OwnerId==helper.Id&&i.Slot is "relic1" or "relic2").Select(i=>i.DefinitionId).ToArray();helper.Relics=relics;helper.MaxFocus=Math.Min(14,helper.MaxFocus+(relics.Contains("RE-09")?1:0));helper.Focus=helper.MaxFocus;helper.ArmorBp=Math.Min(4500,helper.ArmorBp+(relics.Contains("RE-03")?300:0));helper.Initiative+=relics.Contains("RE-04")?2:0;helper.ExposureBonus=Math.Min(4000,helper.ExposureBonus+(relics.Contains("RE-07")?500:0));helper.GuardBase=Math.Min(3500,helper.GuardBase+(relics.Contains("RE-11")?500:0));}
        using var modes=JsonDocument.Parse(_catalog.Definitions["combat_difficulty"][0].PayloadJson);int hpBp=modes.RootElement.EnumerateArray().Single(m=>m.GetProperty("id").GetString()==difficulty).GetProperty("enemy_hp_bp").GetInt32();
        int hp=(id=="ENC-R6-05"?1100:id is "ENC-R6-03" or "ENC-R6-04"?480:360)*hpBp/10000;int armor=id=="ENC-R6-05"?2200:800;int pressure=id=="ENC-R6-05"?8:4;
        EnemyPattern[] patterns=id switch{"ENC-R6-01"=>[new("山口突袭",90,TargetLock.Unit,true,true),new("阵风扫击",45,TargetLock.Column,true,true)],"ENC-R6-02"=>[new("测站机卫冲击",100,TargetLock.Unit,true,true),new("机卫横扫",55,TargetLock.Column,true,true)],"ENC-R6-03" or "ENC-R6-04"=>[new("护送拦阻",105,TargetLock.Unit,true,true),new("外线冲击",50,TargetLock.All,true,true)],_=>[new("平台撞击",170,TargetLock.Unit,true,true),new("平台全场冲击",80,TargetLock.All,true,true)]};
        battle.Units.Add(new(){Id="r6-threat",Name=id=="ENC-R6-05"?"封锁平台主体":id=="ENC-R6-02"?"测站机卫":"当前线路阻断者",Side=CombatSide.Enemy,Slot=0,MaxHp=hp,Hp=hp,ArmorBp=armor,PressureThreshold=pressure,Boss=id=="ENC-R6-05",Initiative=14,Pattern=patterns});
        supplies??=[];Require(supplies.Length<=2&&supplies.Distinct().Count()==supplies.Length,"最多两份不同实际增强补给。");foreach(string carried in supplies){var i=s.Items.SingleOrDefault(i=>i.Id==carried&&i.OwnerKind=="player"&&i.OwnerId=="hero"&&i.DefinitionId is "CON-01" or "CON-02" or "CON-03" or "CON-04")??throw new InvalidOperationException("增强补给未实际持有。");battle.Supplies.Add(new(i.Id,i.DefinitionId));}
        var entry=StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(battle));var engine=new BattleEngine(BattleDefinitions,battle);engine.Start();return await Commit(s,"chapter_six_encounter_entered",[BranchOperations.Change(s,CombatId(id),"combat",new FrontierEncounterState(id,engine.Snapshot(),entry,Actor(s)))],new{encounter=id,instance=battle.Instance,attempt=battle.Attempt,actual_level=hero.Level,actual_grade=hero.Grade,numeric_status="original_unreviewed_human_pending",kill_xp=0},"R6:"+id+":"+battle.Attempt+":entry");
    }
    public async ValueTask<EncounterActionPreview> PreviewEncounterActionAsync(string id,BattleRequest request)
    {
        var s=await _repository.SnapshotAsync();var old=BranchOperations.Read<FrontierEncounterState>(s,CombatId(id))??throw new InvalidOperationException("独立遭遇未进入。");Require(old.Encounter==id&&State(s).Briefing&&Actor(s).Scene==old.ReturnActor.Scene&&Actor(s).Position==old.ReturnActor.Position,"当前遭遇身份、范围或实际入口改变。");var engine=new BattleEngine(BattleDefinitions,old.Battle);var preview=engine.Preview(request);if(!preview.Legal)return new(Guid.Empty,preview,preview.Reason??"行动不可执行。");engine.Apply(request);var battle=engine.Snapshot();bool controlled=old.ThreatControlled||battle.Outcome==CombatOutcome.Controlled;var changes=ImmutableArray.CreateBuilder<EntityChange>();var items=ImmutableArray.CreateBuilder<ItemChange>();string action="chapter_six_combat_action";object payload=new{request,outcome=battle.Outcome,kill_xp=0};string key="R6:"+id+":"+battle.Attempt+":"+request.Command.Value;
        foreach(var used in battle.Supplies.Where(i=>i.Used&&!old.Battle.Supplies.Single(x=>x.Instance==i.Instance).Used)){var item=s.Items.Single(i=>i.Id==used.Instance&&i.OwnerKind=="player"&&i.OwnerId=="hero");items.Add(new(item.Id,item.DefinitionId,item.OwnerKind,item.OwnerId,"consumed",battle.Attempt.ToString(),null));}
        if(controlled&&!old.ThreatControlled){if(id=="ENC-R6-05")Require(State(s).FieldGuardControlled&&State(s).CargoProtected,"制止平台不会补齐未执行的护场器和货物保护。");var r=new FrontierEncounterResult(id,true,battle.Instance,battle.Attempt,"shared_combat",State(s).TowerRevision);changes.Add(BranchOperations.Change(s,"R6:result:"+id,"chapter_encounter_result",r));action="chapter_six_encounter_result";payload=r;key="R6:"+id+":result";}
        changes.Add(BranchOperations.Change(s,CombatId(id),"combat",old with{Battle=battle,ThreatControlled=controlled}));var c=BranchOperations.Plan(_repository,s,key,action,GateChanges(s,changes.ToImmutable()),Reads(s),payload,items:items.ToImmutable(),request:request.Command);Guid token=Guid.NewGuid();_previews[token]=c;return new(token,preview,$"{request.Actor} · {request.Action} · AP {preview.Ap} / 专注 {preview.Focus}");
    }
    public async ValueTask<CommandResult> WithdrawEncounterAsync(string id)
    {
        var s=await _repository.SnapshotAsync();var old=BranchOperations.Read<FrontierEncounterState>(s,CombatId(id))??throw new InvalidOperationException("独立遭遇未进入。");Require(!old.ThreatControlled&&!HasResult(s,id),"实际接受的结果不可撤回刷资源。");var entry=StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(old.Entry));entry.Outcome=CombatOutcome.Withdrawn;entry.CurrentActor=null;var items=old.Battle.Supplies.Where(x=>x.Used).Select(x=>{var i=s.Items.Single(i=>i.Id==x.Instance&&i.OwnerKind=="consumed"&&i.OwnerId==old.Battle.Attempt.ToString());return new ItemChange(i.Id,i.DefinitionId,i.OwnerKind,i.OwnerId,"player","hero",null);}).ToImmutableArray();_previews.Clear();
        return await Commit(s,"chapter_six_encounter_withdrawn",[BranchOperations.Change(s,CombatId(id),"combat",old with{Battle=entry}),BranchOperations.Change(s,"actor","actor",old.ReturnActor)],new{encounter=id,outside_state_and_drafts_retained=true},"R6:"+id+":"+old.Battle.Attempt+":withdraw",items);
    }
}


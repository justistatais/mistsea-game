using System.Collections.Immutable;
using Mist.Application.CombatSessions;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Inventory;
using Mist.Application.Roster;
using Mist.Application.Storage;
using Mist.Core.Combat;
using Mist.Core.Contracts;

namespace Mist.Application.ChapterFive;

public sealed record OldCityEncounterResult(string Encounter,bool Completed,Guid Instance,Guid Attempt,string Method);
public sealed record OldCityEncounterState(string Encounter,BattleSnapshot Battle,BattleSnapshot Entry,Mist.Core.Devices.ActorState ReturnActor,bool ThreatControlled=false,int ProtectionRoundsApplied=0);
public sealed partial class ChapterFiveService
{
    public BattleDefinitions BattleDefinitions=>new(_catalog);
    private static string CombatId(string id)=>"combat:"+id;
    public async ValueTask<OldCityEncounterState?> EncounterAsync(string id)=>BranchOperations.Read<OldCityEncounterState>(await _repository.SnapshotAsync(),CombatId(id));
    public async ValueTask<CommandResult> BeginEncounterAsync(string id,string difficulty="standard",string[]? supplies=null)
    {
        var s=await _repository.SnapshotAsync();Safe(s);Require(!Result(s,id),"该实际领域已经完成，不重复制服刷奖。");var st=State(s);string spot=id switch{"ENC-R5-01"=>"R5-S03.checkpoint_guard","ENC-R5-02"=>"R5-S04.hazard","ENC-R5-03"=>"R5-S06.guard","ENC-R5-04"=>"R5-S08.relay",_=>throw new InvalidOperationException("旧都遭遇未登记。")};At(s,spot);
        if(id=="ENC-R5-01")Require(st.Landing&&st.MapCompared&&st.BasicKit&&Sources(s,"R5-Q501-R-CREDENTIAL"),"查验冲突须当前委托、地图与公开核查规程。");if(id=="ENC-R5-02")Require(st.TunnelIsolated&&st.TunnelBraced&&Sources(s,"R5-Q501-R-TUNNEL"),"隧道须先真实隔离并固定。");if(id=="ENC-R5-03")Require(Closed(s,"Q-501")&&st.RescueScope&&st.DoorDriveIsolated&&st.DutyObstructionCleared&&Sources(s,"R5-Q502-R-RESCUE"),"救援入口、限定许可、门控隔离与看守规程不足。");if(id=="ENC-R5-04")Require(Closed(s,"Q-503")&&st.JointPermission&&st.ArchiveRelayIsolated&&st.ArchiveRackSecured&&Sources(s,"R5-Q504-R-TOWER"),"归档塔共同许可、隔离和机架固定不足。");
        var old=BranchOperations.Read<OldCityEncounterState>(s,CombatId(id));Require(old is null||old.Battle.Outcome==CombatOutcome.Withdrawn,"已有本领域尝试，请继续或明确安全撤回入口。");var party=new PartyService(_repository,_catalog);var hero=party.Inspect(s);var equipped=s.Items.Where(i=>i.OwnerKind=="equipped"&&i.OwnerId=="hero").ToArray();var modules=s.Items.Where(i=>i.OwnerKind=="module_installed"&&equipped.Any(eq=>eq.Id==i.OwnerId)).Select(i=>party.Definitions.Modules[i.DefinitionId]);var helpers=new RosterService(_repository,_catalog).Helpers(s);
        // Only the shared ally/equipment derivation is reused; benchmark enemies are removed.
        var battle=BattleDefinitions.Create("BENCH-WORKSHOP",hero.Level,hero.Grade,hero.Build,hero.Weapon,hero.Armor,hero.Relics,modules,old?.Battle.Instance??Guid.NewGuid(),Guid.NewGuid(),difficulty,helpers.ToArray());battle.Definition=id;battle.Mechanism=null;battle.Controls.Clear();battle.Units.RemoveAll(u=>u.Side==CombatSide.Enemy);
        foreach(var helper in battle.Units.Where(u=>u.Id.StartsWith("CP-",StringComparison.Ordinal)))
        {
            var member=BranchOperations.Read<RosterEntry>(s,"roster:"+helper.Id)??throw new InvalidOperationException("实际同伴名册缺失。");helper.Variants=member.Tactics.ToDictionary();var relics=s.Items.Where(i=>i.OwnerKind=="equipped"&&i.OwnerId==helper.Id&&i.Slot is "relic1" or "relic2").Select(i=>i.DefinitionId).ToArray();helper.Relics=relics;helper.MaxFocus=Math.Min(14,helper.MaxFocus+(relics.Contains("RE-09")?1:0));helper.Focus=helper.MaxFocus;helper.ArmorBp=Math.Min(4500,helper.ArmorBp+(relics.Contains("RE-03")?300:0));helper.Initiative+=relics.Contains("RE-04")?2:0;helper.ExposureBonus=Math.Min(4000,helper.ExposureBonus+(relics.Contains("RE-07")?500:0));helper.GuardBase=Math.Min(3500,helper.GuardBase+(relics.Contains("RE-11")?500:0));
        }
        using var modes=System.Text.Json.JsonDocument.Parse(_catalog.Definitions["combat_difficulty"][0].PayloadJson);int hpBp=modes.RootElement.EnumerateArray().Single(m=>m.GetProperty("id").GetString()==difficulty).GetProperty("enemy_hp_bp").GetInt32();
        BattleUnit Enemy(string name,string eid,int slot,int hp,int threshold,bool boss,int damage)=>new(){Id=eid,Name=name,Side=CombatSide.Enemy,Slot=slot,MaxHp=hp*hpBp/10000,Hp=hp*hpBp/10000,ArmorBp=0,PressureThreshold=threshold,Boss=boss,Initiative=12,Pattern=[new(name+"准备",0,TargetLock.Unit,false,true),new(name+"冲击",damage,TargetLock.Unit,true,true)]};
        if(id=="ENC-R5-03")
        {
            battle.Units.Add(Enemy("北站指挥者","r5-commander",2,650,8,true,120));battle.Units.Add(Enemy("北站护卫一","r5-guard-1",0,180,4,false,55));battle.Units.Add(Enemy("北站护卫二","r5-guard-2",1,180,4,false,55));
        }
        else battle.Units.Add(Enemy(id=="ENC-R5-01"?"北站核查护卫":id=="ENC-R5-02"?"隧道食屑群":"归档护场器","r5-local-threat",0,id=="ENC-R5-01"?340:id=="ENC-R5-02"?280:380,id=="ENC-R5-01"?5:id=="ENC-R5-02"?3:4,false,id=="ENC-R5-04"?80:65));
        supplies??=[];Require(supplies.Length<=2&&supplies.Distinct().Count()==supplies.Length,"最多两件不同真实补给。");foreach(string supplied in supplies){var item=s.Items.SingleOrDefault(i=>i.Id==supplied&&i.OwnerKind=="player"&&i.OwnerId=="hero"&&i.DefinitionId is "CON-01" or "CON-02" or "CON-03" or "CON-04")??throw new InvalidOperationException("补给没有实际持有。");battle.Supplies.Add(new(item.Id,item.DefinitionId));}
        var entry=StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(battle));var engine=new BattleEngine(BattleDefinitions,battle);engine.Start();var started=engine.Snapshot();int protection=ApplyFiniteGuardProtection(id,started,0);return await Commit(s,"old_city_encounter_entered",[BranchOperations.Change(s,CombatId(id),"combat",new OldCityEncounterState(id,started,entry,Actor(s),false,protection))],new{encounter=id,instance=battle.Instance,attempt=battle.Attempt,actual_level=hero.Level,actual_grade=hero.Grade,numeric_status="original_unreviewed_human_pending",kill_xp=0},"R5:"+id+":"+battle.Attempt+":entry");
    }
    // Explicit finite local candidate: one surviving guard supplies 20% protection per round,
    // at most the first three rounds; the source's personal-turn expiry advances normally.
    private static int ApplyFiniteGuardProtection(string id,BattleSnapshot battle,int applied)
    {
        if(id!="ENC-R5-03"||battle.Round<=applied||battle.Round>3||battle.Outcome!=CombatOutcome.Active)return applied;var commander=battle.Units.Single(u=>u.Id=="r5-commander");var guard=battle.Units.Where(u=>u.Id is "r5-guard-1" or "r5-guard-2"&&u.Alive).OrderBy(u=>u.Id).ToArray();if(commander.Alive&&guard.Length>0){var source=guard[(battle.Round-1)%guard.Length];commander.Guards.RemoveAll(g=>g.Id=="r5-finite-rotation");commander.Guards.Add(new("r5-finite-rotation",2000,new(source.Id,source.PersonalTurn+1)));battle.Events.Add(new(battle.Round,"finite_guard_protection",source.Id,commander.Id,2000,"at most rounds 1-3; no respawn"));}return battle.Round;
    }
    public async ValueTask<EncounterActionPreview> PreviewEncounterActionAsync(string id,BattleRequest request)
    {
        var s=await _repository.SnapshotAsync();var old=BranchOperations.Read<OldCityEncounterState>(s,CombatId(id))??throw new InvalidOperationException("独立遭遇未进入。");Require(old.Encounter==id&&Actor(s).Scene==old.ReturnActor.Scene&&Actor(s).Position==old.ReturnActor.Position,"当前遭遇身份或实际入口改变。");var engine=new BattleEngine(BattleDefinitions,old.Battle);var preview=engine.Preview(request);if(!preview.Legal)return new(Guid.Empty,preview,preview.Reason??"行动不合法。");engine.Apply(request);var battle=engine.Snapshot();int protection=ApplyFiniteGuardProtection(id,battle,old.ProtectionRoundsApplied);bool controlled=old.ThreatControlled||battle.Outcome==CombatOutcome.Controlled;var changes=ImmutableArray.CreateBuilder<EntityChange>();var items=ImmutableArray.CreateBuilder<ItemChange>();string action="old_city_combat_action",key="R5:"+id+":"+battle.Attempt+":"+request.Command.Value;object payload=new{request,outcome=battle.Outcome,kill_xp=0};
        foreach(var used in battle.Supplies.Where(i=>i.Used&&!old.Battle.Supplies.Single(x=>x.Instance==i.Instance).Used)){var item=s.Items.Single(i=>i.Id==used.Instance&&i.OwnerKind=="player"&&i.OwnerId=="hero");items.Add(new(item.Id,item.DefinitionId,item.OwnerKind,item.OwnerId,"consumed",battle.Attempt.ToString(),null));}
        if(controlled&&!old.ThreatControlled){var result=new OldCityEncounterResult(id,true,battle.Instance,battle.Attempt,"actual_shared_nonlethal_combat");changes.Add(BranchOperations.Change(s,"R5:result:"+id,"chapter_encounter_result",result));action="old_city_encounter_result";payload=result;key="R5:"+id+":result";}
        changes.Add(BranchOperations.Change(s,CombatId(id),"combat",old with{Battle=battle,ThreatControlled=controlled,ProtectionRoundsApplied=protection}));var command=BranchOperations.Plan(_repository,s,key,action,GateChanges(s,changes.ToImmutable()),Reads(s),payload,items:items.ToImmutable(),request:request.Command);Guid token=Guid.NewGuid();_previews[token]=command;return new(token,preview,$"{request.Actor} · {request.Action} · AP {preview.Ap} / 专注 {preview.Focus}");
    }
    public async ValueTask<CommandResult> WithdrawEncounterAsync(string id)
    {
        var s=await _repository.SnapshotAsync();var old=BranchOperations.Read<OldCityEncounterState>(s,CombatId(id))??throw new InvalidOperationException("独立遭遇未进入。");Require(!old.ThreatControlled&&!Result(s,id),"实际已制止的领域不能撤回刷资源。");var entry=StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(old.Entry));entry.Outcome=CombatOutcome.Withdrawn;entry.CurrentActor=null;var items=old.Battle.Supplies.Where(x=>x.Used).Select(x=>{var item=s.Items.Single(i=>i.Id==x.Instance&&i.OwnerKind=="consumed"&&i.OwnerId==old.Battle.Attempt.ToString());return new ItemChange(item.Id,item.DefinitionId,item.OwnerKind,item.OwnerId,"player","hero",null);}).ToImmutableArray();return await Commit(s,"old_city_encounter_withdrawn",[BranchOperations.Change(s,CombatId(id),"combat",old with{Battle=entry,ProtectionRoundsApplied=0}),BranchOperations.Change(s,"actor","actor",old.ReturnActor)],new{encounter=id,mara_not_killed_by_failure=true,documents_and_drafts_retained=true},"R5:"+id+":"+old.Battle.Attempt+":withdraw",items);
    }
}

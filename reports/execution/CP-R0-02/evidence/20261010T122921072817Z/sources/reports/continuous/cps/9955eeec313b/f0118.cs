using System.Collections.Immutable;
using System.Text.Json;
using Mist.Application.Commands;
using Mist.Application.CombatSessions;
using Mist.Application.Contracts;
using Mist.Application.EventScheduler;
using Mist.Application.Inventory;
using Mist.Application.Roster;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Core.Campaign;
using Mist.Core.ChapterOne;
using Mist.Core.Combat;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.FirstApproaches;
using Mist.Core.Quests;

namespace Mist.Application.FirstApproaches;

public sealed record ApproachSourceAccess(string Id,string Version,string Fingerprint,string ActualSpot);
public sealed class FirstApproachService
{
    private readonly IAdventureRepository _repository;
    private readonly IEventLeaseStore _leases;
    private readonly RuntimeCatalog _catalog;
    private readonly Dictionary<Guid,CommitPlanCommand> _previews=new();
    public FirstApproachPackage Package {get;}
    public ImmutableDictionary<string,SceneGeometry> Geometry {get;}
    public BattleDefinitions BattleDefinitions {get;}
    public FirstApproachService(IAdventureRepository repository,IEventLeaseStore leases,RuntimeCatalog catalog,FirstApproachPackage package,ImmutableDictionary<string,SceneGeometry> geometry)
    {FirstApproachRules.Validate(package);_repository=repository;_leases=leases;_catalog=catalog;Package=package;Geometry=FirstApproachRules.ExtendGeometry(package,geometry);BattleDefinitions=new(catalog);}
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static ActorState Actor(BranchSnapshot s)=>BranchOperations.Read<ActorState>(s,"actor")??throw new InvalidOperationException("当前现场尚未加载。");
    private static QuestState Quest(BranchSnapshot s,string id)=>BranchOperations.Read<QuestState>(s,"quest:"+id)??throw new InvalidOperationException("实际父任务不存在。");
    private static string CombatId(string id)=>"combat:"+id;
    private static string ResultId(string id)=>"first-approach:result:"+id;
    private static string SourceHash(ApproachMaterial m)=>Mist.Core.Assets.AssetText.Hash(string.Join("\n\n",m.Paragraphs.Select(p=>p.En)));
    private static bool Allowed(BranchSnapshot s,string id){var e=BranchOperations.Entity(s,id);if(e is null)return false;using var d=JsonDocument.Parse(e.Json);return d.RootElement.GetProperty("allowed").GetBoolean();}
    private static void Safe(BranchSnapshot s){BranchOperations.Safe(s);Require(!s.Entities.Where(e=>e.Kind=="event").Select(e=>StrictJson.Deserialize<EventSession>(e.Json)).Any(e=>e.State==EventLifecycle.Running),"先交接当前前景事件。");Require(!s.Entities.Where(e=>e.Kind=="transport_segment").Select(e=>StrictJson.Deserialize<TransportSegment>(e.Json)).Any(t=>t.CargoInTransit&&!t.Completed),"当前运输尚未实际交接。");}
    private void At(BranchSnapshot s,string spot){var a=Actor(s);Require(Geometry.TryGetValue(a.Scene,out var g)&&g.Spots.Any(p=>p.Id==spot&&p.Stand.Distance(a.Position)<=2.5)&&new NavigationGrid(g).CanStand(a.Position,true),"请先走到当前实际安全处置站位。");}
    private void Scope(BranchSnapshot s,string id,bool active=true)
    {
        var d=Package.Encounters.Single(e=>e.Id==id);var q=Quest(s,d.Quest);Require(!active||q.Lifecycle==QuestLifecycle.Active,"当前父任务未开启或已经完成。");
        if(id==FirstApproachRules.Relay)
            Require(BranchOperations.Read<CampaignState>(s,"campaign")is{Scope:CampaignRules.Scope}&&BranchOperations.Read<PrologueState>(s,"prologue")is{ToolsAcquired:true,SurvivorsRegistered:true,RelayEntranceFound:true}&&s.Receipts.Any(r=>r.SemanticKey=="Q-P02:resolution"&&r.ActionType=="rescue_message_sent"),"须实际短距救援发送、人员入棚、检修入口和工具，不能裸演员进入中继防卫。");
        else Require(Allowed(s,"permission:Q-104")&&s.Receipts.Any(r=>r.SemanticKey=="Q104:work:accepted"&&r.ActionType=="accept_tidegate_site_work")&&BranchOperations.Read<TidegateLocalState>(s,"tower:local")is{Prepared:true},"当前塔站现场许可与实际本地供给检查未成立。");
    }
    private bool Acquired(BranchSnapshot s,ApproachMaterial m)=>BranchOperations.Read<ApproachSourceAccess>(s,"material:"+m.Id)is{}a&&a.Id==m.Id&&a.Version==m.Version&&a.Fingerprint==SourceHash(m);
    private void Source(BranchSnapshot s,string id){var d=Package.Encounters.Single(e=>e.Id==id);Require(Acquired(s,Package.Materials.Single(m=>m.Id==d.SourceMaterial)),"先在现场取得本次完整处置说明。");}
    private static bool Accepted(BranchSnapshot s,string id)
    {
        var result=BranchOperations.Read<FirstApproachResult>(s,ResultId(id));if(result is null||!result.Completed||result.Encounter!=id||result.SourceVersion!=FirstApproachRules.Version||result.Instance==Guid.Empty||result.Attempt==Guid.Empty||result.Quest!=(id==FirstApproachRules.Relay?"Q-P03":"Q-104")||result.QuestInstance!=Quest(s,result.Quest).Instance)return false;
        var receipt=s.Receipts.SingleOrDefault(r=>r.SemanticKey=="first-approach:"+id+":result"&&r.ActionType=="first_approach_result");if(receipt is null||StrictJson.Deserialize<FirstApproachResult>(receipt.ResultJson)!=result)return false;
        if(result.Method=="manual_isolate_brace_zero_energy")return id==FirstApproachRules.Side&&BranchOperations.Read<SideGateMaintenance>(s,"first-approach:side-maintenance")is{Isolated:true,Braced:true,ZeroEnergy:true};
        var battle=BranchOperations.Read<FirstApproachBattle>(s,CombatId(id));return result.Method=="shared_combat"&&battle is{ResultApplied:true}&&battle.Encounter==id&&battle.QuestInstance==result.QuestInstance&&battle.SourceVersion==result.SourceVersion&&battle.Battle.Instance==result.Instance&&battle.Battle.Attempt==result.Attempt&&battle.Battle.Outcome==CombatOutcome.Controlled&&!battle.Battle.Units.Any(u=>u.Side==CombatSide.Enemy&&u.Alive);
    }
    public static bool RelayCleared(BranchSnapshot s)=>Accepted(s,FirstApproachRules.Relay);
    public static string? TowerAccessIssue(BranchSnapshot s)
    {
        // Completed historic Q104 remains usable; it does not fabricate any missing base ENC receipt.
        if(BranchOperations.Read<QuestState>(s,"quest:Q-104")is{Lifecycle:QuestLifecycle.Closed,Outcome:QuestOutcome.Completed}&&s.Receipts.Any(r=>r.SemanticKey=="Q-104:resolution"&&r.ActionType=="tidegate_vessel_handover")&&BranchOperations.Read<TidegateLocalState>(s,"tower:local")is{Safe:true,FinalInspection:true})return null;
        return Accepted(s,FirstApproachRules.Side)||Accepted(s,FirstApproachRules.Outer)?null:"先实际完成侧闸或外闸的一条塔基通行处置；它不代替塔主机安全。";
    }
    private static void RouteOpen(BranchSnapshot s,string id)
    {Require(!Accepted(s,id),"同一处置结果已受理，不重开奖励遭遇。");if(id!=FirstApproachRules.Relay)Require(!Accepted(s,id==FirstApproachRules.Side?FirstApproachRules.Outer:FirstApproachRules.Side),"另一个塔基路径已经受理，不再双算同一通行。");}
    private CommitPlanCommand Plan(BranchSnapshot s,string key,string action,ImmutableArray<EntityChange> changes,object result,ImmutableArray<ItemChange> items=default,CommandId? command=null)=>BranchOperations.Plan(_repository,s,key,action,changes,s.Entities.Select(e=>e.Id).Concat(new[]{CombatId(FirstApproachRules.Relay),CombatId(FirstApproachRules.Side),CombatId(FirstApproachRules.Outer),ResultId(FirstApproachRules.Relay),ResultId(FirstApproachRules.Side),ResultId(FirstApproachRules.Outer),"first-approach:side-maintenance"}).ToArray(),result,items:items,request:command);
    private async ValueTask<CommandResult> Execute(CommitPlanCommand command)
    {string owner="first-approach:"+command.Context.Command.Value;Require(await _leases.TryLeasesAsync(command.Context.Load,owner,["input:foreground"]),"前景或旧载入尚未交接，原实例保留。");try{return await _repository.ExecuteAsync(command);}finally{await _leases.ReleaseLeasesAsync(command.Context.Load,owner);}}
    public async ValueTask<ImmutableArray<ApproachMaterial>> MaterialsAsync(){var s=await _repository.SnapshotAsync();return Package.Materials.Where(m=>Acquired(s,m)).ToImmutableArray();}
    public async ValueTask<ApproachMaterial> OpenMaterialAsync(string id)=>(await MaterialsAsync()).SingleOrDefault(m=>m.Id==id)??throw new InvalidOperationException("该完整说明尚未实际取得。");
    public async ValueTask<CommandResult> DiscoverAsync(string id)
    {
        var s=await _repository.SnapshotAsync();Safe(s);var material=Package.Materials.Single(m=>m.Id==id);var d=Package.Encounters.Single(e=>e.SourceMaterial==id);Scope(s,d.Id);At(s,material.Spot);if(Acquired(s,material))return new(CommitStatus.AlreadyCommitted,null,[]);
        return await Execute(Plan(s,"material:"+id+":acquired","first_approach_source_acquired",[BranchOperations.Change(s,"material:"+id,"material_access",new ApproachSourceAccess(id,material.Version,SourceHash(material),material.Spot))],new{source=id,version=material.Version,language_assessment="not_evaluated"}));
    }
    public async ValueTask<CommandResult> PerformSideStepAsync(string step)
    {
        var s=await _repository.SnapshotAsync();Safe(s);Scope(s,FirstApproachRules.Side);At(s,"R1-S09.side_gate");Source(s,FirstApproachRules.Side);RouteOpen(s,FirstApproachRules.Side);var old=BranchOperations.Read<SideGateMaintenance>(s,"first-approach:side-maintenance")??new();
        var next=step switch{"isolate"=>old with{Isolated=true},"brace"when old.Isolated=>old with{Braced=true},"inspect_zero_energy"when old.Isolated&&old.Braced=>old with{ZeroEnergy=true},_=>throw new InvalidOperationException("先实际隔离，再固定支架，最后检查零能和零运动。")};var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(BranchOperations.Change(s,"first-approach:side-maintenance","side_gate_maintenance",next));
        if(next.ZeroEnergy){var result=new FirstApproachResult(FirstApproachRules.Side,"Q-104",Quest(s,"Q-104").Instance,Guid.NewGuid(),Guid.NewGuid(),"manual_isolate_brace_zero_energy",Package.ContentVersion,true);changes.Add(BranchOperations.Change(s,ResultId(result.Encounter),"first_approach_result",result));return await Execute(Plan(s,"first-approach:"+result.Encounter+":result","first_approach_result",changes.ToImmutable(),result));}
        return await Execute(Plan(s,"first-approach:side-step:"+Guid.NewGuid(),"first_approach_manual_step",changes.ToImmutable(),new{step,actual_spot="R1-S09.side_gate",tower_safety_established=false,reward=0}));
    }
    public async ValueTask<FirstApproachBattle?> EncounterAsync(string id)=>BranchOperations.Read<FirstApproachBattle>(await _repository.SnapshotAsync(),CombatId(id));
    public async ValueTask<CommandResult> BeginAsync(string id,string difficulty="standard",string[]? supplies=null)
    {
        var s=await _repository.SnapshotAsync();Safe(s);Scope(s,id);var definition=Package.Encounters.Single(e=>e.Id==id);At(s,definition.Spot);Source(s,id);RouteOpen(s,id);var old=BranchOperations.Read<FirstApproachBattle>(s,CombatId(id));Require(old is null||old.Battle.Outcome==CombatOutcome.Withdrawn,"请继续当前遭遇，失败需按真实入口恢复。");
        var party=new PartyService(_repository,_catalog);var hero=party.Inspect(s);Require(hero.Level>=definition.EntryLevelFloor&&hero.Grade==definition.Grade,"不预支父任务等级或校准奖励。");var equipped=s.Items.Where(i=>i.OwnerKind=="equipped"&&i.OwnerId=="hero").ToArray();var modules=s.Items.Where(i=>i.OwnerKind=="module_installed"&&equipped.Any(e=>e.Id==i.OwnerId)).Select(i=>party.Definitions.Modules[i.DefinitionId]);var helpers=id==FirstApproachRules.Relay?new[]{"AUX-GUARD","AUX-SUPPORT"}:new RosterService(_repository,_catalog).Helpers(s).ToArray();
        var battle=BattleDefinitions.Create("BENCH-WORKSHOP",hero.Level,hero.Grade,hero.Build,hero.Weapon,hero.Armor,hero.Relics,modules,old?.Battle.Instance??Guid.NewGuid(),Guid.NewGuid(),difficulty,helpers);battle.Definition=id;battle.Mechanism=null;battle.Controls.Clear();battle.Units.RemoveAll(u=>u.Side==CombatSide.Enemy||id==FirstApproachRules.Relay&&u.Id!="hero");
        foreach(var helper in battle.Units.Where(u=>u.Id.StartsWith("CP-",StringComparison.Ordinal)))
        {var member=BranchOperations.Read<RosterEntry>(s,"roster:"+helper.Id)??throw new InvalidOperationException("当前同伴身份未登记。");helper.Variants=member.Tactics.ToDictionary();var relics=s.Items.Where(i=>i.OwnerKind=="equipped"&&i.OwnerId==helper.Id&&i.Slot=="relic1").Select(i=>i.DefinitionId).ToArray();helper.Relics=relics;helper.MaxFocus=Math.Min(14,helper.MaxFocus+(relics.Contains("RE-09")?1:0));helper.Focus=helper.MaxFocus;helper.ArmorBp=Math.Min(4500,helper.ArmorBp+(relics.Contains("RE-03")?300:0));helper.Initiative+=relics.Contains("RE-04")?2:0;helper.ExposureBonus=Math.Min(4000,helper.ExposureBonus+(relics.Contains("RE-07")?500:0));helper.GuardBase=Math.Min(3500,helper.GuardBase+(relics.Contains("RE-11")?500:0));}
        using var modes=JsonDocument.Parse(_catalog.Definitions["combat_difficulty"][0].PayloadJson);int hpBp=modes.RootElement.EnumerateArray().Single(m=>m.GetProperty("id").GetString()==difficulty).GetProperty("enemy_hp_bp").GetInt32();foreach(var enemy in definition.Enemies){int hp=enemy.Hp*hpBp/10000;battle.Units.Add(new(){Id=enemy.Id,Name=enemy.Template=="braced_warden"?"固守机卫":"巡检浮机",Side=CombatSide.Enemy,Slot=enemy.Slot,MaxHp=hp,Hp=hp,ArmorBp=enemy.ArmorBp,PressureThreshold=enemy.PressureThreshold,Initiative=12,Pattern=enemy.Pattern.ToArray()});}
        supplies??=[];Require(supplies.Length<=2&&supplies.Distinct().Count()==supplies.Length,"最多携带两份当前实际补给。");foreach(string carried in supplies){var item=s.Items.SingleOrDefault(i=>i.Id==carried&&i.OwnerKind=="player"&&i.OwnerId=="hero"&&i.DefinitionId is"CON-01"or"CON-02"or"CON-03"or"CON-04")??throw new InvalidOperationException("所选补给尚未实际持有。");battle.Supplies.Add(new(item.Id,item.DefinitionId));}
        var entry=StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(battle));var engine=new BattleEngine(BattleDefinitions,battle);engine.Start();return await Execute(Plan(s,"first-approach:"+id+":"+battle.Attempt+":entry","first_approach_entered",[BranchOperations.Change(s,CombatId(id),"combat",new FirstApproachBattle(id,Quest(s,definition.Quest).Instance,Package.ContentVersion,engine.Snapshot(),entry,Actor(s)))],new{encounter=id,instance=battle.Instance,attempt=battle.Attempt,actual_level=hero.Level,actual_grade=hero.Grade,roster=definition.Roster,numeric_review="human_pending",kill_xp=0}));
    }
    public async ValueTask<EncounterActionPreview> PreviewActionAsync(string id,BattleRequest request)
    {
        var s=await _repository.SnapshotAsync();Scope(s,id);var old=BranchOperations.Read<FirstApproachBattle>(s,CombatId(id))??throw new InvalidOperationException("尚未实际进入遭遇。");Require(old.Encounter==id&&old.SourceVersion==Package.ContentVersion&&old.QuestInstance==Quest(s,Package.Encounters.Single(e=>e.Id==id).Quest).Instance&&Actor(s).Scene==old.ReturnActor.Scene&&Actor(s).Position==old.ReturnActor.Position,"当前实例、版本或实际入口改变。");var engine=new BattleEngine(BattleDefinitions,old.Battle);var view=engine.Preview(request);if(!view.Legal)return new(Guid.Empty,view,view.Reason??"当前动作不可执行。");engine.Apply(request);var battle=engine.Snapshot();var changes=ImmutableArray.CreateBuilder<EntityChange>();var items=ImmutableArray.CreateBuilder<ItemChange>();bool applied=old.ResultApplied;object result=new{request,outcome=battle.Outcome,kill_xp=0};string key="first-approach:"+id+":"+battle.Attempt+":"+request.Command.Value,action="first_approach_combat_action";
        foreach(var used in battle.Supplies.Where(i=>i.Used&&!old.Battle.Supplies.Single(x=>x.Instance==i.Instance).Used)){var held=s.Items.Single(i=>i.Id==used.Instance&&i.OwnerKind=="player"&&i.OwnerId=="hero");items.Add(new(held.Id,held.DefinitionId,"player","hero","consumed",battle.Attempt.ToString(),null));}
        if(battle.Outcome==CombatOutcome.Controlled&&!applied){Require(!battle.Units.Any(u=>u.Side==CombatSide.Enemy&&u.Alive),"完整威胁未制止。");var r=new FirstApproachResult(id,Package.Encounters.Single(e=>e.Id==id).Quest,old.QuestInstance,battle.Instance,battle.Attempt,"shared_combat",Package.ContentVersion,true);changes.Add(BranchOperations.Change(s,ResultId(id),"first_approach_result",r));applied=true;result=r;key="first-approach:"+id+":result";action="first_approach_result";}
        changes.Add(BranchOperations.Change(s,CombatId(id),"combat",old with{Battle=battle,ResultApplied=applied}));var token=Guid.NewGuid();_previews[token]=Plan(s,key,action,changes.ToImmutable(),result,items.ToImmutable(),request.Command);return new(token,view,$"{request.Actor} · {request.Action} · AP {view.Ap} / 专注 {view.Focus}");
    }
    public ValueTask<CommandResult> ConfirmAsync(Guid token)=>_previews.TryGetValue(token,out var plan)?Execute(plan):ValueTask.FromResult(new CommandResult(CommitStatus.Rejected,null,[new("preview_closed","first-approach","预览已关闭，请按当前实际状态重开。") ]));
    public void ClosePreview(Guid token)=>_previews.Remove(token);
    public async ValueTask<CommandResult> WithdrawAsync(string id)
    {
        var s=await _repository.SnapshotAsync();var old=BranchOperations.Read<FirstApproachBattle>(s,CombatId(id))??throw new InvalidOperationException("尚无入口。");Require(!old.ResultApplied&&!Accepted(s,id),"已制止结果不能撤回刷资源。");var entry=StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(old.Entry));entry.Outcome=CombatOutcome.Withdrawn;entry.CurrentActor=null;var items=old.Battle.Supplies.Where(i=>i.Used).Select(i=>{var held=s.Items.Single(x=>x.Id==i.Instance&&x.OwnerKind=="consumed"&&x.OwnerId==old.Battle.Attempt.ToString());return new ItemChange(held.Id,held.DefinitionId,held.OwnerKind,held.OwnerId,"player","hero",null);}).ToImmutableArray();_previews.Clear();return await Execute(Plan(s,"first-approach:"+id+":"+old.Battle.Attempt+":withdraw","first_approach_withdrawn",[BranchOperations.Change(s,CombatId(id),"combat",old with{Battle=entry}),BranchOperations.Change(s,"actor","actor",old.ReturnActor)],new{sources_and_drafts_retained=true,kill_xp=0},items));
    }
    public async ValueTask<CommandResult> RetryAsync(string id,string? difficulty=null)
    {var old=await EncounterAsync(id)??throw new InvalidOperationException("尚无入口。");var withdrawal=await WithdrawAsync(id);if(withdrawal.Status is not(CommitStatus.Committed or CommitStatus.AlreadyCommitted))return withdrawal;return await BeginAsync(id,difficulty??old.Battle.Difficulty,old.Entry.Supplies.Select(i=>i.Instance).ToArray());}
}

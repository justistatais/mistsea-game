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
using Mist.Core.Combat;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.OptionalEncounterContracts;
using Mist.Core.OptionalLocalWorks;
using Mist.Core.ChapterSix;

namespace Mist.Application.OptionalEncounterContracts;

public sealed class OptionalEncounterService
{
    private readonly IAdventureRepository _repository;private readonly IEventLeaseStore _leases;private readonly RuntimeCatalog _catalog;
    private readonly Dictionary<Guid,CommitPlanCommand> _previews=[];
    public OptionalContractPackage Package {get;}public ImmutableDictionary<string,SceneGeometry> Geometry {get;}public BattleDefinitions BattleDefinitions {get;}
    public OptionalEncounterService(IAdventureRepository repository,IEventLeaseStore leases,RuntimeCatalog catalog,OptionalContractPackage package,ImmutableDictionary<string,SceneGeometry> geometry)
    {OptionalContractRules.Validate(package);_repository=repository;_leases=leases;_catalog=catalog;Package=package;Geometry=OptionalContractRules.ExtendGeometry(package,geometry);BattleDefinitions=new(catalog);}
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static bool ActualSideArrival(StoredReceipt r,SideWellVisit visit)
    {if(r.ActionType!="scene_transition")return false;using var d=JsonDocument.Parse(r.ResultJson);var j=d.RootElement;if(!j.TryGetProperty("from",out var from)||from.GetString()!="R3-S05"||!j.TryGetProperty("to",out var to)||to.GetString()!="R3-O02")return false;var t=StrictJson.Deserialize<TraversedConnection>(r.ResultJson);return t.ActionClock==visit.ArrivalClock&&t.FromPort==visit.FromPort&&t.ToPort==visit.ToPort;}
    private OptionalContractDefinition Definition(string id)=>Package.Contracts.SingleOrDefault(d=>d.Id==id)??throw new InvalidOperationException("没有登记这份独立合同。");
    private static string PermitId(string id)=>"optional-encounter:permit:"+id;
    private static string SourceId(string id)=>"optional-encounter:source:"+id;
    private static string ResultId(string id)=>"optional-encounter:result:"+id;
    private static string CombatId(string id)=>"combat:"+id;
    private static ActorState Actor(BranchSnapshot s)=>BranchOperations.Read<ActorState>(s,"actor")??throw new InvalidOperationException("没有实际现场角色。");
    private void At(BranchSnapshot s,OptionalContractDefinition d){var actor=Actor(s);Require(actor.Scene==d.Scene&&Geometry.TryGetValue(d.Scene,out var scene)&&scene.Spots.Any(p=>p.Id==d.Spot&&p.Stand.Distance(actor.Position)<=2.5)&&new NavigationGrid(scene).CanStand(actor.Position,true),"先走到这份合同的实际安全入口。");}
    private static void Safe(BranchSnapshot s)
    {BranchOperations.Safe(s);Require(!s.Entities.Where(e=>e.Kind=="event").Select(e=>StrictJson.Deserialize<EventSession>(e.Json)).Any(e=>e.State==EventLifecycle.Running),"先安全交接当前事件。");Require(!s.Entities.Where(e=>e.Kind=="transport_segment").Select(e=>StrictJson.Deserialize<TransportSegment>(e.Json)).Any(t=>t.CargoInTransit&&!t.Completed),"先把实际在途货物安全交接。");Require(!BranchOperations.Receipt(s,"Q-604")&&BranchOperations.Entity(s,"final:preparation")is null,"最终准备封存后当地追加合同暂停。");}
    private static StoredReceipt RegionEntry(BranchSnapshot s,OptionalContractDefinition d)
    {
        if(d.Region=="R1")
        {
            var permit=BranchOperations.Entity(s,"permission:Q-104")??throw new InvalidOperationException("尚无塔基实际现场许可。");using var doc=JsonDocument.Parse(permit.Json);Require(doc.RootElement.GetProperty("allowed").GetBoolean(),"当前塔基许可已撤回。");return s.Receipts.SingleOrDefault(r=>r.SemanticKey=="Q104:work:accepted"&&r.ActionType=="accept_tidegate_site_work")??throw new InvalidOperationException("塔基实际工作未受理。");
        }
        string state="chapter"+d.Region[1],action=d.Region switch{"R2"=>"chapter_two_region_initialized","R3"=>"chapter_three_region_initialized","R4"=>"glasshaven_region_initialized","R5"=>"old_city_region_initialized","R6"=>"chapter_six_region_initialized",_=>throw new InvalidOperationException("没有登记该地区。")};
        var entity=BranchOperations.Entity(s,state)??throw new InvalidOperationException("当前地区尚未由真实抵达初始化。");using var document=JsonDocument.Parse(entity.Json);string version=document.RootElement.GetProperty("content_version").GetString()??throw new InvalidOperationException("当前地区来源版本缺失。");
        if(d.Region=="R3")
        {var visit=BranchOperations.Read<SideWellVisit>(s,"R3-O02:actual-visit");Require(visit is not null&&visit.Instance!=Guid.Empty&&visit.FromPort=="R3-S05.to.R3-O02"&&visit.ToPort=="R3-O02.to.R3-S05"&&visit.Scene=="R3-O02"&&Actor(s).Scene==visit.Scene&&Actor(s).ActionClock>=visit.ArrivalClock,"须先实际检查并走过侧井的独立安全配对入口。");BranchOperations.Evidence(s,visit!.SafetyReceipt,"sidewell_entry_inspected");Require(s.Receipts.Any(r=>ActualSideArrival(r,visit!)),"侧井实际到场来源不存在。");}
        if(d.Region=="R6")Require(BranchOperations.Read<FrontierCargoState>(s,"R6:cargo")is{Stage:"received",SafelyDeposited:true}&&BranchOperations.Read<TransportSegment>(s,"R6:base_transport")is{Completed:true,CargoInTransit:false},"当前受保护货组须先实际接收，追加合同不挪用在途货物。");
        return s.Receipts.SingleOrDefault(r=>r.SemanticKey==d.Region+":initialize:"+version&&r.ActionType==action)??throw new InvalidOperationException("当前地区初始化来源不符。");
    }
    private bool Acquired(BranchSnapshot s,OptionalContractMaterial m)=>BranchOperations.Read<OptionalContractSource>(s,SourceId(m.Id))is{}source&&source.Material==m.Id&&source.Version==m.ContentVersion&&source.EnglishFingerprint==OptionalContractRules.Fingerprint(m);
    private OptionalContractMaterial Source(BranchSnapshot s,OptionalContractDefinition d){var m=Package.Materials.Single(m=>m.Id==d.Material);Require(Acquired(s,m),"先在现场取得这份完整说明。");return m;}
    private OptionalContractPermit Permit(BranchSnapshot s,OptionalContractDefinition d)
    {var m=Source(s,d);var permit=BranchOperations.Read<OptionalContractPermit>(s,PermitId(d.Id));Require(permit is{Allowed:true}&&permit.Encounter==d.Id&&permit.Instance!=Guid.Empty&&permit.SourceVersion==Package.ContentVersion&&permit.EnglishFingerprint==OptionalContractRules.Fingerprint(m)&&permit.Target==d.Target&&permit.Purpose==OptionalContractRules.Purpose&&permit.RegionEntryReceipt==RegionEntry(s,d).ReceiptId,"需要当前独立合同的明确许可，主线许可不能代签。");return permit!;}
    private CommitPlanCommand Plan(BranchSnapshot s,string key,string action,ImmutableArray<EntityChange> changes,object result,ImmutableArray<ItemChange> items=default,CommandId? request=null)=>BranchOperations.Plan(_repository,s,key,action,changes,s.Entities.Select(e=>e.Id).Concat(Package.Contracts.SelectMany(c=>new[]{PermitId(c.Id),SourceId(c.Material),ResultId(c.Id),CombatId(c.Id)})).ToArray(),result,items:items,request:request);
    private async ValueTask<CommandResult> Execute(CommitPlanCommand plan)
    {
        var session=_repository.Session;if(plan.Context.Profile!=session.Profile||plan.Context.Branch!=session.Branch||plan.Context.Load!=session.Load||plan.Context.ReleaseId!=session.ReleaseId)return new(CommitStatus.Rejected,null,[new("stale_load","optional-contract","档案读取世代改变，请重开预览。")]);
        string owner="optional-contract:"+plan.Context.Command.Value;Require(await _leases.TryLeasesAsync(plan.Context.Load,owner,["input:foreground"]),"另一项前景工作尚未安全交接。");try{return await _repository.ExecuteAsync(plan);}finally{await _leases.ReleaseLeasesAsync(plan.Context.Load,owner);}
    }
    public async ValueTask<ImmutableArray<OptionalContractMaterial>> MaterialsAsync(){var s=await _repository.SnapshotAsync();return Package.Materials.Where(m=>Acquired(s,m)).ToImmutableArray();}
    public async ValueTask<OptionalContractMaterial> OpenMaterialAsync(string id)=>(await MaterialsAsync()).SingleOrDefault(m=>m.Id==id)??throw new InvalidOperationException("这份说明尚未实际取得。");
    public async ValueTask<OptionalContractPermit?> PermitAsync(string id)=>BranchOperations.Read<OptionalContractPermit>(await _repository.SnapshotAsync(),PermitId(id));
    public async ValueTask<OptionalContractBattle?> EncounterAsync(string id)=>BranchOperations.Read<OptionalContractBattle>(await _repository.SnapshotAsync(),CombatId(id));
    public async ValueTask<OptionalContractResult?> ResultAsync(string id)
    {
        var s=await _repository.SnapshotAsync();var d=Definition(id);var r=BranchOperations.Read<OptionalContractResult>(s,ResultId(id));if(r is null)return null;var battle=BranchOperations.Read<OptionalContractBattle>(s,CombatId(id));var material=Package.Materials.Single(m=>m.Id==d.Material);
        Require(r.Encounter==id&&r.ContractInstance!=Guid.Empty&&r.BattleInstance!=Guid.Empty&&r.Attempt!=Guid.Empty&&r.SourceVersion==Package.ContentVersion&&r.SourceFingerprint==OptionalContractRules.Fingerprint(material)&&r.Target==d.Target&&r.InspectionAction==d.InspectionAction&&r is{Controlled:true,Inspected:true,Experience:0,Marks:0,Parts:0}&&battle is{ResultApplied:true}&&battle.ContractInstance==r.ContractInstance&&battle.Battle.Instance==r.BattleInstance&&battle.Battle.Attempt==r.Attempt&&battle.Battle.Outcome==CombatOutcome.Controlled&&!battle.Battle.Units.Any(u=>u.Side==CombatSide.Enemy&&u.Alive)&&s.Receipts.Any(x=>x.SemanticKey=="optional-contract:"+id+":result"&&x.ActionType=="optional_contract_result"&&x.ResultJson==StrictJson.Serialize(r)),"合同结果与当前实际设备、实例、检查或回执不符。");
        var permission=StrictJson.Deserialize<OptionalContractPermit>(BranchOperations.Evidence(s,r.PermissionReceipt,"optional_contract_accepted").ResultJson);Require(permission.Allowed&&permission.Encounter==id&&permission.Instance==r.ContractInstance&&permission.Target==d.Target&&permission.SourceVersion==r.SourceVersion&&permission.EnglishFingerprint==r.SourceFingerprint&&permission.RegionEntryReceipt==r.RegionEntryReceipt,"受理结果的独立许可来源不符。");return r;
    }
    public async ValueTask<CommandResult> DiscoverAsync(string id)
    {var d=Package.Contracts.Single(c=>c.Material==id);var s=await _repository.SnapshotAsync();Safe(s);At(s,d);RegionEntry(s,d);var m=Package.Materials.Single(m=>m.Id==id);if(Acquired(s,m))return new(CommitStatus.AlreadyCommitted,null,[]);return await Execute(Plan(s,SourceId(id)+":acquired","optional_contract_source_acquired",[BranchOperations.Change(s,SourceId(id),"material_access",new OptionalContractSource(id,m.ContentVersion,OptionalContractRules.Fingerprint(m),d.Spot))],new{material=id,version=m.ContentVersion,access_class=m.AccessClass,private_access_granted=false}));}
    public async ValueTask<CommandResult> AcceptContractAsync(string id)
    {var d=Definition(id);var s=await _repository.SnapshotAsync();Safe(s);At(s,d);var entry=RegionEntry(s,d);var m=Source(s,d);Require(BranchOperations.Entity(s,ResultId(id))is null,"合同已经受理完成，不重开同一设备。");var old=BranchOperations.Read<OptionalContractPermit>(s,PermitId(id));if(old is{Allowed:true})return new(CommitStatus.AlreadyCommitted,null,[]);var permit=new OptionalContractPermit(id,old?.Instance??Guid.NewGuid(),Package.ContentVersion,OptionalContractRules.Fingerprint(m),d.Target,OptionalContractRules.Purpose,entry.ReceiptId,true);return await Execute(Plan(s,"optional-contract:"+id+":accept:"+Guid.NewGuid(),"optional_contract_accepted",[BranchOperations.Change(s,PermitId(id),"optional_contract_permit",permit)],permit));}
    public async ValueTask<CommandResult> BeginAsync(string id,string difficulty="standard",string[]? supplies=null)
    {
        var d=Definition(id);var s=await _repository.SnapshotAsync();Safe(s);At(s,d);var permit=Permit(s,d);Require(BranchOperations.Entity(s,ResultId(id))is null,"同一合同的设备已检查受理，不再生成威胁。");var old=BranchOperations.Read<OptionalContractBattle>(s,CombatId(id));Require(old is null||old.Battle.Outcome==CombatOutcome.Withdrawn,"请继续原实例；失败需明确恢复入口。");
        var party=new PartyService(_repository,_catalog);var hero=party.Inspect(s);var equipped=s.Items.Where(i=>i.OwnerKind=="equipped"&&i.OwnerId=="hero").ToArray();var modules=s.Items.Where(i=>i.OwnerKind=="module_installed"&&equipped.Any(e=>e.Id==i.OwnerId)).Select(i=>party.Definitions.Modules[i.DefinitionId]);var helpers=new RosterService(_repository,_catalog).Helpers(s).ToArray();
        var battle=BattleDefinitions.Create("BENCH-WORKSHOP",hero.Level,hero.Grade,hero.Build,hero.Weapon,hero.Armor,hero.Relics,modules,old?.Battle.Instance??Guid.NewGuid(),Guid.NewGuid(),difficulty,helpers);battle.Definition=id;battle.Mechanism=null;battle.Controls.Clear();battle.Units.RemoveAll(u=>u.Side==CombatSide.Enemy);
        foreach(var helper in battle.Units.Where(u=>u.Id.StartsWith("CP-",StringComparison.Ordinal)))
        {var member=BranchOperations.Read<RosterEntry>(s,"roster:"+helper.Id)??throw new InvalidOperationException("当前同伴身份未登记。");helper.Variants=member.Tactics.ToDictionary();var relics=s.Items.Where(i=>i.OwnerKind=="equipped"&&i.OwnerId==helper.Id&&i.Slot is "relic1" or "relic2").Select(i=>i.DefinitionId).ToArray();helper.Relics=relics;helper.MaxFocus=Math.Min(14,helper.MaxFocus+(relics.Contains("RE-09")?1:0));helper.Focus=helper.MaxFocus;helper.ArmorBp=Math.Min(4500,helper.ArmorBp+(relics.Contains("RE-03")?300:0));helper.Initiative+=relics.Contains("RE-04")?2:0;helper.ExposureBonus=Math.Min(4000,helper.ExposureBonus+(relics.Contains("RE-07")?500:0));helper.GuardBase=Math.Min(3500,helper.GuardBase+(relics.Contains("RE-11")?500:0));}
        using var modes=JsonDocument.Parse(_catalog.Definitions["combat_difficulty"][0].PayloadJson);int hpBp=modes.RootElement.EnumerateArray().Single(m=>m.GetProperty("id").GetString()==difficulty).GetProperty("enemy_hp_bp").GetInt32();foreach(var e in d.Enemies){int hp=Math.Max(1,e.Hp*hpBp/10000);battle.Units.Add(new(){Id=e.Id,Name=e.NameZh,Side=CombatSide.Enemy,Slot=e.Slot,MaxHp=hp,Hp=hp,ArmorBp=e.ArmorBp,PressureThreshold=e.PressureThreshold,Initiative=e.Initiative,Pattern=e.Pattern.ToArray()});}
        supplies??=[];Require(supplies.Length<=2&&supplies.Distinct().Count()==supplies.Length,"最多携带两件当前实际补给。");foreach(string id2 in supplies){var item=s.Items.SingleOrDefault(i=>i.Id==id2&&i.OwnerKind=="player"&&i.OwnerId=="hero"&&i.DefinitionId is "CON-01" or "CON-02" or "CON-03" or "CON-04")??throw new InvalidOperationException("该补给实例当前未持有。");battle.Supplies.Add(new(item.Id,item.DefinitionId));}
        var entry=StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(battle));var engine=new BattleEngine(BattleDefinitions,battle);engine.Start();return await Execute(Plan(s,"optional-contract:"+id+":"+battle.Attempt+":entry","optional_contract_encounter_entered",[BranchOperations.Change(s,CombatId(id),"combat",new OptionalContractBattle(id,permit.Instance,Package.ContentVersion,d.Target,engine.Snapshot(),entry,Actor(s)))],new{encounter=id,contract_instance=permit.Instance,battle_instance=battle.Instance,attempt=battle.Attempt,target=d.Target,actual_level=hero.Level,actual_grade=hero.Grade,helpers,kill_xp=0,extra_reward=0,numeric_review=Package.NumericReview}));
    }
    public async ValueTask<EncounterActionPreview> PreviewActionAsync(string id,BattleRequest request)
    {
        var s=await _repository.SnapshotAsync();var d=Definition(id);At(s,d);var permit=Permit(s,d);var old=BranchOperations.Read<OptionalContractBattle>(s,CombatId(id))??throw new InvalidOperationException("尚未实际进入这份合同。");Require(old.ContractInstance==permit.Instance&&old.Target==d.Target&&old.SourceVersion==Package.ContentVersion&&Actor(s)==old.ReturnActor,"当前合同实例、版本或入口改变。");var engine=new BattleEngine(BattleDefinitions,old.Battle);var preview=engine.Preview(request);if(!preview.Legal)return new(Guid.Empty,preview,preview.Reason??"当前动作不能执行。");engine.Apply(request);var resolved=engine.Snapshot();var items=ImmutableArray.CreateBuilder<ItemChange>();foreach(var used in resolved.Supplies.Where(i=>i.Used&&!old.Battle.Supplies.Single(x=>x.Instance==i.Instance).Used)){var held=s.Items.Single(i=>i.Id==used.Instance&&i.OwnerKind=="player"&&i.OwnerId=="hero");items.Add(new(held.Id,held.DefinitionId,"player","hero","consumed",resolved.Attempt.ToString(),null));}
        var token=Guid.NewGuid();_previews[token]=Plan(s,"optional-contract:"+id+":"+resolved.Attempt+":"+request.Command.Value,"optional_contract_combat_action",[BranchOperations.Change(s,CombatId(id),"combat",old with{Battle=resolved})],new{request,outcome=resolved.Outcome,target=d.Target,inspection_required=true,kill_xp=0},items.ToImmutable(),request.Command);return new(token,preview,$"{request.Actor} · {request.Action} · AP {preview.Ap} / 专注 {preview.Focus}");
    }
    public ValueTask<CommandResult> ConfirmAsync(Guid token)=>_previews.TryGetValue(token,out var plan)?Execute(plan):ValueTask.FromResult(new CommandResult(CommitStatus.Rejected,null,[new("preview_closed","optional-contract","预览已关闭，请按当前状态重新选择。") ]));
    public void ClosePreview(Guid token)=>_previews.Remove(token);
    public async ValueTask<CommandResult> InspectTargetAsync(string id,string operation)
    {
        var d=Definition(id);var s=await _repository.SnapshotAsync();Safe(s);At(s,d);var permit=Permit(s,d);Require(operation==d.InspectionAction,"检查动作未覆盖这份合同的具体设备。");var old=BranchOperations.Read<OptionalContractBattle>(s,CombatId(id))??throw new InvalidOperationException("实际制止尚未成立。");Require(old.ContractInstance==permit.Instance&&old.Target==d.Target&&old.SourceVersion==Package.ContentVersion&&old.Battle.Outcome==CombatOutcome.Controlled&&!old.Battle.Units.Any(u=>u.Side==CombatSide.Enemy&&u.Alive)&&old.Battle.PendingReinforcements.Count==0,"当前合同的完整威胁尚未制止。");
        if(BranchOperations.Read<OptionalContractResult>(s,ResultId(id))is not null)return new(CommitStatus.AlreadyCommitted,null,[]);var permission=s.Receipts.LastOrDefault(r=>r.ActionType=="optional_contract_accepted"&&StrictJson.Deserialize<OptionalContractPermit>(r.ResultJson)==permit)??throw new InvalidOperationException("实际合同受理来源不存在。");var source=Source(s,d);var result=new OptionalContractResult(id,permit.Instance,old.Battle.Instance,old.Battle.Attempt,Package.ContentVersion,OptionalContractRules.Fingerprint(source),d.Target,operation,permission.ReceiptId,permit.RegionEntryReceipt,true,true);
        return await Execute(Plan(s,"optional-contract:"+id+":result","optional_contract_result",[BranchOperations.Change(s,CombatId(id),"combat",old with{ResultApplied=true}),BranchOperations.Change(s,ResultId(id),"optional_contract_result",result)],result));
    }
    private ImmutableArray<ItemChange> RestoreItems(BranchSnapshot s,OptionalContractBattle old)=>old.Battle.Supplies.Where(i=>i.Used).Select(i=>{var held=s.Items.Single(x=>x.Id==i.Instance&&x.OwnerKind=="consumed"&&x.OwnerId==old.Battle.Attempt.ToString());return new ItemChange(held.Id,held.DefinitionId,held.OwnerKind,held.OwnerId,"player","hero",null);}).ToImmutableArray();
    public async ValueTask<CommandResult> WithdrawAsync(string id)
    {var s=await _repository.SnapshotAsync();var d=Definition(id);At(s,d);Permit(s,d);var old=BranchOperations.Read<OptionalContractBattle>(s,CombatId(id))??throw new InvalidOperationException("尚无实际入口。");Require(!old.ResultApplied&&old.Battle.Outcome is CombatOutcome.Active or CombatOutcome.Defeat,"已制止设备不能撤回重刷。");var entry=StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(old.Entry));entry.Outcome=CombatOutcome.Withdrawn;entry.CurrentActor=null;_previews.Clear();return await Execute(Plan(s,"optional-contract:"+id+":"+old.Battle.Attempt+":withdraw","optional_contract_entry_withdrawn",[BranchOperations.Change(s,CombatId(id),"combat",old with{Battle=entry}),BranchOperations.Change(s,"actor","actor",old.ReturnActor)],new{encounter=id,restored_full_entry=true,sources_retained=true,extra_reward=0},RestoreItems(s,old)));}
    public async ValueTask<CommandResult> RevokeContractAsync(string id)
    {
        var s=await _repository.SnapshotAsync();var d=Definition(id);At(s,d);var permit=Permit(s,d);var changes=ImmutableArray.CreateBuilder<EntityChange>();var items=ImmutableArray<ItemChange>.Empty;var old=BranchOperations.Read<OptionalContractBattle>(s,CombatId(id));
        if(old?.Battle.Outcome is CombatOutcome.Active or CombatOutcome.Defeat){var entry=StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(old.Entry));entry.Outcome=CombatOutcome.Withdrawn;entry.CurrentActor=null;items=RestoreItems(s,old);changes.Add(BranchOperations.Change(s,CombatId(id),"combat",old with{Battle=entry}));changes.Add(BranchOperations.Change(s,"actor","actor",old.ReturnActor));}else Safe(s);
        changes.Add(BranchOperations.Change(s,PermitId(id),"optional_contract_permit",permit with{Allowed=false}));_previews.Clear();return await Execute(Plan(s,"optional-contract:"+id+":revoke:"+Guid.NewGuid(),"optional_contract_permission_withdrawn",changes.ToImmutable(),new{encounter=id,permission_revoked=true,accepted_result_preserved=true},items));
    }
    public async ValueTask<CommandResult> RetryAsync(string id,string? difficulty=null){var old=await EncounterAsync(id)??throw new InvalidOperationException("尚无入口。");var result=await WithdrawAsync(id);if(result.Status is not(CommitStatus.Committed or CommitStatus.AlreadyCommitted))return result;return await BeginAsync(id,difficulty??old.Battle.Difficulty,old.Entry.Supplies.Select(i=>i.Instance).ToArray());}
}

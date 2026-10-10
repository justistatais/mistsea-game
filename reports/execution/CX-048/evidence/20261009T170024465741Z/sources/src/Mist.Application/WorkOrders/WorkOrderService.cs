using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Engineering;

namespace Mist.Application.WorkOrders;

public sealed record ProcurementProgress(string Source,int Step,bool Delivered,string LastWorkReceipt);
public sealed record ProcurementStep(string Source,int Step,string Region,string VerifiedWork);
public sealed record QualifiedWorkTeam(string Id,string Region,bool InstallationQualified,ImmutableArray<string> Operators,bool OperatorsQualified,string AuthorityReceipt);
public sealed record TechnicalPackage(string Version,bool Verified,string SourceReceipt);
public sealed record TrialPermission(string Tower,string Configuration,bool Allowed,string SourceReceipt);
public sealed record TowerSafety(string Tower,bool Safe,string SourceWork);
public sealed record WorkTeamAuthority(string Id,string Region,bool InstallationQualified,ImmutableArray<string> Operators,bool OperatorsQualified);
public sealed class WorkOrderService(IAdventureRepository repository,RuntimeCatalog catalog)
{
    public EngineeringRules Rules {get;}=new(catalog);
    public const string Preparation="preparation-d3-v1";
    private static string Key(string tower,WorkType type)=>Preparation+":"+tower+":"+type;
    private static string Entity(string key)=>"work:"+key;
    private static TowerReadiness Readiness(BranchSnapshot s,string tower)=>BranchOperations.Read<TowerReadiness>(s,"readiness:"+tower)??new(tower,false,false,null,false,false,false,null);
    private static void ValidateTeam(BranchSnapshot s,QualifiedWorkTeam team)
    {
        var receipt=BranchOperations.Evidence(s,team.AuthorityReceipt,"engineering_team_verified");var authority=StrictJson.Deserialize<WorkTeamAuthority>(receipt.ResultJson);
        if(authority.Id!=team.Id||authority.Region!=team.Region||authority.InstallationQualified!=team.InstallationQualified||authority.OperatorsQualified!=team.OperatorsQualified||!authority.Operators.SequenceEqual(team.Operators))throw new InvalidOperationException("班组与两个值守岗位必须对应实际资格回执。");
    }
    private static void ValidateConfiguration(BranchSnapshot s,string tower,string configuration,bool trial)
    {
        var package=BranchOperations.Read<TechnicalPackage>(s,"technical-package");if(package is not{Verified:true}||package.Version!=configuration)throw new InvalidOperationException("当前技术包或配置版本尚未核验。");
        var receipt=BranchOperations.Evidence(s,package.SourceReceipt,"technical_package_verified");using(var doc=System.Text.Json.JsonDocument.Parse(receipt.ResultJson))if(doc.RootElement.GetProperty("version").GetString()!=configuration)throw new InvalidOperationException("技术版本与验证回执不符。");
        if(!trial)return;var ready=Readiness(s,tower);var permission=BranchOperations.Read<TrialPermission>(s,"trial-permission:"+tower);
        if(ready.EnhancedVersion!=configuration||!ready.Operators||permission is not{Allowed:true}||permission.Configuration!=configuration)throw new InvalidOperationException("先核对当前增强安装、合格值守与地方试验许可；最终共同协议不参与此检查。");
        var permit=BranchOperations.Evidence(s,permission.SourceReceipt,"local_trial_permission_verified");using var p=System.Text.Json.JsonDocument.Parse(permit.ResultJson);if(p.RootElement.GetProperty("tower").GetString()!=tower||p.RootElement.GetProperty("configuration").GetString()!=configuration)throw new InvalidOperationException("地方试验许可版本不符。");
    }
    public async ValueTask<CommandResult> RegisterSafetyAsync(string safetyReceipt)
    {
        var s=await repository.SnapshotAsync();var receipt=BranchOperations.Evidence(s,safetyReceipt,"tower_safety_verified");var record=StrictJson.Deserialize<TowerSafety>(receipt.ResultJson);if(!Rules.Baseline.ContainsKey(record.Tower)||!record.Safe||string.IsNullOrWhiteSpace(record.SourceWork))throw new InvalidOperationException("实际塔站安全结果未核验。");
        return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,"tower-safety:"+record.Tower,"register_actual_tower_safety",[BranchOperations.Change(s,"readiness:"+record.Tower,"tower_readiness",Readiness(s,record.Tower) with{Safe=true})],["readiness:"+record.Tower],new{source_receipt=safetyReceipt}));
    }
    public async ValueTask<ImmutableArray<TowerReadiness>> ReadinessAsync(){var s=await repository.SnapshotAsync();return Rules.Baseline.Keys.Order().Select(t=>Readiness(s,t)).ToImmutableArray();}
    public async ValueTask<CommandResult> ConsumeProcurementStepAsync(string workReceipt)
    {
        var s=await repository.SnapshotAsync();BranchOperations.Safe(s);var receipt=BranchOperations.Evidence(s,workReceipt,"engineering_procurement_step_verified");var step=StrictJson.Deserialize<ProcurementStep>(receipt.ResultJson);
        if(!Rules.Sources.TryGetValue(step.Source,out var source)||source.Region!=step.Region||step.Step<1||step.Step>source.RequiredSteps||string.IsNullOrWhiteSpace(step.VerifiedWork))throw new InvalidOperationException("该工作回执不属于公开的有限来源。");
        string id="procurement:"+source.Id;var old=BranchOperations.Read<ProcurementProgress>(s,id);bool last=step.Step==source.RequiredSteps;
        if(old?.Delivered==true||old?.Step==step.Step)return new(CommitStatus.AlreadyCommitted,null,[]);
        if(step.Step!=(old?.Step??0)+1)throw new InvalidOperationException("先完成合同中前一项规定工作。");
        var next=new ProcurementProgress(source.Id,step.Step,last,workReceipt);ImmutableArray<BalanceChange> balances=last?[new(EngineeringRules.Available(source.Resource),source.Quantity)]:[];
        return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,"procurement:"+source.Id+":step:"+step.Step,last?"engineering_supply_delivered":"engineering_procurement_progress",[BranchOperations.Change(s,id,"procurement",next)],[id],new{source=source.Id,source.Resource,quantity=last?source.Quantity:0,source_work_receipt=workReceipt},balances:balances));
    }
    public async ValueTask<CommandResult> ReserveAsync(string tower,WorkType type,string configuration,string teamId)
    {
        var s=await repository.SnapshotAsync();BranchOperations.Safe(s);if(!BranchOperations.Receipt(s,"Q-404"))throw new InvalidOperationException("准备清单尚未正式发布。");
        string key=Key(tower,type);Rules.Required(tower,type);if(!Readiness(s,tower).Safe)throw new InvalidOperationException("先完成该塔的实际现场安全处置。");var old=BranchOperations.Read<TowerWork>(s,Entity(key));if(old is not null&&old.Stage!=WorkStage.Cancelled)throw new InvalidOperationException("同一准备包、塔与工作类型已存在，请继续原工作单。");
        var team=BranchOperations.Read<QualifiedWorkTeam>(s,"engineering-team:"+teamId)??throw new InvalidOperationException("实际班组及岗位尚未登记。");
        if(team.Region!=tower[4..]||!team.InstallationQualified||!team.OperatorsQualified||team.Operators.Length!=2||team.Operators.Distinct().Count()!=2)throw new InvalidOperationException("班组地区、安装能力或两个实际值守岗位不符。");
        ValidateTeam(s,team);
        var occupied=new[]{teamId,"installation:"+team.Region}.Concat(team.Operators).Distinct().ToArray();
        if(occupied.Any(actor=>s.Entities.Any(e=>e.Id=="workpost:"+actor&&e.Kind=="work_post")))throw new InvalidOperationException("所需班组、地区同期容量或值守者正在执行另一项工作。");
        foreach(string actor in team.Operators)
        {
            if(actor.StartsWith("AUX-",StringComparison.Ordinal))throw new InvalidOperationException("地方战斗援勤与工程值守是不同岗位。");
            if(BranchOperations.Entity(s,"duty:"+actor) is{Kind:"operator_duty"} duty){using var d=System.Text.Json.JsonDocument.Parse(duty.Json);if(d.RootElement.GetProperty("tower").GetString()!=tower)throw new InvalidOperationException("同一值守者不能同时承担另一塔的岗位。");}
        }
        if(type is WorkType.EnhancedInstall or WorkType.FieldTrial)
        {
            ValidateConfiguration(s,tower,configuration,type==WorkType.FieldTrial);
        }
        var resources=Rules.Required(tower,type);foreach(var r in resources)if(s.Balances.GetValueOrDefault(EngineeringRules.Available(r.Key))<r.Value)throw new InvalidOperationException("缺少实际工程库存："+r.Key+"，请继续公开的有限供给合同。");
        var next=new TowerWork(Preparation,tower,type,configuration,WorkStage.Reserved,resources,teamId,team.Operators.ToArray(),0);var held=resources.SelectMany(r=>new[]{new BalanceChange(EngineeringRules.Available(r.Key),-r.Value),new BalanceChange(EngineeringRules.Held(key,r.Key),r.Value)}).ToImmutableArray();
        string[] reads=new[]{Entity(key),"engineering-team:"+teamId,"technical-package","readiness:"+tower,"trial-permission:"+tower}.Concat(occupied.Select(a=>"workpost:"+a)).Concat(team.Operators.Select(a=>"duty:"+a)).ToArray();
        var changes=occupied.Select(a=>BranchOperations.Change(s,"workpost:"+a,"work_post",new{owner=key,team=teamId})).Append(BranchOperations.Change(s,Entity(key),"work_order",next)).ToImmutableArray();
        return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,key+":reserve:"+(BranchOperations.Entity(s,Entity(key))?.Revision??-1),"reserve_engineering_work",changes,reads,new{work=key,resources},balances:held));
    }
    public async ValueTask<CommandResult> NextStepAsync(string tower,WorkType type)
    {
        var s=await repository.SnapshotAsync();BranchOperations.Safe(s);string key=Key(tower,type);var old=BranchOperations.Read<TowerWork>(s,Entity(key))??throw new InvalidOperationException("工作单尚未预留。");
        if(old.Stage is WorkStage.Planned or WorkStage.Cancelled or WorkStage.Verified)throw new InvalidOperationException("当前工作阶段没有可执行的下一步。");
        var team=BranchOperations.Read<QualifiedWorkTeam>(s,"engineering-team:"+old.Crew)??throw new InvalidOperationException("班组记录不可用。");ValidateTeam(s,team);
        var post=BranchOperations.Entity(s,"workpost:"+old.Crew);if(post is not{Kind:"work_post"})throw new InvalidOperationException("该工作单已不再持有安装班组。");using(var doc=System.Text.Json.JsonDocument.Parse(post.Json))if(doc.RootElement.GetProperty("owner").GetString()!=key)throw new InvalidOperationException("班组正在执行另一工作单。");
        if(type is WorkType.EnhancedInstall or WorkType.FieldTrial)ValidateConfiguration(s,tower,old.Configuration,type==WorkType.FieldTrial);
        var stage=old.Stage switch{WorkStage.Reserved=>WorkStage.InTransit,WorkStage.InTransit=>WorkStage.Delivered,WorkStage.Delivered=>WorkStage.Installed,WorkStage.Installed=>WorkStage.Verified,_=>throw new InvalidOperationException()};
        var next=old with{Stage=stage,Step=old.Step+1};var changes=ImmutableArray.CreateBuilder<EntityChange>();var balances=ImmutableArray.CreateBuilder<BalanceChange>();changes.Add(BranchOperations.Change(s,Entity(key),"work_order",next));
        if(stage==WorkStage.Installed&&type!=WorkType.FieldTrial)foreach(var r in old.Resources){balances.Add(new(EngineeringRules.Held(key,r.Key),-r.Value));balances.Add(new(EngineeringRules.Installed(tower,r.Key),r.Value));}
        if(stage==WorkStage.Verified)
        {
            var ready=Readiness(s,tower);ready=type switch{WorkType.BaselineCheck=>ready with{Baseline=true,Operators=true},WorkType.EnhancedInstall=>ready with{EnhancedVersion=old.Configuration,Trial=false,TrialVersion=null},WorkType.TransitionSupply=>ready with{Transition=true},WorkType.OperatorAssignment=>ready with{Operators=true},WorkType.FieldTrial=>ready with{Trial=true,TrialVersion=old.Configuration},_=>throw new InvalidOperationException()};
            changes.Add(BranchOperations.Change(s,"readiness:"+tower,"tower_readiness",ready));foreach(string occupant in new[]{old.Crew,"installation:"+tower[4..]}.Concat(old.Operators).Distinct())changes.Add(BranchOperations.Change(s,"workpost:"+occupant,"released_post",new{owner=key,completed=true}));
            if(type is WorkType.BaselineCheck or WorkType.OperatorAssignment)foreach(string actor in old.Operators)changes.Add(BranchOperations.Change(s,"duty:"+actor,"operator_duty",new{tower,actor,source_work=key}));
            if(type==WorkType.FieldTrial)foreach(var r in old.Resources){balances.Add(new(EngineeringRules.Held(key,r.Key),-r.Value));balances.Add(new(EngineeringRules.Available(r.Key),r.Value));}
        }
        return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,key+":step:"+next.Step+":"+(BranchOperations.Entity(s,Entity(key))?.Revision??-1),stage==WorkStage.Verified?"engineering_work_result":"execute_engineering_work_step",changes.ToImmutable(),[Entity(key),"workpost:"+old.Crew,"readiness:"+tower,"engineering-team:"+old.Crew,"technical-package","trial-permission:"+tower],new{work=key,stage,configuration=old.Configuration,operators=old.Operators},balances:balances.ToImmutable()));
    }
    public async ValueTask<CommandResult> CancelAsync(string tower,WorkType type)
    {
        var s=await repository.SnapshotAsync();BranchOperations.Safe(s);string key=Key(tower,type);var old=BranchOperations.Read<TowerWork>(s,Entity(key))??throw new InvalidOperationException("没有该工作单。");
        if(old.Stage!=WorkStage.Reserved)throw new InvalidOperationException("只有尚未执行的预留可撤单；已运输或安装的原件需实际返运、交接或继续核验。");
        var refund=old.Resources.SelectMany(r=>new[]{new BalanceChange(EngineeringRules.Held(key,r.Key),-r.Value),new BalanceChange(EngineeringRules.Available(r.Key),r.Value)}).ToImmutableArray();
        var changes=new[]{old.Crew,"installation:"+tower[4..]}.Concat(old.Operators).Distinct().Select(a=>BranchOperations.Change(s,"workpost:"+a,"released_post",new{owner=key,cancelled=true})).Append(BranchOperations.Change(s,Entity(key),"work_order",old with{Stage=WorkStage.Cancelled})).ToImmutableArray();
        return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,key+":cancel:"+(BranchOperations.Entity(s,Entity(key))?.Revision??-1),"cancel_unused_engineering_reservation",changes,[Entity(key),"workpost:"+old.Crew],new{work=key},balances:refund));
    }
}

using Mist.Application.Commands;
using System.Collections.Immutable;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.ChapterFour;
using Mist.Core.Contracts;

namespace Mist.Application.ChapterFour;

public sealed partial class ChapterFourService
{
    private TrialConfiguration Configuration(ChapterFourState st,string controller)=>controller=="A"?new("A",st.OldLoad,st.OldReserve,st.OldIsolated,"local_bench","old_hall_operator","old_hall_recorder",st.OldRevision,0):new("C",st.IslandLoad,st.IslandReserve,st.IslandIsolation,st.IslandContact,st.TrialOperator,st.TrialReviewer,st.IslandRevision,st.OutputRevision);
    private static bool ConfigurationMatches(ExperimentRun run,TrialConfiguration current)=>run.Configuration==current;
    private static string RunId(Guid id)=>"R4:run:"+id;
    public async ValueTask<CommandResult> PrepareOldBenchStepAsync(string step)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R4-S05.bench");var st=State(s);Require(st.OldIsolated&&Result(s,"ENC-R4-01")&&Result(s,"ENC-R4-02")&&Sources(s,"R4-Q402-R-RECONSTRUCT","PZLAB-R-PROTOCOL"),"旧装置隔离、两领域安全与原始规程未成立。");var next=step switch{"secure_guard"=>st with{OldGuard=true},"inspect_socket"when st.OldGuard=>st with{OldSocketChecked=true},_=>throw new InvalidOperationException("实际旧台步骤尚未成立。")};return await Commit(s,"glasshaven_old_bench_prepared",[Change(s,next with{OldRevision=checked(st.OldRevision+1)})],new{step,actual_installed_fixture=true,not_engineering_reward=true});
    }
    public async ValueTask<CommandResult> ConfigureControllerAAsync(int load,bool reserve)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R4-S05.bench");var st=State(s);Require(st.OldIsolated&&st.OldGuard&&st.OldSocketChecked&&Result(s,"ENC-R4-01")&&Result(s,"ENC-R4-02"),"当前旧台实体准备未成立。");Require(load==40||load==70&&reserve,"负荷/储备组合未在冻结A表验证，不能猜测55或70无储备结果。");return await Commit(s,"glasshaven_controller_a_configured",[Change(s,st with{OldLoad=load,OldReserve=reserve,OldRevision=checked(st.OldRevision+1)})],new{controller="A",load,reserve,configured_not_executed=true});
    }
    public async ValueTask<CommandResult> StartExperimentAsync(string controller)
    {
        Require(controller is "A" or "C","控制器未登记。");var s=await _repository.SnapshotAsync();Safe(s);At(s,controller=="A"?"R4-S05.bench":"R4-S07.console");var st=State(s);Require(!Runs(s).Any(r=>r.Configuration.Controller==controller&&r.Outcome=="running"),"已有此装置运行；请观察或明确局部复位，不能用新入口刷新记录。");
        if(controller=="A")Require(st.OldIsolated&&st.OldGuard&&st.OldSocketChecked&&Result(s,"ENC-R4-01")&&Result(s,"ENC-R4-02")&&Sources(s,"PZLAB-R-PROTOCOL","R4-Q402-R-RECONSTRUCT"),"实际旧台与原文尚未准备。");else Require(Prepared(s,st)&&Result(s,"ENC-R4-04")&&OutputChecked(st)&&Sources(s,"R4-Q403-R-ISLAND","R4-Q403-R-OUTPUT"),"当前孤网准备、执行机制止或实际TEST输出未成立。");
        var run=new ExperimentRun(Guid.NewGuid(),Configuration(st,controller),0,[],"running",controller=="A"?"old_controller_reconstruction":"bounded_island_trial",controller=="A"?"r4-actual-controller-a-run":"r4-actual-controller-c-run");return await Commit(s,"glasshaven_experiment_started",[BranchOperations.Change(s,RunId(run.Id),"experiment_run",run)],run,"R4:run:"+run.Id+":started");
    }
    public async ValueTask<CommandResult> ObserveExperimentAsync(Guid runId,int expectedMinutes)
    {
        Require(expectedMinutes is 0 or 5,"观察步骤未登记。");var s=await _repository.SnapshotAsync();Safe(s);if(BranchOperations.Receipt(s,"R4:run:"+runId+":minute:"+(expectedMinutes+5)))return new(CommitStatus.AlreadyCommitted,null,[]);var run=BranchOperations.Read<ExperimentRun>(s,RunId(runId))??throw new InvalidOperationException("实际运行不存在。");At(s,run.Configuration.Controller=="A"?"R4-S05.bench":"R4-S07.console");Require(run.Outcome=="running"&&run.Minutes==expectedMinutes,"此运行已保存或当前观察步骤已改变。");var st=State(s);Require(ConfigurationMatches(run,Configuration(st,run.Configuration.Controller)),"当前配置已改变，不能替旧运行编写新配置读数；请保存局部复位记录。");
        int minute=run.Minutes+5;bool stable=run.Configuration.Load==40&&run.Configuration.Reserve&&run.Configuration.Isolation&&(run.Configuration.Controller=="A"||run.Configuration.Contact=="limited"&&run.Configuration.Operator=="N-SIAN"&&run.Configuration.Reviewer=="ROLE-R4-TRIAL-REVIEW");string observed=minute==5?"intermediate_not_final":stable?"stable":"safe_stopped";var next=run with{Minutes=minute,Observations=run.Observations.Add(new(minute,observed)),Outcome=minute==10?observed:"running"};return await Commit(s,minute==10?"glasshaven_experiment_observations_saved":"glasshaven_experiment_observed",[BranchOperations.Change(s,RunId(runId),"experiment_run",next)],next,"R4:run:"+runId+":minute:"+minute);
    }
    public async ValueTask<CommandResult> ResetExperimentAsync(string controller)
    {
        Require(controller is "A" or "C","控制器未登记。");var s=await _repository.SnapshotAsync();Safe(s);At(s,controller=="A"?"R4-S05.bench":"R4-S07.console");var st=State(s);var changes=ImmutableArray.CreateBuilder<EntityChange>();foreach(var running in Runs(s).Where(r=>r.Configuration.Controller==controller&&r.Outcome=="running"))changes.Add(BranchOperations.Change(s,RunId(running.Id),"experiment_run",running with{Outcome="stopped_by_local_reset"}));
        // Only current apparatus preparation changes. Completed historical runs, sources, gates, other districts and rewards remain.
        changes.Add(Change(s,controller=="A"?st with{OldRevision=checked(st.OldRevision+1),OldGuard=false,OldSocketChecked=false}:st with{IslandRevision=checked(st.IslandRevision+1),ConfigurationCheckRevision=null,OutputRevision=checked(st.OutputRevision+1),OutputCheckRevision=null}));return await Commit(s,"glasshaven_local_experiment_reset",changes.ToImmutable(),new{controller,historical_runs_retained=true,external_results_retained=true,logistics_latch_retained=true});
    }
    public async ValueTask<CommandResult> CompareTrialPairAsync(string question,string a,string b)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R4-S05.protocol");Require(Sources(s,"PZLAB-R-PROTOCOL"),"冻结试验表未实际取得。");var pair=new HashSet<string>([a,b]);Require(a!=b&&(question=="load"&&(pair.SetEquals(new[]{"T1","T2"})||pair.SetEquals(new[]{"T2","T4"}))||question=="reserve"&&pair.SetEquals(new[]{"T1","T3"})),"该组合不能回答所选单因素问题；T1/T3仍是有效储备比较，55保持未测。");var comparison=new TrialPair(question,a,b);return await Commit(s,"glasshaven_trial_pair_compared",[BranchOperations.Change(s,"R4:trial_pair","trial_pair",comparison)],comparison);
    }
    private bool CurrentStableRun(BranchSnapshot s)=>Runs(s).Any(r=>r.Configuration.Controller=="C"&&r.Minutes==10&&r.Outcome=="stable"&&ConfigurationMatches(r,Configuration(State(s),"C")))&&Prepared(s,State(s))&&OutputChecked(State(s));
    public async ValueTask<ExperimentRun?> ExperimentAsync(Guid id)=>BranchOperations.Read<ExperimentRun>(await _repository.SnapshotAsync(),RunId(id));
}

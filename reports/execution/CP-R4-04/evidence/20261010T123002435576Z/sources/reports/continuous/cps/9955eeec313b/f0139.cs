using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.ChapterFive;
using Mist.Core.ChapterFour;
using Mist.Core.ScopedPersonalWorks;

namespace Mist.Application.ScopedPersonalWorks;

public sealed partial class ScopedPersonalWorksService
{
    public static readonly ImmutableArray<string> RoleFields=["role_counts","source_version","source_root"];
    public static ImmutableArray<string> ReviewRecipients(string mode)=>mode switch
    {"independent_trustee_review"=>["ROLE-R5-INDEPENDENT-TRUSTEE"],"two_controlled_copies_cross_review"=>["ROLE-R5-CONTROLLED-CUSTODIAN-A","ROLE-R5-CONTROLLED-CUSTODIAN-B"],_=>throw new InvalidOperationException("原件只提供独立受托查验或双份受控交叉查验。")};
    private PersonalAccessExercise MiraExercise(BranchSnapshot s,bool allowClosed=false)
    {
        if(!allowClosed)Current(s,"CQ-MIRA-03");else Require(BranchOperations.Read<ChapterFiveState>(s,"chapter5") is{MiraInvited:true}&&Quest(s,"CQ-MIRA-03") is{Lifecycle:Mist.Core.Quests.QuestLifecycle.Active or Mist.Core.Quests.QuestLifecycle.Closed},"当前私人访问邀请未成立。");Material(s,"R5-CQMIRA03-R-EXERCISE");
        var e=BranchOperations.Read<PersonalAccessExercise>(s,"R5:personal_access_exercise")??throw new InvalidOperationException("请先沿原节点启动有限访问演练。");
        var copy=ReviewSource(s);var state=BranchOperations.Read<ChapterFiveState>(s,"chapter5")!;
        Require(PersonalProductionRules.Ready(s,"CQ-MIRA-02",false)&&e.SourceCopy==copy.Version&&!e.IdentitiesIncluded&&e.MainCustodyUnchanged==state.Custody?.Mode,"同一已交付纸件、来源或原主线保管改变。");return e;
    }
    // The player chooses a bounded question, never the other person's answer.
    // These are authored finite responses with current source/mode/recipient scope, not human consent.
    public async ValueTask<CommandResult> RequestMiraRenewalAnswerAsync(string actor,ImmutableArray<string> fields,ImmutableArray<string> recipients)
    {
        var s=await _repository.SnapshotAsync();Safe(s);await At(s,"R5-S07.personal_review");var q=Quest(s,"CQ-MIRA-03");var e=MiraExercise(s,true);
        Require(actor is "CP-MIRA" or "W-04"&&!fields.IsDefault&&!recipients.IsDefault&&fields.Distinct().Count()==fields.Length&&recipients.Distinct().Count()==recipients.Length,"问题需指定实际相关本人及明确字段、接收范围。");
        bool allow=fields.Order().SequenceEqual(RoleFields.Order())&&recipients.Order().SequenceEqual(ReviewRecipients(e.Mode).Order());
        var response=Package.Responses.Single(r=>r.Actor==actor&&r.Purpose==(allow?"renew_bounded_review":"deny_scope_expansion"));
        string id="personal-detail:mira03-answer:"+actor;var old=BranchOperations.Read<ActualMiraRenewalAnswer>(s,id);
        string invitation=PersonalProductionRules.CurrentInvitationReceipt(s,"CQ-MIRA-03")??throw new InvalidOperationException("当前实际私人邀请回执缺失。");var answer=new ActualMiraRenewalAnswer(q.Instance,actor,e.SourceCopy,e.Mode,Package.Version,fields,recipients,allow,checked((old?.Generation??0)+1),response.Fingerprint,invitation);
        return await Commit(s,"personal-detail:mira03:answer:"+actor+":"+answer.Generation,"mira_current_bounded_question_answered",[BranchOperations.Change(s,id,"mira_current_answer",answer)],answer,actor=="W-04"?"W-04":"CP-MIRA");
    }
    public async ValueTask<ScopedPersonalResponse> OpenMiraAnswerAsync(string actor)
    {
        var s=await _repository.SnapshotAsync();Material(s,"R5-CQMIRA03-R-EXERCISE");var e=BranchOperations.Read<PersonalAccessExercise>(s,"R5:personal_access_exercise")??throw new InvalidOperationException("当前有限演练记录缺失。");var a=PersonalProductionRules.CurrentMiraAnswer(s,actor,e.Mode,Package.Version)??throw new InvalidOperationException("本次范围还没有实际当前应答，或当前访问邀请已撤回。");
        return Package.Responses.Single(r=>r.Actor==actor&&r.Fingerprint==a.ResponseHash);
    }
    public async ValueTask<CommandResult> MakeMiraDrillCopiesAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);await At(s,"R5-S07.personal_review");var q=Current(s,"CQ-MIRA-03");var e=MiraExercise(s);var original=ReviewSource(s);
        Require(new[]{"CP-MIRA","W-04"}.All(a=>PersonalProductionRules.CurrentMiraAnswer(s,a,e.Mode,Package.Version) is{Allowed:true}),"两个相关本人须分别对当前必要字段、当前接收方作有限应答，不能由玩家勾选接受。");
        Require(BranchOperations.Entity(s,"personal-detail:mira03-drill") is null,"当前工作已有实际受控副件，不能另建一套或重置记录。");
        var recipients=ReviewRecipients(e.Mode);var copies=Enumerable.Range(1,recipients.Length).Select(n=>"MIRA03-CONTROLLED-COPY-"+n).ToImmutableArray();
        Require(copies.All(id=>s.Items.All(i=>i.Id!=id)),"有限受控副件序号已占用。");
        var drill=new ActualMiraDrill(q.Instance,e.Mode,copies,original.SourceRoot,original.Version,e.MainCustodyUnchanged,"copies_in_named_custody",1);
        var items=copies.Select((id,i)=>new ItemChange(id,"MIRA-ROLE-SOURCE-CONTROLLED-COPY","new","","warehouse",recipients[i],null)).ToImmutableArray();
        return await Commit(s,"personal-detail:mira03:copies-made","mira_current_controlled_copies_received",[BranchOperations.Change(s,"personal-detail:mira03-drill","mira_actual_drill",drill)],drill,"CP-MIRA",items);
    }
    private ActualMiraDrill Drill(BranchSnapshot s)
    {
        var e=MiraExercise(s);var d=BranchOperations.Read<ActualMiraDrill>(s,"personal-detail:mira03-drill")??throw new InvalidOperationException("实际受控副件还未交到指定接收范围。");
        Require(d.NodeInstance==Quest(s,"CQ-MIRA-03").Instance&&d.Mode==e.Mode&&d.MainCustody==e.MainCustodyUnchanged&&d.OriginalSourceRoot==ReviewSource(s).SourceRoot,"演练配置或原来源改变。");return d;
    }
    public async ValueTask<CommandResult> AttemptMiraReadAsync(string recipient,ImmutableArray<string> fields,string kind="review")
    {
        var s=await _repository.SnapshotAsync();Safe(s);await At(s,"R5-S07.personal_review");var d=Drill(s);Require(kind is "review" or "future_contact"&&!fields.IsDefault,"请明确实际访问种类和字段。");
        var answers=new[]{"CP-MIRA","W-04"}.Select(a=>PersonalProductionRules.CurrentMiraAnswer(s,a,d.Mode,Package.Version)).ToArray();
        bool current=answers.All(a=>a is{Allowed:true});var gens=answers.Where(a=>a is not null).ToImmutableDictionary(a=>a!.Actor,a=>a!.Generation);
        bool destination=ReviewRecipients(d.Mode).Contains(recipient);bool bounded=fields.Length==RoleFields.Length&&fields.Distinct().Count()==fields.Length&&fields.All(RoleFields.Contains);
        bool future=BranchOperations.Read<MiraFutureExit>(s,"personal-detail:mira03-exit")?.FutureContactAllowed??true;
        bool allowed=current&&destination&&bounded&&(kind!="future_contact"||future);
        string reason=!current?"current_scope_denied":!destination?"recipient_outside_scope":!bounded?"fields_outside_scope":kind=="future_contact"&&!future?"future_contact_withdrawn":"within_current_bounded_scope";
        var p=new MiraPolicyProbe(d.NodeInstance,kind,recipient,fields,allowed,reason,d.Configuration,gens);
        string id=kind=="future_contact"?"personal-detail:mira03-probe:future":allowed?"personal-detail:mira03-probe:allowed":"personal-detail:mira03-probe:denied";
        return await Commit(s,"personal-detail:mira03:probe:"+Guid.NewGuid(),"mira_current_scope_read_attempted",[BranchOperations.Change(s,id,"mira_policy_probe",p)],p,"CP-MIRA");
    }
    public async ValueTask<CommandResult> WithdrawFutureMiraContactAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);await At(s,"R5-S07.personal_review");var d=Drill(s);
        var e=new MiraFutureExit(d.NodeInstance,false,false,d.OriginalVersion,d.MainCustody);
        return await Commit(s,"personal-detail:mira03:future-contact-withdrawn","mira_optional_future_contact_withdrawn",[BranchOperations.Change(s,"personal-detail:mira03-exit","mira_future_exit",e)],e,"W-04");
    }
    public async ValueTask<CommandResult> VerifyMiraExitAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);await At(s,"R5-S07.personal_review");var d=Drill(s);var exit=BranchOperations.Read<MiraFutureExit>(s,"personal-detail:mira03-exit");var test=BranchOperations.Read<MiraPolicyProbe>(s,"personal-detail:mira03-probe:future");
        Require(exit is{FutureContactAllowed:false}&&test is{Kind:"future_contact",Allowed:false,Reason:"future_contact_withdrawn"}&&test.NodeInstance==d.NodeInstance,"实际关闭未来联系后，须确实尝试并观察拒绝；退出不删除合法历史。");
        var next=exit! with{ActualTestDenied=true};return await Commit(s,"personal-detail:mira03:exit-verified","mira_future_exit_actually_verified",[BranchOperations.Change(s,"personal-detail:mira03-exit","mira_future_exit",next)],next,"CP-MIRA");
    }
    public async ValueTask<CommandResult> AppendMiraCorrectionAsync(string earlierInterpretation,string correctedInterpretation)
    {
        var s=await _repository.SnapshotAsync();Safe(s);await At(s,"R5-S07.personal_review");var d=Drill(s);
        Require(PersonalProductionRules.StepReady(s,"test_optional_exit")&&earlierInterpretation=="role_entries_are_identity_count"&&correctedInterpretation=="role_entries_only_identity_count_unknown","追加纠正的是解释；原三条登记、2条观察/1条服务和原版本均保留，不能编造错误原件。");
        var c=new MiraCorrection(d.NodeInstance,earlierInterpretation,"interpretation@1",correctedInterpretation,"interpretation@2",d.OriginalVersion,d.OriginalSourceRoot,true,false);
        return await Commit(s,"personal-detail:mira03:correction-appended","mira_bounded_interpretation_correction_appended",[BranchOperations.Change(s,"personal-detail:mira03-correction","mira_actual_correction",c)],c,"CP-MIRA");
    }
}

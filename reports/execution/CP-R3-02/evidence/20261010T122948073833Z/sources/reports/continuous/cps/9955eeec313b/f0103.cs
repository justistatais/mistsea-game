using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Roster;
using Mist.Application.Storage;
using Mist.Core.ChapterThree;
using Mist.Core.Contracts;
using Mist.Core.Quests;

namespace Mist.Application.ChapterThree;

public sealed record AuxiliaryRepair(string Kit,string Stage,long Configuration,bool TestPassed);
public sealed record AlarmReviewInvitation(string Reviewer,string Scene,string Spot,long Configuration,string Purpose);
public sealed partial class ChapterThreeService
{
    public async ValueTask<CommandResult> DecideOrenInvitationAsync(bool? join)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R3-S03.oren");Require(Closed(s,"Q-302")&&BranchOperations.Entity(s,"finding:R3-OREN-ACCEPTANCE") is not null,"正式邀请须在实际三方核验责任结论之后。");if(join is null)return await Commit(s,"chapter_three_oren_invitation_deferred",[],new{decision="defer",main_not_blocked=true});
        if(_repository is not Mist.Application.EventScheduler.IEventLeaseStore leases)throw new InvalidOperationException("正式邀请需要原子前景占用接口。");string owner="chapter3:oren:"+Guid.NewGuid();Require(await leases.TryLeasesAsync(_repository.Session.Load,owner,["input:foreground"]),"另一项前景工作尚未交接。");try{var roster=new RosterService(_repository,_catalog);var invited=await roster.InviteAsync("CP-OREN");Require(invited.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,"正式邀请未提交。");return await roster.DecideAsync("CP-OREN",join.Value);}finally{await leases.ReleaseLeasesAsync(_repository.Session.Load,owner);}
    }
    public async ValueTask<CommandResult> DecideAlarmInvitationAsync(string decision)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R3-S03.alarm_invite");Require(Closed(s,"Q-302"),"个人旁修须在核查责任之后。");Require(decision is "accept" or "decline" or "defer" or "withdraw","请明确邀请决定。");Require(BranchOperations.Entity(s,"workpost:CP-OREN") is not{Kind:"work_post"},"已承担实际维修须先修复并独立交接，不能在工作中撤销占用。");
        var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(Change(s,State(s) with{AlarmInvitation=decision}));if(decision=="accept"&&Quest(s,"CQ-OREN-01") is{Lifecycle:QuestLifecycle.Unaccepted} q)changes.Add(BranchOperations.Change(s,"quest:CQ-OREN-01","quest",q.Accept()));if(decision=="accept"&&Closed(s,"CQ-OREN-01")&&Closed(s,"Q-304")&&Quest(s,"CQ-OREN-02") is{Lifecycle:QuestLifecycle.Unaccepted} q2)changes.Add(BranchOperations.Change(s,"quest:CQ-OREN-02","quest",q2.Accept()));
        return await Commit(s,"chapter_three_alarm_invitation_decided",changes.ToImmutable(),new{decision,formal_joining_required=false,prior_receipts_and_drafts_retained=true});
    }
    public async ValueTask<CommandResult> CommitAuxiliaryScopeAsync(string scope,string promise,bool preserveOriginalResponsibility)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R3-S03.alarm_check");Require(State(s).AlarmInspected&&State(s).AlarmInvitation=="accept"&&scope=="auxiliary_residential_alarm_only"&&promise=="repair_and_independent_review"&&preserveOriginalResponsibility,"具体旁修承诺须限定辅助住区报警器、独立复核，保留未修旁路验收责任。");return await Commit(s,"chapter_three_auxiliary_scope_committed",[Change(s,State(s) with{AlarmPromise=true}),BranchOperations.Change(s,"R3:alarm_scope","repair_scope",new{scope,promise,preserve_original_responsibility=true,main_tower_resources=false})],new{scope,promise,preserve_original_responsibility=true},"R3:alarm_scope");
    }
    public async ValueTask<CommandResult> BeginAuxiliaryRepairAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R3-S03.alarm_repair");Require(Closed(s,"Q-304")&&Closed(s,"CQ-OREN-01")&&State(s).AlarmInvitation=="accept"&&Sources(s,"R3-CQOREN02-R-PARTS"),"正式塔站工作、具体承诺或独立旁修件来源缺失。");Require(Quest(s,"CQ-OREN-02") is{Lifecycle:QuestLifecycle.Active}&&!State(s).AlarmRepair,"旁修未开启或已修复。");Require(!s.Entities.Any(e=>e.Id=="workpost:CP-OREN"&&e.Kind=="work_post"||e.Id=="duty:CP-OREN"&&e.Kind=="operator_duty"),"奥伦已有实际工作，不能双岗。");var old=BranchOperations.Read<RosterEntry>(s,"roster:CP-OREN")??new("CP-OREN",Membership.Contact,ActorAvailability.LocalContact,"R3-S03",ImmutableDictionary<int,string>.Empty);Require(old.Availability is ActorAvailability.LocalContact or ActorAvailability.ShipStandby,"奥伦当前不可承担地方旁修。");var kit=s.Items.Single(i=>i.Id=="R3-local:aux_alarm_kit:1");Require(kit.OwnerKind=="warehouse"&&kit.OwnerId=="R3-S08","独立旁修件已被挪用；不能扣六塔或民生工包。");
        return await Commit(s,"chapter_three_auxiliary_repair_started",[BranchOperations.Change(s,"R3:aux_repair","auxiliary_repair",new AuxiliaryRepair(kit.Id,"inspect_compatibility",State(s).AlarmRevision,false)),BranchOperations.Change(s,"workpost:CP-OREN","work_post",new{owner="R3:aux_repair",team=new[]{"CP-OREN"}}),BranchOperations.Change(s,"R3:oren_before_aux","roster_backup",old),BranchOperations.Change(s,"roster:CP-OREN","roster",old with{Availability=ActorAvailability.Working,Location="R3-S03"})],new{own_local_kit=kit.Id,formal_recruitment_required=false,old_signature_preserved=true},"R3:aux_repair_started",[new(kit.Id,kit.DefinitionId,kit.OwnerKind,kit.OwnerId,"warehouse","R3:aux_repair",null)]);
    }
    public async ValueTask<CommandResult> PerformAuxiliaryRepairStepAsync(string step)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R3-S03.alarm_repair");var old=BranchOperations.Read<AuxiliaryRepair>(s,"R3:aux_repair")??throw new InvalidOperationException("实际旁修未开始。");var st=State(s);Require(BranchOperations.Entity(s,"workpost:CP-OREN") is{Kind:"work_post"}&&st.AlarmInvitation=="accept"&&!st.AlarmRepair,"当前奥伦岗位或实际维修状态不符。");string next=step switch{"inspect_compatibility"when old.Stage==step=>"isolate","isolate"when old.Stage==step=>"fit_own_kit","fit_own_kit"when old.Stage==step=>"test_alarm","test_alarm"when old.Stage==step=>"ready_for_independent_review",_=>throw new InvalidOperationException("须实际核对兼容、隔离、安装独立件、测试报警回路。")};var changes=ImmutableArray.CreateBuilder<EntityChange>();var items=ImmutableArray.CreateBuilder<ItemChange>();long revision=checked(st.AlarmRevision+1);changes.Add(BranchOperations.Change(s,"R3:aux_repair","auxiliary_repair",old with{Stage=next,Configuration=revision,TestPassed=step=="test_alarm"}));changes.Add(Change(s,st with{AlarmRevision=revision,AlarmRepair=step=="test_alarm",AlarmReviewed=false,AlarmReviewRevision=null}));if(step=="fit_own_kit"){var kit=s.Items.Single(i=>i.Id==old.Kit);Require(kit.OwnerKind=="warehouse"&&kit.OwnerId=="R3:aux_repair","实际独立旁修件不在本项维修。");items.Add(new(kit.Id,kit.DefinitionId,kit.OwnerKind,kit.OwnerId,"installed","R3:auxiliary_alarm",null));}return await Commit(s,"chapter_three_auxiliary_repair_step",changes.ToImmutable(),new{step,configuration=revision,main_materials_untouched=true},items:items.ToImmutable());
    }
    public async ValueTask<CommandResult> InviteIndependentAlarmReviewAsync(string reviewer)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R3-S02.pera");var st=State(s);Require(reviewer=="N-PERA"&&st.AlarmRepair&&st.AlarmReviewRequested,"必须请实际地方接收员独立复核已测试的维修，奥伦不能自审。");return await Commit(s,"chapter_three_alarm_reviewer_invited",[BranchOperations.Change(s,"R3:alarm_review_invitation","review_invitation",new AlarmReviewInvitation(reviewer,"R3-S03","R3-S03.alarm_review",st.AlarmRevision,"independent_auxiliary_alarm_test"))],new{reviewer,accepted_local_work_invitation=true,configuration=st.AlarmRevision});
    }
    public async ValueTask<CommandResult> ConfirmIndependentAlarmReviewAsync(string reviewer)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R3-S03.alarm_review");var st=State(s);var invitation=BranchOperations.Read<AlarmReviewInvitation>(s,"R3:alarm_review_invitation");var repair=BranchOperations.Read<AuxiliaryRepair>(s,"R3:aux_repair");Require(reviewer=="N-PERA"&&invitation is{Reviewer:"N-PERA",Scene:"R3-S03",Spot:"R3-S03.alarm_review"}&&invitation.Configuration==st.AlarmRevision&&repair is{TestPassed:true,Stage:"ready_for_independent_review"}&&repair.Configuration==st.AlarmRevision&&st.AlarmReviewRequested,"真实独立复核邀请、同一配置测试或到场范围无效，不能由奥伦自审。");var prior=BranchOperations.Read<RosterEntry>(s,"R3:oren_before_aux")??throw new InvalidOperationException("真实工作前身份不存在。");return await Commit(s,"chapter_three_alarm_independently_reviewed",[Change(s,st with{AlarmReviewed=true,AlarmReviewRevision=st.AlarmRevision}),BranchOperations.Change(s,"workpost:CP-OREN","released_post",new{owner="R3:aux_repair",completed=true}),BranchOperations.Change(s,"roster:CP-OREN","roster",prior)],new{reviewer,configuration=st.AlarmRevision,repair_confirmed=true,forgiveness_required=false,original_acceptance_responsibility_retained=true},"R3:alarm_independent_review");
    }
}


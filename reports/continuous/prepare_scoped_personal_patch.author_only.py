"""Generate reviewable pending patches only. --apply is reserved for Root after review.
No compiler, tests, content importer, baseline or task ledger is invoked or modified.
"""
from pathlib import Path
import difflib
import sys

ROOT=Path(__file__).resolve().parents[2]
RULE='Mist.Application.ScopedPersonalWorks.PersonalProductionRules'
changes={}

def load(path):
    if path not in changes:
        text=(ROOT/path).read_text(encoding='utf-8-sig')
        changes[path]=[text,text]
    return changes[path][1]

def replace(path,old,new):
    text=load(path)
    if text.count(old)!=1:
        raise RuntimeError(f'{path}: expected one exact anchor; got {text.count(old)} for {old[:70]}')
    changes[path][1]=text.replace(old,new,1)

p='src/Mist.Application/ChapterFour/ChapterFourService.Submissions.cs'
replace(p,'case "CQ-MIRA-02":\n',f'case "CQ-MIRA-02":\n                Check({RULE}.Ready(s,quest),"actual_personal_paper_missing","须同一有限纸件已封口并实际交具名复核台；正文、派生实体或旧fixture不能替代。");\n')
replace(p,'Reads(s),result,balances:',f'Reads(s),result,items:{RULE}.ItemClaims(s,quest),balances:')
p='src/Mist.Application/ChapterFive/ChapterFiveService.Submissions.cs'
for q in ['CQ-MIRA-03','CQ-OREN-03']:
    replace(p,f'case "{q}":\n',f'case "{q}":\n                Check({RULE}.Ready(s,quest),"actual_personal_work_missing","须当前实际必要字段、限阅演练或同配置设备工作与具名独立接收；步骤名与本人接受勾选不能制造事实。");\n')
replace(p,'var command=BranchOperations.Plan(_repository,s,quest+":resolution",',f'items=items.AddRange({RULE}.ItemClaims(s,quest));\n        var command=BranchOperations.Plan(_repository,s,quest+":resolution",')
p='src/Mist.Application/ChapterFive/ChapterFiveService.Personal.cs'
replace(p,'string[] steps=["check_role_fields",',f'Require({RULE}.StepReady(s,step),"本步骤尚无实际当前演练凭证；请先操作纸件、退出或限阅访问。");string[] steps=["check_role_fields",')
replace(p,'var next=actor=="CP-MIRA"?',f'Require({RULE}.ArrangementAnswerReady(s,actor,decision),"本人决定必须与当前实际有限问题应答、邀请世代及接收范围一致。");var next=actor=="CP-MIRA"?')
replace(p,'roster with{Availability=ActorAvailability.Working,Location=mode=="rotation_local_maintenance"?"R3-S08":"HUB-S03"}', 'roster with{Availability=ActorAvailability.Working}')
replace(p,'var original=BranchOperations.Read<RosterEntry>(s,"R5:oren_before_duty")',f'Require({RULE}.HandoffReady(s),"须当前同一设备、独立接替实际接收及原位置安全返程；不能仅凭接收方字符串释放岗位。");var original=BranchOperations.Read<RosterEntry>(s,"R5:oren_before_duty")')

p='tests/Mist.Tests/ChapterFour/ChapterFourCases.cs'
old=next(line for line in load(p).splitlines() if 'using var s=Store("mira")' in line)
new='''        {using var s=Store("mira");var detail=Mist.Tests.ScopedPersonalWorks.ScopedPersonalWorkCases.MainComplete(s);detail.Mira01();detail.Mira02(false);var r=new Run(s,detail.Four);r.At("R4-S04.w04_copy");Throws<InvalidOperationException>(()=>Done(r.Service.GenerateReviewCopyAsync("R4-review-desk","verify_role_counts","W04-review-v1",["role_counts","source_version","source_root","identity_key"])));r.Meaning("CQ-MIRA-02",("fields","role_counts_and_source_only"),("recipient","R4-review-desk"),("purpose","verify_role_counts"),("identity_publication","not_permitted"),("copy_independence","same_source"));var p=r.Submit("CQ-MIRA-02");Require(Done(r.Service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.AlreadyCommitted&&r.View.Snapshot.Balances["experience"]==10560&&Read<DerivedReviewCopy>(s,"R4:mira_review_copy") is{IdentitiesIncluded:false,Observers:2,Service:1}&&r.View.Snapshot.Receipts.Count(x=>x.SemanticKey=="CQ-MIRA-02:resolution")==1&&r.View.Snapshot.Receipts.All(x=>!x.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal)));});'''
replace(p,old,new)

p='tests/Mist.Tests/ChapterFive/ChapterFiveCases.cs'
names=['"mira"','"mira-consent"','"oren"','"oren-withdraw"']
for name in names:
    old=next(line for line in load(p).splitlines() if 'Add("CP-R5-04"' in line and 'using var s=Store('+name+')' in line)
    if name=='"mira"':
        new='''        Add("CP-R5-04","Personal record exercise keeps main custody and original history, withdraws before confirm, resumes same instance",()=>{using var s=Store("mira");var detail=Mist.Tests.ScopedPersonalWorks.ScopedPersonalWorkCases.NormalPriorComplete(s);var r=new Run(s,detail.Five);string custody=r.View.State.Custody!.Mode;detail.Mira03("two_controlled_copies_cross_review");var instance=r.View.Quests.Single(q=>q.Id=="CQ-MIRA-03").Instance;var p=Done(r.Service.PreviewSubmissionAsync("CQ-MIRA-03"));Require(p.Ready);Success(Done(r.Service.DecidePersonalInvitationAsync("CQ-MIRA-03","withdraw")));Require(Done(r.Service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.Rejected);Throws<InvalidOperationException>(()=>Done(r.Service.PerformPersonalAccessStepAsync("test_restricted_review")));Require(!r.View.Materials.Any(m=>m.Id=="R5-CQMIRA03-R-EXERCISE"));Success(Done(r.Service.DecidePersonalInvitationAsync("CQ-MIRA-03","accept")));Require(r.View.Quests.Single(q=>q.Id=="CQ-MIRA-03").Instance==instance&&!Mist.Application.ScopedPersonalWorks.PersonalProductionRules.Ready(r.View.Snapshot,"CQ-MIRA-03"));detail.RefreshMiraConsent("two_controlled_copies_cross_review");r.Meaning("CQ-MIRA-03",("mode","two_controlled_copies_cross_review"),("access","role_counts_source_only"),("correction","append_keep_original"),("exit","future_contact_only_not_destroy_evidence"),("main_custody","unchanged"));var completed=r.Submit("CQ-MIRA-03");Require(r.View.Snapshot.Balances["experience"]==10660&&r.View.State.Custody!.Mode==custody&&Done(r.Service.ConfirmPreviewAsync(completed.Token)).Status==CommitStatus.AlreadyCommitted&&Read<PersonalAccessExercise>(s,"R5:personal_access_exercise").Steps.Length==4);});'''
    elif name=='"mira-consent"':
        new='''        Add("CP-R5-04","Both personal record participants must accept after actual four-step exercise",()=>{using var s=Store("mira-consent");var detail=Mist.Tests.ScopedPersonalWorks.ScopedPersonalWorkCases.NormalPriorComplete(s);var r=new Run(s,detail.Five);detail.StartMira03("independent_trustee_review");Throws<InvalidOperationException>(()=>Done(r.Service.RecordPersonalArrangementDecisionAsync("W-04","accept")));detail.Mira03Steps("independent_trustee_review");Success(Done(r.Service.RecordPersonalArrangementDecisionAsync("CP-MIRA","accept")));Success(Done(detail.Detail.RequestMiraRenewalAnswerAsync("W-04",["private_address"],["public_notice_board"])));Throws<InvalidOperationException>(()=>Done(r.Service.RecordPersonalArrangementDecisionAsync("W-04","accept")));Success(Done(r.Service.RecordPersonalArrangementDecisionAsync("W-04","decline")));Require(!r.View.State.MiraArrangementAccepted&&r.View.Snapshot.Balances["experience"]==10620&&!Mist.Application.ScopedPersonalWorks.PersonalProductionRules.Ready(r.View.Snapshot,"CQ-MIRA-03"));});'''
    elif name=='"oren"':
        new='''        Add("CP-R5-04","Oren's accepted remote or local post cannot overlap battle and hands off without deleting responsibility",()=>{using var s=Store("oren");var detail=Mist.Tests.ScopedPersonalWorks.ScopedPersonalWorkCases.NormalPriorComplete(s);var r=new Run(s,detail.Five);detail.Oren03("rotation_local_maintenance");var roster=new RosterService(s,Catalog);Throws<InvalidOperationException>(()=>Done(roster.SelectAsync(["CP-OREN","AUX-SUPPORT"])));var p=r.Submit("CQ-OREN-03");Require(r.View.Snapshot.Balances["experience"]==10660&&Read<OrenMaintenancePost>(s,"R5:oren_duty").OriginalResponsibilityPreserved&&Done(r.Service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.AlreadyCommitted);detail.HandoffOren();Require(Read<OrenMaintenancePost>(s,"R5:oren_duty").Status=="handed_off"&&Done(s.SnapshotAsync()).Entities.Single(e=>e.Id=="workpost:CP-OREN").Kind=="released_post"&&r.View.Snapshot.Balances["experience"]==10660&&Read<Mist.Core.ScopedPersonalWorks.OrenActualPosition>(s,"personal-detail:oren03-position") is{Scene:"R3-S03"} pos&&pos.Walks.Length>0);});'''
    else:
        new='''        Add("CP-R5-04","Oren invitation withdrawal rejects a pending private confirmation while retaining actual accepted duty",()=>{using var s=Store("oren-withdraw");var detail=Mist.Tests.ScopedPersonalWorks.ScopedPersonalWorkCases.NormalPriorComplete(s);var r=new Run(s,detail.Five);detail.Oren03();var p=Done(r.Service.PreviewSubmissionAsync("CQ-OREN-03"));Require(p.Ready);Success(Done(r.Service.DecidePersonalInvitationAsync("CQ-OREN-03","withdraw")));Require(Done(r.Service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.Rejected&&Read<OrenMaintenancePost>(s,"R5:oren_duty").Status=="accepted_current");Throws<InvalidOperationException>(()=>Done(r.Service.ConfirmMeaningAsync("CQ-OREN-03",ImmutableDictionary<string,string>.Empty,true)));detail.HandoffOren();Require(r.View.Snapshot.Balances["experience"]==10620);});'''
    replace(p,old,new)

out=ROOT/'reports/continuous/scoped_personal_consumers.patch.pending'
patch=''.join(''.join(difflib.unified_diff(before.splitlines(True),after.splitlines(True),fromfile='a/'+path,tofile='b/'+path)) for path,(before,after) in changes.items())
out.write_text(patch,encoding='utf-8')
print(f'PENDING: {len(changes)} existing files, {len(patch)} chars; no original writes.')
if '--apply' in sys.argv:
    for path,(_,after) in changes.items():
        (ROOT/path).write_text(after,encoding='utf-8')
    print('Root review requested apply: consumer/helper changes written; no build/test run.')

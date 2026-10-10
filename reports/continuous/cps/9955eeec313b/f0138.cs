using Mist.Application.ChapterThree;
using Mist.Application.Commands;
using Mist.Application.Roster;
using Mist.Application.Storage;
using Mist.Core.ChapterFive;
using Mist.Core.ChapterFour;
using Mist.Core.ChapterThree;
using Mist.Core.ChapterTwo;
using Mist.Core.Contracts;
using Mist.Core.Quests;
using Mist.Core.ScopedPersonalWorks;
using System.Text.Json;
using System.Collections.Immutable;

namespace Mist.Application.ScopedPersonalWorks;

// Existing chapter consumers call these before changing any original CQ reward or closure.
public static class PersonalProductionRules
{
    public const string DetailVersion="SCOPED-PERSONAL-20261010-v1";
    public static ImmutableArray<ItemChange> ItemClaims(BranchSnapshot s,string node)
    {
        IEnumerable<string> ids=node switch
        {"CQ-MIRA-02"=>[ScopedPersonalWorksService.ReviewPaper],"CQ-MIRA-03"=>(Read<ActualMiraDrill>(s,"personal-detail:mira03-drill")?.Copies??[]).Append(ScopedPersonalWorksService.ReviewPaper),"CQ-OREN-03"=>Read<ActualOrenWork>(s,"personal-detail:oren03-work") is{} w?[w.Kit]:[],_=>[]};
        return ids.Select(id=>s.Items.Single(i=>i.Id==id)).Select(i=>new ItemChange(i.Id,i.DefinitionId,i.OwnerKind,i.OwnerId,i.OwnerKind,i.OwnerId,i.Slot)).ToImmutableArray();
    }
    private static T? Read<T>(BranchSnapshot s,string id) where T:class=>BranchOperations.Read<T>(s,id);
    private static QuestState? Quest(BranchSnapshot s,string id)=>Read<QuestState>(s,"quest:"+id);
    private static bool Evidence<T>(BranchSnapshot s,string action,T value)=>s.Receipts.Any(r=>r.ActionType==action&&r.ResultJson==StrictJson.Serialize(value));
    public static string? CurrentInvitationReceipt(BranchSnapshot s,string node)
    {
        foreach(var r in s.Receipts.Where(r=>r.ActionType=="old_city_private_invitation_decided").OrderByDescending(r=>r.Sequence))
        {using var d=JsonDocument.Parse(r.ResultJson);var j=d.RootElement;if(j.TryGetProperty("quest",out var q)&&q.GetString()==node)return j.GetProperty("decision").GetString()=="accept"?r.ReceiptId:null;}return null;
    }
    public static ActualMiraRenewalAnswer? CurrentMiraAnswer(BranchSnapshot s,string actor,string mode,string version)
    {
        var a=Read<ActualMiraRenewalAnswer>(s,"personal-detail:mira03-answer:"+actor);
        return Read<ChapterFiveState>(s,"chapter5") is{MiraInvited:true}&&a is not null&&a.NodeInstance==Quest(s,"CQ-MIRA-03")?.Instance&&a.Actor==actor&&a.SourceCopy=="W04-review-v1"&&a.Mode==mode&&a.Version==version&&a.Generation>0&&a.InvitationReceipt==CurrentInvitationReceipt(s,"CQ-MIRA-03")&&Evidence(s,"mira_current_bounded_question_answered",a)?a:null;
    }
    private static bool CurrentProbe(BranchSnapshot s,MiraPolicyProbe? p,bool allowed)
    {
        var d=Read<ActualMiraDrill>(s,"personal-detail:mira03-drill");return d is not null&&p is not null&&p.NodeInstance==d.NodeInstance&&p.Configuration==d.Configuration&&p.Allowed==allowed&&Evidence(s,"mira_current_scope_read_attempted",p)&&new[]{"CP-MIRA","W-04"}.All(actor=>CurrentMiraAnswer(s,actor,d.Mode,DetailVersion) is{Allowed:true} a&&p.ConsentGenerations.GetValueOrDefault(actor)==a.Generation);
    }
    public static bool StepReady(BranchSnapshot s,string step)
    {
        var d=Read<ActualMiraDrill>(s,"personal-detail:mira03-drill");if(d is null||d.NodeInstance!=Quest(s,"CQ-MIRA-03")?.Instance)return false;
        if(step=="check_role_fields")return CurrentProbe(s,Read<MiraPolicyProbe>(s,"personal-detail:mira03-probe:allowed"),true);
        if(step=="test_optional_exit")return Read<MiraFutureExit>(s,"personal-detail:mira03-exit") is{FutureContactAllowed:false,ActualTestDenied:true} e&&e.NodeInstance==d.NodeInstance&&e.OriginalReviewCopyRetained==d.OriginalVersion&&e.MainCustody==d.MainCustody&&Evidence(s,"mira_future_exit_actually_verified",e)&&CurrentProbe(s,Read<MiraPolicyProbe>(s,"personal-detail:mira03-probe:future"),false);
        if(step=="append_correction")return Read<MiraCorrection>(s,"personal-detail:mira03-correction") is{EarlierInterpretation:"role_entries_are_identity_count",EarlierVersion:"interpretation@1",CorrectedInterpretation:"role_entries_only_identity_count_unknown",CorrectedVersion:"interpretation@2",OriginalRetained:true,IndependentSource:false} c&&c.NodeInstance==d.NodeInstance&&c.OriginalCopy==d.OriginalVersion&&c.OriginalSourceRoot==d.OriginalSourceRoot&&Evidence(s,"mira_bounded_interpretation_correction_appended",c);
        if(step=="test_restricted_review")return CurrentProbe(s,Read<MiraPolicyProbe>(s,"personal-detail:mira03-probe:denied"),false)&&Read<MiraPolicyProbe>(s,"personal-detail:mira03-probe:denied") is{Kind:"review",Reason:"recipient_outside_scope" or "fields_outside_scope"};
        return false;
    }
    public static bool ArrangementAnswerReady(BranchSnapshot s,string actor,string decision)
    {
        var e=Read<PersonalAccessExercise>(s,"R5:personal_access_exercise");var a=e is null?null:CurrentMiraAnswer(s,actor,e.Mode,DetailVersion);
        return a is not null&&decision==(a.Allowed?"accept":"decline");
    }
    public static bool Ready(BranchSnapshot s,string node,bool requireInvitation=true)
    {
        var q=Quest(s,node);if(q is null||q.Instance==Guid.Empty)return false;
        if(node=="CQ-MIRA-02")
        {
            var paper=Read<PhysicalMiraReviewPaper>(s,"personal-detail:mira02-paper");var copy=Read<DerivedReviewCopy>(s,"R4:mira_review_copy");var access=Read<ChapterMaterialAccess>(s,"material:R4-CQMIRA02-R-REQUEST");
            return (!requireInvitation||Read<ChapterFourState>(s,"chapter4") is{MiraReviewInvited:true})&&Read<ChapterTwoState>(s,"chapter2") is{W04Scope:true}&&Read<ChapterRecordScope>(s,"R2:w04_scope") is{Grantor:"W-04",IndividualConsent:true,IdentityPublication:false}&&BranchOperations.Entity(s,"R2:w04_handover") is not null&&s.Receipts.Any(r=>r.SemanticKey=="CQ-MIRA-01:resolution"&&r.ActionType=="chapter_two_w04_handover_received")&&paper is{Status:"delivered"}&&paper.NodeInstance==q.Instance&&paper.Paper==ScopedPersonalWorksService.ReviewPaper&&copy is{Version:"W04-review-v1",Observers:2,Service:1,Recipient:"R4-review-desk",Purpose:"verify_role_counts",IdentitiesIncluded:false}&&paper.Fields==new MiraReviewFields(2,1,copy.Version,copy.SourceRoot,copy.Recipient,copy.Purpose,false)&&access is not null&&paper.SourceMaterialVersion==access.Version&&paper.SourceMaterialHash==access.Fingerprint&&s.Items.Count(i=>i.Id==paper.Paper&&i.OwnerKind=="warehouse"&&i.OwnerId=="R4-review-desk")==1&&Evidence(s,"mira_same_review_paper_delivered",paper);
        }
        if(node=="CQ-MIRA-03")
        {
            var d=Read<ActualMiraDrill>(s,"personal-detail:mira03-drill");var e=Read<PersonalAccessExercise>(s,"R5:personal_access_exercise");var copy=Read<DerivedReviewCopy>(s,"R4:mira_review_copy");
            if(!Ready(s,"CQ-MIRA-02",false)||d is null||e is null||copy is null||d.NodeInstance!=q.Instance||d.Mode!=e.Mode||d.OriginalVersion!=copy.Version||d.OriginalSourceRoot!=copy.SourceRoot||d.MainCustody!=Read<ChapterFiveState>(s,"chapter5")?.Custody?.Mode)return false;
            var receivers=ScopedPersonalWorksService.ReviewRecipients(d.Mode);return d.Copies.Length==receivers.Length&&d.Copies.Distinct().Count()==d.Copies.Length&&d.Copies.Select((id,i)=>s.Items.Count(t=>t.Id==id&&t.OwnerKind=="warehouse"&&t.OwnerId==receivers[i])==1).All(x=>x)&&new[]{"CP-MIRA","W-04"}.All(actor=>CurrentMiraAnswer(s,actor,d.Mode,DetailVersion) is{Allowed:true})&&new[]{"check_role_fields","test_optional_exit","append_correction","test_restricted_review"}.All(step=>StepReady(s,step));
        }
        if(node=="CQ-OREN-03")
        {
            var state=Read<ChapterThreeState>(s,"chapter3");var repair=Read<AuxiliaryRepair>(s,"R3:aux_repair");var w=Read<ActualOrenWork>(s,"personal-detail:oren03-work");var d=Read<OrenDeviceCheck>(s,"personal-detail:oren03-device");var r=Read<OrenNamedReceiving>(s,"personal-detail:oren03-receiving");var p=Read<OrenMaintenancePost>(s,"R5:oren_duty");
            bool actual=(!requireInvitation||Read<ChapterFiveState>(s,"chapter5") is{OrenInvited:true})&&state is{AlarmRepair:true,AlarmReviewed:true}&&repair is{TestPassed:true}&&state.AlarmRevision==state.AlarmReviewRevision&&repair.Configuration==state.AlarmRevision&&w is{Stage:"ready_for_named_receiving",TechnicalAnswer:"isolate_before_check_same_installed_kit",OriginalResponsibility:"MC2_bypass_signature_preserved"}&&w.NodeInstance==q.Instance&&w.DeviceConfiguration==repair.Configuration&&w.Kit==repair.Kit&&s.Receipts.Any(x=>x.ReceiptId==w.IndependentReviewReceipt&&x.ActionType=="chapter_three_alarm_independently_reviewed")&&s.Items.Any(i=>i.Id==w.Kit&&i.OwnerKind=="installed"&&i.OwnerId=="R3:auxiliary_alarm")&&d is{Restored:true,PassiveTestPassed:true,InputIsolated:false}&&d.NodeInstance==q.Instance&&d.SourceConfiguration==w.DeviceConfiguration&&r is{Accepted:true,IncomingWorker:ScopedPersonalWorksService.IncomingOrenWorker,SafeWindow:"next_safe_reassignment"}&&r.NodeInstance==q.Instance&&r.Kit==w.Kit&&r.DeviceConfiguration==w.DeviceConfiguration&&p is{Status:"accepted_current"}&&p.Mode==w.Mode&&p.Receiver==r.Receiver&&p.Supervisor==r.Supervisor&&Evidence(s,"oren_named_incoming_instructions_received",r)&&s.Receipts.Any(x=>x.ReceiptId==r.WorkReceipt&&x.ActionType=="oren_current_device_operation");
            if(!actual||r!.Version!=DetailVersion||r.ResponseHashes.Count!=2||r.ResponseHashes.Values.Any(h=>h.Length!=64)||requireInvitation&&r.InvitationReceipt!=CurrentInvitationReceipt(s,node))return false;return w!.Mode!="rotation_local_maintenance"||Read<OrenActualPosition>(s,"personal-detail:oren03-position") is{} pos&&pos.NodeInstance==q.Instance&&pos.Walks.Length>0&&pos.GeometryHashes.Count>0;
        }
        return false;
    }
    public static bool HandoffReady(BranchSnapshot s)
    {
        var h=Read<OrenActualHandover>(s,"personal-detail:oren03-handover");var original=Read<RosterEntry>(s,"R5:oren_before_duty");var now=Read<RosterEntry>(s,"roster:CP-OREN");return Ready(s,"CQ-OREN-03",false)&&h is{Outgoing:"CP-OREN",Incoming:ScopedPersonalWorksService.IncomingOrenWorker,OriginalResponsibility:"MC2_bypass_signature_preserved",SafeWindow:"next_safe_reassignment",ReleasedWorkCanResumeBattle:false}&&h.NodeInstance==Quest(s,"CQ-OREN-03")?.Instance&&Evidence(s,"oren_actual_independent_replacement_received",h)&&original is not null&&now is not null&&now.Location==original.Location;
    }
}

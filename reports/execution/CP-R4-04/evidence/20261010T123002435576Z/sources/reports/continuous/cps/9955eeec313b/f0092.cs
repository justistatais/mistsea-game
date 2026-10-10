using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.ChapterFour;

namespace Mist.Application.ChapterFour;

public sealed partial class StudyInquiryService
{
    private static bool MeaningValid(StudyInquiryMeaning? m)=>m is{OriginalRoom:"Cedar",CorrectedRoom:"Birch",Time:"14:00",CorrectionScope:"room_only",HistoricalRecords:"retain_original_and_correction",SafetyScope:"public_course_arrangements_only"}&&m.Request is "schedule_clarification" or "public_outline" or "alternate_practice_request";
    private static (string English,string Chinese,string Consequence) ReplyFor(string request)=>request switch
    {
        "schedule_clarification"=>("The desk has received your enquiry. Instrument practice remains at 14:00 in Birch; Cedar is reserved for the archive workshop. The correction changes the room only. It does not cancel either course or remove their simultaneous time. No course enrolment or attendance record has been changed.","课程台已收到询问。仪表练习仍在14:00于Birch，Cedar留给档案工作坊。更正只改房间，没有取消课程或消除同时段冲突。没有修改选课或出勤记录。","published_arrangement_clarified_time_conflict_remains"),
        "public_outline"=>("The desk has received your request for a public outline. Instrument practice works with a housing and reference marks. Archive-workshop practice concerns keeping source documents in order. This reply provides the published outline as a reading aid. It is not a certificate of attendance, a substitute assessed exercise or a change to either course's time.","课程台已收到公开提纲申请。仪表练习使用外壳和参考标；档案工作坊练习保持来源文件次序。此答复提供公开阅读提纲，不是出勤证明、替代考核练习，也不改任一课程时刻。","public_outline_physically_provided_not_attendance"),
        "alternate_practice_request"=>("Your request for an alternative practice opportunity has been entered in the local course-desk ledger. Allocation remains pending; this reply does not create another session or reserve a seat. Keep the original and room-correction notices. The published instrument-practice time remains 14:00 in Birch until a separate authorised announcement changes it.","另一练习机会的申请已记入本地课程台账，分配仍待定；此答复没有创建新场次或预留席位。保留原通知与房间更正，在另有正式变更前，已公布的仪表练习仍为14:00于Birch。","alternative_request_registered_allocation_pending"),
        _=>throw new InvalidOperationException("课程台没有这个问题的公开受理范围。")
    };
    public async ValueTask<StudyInquiryPreview> PreviewAsync(string operation)
    {
        if(operation is not("seal" or "deliver"))throw new ArgumentOutOfRangeException(nameof(operation));var s=await _repository.SnapshotAsync();Safe(s);var st=State(s);Permit(s);
        var d=await _repository.GetDraftAsync(Package.DraftTask);var proposal=BranchOperations.Read<StudyInquiryProposal>(s,ProposalId);var issues=ImmutableArray.CreateBuilder<RuleIssue>();
        void Check(bool okay,string code,string text){if(!okay)issues.Add(new(code,StateId,text));}
        try{await At(s);}catch(InvalidOperationException){Check(false,"wrong_stand","请在共学室课程台进行实际定稿或交付。 ");}
        Check(st.Compared&&BranchOperations.Read<StudyNoticeComparison>(s,ComparisonId) is{} comparison&&comparison is{OriginalRoom:"Cedar",CorrectedRoom:"Birch",Time:"14:00",CorrectionScope:"room_only",CourseCancelled:false,TimeConflictResolved:false},"comparison_missing","先在通知板实际比较保留通知链；不能把更正房间认作取消课程。");
        Check(proposal is not null&&proposal.DraftHash==d?.Hash,"draft_changed","当前保存稿与明确问题确认不符。");
        Check(proposal?.StructuredAssistance==true||d is{Deleted:false}&&!string.IsNullOrWhiteSpace(d.Body),"empty_draft","空白正文仅可通过明确结构化辅助继续。");
        Check(MeaningValid(proposal?.Meaning),"meaning_invalid","保留房间和时刻，只问公共安排、提纲或待分配练习机会；不扩张私人、试验或出勤权限。");
        Check(s.Items.Any(i=>i.Id==PaperId&&i.DefinitionId=="R4-LOCAL-INQUIRY-PAPER"&&i.OwnerKind=="player"&&i.OwnerId=="hero"),"paper_missing","你未持有本份工作询问紙件。");
        var changes=ImmutableArray.CreateBuilder<EntityChange>();var items=ImmutableArray.CreateBuilder<ItemChange>();string key,action;object payload;string? description=null;
        // An invalid question never creates a canonical answer or a ready command.
        var meaning=proposal?.Meaning;
        if(operation=="seal")
        {
            Check(st.Stage==StudyInquiryStage.CarriedBlank,"paper_not_blank","先领本份纸；旧封件须实际拆封后再定稿。");
            if(issues.Count>0)return new(Guid.NewGuid(),false,issues.ToImmutable(),operation,null);
            var seal=new StudySealedInquiry(st.Instance,PaperId,checked(st.Revision+1),d?.Hash,d?.Body,proposal!.StructuredAssistance,meaning!,Versions());
            changes.Add(BranchOperations.Change(s,StateId,"study_inquiry_state",st with{Stage=StudyInquiryStage.Sealed,Revision=seal.Revision}));changes.Add(BranchOperations.Change(s,SealedId,"study_sealed_inquiry",seal));
            key="R4:study:seal:"+st.Instance+":"+seal.Revision;action="study_same_paper_physically_sealed";payload=seal;description="同一纸件第"+seal.Revision+"版；当前明确问题 "+meaning!.Request+"。尚未交付，正文未自动评价。";
        }
        else
        {
            var seal=BranchOperations.Read<StudySealedInquiry>(s,SealedId);
            Check(st.Stage==StudyInquiryStage.Sealed&&seal is not null&&seal.Instance==st.Instance&&seal.Paper==PaperId&&seal.Revision==st.Revision&&seal.DraftHash==d?.Hash&&seal.Meaning==meaning&&seal.SourceVersions.Count==Versions().Count&&seal.SourceVersions.All(x=>Versions().GetValueOrDefault(x.Key)==x.Value),"seal_changed","本份封件须与当前稿、问题及来源一致；已修改请拆封同一纸重做。");
            Check(!s.Items.Any(i=>i.Id==ReplyPaperId),"reply_already_exists","本工作唯一答复已存在，不补造。");
            if(issues.Count>0)return new(Guid.NewGuid(),false,issues.ToImmutable(),operation,null);
            var answer=ReplyFor(meaning!.Request);var entry=new StudyDeskEntry(st.Instance,PaperId,st.Revision,Package.Desk,meaning.Request,d?.Hash,Versions(),"original_and_correction_retained","published_14:00_unchanged",answer.Consequence);
            var reply=new StudyDeskReply(st.Instance,ReplyPaperId,meaning.Request,answer.English,answer.Chinese,answer.Consequence,Versions());
            changes.Add(BranchOperations.Change(s,StateId,"study_inquiry_state",st with{Stage=StudyInquiryStage.Received}));changes.Add(BranchOperations.Change(s,EntryId,"study_desk_entry",entry));changes.Add(BranchOperations.Change(s,ReplyId,"study_desk_reply",reply));
            items.Add(new(PaperId,"R4-LOCAL-INQUIRY-PAPER","player","hero","warehouse",Package.Desk,null));items.Add(new(ReplyPaperId,"R4-LOCAL-COURSE-REPLY","new","","warehouse",Package.Desk,null));
            key="R4:study:delivered";action="study_inquiry_physically_received";payload=entry;description="把本份封件交到课程台保管，并登记所问公共问题。课程台准备一份限定答复，你仍须实际领取；没有改课表、认证上课或授予奖励。";
        }
        var token=Guid.NewGuid();_previews[token]=BranchOperations.Plan(_repository,s,key,action,changes.ToImmutable(),Reads(s),payload,items.ToImmutable(),draft:new(Package.DraftTask,d?.Id,d?.Revision??0,d?.Hash));return new(token,true,[],operation,description);
    }
}

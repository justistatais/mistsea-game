using Mist.Application.ChapterFour;
using Mist.Application.Contracts;
using Mist.Core.ChapterFour;
using Mist.Core.Contracts;
using Mist.Infrastructure.Storage;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.ChapterFour;

public static class StudyInquiryCases
{
    private static string Root=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
    public static StudyInquiryPackage Package()=>StudyInquiryPackage.ParseJson(File.ReadAllText(Path.Combine(Root,"content/source/chapter_four/optional_inquiry.json")));
    private static ProfileStore Store()=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"r4-study-"+Guid.NewGuid()),Q102.Q102Cases.Catalog().ReleaseId);
    private static void Success(CommandResult r)=>Require(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(r));
    private static readonly StudyNoticeComparison Compared=new("Cedar","Birch","14:00","room_only",false,false);
    private static StudyInquiryMeaning Meaning(string request)=>new("Cedar","Birch","14:00","room_only",request,"retain_original_and_correction","public_course_arrangements_only");
    private sealed class Run
    {
        public ProfileStore Store {get;} public ChapterFourCases.Run Chapter {get;} public StudyInquiryService Service {get;}
        public Run(ProfileStore store,bool normal=false)
        {Store=store;if(normal){var p=ChapterFourCases.ActualArrival(store);Chapter=new(store,p.Service);}else{Chapter=new(store);Chapter.FixturePriorCommissionAndActualVoyage();}Chapter.Receive();Service=new(store,Chapter.Service,Package());}
        public StudyInquiryView View=>Done(Service.ViewAsync());public void At(string spot="R4-O01.inquiry")=>Chapter.At(spot);
        public void Sources()
        {At("R4-O01.notices");Success(Done(Chapter.Service.DiscoverAsync(Package().OriginalMaterial)));At();foreach(var m in Package().Materials)Success(Done(Service.DiscoverAsync(m.Id)));}
        public void Commission()
        {Sources();At();Success(Done(Service.AcceptCommissionAsync()));Success(Done(Service.TakePaperAsync()));At("R4-O01.notices");Success(Done(Service.CompareNoticesAsync(Compared)));At();}
        public void Proposal(string request="schedule_clarification",string body="A formal public enquiry. Meaning is explicitly supplied; the English has not been machine-assessed.",bool assisted=true)
        {At();var d=Done(Store.GetDraftAsync(Package().DraftTask));Done(Service.SaveDraftAsync(d?.Revision??0,d?.Id,body));Success(Done(Service.ConfirmMeaningAsync(Meaning(request),assisted)));}
        public StudyInquiryPreview Submit(string operation)
        {At();var p=Done(Service.PreviewAsync(operation));Require(p.Ready,StrictJson.Serialize(p));Success(Done(Service.ConfirmPreviewAsync(p.Token)));return p;}
        public void Reply()
        {At();Success(Done(Service.TakeReplyAsync()));Require(!string.IsNullOrWhiteSpace(Done(Service.OpenReplyAsync()).English));Success(Done(Service.AcknowledgeReplyAsync()));}
    }
    public static void Register()
    {
        Add("CP-R4-04","Study normal zero-XP source preserved notices actual one-sheet delivery reply and zero reward",()=>
        {
            using var s=Store();var r=new Run(s,true);var before=Done(s.SnapshotAsync());Require(before.Balances["experience"]==3840);
            r.Commission();r.Proposal("public_outline");long sequence=r.View.Snapshot.Sequence;var p=Done(r.Service.PreviewAsync("seal"));Require(p.Ready&&r.View.Snapshot.Sequence==sequence);Success(Done(r.Service.ConfirmPreviewAsync(p.Token)));Success(Done(r.Service.ConfirmPreviewAsync(p.Token)));
            Require(!r.View.Snapshot.Receipts.Any(x=>x.SemanticKey=="R4:optional-study:resolution"));p=r.Submit("deliver");Success(Done(r.Service.ConfirmPreviewAsync(p.Token)));Require(r.View.CollectedReply is null);Throws<InvalidOperationException>(()=>Done(r.Service.AcknowledgeReplyAsync()));
            r.Reply();Success(Done(r.Service.AcknowledgeReplyAsync()));var after=Done(s.SnapshotAsync());var receipt=after.Receipts.Single(x=>x.SemanticKey=="R4:optional-study:resolution");var result=StrictJson.Deserialize<StudyInquiryReceipt>(receipt.ResultJson);
            Require(receipt.ActionType=="study_bounded_reply_acknowledged"&&result.Consequence=="public_outline_physically_provided_not_attendance"&&!result.EnglishAssessed&&result.ExperienceAwarded==0&&result.MarksAwarded==0&&result.PartsAwarded==0);
            Require(after.Balances.Count==before.Balances.Count&&before.Balances.All(x=>after.Balances.GetValueOrDefault(x.Key)==x.Value));Require(after.Items.Single(i=>i.Id==StudyInquiryService.PaperId) is{OwnerKind:"warehouse",OwnerId:"ROLE-R4-COURSE-DESK"}&&after.Items.Single(i=>i.Id==StudyInquiryService.ReplyPaperId) is{OwnerKind:"player",OwnerId:"hero"});
            Require(after.Items.Count(i=>i.Id==StudyInquiryService.PaperId||i.Id==StudyInquiryService.ReplyPaperId)==2&&after.Receipts.Count(x=>x.SemanticKey=="R4:study:delivered")==1&&after.Receipts.All(x=>!x.SemanticKey.StartsWith("fixture:",StringComparison.Ordinal)));
            Require(Done(r.Chapter.Service.OpenMaterialAsync(Package().OriginalMaterial)).English.Contains("room, not cancellation",StringComparison.Ordinal)&&!after.Receipts.Any(x=>x.SemanticKey=="Q-401:resolution"||x.SemanticKey=="Q-404:resolution"));
            var reply=Done(r.Service.OpenReplyAsync());Require(!reply.AttendanceCertified&&!reply.TimetableChanged&&!reply.AlternateSessionApproved);
        });
        Add("CP-R4-04","Study explicit prior-source fixture rejects remote missing source cancellation and private meaning",()=>
        {
            using var s=Store();var r=new Run(s);Throws<InvalidOperationException>(()=>Done(r.Service.DiscoverAsync(Package().Materials[0].Id)));r.At();Throws<InvalidOperationException>(()=>Done(r.Service.AcceptCommissionAsync()));r.Commission();r.At("R4-O01.notices");Throws<InvalidOperationException>(()=>Done(r.Service.CompareNoticesAsync(Compared with{CourseCancelled=true})));r.At();r.Proposal();
            Success(Done(r.Service.ConfirmMeaningAsync(Meaning("private_student_identity") with{SafetyScope="private_records"},true)));var p=Done(r.Service.PreviewAsync("seal"));Require(!p.Ready&&p.Issues.Any(x=>x.Code=="meaning_invalid")&&!r.View.Snapshot.Receipts.Any(x=>x.SemanticKey=="R4:study:delivered"));r.Proposal();r.Submit("seal");r.At("R4-O01.notices");p=Done(r.Service.PreviewAsync("deliver"));Require(!p.Ready&&p.Issues.Any(x=>x.Code=="wrong_stand"));
        });
        Add("CP-R4-04","Study explicit prior-source fixture cancel edit movement and load reject stale physical preview",()=>
        {
            using var s=Store();var r=new Run(s);r.Commission();r.Proposal();var p=Done(r.Service.PreviewAsync("seal"));long seq=r.View.Snapshot.Sequence;r.Service.ClosePreview(p.Token);Require(Done(r.Service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.Rejected&&r.View.Snapshot.Sequence==seq);
            p=Done(r.Service.PreviewAsync("seal"));var d=Done(s.GetDraftAsync(Package().DraftTask))!;Throws<Mist.Application.Storage.StoreConflictException>(()=>Done(r.Service.SaveDraftAsync(0,null,"stale")));Done(r.Service.SaveDraftAsync(d.Revision,d.Id,"Changed but not re-confirmed."));Require(Done(r.Service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.Rejected);
            r.Proposal();p=Done(r.Service.PreviewAsync("seal"));r.At("R4-O01.notices");Require(Done(r.Service.ConfirmPreviewAsync(p.Token)).Status==CommitStatus.Rejected);r.At();p=Done(r.Service.PreviewAsync("seal"));Done(s.ContinueAsync());seq=r.View.Snapshot.Sequence;var reject=Done(r.Service.ConfirmPreviewAsync(p.Token));Require(reject.Status==CommitStatus.Rejected&&reject.Issues.Any(x=>x.Code=="stale_load")&&r.View.Snapshot.Sequence==seq&&r.View.State!.Stage==StudyInquiryStage.CarriedBlank);
        });
        Add("CP-R4-04","Study explicit prior-source fixture same paper recovery retains delivered revision and allocation remains pending",()=>
        {
            using var s=Store();var r=new Run(s);r.Commission();r.Proposal();r.Submit("seal");r.Proposal("alternate_practice_request","Changed unassessed request.");Require(!Done(r.Service.PreviewAsync("deliver")).Ready);Success(Done(r.Service.ReopenPaperAsync()));r.Proposal("alternate_practice_request","",true);r.Submit("seal");r.Submit("deliver");var handed=StrictJson.Deserialize<StudyDeskEntry>(r.View.Snapshot.Entities.Single(e=>e.Id=="R4:study:desk-entry").Json);
            var d=Done(s.GetDraftAsync(Package().DraftTask))!;Done(r.Service.SaveDraftAsync(d.Revision,d.Id,"Personal revision after actual delivery."));r.Reply();var reply=Done(r.Service.OpenReplyAsync());Require(reply.ActualConsequence=="alternative_request_registered_allocation_pending"&&!reply.AlternateSessionApproved&&!reply.AttendanceCertified&&!reply.TimetableChanged);
            var acknowledged=StrictJson.Deserialize<StudyInquiryReceipt>(r.View.Snapshot.Entities.Single(e=>e.Id=="R4:study:acknowledged").Json);Require(acknowledged.DeliveredDraftHash==handed.DraftHash&&r.View.Snapshot.Items.Count(i=>i.Id==StudyInquiryService.PaperId)==1);Throws<InvalidOperationException>(()=>Done(r.Service.ReopenPaperAsync()));Throws<InvalidOperationException>(()=>Done(r.Service.ConfirmMeaningAsync(Meaning("public_outline"),true)));
        });
    }
}

using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.EventScheduler;
using Mist.Application.Storage;
using Mist.Core.ChapterFour;
using Mist.Core.Devices;
using Mist.Core.Contracts;
using Mist.Core.Exploration;
using OriginalAccess = Mist.Core.ChapterTwo.ChapterMaterialAccess;

namespace Mist.Application.ChapterFour;

public sealed record StudyInquiryView(BranchSnapshot Snapshot,ActorState Actor,StudyInquiryState? State,ImmutableArray<StudyInquiryMaterial> Materials,StudyDeskReply? CollectedReply);
public sealed record StudyInquiryPreview(Guid Token,bool Ready,ImmutableArray<RuleIssue> Issues,string Operation,string? Description);

// Public local course correspondence. No main quest, reward, private permission or attendance authority.
public sealed partial class StudyInquiryService
{
    public const string StateId="R4:optional-study",PaperId="R4-STUDY-INQUIRY-ONE",ReplyPaperId="R4-STUDY-REPLY-ONE";
    private const string PermitId="R4:study:permit",ComparisonId="R4:study:comparison",ProposalId="R4:study:proposal",SealedId="R4:study:sealed",EntryId="R4:study:desk-entry",ReplyId="R4:study:reply";
    private readonly IAdventureRepository _repository;private readonly ChapterFourService _chapter;
    private readonly Dictionary<Guid,CommitPlanCommand> _previews=new();
    public StudyInquiryPackage Package {get;}
    public StudyInquiryService(IAdventureRepository repository,ChapterFourService chapter,StudyInquiryPackage package)
    {StudyInquiryPackage.Validate(package);_repository=repository;_chapter=chapter;Package=package;}
    private static void Require(bool okay,string message){if(!okay)throw new InvalidOperationException(message);}
    private void Safe(BranchSnapshot s)
    {
        BranchOperations.Safe(s);
        Require(!s.Entities.Where(e=>e.Kind=="event").Select(e=>StrictJson.Deserialize<EventSession>(e.Json)).Any(e=>e.State==EventLifecycle.Running),"当前前景事件尚未交接。");
        Require(!s.Entities.Where(e=>e.Kind=="transport_segment").Select(e=>StrictJson.Deserialize<Mist.Application.Travel.TransportSegment>(e.Json)).Any(e=>e.CargoInTransit&&!e.Completed),"当前运输尚未交接。");
        Require(!s.Receipts.Any(r=>r.SemanticKey=="Q-604:resolution"||r.ActionType=="network_finale_departure"),"终章出发后不再执行新的学城课程工作。");
        var main=BranchOperations.Read<ChapterFourState>(s,ChapterFourService.StateId);
        Require(main is not null&&main.ContentVersion==_chapter.Package.Version&&s.Receipts.Any(r=>r.SemanticKey=="R4:initialize:"+_chapter.Package.Version&&r.ActionType=="glasshaven_region_initialized"),"需要本存档真实学城到达与初始化来源。");
    }
    private async ValueTask At(BranchSnapshot s,string spot="R4-O01.inquiry")
    {
        var a=BranchOperations.Read<ActorState>(s,"actor")??throw new InvalidOperationException("实际角色位置缺失。");var geo=await _chapter.CurrentGeometryAsync();
        Require(a.Scene=="R4-O01"&&geo.TryGetValue(a.Scene,out var scene)&&scene.Spots.Any(p=>p.Id==spot&&p.Stand.Distance(a.Position)<=2.5)&&new NavigationGrid(scene).CanStand(a.Position,true),"请实际走到学生共学室当前操作站位。");
    }
    private bool Acquired(BranchSnapshot s,StudyInquiryMaterial m)=>BranchOperations.Read<StudyInquiryAccess>(s,"material:"+m.Id) is{} a&&a.Id==m.Id&&a.Version==m.Version&&a.Fingerprint==m.Fingerprint&&a.SourceRoot==m.SourceRoot;
    private bool OriginalCurrent(BranchSnapshot s)
    {var m=_chapter.Package.Materials.Single(m=>m.Id==Package.OriginalMaterial);return m.AccessClass=="public"&&BranchOperations.Read<OriginalAccess>(s,"material:"+m.Id) is{} a&&a.Id==m.Id&&a.Version==m.Version&&a.Fingerprint==m.Fingerprint&&a.SourceRoot==m.SourceRoot;}
    private bool AllSources(BranchSnapshot s)=>OriginalCurrent(s)&&Package.Materials.All(m=>Acquired(s,m));
    private ImmutableDictionary<string,string> Versions()
    {var m=_chapter.Package.Materials.Single(m=>m.Id==Package.OriginalMaterial);return Package.Materials.ToImmutableDictionary(x=>x.Id,x=>x.Version+":"+x.Fingerprint).Add(m.Id,m.Version+":"+m.Fingerprint);}
    private StudyInquiryState State(BranchSnapshot s)=>BranchOperations.Read<StudyInquiryState>(s,StateId) is{} st&&st.ContentVersion==Package.Version?st:throw new InvalidOperationException("尚未实际领取有限课程询问工作。");
    private StudyInquiryPermit Permit(BranchSnapshot s)
    {
        Require(AllSources(s),"当前保留通知链与公共规程来源不符。");var p=BranchOperations.Read<StudyInquiryPermit>(s,PermitId)??throw new InvalidOperationException("实际公共询问许可缺失。");
        var receipt=s.Receipts.SingleOrDefault(r=>r.SemanticKey=="R4:study:permit:"+Package.Version&&r.ActionType=="study_public_inquiry_permit_issued");
        Require(receipt is not null&&receipt.ResultJson==StrictJson.Serialize(p)&&p.Instance==State(s).Instance&&p.Version==Package.PermitVersion&&p.Grantor==Package.Desk&&p.Recipient=="patrol_technician"&&p.Purpose=="public_course_arrangement_enquiry"&&p.Fields.SequenceEqual(new[]{"notice_room_time","bounded_public_question"})&&p.SourceVersions.Count==Versions().Count&&p.SourceVersions.All(x=>Versions().GetValueOrDefault(x.Key)==x.Value),"课程台许可实例、字段范围或原来源版本不符。");return p;
    }
    private string[] Reads(BranchSnapshot s)=>new[]{"actor",ChapterFourService.StateId,StateId,PermitId,ComparisonId,ProposalId,SealedId,EntryId,ReplyId,"R4:study:acknowledged","R4:gate:public_student_room","material:"+Package.OriginalMaterial}
        .Concat(Package.Materials.Select(m=>"material:"+m.Id)).Concat(s.Entities.Where(e=>e.Kind is "event" or "transport_segment").Select(e=>e.Id)).ToArray();
    private async ValueTask<CommandResult> Execute(CommitPlanCommand command)
    {
        var current=_repository.Session;if(command.Context.Profile!=current.Profile||command.Context.Branch!=current.Branch||command.Context.Load!=current.Load||command.Context.ReleaseId!=current.ReleaseId)return new(CommitStatus.Rejected,null,[new("stale_load",StateId,"加载世代或分支已改变，请重新核对。")]);
        if(_repository is not IEventLeaseStore leases)throw new InvalidOperationException("课程询问需要原子前景租约接口。");string owner="r4-study:"+command.Context.Command.Value;
        Require(await leases.TryLeasesAsync(command.Context.Load,owner,["input:foreground","role:R4-course-desk"]),"课程台前景岗位正在受理其他工作。");
        try{return await _repository.ExecuteAsync(command);}finally{await leases.ReleaseLeasesAsync(command.Context.Load,owner);}
    }
    private ValueTask<CommandResult> Commit(BranchSnapshot s,string key,string action,ImmutableArray<EntityChange> changes,object payload,ImmutableArray<ItemChange> items=default,DraftStamp? draft=null)=>Execute(BranchOperations.Plan(_repository,s,key,action,changes,Reads(s),payload,items,draft:draft));
    public async ValueTask<StudyInquiryView> ViewAsync()
    {var v=await _chapter.ViewAsync();var st=BranchOperations.Read<StudyInquiryState>(v.Snapshot,StateId);if(st is not null&&st.ContentVersion!=Package.Version)throw new InvalidOperationException("课程询问版本不符，原件保留。");var reply=v.Snapshot.Items.Any(i=>i.Id==ReplyPaperId&&i.OwnerKind=="player"&&i.OwnerId=="hero")?BranchOperations.Read<StudyDeskReply>(v.Snapshot,ReplyId):null;return new(v.Snapshot,v.Actor,st,Package.Materials.Where(m=>Acquired(v.Snapshot,m)).ToImmutableArray(),reply);}
    public async ValueTask<CommandResult> DiscoverAsync(string id)
    {var s=await _repository.SnapshotAsync();Safe(s);var m=Package.Materials.Single(m=>m.Id==id);await At(s,m.Spot);if(Acquired(s,m))return new(CommitStatus.AlreadyCommitted,null,[]);return await Commit(s,"material:"+id+":acquired","study_public_procedure_acquired",[BranchOperations.Change(s,"material:"+id,"material_access",new StudyInquiryAccess(id,m.Version,m.Fingerprint,m.SourceRoot))],new{material=id,version=m.Version,fingerprint=m.Fingerprint,public_scope_only=true});}
    public async ValueTask<StudyInquiryMaterial> OpenMaterialAsync(string id)=>(await ViewAsync()).Materials.SingleOrDefault(m=>m.Id==id)??throw new InvalidOperationException("仅可打开实际取得的当前公共来源。");
    public async ValueTask<CommandResult> AcceptCommissionAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);await At(s);Require(AllSources(s),"先在通知板保留原通知及更正，再在台前取得两份规程。");
        if(BranchOperations.Entity(s,StateId) is not null){Permit(s);return new(CommitStatus.AlreadyCommitted,null,[]);}Require(!s.Items.Any(i=>i.Id==PaperId||i.Id==ReplyPaperId),"本工作唯一纸件已存在，不能补造。");
        var st=new StudyInquiryState(Guid.NewGuid(),Package.Version,StudyInquiryStage.DeskStock);var p=new StudyInquiryPermit(st.Instance,Package.PermitVersion,Package.Desk,"patrol_technician","public_course_arrangement_enquiry",["notice_room_time","bounded_public_question"],Versions());
        return await Commit(s,"R4:study:permit:"+Package.Version,"study_public_inquiry_permit_issued",[BranchOperations.Change(s,StateId,"study_inquiry_state",st),BranchOperations.Change(s,PermitId,"study_inquiry_permit",p)],p,[new(PaperId,"R4-LOCAL-INQUIRY-PAPER","new","","warehouse","R4-O01.inquiry",null)]);
    }
    public async ValueTask<CommandResult> TakePaperAsync()
    {var s=await _repository.SnapshotAsync();Safe(s);await At(s);Permit(s);var st=State(s);if(st.Stage!=StudyInquiryStage.DeskStock)return new(CommitStatus.AlreadyCommitted,null,[]);Require(s.Items.Any(i=>i.Id==PaperId&&i.OwnerKind=="warehouse"&&i.OwnerId=="R4-O01.inquiry"),"台前本份询问纸件缺失。");return await Commit(s,"R4:study:paper-collected:"+st.Instance,"study_blank_paper_collected",[BranchOperations.Change(s,StateId,"study_inquiry_state",st with{Stage=StudyInquiryStage.CarriedBlank})],new{paper=PaperId,working_stock_not_reward=true},[new(PaperId,"R4-LOCAL-INQUIRY-PAPER","warehouse","R4-O01.inquiry","player","hero",null)]);}
    public async ValueTask<CommandResult> CompareNoticesAsync(StudyNoticeComparison comparison)
    {var s=await _repository.SnapshotAsync();Safe(s);await At(s,"R4-O01.notices");Permit(s);var st=State(s);Require(st.Stage is not(StudyInquiryStage.Received or StudyInquiryStage.Acknowledged),"已交付原件不重写。");Require(comparison is{OriginalRoom:"Cedar",CorrectedRoom:"Birch",Time:"14:00",CorrectionScope:"room_only",CourseCancelled:false,TimeConflictResolved:false},"更正只改变房间；保留时刻，不推断取消课程或时间冲突已解决。");return await Commit(s,"R4:study:comparison:"+Guid.NewGuid(),"study_retained_notices_compared",[BranchOperations.Change(s,StateId,"study_inquiry_state",st with{Compared=true}),BranchOperations.Change(s,ComparisonId,"study_notice_comparison",comparison)],comparison);}
    public ValueTask<DraftSaved> SaveDraftAsync(long expectedRevision,DraftId? id,string body)
    {var session=_repository.Session;return _repository.SaveAsync(new(session.Profile,session.Branch,session.Load,Package.DraftTask,id,expectedRevision,body,_chapter.Package.Version));}
    public async ValueTask<CommandResult> ConfirmMeaningAsync(StudyInquiryMeaning meaning,bool assisted)
    {var s=await _repository.SnapshotAsync();Safe(s);await At(s);Permit(s);Require(State(s).Stage is not(StudyInquiryStage.Received or StudyInquiryStage.Acknowledged),"新个人草稿不改写实际已交付询问。");var d=await _repository.GetDraftAsync(Package.DraftTask);Require(assisted||d is{Deleted:false}&&!string.IsNullOrWhiteSpace(d.Body),"空白正文须明确选结构化辅助。");return await Commit(s,"R4:study:meaning:"+Guid.NewGuid(),"study_bounded_question_confirmed",[BranchOperations.Change(s,ProposalId,"study_inquiry_proposal",new StudyInquiryProposal(d?.Hash,assisted,meaning))],new{meaning,structured_assistance=assisted,automatic_english_understanding=false,language_score=(string?)null},draft:new(Package.DraftTask,d?.Id,d?.Revision??0,d?.Hash));}
    public async ValueTask<CommandResult> ReopenPaperAsync()
    {var s=await _repository.SnapshotAsync();Safe(s);await At(s);Permit(s);var st=State(s);Require(st.Stage==StudyInquiryStage.Sealed&&s.Items.Any(i=>i.Id==PaperId&&i.OwnerKind=="player"&&i.OwnerId=="hero"),"只能拆封自己尚未交付的同一纸件。");return await Commit(s,"R4:study:reopen:"+st.Instance+":"+st.Revision,"study_same_paper_reopened",[BranchOperations.Change(s,StateId,"study_inquiry_state",st with{Stage=StudyInquiryStage.CarriedBlank,Revision=checked(st.Revision+1)})],new{paper=PaperId,previous_versions_retained=true});}
    private StudyDeskEntry Received(BranchSnapshot s)
    {var e=BranchOperations.Read<StudyDeskEntry>(s,EntryId)??throw new InvalidOperationException("实际收件登记缺失。");var r=s.Receipts.SingleOrDefault(r=>r.SemanticKey=="R4:study:delivered"&&r.ActionType=="study_inquiry_physically_received");Require(r is not null&&r.ResultJson==StrictJson.Serialize(e)&&e.Instance==State(s).Instance&&e.Paper==PaperId&&e.Recipient==Package.Desk&&s.Items.Any(i=>i.Id==PaperId&&i.OwnerKind=="warehouse"&&i.OwnerId==Package.Desk),"台账与实际纸件/收件来源不一致。");return e;}
    public async ValueTask<CommandResult> TakeReplyAsync()
    {var s=await _repository.SnapshotAsync();Safe(s);await At(s);Permit(s);Received(s);Require(State(s).Stage is StudyInquiryStage.Received or StudyInquiryStage.Acknowledged,"课程台尚未实际收件。");if(s.Items.Any(i=>i.Id==ReplyPaperId&&i.OwnerKind=="player"&&i.OwnerId=="hero"))return new(CommitStatus.AlreadyCommitted,null,[]);Require(s.Items.Any(i=>i.Id==ReplyPaperId&&i.OwnerKind=="warehouse"&&i.OwnerId==Package.Desk),"本份答复尚未在课程台准备。");return await Commit(s,"R4:study:reply-collected","study_actual_reply_collected",[],new{paper=ReplyPaperId,attendance_certified=false},[new(ReplyPaperId,"R4-LOCAL-COURSE-REPLY","warehouse",Package.Desk,"player","hero",null)]);}
    public async ValueTask<StudyDeskReply> OpenReplyAsync()=>(await ViewAsync()).CollectedReply??throw new InvalidOperationException("须先在实际课程台领取本份答复。");
    public async ValueTask<CommandResult> AcknowledgeReplyAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);await At(s);Permit(s);var e=Received(s);var st=State(s);if(st.Stage==StudyInquiryStage.Acknowledged)return new(CommitStatus.AlreadyCommitted,null,[]);
        var reply=await OpenReplyAsync();var canonical=ReplyFor(e.Request);Require(reply.Instance==st.Instance&&reply.Paper==ReplyPaperId&&reply.Request==e.Request&&reply.English==canonical.English&&reply.Chinese==canonical.Chinese&&reply.ActualConsequence==canonical.Consequence&&reply.SourceVersions.Count==e.SourceVersions.Count&&reply.SourceVersions.All(x=>e.SourceVersions.GetValueOrDefault(x.Key)==x.Value)&&!reply.TimetableChanged&&!reply.AttendanceCertified&&!reply.AlternateSessionApproved,"答复来源或有限后果不符。");
        var receipt=new StudyInquiryReceipt(st.Instance,PaperId,ReplyPaperId,Package.Desk,e.Request,reply.ActualConsequence,e.DraftHash,e.SourceVersions);
        return await Commit(s,"R4:optional-study:resolution","study_bounded_reply_acknowledged",[BranchOperations.Change(s,StateId,"study_inquiry_state",st with{Stage=StudyInquiryStage.Acknowledged}),BranchOperations.Change(s,"R4:study:acknowledged","study_inquiry_receipt",receipt)],receipt);
    }
    public ValueTask<CommandResult> ConfirmPreviewAsync(Guid token)=>_previews.TryGetValue(token,out var command)?Execute(command):ValueTask.FromResult(new CommandResult(CommitStatus.Rejected,null,[new("preview_closed",StateId,"预览尚未就绪或已关闭。") ]));
    public void ClosePreview(Guid token)=>_previews.Remove(token);
}

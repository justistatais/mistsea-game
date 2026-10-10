using System.Collections.Immutable;
using Mist.Application.ChapterFour;
using Mist.Application.ChapterFive;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.EventScheduler;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Core.ChapterFour;
using Mist.Core.ChapterFive;
using Mist.Core.ChapterTwo;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.Quests;
using Mist.Core.ScopedPersonalWorks;

namespace Mist.Application.ScopedPersonalWorks;
// Detail producer only. Existing chapter owners retain invitations, drafts, node closure and rewards.
public sealed partial class ScopedPersonalWorksService
{
    public const string ReviewPaper="MIRA02-REVIEW-PAPER-ONE";
    private readonly IAdventureRepository _repository;private readonly ChapterFourService _four;private readonly ChapterFiveService _five;private readonly Func<ValueTask<ImmutableDictionary<string,SceneGeometry>>> _currentGeometry;
    private readonly Dictionary<Guid,CommitPlanCommand> _previews=new();public ScopedPersonalPackage Package {get;}
    public ScopedPersonalWorksService(IAdventureRepository repository,RuntimeCatalog catalog,ChapterFourService four,ChapterFiveService five,ScopedPersonalPackage package,Func<ValueTask<ImmutableDictionary<string,SceneGeometry>>> currentGeometry)
    {_repository=repository;_four=four;_five=five;Package=package;_currentGeometry=currentGeometry;Require(catalog.Definitions["personal_nodes"].Any(n=>n.Id=="CQ-MIRA-02"),"原个人节点目录缺失。");}
    private static void Require(bool value,string text){if(!value)throw new InvalidOperationException(text);}
    private static QuestState Quest(BranchSnapshot s,string id)=>BranchOperations.Read<QuestState>(s,"quest:"+id)??throw new InvalidOperationException("原个人节点缺失。");
    private void Safe(BranchSnapshot s)
    {BranchOperations.Safe(s);Require(!s.Entities.Where(e=>e.Kind=="event").Select(e=>StrictJson.Deserialize<EventSession>(e.Json)).Any(e=>e.State==EventLifecycle.Running)&&!s.Entities.Where(e=>e.Kind=="transport_segment").Select(e=>StrictJson.Deserialize<TransportSegment>(e.Json)).Any(e=>e.CargoInTransit&&!e.Completed),"当前前景或护送尚未交接。");Require(!s.Receipts.Any(r=>r.ActionType=="network_finale_departure"),"终章起航后不开展新的个人工作。");}
    private async ValueTask At(BranchSnapshot s,string spot)
    {var a=BranchOperations.Read<ActorState>(s,"actor")??throw new InvalidOperationException("实际位置缺失。");var g=await _currentGeometry();string scene=spot.Split('.')[0];Require(a.Scene==scene&&g.TryGetValue(scene,out var geo)&&geo.Spots.Any(p=>p.Id==spot&&p.Stand.Distance(a.Position)<=2.5)&&new NavigationGrid(geo).CanStand(a.Position,true),"请沿当前安全路径实际来到 "+spot+"。");}
    private ChapterMaterial Material(BranchSnapshot s,string id)
    {var m=_four.Package.Materials.Concat(_five.Package.Materials).Single(x=>x.Id==id);var a=BranchOperations.Read<ChapterMaterialAccess>(s,"material:"+id);Require(a is not null&&a.Id==id&&a.Version==m.Version&&a.Fingerprint==m.Fingerprint&&a.SourceRoot==m.SourceRoot,"原个人材料尚未在当前范围实际取得。");return m;}
    private QuestState Current(BranchSnapshot s,string id)
    {
        var q=Quest(s,id);Require(q.Instance!=Guid.Empty&&q.Lifecycle==QuestLifecycle.Active,"原个人节点未开启或已正式收束；不得另发奖励。");
        if(id=="CQ-MIRA-02")Require(BranchOperations.Read<ChapterFourState>(s,ChapterFourService.StateId) is{MiraReviewInvited:true},"米拉当前审阅邀请已撤回。");
        else{var st=BranchOperations.Read<ChapterFiveState>(s,ChapterFiveService.StateId);Require(st is not null&&(id=="CQ-MIRA-03"?st.MiraInvited:st.OrenInvited),"本人当前私人邀请已撤回。");}return q;
    }
    private string[] Reads(BranchSnapshot s)=>s.Entities.Select(e=>e.Id).Concat(new[]{"actor","ship:glimmer","R2:w04_scope","R2:w04_handover","R4:mira_review_copy","R4:actual_review_delivery","R5:personal_access_exercise","R5:oren_duty","workpost:CP-OREN","roster:CP-OREN","party:formation","R3:aux_repair","R3:alarm_review_invitation","R3:oren_before_aux","R5:oren_before_duty","personal-detail:mira02-paper","personal-detail:mira03-drill","personal-detail:mira03-answer:CP-MIRA","personal-detail:mira03-answer:W-04","personal-detail:mira03-correction","personal-detail:mira03-exit","personal-detail:mira03-probe:future","personal-detail:mira03-probe:allowed","personal-detail:mira03-probe:denied","personal-detail:oren03-device","personal-detail:oren03-work","personal-detail:oren03-receiving","personal-detail:oren03-position","personal-detail:oren03-handover","banter-presence:CP-OREN"}).Distinct().ToArray();
    private async ValueTask<CommandResult> Execute(CommitPlanCommand c,string actor)
    {var current=_repository.Session;if(c.Context.Profile!=current.Profile||c.Context.Branch!=current.Branch||c.Context.Load!=current.Load||c.Context.ReleaseId!=current.ReleaseId)return new(CommitStatus.Rejected,null,[new("stale_load","personal-detail","加载已改变，请重新核对。")]);if(_repository is not IEventLeaseStore leases)throw new InvalidOperationException("个人实际操作需要原子占用。");string owner="personal-detail:"+c.Context.Command.Value;string[] resources=actor=="CP-OREN"?["input:foreground",actor,"ROLE-R3-LOCAL-MAINTENANCE","ROLE-R3-REMOTE-REVIEW",IncomingOrenWorker]:["input:foreground",actor];Require(await leases.TryLeasesAsync(current.Load,owner,resources.ToImmutableArray()),"本人、接替人员或前景被另一工作占用。");try{return await _repository.ExecuteAsync(c);}finally{await leases.ReleaseLeasesAsync(current.Load,owner);}}
    private ValueTask<CommandResult> Commit(BranchSnapshot s,string key,string action,ImmutableArray<EntityChange> changes,object result,string actor,ImmutableArray<ItemChange> items=default)=>Execute(BranchOperations.Plan(_repository,s,key,action,changes,Reads(s),result,items),actor);
    public ValueTask<BranchSnapshot> SnapshotAsync()=>_repository.SnapshotAsync();
    public async ValueTask<CommandResult> AskMiraPaperExpansionAsync(ImmutableArray<string> fields)
    {
        var s=await _repository.SnapshotAsync();Safe(s);await At(s,"R4-S04.w04_copy");var q=Current(s,"CQ-MIRA-02");ReviewSource(s);Require(!fields.IsDefault&&fields.Any(f=>f is "identity_key" or "private_address"),"请明确真正提出的扩大字段，不把已有有限范围说成扩大同意。");
        var response=Package.Responses.Single(r=>r.Actor=="W-04"&&r.Purpose=="deny_scope_expansion");string invitation=s.Receipts.Where(r=>r.ActionType=="glasshaven_mira_review_invitation_decided").OrderByDescending(r=>r.Sequence).First().ReceiptId;var answer=new ActualMiraRenewalAnswer(q.Instance,"W-04","W04-v1","R4-review-desk",Package.Version,fields,["R4-review-desk"],false,1,response.Fingerprint,invitation);
        return await Commit(s,"personal-detail:mira02:expansion-declined","mira_paper_scope_expansion_answered",[BranchOperations.Change(s,"personal-detail:mira02-expansion","mira_paper_expansion_answer",answer)],answer,"W-04");
    }
    public async ValueTask<ScopedPersonalResponse> OpenMiraPaperExpansionAsync()
    {var s=await _repository.SnapshotAsync();Require(BranchOperations.Read<ChapterFourState>(s,"chapter4") is{MiraReviewInvited:true},"当前访问邀请已撤回。");var a=BranchOperations.Read<ActualMiraRenewalAnswer>(s,"personal-detail:mira02-expansion")??throw new InvalidOperationException("尚未提出并取得本次实际应答。");var invitation=s.Receipts.Where(r=>r.ActionType=="glasshaven_mira_review_invitation_decided").OrderByDescending(r=>r.Sequence).FirstOrDefault();Require(a.NodeInstance==Quest(s,"CQ-MIRA-02").Instance&&a.Version==Package.Version&&a.InvitationReceipt==invitation?.ReceiptId&&s.Receipts.Any(r=>r.ActionType=="mira_paper_scope_expansion_answered"&&r.ResultJson==StrictJson.Serialize(a)),"当前应答来源或邀请世代不符。");return Package.Responses.Single(r=>r.Id=="SP-W04-DENY"&&r.Fingerprint==a.ResponseHash);}
    private DerivedReviewCopy ReviewSource(BranchSnapshot s)
    {
        var m=Material(s,"R4-CQMIRA02-R-REQUEST");var p=BranchOperations.Read<DerivedReviewCopy>(s,"R4:mira_review_copy")??throw new InvalidOperationException("原有限审阅件还未生成。");Require(p is{Version:"W04-review-v1",Observers:2,Service:1,Recipient:"R4-review-desk",Purpose:"verify_role_counts",IdentitiesIncluded:false}&&p.SourceRoot==m.SourceRoot&&s.Receipts.Any(r=>r.ActionType=="glasshaven_derived_review_copy_generated"),"原有限角色数量与同源性不符。");
        var old=BranchOperations.Read<ChapterRecordScope>(s,"R2:w04_scope");var hand=BranchOperations.Entity(s,"R2:w04_handover");Require(old is{Record:"W-04",Version:"W04-v1",Grantor:"W-04",IndividualConsent:true,IdentityPublication:false}&&hand is not null&&s.Receipts.Any(r=>r.SemanticKey=="CQ-MIRA-01:resolution"&&r.ActionType=="chapter_two_w04_handover_received")&&BranchOperations.Read<ChapterTwoState>(s,"chapter2") is{W04Invited:true,W04Scope:true},"实际W04前节点交付/本人范围缺失，不能用消费者fixture补来源。");return p;
    }
    public async ValueTask<CommandResult> MakeReviewPaperAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);await At(s,"R4-S04.w04_copy");var q=Current(s,"CQ-MIRA-02");var copy=ReviewSource(s);if(BranchOperations.Entity(s,"personal-detail:mira02-paper") is not null)return new(CommitStatus.AlreadyCommitted,null,[]);Require(!s.Items.Any(i=>i.Id==ReviewPaper),"同一有限工作纸件已经存在。");var m=Material(s,"R4-CQMIRA02-R-REQUEST");var paper=new PhysicalMiraReviewPaper(q.Instance,ReviewPaper,"working",new(copy.Observers,copy.Service,copy.Version,copy.SourceRoot,copy.Recipient,copy.Purpose,copy.IdentitiesIncluded),m.Version,m.Fingerprint);return await Commit(s,"personal-detail:mira02:paper-made","mira_bounded_same_review_paper_made",[BranchOperations.Change(s,"personal-detail:mira02-paper","mira_review_paper",paper)],paper,"CP-MIRA",[new(ReviewPaper,"MIRA-BOUNDED-REVIEW-PAPER","new","","player","hero",null)]);
    }
    public async ValueTask<ScopedPersonalPreview> PreviewReviewPaperAsync(string operation,MiraReviewFields? explicitFields=null)
    {
        var s=await _repository.SnapshotAsync();try
        {
            Safe(s);await At(s,"R4-S04.w04_copy");var q=Current(s,"CQ-MIRA-02");var copy=ReviewSource(s);var old=BranchOperations.Read<PhysicalMiraReviewPaper>(s,"personal-detail:mira02-paper")??throw new InvalidOperationException("同一工作纸件未实际制作。");Require(old.NodeInstance==q.Instance&&s.Items.Any(i=>i.Id==ReviewPaper&&i.OwnerKind=="player"&&i.OwnerId=="hero"),"玩家没有持有本次同一审阅纸件。");PhysicalMiraReviewPaper next;ImmutableArray<ItemChange> items=[];
            if(operation=="seal")
            {Require(old.Status=="working"&&explicitFields is not null&&explicitFields==new MiraReviewFields(copy.Observers,copy.Service,copy.Version,copy.SourceRoot,copy.Recipient,copy.Purpose,false),"逐项明确所需角色数量、同源版本、接收方和用途；身份键不能出现在这份审阅件。");next=old with{Status="sealed",Fields=explicitFields!};}
            else{Require(operation=="deliver"&&old.Status=="sealed","先实际封好同一有限审阅件，再明确交付。");next=old with{Status="delivered"};items=[new(ReviewPaper,"MIRA-BOUNDED-REVIEW-PAPER","player","hero","warehouse","R4-review-desk",null)];}
            var c=BranchOperations.Plan(_repository,s,"personal-detail:mira02:"+operation,operation=="seal"?"mira_same_review_paper_sealed":"mira_same_review_paper_delivered",[BranchOperations.Change(s,"personal-detail:mira02-paper","mira_review_paper",next)],Reads(s),next,items);Guid token=Guid.NewGuid();_previews[token]=c;return new(token,true,[],operation=="seal"?"封口这份明确必要字段的同一工作件，不发奖励。":"把同一封件交到R4审阅台，留实际物件归属和接收回执；原章节仍单独确认节点结算。");
        }catch(InvalidOperationException e){return new(Guid.Empty,false,[new("actual_paper_scope","CQ-MIRA-02",e.Message)],"未执行纸件操作。");}
    }
    public ValueTask<CommandResult> ConfirmPreviewAsync(Guid token)=>_previews.TryGetValue(token,out var c)?Execute(c,c.Plan.SemanticKey.Contains("oren",StringComparison.Ordinal)?"CP-OREN":"CP-MIRA"):ValueTask.FromResult(new CommandResult(CommitStatus.Rejected,null,[new("preview_closed","personal-detail","预览已关闭。") ]));
    public void ClosePreview(Guid token)=>_previews.Remove(token);
}
public sealed record ScopedPersonalPreview(Guid Token,bool Ready,ImmutableArray<RuleIssue> Issues,string Description);

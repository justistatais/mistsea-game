using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Application.ChapterTwo;
using Mist.Application.EventScheduler;
using Mist.Application.Travel;
using Mist.Core.ChapterTwo;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.Quests;
using Mist.Core.R2Life;

namespace Mist.Application.R2Life;

public sealed record R2LifeView(BranchSnapshot Snapshot,ActorState Actor,R2LifeState State,ImmutableArray<QuestState> Quests,ImmutableArray<ChapterMaterial> Materials,LetterScope? Scope,WriterReview? WriterReview);
public sealed record R2LifePreview(Guid Token,bool Ready,ImmutableArray<string> Issues,string Quest);
public sealed class R2LifeService
{
    public const string StateId="r2-life",ScopeId="r2-life:letter-scope",ReviewId="r2-life:writer-review";
    private readonly IAdventureRepository _repository;
    private readonly IEventLeaseStore _leases;
    private readonly ChapterTwoService _chapter;
    private readonly Dictionary<Guid,(CommitPlanCommand Plan,string Quest)> _previews=new();
    public R2LifeContract Contract {get;}
    public ImmutableArray<ChapterMaterial> Materials {get;}
    public ImmutableArray<ChapterWritingTask> Writing {get;}
    public ImmutableDictionary<string,SceneGeometry> Geometry=>_chapter.Geometry;
    public R2LifeService(IAdventureRepository repository,IEventLeaseStore leases,ChapterTwoService chapter,R2LifeContract contract)
    {
        R2LifeContract.Validate(contract);_repository=repository;_leases=leases;_chapter=chapter;Contract=contract;
        Materials=contract.SourceMaterials.Select(id=>chapter.Package.Materials.Single(m=>m.Id==id)).ToImmutableArray();
        Writing=new[]{R2LifeRules.Reply,R2LifeRules.Seeds}.Select(q=>chapter.Package.WritingTasks.Single(w=>w.Quest==q)).ToImmutableArray();
        if(Materials.Any(m=>m.Version!=chapter.Package.Version)||Materials.Single(m=>m.Id==R2LifeRules.LetterMaterial).AccessClass!="individual_consent"||Materials.Single(m=>m.Id==R2LifeRules.SeedMaterial).AccessClass!="public")throw new InvalidDataException("Optional original source/version boundaries changed.");
    }
    private static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static ActorState Actor(BranchSnapshot s)=>BranchOperations.Read<ActorState>(s,"actor")??throw new InvalidOperationException("当前现场未加载。");
    private R2LifeState State(BranchSnapshot s)=>BranchOperations.Read<R2LifeState>(s,StateId)is{}st&&st.ContentVersion==Contract.ContentVersion?st:throw new InvalidOperationException("地方工作尚未初始化或版本已改变。");
    private static QuestState Quest(BranchSnapshot s,string quest)=>BranchOperations.Read<QuestState>(s,"quest:"+quest)??throw new InvalidOperationException("地方请求尚未建立。");
    private static void Safe(BranchSnapshot s){BranchOperations.Safe(s);Require(!BranchOperations.Receipt(s,"Q-604"),"终章起航后外区地方工作暂停。");Require(!s.Entities.Where(e=>e.Kind=="event").Select(e=>StrictJson.Deserialize<EventSession>(e.Json)).Any(e=>e.State==EventLifecycle.Running),"请先交接当前前景事件。");Require(!s.Entities.Where(e=>e.Kind=="transport_segment").Select(e=>StrictJson.Deserialize<TransportSegment>(e.Json)).Any(e=>e.CargoInTransit&&!e.Completed),"当前实物运输尚未交接。");}
    private void At(BranchSnapshot s,string spot){var a=Actor(s);var geometry=_chapter.CurrentGeometry(s);Require(geometry.TryGetValue(a.Scene,out var scene)&&scene.Spots.Any(x=>x.Id==spot&&x.Stand.Distance(a.Position)<=2.5)&&new NavigationGrid(scene).CanStand(a.Position,true),"请走到当前实际安全工作站位。");}
    private bool ScopeAllowed(BranchSnapshot s)=>BranchOperations.Read<LetterScope>(s,ScopeId)is{Allowed:true} scope&&scope.Actor==Contract.Roles.Writer&&scope.QuestInstance==Quest(s,R2LifeRules.Reply).Instance&&scope.Version==Materials.Single(m=>m.Id==R2LifeRules.LetterMaterial).Version&&scope.Fields.Order().SequenceEqual(new[]{"correspondence_intent","current_draft"})&&scope.Recipient=="patrol_technician"&&scope.Purpose=="help_this_reply";
    private void Active(BranchSnapshot s,string quest)
    {
        Require(quest is R2LifeRules.Reply or R2LifeRules.Seeds,"未知地方工作。");Safe(s);Require(Quest(s,quest).Lifecycle==QuestLifecycle.Active&&!(quest==R2LifeRules.Reply?State(s).LetterPaused:State(s).SeedsPaused),"请接受或恢复同一地方实例。");if(quest==R2LifeRules.Reply)Require(ScopeAllowed(s),"作者本人当前范围已撤回或不符，不继续私人工作。");
    }
    private bool CanRead(BranchSnapshot s,ChapterMaterial m)=>(Quest(s,m.Quest).Lifecycle is QuestLifecycle.Active or QuestLifecycle.Closed)&&Quest(s,m.Quest).Outcome is not(QuestOutcome.DeclinedPermanently or QuestOutcome.Withdrawn)&& (m.Id!=R2LifeRules.LetterMaterial||ScopeAllowed(s));
    private static bool Acquired(BranchSnapshot s,ChapterMaterial m)=>BranchOperations.Read<ChapterMaterialAccess>(s,"material:"+m.Id)is{}a&&a.Version==m.Version&&a.Fingerprint==m.Fingerprint&&a.SourceRoot==m.SourceRoot;
    private void Source(BranchSnapshot s,string quest){var m=Materials.Single(m=>m.Quest==quest);Require(CanRead(s,m)&&Acquired(s,m),"原文尚未现场取得或当前私人范围已撤回。");}
    private CommitPlanCommand Plan(BranchSnapshot s,string key,string action,ImmutableArray<EntityChange> changes,object result,DraftStamp? draft=null)=>BranchOperations.Plan(_repository,s,key,action,changes,s.Entities.Select(e=>e.Id).Concat(new[]{StateId,ScopeId,ReviewId,"quest:"+R2LifeRules.Reply,"quest:"+R2LifeRules.Seeds}).ToArray(),result,draft:draft);
    private async ValueTask<CommandResult> Execute(CommitPlanCommand plan,string? actor=null,string? secondActor=null)
    {
        string owner="r2-life:"+plan.Context.Command.Value;var resources=ImmutableArray.Create("input:foreground");if(actor is not null)resources=resources.Add(actor);if(secondActor is not null)resources=resources.Add(secondActor);
        Require(await _leases.TryLeasesAsync(plan.Context.Load,owner,resources),"同一地方岗位或前景正在处理另一工作；原稿和实例保留。");try{return await _repository.ExecuteAsync(plan);}finally{await _leases.ReleaseLeasesAsync(plan.Context.Load,owner);}
    }
    private ValueTask<CommandResult> Write(BranchSnapshot s,string action,R2LifeState state,object result,string? actor=null)=>Execute(Plan(s,"r2-life:"+action+":"+Guid.NewGuid(),action,[BranchOperations.Change(s,StateId,"r2_local_life",state)],result),actor);
    public async ValueTask<CommandResult> InitializeAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);if(BranchOperations.Entity(s,StateId)is not null){State(s);return new(CommitStatus.AlreadyCommitted,null,[]);}
        Require(Actor(s).Scene.StartsWith("R2-",StringComparison.Ordinal)&&BranchOperations.Read<ChapterTwoState>(s,ChapterTwoService.StateId)is{Landing:true}&&s.Receipts.Any(r=>r.SemanticKey=="R2:initialize:"+_chapter.Package.Version&&r.ActionType=="chapter_two_region_initialized")&&s.Receipts.Any(r=>r.SemanticKey=="R2:landing"&&r.ActionType=="chapter_two_station_action"),"先通过实际同船航行、R2初始化和登陆受理，不以裸actor制造地方历史。");
        var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(BranchOperations.Change(s,StateId,"r2_local_life",new R2LifeState{ContentVersion=Contract.ContentVersion}));foreach(string q in new[]{R2LifeRules.Reply,R2LifeRules.Seeds})if(BranchOperations.Entity(s,"quest:"+q)is null)changes.Add(BranchOperations.Change(s,"quest:"+q,"quest",new QuestState(q,Guid.NewGuid(),QuestLifecycle.Unaccepted,null)));
        return await Execute(Plan(s,"r2-life:initialized","r2_local_life_initialized",changes.ToImmutable(),new{scope="two_original_local_stories",rewards=0,harvest_created=false}));
    }
    public async ValueTask<R2LifeView> ViewAsync(){var s=await _repository.SnapshotAsync();return new(s,Actor(s),State(s),new[]{R2LifeRules.Reply,R2LifeRules.Seeds}.Select(q=>Quest(s,q)).ToImmutableArray(),Materials.Where(m=>CanRead(s,m)&&Acquired(s,m)).ToImmutableArray(),BranchOperations.Read<LetterScope>(s,ScopeId),BranchOperations.Read<WriterReview>(s,ReviewId));}
    public ValueTask TraverseAsync(ImmutableArray<Point2> path)=>_chapter.TraverseAsync(path);
    public string? PortIssue(BranchSnapshot s,Port port)
    {
        if(port.Id=="R2-O01.to.R2-S03")return null;
        if(port.Id=="R2-S03.to.R2-O01")return ScopeAllowed(s)?null:"请先在居住区公开门口取得作者本人这封信的有限许可。";
        return _chapter.PortIssue(s,port);
    }
    public async ValueTask<CommandResult> EnterPortAsync(string id)
    {
        if(id is not("R2-O01.to.R2-S03" or "R2-S03.to.R2-O01"))return await _chapter.EnterPortAsync(id);
        var s=await _repository.SnapshotAsync();Safe(s);var a=Actor(s);var geo=_chapter.CurrentGeometry(s);var p=geo[a.Scene].Ports.Single(x=>x.Id==id);Require(a.Position.Distance(p.Stand)<=2.5&&PortIssue(s,p)is null,"当前站位或本人有限入口不符。");var pair=geo[p.TargetScene].Ports.Single(x=>x.Id==p.TargetPort&&x.TargetScene==a.Scene&&x.TargetPort==p.Id);Require(new NavigationGrid(geo[a.Scene]).CanStand(a.Position,true)&&new NavigationGrid(geo[p.TargetScene]).CanStand(pair.Stand,true),"实际出口与入口须当前安全。");
        return await Execute(Plan(s,"r2-life:letter-port:"+Guid.NewGuid(),"r2_private_reply_transition",[BranchOperations.Change(s,"actor","actor",a with{Scene=p.TargetScene,Position=pair.Stand,ActionClock=checked(a.ActionClock+1)})],new{from=a.Scene,to=p.TargetScene,source_port=p.Id,target_port=pair.Id,private_scope_granted_to_other_sources=false}));
    }
    public async ValueTask<CommandResult> RequestLetterScopeAsync(ImmutableArray<string> fields,string recipient,string purpose,string version)
    {
        var s=await _repository.SnapshotAsync();Safe(s);State(s);At(s,"R2-S03.address");Require(Quest(s,R2LifeRules.Reply).Lifecycle!=QuestLifecycle.Closed&&fields.Order().SequenceEqual(new[]{"correspondence_intent","current_draft"})&&recipient=="patrol_technician"&&purpose=="help_this_reply"&&version==Materials.Single(m=>m.Id==R2LifeRules.LetterMaterial).Version,"作者仅同意当前草稿/通信意图和本次原信版本；不授公共住址或见面许可。");var old=BranchOperations.Read<LetterScope>(s,ScopeId);var scope=new LetterScope(Contract.Roles.Writer,Quest(s,R2LifeRules.Reply).Instance,fields,recipient,purpose,version,true,(old?.Generation??0)+1);
        return await Execute(Plan(s,"r2-life:author-consent:"+scope.Generation,"r2_private_writer_scope_granted",[BranchOperations.Change(s,ScopeId,"scoped_permission",scope)],scope),Contract.Roles.Writer);
    }
    public async ValueTask<CommandResult> WithdrawLetterScopeAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);State(s);Require(Actor(s).Scene is "R2-O01" or "R2-S03","本人撤回发生在当前回信屋或门口。");At(s,Actor(s).Scene=="R2-O01"?"R2-O01.reply":"R2-S03.address");var scope=BranchOperations.Read<LetterScope>(s,ScopeId)??throw new InvalidOperationException("没有当前本人许可。");return await Execute(Plan(s,"r2-life:withdraw:"+scope.Generation,"r2_private_writer_scope_withdrawn",[BranchOperations.Change(s,ScopeId,"scoped_permission",scope with{Allowed=false,Generation=scope.Generation+1})],new{actor=Contract.Roles.Writer,current_draft_not_destroyed=true,world_endings_affected=false}),Contract.Roles.Writer);
    }
    public async ValueTask<CommandResult> AcceptAsync(string quest)
    {
        var s=await _repository.SnapshotAsync();Safe(s);State(s);At(s,Writing.Single(w=>w.Quest==quest).Spot);if(quest==R2LifeRules.Reply)Require(ScopeAllowed(s),"本人范围未成立。");var q=Quest(s,quest);if(q.Lifecycle==QuestLifecycle.Active)return new(CommitStatus.AlreadyCommitted,null,[]);Require(q.Lifecycle==QuestLifecycle.Unaccepted,"已关闭实例不再复制受理。");return await Execute(Plan(s,quest+":accepted","r2_local_story_accepted",[BranchOperations.Change(s,"quest:"+quest,"quest",q.Accept())],new{quest,instance=q.Instance,reward=0}),quest==R2LifeRules.Reply?Contract.Roles.Writer:Contract.Roles.Grower);
    }
    public async ValueTask<CommandResult> SetPausedAsync(string quest,bool paused)
    {
        Require(quest is R2LifeRules.Reply or R2LifeRules.Seeds,"未知地方工作。");var s=await _repository.SnapshotAsync();Safe(s);var st=State(s);Require(Quest(s,quest).Lifecycle==QuestLifecycle.Active,"仅同一已受理实例可以暂缓或恢复。");if(!paused){At(s,Writing.Single(w=>w.Quest==quest).Spot);if(quest==R2LifeRules.Reply)Require(ScopeAllowed(s),"恢复私人工作需当前本人范围。");}return await Write(s,paused?"r2_local_story_paused":"r2_local_story_resumed",quest==R2LifeRules.Reply?st with{LetterPaused=paused}:st with{SeedsPaused=paused},new{quest,instance=Quest(s,quest).Instance,real_clock_penalty=false});
    }
    public async ValueTask<CommandResult> DeclineAsync(string quest)
    {
        Require(quest is R2LifeRules.Reply or R2LifeRules.Seeds,"未知地方工作。");var s=await _repository.SnapshotAsync();Safe(s);State(s);At(s,quest==R2LifeRules.Reply?"R2-S03.address":"R2-O02.allocation");var q=Quest(s,quest);Require(q.Lifecycle==QuestLifecycle.Unaccepted,"已受理工作请暂缓，不销毁原实例。");return await Execute(Plan(s,quest+":declined","r2_local_story_declined",[BranchOperations.Change(s,"quest:"+quest,"quest",q with{Lifecycle=QuestLifecycle.Closed,Outcome=QuestOutcome.DeclinedPermanently})],new{quest,mainline_and_ending_affected=false}),quest==R2LifeRules.Reply?Contract.Roles.Writer:Contract.Roles.Grower);
    }
    public async ValueTask<CommandResult> DiscoverAsync(string id)
    {
        var s=await _repository.SnapshotAsync();var m=Materials.Single(x=>x.Id==id);Active(s,m.Quest);At(s,m.Spot);if(Acquired(s,m))return new(CommitStatus.AlreadyCommitted,null,[]);return await Execute(Plan(s,"material:"+id+":acquired","r2_local_original_acquired",[BranchOperations.Change(s,"material:"+id,"material_access",new ChapterMaterialAccess(id,m.Version,m.TranslationVersion,m.Fingerprint,m.SourceRoot,m.Spot))],new{material=id,source_version=m.Version,private_scope_restricted=m.AccessClass=="individual_consent"}));
    }
    public async ValueTask<ChapterMaterial> OpenMaterialAsync(string id)=>(await ViewAsync()).Materials.SingleOrDefault(m=>m.Id==id)??throw new InvalidOperationException("这份原文尚未取得或本人许可已撤回。");
    public async ValueTask<CommandResult> InspectBedAsync(string bed)
    {
        var s=await _repository.SnapshotAsync();Active(s,R2LifeRules.Seeds);At(s,"R2-O02.allocation");Source(s,R2LifeRules.Seeds);Require(Contract.BedIds.Contains(bed),"未知实际床位。");var st=State(s);if(st.InspectedBeds.Contains(bed))return new(CommitStatus.AlreadyCommitted,null,[]);return await Write(s,"r2_trial_bed_space_inspected",st with{InspectedBeds=st.InspectedBeds.Add(bed)},new{bed,available=true,walkway_remains_open=true});
    }
    public async ValueTask<CommandResult> SetLayoutAsync(ImmutableArray<BedAssignment> layout)
    {
        var s=await _repository.SnapshotAsync();Active(s,R2LifeRules.Seeds);At(s,"R2-O02.allocation");Source(s,R2LifeRules.Seeds);var st=State(s);Require(st.Planted.IsEmpty,"已种植的真实床位不能通过改方案重置库存；保留本次记录。");Require(R2LifeRules.ValidLayout(Contract,layout),"当前两方案均在六床内，编号不得重复，四叶两根或两叶四根。");return await Write(s,"r2_trial_layout_marked",st with{Layout=layout,Approval=null},new{layout,estimate=R2LifeRules.Estimate(layout),planting_occurred=false});
    }
    public async ValueTask<CommandResult> RequestLayoutRevisionAsync(string note)
    {
        var s=await _repository.SnapshotAsync();Active(s,R2LifeRules.Seeds);At(s,"R2-O02.allocation");Require(!string.IsNullOrWhiteSpace(note)&&note.Length<=2000&&State(s).Planted.IsEmpty,"修订请求须具体且尚未种植。");return await Write(s,"r2_trial_revision_requested",State(s)with{RevisionRequest=note},new{note,status="pending_grower_agreement_not_authorised"},Contract.Roles.Grower);
    }
    public async ValueTask<CommandResult> RequestGrowerApprovalAsync()
    {
        var s=await _repository.SnapshotAsync();Active(s,R2LifeRules.Seeds);At(s,"R2-O02.allocation");Source(s,R2LifeRules.Seeds);var st=State(s);Require(R2LifeRules.ValidLayout(Contract,st.Layout)&&st.InspectedBeds.Order().SequenceEqual(Contract.BedIds.Order()),"先标出完整合法方案并逐床实际核对可用空间。");string key="r2-life:grower:"+Guid.NewGuid();var approval=new GrowerApproval(Contract.Roles.Grower,Quest(s,R2LifeRules.Seeds).Instance,R2LifeRules.LayoutHash(st.Layout),key,Actor(s).ActionClock);return await Execute(Plan(s,key,"r2_trial_grower_approved_layout",[BranchOperations.Change(s,StateId,"r2_local_life",st with{Approval=approval})],approval),Contract.Roles.Grower);
    }
    public async ValueTask<CommandResult> PlantBedAsync(string bed)
    {
        var s=await _repository.SnapshotAsync();Active(s,R2LifeRules.Seeds);At(s,"R2-O02.allocation");Source(s,R2LifeRules.Seeds);var st=State(s);var approval=st.Approval??throw new InvalidOperationException("当前布局尚未获种植者实际同意。");Require(approval.Actor==Contract.Roles.Grower&&approval.QuestInstance==Quest(s,R2LifeRules.Seeds).Instance&&approval.LayoutHash==R2LifeRules.LayoutHash(st.Layout)&&s.Receipts.Any(r=>r.SemanticKey==approval.ReceiptKey&&r.ActionType=="r2_trial_grower_approved_layout"&&StrictJson.Deserialize<GrowerApproval>(r.ResultJson)==approval),"当前同一布局批准回执无效。");var assigned=st.Layout.SingleOrDefault(x=>x.Bed==bed)??throw new InvalidOperationException("此床位没有当前分配。");if(st.Planted.Any(p=>p.Bed==bed))return new(CommitStatus.AlreadyCommitted,null,[]);var a=Actor(s);var record=new PlantingRecord(bed,assigned.Crop,approval.LayoutHash,checked(a.ActionClock+1),approval.ReceiptKey);
        return await Execute(Plan(s,"r2-life:bed:"+Quest(s,R2LifeRules.Seeds).Instance+":"+bed,"r2_trial_bed_actually_planted",[BranchOperations.Change(s,StateId,"r2_local_life",st with{Planted=st.Planted.Add(record)}),BranchOperations.Change(s,"actor","actor",a with{ActionClock=record.WorkClock}),BranchOperations.Change(s,"r2-life:planted:"+bed,"local_planting",record)],new{record,harvest_stock_created=false,engineering_resources_credited=false}),Contract.Roles.Grower);
    }
    public async ValueTask<DraftSaved> SaveDraftAsync(string quest,DraftId? id,long revision,string body){var s=await _repository.SnapshotAsync();Active(s,quest);var w=Writing.Single(x=>x.Quest==quest);At(s,w.Spot);var session=_repository.Session;return await _repository.SaveAsync(new(session.Profile,session.Branch,session.Load,w.Id,id,revision,body,Contract.ContentVersion));}
    public async ValueTask<CommandResult> ConfirmMeaningAsync(string quest,bool structuredAssistance,PrivateReplyMeaning? reply=null)
    {
        var s=await _repository.SnapshotAsync();Active(s,quest);var w=Writing.Single(x=>x.Quest==quest);At(s,w.Spot);Source(s,quest);var draft=await _repository.GetDraftAsync(w.Id)??throw new InvalidOperationException("请先保存自己的短英文说明。");Require(!draft.Deleted&&!string.IsNullOrWhiteSpace(draft.Body),"空稿不确认，不输出Band评分。");var st=State(s);
        if(quest==R2LifeRules.Reply)Require(reply is not null&&R2LifeRules.ValidReply(reply),"保留通信邀请，不授来访承诺/公开住址，使用既有房间代码的保管人转送。");else Require(R2LifeRules.PlantedAll(Contract,st),"正文不能代替实际六床种植记录。");var meaning=new LocalMeaning(quest,draft.Hash,quest==R2LifeRules.Seeds?R2LifeRules.LayoutHash(st.Layout):"private_current_scope",structuredAssistance,reply);
        return await Execute(Plan(s,"r2-life:meaning:"+Guid.NewGuid(),"r2_local_meaning_confirmed",[BranchOperations.Change(s,"meaning:"+quest,"local_meaning",meaning)],new{quest,language_assessment="not_evaluated",structured_assistance=structuredAssistance},new(w.Id,draft.Id,draft.Revision,draft.Hash)));
    }
    public async ValueTask<CommandResult> RequestAuthorReviewAsync(ReplyDecision decision)
    {
        var s=await _repository.SnapshotAsync();Active(s,R2LifeRules.Reply);At(s,"R2-O01.reply");Source(s,R2LifeRules.Reply);Require(Enum.IsDefined(decision),"作者只有当前短确认/留稿两种已公开同意路径。");var w=Writing.Single(x=>x.Quest==R2LifeRules.Reply);var draft=await _repository.GetDraftAsync(w.Id)??throw new InvalidOperationException("当前草稿不存在。");var meaning=BranchOperations.Read<LocalMeaning>(s,"meaning:"+R2LifeRules.Reply);Require(meaning is not null&&meaning.DraftHash==draft.Hash&&meaning.Reply is not null&&R2LifeRules.ValidReply(meaning.Reply),"作者须看已保存并明确意思的当前稿，不代玩家评测英语。");var scope=BranchOperations.Read<LetterScope>(s,ScopeId)??throw new InvalidOperationException("本人范围不存在。");string key="r2-life:writer-review:"+Guid.NewGuid();var review=new WriterReview(Contract.Roles.Writer,Quest(s,R2LifeRules.Reply).Instance,draft.Hash,scope.Generation,decision,"custodian_existing_room_code",key);
        return await Execute(Plan(s,key,"r2_private_author_current_draft_review",[BranchOperations.Change(s,ReviewId,"author_review",review)],review,new(w.Id,draft.Id,draft.Revision,draft.Hash)),Contract.Roles.Writer);
    }
    public async ValueTask<R2LifePreview> PreviewSubmissionAsync(string quest)
    {
        var s=await _repository.SnapshotAsync();var errors=ImmutableArray.CreateBuilder<string>();try{Active(s,quest);At(s,Writing.Single(w=>w.Quest==quest).Spot);Source(s,quest);}catch(InvalidOperationException e){errors.Add(e.Message);}var w=Writing.Single(x=>x.Quest==quest);var draft=await _repository.GetDraftAsync(w.Id);var meaning=BranchOperations.Read<LocalMeaning>(s,"meaning:"+quest);var st=State(s);var review=BranchOperations.Read<WriterReview>(s,ReviewId);
        if(draft is null||draft.Deleted||meaning is null||meaning.DraftHash!=draft.Hash)errors.Add("当前稿和已确认意思不一致。");
        if(quest==R2LifeRules.Reply){var scope=BranchOperations.Read<LetterScope>(s,ScopeId);if(review is null||review.Actor!=Contract.Roles.Writer||review.QuestInstance!=Quest(s,quest).Instance||review.DraftHash!=draft?.Hash||review.ConsentGeneration!=scope?.Generation||!s.Receipts.Any(r=>r.SemanticKey==review.ReviewReceiptKey&&r.ActionType=="r2_private_author_current_draft_review"&&StrictJson.Deserialize<WriterReview>(r.ResultJson)==review))errors.Add("作者尚未看过并明确决定当前稿，旧许可不能寄新稿。");}
        else if(!R2LifeRules.PlantedAll(Contract,st)||meaning?.PhysicalHash!=R2LifeRules.LayoutHash(st.Layout))errors.Add("当前完整六床种植和对应正文未成立。");
        if(errors.Count>0)return new(Guid.Empty,false,errors.ToImmutable(),quest);var saved=draft??throw new InvalidOperationException("当前稿缺失。");var q=Quest(s,quest);var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(BranchOperations.Change(s,"quest:"+quest,"quest",q.Close(QuestOutcome.Completed)));
        object result;if(quest==R2LifeRules.Reply){var author=review??throw new InvalidOperationException("作者决定缺失。");bool sent=author.Decision==ReplyDecision.SendShortAcknowledgement;result=new{quest,instance=q.Instance,decision=author.Decision,delivery=author.Delivery,sent,custodian=sent?Contract.Roles.Custodian:null,current_draft_hash=saved.Hash,author_current_draft_approved=true,visit_promised=false,address_published=false,language_assessment="not_evaluated",rewards=0};changes.Add(BranchOperations.Change(s,"r2-life:reply-result","private_reply",result));if(sent)changes.Add(BranchOperations.Change(s,"r2-life:post-custody","local_correspondence_delivery",new{custodian=Contract.Roles.Custodian,quest_instance=q.Instance,source_review=author.ReviewReceiptKey,current_draft_hash=saved.Hash,route="custodian_existing_room_code",short_acknowledgement_only=true}));}
        else{result=new{quest,instance=q.Instance,actual_planting=st.Planted,estimate=R2LifeRules.Estimate(st.Layout),harvest_created=false,personal_or_tower_stock_created=false,language_assessment="not_evaluated",rewards=0};changes.Add(BranchOperations.Change(s,"r2-life:trial-result","local_trial_result",result));}
        string action=quest==R2LifeRules.Reply?(review!.Decision==ReplyDecision.SendShortAcknowledgement?"r2_private_short_acknowledgement_sent":"r2_private_revision_kept"):"r2_six_bed_trial_received";var token=Guid.NewGuid();_previews.Add(token,(Plan(s,quest+":resolution",action,changes.ToImmutable(),result,new(w.Id,saved.Id,saved.Revision,saved.Hash)),quest));return new(token,true,[],quest);
    }
    public async ValueTask<CommandResult> ConfirmPreviewAsync(Guid token){if(!_previews.Remove(token,out var p))return new(CommitStatus.Rejected,null,[new("preview_closed","r2-life","预览已关闭，请重新核实当前结果。")]);return await Execute(p.Plan,p.Quest==R2LifeRules.Reply?Contract.Roles.Writer:Contract.Roles.Grower,p.Plan.Plan.ActionType=="r2_private_short_acknowledgement_sent"?Contract.Roles.Custodian:null);}
    public void ClosePreview(Guid token)=>_previews.Remove(token);
}

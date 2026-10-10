using System.Collections.Immutable;
using Mist.Application.ChapterThree;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.EventScheduler;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Core.ChapterThree;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.Quests;
using Mist.Core.R3ShiftWork;

namespace Mist.Application.R3ShiftWork;
public sealed record ShiftWorkView(BranchSnapshot Snapshot,ActorState Actor,ShiftWorkState? State,ImmutableArray<ShiftMaterial> Materials,ImmutableArray<ShiftAvailability> Availability,ShiftPosted? Posted);
public sealed record ShiftWorkPreview(Guid Token,bool Ready,ImmutableArray<RuleIssue> Issues,string Description);
public sealed class R3ShiftWorkService
{
    public const string StateId="R3:shift-work",BoardId="R3-SHIFT-BOARD-ONE";
    private readonly IAdventureRepository _repository;private readonly ChapterThreeService _chapter;private readonly Dictionary<Guid,CommitPlanCommand> _previews=new();
    public R3ShiftPackage Package {get;}
    public R3ShiftWorkService(IAdventureRepository repository,ChapterThreeService chapter,R3ShiftPackage package){R3ShiftPackage.Validate(package);_repository=repository;_chapter=chapter;Package=package;}
    private static void Require(bool value,string text){if(!value)throw new InvalidOperationException(text);}
    private ShiftWorkState State(BranchSnapshot s)=>BranchOperations.Read<ShiftWorkState>(s,StateId) is{} st&&st.Version==Package.Version?st:throw new InvalidOperationException("当前班工作尚未领取或版本不符。");
    private static string AvailabilityId(string actor)=>"R3:shift-availability:"+actor;
    private static string PositionId(string actor)=>"R3:shift-person:"+actor;
    private static string PostId(string actor)=>"workpost:"+actor;
    private void Safe(BranchSnapshot s)
    {
        BranchOperations.Safe(s);Require(!s.Entities.Where(e=>e.Kind=="event").Select(e=>StrictJson.Deserialize<EventSession>(e.Json)).Any(e=>e.State==EventLifecycle.Running),"另一前景事件尚未交接。");Require(!s.Entities.Where(e=>e.Kind=="transport_segment").Select(e=>StrictJson.Deserialize<TransportSegment>(e.Json)).Any(e=>e.CargoInTransit&&!e.Completed),"实际运输尚未交接。");Require(!s.Receipts.Any(r=>r.ActionType=="network_finale_departure"),"终章起航后请从起航前存档返回。");
        var main=BranchOperations.Read<ChapterThreeState>(s,ChapterThreeService.StateId);Require(main is{Commission:true,WorkersReceived:true,BasicDelivered:true}&&main.ContentVersion==_chapter.Package.Version&&BranchOperations.Read<QuestState>(s,"quest:Q-301") is{Lifecycle:QuestLifecycle.Closed,Outcome:QuestOutcome.Completed}&&s.Receipts.Any(r=>r.SemanticKey=="Q-301:resolution"&&r.ActionType=="chapter_three_report_received")&&s.Receipts.Any(r=>r.ActionType=="chapter_three_region_initialized"&&r.SemanticKey=="R3:initialize:"+_chapter.Package.Version),"需要真实R3到达、当班地方委托与Q301基本救援闭合来源，不能靠位置领取工作。");
    }
    private async ValueTask At(BranchSnapshot s,string spot)
    {var a=BranchOperations.Read<ActorState>(s,"actor")??throw new InvalidOperationException("实际角色位置缺失。");var geo=await _chapter.CurrentGeometryAsync();Require(a.Scene==Package.Scene&&geo.TryGetValue(a.Scene,out var g)&&g.Spots.Any(p=>p.Id==spot&&p.Stand.Distance(a.Position)<=2.5)&&new NavigationGrid(g).CanStand(a.Position,true),"请沿实际路径来到本班工作站位 "+spot+"。");}
    private bool Acquired(BranchSnapshot s,ShiftMaterial m)=>BranchOperations.Read<ShiftAccess>(s,"material:"+m.Id) is{} a&&a.Id==m.Id&&a.Version==m.Version&&a.Fingerprint==m.Fingerprint;
    private ImmutableDictionary<string,string> Versions()=>Package.Materials.ToImmutableDictionary(m=>m.Id,m=>m.Version+":"+m.Fingerprint);
    private bool PublicSources(BranchSnapshot s)=>Package.Materials.Where(m=>m.ResponseActor=="").All(m=>Acquired(s,m));
    private bool AllSources(BranchSnapshot s)=>Package.Materials.All(m=>Acquired(s,m));
    private string[] Reads(BranchSnapshot s)=>s.Entities.Select(e=>e.Id).Concat(new[]{StateId,"actor","R3:shift-proposal","R3:shift-posted","R3:shift-handover","R3:shift-resolution","quest:Q-301",ChapterThreeService.StateId}).Concat(Package.Staff.SelectMany(a=>new[]{AvailabilityId(a.Actor),PositionId(a.Actor),PostId(a.Actor),"duty:"+a.Actor})).Concat(Package.Materials.Select(m=>"material:"+m.Id)).Distinct().ToArray();
    private async ValueTask<CommandResult> Execute(CommitPlanCommand c)
    {
        var current=_repository.Session;if(c.Context.Profile!=current.Profile||c.Context.Branch!=current.Branch||c.Context.Load!=current.Load||c.Context.ReleaseId!=current.ReleaseId)return new(CommitStatus.Rejected,null,[new("stale_load",StateId,"加载或分支已改变，请重新核对。")]);
        if(_repository is not IEventLeaseStore leases)throw new InvalidOperationException("有限换班需要原子人物与前景占用。");string owner="r3-shift:"+c.Context.Command.Value;Require(await leases.TryLeasesAsync(current.Load,owner,Package.Staff.Select(p=>p.Actor).Append("input:foreground").ToImmutableArray()),"当班人物或前景被另一项工作占用。");try{return await _repository.ExecuteAsync(c);}finally{await leases.ReleaseLeasesAsync(current.Load,owner);}
    }
    private ValueTask<CommandResult> Commit(BranchSnapshot s,string key,string action,ImmutableArray<EntityChange> changes,object result,ImmutableArray<ItemChange> items=default,DraftStamp? draft=null)=>Execute(BranchOperations.Plan(_repository,s,key,action,changes,Reads(s),result,items,draft:draft));
    public async ValueTask<ShiftWorkView> ViewAsync()
    {var v=await _chapter.ViewAsync();return new(v.Snapshot,v.Actor,BranchOperations.Read<ShiftWorkState>(v.Snapshot,StateId),Package.Materials.Where(m=>Acquired(v.Snapshot,m)).ToImmutableArray(),Package.Staff.Select(a=>BranchOperations.Read<ShiftAvailability>(v.Snapshot,AvailabilityId(a.Actor))).OfType<ShiftAvailability>().ToImmutableArray(),BranchOperations.Read<ShiftPosted>(v.Snapshot,"R3:shift-posted"));}
    public async ValueTask<CommandResult> DiscoverAsync(string id)
    {var s=await _repository.SnapshotAsync();Safe(s);var m=Package.Materials.Single(m=>m.Id==id);await At(s,m.Spot);Require(m.ResponseActor=="","本人当前工作应答须实际询问，不把打开材料当签字。");if(Acquired(s,m))return new(CommitStatus.AlreadyCommitted,null,[]);return await Commit(s,"material:"+id+":acquired","shift_public_source_acquired",[BranchOperations.Change(s,"material:"+id,"material_access",new ShiftAccess(id,m.Version,m.Fingerprint))],new{material=id,version=m.Version,source_hash=m.Fingerprint,private_household_fields=false});}
    public async ValueTask<ShiftMaterial> OpenMaterialAsync(string id)=>(await ViewAsync()).Materials.SingleOrDefault(m=>m.Id==id)??throw new InvalidOperationException("该公共原件或本人应答尚未实际取得。");
    public async ValueTask<CommandResult> AcceptWorkAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);await At(s,Package.BoardSpot);Require(PublicSources(s),"先取得本班公共板、原承诺与有限重新分工规程。");if(BranchOperations.Entity(s,StateId) is not null)return new(CommitStatus.AlreadyCommitted,null,[]);Require(!s.Items.Any(i=>i.Id==BoardId),"本次唯一账板已存在，不能补造。");var st=new ShiftWorkState(Guid.NewGuid(),Package.Version);var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(BranchOperations.Change(s,StateId,"shift_work_state",st));foreach(var p in Package.Staff)changes.Add(BranchOperations.Change(s,PositionId(p.Actor),"shift_person",new ShiftPersonPosition(p.Actor,Package.Scene,p.Spot,"R3-SHIFT-R-BOARD:"+Package.Materials.Single(m=>m.Id=="R3-SHIFT-R-BOARD").Fingerprint)));
        return await Commit(s,"R3:shift-work:accepted:"+Package.Version,"shift_finite_public_work_accepted",changes.ToImmutable(),new{instance=st.Instance,window=Package.Window,source_versions=Versions(),family_details="outside_scope",world_reward=0},[new(BoardId,"R3-LOCAL-SHIFT-LEDGER-BOARD","new","","warehouse",Package.BoardSpot,null)]);
    }
    public async ValueTask<CommandResult> TakeBoardAsync()
    {var s=await _repository.SnapshotAsync();Safe(s);await At(s,Package.BoardSpot);var st=State(s);if(st.BoardTaken)return new(CommitStatus.AlreadyCommitted,null,[]);Require(s.Items.Any(i=>i.Id==BoardId&&i.OwnerKind=="warehouse"&&i.OwnerId==Package.BoardSpot),"本次同一账板不在台前。");return await Commit(s,"R3:shift-work:board-taken","shift_same_board_collected",[BranchOperations.Change(s,StateId,"shift_work_state",st with{BoardTaken=true})],new{board=BoardId,work_stock_not_reward=true},[new(BoardId,"R3-LOCAL-SHIFT-LEDGER-BOARD","warehouse",Package.BoardSpot,"player","hero",null)]);}
    private ShiftAvailability Availability(BranchSnapshot s,ShiftStaff staff)
    {
        var st=State(s);var a=BranchOperations.Read<ShiftAvailability>(s,AvailabilityId(staff.Actor))??throw new InvalidOperationException("尚未当面询问 "+staff.Name+"。");var m=Package.Materials.Single(m=>m.Id==staff.Response);var position=BranchOperations.Read<ShiftPersonPosition>(s,PositionId(staff.Actor));
        Require(a.Instance==st.Instance&&a.Actor==staff.Actor&&a.Window==Package.Window&&a.WorkWindow==staff.Window&&a.Post=="shelter_maintenance_desk"&&a.Scene==Package.Scene&&a.Spot==staff.Spot&&a.Version==Package.Version&&a.SourceHash==m.Fingerprint&&a.Allowed&&a.Fields.SequenceEqual(new[]{"public_work_window","named_desk","unfinished_checks"})&&position is not null&&position.Scene==Package.Scene&&position.Spot==staff.Spot&&Acquired(s,m)&&s.Receipts.Any(r=>r.ActionType=="shift_named_person_availability_answered"&&r.ResultJson==StrictJson.Serialize(a)),"本人当前有限许可、所在站位或应答版本不符。");return a;
    }
    public async ValueTask<CommandResult> RequestAvailabilityAsync(string actor)
    {
        var s=await _repository.SnapshotAsync();Safe(s);var p=Package.Staff.Single(p=>p.Actor==actor);await At(s,p.Spot);var st=State(s);Require(!st.Completed,"有限本班已经结束，不重开下一班。");if(st.Posted){Availability(s,p);return new(CommitStatus.AlreadyCommitted,null,[]);}var position=BranchOperations.Read<ShiftPersonPosition>(s,PositionId(actor));Require(position is not null&&position.Actor==actor&&position.Scene==Package.Scene&&position.Spot==p.Spot,"当前本人不在实际公众台前。");Require(!s.Entities.Any(e=>e.Id==PostId(actor)&&e.Kind=="work_post"||e.Id=="duty:"+actor&&e.Kind=="operator_duty"),"本人已承担另一当前岗位，不能自动成为替班。");var old=BranchOperations.Read<ShiftAvailability>(s,AvailabilityId(actor));var m=Package.Materials.Single(m=>m.Id==p.Response);var a=new ShiftAvailability(st.Instance,actor,Package.Window,p.Window,"shelter_maintenance_desk",Package.Scene,p.Spot,Package.Version,m.Fingerprint,true,checked((old?.Generation??0)+1),["public_work_window","named_desk","unfinished_checks"]);
        return await Commit(s,"R3:shift:availability:"+actor+":"+a.Generation,"shift_named_person_availability_answered",[BranchOperations.Change(s,AvailabilityId(actor),"shift_availability",a),BranchOperations.Change(s,"material:"+m.Id,"material_access",new ShiftAccess(m.Id,m.Version,m.Fingerprint))],a);
    }
    public ValueTask<DraftSaved> SaveDraftAsync(long expected,DraftId? id,string body){var c=_repository.Session;return _repository.SaveAsync(new(c.Profile,c.Branch,c.Load,Package.DraftTask,id,expected,body,Package.Version));}
    public async ValueTask<CommandResult> ConfirmMeaningAsync(ShiftPlan plan,bool assisted)
    {var s=await _repository.SnapshotAsync();Safe(s);await At(s,Package.BoardSpot);Require(!State(s).Posted&&AllSources(s),"先取得所有公共原件及三人实际应答；已贴出的世界原件不可被新草稿覆盖。");var d=await _repository.GetDraftAsync(Package.DraftTask);Require(assisted||d is{Deleted:false}&&!string.IsNullOrWhiteSpace(d.Body),"空白正文须明确选结构化辅助。");var generations=Package.Staff.ToImmutableDictionary(p=>p.Actor,p=>Availability(s,p).Generation);var proposal=new ShiftProposal(d?.Hash,assisted,plan,generations);return await Commit(s,"R3:shift:meaning:"+Guid.NewGuid(),"shift_explicit_draft_meaning_confirmed",[BranchOperations.Change(s,"R3:shift-proposal","shift_proposal",proposal)],new{proposal,automatic_english_understanding=false,language_score=(string?)null},draft:new(Package.DraftTask,d?.Id,d?.Revision??0,d?.Hash));}
    public async ValueTask<ShiftWorkPreview> PreviewPostAsync()
    {
        var s=await _repository.SnapshotAsync();var issues=ImmutableArray.CreateBuilder<RuleIssue>();void Check(bool ok,string code,string text){if(!ok)issues.Add(new(code,StateId,text));}ShiftWorkState? st=null;try{Safe(s);await At(s,Package.BoardSpot);st=State(s);}catch(InvalidOperationException e){Check(false,"current_scope",e.Message);}if(st is null)return new(Guid.Empty,false,issues.ToImmutable(),"未登记本班。");
        var d=await _repository.GetDraftAsync(Package.DraftTask);var p=BranchOperations.Read<ShiftProposal>(s,"R3:shift-proposal");Check(!st.Posted&&!st.Completed&&st.BoardTaken&&s.Items.Any(i=>i.Id==BoardId&&i.OwnerKind=="player"&&i.OwnerId=="hero"),"same_board_missing","同一账板须由玩家实际持有，不能凭草稿补造。");Check(AllSources(s)&&p is not null&&p.DraftHash==d?.Hash,"draft_or_source_changed","当前正文与实际来源须再次明确确认。");
        if(p is not null)
        {
            var plan=p.Plan;Check(plan.Morning=="ROLE-R3-SHIFT-SELA"&&plan.Afternoon is "ROLE-R3-SHIFT-TOBIN" or "ROLE-R3-SHIFT-NIA"&&(plan.Path=="reassign"&&plan.Afternoon=="ROLE-R3-SHIFT-TOBIN"||plan.Path=="extra_help"&&plan.Afternoon=="ROLE-R3-SHIFT-NIA")&&plan.Window==Package.Window&&plan.PrivateFields=="excluded"&&plan.OldPromise=="retained"&&plan.UnfinishedChecks=="mark_on_same_board"&&plan.NextWatch=="not_agreed","plan_invalid","两时段各一人、当前本人答应、旧承诺保留、私人字段排除、同板未完项与下一班未同意须明确。");
            foreach(var person in Package.Staff)try{var a=Availability(s,person);Check(p.AvailabilityGenerations.GetValueOrDefault(person.Actor)==a.Generation,"person_scope_changed","本人应答版本改变，请重核意思。");}catch(InvalidOperationException e){Check(false,"person_scope_missing",e.Message);}
            foreach(string actor in new[]{plan.Morning,plan.Afternoon})Check(!s.Entities.Any(e=>e.Id==PostId(actor)&&e.Kind=="work_post"||e.Id=="duty:"+actor&&e.Kind=="operator_duty"),"actor_occupied","所选人员已有其他当前岗位。");
        }
        if(issues.Count>0)return new(Guid.Empty,false,issues.ToImmutable(),"未贴出，也未执行交班。");var posted=new ShiftPosted(st.Instance,BoardId,checked(st.Configuration+1),p!.Plan,d?.Hash,Versions());var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(BranchOperations.Change(s,"R3:shift-posted","shift_posted",posted));changes.Add(BranchOperations.Change(s,StateId,"shift_work_state",st with{Posted=true,Configuration=posted.Configuration}));foreach(var pair in new[]{(p.Plan.Morning,"morning"),(p.Plan.Afternoon,"afternoon")}){var a=Availability(s,Package.Staff.Single(x=>x.Actor==pair.Item1));var source=s.Receipts.Single(r=>r.ActionType=="shift_named_person_availability_answered"&&r.ResultJson==StrictJson.Serialize(a));changes.Add(BranchOperations.Change(s,PostId(pair.Item1),"work_post",new ShiftLocalPost(st.Instance,pair.Item1,"shelter_maintenance_desk",Package.Window,pair.Item2,source.ReceiptId,false)));}
        var command=BranchOperations.Plan(_repository,s,"R3:shift-work:posted","shift_finite_corrected_board_posted",changes.ToImmutable(),Reads(s),posted,[new(BoardId,"R3-LOCAL-SHIFT-LEDGER-BOARD","player","hero","installed","R3-S08:shift_desk_board",null)],draft:new(Package.DraftTask,d?.Id,d?.Revision??0,d?.Hash));Guid token=Guid.NewGuid();_previews[token]=command;return new(token,true,[],"贴出保留旧承诺的本班安排，预约两个有限地方岗。尚未实际交班；零奖励。");
    }
    private ShiftPosted Posted(BranchSnapshot s)
    {var st=State(s);var p=BranchOperations.Read<ShiftPosted>(s,"R3:shift-posted")??throw new InvalidOperationException("本班安排尚未实际贴出。");Require(st.Posted&&p.Instance==st.Instance&&p.Configuration==st.Configuration&&p.Board==BoardId&&s.Items.Any(i=>i.Id==BoardId&&i.OwnerKind=="installed"&&i.OwnerId=="R3-S08:shift_desk_board")&&s.Receipts.Any(r=>r.SemanticKey=="R3:shift-work:posted"&&r.ActionType=="shift_finite_corrected_board_posted"&&r.ResultJson==StrictJson.Serialize(p)),"实际同一账板与贴出来源不符。");return p;}
    public async ValueTask<CommandResult> ReceiveShiftAsync(string actor,string window)
    {
        var s=await _repository.SnapshotAsync();Safe(s);var staff=Package.Staff.Single(p=>p.Actor==actor);await At(s,staff.Spot);var st=State(s);var posted=Posted(s);Require(!st.Completed,"本班已经结束。");Availability(s,staff);Require(window==staff.Window&&(window=="morning"&&actor==posted.Plan.Morning||window=="afternoon"&&actor==posted.Plan.Afternoon),"本人不是已贴出的本时段接班人。");Require(window=="morning"||st.MorningReceived&&st.HandoverChecked,"下午须接收瑟拉已经实际标记并检查的同一账板。");var post=BranchOperations.Read<ShiftLocalPost>(s,PostId(actor));Require(post is not null&&post.Instance==st.Instance&&post.Actor==actor&&post.WorkWindow==window,"本人的有限岗位来源缺失。");string key="R3:shift-work:"+window+":received";if(BranchOperations.Receipt(s,key))return new(CommitStatus.AlreadyCommitted,null,[]);var receipt=new ShiftReception(st.Instance,actor,window,BoardId,st.Configuration,window=="afternoon");return await Commit(s,key,"shift_named_person_same_board_received",[BranchOperations.Change(s,"R3:shift-reception:"+window,"shift_reception",receipt),BranchOperations.Change(s,PostId(actor),"work_post",post! with{Accepted=true}),BranchOperations.Change(s,StateId,"shift_work_state",window=="morning"?st with{MorningReceived=true}:st with{AfternoonReceived=true})],receipt);
    }
    public async ValueTask<CommandResult> CheckHandoverAsync(string unfinished)
    {var s=await _repository.SnapshotAsync();Safe(s);await At(s,Package.HandoverSpot);var st=State(s);var posted=Posted(s);Require(st.MorningReceived&&!st.Completed&&unfinished=="ledger_checked_fittings_pending_marked","须由上午实际接收后，在同一账板标明已核账和待查损坏件。");if(st.HandoverChecked)return new(CommitStatus.AlreadyCommitted,null,[]);return await Commit(s,"R3:shift-work:handover-checked","shift_same_board_unfinished_checks_marked",[BranchOperations.Change(s,StateId,"shift_work_state",st with{HandoverChecked=true})],new{instance=st.Instance,board=BoardId,configuration=st.Configuration,unfinished,incoming=posted.Plan.Afternoon,private_household_fields=false});}
    public async ValueTask<CommandResult> CompleteAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);await At(s,Package.ReceiptSpot);var st=State(s);var posted=Posted(s);if(st.Completed)return new(CommitStatus.AlreadyCommitted,null,[]);Require(st.MorningReceived&&st.HandoverChecked&&st.AfternoonReceived,"草案和预约不算实际两段接收与交班。");var receipts=ImmutableArray.CreateBuilder<string>();var changes=ImmutableArray.CreateBuilder<EntityChange>();foreach(string window in new[]{"morning","afternoon"}){var r=s.Receipts.Single(x=>x.SemanticKey=="R3:shift-work:"+window+":received"&&x.ActionType=="shift_named_person_same_board_received");var entry=StrictJson.Deserialize<ShiftReception>(r.ResultJson);string actor=window=="morning"?posted.Plan.Morning:posted.Plan.Afternoon;Require(entry.Instance==st.Instance&&entry.Actor==actor&&entry.Board==BoardId&&entry.Configuration==st.Configuration,"真实接收回执与当前同一账板不符。");receipts.Add(r.ReceiptId);var post=BranchOperations.Read<ShiftLocalPost>(s,PostId(actor));Require(post is{Accepted:true}&&post.Instance==st.Instance,"本人的实际地方岗位未接收。");changes.Add(BranchOperations.Change(s,PostId(actor),"released_post",post));}
        var result=new ShiftResolution(st.Instance,BoardId,posted.Plan,receipts.ToImmutable(),posted.SourceVersions,"retained","not_agreed");changes.Add(BranchOperations.Change(s,StateId,"shift_work_state",st with{Completed=true}));changes.Add(BranchOperations.Change(s,"R3:shift-resolution","shift_resolution",result));return await Commit(s,"R3:shift-work:resolution","shift_finite_actual_handover_received",changes.ToImmutable(),result);
    }
    public ValueTask<CommandResult> ConfirmPreviewAsync(Guid token)=>_previews.TryGetValue(token,out var c)?Execute(c):ValueTask.FromResult(new CommandResult(CommitStatus.Rejected,null,[new("preview_closed",StateId,"预览已关闭，请重新核对当前班。")]));
    public void ClosePreview(Guid token)=>_previews.Remove(token);
}

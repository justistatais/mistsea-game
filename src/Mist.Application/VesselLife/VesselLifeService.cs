using System.Collections.Immutable;
using System.Text.Json;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.EventScheduler;
using Mist.Application.Roster;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Application.VesselInterior;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.VesselLife;

namespace Mist.Application.VesselLife;
public sealed record BanterAvailability(string Id,string Title,string Scene,string Spot,bool Completed,bool Paused,string? Issue);
public sealed record BanterView(BanterScript Script,BanterState State,BanterLine? CurrentLine);
// Owns bounded ship conversations and actual participant walks. It cannot recruit, sail or grant rewards.
public sealed class VesselLifeService
{
    private readonly IAdventureRepository _repository;private readonly VesselInteriorService _interior;
    public VesselLifePackage Package {get;}
    public VesselLifeService(IAdventureRepository repository,VesselInteriorService interior,VesselLifePackage package){VesselLifePackage.Validate(package);_repository=repository;_interior=interior;Package=package;}
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private BanterScript Script(string id)=>Package.Scripts.Single(x=>x.Id==id);
    private static string StateId(string id)=>"banter:"+id;
    private static string InviteId(string id)=>"banter-invitation:"+id;
    private static string PresenceId(string actor)=>"banter-presence:"+actor;
    private static ActorState Actor(BranchSnapshot s)=>BranchOperations.Read<ActorState>(s,"actor")??throw new InvalidOperationException("实际角色现场缺失。");
    private static SharedVessel Ship(BranchSnapshot s)=>BranchOperations.Read<SharedVessel>(s,"ship:glimmer")??throw new InvalidOperationException("尚未真实移交微光号。");
    private static BanterState? State(BranchSnapshot s,string id)=>BranchOperations.Read<BanterState>(s,StateId(id));
    private void Safe(BranchSnapshot s,string? ownEvent=null)
    {
        BranchOperations.Safe(s);Require(!s.Receipts.Any(r=>r.ActionType=="network_finale_departure"),"终章已出发，请从真实起航前存档继续船上生活。");
        Require(!s.Entities.Where(e=>e.Kind=="transport_segment").Select(e=>StrictJson.Deserialize<TransportSegment>(e.Json)).Any(x=>x.CargoInTransit&&!x.Completed),"当前运输尚未交接。");
        Require(!s.Entities.Where(e=>e.Kind=="event").Select(e=>StrictJson.Deserialize<EventSession>(e.Json)).Any(x=>x.State==EventLifecycle.Running&&x.Instance!=ownEvent),"另一前景事件尚未交接。");
    }
    private async ValueTask At(BranchSnapshot s,BanterScript d)
    {var a=Actor(s);Require(a.Scene==d.Scene,"请到这段生活片段的原船舱空间。");await _interior.WorkspaceAtAsync(d.Spot);var spot=_interior.Geometry[d.Scene].Spots.Single(x=>x.Id==d.Spot);Require(a.Position.Distance(spot.Stand)<=2.5&&new NavigationGrid(_interior.Geometry[d.Scene]).CanStand(a.Position,true),"请实际走到船上生活站位。");}
    private static StoredReceipt Accepted(BranchSnapshot s,string actor)
    {
        var e=BranchOperations.Read<RosterEntry>(s,"roster:"+actor);Require(e is{Membership:Membership.Joined,Availability:ActorAvailability.ShipStandby}&&e.Actor==actor,"人物尚未实际加入或当前不可用。");
        Require(!s.Entities.Any(e=>e.Id=="workpost:"+actor&&e.Kind=="work_post"||e.Id=="duty:"+actor&&e.Kind=="operator_duty"),"人物正在实际施工或值守，不能挪来闲聊。");
        var decisions=s.Receipts.Where(r=>r.ActionType is "accept_invitation" or "finish_safe_departure").Where(r=>{using var j=JsonDocument.Parse(r.ResultJson);return j.RootElement.TryGetProperty("actor",out var a)&&a.GetString()==actor;}).OrderByDescending(r=>r.Sequence).ToArray();
        Require(decisions.FirstOrDefault() is{ActionType:"accept_invitation"},"加入状态缺少当前真实接受邀请来源。");return decisions[0];
    }
    private ImmutableArray<string> Sources(BranchSnapshot s,BanterScript d)
    {
        var sources=d.Actors.Select(a=>Accepted(s,a).ReceiptId).ToImmutableArray();
        if(d.ProgressGate!="")
        {
            var r=s.Receipts.SingleOrDefault(r=>r.SemanticKey==d.ProgressGate+":resolution"&&r.ActionType=="chapter_three_auxiliary_review_received");Require(r is not null&&BranchOperations.Entity(s,"R3:oren_toolframe") is{Kind:"souvenir"},"旧工具框须来自真实旁修及独立复核，不能从招募推定。");sources=sources.Add(r!.ReceiptId);
        }
        if(d.Scope!="public")sources=sources.Add(PrivateScope(s,d));return sources;
    }
    // Deliberately reads only typed source metadata, never a reply's English/Chinese contents.
    private static string PrivateScope(BranchSnapshot s,BanterScript d)
    {
        string purpose=d.Scope=="lina_reply_folding"?"folding_only":"keep_original_words";
        var reply=BranchOperations.Entity(s,"lina-work:reply:received")??throw new InvalidOperationException("尚未实际取得本人回信。");using var replyDoc=JsonDocument.Parse(reply.Json);var p=replyDoc.RootElement;
        string Text(JsonElement j,string key)=>j.GetProperty(key).GetString()??"";
        Require(reply.Kind=="lina_received_reply"&&Text(p,"sender")=="N-LINA-BROTHER"&&!string.IsNullOrWhiteSpace(Text(p,"body_hash")),"本人回信来源不符。");
        var r=s.Receipts.SingleOrDefault(r=>r.SemanticKey==Text(p,"receipt_semantic")&&r.ActionType=="lina_personal_reply_received");Require(r is not null&&r.ResultJson==reply.Json,"实际回信实体与接收回执不一致。");
        var scope=BranchOperations.Entity(s,"lina-work:public-topic:"+purpose)??throw new InvalidOperationException("本人尚未同意这个有限生活话题。");using var scopeDoc=JsonDocument.Parse(scope.Json);var q=scopeDoc.RootElement;
        string[] recipients=d.Scope=="lina_reply_folding"?["patrol_technician"]:["patrol_technician","CP-MIRA"];
        Require(Text(q,"actor")=="CP-LINA"&&Text(q,"source_receipt")==r!.ReceiptId&&Text(q,"version")=="LINA-WORK-20261010-v1"&&q.GetProperty("allowed").GetBoolean()&&Text(q,"purpose")==purpose&&q.GetProperty("recipients").EnumerateArray().Select(x=>x.GetString()!).Order().SequenceEqual(recipients.Order()),"当前本人许可已撤回或接收方/用途越界。");
        var authority=s.Receipts.Where(r=>r.ActionType=="lina_public_topic_scope_decided"&&r.ResultJson==scope.Json).OrderByDescending(r=>r.Sequence).FirstOrDefault();Require(authority is not null,"有限公开话题没有本人实际应答来源。");return authority!.ReceiptId;
    }
    private string[] Reads(BranchSnapshot s,BanterScript d)=>s.Entities.Select(e=>e.Id).Concat(d.Actors.SelectMany(a=>new[]{"roster:"+a,"workpost:"+a,"duty:"+a,PresenceId(a)})).Concat(new[]{"actor","ship:glimmer",StateId(d.Id),InviteId(d.Id),"banter:boarding-used","lina-work:reply:received","lina-work:public-topic:folding_only","lina-work:public-topic:keep_original_words","R3:oren_toolframe"}).Distinct().ToArray();
    private async ValueTask<CommandResult> Execute(CommitPlanCommand command,ImmutableArray<string> actors,string owner,bool keep=false)
    {
        var c=_repository.Session;if(command.Context.Profile!=c.Profile||command.Context.Branch!=c.Branch||command.Context.Load!=c.Load||command.Context.ReleaseId!=c.ReleaseId)return new(CommitStatus.Rejected,null,[new("stale_load","banter","加载已改变，请重新核对。")]);
        if(_repository is not IEventLeaseStore leases)throw new InvalidOperationException("船上闲聊需要原子的全体人物与前景占用。");Require(await leases.TryLeasesAsync(c.Load,owner,actors.Add("input:foreground")),"人物或前景正在被另一事件使用。");
        bool retained=false;try{var r=await _repository.ExecuteAsync(command);retained=keep&&r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted;return r;}finally{if(!retained)await leases.ReleaseLeasesAsync(c.Load,owner);}
    }
    private ValueTask<CommandResult> Commit(BranchSnapshot s,BanterScript d,string key,string action,ImmutableArray<EntityChange> changes,object result,string? owner=null,bool keep=false)=>Execute(BranchOperations.Plan(_repository,s,key,action,changes,Reads(s,d),result),d.Actors,owner??"banter-command:"+Guid.NewGuid(),keep);
    private (BanterPresence Presence,RosterEntry Roster) Walk(BranchSnapshot s,BanterScript d,string actor)
    {
        var roster=BranchOperations.Read<RosterEntry>(s,"roster:"+actor)!;var accept=Accepted(s,actor);var old=BranchOperations.Read<BanterPresence>(s,PresenceId(actor));var ship=Ship(s);var g=_interior.Geometry;string scene;Point2 position;
        if(old is not null&&old.AcceptedInvitationReceipt==accept.ReceiptId&&old.VesselInstance==ship.Instance&&old.Scene==roster.Location){scene=old.Scene;position=old.Position;}
        else{Require(roster.Location=="HUB-S01","人物位置缺少实际步行来源，不能由场景字符串补造。");scene="HUB-S01";position=g[scene].Spots.Single(x=>x.Id=="HUB-S01.gangway").Stand;}
        var target=g[d.Scene].Spots.Single(x=>x.Id==d.Spot).Stand;var walks=ImmutableArray.CreateBuilder<BanterWalk>();
        // Three original rooms have exactly two paired connections; inspect every interior leg.
        string[] order=["HUB-S01","HUB-S02","HUB-S03"];Require(order.Contains(scene),"人物不在实际船内。");
        while(scene!=d.Scene)
        {
            int i=Array.IndexOf(order,scene),j=Array.IndexOf(order,d.Scene);string next=order[i+Math.Sign(j-i)];var port=g[scene].Ports.Single(x=>x.TargetScene==next);var pair=g[next].Ports.Single(x=>x.Id==port.TargetPort&&x.TargetPort==port.Id);var path=new NavigationGrid(g[scene]).FindPath(position,port.Stand,true);Require(path.Length>0&&new NavigationGrid(g[next]).CanStand(pair.Stand,true),"人物实际船内路段不可安全抵达。");walks.Add(new(scene,position,next,pair.Stand,port.Id,pair.Id,path));scene=next;position=pair.Stand;
        }
        var last=new NavigationGrid(g[scene]).FindPath(position,target,true);Require(last.Length>0,"人物不能穿墙来到对话站位。");walks.Add(new(scene,position,scene,target,"","",last));return(new(actor,ship.Instance,scene,target,accept.ReceiptId,walks.ToImmutable()),roster with{Location=scene});
    }
    public async ValueTask<CommandResult> InviteAsync(string id)
    {
        var d=Script(id);var s=await _repository.SnapshotAsync();Safe(s);await At(s,d);var source=Sources(s,d);var old=BranchOperations.Read<BanterInvitation>(s,InviteId(id));var st=State(s,id);Require(st?.Completed!=true,"本段已完成，可按当前许可只读回看。");var changes=ImmutableArray.CreateBuilder<EntityChange>();foreach(string actor in d.Actors){var walk=Walk(s,d,actor);changes.Add(BranchOperations.Change(s,PresenceId(actor),"banter_presence",walk.Presence));changes.Add(BranchOperations.Change(s,"roster:"+actor,"roster",walk.Roster));}
        var invite=new BanterInvitation(id,d.Version,d.Fingerprint,Ship(s).Instance,d.Actors,true,checked((old?.Generation??0)+1),source);changes.Add(BranchOperations.Change(s,InviteId(id),"banter_invitation",invite));return await Commit(s,d,"banter:invite:"+id+":"+invite.Generation,"ship_life_topic_invitation_answered",changes.ToImmutable(),invite);
    }
    private BanterInvitation Invitation(BranchSnapshot s,BanterScript d)
    {var i=BranchOperations.Read<BanterInvitation>(s,InviteId(d.Id))??throw new InvalidOperationException("先向当前人物邀请这个有限话题。");var sources=Sources(s,d);Require(i.Allowed&&i.Id==d.Id&&i.Version==d.Version&&i.Fingerprint==d.Fingerprint&&i.VesselInstance==Ship(s).Instance&&i.Actors.SequenceEqual(d.Actors)&&i.SourceReceipts.SequenceEqual(sources)&&s.Receipts.Any(r=>r.ActionType=="ship_life_topic_invitation_answered"&&r.ResultJson==StrictJson.Serialize(i)),"话题邀请或当前来源已经改变。");foreach(string actor in d.Actors){var p=BranchOperations.Read<BanterPresence>(s,PresenceId(actor));Require(p is not null&&p.Scene==d.Scene&&p.VesselInstance==i.VesselInstance&&p.Position==_interior.Geometry[d.Scene].Spots.Single(x=>x.Id==d.Spot).Stand&&p.AcceptedInvitationReceipt==Accepted(s,actor).ReceiptId&&BranchOperations.Read<RosterEntry>(s,"roster:"+actor)!.Location==p.Scene,"参加者尚未实际同地。");}return i;}
    public async ValueTask<CommandResult> StartAsync(string id)
    {
        var d=Script(id);var s=await _repository.SnapshotAsync();var old=State(s,id);Safe(s,old?.EventInstance);await At(s,d);Invitation(s,d);Require(old?.Completed!=true,"本段已完成，回看不会再次结算。");var board=s.Receipts.Where(r=>r.ActionType=="board_shared_glimmer").OrderByDescending(r=>r.Sequence).FirstOrDefault();Require(board is not null,"当前登船没有实际来源。");
        string instance=old?.EventInstance??"ship-life:"+id+":"+d.Version;var used=BranchOperations.Read<BanterBoardingUse>(s,"banter:boarding-used");Require(old is not null||used?.BoardingReceipt!=board!.ReceiptId,"本次实际登船已展示过一段新生活片段，请在下一次安全返船时再邀请。");
        var st=old??new(id,d.Version,d.Fingerprint,Ship(s).Instance,instance,0,board!.ReceiptId,false,false);Require(st.Version==d.Version&&st.Fingerprint==d.Fingerprint&&st.VesselInstance==Ship(s).Instance,"旧对话版本保留，不能直接覆盖。");st=st with{Paused=false};var ev=new EventSession(instance,new(id,d.Version,id,1,d.Actors,d.Spot,0),EventLifecycle.Running,st.NextLine.ToString(),ActiveLoad:_repository.Session.Load);
        var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(BranchOperations.Change(s,StateId(id),"banter_state",st));changes.Add(BranchOperations.Change(s,"event:"+instance,"event",ev));if(old is null)changes.Add(BranchOperations.Change(s,"banter:boarding-used","banter_boarding_use",new BanterBoardingUse(id,board!.ReceiptId)));
        return await Commit(s,d,"banter:start:"+instance+":"+_repository.Session.Load+":"+s.Sequence,"start_ship_life_event",changes.ToImmutable(),new{id,next_line=st.NextLine,actual_participants=d.Actors},instance,true);
    }
    private BanterState Active(BranchSnapshot s,BanterScript d)
    {var st=State(s,d.Id)??throw new InvalidOperationException("本段尚未开始。");var ev=BranchOperations.Read<EventSession>(s,"event:"+st.EventInstance);Require(!st.Completed&&!st.Paused&&ev is{State:EventLifecycle.Running}&&ev.ActiveLoad==_repository.Session.Load,"请先在原站位恢复当前对话。");Invitation(s,d);return st;}
    public async ValueTask<CommandResult> AdvanceAsync(string id)
    {
        var d=Script(id);var s=await _repository.SnapshotAsync();var st=Active(s,d);Safe(s,st.EventInstance);await At(s,d);int next=checked(st.NextLine+1);bool complete=next>=d.Lines.Length;var e=BranchOperations.Read<EventSession>(s,"event:"+st.EventInstance)!;var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(BranchOperations.Change(s,StateId(id),"banter_state",st with{NextLine=next,Completed=complete}));changes.Add(BranchOperations.Change(s,"event:"+st.EventInstance,"event",e with{Node=next.ToString(),State=complete?EventLifecycle.Completed:EventLifecycle.Running}));object body=new{id,next_line=next,completed=false};string key="banter:line:"+id+":"+d.Version+":"+next;
        if(complete){var result=new BanterCompletion(id,d.Version,d.Fingerprint,st.VesselInstance,st.BoardingReceipt,d.Consequence,d.Actors);changes.Add(BranchOperations.Change(s,"banter:completed:"+id,"banter_completion",result));body=result;key="banter:"+id+":"+d.Version+":completed";}
        return await Commit(s,d,key,complete?"ship_life_scene_completed":"ship_life_line_checkpoint",changes.ToImmutable(),body,st.EventInstance,!complete);
    }
    public async ValueTask<CommandResult> PauseAsync(string id)
    {var d=Script(id);var s=await _repository.SnapshotAsync();var st=State(s,id)??throw new InvalidOperationException("本段尚未开始。");if(st.Completed||st.Paused)return new(CommitStatus.AlreadyCommitted,null,[]);var e=BranchOperations.Read<EventSession>(s,"event:"+st.EventInstance)??throw new InvalidOperationException("对话占用实例缺失。");Require(e.State==EventLifecycle.Running&&e.ActiveLoad==_repository.Session.Load,"旧加载对话需要先恢复。");return await Commit(s,d,"banter:pause:"+id+":"+s.Sequence,"pause_ship_life_event",[BranchOperations.Change(s,StateId(id),"banter_state",st with{Paused=true}),BranchOperations.Change(s,"event:"+st.EventInstance,"event",e with{State=EventLifecycle.Paused})],new{id,next_line=st.NextLine,world_reward=0},st.EventInstance);}
    public async ValueTask<ImmutableArray<BanterAvailability>> ListAsync()
    {var s=await _repository.SnapshotAsync();return Package.Scripts.Select(d=>{string? issue=null;try{Sources(s,d);}catch(InvalidOperationException e){issue=e.Message;}var st=State(s,d.Id);return new BanterAvailability(d.Id,d.Title,d.Scene,d.Spot,st?.Completed==true,st?.Paused==true,issue);}).ToImmutableArray();}
    public async ValueTask<BanterView> CurrentAsync(string id)
    {var d=Script(id);var s=await _repository.SnapshotAsync();var st=Active(s,d);await At(s,d);return new(d,st,d.Lines[st.NextLine]);}
    public async ValueTask<BanterScript> ReplayAsync(string id)
    {var d=Script(id);var s=await _repository.SnapshotAsync();Require(State(s,id)?.Completed==true,"未完成的段落从保存句继续。");Sources(s,d);return d;}
}

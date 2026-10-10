using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.EventScheduler;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Core.ChapterTwo;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.Quests;
using Mist.Core.R1Life;

namespace Mist.Application.R1Life;

public sealed record R1LifeView(BranchSnapshot Snapshot,ActorState Actor,R1LifeState State,ImmutableArray<QuestState> Quests,ImmutableArray<ChapterMaterial> Materials,ImmutableArray<ChapterStation> Stations);
public sealed record R1LifePreview(Guid Token,bool Ready,ImmutableArray<string> Issues,string Quest,bool StructuredAssistance);
public sealed class R1LifeService
{
    public const string StateId="r1-life";
    private readonly IAdventureRepository _repository;
    private readonly IEventLeaseStore _leases;
    private readonly Dictionary<Guid,CommitPlanCommand> _previews=new();
    public R1LifePackage Package {get;}
    public ImmutableDictionary<string,SceneGeometry> Geometry {get;}
    public R1LifeService(IAdventureRepository repository,IEventLeaseStore leases,R1LifePackage package,ImmutableDictionary<string,SceneGeometry> harbourGeometry)
    {
        R1LifePackage.Validate(package);_repository=repository;_leases=leases;Package=package;
        if(!harbourGeometry.TryGetValue("R1-S02",out var street))throw new InvalidOperationException("Actual harbour geometry is required.");
        var approaches=street.Ports.Where(p=>p.TargetScene==package.StreetPort.TargetScene).ToArray();
        if(approaches.Length>1||approaches.Any(p=>p.Id!=package.StreetPort.Id||p.TargetPort!=package.StreetPort.TargetPort||p.Stand!=package.StreetPort.Stand)||street.Ports.Any(p=>p.Id==package.StreetPort.Id&&p.TargetScene!=package.StreetPort.TargetScene))
            throw new InvalidDataException("The existing registered street/inn approach is conflicting or duplicated; retain the other mainline ports and repair the actual registry.");
        var patch=street with{Ports=street.Ports.Where(p=>p.TargetScene!=package.StreetPort.TargetScene).Append(package.StreetPort).ToImmutableArray(),Spots=street.Spots.Where(p=>p.Id!=package.StreetSpot.Id).Append(package.StreetSpot).ToImmutableArray()};
        if(!new NavigationGrid(patch).CanStand(package.StreetSpot.Stand,true)||!new NavigationGrid(patch).CanStand(package.StreetPort.Stand,true))throw new InvalidDataException("Public inn approach is not accessible in the actual street.");
        Geometry=harbourGeometry.SetItem(street.Id,patch).SetItems(package.Scenes.Select(s=>new KeyValuePair<string,SceneGeometry>(s.Id,s)));
    }
    private static void Require(bool valid,string message){if(!valid)throw new InvalidOperationException(message);}
    private static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static ActorState Actor(BranchSnapshot s)=>BranchOperations.Read<ActorState>(s,"actor")??throw new InvalidOperationException("当前现场未加载。");
    private R1LifeState State(BranchSnapshot s)
    {
        var state=BranchOperations.Read<R1LifeState>(s,StateId)??throw new InvalidOperationException("当地生活支线尚未初始化。");
        Require(state.ContentVersion==Package.Version,"当地文本版本与当前工作状态不一致。");return state;
    }
    private static QuestState Quest(BranchSnapshot s,string id)=>BranchOperations.Read<QuestState>(s,"quest:"+id)??throw new InvalidOperationException("地方任务未建立。");
    private void At(BranchSnapshot s,string spot)
    {
        var actor=Actor(s);Require(Geometry.TryGetValue(actor.Scene,out var geometry)&&geometry.Spots.Any(p=>p.Id==spot&&p.Stand.Distance(actor.Position)<=2.5)&&new NavigationGrid(geometry).CanStand(actor.Position,true),"请走到当前实际安全操作站位。");
    }
    private static bool Paused(R1LifeState state,string quest)=>quest==R1LifeRules.Rooms?state.RoomsPaused:state.ShowPaused;
    private void Active(BranchSnapshot s,string quest)
    {
        BranchOperations.Safe(s);Require(Quest(s,quest).Lifecycle==QuestLifecycle.Active&&!Paused(State(s),quest),"请在同一地方任务实例中接受或恢复工作。");
    }
    private static string Receiver(string quest)=>quest==R1LifeRules.Rooms?"R1-O01.front_desk":"R1-O02.programme";
    private bool Acquired(BranchSnapshot s,ChapterMaterial m)=>BranchOperations.Read<ChapterMaterialAccess>(s,"material:"+m.Id) is{} a&&a.Version==m.Version&&a.Fingerprint==m.Fingerprint&&a.SourceRoot==m.SourceRoot;
    private bool CanRead(BranchSnapshot s,ChapterMaterial m)=>(Quest(s,m.Quest).Lifecycle is QuestLifecycle.Active or QuestLifecycle.Closed)&&Quest(s,m.Quest).Outcome is not (QuestOutcome.Withdrawn or QuestOutcome.DeclinedPermanently)&&
        (m.Id!="R1-SHOW-R-SCENE"||Quest(s,R1LifeRules.Show) is{Lifecycle:QuestLifecycle.Closed,Outcome:QuestOutcome.Completed});
    private void Sources(BranchSnapshot s,params string[] ids)=>Require(ids.All(id=>Package.Materials.SingleOrDefault(m=>m.Id==id) is{} m&&CanRead(s,m)&&Acquired(s,m)),"该工作的原文来源尚未实际取得或当前委托已撤回。");
    private CommitPlanCommand Plan(BranchSnapshot s,string key,string action,ImmutableArray<EntityChange> changes,object result,
        ImmutableArray<ItemChange> items=default,ImmutableArray<BalanceChange> balances=default,DraftStamp? draft=null)=>BranchOperations.Plan(_repository,s,key,action,changes,
        new[]{"actor",StateId,"quest:"+R1LifeRules.Rooms,"quest:"+R1LifeRules.Show,"meaning:"+R1LifeRules.Rooms,"meaning:"+R1LifeRules.Show,"ship:glimmer"}.Concat(Package.Materials.Select(m=>"material:"+m.Id)).ToArray(),result,items,balances,draft:draft);
    private async ValueTask<CommandResult> Write(BranchSnapshot s,string action,R1LifeState next,object result,ImmutableArray<ItemChange> items=default)=>(await _repository.ExecuteAsync(Plan(s,action+":"+(BranchOperations.Entity(s,StateId)?.Revision??-1),action,[BranchOperations.Change(s,StateId,"r1_life",next)],result,items)));
    private async ValueTask<CommandResult> ReceiverCommit(CommitPlanCommand plan,string quest)
    {
        string eventId="r1-local-receiver:"+plan.Context.Command.Value;
        var resources=ImmutableArray.Create(quest==R1LifeRules.Rooms?"N-INNKEEPER":"ROLE-R1-SHOW-SPEAKER","input:foreground");
        if(!await _leases.TryLeasesAsync(plan.Context.Load,eventId,resources))throw new InvalidOperationException("同一受理人正在处理另一项前景工作，请稍后从原柜台或受控来信恢复。");
        try{return await _repository.ExecuteAsync(plan);}finally{await _leases.ReleaseLeasesAsync(plan.Context.Load,eventId);}
    }
    public async ValueTask<CommandResult> InitializeRegionAsync()
    {
        await _repository.EnsureInventorySchemaAsync();var s=await _repository.SnapshotAsync();BranchOperations.Safe(s);
        if(BranchOperations.Entity(s,StateId) is not null)return new(CommitStatus.AlreadyCommitted,null,[]);
        var arrival=s.Receipts.SingleOrDefault(r=>r.SemanticKey=="Q-101:resolution"&&r.ActionType=="temporary_maintenance_commission_issued");
        Require(Actor(s).Scene.StartsWith("R1-",StringComparison.Ordinal)&&arrival is not null,"先通过实际港口登记到达盐雾港，生活支线不制造主线完成记录。");
        using(var proof=JsonDocument.Parse(arrival!.ResultJson))
        {
            var j=proof.RootElement;Require(j.GetProperty("scope").GetString()==Mist.Core.Campaign.CampaignRules.Scope&&j.GetProperty("authority_actor").GetString()=="N-HAVEL"&&
                j.GetProperty("permission_source").GetString()=="Q-101:resolution"&&j.GetProperty("q102_entry_level").GetInt32()==3&&!j.GetProperty("glimmer_transferred").GetBoolean(),"入城许可来源、实际范围或船产权事实不一致。");
        }
        var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(BranchOperations.Change(s,StateId,"r1_life",new R1LifeState{ContentVersion=Package.Version}));
        foreach(string quest in new[]{R1LifeRules.Rooms,R1LifeRules.Show})if(BranchOperations.Entity(s,"quest:"+quest) is null)changes.Add(BranchOperations.Change(s,"quest:"+quest,"quest",new QuestState(quest,Guid.NewGuid(),QuestLifecycle.Unaccepted,null)));
        var items=new[]{"BRACE","CELL","SCREEN","DRUM"}.Select(id=>new ItemChange("R1-THEATRE-"+id,"LOCAL-THEATRE-"+id,"new","","warehouse",R1LifeRules.Store,null)).ToImmutableArray();
        return await _repository.ExecuteAsync(Plan(s,"R1-life:initialized","r1_local_life_initialized",changes.ToImmutable(),new{scope="original_local_candidate",finite_theatre_stock=4,main_history_changed=false},items));
    }
    public async ValueTask<R1LifeView> ViewAsync()
    {
        var s=await _repository.SnapshotAsync();var a=Actor(s);var state=State(s);
        return new(s,a,state,new[]{R1LifeRules.Rooms,R1LifeRules.Show}.Select(q=>Quest(s,q)).ToImmutableArray(),Package.Materials.Where(m=>CanRead(s,m)&&Acquired(s,m)).ToImmutableArray(),Package.Stations.Where(st=>st.Spot.StartsWith(a.Scene+'.',StringComparison.Ordinal)).ToImmutableArray());
    }
    public async ValueTask TraverseAsync(ImmutableArray<Point2> path)
    {
        if(path.IsDefaultOrEmpty)return;var s=await _repository.SnapshotAsync();BranchOperations.Safe(s);var a=Actor(s);var nav=new NavigationGrid(Geometry[a.Scene]);var from=a.Position;
        foreach(var to in path){Require(nav.SegmentClear(from,to,true),"路径穿过真实障碍或未处置风险。");from=to;}
        var result=await _repository.ExecuteAsync(Plan(s,"r1-life:walk:"+Guid.NewGuid(),"r1_local_life_walk",[BranchOperations.Change(s,"actor","actor",a with{Position=from,ActionClock=checked(a.ActionClock+1)})],new{actual_path=path}));Require(result.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,"移动状态已改变，请重新计算路径。");
    }
    public string? PortIssue(BranchSnapshot s,Port port)
    {
        if(!Package.PortGates.TryGetValue(port.Id,out var gate))return "此出口由现有主线工作服务核验。";
        var state=State(s);return gate switch{"inn_discovered" when !state.InnDiscovered=>"先在街道公开门牌发现旅舍入口。","theatre_discovered" when !state.TheatreDiscovered=>"先从旅舍公开来信取得剧场位置。",_=>null};
    }
    public async ValueTask<CommandResult> EnterPortAsync(string id)
    {
        var s=await _repository.SnapshotAsync();BranchOperations.Safe(s);var a=Actor(s);var port=Geometry[a.Scene].Ports.Single(p=>p.Id==id);Require(a.Position.Distance(port.Stand)<=2.5,"请先走到出口。");Require(PortIssue(s,port) is null,PortIssue(s,port)??"");
        var target=Geometry[port.TargetScene].Ports.Single(p=>p.Id==port.TargetPort);return await _repository.ExecuteAsync(Plan(s,"r1-life:port:"+Guid.NewGuid(),"r1_local_life_transition",[BranchOperations.Change(s,"actor","actor",a with{Scene=port.TargetScene,Position=target.Stand,ActionClock=checked(a.ActionClock+1)})],new{from=a.Scene,to=port.TargetScene,source_port=port.Id,target_port=target.Id}));
    }
    public async ValueTask<CommandResult> DiscoverInnAsync(){var s=await _repository.SnapshotAsync();BranchOperations.Safe(s);At(s,"R1-S02.inn_notice");return await Write(s,"r1_inn_discovered",State(s) with{InnDiscovered=true},new{public_location="R1-O01"});}
    public async ValueTask<CommandResult> DiscoverTheatreAsync(){var s=await _repository.SnapshotAsync();BranchOperations.Safe(s);At(s,"R1-O01.theatre_offer");return await Write(s,"r1_theatre_discovered",State(s) with{TheatreDiscovered=true},new{public_location="R1-O02",mainline_lock=false});}
    public async ValueTask<CommandResult> AcceptAsync(string quest)
    {
        Require(quest is R1LifeRules.Rooms or R1LifeRules.Show,"未知地方任务。");var s=await _repository.SnapshotAsync();BranchOperations.Safe(s);At(s,Receiver(quest));var q=Quest(s,quest);if(q.Lifecycle==QuestLifecycle.Active)return new(CommitStatus.AlreadyCommitted,null,[]);
        Require(q.Lifecycle==QuestLifecycle.Unaccepted,"已关闭的同一实例不能重新领奖。");return await ReceiverCommit(Plan(s,quest+":accepted","r1_local_story_accepted",[BranchOperations.Change(s,"quest:"+quest,"quest",q.Accept())],new{quest,instance=q.Instance,world_endings_affected=false}),quest);
    }
    public async ValueTask<CommandResult> PauseAsync(string quest)
    {
        var s=await _repository.SnapshotAsync();Active(s,quest);var state=State(s);return await Write(s,"r1_local_story_paused",quest==R1LifeRules.Rooms?state with{RoomsPaused=true}:state with{ShowPaused=true},new{quest,instance=Quest(s,quest).Instance,real_clock_penalty=false});
    }
    private void ReceiverAt(BranchSnapshot s,string quest,bool correspondence)
    {
        if(!correspondence){At(s,Receiver(quest));return;}
        Require(quest==R1LifeRules.Rooms,"剧场节目须在现场实际接收。");At(s,"HUB-S02.correspondence");
        var ship=BranchOperations.Read<SharedVessel>(s,"ship:glimmer");var receipt=s.Receipts.SingleOrDefault(r=>r.SemanticKey=="Q-104:resolution"&&r.ActionType=="tidegate_vessel_handover");
        Require(ship is not null&&receipt is not null&&s.Entities.Count(e=>e.Kind=="shared_vessel")==1,"需要实际巡灯船移交与当前船内通信入口。");
        var proof=StrictJson.Deserialize<Mist.Core.ChapterOne.TidegateHandover>(receipt!.ResultJson);Require(proof.VesselInstance==ship!.Instance.ToString()&&proof.VesselDefinition=="GLIMMER","通信入口必须属于已实际移交的同一微光号。");
    }
    public async ValueTask<CommandResult> ResumeAsync(string quest,bool correspondence=false)
    {
        var s=await _repository.SnapshotAsync();BranchOperations.Safe(s);ReceiverAt(s,quest,correspondence);Require(Quest(s,quest).Lifecycle==QuestLifecycle.Active,"只有已接受的同一实例可恢复。");var state=State(s);
        return await Write(s,"r1_local_story_resumed",quest==R1LifeRules.Rooms?state with{RoomsPaused=false}:state with{ShowPaused=false},new{quest,instance=Quest(s,quest).Instance,correspondence});
    }
    public async ValueTask<CommandResult> DeclineAsync(string quest)
    {
        var s=await _repository.SnapshotAsync();BranchOperations.Safe(s);At(s,Receiver(quest));var q=Quest(s,quest);Require(q.Lifecycle==QuestLifecycle.Unaccepted,"已接受的工作可暂停；永久拒绝只用于尚未接受的请求。");
        return await ReceiverCommit(Plan(s,quest+":declined","r1_local_story_declined",[BranchOperations.Change(s,"quest:"+quest,"quest",q with{Lifecycle=QuestLifecycle.Closed,Outcome=QuestOutcome.DeclinedPermanently})],new{quest,mainline_affected=false}),quest);
    }
    public async ValueTask<CommandResult> DiscoverAsync(string id)
    {
        var s=await _repository.SnapshotAsync();var material=Package.Materials.Single(m=>m.Id==id);
        if(id=="R1-SHOW-R-SCENE"){BranchOperations.Safe(s);Require(Quest(s,R1LifeRules.Show) is{Lifecycle:QuestLifecycle.Closed,Outcome:QuestOutcome.Completed},"恢复后的短场景须有真实节目接收结果。");}else Active(s,material.Quest);At(s,material.Spot);
        if(Acquired(s,material))return new(CommitStatus.AlreadyCommitted,null,[]);
        return await _repository.ExecuteAsync(Plan(s,"material:"+id+":acquired","r1_local_story_material_acquired",[BranchOperations.Change(s,"material:"+id,"material_access",new ChapterMaterialAccess(id,material.Version,material.TranslationVersion,material.Fingerprint,material.SourceRoot,material.Spot))],new{material=id,source_channel=material.Kind=="listening_script"?"written_script_only":"written_original",audio_listened=false}));
    }
    public async ValueTask<ChapterMaterial> OpenMaterialAsync(string id)=>(await ViewAsync()).Materials.SingleOrDefault(m=>m.Id==id)??throw new InvalidOperationException("原文尚未取得或当前委托已撤回。");
    public async ValueTask<CommandResult> ConfirmArrivedGuestsAsync()
    {
        var s=await _repository.SnapshotAsync();Active(s,R1LifeRules.Rooms);At(s,"R1-O01.guest_confirmation");Sources(s,"R1-ROOM-R-SEPARATE","R1-ROOM-L-GUESTS");return await Write(s,"r1_actual_arrived_guests_confirmed",State(s) with{GuestsConfirmed=true},new{actual_groups=2,actual_people=3,bookings=new[]{"BKG-17","BKG-22"},copies_not_groups=true});
    }
    public async ValueTask<CommandResult> InspectRoomAsync(string room)
    {
        Require(room is "QT-1" or "GS-1","未知实际房间。");var s=await _repository.SnapshotAsync();Active(s,R1LifeRules.Rooms);At(s,room=="QT-1"?"R1-O01.room_quiet":"R1-O01.room_ground");Sources(s,"R1-ROOM-R-CARDS");
        var state=State(s);return await Write(s,"r1_room_attributes_inspected",room=="QT-1"?state with{QuietRoomInspected=true}:state with{GroundRoomInspected=true},Package.Rooms.Single(r=>r.Id==room));
    }
    public async ValueTask<CommandResult> ArrangeRoomsAsync(ImmutableArray<RoomAllocation> allocations,ExtraLodgingArrangement extra)
    {
        var s=await _repository.SnapshotAsync();Active(s,R1LifeRules.Rooms);At(s,"R1-O01.ledger");Sources(s,"R1-ROOM-R-OLD","R1-ROOM-R-AMEND","R1-ROOM-R-SEPARATE","R1-ROOM-R-DESK");var state=State(s);
        Require(state.GuestsConfirmed&&state.QuietRoomInspected&&state.GroundRoomInspected,"实际到达住客与两间房的现场属性须分别核对。");Require(Enum.IsDefined(extra)&&R1LifeRules.ValidAllocation(Package,allocations),"当前是两笔有效订单；每组实际床位、安静或平层要求必须满足。");
        var ledger=new{stay="Harbour Day 2",active_orders=R1LifeRules.CurrentBookings(Package),retained_history=Package.Bookings,allocations,extra,player_resources_taken=false};
        return await _repository.ExecuteAsync(Plan(s,"r1-room:ledger:"+(BranchOperations.Entity(s,StateId)?.Revision??-1),"r1_actual_room_allocation",[BranchOperations.Change(s,StateId,"r1_life",state with{Allocations=allocations,Extra=extra}),BranchOperations.Change(s,"R1:inn_ledger","local_lodging",ledger),BranchOperations.Change(s,"R1:inn_guests","local_people",new{groups=new[]{new{booking="BKG-17",people=2,room=allocations.Single(a=>a.Booking=="BKG-17").Room},new{booking="BKG-22",people=1,room=allocations.Single(a=>a.Booking=="BKG-22").Room}}})],ledger));
    }
    public async ValueTask<DraftSaved> SaveDraftAsync(string quest,DraftId? draft,long expectedRevision,string body)
    {
        var s=await _repository.SnapshotAsync();Active(s,quest);var writer=Package.Writing.Single(w=>w.Quest==quest);At(s,writer.Spot);var session=_repository.Session;
        return await _repository.SaveAsync(new(session.Profile,session.Branch,session.Load,writer.Id,draft,expectedRevision,body,Package.Version));
    }
    public async ValueTask<CommandResult> ConfirmMeaningAsync(string quest,bool structuredAssistance,bool correspondence=false)
    {
        var s=await _repository.SnapshotAsync();Active(s,quest);if(correspondence)ReceiverAt(s,quest,true);else At(s,Package.Writing.Single(w=>w.Quest==quest).Spot);
        var task=Package.Writing.Single(w=>w.Quest==quest).Id;var draft=await _repository.GetDraftAsync(task);Require(draft is{Deleted:false,Body:not null}&&!string.IsNullOrWhiteSpace(draft.Body),"先实际保存自己的英文留言，短文不产生band评分。");
        var saved=draft??throw new InvalidOperationException("当前正文不存在。");
        var state=State(s);Require(quest==R1LifeRules.Rooms?R1LifeRules.ValidAllocation(Package,state.Allocations)&&state.Extra is not null:R1LifeRules.ShowReady(state),"先完成真实分房或已准备的演出，不以正文替代物理工作。");
        return await _repository.ExecuteAsync(Plan(s,"r1-life:meaning:"+quest+":"+Guid.NewGuid(),"r1_local_story_meaning_confirmed",[BranchOperations.Change(s,"meaning:"+quest,"local_story_meaning",new LocalStoryMeaning(quest,saved.Hash,true,structuredAssistance))],new{quest,draft_hash=saved.Hash,structured_assistance=structuredAssistance,language_assessment="not_evaluated"},draft:new(task,saved.Id,saved.Revision,saved.Hash)));
    }
    public async ValueTask<R1LifePreview> PreviewSubmissionAsync(string quest,bool correspondence=false)
    {
        var s=await _repository.SnapshotAsync();var issues=ImmutableArray.CreateBuilder<string>();var state=State(s);var q=Quest(s,quest);var writer=Package.Writing.Single(w=>w.Quest==quest);var draft=await _repository.GetDraftAsync(writer.Id);var meaning=BranchOperations.Read<LocalStoryMeaning>(s,"meaning:"+quest);
        try{Active(s,quest);ReceiverAt(s,quest,correspondence);}catch(InvalidOperationException e){issues.Add(e.Message);}
        if(draft is not{Deleted:false,Body:not null}||meaning is not{Confirmed:true}||meaning.DraftHash!=draft.Hash)issues.Add("当前正文与已确认意思不一致，请重新保存并确认。");
        if(quest==R1LifeRules.Rooms?!(state.GuestsConfirmed&&state.QuietRoomInspected&&state.GroundRoomInspected&&R1LifeRules.ValidAllocation(Package,state.Allocations)&&state.Extra is not null):!R1LifeRules.ShowReady(state))issues.Add("实际住宿或演出准备条件未完成。");
        if(issues.Count>0)return new(Guid.Empty,false,issues.ToImmutable(),quest,meaning?.StructuredAssistance??false);
        var saved=draft??throw new InvalidOperationException("当前正文不存在。");var confirmed=meaning??throw new InvalidOperationException("当前确认不存在。");
        var result=new{quest,instance=q.Instance,allocations=state.Allocations,extra=state.Extra,programme=state.Roles,correspondence,local_only=true,language_assessment="not_evaluated",structured_assistance=confirmed.StructuredAssistance};
        var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(BranchOperations.Change(s,"quest:"+quest,"quest",q.Close(QuestOutcome.Completed)));changes.Add(BranchOperations.Change(s,"R1:result:"+quest,"local_story_result",result));
        if(quest==R1LifeRules.Show)changes.Add(BranchOperations.Change(s,"R1:theatre_programme","local_programme",state.Roles!));
        var command=Plan(s,quest+":resolution",quest==R1LifeRules.Rooms?"r1_inn_arrangement_received":"r1_theatre_programme_received",changes.ToImmutable(),result,balances:quest==R1LifeRules.Rooms?[new("experience",25)]:[],draft:new(writer.Id,saved.Id,saved.Revision,saved.Hash));
        var token=Guid.NewGuid();_previews.Add(token,command);return new(token,true,[],quest,confirmed.StructuredAssistance);
    }
    public async ValueTask<CommandResult> ConfirmPreviewAsync(Guid token)
    {
        if(!_previews.Remove(token,out var command))throw new InvalidOperationException("预览已关闭、过期或提交，请重新核对。");
        string quest=command.Plan.SemanticKey.StartsWith(R1LifeRules.Rooms,StringComparison.Ordinal)?R1LifeRules.Rooms:R1LifeRules.Show;return await ReceiverCommit(command,quest);
    }
    public void ClosePreview(Guid token)=>_previews.Remove(token);
    public async ValueTask<CommandResult> InspectStageAsync(){var s=await _repository.SnapshotAsync();Active(s,R1LifeRules.Show);At(s,"R1-O02.stage_check");Sources(s,"R1-SHOW-R-STAGE");return await Write(s,"r1_theatre_stage_inspected",State(s) with{StageInspected=true},new{stage_edge_checked=true,return_passage_clear=true});}
    public async ValueTask<CommandResult> LampActionAsync(string action)
    {
        var s=await _repository.SnapshotAsync();Active(s,R1LifeRules.Show);At(s,"R1-O02.power");Sources(s,"R1-SHOW-R-EQUIPMENT");var st=State(s);var next=st;ImmutableArray<ItemChange> items=[];
        switch(action)
        {
            case "isolate":next=st with{LampIsolated=true,LampPowered=false,LampTestConfiguration=null,LampConfiguration=checked(st.LampConfiguration+1)};break;
            case "brace":case "cell":
                Require(st.LampIsolated&&!st.LampPowered,"安装前先隔离剧场自身灯供给。");string suffix=action=="brace"?"BRACE":"CELL";var item=s.Items.Single(i=>i.Id=="R1-THEATRE-"+suffix);
                if(item.OwnerKind=="installed"&&item.OwnerId==R1LifeRules.Lamp)return new(CommitStatus.AlreadyCommitted,null,[]);
                Require(item.OwnerKind=="warehouse"&&item.OwnerId==R1LifeRules.Store,"该唯一物件不在剧场仓库，不能以主线物品或新副本替代。");items=[new(item.Id,item.DefinitionId,item.OwnerKind,item.OwnerId,"installed",R1LifeRules.Lamp,null)];
                next=action=="brace"?st with{BraceInstalled=true,LampConfiguration=checked(st.LampConfiguration+1),LampTestConfiguration=null}:st with{CellInstalled=true,LampConfiguration=checked(st.LampConfiguration+1),LampTestConfiguration=null};break;
            case "power":Require(st.LampIsolated&&st.BraceInstalled&&st.CellInstalled,"先在隔离状态安装实际支架和电芯。");next=st with{LampIsolated=false,LampPowered=true,LampConfiguration=checked(st.LampConfiguration+1),LampTestConfiguration=null};break;
            case "test":Require(!st.LampIsolated&&st.LampPowered&&st.BraceInstalled&&st.CellInstalled,"须测试已实际接通的当前配置。");next=st with{LampTestConfiguration=st.LampConfiguration};break;
            default:throw new ArgumentException("未知剧场灯具动作。");
        }
        return await Write(s,"r1_theatre_lamp_"+action,next,new{action,configuration=next.LampConfiguration,test_configuration=next.LampTestConfiguration,theatre_local_items_only=true},items);
    }
    public async ValueTask<CommandResult> PlacePropAsync(string prop)
    {
        Require(prop is "screen" or "drum","未知剧场道具。");var s=await _repository.SnapshotAsync();Active(s,R1LifeRules.Show);At(s,"R1-O02.props");Sources(s,"R1-SHOW-R-EQUIPMENT","R1-SHOW-R-STAGE");string suffix=prop.ToUpperInvariant();var item=s.Items.Single(i=>i.Id=="R1-THEATRE-"+suffix);
        if(item.OwnerKind=="installed"&&item.OwnerId==R1LifeRules.Stage)return new(CommitStatus.AlreadyCommitted,null,[]);Require(item.OwnerKind=="warehouse"&&item.OwnerId==R1LifeRules.Store,"道具唯一实物不在本地仓库。");
        var st=State(s);var next=prop=="screen"?st with{ScreenPlaced=true}:st with{DrumPlaced=true};return await Write(s,"r1_theatre_prop_delivered",next,new{prop,destination=R1LifeRules.Stage},[new(item.Id,item.DefinitionId,item.OwnerKind,item.OwnerId,"installed",R1LifeRules.Stage,null)]);
    }
    public async ValueTask<CommandResult> AssignRolesAsync(TheatreRoles roles)
    {
        var s=await _repository.SnapshotAsync();Active(s,R1LifeRules.Show);At(s,"R1-O02.crew");Sources(s,"R1-SHOW-L-CREW","R1-SHOW-R-STAGE");Require(R1LifeRules.ValidRoles(roles),"两个实际当地岗位、相应角色和已公开时段须一致；不能由同一人同时兼任。");
        return await Write(s,"r1_theatre_actual_roles_assigned",State(s) with{Roles=roles},roles);
    }
    public async ValueTask<CommandResult> WatchShowAsync(bool skip)
    {
        var s=await _repository.SnapshotAsync();BranchOperations.Safe(s);At(s,"R1-O02.performance");Require(Quest(s,R1LifeRules.Show).Lifecycle==QuestLifecycle.Closed&&Quest(s,R1LifeRules.Show).Outcome==QuestOutcome.Completed&&BranchOperations.Receipt(s,R1LifeRules.Show),"只可观看已实际完成安排的节目。");
        if(State(s).ShowWatched)return new(CommitStatus.AlreadyCommitted,null,[]);return await Write(s,"r1_theatre_scene_viewed",State(s) with{ShowWatched=true},new{skip,programme=State(s).Roles,reward=0});
    }
}

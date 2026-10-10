using System.Collections.Immutable;
using System.Text.Json;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.EventScheduler;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Core.ChapterFive;
using Mist.Core.ChapterFour;
using Mist.Core.ChapterTwo;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Engineering;
using Mist.Core.Exploration;
using Mist.Core.Quests;

namespace Mist.Application.ChapterFive;

public sealed record ChapterFiveView(BranchSnapshot Snapshot,ActorState Actor,ChapterFiveState State,ImmutableArray<QuestState> Quests,ImmutableArray<ChapterMaterial> Materials,ImmutableArray<ChapterStation> Stations,ImmutableDictionary<string,SceneGeometry> Geometry);
public sealed record OldCityPreview(Guid Token,bool Ready,ImmutableArray<RuleIssue> Issues,string Task,string? DraftHash,bool StructuredAssistance);
public sealed partial class ChapterFiveService
{
    public const string StateId="chapter5",OriginalCase="OC-MASTER-01";
    public static readonly ImmutableArray<string> MainQuests=["Q-501","Q-502","Q-503","Q-504"];
    public static readonly ImmutableArray<string> PersonalQuests=["CQ-MIRA-03","CQ-OREN-03"];
    private readonly IAdventureRepository _repository;private readonly RuntimeCatalog _catalog;private readonly Dictionary<Guid,CommitPlanCommand> _previews=new();
    public ChapterFivePackage Package {get;}
    public ImmutableDictionary<string,SceneGeometry> Geometry {get;}
    public ChapterFiveService(IAdventureRepository repository,RuntimeCatalog catalog,ChapterFivePackage package){ChapterFivePackage.Validate(package);_repository=repository;_catalog=catalog;Package=package;Geometry=package.Scenes.ToImmutableDictionary(s=>s.Geometry.Id,s=>s.Geometry);}
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static ActorState Actor(BranchSnapshot s)=>BranchOperations.Read<ActorState>(s,"actor")??throw new InvalidOperationException("实际角色位置缺失。");
    private ChapterFiveState State(BranchSnapshot s)=>BranchOperations.Read<ChapterFiveState>(s,StateId) is{} state&&state.ContentVersion==Package.Version?state:throw new InvalidOperationException("旧都尚未初始化或内容版本不符。");
    private static QuestState? Quest(BranchSnapshot s,string id)=>BranchOperations.Read<QuestState>(s,"quest:"+id);
    private static bool Closed(BranchSnapshot s,string id)=>Quest(s,id) is{Lifecycle:QuestLifecycle.Closed,Outcome:QuestOutcome.Completed};
    private static bool Result(BranchSnapshot s,string id)=>BranchOperations.Read<OldCityEncounterResult>(s,"R5:result:"+id) is{Completed:true} result&&s.Receipts.Any(r=>r.SemanticKey=="R5:"+id+":result"&&r.ActionType=="old_city_encounter_result"&&StrictJson.Deserialize<OldCityEncounterResult>(r.ResultJson)==result);
    private EntityChange Change(BranchSnapshot s,ChapterFiveState state)=>BranchOperations.Change(s,StateId,"chapter_five_state",state);
    private string[] Reads(BranchSnapshot s)=>s.Entities.Select(e=>e.Id).Concat(new[]{StateId,"actor","ship:glimmer","commission:next-inquiry","sea-route:R4:R5","berth:R5","readiness:TWR-R5","R4:actual_handover","R5:actual_handover"}).Concat(MainQuests.Concat(PersonalQuests).SelectMany(q=>new[]{"quest:"+q,"proposal:"+q})).Distinct().ToArray();
    private async ValueTask<CommandResult> Execute(CommitPlanCommand command)
    {
        var session=_repository.Session;
        if(command.Context.Profile!=session.Profile||command.Context.Branch!=session.Branch||command.Context.Load!=session.Load||command.Context.ReleaseId!=session.ReleaseId)return new(CommitStatus.Rejected,null,[new("stale_load",StateId,"档案分支或读取世代改变，请重新预览。")]);
        if(_repository is not IEventLeaseStore lease)throw new InvalidOperationException("旧都工作需要原子前景占用接口。");string owner="chapter5:"+command.Context.Command.Value;Require(await lease.TryLeasesAsync(command.Context.Load,owner,["input:foreground"]),"其他前景工作未交接。");try{return await _repository.ExecuteAsync(command);}finally{await lease.ReleaseLeasesAsync(command.Context.Load,owner);}
    }
    private ValueTask<CommandResult> Commit(BranchSnapshot s,string action,ImmutableArray<EntityChange> changes,object payload,string? key=null,ImmutableArray<ItemChange> items=default,ImmutableArray<BalanceChange> balances=default,DraftStamp? draft=null)=>Execute(BranchOperations.Plan(_repository,s,key??"R5:action:"+Guid.NewGuid(),action,GateChanges(s,changes),Reads(s),payload,items,balances,draft:draft));
    private static void Safe(BranchSnapshot s)
    {
        BranchOperations.Safe(s);Require(!s.Entities.Where(e=>e.Kind=="event").Select(e=>StrictJson.Deserialize<EventSession>(e.Json)).Any(e=>e.State==EventLifecycle.Running),"当前事件未安全交接。");Require(!s.Entities.Where(e=>e.Kind=="transport_segment").Select(e=>StrictJson.Deserialize<TransportSegment>(e.Json)).Any(e=>e.CargoInTransit&&!e.Completed),"当前运输未交接。");Require(!BranchOperations.Receipt(s,"Q-604"),"正式终章出发后旧都工作暂停。");
    }
    private static bool Current(BranchSnapshot s,IEnumerable<EntityStamp> reads)=>reads.All(r=>(BranchOperations.Entity(s,r.EntityId)?.Revision??-1)==r.Revision);
    private static TechnicalHandover ActualArrival(BranchSnapshot s)
    {
        Require(Closed(s,"Q-404"),"需要实际学城核验收束。");var receipt=s.Receipts.SingleOrDefault(r=>r.SemanticKey=="Q-404:resolution"&&r.ActionType=="glasshaven_technical_handover_received")??throw new InvalidOperationException("学城实际交接来源缺失。");var hand=StrictJson.Deserialize<TechnicalHandover>(receipt.ResultJson);var actual=BranchOperations.Read<TechnicalHandover>(s,"R4:actual_handover");
        Require(hand is{Destination:"R5",Scope:"bounded_validated_alternative",Evidence:"E-06/E-07/E-11",Location:"old_city_north_station",MaraCurrentStatus:"unknown",PolicyDecision:"not_selected"}&&!hand.PublicPreparation.IsDefault&&hand.PublicPreparation.SequenceEqual(new[]{"verified_scheme","six_tower_changes","transition_supply","common_protocol","field_trials"})&&actual is not null&&StrictJson.Serialize(actual)==StrictJson.Serialize(hand),"学城有限交接、当前未知与真实来源不符。");
        var commission=BranchOperations.Entity(s,"commission:next-inquiry")??throw new InvalidOperationException("真实跨区委托缺失。");using(var d=JsonDocument.Parse(commission.Json)){var j=d.RootElement;Require(j.GetProperty("allowed").GetBoolean()&&j.GetProperty("region").GetString()=="R5"&&j.GetProperty("basis").GetString()=="Q-404:resolution"&&j.GetProperty("purpose").GetString()=="verify_north_station_and_legal_access","跨区委托不允许本次北站核验。");}
        var ship=BranchOperations.Read<SharedVessel>(s,"ship:glimmer")??throw new InvalidOperationException("真实巡灯船缺失。");Require(ship.DockRegion=="R5"&&ship.WatchOfficer=="N-DECKWATCH"&&s.Entities.Count(e=>e.Kind=="shared_vessel")==1,"同一巡灯船实际泊位不符。");
        var route=BranchOperations.Read<SeaRoute>(s,"sea-route:R4:R5")??throw new InvalidOperationException("真实R4到R5航路缺失。");var originalRoute=StrictJson.Deserialize<SeaRoute>(BranchOperations.Evidence(s,route.AuthorityReceipt,"sea_route_verified").ResultJson);Require(route.A=="R4"&&route.B=="R5"&&originalRoute.A==route.A&&originalRoute.B==route.B&&originalRoute.GateReads.SequenceEqual(route.GateReads)&&Current(s,route.GateReads),"当前海路来源或读戳不符。");
        var berth=BranchOperations.Read<VesselBerth>(s,"berth:R5")??throw new InvalidOperationException("真实旧都泊位缺失。");var originalBerth=StrictJson.Deserialize<VesselBerth>(BranchOperations.Evidence(s,berth.AuthorityReceipt,"vessel_berth_verified").ResultJson);Require(berth.Instance==ship.Instance&&berth.Region=="R5"&&berth.Scene=="R5-S01"&&StrictJson.Serialize(originalBerth with{AuthorityReceipt=berth.AuthorityReceipt})==StrictJson.Serialize(berth)&&Current(s,berth.GateReads),"泊位唯一船实例或当前许可不符。");
        Require(s.Receipts.Where(r=>r.ActionType=="sail_shared_glimmer").Any(r=>{using var d=JsonDocument.Parse(r.ResultJson);var j=d.RootElement;return j.GetProperty("ship_instance").GetGuid()==ship.Instance&&j.GetProperty("from").GetString()=="R4"&&j.GetProperty("to").GetString()=="R5";}),"需要同一船真实R4到R5航行，不能只改角色地区。");Require(Actor(s).Scene=="R5-S01","请从实际旧城渡口接入。");return hand;
    }
    public async ValueTask InitializeRegionAsync()
    {
        var s=await _repository.SnapshotAsync();if(BranchOperations.Entity(s,StateId) is not null){State(s);return;}Safe(s);ActualArrival(s);Require(Actor(s).Position.Distance(Geometry["R5-S01"].Spots.Single(x=>x.Id=="R5-S01.berth").Stand)<=2.5&&new NavigationGrid(Geometry["R5-S01"]).CanStand(Actor(s).Position,true),"实际登陆落点须在公开安全泊位。");
        var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(Change(s,new(){ContentVersion=Package.Version}));foreach(string q in MainQuests.Concat(PersonalQuests))changes.Add(BranchOperations.Change(s,"quest:"+q,"quest",new QuestState(q,Guid.NewGuid(),q=="Q-501"?QuestLifecycle.Active:QuestLifecycle.Unaccepted,null)));
        // Finite local mission objects. No engineering reserve or player reward is minted on arrival.
        var items=ImmutableArray.Create(new ItemChange("R5-basic-kit","R5-MISSION-INSPECTION-KIT","new","","warehouse","R5-local-mission-desk",null),new ItemChange("R5-basic-lamp","R5-MISSION-LAMP","new","","warehouse","R5-local-mission-desk",null),new ItemChange("R5-basic-line","R5-MISSION-SAFETY-LINE","new","","warehouse","R5-local-mission-desk",null),new ItemChange(OriginalCase,"R5-ORIGINAL-ARCHIVE-CASE","new","","warehouse","R5-local-original-custodian",null));
        var result=await Commit(s,"old_city_region_initialized",changes.ToImmutable(),new{scope="original_unreviewed_runtime_candidate",mara_current_status="unknown",experience_awarded=0,private_invitation=false},"R5:initialize:"+Package.Version,items);Require(result.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,"旧都初始化未提交。");
    }
    private bool TowerSafe(BranchSnapshot s,ChapterFiveState st)=>st.JointPermission&&st.ArchiveRelayIsolated&&st.ArchiveRackSecured&&Result(s,"ENC-R5-04")&&st.Load==new OldCityTowerLoad(3,1,0)&&st.Operator=="ROLE-R5-OPERATOR"&&st.TowerCheckRevision==st.TowerRevision&&st.TowerReturnChecked&&new[]{"ROLE-R5-OPERATOR","N-ADA","ROLE-R5-ARCHIVE-CUSTODIAN"}.All(st.Supervisors.Contains);
    private ImmutableArray<EntityChange> GateChanges(BranchSnapshot s,ImmutableArray<EntityChange> changes)
    {
        var changed=changes.SingleOrDefault(c=>c.Id==StateId);var st=changed is null?BranchOperations.Read<ChapterFiveState>(s,StateId):StrictJson.Deserialize<ChapterFiveState>(changed.Json);if(st is null)return changes;
        var gates=new Dictionary<string,bool>{{"registered_resident_landing",st.Landing},{"public_streets",true},{"current_credentials_checked",st.CredentialChecked&&st.CheckpointPassageChecked},{"basic_mission_kit",st.BasicKit},{"tunnel_currently_safe",st.TunnelPassageChecked},{"detention_approach_safe",st.RescueScope&&st.DetentionRecordsChecked&&st.DoorDriveIsolated&&st.DutyObstructionCleared},{"inner_evacuation_open",st.MaraReleased&&st.EvacuationBoltOpen&&st.EvacuationChecked},{"public_review_hall",true},{"joint_takeover_permission",st.JointPermission},{"public_sign_workshop",true},{"local_property_desk",true}};var output=changes.ToBuilder();foreach(var g in gates){string id="R5:gate:"+g.Key;var old=BranchOperations.Read<OldCityGate>(s,id);if(old is null||old.Allowed!=g.Value)output.Add(BranchOperations.Change(s,id,"chapter_gate",new OldCityGate(g.Key,g.Value,Package.Version)));}
        var ready=BranchOperations.Read<TowerReadiness>(s,"readiness:TWR-R5");if(ready is not null){bool safe=TowerSafe(s,st);if(ready.Safe!=safe)output.Add(BranchOperations.Change(s,"readiness:TWR-R5","tower_readiness",ready with{Safe=safe}));}return output.ToImmutable();
    }
    public ImmutableDictionary<string,SceneGeometry> CurrentGeometry(BranchSnapshot s){var st=State(s);return Package.Scenes.ToImmutableDictionary(x=>x.Geometry.Id,x=>x.Geometry with{Danger=x.Hazards.Where(h=>h.Id switch{"R5-S04.local_threat"=>!st.TunnelPassageChecked,"R5-S06.local_threat"=>!Result(s,"ENC-R5-03"),"R5-S08.local_threat"=>!TowerSafe(s,st),_=>true}).Select(h=>h.Area).ToImmutableArray()});}
    public async ValueTask<ImmutableDictionary<string,SceneGeometry>> CurrentGeometryAsync()=>CurrentGeometry(await _repository.SnapshotAsync());
    private void At(BranchSnapshot s,string spot){var actor=Actor(s);var geometry=CurrentGeometry(s);Require(geometry.TryGetValue(actor.Scene,out var scene)&&scene.Spots.Any(p=>p.Id==spot&&actor.Position.Distance(p.Stand)<=2.5)&&new NavigationGrid(scene).CanStand(actor.Position,true),"请先走到当前真实安全操作站位。");}
    private bool Acquired(BranchSnapshot s,ChapterMaterial m)=>BranchOperations.Read<ChapterMaterialAccess>(s,"material:"+m.Id) is{} access&&access.Id==m.Id&&access.Version==m.Version&&access.Fingerprint==m.Fingerprint&&access.SourceRoot==m.SourceRoot;
    private bool Accessible(BranchSnapshot s,ChapterMaterial m)
    {
        var st=State(s);if(m.Quest=="Q-502"&&!Closed(s,"Q-501"))return false;if(m.Quest=="Q-503"&&!st.MaraReceived)return false;if(m.Quest=="Q-504"&&!Closed(s,"Q-503"))return false;if(m.Id=="R5-Q504-R-TRANSFER"&&!st.RookRefusalRecorded)return false;
        if(m.AccessClass=="individual_permission")return m.Quest switch{"CQ-MIRA-03"=>st.MiraInvited,"CQ-OREN-03"=>st.OrenInvited,_=>false};if(m.AccessClass=="public")return true;
        var scope=BranchOperations.Read<OldCityRecordScope>(s,"R5:responsibility_scope");return m.AccessClass=="controlled_review"&&st.ResponsibilityScope&&scope is{Version:"OC-responsibility-set@1",Recipient:"patrol_technician",Purpose:"distinguish_design_policy_execution_responsibility",Grantor:"R5-local-record-custodian"}&&new[]{"design_limits","correction_history","policy_authorization","execution_order","version_source"}.All(scope.Fields.Contains);
    }
    private bool Sources(BranchSnapshot s,params string[] ids)=>ids.All(id=>Package.Materials.SingleOrDefault(m=>m.Id==id) is{} m&&Acquired(s,m)&&Accessible(s,m));
    public async ValueTask<ChapterFiveView> ViewAsync(){var s=await _repository.SnapshotAsync();var actor=Actor(s);return new(s,actor,State(s),MainQuests.Concat(PersonalQuests).Select(q=>Quest(s,q)!).ToImmutableArray(),Package.Materials.Where(m=>Acquired(s,m)&&Accessible(s,m)).ToImmutableArray(),Package.Stations.Where(st=>st.Spot.StartsWith(actor.Scene+'.',StringComparison.Ordinal)).ToImmutableArray(),CurrentGeometry(s));}
    public async ValueTask TraverseAsync(ImmutableArray<Point2> path)
    {
        if(path.IsDefaultOrEmpty)return;var s=await _repository.SnapshotAsync();Safe(s);var a=Actor(s);var nav=new NavigationGrid(CurrentGeometry(s)[a.Scene]);var from=a.Position;foreach(var to in path){Require(nav.SegmentClear(from,to,true),"路径穿过真实障碍或未处置危险。");from=to;}var result=await Commit(s,"old_city_walk",[BranchOperations.Change(s,"actor","actor",a with{Position=from,ActionClock=checked(a.ActionClock+1)})],new{actual_path=path});Require(result.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,"当前状态变化，移动未提交。");
    }
    public async ValueTask MoveAsync(Point2 p)=>await TraverseAsync([p]);
    public string? PortIssue(BranchSnapshot s,Port p)=>BranchOperations.Read<OldCityGate>(s,"R5:gate:"+Package.PortGates[p.Id]) is{Allowed:true}?null:"当前现场许可或独立安全工作尚未成立。";
    public async ValueTask<CommandResult> EnterPortAsync(string id)
    {
        var s=await _repository.SnapshotAsync();Safe(s);var actor=Actor(s);var geometry=CurrentGeometry(s);var port=geometry[actor.Scene].Ports.Single(p=>p.Id==id);Require(actor.Position.Distance(port.Stand)<=2.5,"请先走到实际出口。");Require(PortIssue(s,port) is null,PortIssue(s,port)??"");var pair=geometry[port.TargetScene].Ports.Single(p=>p.Id==port.TargetPort);Require(new NavigationGrid(geometry[actor.Scene]).CanStand(actor.Position,true)&&new NavigationGrid(geometry[port.TargetScene]).CanStand(pair.Stand,true),"实际出入口危险。");
        var connection=_catalog.Connections.Single(c=>c.A==actor.Scene&&c.B==port.TargetScene||c.B==actor.Scene&&c.A==port.TargetScene);string gate="R5:gate:"+Package.PortGates[id],travel="travel-gate:"+TravelService.ConnectionKey(connection.A,connection.B);var reads=ImmutableArray.Create(new EntityStamp(gate,BranchOperations.Entity(s,gate)!.Revision));var old=BranchOperations.Read<ConnectionSafetyState>(s,travel);
        if(old is null||!old.Dependencies.SequenceEqual(reads)){var issued=await Commit(s,"connection_safety_verified",[],new ConnectionSafetyState(connection.A,connection.B,TravelService.Contract(connection),true,true,reads,""));Require(issued.Status==CommitStatus.Committed&&issued.Receipt is not null,"连接现场证明未提交。");var registered=await new TravelService(_repository,_catalog,CurrentGeometry(await _repository.SnapshotAsync())).RegisterConnectionSafetyAsync(issued.Receipt!.Id.Value.ToString());Require(registered.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,"连接来源登记未提交。");s=await _repository.SnapshotAsync();actor=Actor(s);}
        var proof=new TraversedConnection(actor.Scene,port.TargetScene,port.Id,pair.Id,port.Stand,pair.Stand,checked(actor.ActionClock+1),TravelService.Contract(connection),reads.Add(new(travel,BranchOperations.Entity(s,travel)!.Revision)),true);var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(BranchOperations.Change(s,"actor","actor",actor with{Scene=port.TargetScene,Position=pair.Stand,ActionClock=proof.ActionClock}));var st=State(s);
        if(port.TargetScene=="R5-S05"&&actor.Scene is "R5-S03" or "R5-S04")changes.Add(Change(s,st with{UsedRoute=actor.Scene=="R5-S03"?"CHECKPOINT-DUTY-PASSAGE":"TUNNEL-SERVICE-PASSAGE"}));
        if(actor.Scene=="R5-S06"&&port.TargetScene=="R5-S02"&&st.MaraReleased&&!st.MaraReceived){changes.Add(Change(s,st with{MaraEscorted=true}));changes.Add(BranchOperations.Change(s,"R5:mara","mara_rescue",new MaraRescue("escorted_alive","R5-S02","actual_inner_evacuation","pending_reception",proof.ActionClock)));}
        return await Commit(s,"scene_transition",changes.ToImmutable(),proof);
    }
    public async ValueTask<CommandResult> DiscoverAsync(string id){var s=await _repository.SnapshotAsync();Safe(s);var m=Package.Materials.Single(x=>x.Id==id);At(s,m.Spot);Require(Accessible(s,m),"当前具体材料访问范围未成立。");if(Acquired(s,m))return new(CommitStatus.AlreadyCommitted,null,[]);return await Commit(s,"old_city_material_acquired",[BranchOperations.Change(s,"material:"+id,"material_access",new ChapterMaterialAccess(id,m.Version,m.TranslationVersion,m.Fingerprint,m.SourceRoot,m.Spot))],new{material=id,source_root=m.SourceRoot,source_channel=m.Kind=="listening_script"?"written_transcript_only":"written_original",audio_listened=false},"material:"+id+":acquired");}
    public async ValueTask<ChapterMaterial> OpenMaterialAsync(string id)=>(await ViewAsync()).Materials.SingleOrDefault(x=>x.Id==id)??throw new InvalidOperationException("仅实际取得且当前获准的材料可查看。");
    public async ValueTask<DraftSaved> SaveDraftAsync(string task,long expectedRevision,DraftId? draft,string body){Require(Package.WritingTasks.Any(x=>x.Id==task),"未登记写作任务。");var session=_repository.Session;return await _repository.SaveAsync(new(session.Profile,session.Branch,session.Load,task,draft,expectedRevision,body,Package.Version));}
    public async ValueTask<CommandResult> ConfirmMeaningAsync(string quest,ImmutableDictionary<string,string> fields,bool structuredAssistance)
    {
        var s=await _repository.SnapshotAsync();Safe(s);var task=Package.WritingTasks.Single(t=>t.Quest==quest);At(s,task.Spot);Require(Quest(s,quest) is{Lifecycle:QuestLifecycle.Active},"任务尚未开启或已交接。");var st=State(s);Require(quest switch{"CQ-MIRA-03"=>st.MiraInvited,"CQ-OREN-03"=>st.OrenInvited,_=>true},"私人邀请已撤回。");var draft=await _repository.GetDraftAsync(task.Id);Require(structuredAssistance||draft is{Deleted:false}&&!string.IsNullOrWhiteSpace(draft.Body),"空稿须明确结构化辅助。");return await Commit(s,"old_city_meaning_confirmed",[BranchOperations.Change(s,"proposal:"+quest,"proposal",new ChapterWorldMeaning(quest,draft?.Hash,structuredAssistance,fields))],new{structured_assistance=structuredAssistance,free_english_understanding="not_claimed",language_score=(string?)null},draft:new(task.Id,draft?.Id,draft?.Revision??0,draft?.Hash));
    }
    public ValueTask<CommandResult> ConfirmPreviewAsync(Guid token)=>_previews.TryGetValue(token,out var command)?Execute(command):ValueTask.FromResult(new CommandResult(CommitStatus.Rejected,null,[new("preview_closed",StateId,"预览未就绪或已取消。") ]));
    public void ClosePreview(Guid token)=>_previews.Remove(token);
    public string SubmissionSpot(string quest)=>quest switch{"Q-501"=>"R5-S02.report","Q-502"=>"R5-S02.reception","Q-503"=>"R5-S07.hearing","Q-504"=>"R5-S08.handover","CQ-MIRA-03"=>"R5-S07.personal_review","CQ-OREN-03"=>"R5-S07.oren_duty",_=>throw new InvalidOperationException("未登记交接接收点。")};
}

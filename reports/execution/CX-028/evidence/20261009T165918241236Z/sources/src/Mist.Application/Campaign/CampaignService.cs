using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Inventory;
using Mist.Application.Q102;
using Mist.Application.Storage;
using Mist.Core.Campaign;
using Mist.Core.Combat;
using Mist.Core.Content;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Exploration;
using Mist.Core.Quests;

namespace Mist.Application.Campaign;

public sealed record ShoreEncounterState(BattleSnapshot Battle,BattleSnapshot Entry,ActorState ReturnActor,bool ResultApplied=false);
public sealed record CampaignView(BranchSnapshot Snapshot,ActorState Actor,CampaignState Campaign,PrologueState Prologue,
    string CurrentQuest,ImmutableArray<QuestState> Quests,ImmutableArray<CampaignMaterial> Materials,
    ImmutableArray<CampaignStation> Stations,ShoreEncounterState? ShoreBattle,bool Q102Ready);
public sealed record CampaignSubmissionPreview(Guid Token,bool Ready,ImmutableArray<RuleIssue> Issues,string DraftTask,string? DraftHash,bool StructuredAssistance);
public sealed record ShoreActionPreview(Guid Token,ActionPreviewView Action);

// Commands are typed local adventure actions. This service never evaluates English or accepts content-supplied world writes.
public sealed class CampaignService
{
    public const string RescueDraft="Q-P02:rescue-message",HarbourDraft="Q-101:work-request";
    public const string ShoreCombat="combat:shore",ToolId="QP-RESCUE-TOOLS-ONE",RopeId="QP-RESCUE-ROPE-ONE";
    private readonly IAdventureRepository _repository;
    private readonly RuntimeCatalog _catalog;
    private readonly BattleDefinitions _battleDefinitions;
    private readonly Dictionary<Guid,CommitPlanCommand> _previews=new();
    private readonly Dictionary<Guid,CommitPlanCommand> _battlePreviews=new();
    public CampaignPackage Package {get;}
    public ImmutableDictionary<string,SceneGeometry> Geometry {get;}
    public CampaignService(IAdventureRepository repository,RuntimeCatalog catalog,CampaignPackage package)
    {
        CampaignRules.Validate(package);_repository=repository;_catalog=catalog;Package=package;
        Geometry=package.Scenes.ToImmutableDictionary(s=>s.Id);_battleDefinitions=new(catalog);
    }
    public async ValueTask InitializeNewAsync()
    {
        var snapshot=await _repository.SnapshotAsync();
        if(BranchOperations.Entity(snapshot,"actor") is not null)
        {
            var state=BranchOperations.Read<CampaignState>(snapshot,"campaign");
            if(state is null||state.Scope!=CampaignRules.Scope||state.ContentVersion!=Package.Version)
                throw new InvalidOperationException("该存档不是本序章候选的正常新档，请保留原档并选择独立新档。");
            return;
        }
        if(snapshot.Receipts.Any(r=>r.SemanticKey is "internal:q102-slice:seed" or "Q-101:resolution")||snapshot.Balances.GetValueOrDefault("experience")!=0)
            throw new InvalidOperationException("已有冒险进度不能改写成正常序章新档。");
        await new PartyService(_repository,_catalog).InitializeStarterCollectionAsync();
        snapshot=await _repository.SnapshotAsync();
        var changes=ImmutableArray.CreateBuilder<EntityChange>();
        changes.Add(BranchOperations.Change(snapshot,"actor","actor",new ActorState("R0-S01",new(20,84),0)));
        changes.Add(BranchOperations.Change(snapshot,"campaign","campaign",new CampaignState(CampaignRules.Scope,Package.Version)));
        changes.Add(BranchOperations.Change(snapshot,"prologue","prologue",new PrologueState()));
        changes.Add(BranchOperations.Change(snapshot,"coast:survivors","rescue_group",new CoastSurvivorGroup(["CP-LINA","COAST-SURVIVOR-02","COAST-SURVIVOR-03","COAST-SURVIVOR-04"],"R0-S01","awaiting_safe_evacuation")));
        foreach(var id in CampaignRules.Quests)
            changes.Add(BranchOperations.Change(snapshot,"quest:"+id,"quest",new QuestState(id,Guid.NewGuid(),id=="Q-P01"?QuestLifecycle.Active:QuestLifecycle.Unaccepted,null)));
        changes.Add(BranchOperations.Change(snapshot,"proposal:Q-P02","proposal",new CampaignMeaning<RescueMessageMeaning>(0,false,null,false,null)));
        changes.Add(BranchOperations.Change(snapshot,"proposal:Q-101","proposal",new CampaignMeaning<HarbourRequestMeaning>(0,false,null,false,null)));
        var command=BranchOperations.Plan(_repository,snapshot,"campaign:new:"+Package.Version,"normal_campaign_created",changes.ToImmutable(),["actor","campaign","prologue"],
            new{scope=CampaignRules.Scope,content_version=Package.Version,experience_awarded=0,prologue_played=false},
            [new(ToolId,"QP-RESCUE-TOOLS","new","","warehouse","R0-S01",null),new(RopeId,"QP-RESCUE-ROPE","new","","warehouse","R0-S01",null)],[new("marks",100)]);
        RequireCommitted(await _repository.ExecuteAsync(command));
    }
    public async ValueTask<CampaignView> ViewAsync()
    {
        var s=await _repository.SnapshotAsync();var campaign=BranchOperations.Read<CampaignState>(s,"campaign")??throw new InvalidOperationException("正常新档尚未建立。");
        if(campaign.Scope!=CampaignRules.Scope||campaign.ContentVersion!=Package.Version)throw new InvalidOperationException("序章内容版本与存档不符。");
        var actor=BranchOperations.Read<ActorState>(s,"actor")!;var p=BranchOperations.Read<PrologueState>(s,"prologue")!;
        var quests=CampaignRules.Quests.Select(id=>BranchOperations.Read<QuestState>(s,"quest:"+id)!).ToImmutableArray();
        return new(s,actor,campaign,p,CampaignRules.CurrentQuest(quests),quests,
            Package.Materials.Where(m=>BranchOperations.Entity(s,"material:"+m.Id) is not null).ToImmutableArray(),
            Package.Stations.Where(st=>Geometry.TryGetValue(actor.Scene,out var scene)&&scene.Spots.Any(spot=>spot.Id==st.Spot)).ToImmutableArray(),
            BranchOperations.Read<ShoreEncounterState>(s,ShoreCombat),campaign.Q102Ready);
    }
    private static void RequireCommitted(CommandResult result)
    {if(result.Status is not(CommitStatus.Committed or CommitStatus.AlreadyCommitted))throw new InvalidOperationException("序章操作未提交："+StrictJson.Serialize(result));}
    private void At(CampaignView view,string spot)
    {
        var stand=Geometry.TryGetValue(view.Actor.Scene,out var scene)?scene.Spots.SingleOrDefault(s=>s.Id==spot):null;
        if(stand is null||view.Actor.Position.Distance(stand.Stand)>2.5)throw new InvalidOperationException("请先沿可走路径到达该物件的操作站位。");
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static bool Closed(CampaignView view,string id)=>BranchOperations.Receipt(view.Snapshot,id+":resolution");
    private static bool Acquired(CampaignView view,string id)=>view.Materials.Any(m=>m.Id==id);
    private static EntityChange P(CampaignView view,PrologueState state)=>BranchOperations.Change(view.Snapshot,"prologue","prologue",state);
    private static CoastSurvivorGroup People(CampaignView view)
    {
        var group=BranchOperations.Read<CoastSurvivorGroup>(view.Snapshot,"coast:survivors")??throw new InvalidOperationException("本次救援人员登记缺失。");
        Require(group.People.Length==CampaignRules.Survivors&&group.People.Distinct().Count()==CampaignRules.Survivors&&group.People.Contains("CP-LINA"),"救援人员身份或数量不一致，不能由界面补造。");return group;
    }
    private static EntityChange PeopleAt(CampaignView view,string location,string phase)=>BranchOperations.Change(view.Snapshot,"coast:survivors","rescue_group",People(view) with{Location=location,Phase=phase});
    private ValueTask<CommandResult> Commit(CampaignView view,string action,ImmutableArray<EntityChange> changes,
        ImmutableArray<ItemChange> items=default,ImmutableArray<BalanceChange> balances=default,string? semantic=null,object? payload=null,DraftStamp? draft=null)
    {
        BranchOperations.Safe(view.Snapshot);
        var command=BranchOperations.Plan(_repository,view.Snapshot,semantic??"campaign:action:"+Guid.NewGuid(),action,changes,
            ["actor","campaign","prologue","coast:survivors",..CampaignRules.Quests.Select(q=>"quest:"+q)],payload??new{scope=CampaignRules.Scope},items,balances,draft:draft);
        return _repository.ExecuteAsync(command);
    }
    public async ValueTask MoveAsync(Point2 position)=>await TraverseAsync([position]);
    public async ValueTask TraverseAsync(ImmutableArray<Point2> positions)
    {
        if(positions.IsDefaultOrEmpty)return;var v=await ViewAsync();Require(!v.Q102Ready,"已进入渡轮工作，请使用渡轮工作服务的同一角色位置。");
        var nav=new NavigationGrid(Geometry[v.Actor.Scene]);Point2 from=v.Actor.Position;
        foreach(var to in positions){Require(nav.SegmentClear(from,to,v.Actor.Scene=="R0-S02"&&!v.Prologue.ShoreSafe),"路径穿过障碍或当前危险，未移动角色。");from=to;}
        RequireCommitted(await Commit(v,"campaign_walk",[BranchOperations.Change(v.Snapshot,"actor","actor",v.Actor with{Position=from,ActionClock=checked(v.Actor.ActionClock+1)})]));
    }
    public string? PortIssue(CampaignView view,Port port)
    {
        string a=view.Actor.Scene,b=port.TargetScene;
        if(!Geometry.ContainsKey(b))return "该后续场景尚未由本序章候选接入。";
        if(a=="R1-S01"&&b=="R0-S04")return "初次接应是救援通行，返航需Q104后正式取得巡灯船并由旅行系统核对。";
        if(a=="R0-S04"&&b=="R1-S01"&&!Closed(view,"Q-P03"))return "救援、通信、记录与港口接应尚未完整成立。";
        if((a=="R0-S01"&&b=="R0-S02"||a=="R0-S02"&&b=="R0-S01")&&!view.Prologue.PathReinforced)return "船舱安全通路尚未加固。";
        if((a=="R0-S03"&&b=="R0-S04"||a=="R0-S04"&&b=="R0-S03")&&!view.Prologue.RopeFixed)return "救援索尚未固定。";
        if((a=="R0-S03"&&b=="R0-S05"||a=="R0-S05"&&b=="R0-S03")&&(!view.Prologue.RelayEntranceFound||!view.Prologue.ToolsAcquired||!Closed(view,"Q-P02")))return "先完成救援，再调查并识别中继站检修入口。";
        if((a=="R0-S04"&&b=="R0-S05"||a=="R0-S05"&&b=="R0-S04")&&!view.Prologue.RelayBackDoorOpen)return "救援棚后门尚未从中继站内侧打开。";
        if(a=="R0-S02"&&b=="R0-S03"&&!Closed(view,"Q-P01"))return "先将船上人员沿加固路线带到岸边集合处。";
        return null;
    }
    public async ValueTask<CommandResult> EnterPortAsync(string portId)
    {
        var v=await ViewAsync();Require(!v.Q102Ready,"渡轮工作已接续，请使用当前工作服务。");
        var port=Geometry[v.Actor.Scene].Ports.Single(p=>p.Id==portId);Require(v.Actor.Position.Distance(port.Stand)<=2.5,"请先走到出口登记站位。");
        var issue=PortIssue(v,port);if(issue is not null)throw new InvalidOperationException(issue);
        var pair=Geometry[port.TargetScene].Ports.SingleOrDefault(p=>p.Id==port.TargetPort);
        Require(pair is not null&&new NavigationGrid(Geometry[port.TargetScene]).CanStand(pair.Stand),"配对入口不可用，保留原安全位置。");
        var changes=ImmutableArray.Create(BranchOperations.Change(v.Snapshot,"actor","actor",v.Actor with{Scene=port.TargetScene,Position=pair!.Stand,ActionClock=checked(v.Actor.ActionClock+1)}));
        if(v.Actor.Scene=="R0-S04"&&port.TargetScene=="R1-S01")
        {
            Require(People(v) is{Location:"R0-S04",Phase:"registered_shelter"},"接应名单与现场人员位置不符。");
            changes=changes.Add(PeopleAt(v,"R1-S01","awaiting_harbour_registration"));
        }
        return await Commit(v,"campaign_scene_transition",changes);
    }
    public async ValueTask<CommandResult> DiscoverAsync(string materialId)
    {
        var v=await ViewAsync();var material=Package.Materials.SingleOrDefault(m=>m.Id==materialId)??throw new InvalidOperationException("材料未加载。");At(v,material.Spot);
        if(materialId is "QP03-R-RECORDER" or "QP03-R-MAIL")
            Require(materialId=="QP03-R-RECORDER"?v.Prologue.RecorderCollected:v.Prologue.MailbagCollected,"请先合法领取现场原件，不能隔着物件读取未来材料。");
        if(material.Scene=="R0-S05")Require(Closed(v,"Q-P02"),"救援优先，独立记录与导师邮包尚未取得。");
        if(materialId=="QP03-R-PICKUP")Require(Closed(v,"Q-P02")&&v.Prologue.RelayReady,"当前中继尚未收到有效接应回执。");
        if(Acquired(v,materialId))return new(CommitStatus.AlreadyCommitted,null,[]);
        return await Commit(v,"material_acquired",[BranchOperations.Change(v.Snapshot,"material:"+materialId,"material_access",new{acquired=true,version=material.Version,scope=CampaignRules.Scope})],semantic:"material:"+materialId+":acquired");
    }
    public async ValueTask<CampaignMaterial> OpenMaterialAsync(string id)
        =>(await ViewAsync()).Materials.SingleOrDefault(m=>m.Id==id)??throw new InvalidOperationException("只有现场取得的材料可在档案中打开。");
    public async ValueTask<CommandResult> ConfirmTimelineAsync(CoastTimeline timeline)
    {
        var v=await ViewAsync();Require(Acquired(v,"QP01-R-LOG"),"先获取现场船钟抄记；未有依据时不能确认先后。");
        Require(!Closed(v,"Q-P01"),"已结算的救援记录保留原始确认，不因重新打开谜题而改写。");
        return await Commit(v,"confirm_coast_timeline",[P(v,v.Prologue with{Timeline=timeline})],payload:new{timeline,interpretation_mode="explicit_structured_confirmation",language_assessment=(string?)null});
    }
    public async ValueTask<CommandResult> ChooseLandingAsync(RescueLanding landing)
    {
        var v=await ViewAsync();At(v,"R0-S03.winch");Require(Closed(v,"Q-P01")&&!v.Prologue.SurvivorsSheltered,"接应方向只能在本次实际救援准备阶段更改。");
        Require(landing!=RescueLanding.Unselected&&Acquired(v,"QP02-R-TIDE")&&Acquired(v,"QP02-L-CORRECTION"),"需要现场潮位说明与当前更正来源。");
        return await Commit(v,"choose_rescue_receiving_direction",[P(v,v.Prologue with{Landing=landing,WinchRevision=checked(v.Prologue.WinchRevision+1),WinchTestRevision=null})],payload:new{landing,interpretation_mode="explicit_structured_confirmation",listening_evidence="script_access_is_not_listening"});
    }
    private ImmutableArray<BalanceChange> Reward(string id)
    {
        var reward=_catalog.Quests.Single(q=>q.Id==id).Reward.Bundle;
        return [new("experience",reward.Experience),new("marks",reward.Marks),new("parts",reward.Parts)];
    }
    private static ImmutableArray<EntityChange> Resolve(CampaignView view,string id)
    {
        var q=view.Quests.Single(q=>q.Id==id);Require(q.Lifecycle==QuestLifecycle.Active,"任务尚未开启或已经完成。");
        var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(BranchOperations.Change(view.Snapshot,"quest:"+id,"quest",q.Close(QuestOutcome.Completed)));
        int next=CampaignRules.Quests.IndexOf(id)+1;if(next<CampaignRules.Quests.Length)
        {
            var following=view.Quests.Single(q=>q.Id==CampaignRules.Quests[next]);Require(following.Lifecycle==QuestLifecycle.Unaccepted,"下一任务实例状态不一致。");
            changes.Add(BranchOperations.Change(view.Snapshot,"quest:"+following.Id,"quest",following.Accept()));
        }
        return changes.ToImmutable();
    }
    public async ValueTask<CommandResult> ExecuteStationAsync(string action)
    {
        var v=await ViewAsync();var p=v.Prologue;
        string spot=action switch
        {
            "acquire_rescue_tools"=>"R0-S01.tools","reinforce_cabin_path"=>"R0-S01.floor","evacuate_to_shore"=>"R0-S02.landing",
            "isolate_winch" or "seat_winch_cable" or "fix_rescue_rope" or "power_winch" or "test_winch" or "move_survivors_to_shelter"=>"R0-S03.winch",
            "inspect_relay_entrance"=>"R0-S03.relay_entrance","register_survivors"=>"R0-S04.survivor_register","acknowledge_pickup"=>"R0-S04.pickup",
            "isolate_relay" or "seat_relay_lead" or "power_relay" or "test_relay"=>"R0-S05.relay","collect_coast_record"=>"R0-S05.recorder","collect_inspection_mail"=>"R0-S05.mailbag",
            "open_shelter_back_door"=>"R0-S05.back_door","register_harbour_arrival"=>"R1-S01.contact",
            _=>throw new InvalidOperationException("该操作需要独立的明确确认工作区，或尚未实现。")
        };
        At(v,spot);BranchOperations.Safe(v.Snapshot);
        switch(action)
        {
            case "acquire_rescue_tools":
            {
                Require(!p.ToolsAcquired,"本船这份救援包已经取出。");var tools=v.Snapshot.Items.Single(i=>i.Id==ToolId);var rope=v.Snapshot.Items.Single(i=>i.Id==RopeId);
                Require(tools.OwnerKind=="warehouse"&&rope.OwnerKind=="warehouse"&&tools.OwnerId=="R0-S01"&&rope.OwnerId=="R0-S01","救援包实物归属已变化。");
                return await Commit(v,action,[P(v,p with{ToolsAcquired=true})],[new(tools.Id,tools.DefinitionId,"warehouse","R0-S01","player","hero",null),new(rope.Id,rope.DefinitionId,"warehouse","R0-S01","player","hero",null)],semantic:"prologue:rescue-kit:collected");
            }
            case "reinforce_cabin_path":
                Require(p.ToolsAcquired&&Acquired(v,"QP01-R-ROUTE"),"需要现场撤离说明与已取出的基础工具。");
                Require(!p.PathReinforced,"该安全通路已经加固，回访不重新制造事故。");
                return await Commit(v,action,[P(v,p with{PathReinforced=true})]);
            case "evacuate_to_shore":
                Require(p.PathReinforced&&p.ToolsAcquired&&p.Timeline==CoastTimeline.LightLossBeforeDrift&&Acquired(v,"QP01-R-LOG")&&Acquired(v,"QP02-R-TIDE"),"尚未建立有依据的安全离船通路与初步先后关系。");
                Require(People(v) is{Location:"R0-S01",Phase:"awaiting_safe_evacuation"},"本次离船人员的位置不一致。");
                return await Commit(v,action,Resolve(v,"Q-P01").Add(P(v,p with{PeopleOnShore=true})).Add(PeopleAt(v,"R0-S02","protected_shore_assembly")),balances:Reward("Q-P01"),semantic:"Q-P01:resolution",payload:new{people_on_protected_shore=true,chronology="light_loss_before_deviation",responsible_person=(string?)null});
            case "isolate_winch":
                Require(Closed(v,"Q-P01")&&p.ToolsAcquired&&Acquired(v,"QP02-R-WINCH")&&!p.SurvivorsSheltered,"先取得绞盘检修说明并完成离船救援。");
                return await Commit(v,action,[P(v,p with{WinchIsolated=true,WinchPowered=false,WinchRevision=checked(p.WinchRevision+1),WinchTestRevision=null})]);
            case "seat_winch_cable":
                Require(p.WinchIsolated&&!p.WinchPowered&&!p.SurvivorsSheltered,"钢缆调整必须在驱动关闭并隔离时进行。");
                return await Commit(v,action,[P(v,p with{WinchCableSeated=true,WinchRevision=checked(p.WinchRevision+1),WinchTestRevision=null})]);
            case "fix_rescue_rope":
            {
                Require(p.WinchIsolated&&!p.WinchPowered&&!p.RopeFixed,"先关闭并隔离驱动；同一根救援索不能重复安装。");
                var rope=v.Snapshot.Items.Single(i=>i.Id==RopeId);Require(rope.OwnerKind=="player"&&rope.OwnerId=="hero","需要本次救援的唯一实物索具。");
                return await Commit(v,action,[P(v,p with{RopeFixed=true,WinchRevision=checked(p.WinchRevision+1),WinchTestRevision=null})],[new(rope.Id,rope.DefinitionId,"player","hero","installed","COAST-WINCH","rescue_rope")]);
            }
            case "power_winch":
                Require(p.WinchIsolated&&p.WinchCableSeated&&p.RopeFixed&&!p.SurvivorsSheltered,"先完成隔离状态的钢缆与索具准备。");
                return await Commit(v,action,[P(v,p with{WinchIsolated=false,WinchPowered=true,WinchRevision=checked(p.WinchRevision+1),WinchTestRevision=null})]);
            case "test_winch":
                Require(p.WinchPowered&&p.WinchCableSeated&&p.RopeFixed&&p.Landing==RescueLanding.NorthReach&&!p.SurvivorsSheltered,"当前接应方向或索具不符合现场约束；未签发张力检查。");
                return await Commit(v,action,[P(v,p with{WinchTestRevision=p.WinchRevision})]);
            case "move_survivors_to_shelter":
                Require(Closed(v,"Q-P01")&&p.PeopleOnShore&&p.WinchReady&&p.ShoreSafe&&!p.SurvivorsSheltered,"先完成当前配置张力检查、正确接应方向和近岸危险处置。");
                Require(People(v) is{Location:"R0-S02",Phase:"protected_shore_assembly"},"现场接应人员并未在岸边集合处。");
                return await Commit(v,action,[P(v,p with{SurvivorsSheltered=true}),PeopleAt(v,"R0-S04","sheltered")],payload:new{survivors=CampaignRules.Survivors,destination="R0-S04",shore_cleared_by_actual_encounter=true});
            case "register_survivors":
                Require(p.SurvivorsSheltered&&!p.SurvivorsRegistered,"人员尚未实际进入避风棚，或已经登记。");
                Require(People(v) is{Location:"R0-S04",Phase:"sheltered"},"登记必须对应实际进入避风棚的人员。");
                return await Commit(v,action,[P(v,p with{SurvivorsRegistered=true}),PeopleAt(v,"R0-S04","registered_shelter")],payload:new{survivors=CampaignRules.Survivors,player_included=false,no_wall_time_loss=true});
            case "inspect_relay_entrance":
                Require(Closed(v,"Q-P02")&&p.ToolsAcquired,"先完成救援与求援，再追查中继记录。");
                return await Commit(v,action,[P(v,p with{RelayEntranceFound=true})]);
            case "isolate_relay":
                Require(Closed(v,"Q-P02")&&Acquired(v,"QP03-R-RELAY"),"需要合法到达中继站并取得本站检修说明。");
                return await Commit(v,action,[P(v,p with{RelayIsolated=true,RelayPowered=false,RelayRevision=checked(p.RelayRevision+1),RelayTestRevision=null})]);
            case "seat_relay_lead":
                Require(p.RelayIsolated&&!p.RelayPowered,"先关闭并隔离外发设备。");
                return await Commit(v,action,[P(v,p with{RelayLeadSeated=true,RelayRevision=checked(p.RelayRevision+1),RelayTestRevision=null})]);
            case "power_relay":
                Require(p.RelayIsolated&&p.RelayLeadSeated,"先隔离并落实本站接线。");
                return await Commit(v,action,[P(v,p with{RelayIsolated=false,RelayPowered=true,RelayRevision=checked(p.RelayRevision+1),RelayTestRevision=null})]);
            case "test_relay":
                Require(p.RelayLeadSeated&&p.RelayPowered,"当前接线与本地电源不完整。");
                return await Commit(v,action,[P(v,p with{RelayTestRevision=p.RelayRevision})]);
            case "collect_coast_record":
            case "collect_inspection_mail":
            {
                Require(Closed(v,"Q-P02")&&p.RelayReady,"先完成实际救援与当前中继链路检查。");
                bool record=action=="collect_coast_record";Require(record?!p.RecorderCollected:!p.MailbagCollected,"这份原件已领取，档案保留副本。");
                string id=record?"QP03-R-RECORDER":"QP03-R-MAIL";
                return await Commit(v,action,[P(v,record?p with{RecorderCollected=true}:p with{MailbagCollected=true}),BranchOperations.Change(v.Snapshot,"material:"+id,"material_access",new{acquired=true,version="candidate-1",scope=CampaignRules.Scope})],semantic:"material:"+id+":acquired",payload:new{material=id,source_root=record?"coast-independent-recorder":"inspection-post-Mara-letter",current_mara_status="unknown",sender_location_not_inferred=true});
            }
            case "open_shelter_back_door":
                Require(Closed(v,"Q-P02"),"救援完成后可从中继站内侧开闩。");
                return await Commit(v,action,[P(v,p with{RelayBackDoorOpen=true})]);
            case "acknowledge_pickup":
                Require(p.RelayReady&&p.RecorderCollected&&p.MailbagCollected&&Acquired(v,"QP03-R-PICKUP")&&Closed(v,"Q-P02"),"需要当前通信、实际取得的记录与邮包以及港口接应回执。");
                return await Commit(v,action,Resolve(v,"Q-P03").Add(P(v,p with{PickupAcknowledged=true})).Add(BranchOperations.Change(v.Snapshot,"campaign","campaign",v.Campaign with{ProloguePlayed=true})),
                    balances:Reward("Q-P03"),semantic:"Q-P03:resolution",payload:new{actual_rescue_completed=true,independent_record_acquired=true,mentor_letter_acquired=true,harbour_pickup_established=true,mentor_current_status="unknown"});
            case "register_harbour_arrival":
                Require(Closed(v,"Q-P03")&&p.PickupAcknowledged&&Acquired(v,"Q101-R-ARRIVAL")&&!p.HarbourReceived,"先完成实际接应通行并核对接应泊位记录。");
                Require(People(v) is{Location:"R1-S01",Phase:"awaiting_harbour_registration"},"人员尚未实际乘接应船抵达泊位。");
                return await Commit(v,action,[P(v,p with{HarbourReceived=true}),PeopleAt(v,"R1-S01","harbour_received")],payload:new{survivors=CampaignRules.Survivors,received_at="R1-S01",civil_ferry_not_departed=true,patrol_boat_not_transferred=true});
        }
        throw new InvalidOperationException("操作尚未实现。");
    }
    private async ValueTask<DraftStamp> CurrentDraft(string task)
    {var d=await _repository.GetDraftAsync(task);return new(task,d?.Id,d?.Revision??0,d?.Hash);}
    public async ValueTask<CommandResult> ConfirmRescueMeaningAsync(RescueMessageMeaning meaning,bool structuredAssistance)
    {
        var v=await ViewAsync();Require(!Closed(v,"Q-P02")&&Closed(v,"Q-P01"),"求援意图只适用于当前救援任务。");
        var draft=await _repository.GetDraftAsync(RescueDraft);Require(structuredAssistance||draft is{Deleted:false}&&!string.IsNullOrWhiteSpace(draft.Body),"空白正文须显式采用结构化辅助；不能记为独立写作。");
        var old=BranchOperations.Read<CampaignMeaning<RescueMessageMeaning>>(v.Snapshot,"proposal:Q-P02")!;
        return await Commit(v,"confirm_rescue_message_meaning",[BranchOperations.Change(v.Snapshot,"proposal:Q-P02","proposal",new CampaignMeaning<RescueMessageMeaning>(checked(old.Version+1),true,draft?.Hash,structuredAssistance,meaning))],draft:await CurrentDraft(RescueDraft));
    }
    public async ValueTask<CommandResult> ConfirmHarbourMeaningAsync(HarbourRequestMeaning meaning,bool structuredAssistance)
    {
        var v=await ViewAsync();Require(Closed(v,"Q-P03")&&!Closed(v,"Q-101"),"该申请只适用于接应后的当前港口委托。");
        var draft=await _repository.GetDraftAsync(HarbourDraft);Require(structuredAssistance||draft is{Deleted:false}&&!string.IsNullOrWhiteSpace(draft.Body),"空白正文须显式采用结构化辅助；不能记为独立写作。");
        var old=BranchOperations.Read<CampaignMeaning<HarbourRequestMeaning>>(v.Snapshot,"proposal:Q-101")!;
        return await Commit(v,"confirm_harbour_request_meaning",[BranchOperations.Change(v.Snapshot,"proposal:Q-101","proposal",new CampaignMeaning<HarbourRequestMeaning>(checked(old.Version+1),true,draft?.Hash,structuredAssistance,meaning))],draft:await CurrentDraft(HarbourDraft));
    }
    public async ValueTask<CampaignSubmissionPreview> PreviewRescueMessageAsync()
    {
        var v=await ViewAsync();var draft=await CurrentDraft(RescueDraft);var proposal=BranchOperations.Read<CampaignMeaning<RescueMessageMeaning>>(v.Snapshot,"proposal:Q-P02")!;
        var issues=ImmutableArray.CreateBuilder<RuleIssue>();void Check(bool okay,string code,string message){if(!okay)issues.Add(new(code,"Q-P02",message));}
        Check(v.Actor.Scene=="R0-S04"&&v.Actor.Position.Distance(Geometry["R0-S04"].Spots.Single(s=>s.Id=="R0-S04.message_desk").Stand)<=2.5,"not_at_submit_spot","请走到避风棚求援发送台。");
        Check(v.Prologue.SurvivorsSheltered&&v.Prologue.SurvivorsRegistered&&v.Prologue.WinchReady&&v.Prologue.ShoreSafe,"rescue_not_ready","人员、当前索具检查或近岸安全尚未落实。");
        Check(People(v) is{Location:"R0-S04",Phase:"registered_shelter"},"people_not_registered","求援名单必须对应实际登记的棚内人员。");
        Check(v.Quests.Single(q=>q.Id=="Q-P02").Lifecycle==QuestLifecycle.Active,"quest_not_active","当前求援任务已结束或未开启。");
        Check(proposal.Confirmed&&proposal.DraftHash==draft.Hash,"proposal_changed","正文修改后须重新确认求援意图。");
        Check(proposal.Meaning is{Location:"shelter",People:CampaignRules.Survivors,Need:"pickup",ApproachConstraint:"north_reach_only"},"meaning_not_admissible","核对位置、接应人数、需求与当前更正后的进路。");
        var command=BranchOperations.Plan(_repository,v.Snapshot,"Q-P02:resolution","rescue_message_sent",ResolveIfActive(v,"Q-P02"),["actor","campaign","prologue","coast:survivors","proposal:Q-P02"],
            new{scope=CampaignRules.Scope,meaning=proposal.Meaning,structured_assistance=proposal.StructuredAssistance,interpretation_mode="explicit_typed_confirmation",language_assessment=(string?)null,survivors=CampaignRules.Survivors},balances:Reward("Q-P02"),draft:draft);
        return Preview(command,issues.ToImmutable(),draft,proposal.StructuredAssistance);
    }
    private static ImmutableArray<EntityChange> ResolveIfActive(CampaignView v,string q)=>v.Quests.Single(x=>x.Id==q).Lifecycle==QuestLifecycle.Active?Resolve(v,q):[];
    public async ValueTask<CampaignSubmissionPreview> PreviewHarbourRequestAsync()
    {
        var v=await ViewAsync();var draft=await CurrentDraft(HarbourDraft);var proposal=BranchOperations.Read<CampaignMeaning<HarbourRequestMeaning>>(v.Snapshot,"proposal:Q-101")!;
        var issues=ImmutableArray.CreateBuilder<RuleIssue>();void Check(bool okay,string code,string message){if(!okay)issues.Add(new(code,"Q-101",message));}
        Check(v.Actor.Scene=="R1-S03"&&v.Actor.Position.Distance(Geometry["R1-S03"].Spots.Single(s=>s.Id=="R1-S03.havel").Stand)<=2.5,"not_at_submit_spot","请走到哈文公共咨询台申请实际委托。");
        Check(v.Prologue.HarbourReceived&&Closed(v,"Q-P03"),"arrival_not_received","接应通行与实际到港登记尚未成立。");
        Check(People(v) is{Location:"R1-S01",Phase:"harbour_received"},"people_not_received","接应人员须真实到港并登记。");
        Check(new[]{"Q101-R-ARRIVAL","Q101-R-CREDENTIAL","Q101-R-CLOSURE","Q101-R-EXCEPTION"}.All(m=>Acquired(v,m)),"sources_missing","核对接应、任职凭证、现行封港令与例外范围。");
        Check(v.Quests.Single(q=>q.Id=="Q-101").Lifecycle==QuestLifecycle.Active&&!v.Campaign.Q102Ready,"quest_not_active","该委托已经签发或当前任务未开启。");
        Check(proposal.Confirmed&&proposal.DraftHash==draft.Hash,"proposal_changed","正文修改后须重新确认委托请求。");
        Check(proposal.Meaning is{Purpose:"evacuation_assistance",WorkScope:"civil_ferry_and_local_workshop",PowerBoundary:"main_generator_excluded"},"meaning_not_admissible","请求必须符合当前撤离用途、设备范围与主发电机边界。");
        Check(!v.Snapshot.Items.Any(i=>i.Id is Q102Service.BracketId or Q102Service.BatteryId)&&BranchOperations.Entity(v.Snapshot,"permission:Q-102") is null,"handoff_conflict","已有渡轮物件或授权，不能覆盖它们建立第二个身份。");
        var changes=ResolveIfActive(v,"Q-101").AddRange(new[]{
            BranchOperations.Change(v.Snapshot,"campaign","campaign",v.Campaign with{Q102Ready=true}),
            BranchOperations.Change(v.Snapshot,"workshop","device",new WorkshopState(WorkshopMode.Active,false)),
            BranchOperations.Change(v.Snapshot,"ferry","device",new FerryState(true,false,false,true,0,null,false)),
            BranchOperations.Change(v.Snapshot,"quest:Q-102","quest",new QuestState("Q-102",Guid.NewGuid(),QuestLifecycle.Unaccepted,null)),
            BranchOperations.Change(v.Snapshot,"proposal:Q-102","proposal",new ProposalState(0,false,null,false,null)),
            BranchOperations.Change(v.Snapshot,"permission:Q-102","permission",new{allowed=true,source="Q-101:resolution",authority_actor="N-HAVEL",scope="civil_ferry_and_local_workshop",main_generator_excluded=true})});
        var command=BranchOperations.Plan(_repository,v.Snapshot,"Q-101:resolution","temporary_maintenance_commission_issued",changes,
            ["actor","campaign","prologue","coast:survivors","proposal:Q-101",..new[]{"Q101-R-ARRIVAL","Q101-R-CREDENTIAL","Q101-R-CLOSURE","Q101-R-EXCEPTION"}.Select(m=>"material:"+m)],
            new{scope=CampaignRules.Scope,authority_actor="N-HAVEL",meaning=proposal.Meaning,structured_assistance=proposal.StructuredAssistance,interpretation_mode="explicit_typed_confirmation",permission_source="Q-101:resolution",q102_entry_level=3,calibration_grade=0,glimmer_transferred=false},
            [new(Q102Service.BracketId,"Q102-BRACKET","new","","warehouse","R1-S06",null),new(Q102Service.BatteryId,"Q102-BATTERY","new","","vessel","CIVIL-FERRY",null)],Reward("Q-101"),draft:draft);
        return Preview(command,issues.ToImmutable(),draft,proposal.StructuredAssistance);
    }
    private CampaignSubmissionPreview Preview(CommitPlanCommand command,ImmutableArray<RuleIssue> issues,DraftStamp draft,bool assistance)
    {
        Guid token=Guid.NewGuid();bool ready=issues.IsEmpty;if(ready)_previews[token]=command;
        return new(token,ready,issues,draft.TaskId,draft.Hash,assistance);
    }
    public async ValueTask<CommandResult> ConfirmSubmissionAsync(Guid token)
    {
        if(!_previews.TryGetValue(token,out var command))return new(CommitStatus.Rejected,null,[new("preview_closed","campaign","未就绪或已关闭的预览不能执行。")]);
        // The immutable command stays available for an outcome-unknown retry. Expected draft/load/world stamps reject stale input.
        return await _repository.ExecuteAsync(command);
    }
    public void CloseSubmissionPreview(Guid token)=>_previews.Remove(token);

    public async ValueTask<CommandResult> BeginShoreEncounterAsync()
    {
        var v=await ViewAsync();At(v,"R0-S02.creature");Require(Closed(v,"Q-P01")&&!v.Prologue.ShoreSafe,"实际离船后才处置接应路径威胁；已清除不生成奖励战斗。");
        Require(v.ShoreBattle is null||v.ShoreBattle.Battle.Outcome==CombatOutcome.Withdrawn,"继续当前遭遇，不能开菜单回满或绕过战败恢复。");
        var party=new PartyService(_repository,_catalog);var hero=party.Inspect(v.Snapshot);var battle=new BattleSnapshot{Definition=Package.ShoreEncounter.Id,Instance=v.ShoreBattle?.Battle.Instance??Guid.NewGuid(),Attempt=Guid.NewGuid(),EnemyDamageBp=10000};
        BattleUnit Ally(string id,string name,int slot,Mist.Core.Progression.Attributes attributes,string weapon,string armor,string[] learned,string[] active)
        {
            var stats=Mist.Core.Progression.ProgressionRules.Derive(hero.Level,hero.Grade,attributes,party.Definitions.Items[weapon],party.Definitions.Items[armor]);
            return new(){Id=id,Name=name,Side=CombatSide.Ally,Slot=slot,Level=hero.Level,MaxHp=stats.MaxHp,Hp=stats.MaxHp,MaxFocus=stats.MaxFocus,Focus=stats.MaxFocus,AttackUnits=stats.AttackUnits,TechUnits=stats.TechUnits,ArmorBp=stats.ArmorBp,GuardBase=stats.GuardBp,ExposureBonus=stats.ExposureBp,Strength=attributes.Strength,Initiative=stats.Initiative,Weapon=weapon,Armor=armor,Scaling=party.Definitions.Items[weapon].Scaling!.Value,Learned=learned,Active=active};
        }
        battle.Units.Add(Ally("hero","主角",0,hero.Build.Attributes,hero.Weapon,hero.Armor,hero.Build.Learned.ToArray(),hero.Build.Active.ToArray()));
        battle.Units.Add(Ally("CP-LINA","莉娜（此次救援协作）",2,Mist.Core.Progression.ProgressionRules.AllocateTemplate(hero.Level,[2,1,4,3,1]),"WP-03","AR-01",[],["CP-LINA-1","CP-LINA-2"]));
        var enemy=Package.ShoreEncounter;battle.Units.Add(new(){Id="shore_creature",Name="近岸生物",Side=CombatSide.Enemy,Slot=0,MaxHp=enemy.Hp,Hp=enemy.Hp,ArmorBp=enemy.ArmorBp,PressureThreshold=enemy.PressureThreshold,Initiative=enemy.Initiative,Pattern=enemy.Pattern.Select(p=>new EnemyPattern(p.Name,p.Damage,p.Target switch{"unit"=>TargetLock.Unit,"column"=>TargetLock.Column,"all"=>TargetLock.All,_=>throw new InvalidDataException()},true,p.Interruptible)).ToArray()});
        var entry=StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(battle));var engine=new BattleEngine(_battleDefinitions,battle);engine.Start();
        var command=BranchOperations.Plan(_repository,v.Snapshot,"shore:"+battle.Instance+":attempt:"+battle.Attempt+":entry","begin_shore_encounter",[BranchOperations.Change(v.Snapshot,ShoreCombat,"combat",new ShoreEncounterState(engine.Snapshot(),entry,v.Actor))],["actor","prologue","hero-build"],new{temporary_rescue_cooperation=true,formal_lina_recruitment=false,free_auxiliaries_not_yet_enabled=true});
        return await _repository.ExecuteAsync(command);
    }
    public async ValueTask<ShoreActionPreview> PreviewShoreActionAsync(BattleRequest request)
    {
        var v=await ViewAsync();var old=v.ShoreBattle??throw new InvalidOperationException("当前没有近岸遭遇。");var engine=new BattleEngine(_battleDefinitions,old.Battle);var preview=engine.Preview(request);
        if(!preview.Legal)return new(Guid.Empty,preview);engine.Apply(request);var battle=engine.Snapshot();bool settled=old.ResultApplied;
        var changes=ImmutableArray.CreateBuilder<EntityChange>();if(battle.Outcome==CombatOutcome.Controlled&&!settled){settled=true;changes.Add(P(v,v.Prologue with{ShoreSafe=true}));}
        changes.Add(BranchOperations.Change(v.Snapshot,ShoreCombat,"combat",old with{Battle=battle,ResultApplied=settled}));Guid token=Guid.NewGuid();
        _battlePreviews[token]=BranchOperations.Plan(_repository,v.Snapshot,"shore:"+battle.Instance+":attempt:"+battle.Attempt+":action:"+request.Command.Value,"shore_combat_action",changes.ToImmutable(),["actor","prologue"],new{request,outcome=battle.Outcome,experience_awarded=0},request:request.Command);
        return new(token,preview);
    }
    public ValueTask<CommandResult> ConfirmShoreActionAsync(Guid token)
        =>_battlePreviews.TryGetValue(token,out var command)?_repository.ExecuteAsync(command):ValueTask.FromResult(new CommandResult(CommitStatus.Rejected,null,[new("preview_closed",ShoreCombat,"近岸行动预览未就绪或已关闭。") ]));
    public void CloseShoreActionPreview(Guid token)=>_battlePreviews.Remove(token);
    public async ValueTask<CommandResult> WithdrawShoreAsync()
    {
        var v=await ViewAsync();var old=v.ShoreBattle??throw new InvalidOperationException("没有近岸遭遇入口。");Require(!old.ResultApplied,"已制止的危险不能通过撤回重新生成。");
        var battle=StrictJson.Deserialize<BattleSnapshot>(StrictJson.Serialize(old.Entry));battle.Outcome=CombatOutcome.Withdrawn;battle.CurrentActor=null;_battlePreviews.Clear();
        return await _repository.ExecuteAsync(BranchOperations.Plan(_repository,v.Snapshot,"shore:"+old.Battle.Attempt+":withdraw","shore_withdraw_to_entry",
            [BranchOperations.Change(v.Snapshot,ShoreCombat,"combat",old with{Battle=battle}),BranchOperations.Change(v.Snapshot,"actor","actor",old.ReturnActor)],["prologue"],new{materials_and_drafts_retained=true,experience_awarded=0}));
    }
}

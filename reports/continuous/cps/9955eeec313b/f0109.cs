using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.ChapterTwo;
using Mist.Core.Contracts;
using Mist.Core.Devices;
using Mist.Core.Quests;

namespace Mist.Application.ChapterTwo;

public sealed record ChapterGate(string Name,bool Allowed,string ContentVersion);
public sealed partial class ChapterTwoService
{
    private bool Sources(BranchSnapshot s,params string[] ids)=>ids.All(id=>Package.Materials.SingleOrDefault(m=>m.Id==id) is{} m&&Acquired(s,m)&&Accessible(s,m));
    public async ValueTask<CommandResult> ExecuteStationAsync(string action)
    {
        var s=await _repository.SnapshotAsync();Safe(s);var st=State(s);var station=Package.Stations.SingleOrDefault(x=>x.Action==action)??throw new InvalidOperationException("操作未登记。");At(s,station.Spot);
        var changes=ImmutableArray.CreateBuilder<EntityChange>();var items=ImmutableArray.CreateBuilder<ItemChange>();ChapterTwoState next=st;string? key=null;
        void Need(bool okay,string why)=>Require(okay,why);
        switch(action)
        {
            case "register_landing":ActualSeaArrival(s);Need(Sources(s,"R2-Q201-R-LANDING"),"先查看接驳条款。");next=st with{Landing=true};key="R2:landing";break;
            case "request_local_reception":Need(st.Landing&&Sources(s,"R2-Q201-R-REQUEST"),"实际登陆与地方请求未成立。");next=st with{Reception=true};key="R2:local_request";break;
            case "inspect_residence_a":case "inspect_residence_b":case "inspect_residence_c":
                Need(st.Reception,"先取得本次自愿地方核查范围。");string room=action[^1..].ToUpperInvariant();var group=BranchOperations.Read<LocalResidentGroup>(s,"R2:residents:"+room)??throw new InvalidOperationException("实际住民组不存在。");
                Need(group.Scene==Actor(s).Scene&&group.Spot==station.Spot&&group.LocalCheckConsent&&group.People.Length>0,"当事人没有在该公开门廊同意本次核查。");
                changes.Add(BranchOperations.Change(s,"R2:observation:"+room,"residence_observation",new ResidenceObservation(room,group.People,"r2-porch-firsthand-"+room,Actor(s).ActionClock,"current_local_residence_check")));
                next=room switch{"A"=>st with{VisitA=true},"B"=>st with{VisitB=true},"C"=>st with{VisitC=true},_=>throw new InvalidOperationException()};key="R2:porch:"+room;break;
            case "collect_local_maintenance_items":
                Need(st.InspectionScope&&Sources(s,"R2-Q203-R-WATER"),"本地检修委托与料柜用途未核对。");
                foreach(var id in new[]{ClampId,ConnectorId}){var item=s.Items.Single(i=>i.Id==id);if(item.OwnerKind=="player"&&item.OwnerId=="hero")continue;Need(item.OwnerKind=="warehouse"&&item.OwnerId=="R2-S05","唯一地方件已在另一真实位置。");items.Add(new(item.Id,item.DefinitionId,item.OwnerKind,item.OwnerId,"player","hero",null));}key="R2:local_stock_receipt";break;
            case "inspect_stair_boundary":Need(st.InspectionScope&&HasResult(s,"ENC-R2-01"),"需要独立根道处置结果与地方检修范围。");next=st with{StairSafe=true};key="R2:stair_safe";break;
            case "isolate_conveyor":Need(st.InspectionScope,"地方检修范围未取得。");next=st with{ConveyorIsolated=true,ConveyorRevision=checked(st.ConveyorRevision+1),UpperCheckRevision=null};break;
            case "open_conveyor_guard":Need(st.ConveyorIsolated&&!st.GuardSecured,"输送器必须隔离且护罩尚未固定。");next=st with{GuardOpen=true,ConveyorRevision=checked(st.ConveyorRevision+1),UpperCheckRevision=null};break;
            case "release_conveyor_jam":Need(st.ConveyorIsolated&&st.GuardOpen,"先隔离并实际打开护罩。");next=st with{JamReleased=true,ConveyorRevision=checked(st.ConveyorRevision+1),UpperCheckRevision=null};break;
            case "secure_conveyor_guard":Need(st.ConveyorIsolated&&st.GuardOpen&&st.JamReleased,"卡滞处置与打开护罩未成立。");next=st with{GuardOpen=false,GuardSecured=true,ConveyorRevision=checked(st.ConveyorRevision+1),UpperCheckRevision=null};break;
            case "inspect_conveyor_stopped":
                Need(st.ConveyorIsolated&&st.JamReleased&&st.GuardSecured,"手动处置四步与当前零能检查未成立。");
                var result=new ChapterEncounterResult("ENC-R2-02",true,Guid.NewGuid(),Guid.NewGuid(),"manual_guard_jam_zero_motion",st.ConveyorRevision);changes.Add(BranchOperations.Change(s,"R2:result:ENC-R2-02","chapter_encounter_result",result));
                return await Commit(s,"chapter_two_encounter_result",changes.ToImmutable(),result,"R2:ENC-R2-02:result");
            case "set_external_clamp":
                Need(st.ConveyorIsolated,"先隔离输送器。");var clamp=s.Items.Single(i=>i.Id==ClampId);Need(clamp.OwnerKind=="player"&&clamp.OwnerId=="hero"||clamp.OwnerKind=="installed"&&clamp.OwnerId=="R2-UPPER-CLAMP","实际夹具未领取。");if(clamp.OwnerKind=="player")items.Add(new(clamp.Id,clamp.DefinitionId,clamp.OwnerKind,clamp.OwnerId,"installed","R2-UPPER-CLAMP",null));next=st with{ClampFixed=true,ConveyorRevision=checked(st.ConveyorRevision+1),UpperCheckRevision=null};break;
            case "drain_upper_floor":Need(st.ConveyorIsolated,"排水前先隔离。");next=st with{Drained=true,ConveyorRevision=checked(st.ConveyorRevision+1),UpperCheckRevision=null};break;
            case "inspect_upper_route":Need(st.ConveyorIsolated&&st.GuardSecured&&st.ClampFixed&&st.Drained&&HasResult(s,"ENC-R2-02"),"当前停转、夹具、排水或独立输送器处置缺失。");next=st with{UpperCheckRevision=st.ConveyorRevision};break;
            case "inspect_branch_a":Need(st.UpperSafe,"当前上层路线不安全。");next=st with{BranchASeen=true};break;
            case "inspect_branch_b":Need(st.UpperSafe,"当前上层路线不安全。");next=st with{BranchBSeen=true};break;
            case "isolate_local_reserve":next=st with{ReserveIsolated=true,ReservePowered=false,ReserveRevision=checked(st.ReserveRevision+1),TestRevision=null,KitchenFeed=false};break;
            case "seat_local_connector":
                Need(st.ReserveIsolated&&!st.ReservePowered,"须隔离后座接。");var connector=s.Items.Single(i=>i.Id==ConnectorId);Need(connector.OwnerKind=="player"&&connector.OwnerId=="hero"||connector.OwnerKind=="installed"&&connector.OwnerId=="R2-LOCAL-RESERVE","实际唯一连接头未领取。");if(connector.OwnerKind=="player")items.Add(new(connector.Id,connector.DefinitionId,connector.OwnerKind,connector.OwnerId,"installed","R2-LOCAL-RESERVE",null));next=st with{ConnectorSeated=true,ReserveRevision=checked(st.ReserveRevision+1),TestRevision=null,KitchenFeed=false};break;
            case "power_local_reserve":Need(st.ConnectorSeated&&HasResult(s,"ENC-R2-03"),"连接头或实际核心安全目标尚未成立。");next=st with{ReserveIsolated=false,ReservePowered=true,ReserveRevision=checked(st.ReserveRevision+1),TestRevision=null,KitchenFeed=false};break;
            case "test_local_configuration":Need(st.ReservePowered&&st.ConnectorSeated&&st.UpperSafe&&HasResult(s,"ENC-R2-03")&&st.Load is LocalLoad.Kitchen or LocalLoad.Wash or LocalLoad.Alternating,"实际试验配置缺失或请求超载/塔站出口。");next=st with{TestRevision=st.ReserveRevision,KitchenFeed=false};break;
            case "open_inner_latch":Need(st.UpperSafe,"实际内侧路线不安全。");next=st with{InnerLatch=true};key="R2:inner_latch_open";break;
            case "inspect_kitchen_feed":Need(st.LocalStable,"当前有效试验尚未包含厨房回路。");next=st with{KitchenFeed=true};break;
            case "request_tower_work_permit":Need(st.LocalStable&&st.KitchenFeed,"本地稳定与实际厨房验收未成立。");next=st with{TowerPermit=true};key="R2:tower_work_permit";break;
            case "inspect_tower_duty":Need(st.TowerPermit&&st.LocalStable&&st.KitchenFeed,"当前塔站工作许可或地方供给无效。");next=st with{TowerDuty=true};break;
            case "inspect_tower_isolation":Need(st.TowerPermit&&Sources(s,"R2-Q203-R-TOWER"),"先核对实际塔站查验范围。");next=st with{TowerIsolation=true};break;
            case "inspect_tower_return_route":
                Need(st.TowerPermit&&st.TowerIsolation,"塔站当前隔离与许可未核查。");var geo=CurrentGeometry(s);var a=Actor(s);var port=geo[a.Scene].Ports.Single(p=>p.TargetScene=="R2-S02");Need(new Mist.Core.Exploration.NavigationGrid(geo[a.Scene]).FindPath(a.Position,port.Stand,true).Length>0&&new Mist.Core.Exploration.NavigationGrid(geo[port.TargetScene]).CanStand(geo[port.TargetScene].Ports.Single(p=>p.Id==port.TargetPort).Stand,true),"实际返程几何不可达。");next=st with{TowerReturn=true};break;
            case "record_tower_future_requirements":Need(st.TowerDuty&&st.TowerIsolation&&st.TowerReturn,"本次现场检查尚未完成。");changes.Add(BranchOperations.Change(s,"R2:tower_future_requirements","work_requirement",new{tower="TWR-R2",base_isolator=1,local_backup=1,base_provision=2,operator_posts=2,crew_capacity=1,delivered=false,authority_granted=false}));key="R2:tower_future_requirements";break;
            case "attend_powered_dinner":Need(st.LocalStable&&st.KitchenFeed,"实际厨房供给尚未稳定。");key="R2:powered_dinner";break;
            default:throw new InvalidOperationException("此操作需要明确的typed参数、稿件预览或共享遭遇接口；不能用无参数按钮推定答案。");
        }
        if(key is not null&&BranchOperations.Receipt(s,key))return new(CommitStatus.AlreadyCommitted,null,[]);if(next!=st)changes.Add(Change(s,next));
        return await Commit(s,"chapter_two_station_action",changes.ToImmutable(),new{action,actual_stand=station.Spot,experience_awarded=0},key,items.ToImmutable());
    }
    public async ValueTask<CommandResult> PerformConveyorStepAsync(string action)
    {
        Require(action is "open_conveyor_guard" or "release_conveyor_jam" or "secure_conveyor_guard" or "inspect_conveyor_stopped","输送器步骤未登记。");
        // These explicit substeps all use the same physical conveyor stand; no content-provided writes.
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R2-S06.conveyor");var st=State(s);Require(st.InspectionScope&&st.ConveyorIsolated,"当前地方范围或隔离无效。");
        if(HasResult(s,"ENC-R2-02"))return new(CommitStatus.AlreadyCommitted,null,[]);
        ChapterTwoState next=action switch
        {"open_conveyor_guard"when !st.GuardSecured=>st with{GuardOpen=true},"release_conveyor_jam"when st.GuardOpen=>st with{JamReleased=true},"secure_conveyor_guard"when st.GuardOpen&&st.JamReleased=>st with{GuardOpen=false,GuardSecured=true},"inspect_conveyor_stopped"when st.JamReleased&&st.GuardSecured=>st,_=>throw new InvalidOperationException("上一步实际护罩/卡滞动作缺失。")};
        if(action=="inspect_conveyor_stopped")
        {var r=new ChapterEncounterResult("ENC-R2-02",true,Guid.NewGuid(),Guid.NewGuid(),"manual_guard_jam_zero_motion",st.ConveyorRevision);return await Commit(s,"chapter_two_encounter_result",[BranchOperations.Change(s,"R2:result:ENC-R2-02","chapter_encounter_result",r)],r,"R2:ENC-R2-02:result");}
        next=next with{ConveyorRevision=checked(st.ConveyorRevision+1),UpperCheckRevision=null};return await Commit(s,"chapter_two_conveyor_step",[Change(s,next)],new{action,configuration=next.ConveyorRevision});
    }
    public async ValueTask<CommandResult> SetLocalLoadAsync(LocalLoad load)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R2-S07.selector");var st=State(s);Require(st.ReserveIsolated&&!st.ReservePowered,"改负荷档位须先隔离备用源。");Require(Enum.IsDefined(load)&&load!=LocalLoad.Unselected,"请选择明确档位。");
        return await Commit(s,"chapter_two_selector_changed",[Change(s,st with{Load=load,ReserveRevision=checked(st.ReserveRevision+1),TestRevision=null,KitchenFeed=false})],new{load,proposal_is_not_test=true});
    }
    public async ValueTask<CommandResult> ConfirmM12Async(string authorisation,string arrival,string copying,bool copyIsIndependent,string centralKnowledge,string lateCopyingIntent)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R2-S04.scope");Require(Sources(s,"PZTIME-R-ORDER","PZTIME-R-ARRIVAL"),"两篇地方原始来源未取得。");
        Require(authorisation=="before_14:10_exact_minute_unknown"&&arrival=="14:40"&&copying=="16:05"&&!copyIsIndependent&&centralKnowledge=="unknown"&&lateCopyingIntent=="unknown","相对顺序或未知项未受材料支持；准确授权分钟不能猜补。");
        return await Commit(s,"chapter_two_m12_order_confirmed",[Change(s,State(s) with{M12Verified=true})],new{authorisation,arrival,copying,copyIsIndependent,centralKnowledge,lateCopyingIntent},"R2:m12_order");
    }
    public async ValueTask<CommandResult> RequestRecordScopeAsync(string record,string version,ImmutableArray<string> fields,string recipient,string purpose)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R2-S04.scope");var st=State(s);Require(st.BasicArchive&&Sources(s,"R2-Q202-R-ACCESS"),"基础身份与访问条款未取得。");
        string[] allowed=record switch{"TR-D1-7"=>["transfer_id","identity_link","dated_condition","destination_code","version_source"],"TS-18"=>["signature_reference","version_source"],_=>throw new InvalidOperationException("该地方记录未登记。")};
        Require(version=="1"&&recipient=="patrol_technician"&&purpose==(record=="TR-D1-7"?"verify_transfer":"verify_historical_scope")&&!fields.IsDefaultOrEmpty&&fields.Distinct().Count()==fields.Length&&fields.All(allowed.Contains),"请求版本、字段、接收方或用途超出地方保管范围。");
        if(record=="TS-18")Require(Closed(s,"Q-202")&&Closed(s,"Q-203"),"历史页须在本章核查与安全工作实际完成后开放。");
        var scope=new ChapterRecordScope(record,version,fields,recipient,purpose,false,false,"N-JUNE");return await Commit(s,"chapter_two_record_scope_issued",[Change(s,record=="TR-D1-7"?st with{ControlledReview=true}:st with{HistoricalReview=true}),BranchOperations.Change(s,"R2:scope:"+record,"record_scope",scope)],scope);
    }
    public async ValueTask<CommandResult> VerifyD1ChainAsync(string transfer,string timeScope,string currentStatus)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R2-S04.d1_handover");Require(State(s).ControlledReview&&Sources(s,"R2-Q202-R-SENDING","R2-Q202-R-D1"),"两端受控来源和具体许可未实际取得。");
        var scope=BranchOperations.Read<ChapterRecordScope>(s,"R2:scope:TR-D1-7")!;Require(new[]{"transfer_id","identity_link","dated_condition","destination_code","version_source"}.All(scope.Fields.Contains)&&transfer=="TR-D1-7"&&timeScope=="D-1_only"&&currentStatus=="unknown","交接身份/日期/范围尚未核对，不得写成当前存活。");
        return await Commit(s,"chapter_two_d1_chain_verified",[Change(s,State(s) with{D1Verified=true})],new{transfer,time_scope=timeScope,current_mara_status=currentStatus,source_roots=new[]{"r2-d1-sending-custody-original","r2-d1-receiving-custody-original"}},"R2:d1_chain");
    }
}

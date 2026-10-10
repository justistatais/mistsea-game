using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Core.ChapterThree;
using Mist.Core.Contracts;
using Mist.Core.Exploration;
using Mist.Core.ChapterTwo;

namespace Mist.Application.ChapterThree;

public sealed partial class ChapterThreeService
{
    private void StationAt(BranchSnapshot s,string action)=>At(s,Package.Stations.Single(x=>x.Action==action).Spot);
    public async ValueTask<CommandResult> ExecuteStationAsync(string action)
    {
        var s=await _repository.SnapshotAsync();Safe(s);StationAt(s,action);var st=State(s);var next=st;var changes=ImmutableArray.CreateBuilder<EntityChange>();var items=ImmutableArray.CreateBuilder<ItemChange>();string? key=null;
        switch(action)
        {
            case "register_landing":ActualSeaArrival(s);Require(Sources(s,"R3-Q301-R-LANDING"),"先实际查阅登陆条款。");next=st with{Landing=true};key="R3:landing";break;
            case "request_local_work":Require(st.Landing&&Sources(s,"R3-Q301-R-COMMISSION"),"实际登陆与MC-2地方委托条款未核对。");next=st with{Commission=true,EntryInstructions=true};key="R3:local_work";break;
            case "inspect_households":Require(st.Commission&&Sources(s,"R3-Q301-R-BASIC"),"地方核查范围与基本保障要求未取得。");next=st with{Households=true};key="R3:households";break;
            case "receive_basic_supplies":
                Require(st.Commission&&st.Households&&Sources(s,"R3-Q301-R-BASIC"),"基本安置的实际对象、库存与地方用途尚未核对。");
                foreach(var entry in new[]{("warm_meal",12),("first_aid",3),("wage_shift",1)})Require(Enumerable.Range(1,entry.Item2).All(n=>s.Items.Any(i=>i.Id=="R3-local:"+entry.Item1+":"+n)),"基本保障的实际十二餐、三急救包或一班工资凭据不完整。");
                foreach(var i in s.Items.Where(i=>i.Id.StartsWith("R3-local:",StringComparison.Ordinal)&&new[]{"warm_meal","first_aid","wage_shift"}.Any(k=>i.Id.StartsWith("R3-local:"+k+":",StringComparison.Ordinal)))){if(i.OwnerKind=="installed"&&i.OwnerId=="R3-S02:shelter")continue;Require(i.OwnerKind=="warehouse"&&i.OwnerId=="R3-S08","基本救援份额已被挪用。");items.Add(new(i.Id,i.DefinitionId,i.OwnerKind,i.OwnerId,"installed","R3-S02:shelter",null));}
                var kit=s.Items.Single(i=>i.Id=="R3-local:safety_kit:1");if(kit.OwnerKind=="warehouse"&&kit.OwnerId=="R3-S08")items.Add(new(kit.Id,kit.DefinitionId,kit.OwnerKind,kit.OwnerId,"installed","R3:patrol_safety_kit",null));else Require(kit.OwnerKind=="installed"&&kit.OwnerId=="R3:patrol_safety_kit","基本防护套件不在本次救援任务。");next=st with{BasicDelivered=true,BasicGear=true};key="R3:basic_delivery";break;
            case "inspect_ladder":Require(st.Commission&&st.BasicGear&&Sources(s,"R3-Q301-R-LADDER"),"独立梯道规程与实际防护套件未取得。");var g=CurrentGeometry(s);var a=Actor(s);var p=g[a.Scene].Ports.Single(x=>x.TargetScene=="R3-S05");Require(new NavigationGrid(g[a.Scene]).FindPath(a.Position,p.Stand,true).Length>0,"实际梯道入口不可达。");next=st with{LadderSafe=true};key="R3:independent_ladder";break;
            case "inspect_lift":Require(st.Commission&&Sources(s,"R3-Q301-R-SHIFT"),"先核对升降机负荷与当班边界。");next=st with{LiftInspected=true};break;
            case "inspect_worker_signal":Require(st.LadderSafe&&HasResult(s,"ENC-R3-01")&&Sources(s,"R3-Q301-R-RAIL"),"当前轨道处置或独立返程路线未成立。");next=st with{SignalLocated=true};break;
            case "isolate_pump":Require(st.Commission&&Sources(s,"R3-Q301-R-PUMP"),"先核对实际泵房隔离与排水规则。");next=st with{PumpIsolated=true,PumpRevision=checked(st.PumpRevision+1),WaterCheck=null,Drained=false};break;
            case "drain_pump":Require(st.PumpIsolated&&st.PumpMode=="isolated_drain"&&HasResult(s,"ENC-R3-02"),"实际隔离、排水档与独立泵房处置尚未成立。");next=st with{Drained=true,PumpRevision=checked(st.PumpRevision+1),WaterCheck=null};break;
            case "inspect_water":Require(st.PumpIsolated&&st.Drained&&HasResult(s,"ENC-R3-02"),"当前排水与设备控制结果未成立。");next=st with{WaterCheck=st.PumpRevision};break;
            case "locate_workers":Require(st.MineRouteSafe&&st.SignalLocated&&Sources(s,"R3-Q301-R-RESCUE","R3-Q301-R-ENVIRONMENT"),"实际信号、环境与救援请求来源未核对。");next=st with{WorkersLocated=true};break;
            case "receive_workers":
                Require(st.BasicDelivered&&st.LadderSafe&&HasResult(s,"ENC-R3-04"),"实际护送返程、独立梯道或基本安置缺失。");foreach(string id in new[]{"W1","W2","W3"}){var w=BranchOperations.Read<MiningWorker>(s,"R3:worker:"+id)??throw new InvalidOperationException("救援对象不存在。");Require(w.Status=="escorted"&&w.Scene==Actor(s).Scene&&w.ActionClock==Actor(s).ActionClock,"三名工人须随真实移动到达接收站。");changes.Add(BranchOperations.Change(s,"R3:worker:"+id,"mining_worker",w with{Status="received",Spot="R3-S02.basic_handover"}));}
                changes.Add(BranchOperations.Change(s,RescueSegment,"transport_segment",new TransportSegment(RescueSegment,"R3-S07","R3-S02",false,true)));next=st with{WorkersReceived=true,TowerPermit=true};key="R3:workers_received";break;
            case "inspect_raw_gauges":Require(st.MineRouteSafe&&HasResult(s,"ENC-R3-03")&&Sources(s,"R3-Q302-R-RAW"),"仪表现场、独立设备处置或原表未取得。");next=st with{FieldInstrument=true};key="R3:field_instrument";break;
            case "inspect_bypass":Require(st.MineRouteSafe&&Sources(s,"R3-Q302-R-RAW"),"当前安全仪表与原始旁路检查字段未查阅。");next=st with{BypassInspected=true};break;
            case "repair_freight_lift":Require(st.MineRouteSafe&&st.LadderSafe,"必须保留独立梯道并在当前安全矿区修复返程支路。");next=st with{FreightLiftRepaired=true,FreightLiftTested=false};break;
            case "test_freight_lift":Require(st.FreightLiftRepaired&&st.LadderSafe&&st.MineRouteSafe,"货运返程支路或独立梯道未核查。");next=st with{FreightLiftTested=true};key="R3:freight_return_test";break;
            case "inspect_workshop_copy":Require(st.Commission&&Sources(s,"R3-Q302-R-COPY"),"工坊保留副本的当前受托访问与版本缺失。");next=st with{WorkshopCopy=true};break;
            case "inspect_signed_acceptance":Require(st.Commission&&Sources(s,"R3-Q302-R-SIGNED"),"正式验收签章版本未实际取得。");next=st with{SignedAcceptance=true};break;
            case "inspect_local_stores":Require(Closed(s,"Q-301")&&Sources(s,"R3-Q303-R-STOCK"),"基本救援尚未闭合或地方额外库存来源缺失。");next=st with{StoresInspected=true};break;
            case "deliver_extra_packages":
                Require(st.Allocation is not null&&!st.ExtraDelivered,"先明确分配真实六件地方工包。");int index=0;foreach(var entry in new[]{("shelter",st.Allocation!.Shelter),("retrofit",st.Allocation.Retrofit),("production",st.Allocation.Production)})for(int i=0;i<entry.Item2;i++){string itemId="R3-local:work_kit:"+(++index);var item=s.Items.Single(x=>x.Id==itemId);Require(item.OwnerKind=="warehouse"&&item.OwnerId=="R3:extra_allocation","实际工包未保留或已被移走。");items.Add(new(item.Id,item.DefinitionId,item.OwnerKind,item.OwnerId,"installed","R3-S08:"+entry.Item1,null));}next=st with{ExtraDelivered=true,Configuration=checked(st.Configuration+1),TowerTest=null};key="R3:extra_delivered";break;
            case "stop_machine_feed":Require(st.TowerPermit&&Sources(s,"R3-Q304-R-SEQUENCE"),"现场塔站范围与停送料规程未取得。");next=st with{FeedStopped=true,Configuration=checked(st.Configuration+1),TowerTest=null};break;
            case "brace_machine_arm":Require(st.FeedStopped&&st.TowerPermit,"须实际停送料后固定吊臂，压强不能替代这一步。");next=st with{ArmBraced=true,Configuration=checked(st.Configuration+1),TowerTest=null};break;
            case "test_tower_load":Require(HasResult(s,"ENC-R3-05")&&st.FeedStopped&&st.ArmBraced&&st.CurrentLoadAllowed&&st.TowerExtraction==st.CurrentExtraction,"主机结果、机械固定或当前实际受限用电不符。");next=st with{TowerTest=st.Configuration};break;
            case "inspect_tower_duty":Require(st.TowerPermit&&Sources(s,"R3-Q304-R-DUTY"),"地方塔站值守范围未实际核对。");next=st with{TowerDuty=true};break;
            case "inspect_tower_return":Require(st.LadderSafe&&st.TowerPermit,"独立梯道或塔站工作许可未成立。");var geo=CurrentGeometry(s);var actor=Actor(s);var port=geo[actor.Scene].Ports.Single(x=>x.TargetScene=="R3-S08");Require(new NavigationGrid(geo[actor.Scene]).FindPath(actor.Position,port.Stand,true).Length>0&&new NavigationGrid(geo[port.TargetScene]).CanStand(geo[port.TargetScene].Ports.Single(x=>x.Id==port.TargetPort).Stand,true),"当前实际塔站返程几何不可达。");next=st with{TowerReturn=true};break;
            case "pack_verification_sample":Require(st.TowerCurrent&&HasResult(s,"ENC-R3-05")&&Sources(s,"R3-Q304-R-HANDOVER","R3-Q304-R-TOWER"),"当前安全、独立主机处置与合法样本交接范围尚未成立。");next=st with{SamplePacked=true};changes.Add(BranchOperations.Change(s,"R3:sample_custody","sealed_sample",new{seal="R3-MC2-SAMPLE-01",batch="MC-2",custodian="patrol_technician",recipient="R4-public-verification-desk",purpose="verify_materials_and_signatory_scope",origin="R3-S09",visited_remote_site=false}));if(!st.SamplePacked)items.Add(new("R3-MC2-SAMPLE-01","R3-SEALED-VERIFICATION-SAMPLE","new","","player","hero",null));key="R3:sample_pack";break;
            case "record_tower_future_requirements":Require(st.TowerCurrent,"当前实际塔站安全尚未成立。");changes.Add(BranchOperations.Change(s,"R3:tower_future_requirements","work_requirement",new{tower="TWR-R3",base_isolator=1,local_backup=1,base_provision=2,operator_posts=2,crew_capacity=1,local_extra_packages_are_not_tower_kits=true,delivered=false,authority_granted=false}));key="R3:tower_future_requirements";break;
            case "inspect_auxiliary_alarm":Require(Closed(s,"Q-302")&&st.AlarmInvitation=="accept"&&Sources(s,"R3-CQOREN01-R-AUX"),"私人维修邀请与本人限定资料许可尚未成立。");next=st with{AlarmInspected=true};break;
            case "request_independent_alarm_review":Require(st.AlarmRepair&&Closed(s,"Q-304")&&Closed(s,"CQ-OREN-01"),"独立旁修与主塔工作尚未实际完成。");next=st with{AlarmReviewRequested=true};break;
            case "propose_musicbox_repair":case "propose_sidewell_observation":throw new InvalidOperationException("可选委托尚未注册完成合同，当前仅材料可读，不推定完成或奖励。");
            default:throw new InvalidOperationException("该操作需要明确typed参数、稿件确认预览或独立遭遇；无参数按钮不能推定执行。");
        }
        if(key is not null&&BranchOperations.Receipt(s,key))return new(CommitStatus.AlreadyCommitted,null,[]);if(next!=st)changes.Add(Change(s,next));return await Commit(s,"chapter_three_station_action",changes.ToImmutable(),new{action,actual_stand=Package.Stations.Single(x=>x.Action==action).Spot,experience_awarded=0},key,items.ToImmutable());
    }
    public async ValueTask<CommandResult> SetPumpModeAsync(string mode){var s=await _repository.SnapshotAsync();Safe(s);At(s,"R3-S06.drain_control");var st=State(s);Require(st.PumpIsolated&&mode=="isolated_drain","仅在实际隔离后可设定受托排水档，运行旁路不是安全替代。");return await Commit(s,"chapter_three_pump_configured",[Change(s,st with{PumpMode=mode,PumpRevision=checked(st.PumpRevision+1),WaterCheck=null,Drained=false})],new{mode,selection_is_not_drain=true});}
    public async ValueTask<CommandResult> RequestBatchRecordScopeAsync(ImmutableArray<string> fields,string recipient,string purpose,string version)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R3-S03.comparison");Require(State(s).Commission&&Sources(s,"R3-Q301-R-COMMISSION"),"本次MC-2地方检修核查委托未实际核对。");string[] allowed=["check_status","acceptance_status","signatory_scope","version_source"];
        Require(recipient=="patrol_technician"&&purpose=="verify_original_instruments_and_acceptance"&&version=="MC2-current-source-set"&&!fields.IsDefaultOrEmpty&&fields.Distinct().Count()==fields.Length&&fields.All(allowed.Contains),"请求字段、接收方、用途或版本超出本次受托记录范围。");var scope=new ChapterRecordScope("MC-2",version,fields,recipient,purpose,false,false,"R3-local-record-desk");return await Commit(s,"chapter_three_batch_record_scope_issued",[BranchOperations.Change(s,"R3:batch_scope","record_scope",scope)],scope);
    }
    public async ValueTask<CommandResult> RescueWorkerAsync(string id)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R3-S07.rescue");var st=State(s);Require(st.WorkersLocated&&st.MineRouteSafe&&st.LadderSafe,"实际对象位置或独立安全返程路线未成立。");Require(new[]{"W1","W2","W3"}.Contains(id),"现场救援对象未登记。");var w=BranchOperations.Read<MiningWorker>(s,"R3:worker:"+id)??throw new InvalidOperationException("对象不存在。");Require(w.Scene==Actor(s).Scene&&w.Spot=="R3-S07.rescue"&&w.Status=="awaiting_rescue","该工人不在当前实际救援点或已交接。");
        var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(BranchOperations.Change(s,"R3:worker:"+id,"mining_worker",w with{Status="rescued",ActionClock=Actor(s).ActionClock}));
        if(new[]{"W1","W2","W3"}.Where(x=>x!=id).All(x=>BranchOperations.Read<MiningWorker>(s,"R3:worker:"+x) is{Status:"rescued"})){changes.Clear();foreach(string key in new[]{"W1","W2","W3"}){var worker=BranchOperations.Read<MiningWorker>(s,"R3:worker:"+key)!;changes.Add(BranchOperations.Change(s,"R3:worker:"+key,"mining_worker",worker with{Status="escorted",Spot="with_patrol",ActionClock=Actor(s).ActionClock}));}changes.Add(BranchOperations.Change(s,RescueSegment,"transport_segment",new TransportSegment(RescueSegment,"R3-S07","R3-S02",true,false)));}
        return await Commit(s,"chapter_three_worker_rescued",changes.ToImmutable(),new{worker=id,actual_scene=Actor(s).Scene,actual_clock=Actor(s).ActionClock},"R3:rescue:"+id);
    }
    public async ValueTask<CommandResult> CompareBatchAsync(MiningBatchComparison comparison)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R3-S03.comparison");var st=State(s);Require(st.FieldInstrument&&st.BypassInspected&&st.WorkshopCopy&&st.SignedAcceptance&&Sources(s,"R3-Q302-R-RAW","R3-Q302-R-COPY","R3-Q302-R-SIGNED","R3-Q302-R-BATCHES"),"三方现场及原始版本尚未共同取得。");
        Require(comparison.Batch=="MC-2"&&comparison.RawVersion=="MC-2.instrument.I@1"&&comparison.CopyVersion=="MC-2.retained.I@1"&&comparison.AcceptanceVersion=="MC-2.acceptance@3"&&!comparison.CopyIndependent&&comparison.IncompleteChecks.Order().SequenceEqual(new[]{"MC-2-05","MC-2-06"})&&comparison.OrenResponsibility=="unrepaired_bypass_accepted"&&comparison.FullCausality=="unproven"&&comparison.MaraCurrentStatus=="unknown","记录版本、05/06未完成字段、复制同源性或责任/因果范围不符。");
        return await Commit(s,"chapter_three_batch_comparison_verified",[Change(s,st with{Comparison=true}),BranchOperations.Change(s,"R3:comparison","batch_comparison",comparison)],comparison,"R3:tripartite_comparison");
    }
    public async ValueTask<CommandResult> ConfirmAccountabilityAsync(string scope){var s=await _repository.SnapshotAsync();Safe(s);At(s,"R3-S03.oren");Require(State(s).Comparison&&scope=="signed_unrepaired_bypass_cannot_be_excused","责任结论须来自三方核验；修复或歉意不撤销原签章。");return await Commit(s,"chapter_three_accountability_confirmed",[Change(s,State(s) with{Accountability=true})],new{scope,oren_confession_required=false,full_accident_causality="unproven"},"R3:accountability");}
    public async ValueTask<CommandResult> AllocatePackagesAsync(MiningAllocation allocation)
    {
        allocation.Validate();var s=await _repository.SnapshotAsync();Safe(s);At(s,"R3-S08.allocation");var st=State(s);Require(st.StoresInspected&&st.BasicDelivered&&st.WorkersReceived&&Closed(s,"Q-301")&&!st.ExtraDelivered,"先完成基本保障并核对地方六件额外库存，已交付配置不能回收重分。");var items=ImmutableArray.CreateBuilder<ItemChange>();for(int i=1;i<=6;i++){var item=s.Items.Single(x=>x.Id=="R3-local:work_kit:"+i);Require(item.OwnerKind=="warehouse"&&(item.OwnerId is "R3-S08" or "R3:extra_allocation"),"六件实际额外工包不在当前地方库存。");if(item.OwnerKind=="warehouse"&&item.OwnerId=="R3-S08")items.Add(new(item.Id,item.DefinitionId,item.OwnerKind,item.OwnerId,"warehouse","R3:extra_allocation",null));}
        return await Commit(s,"chapter_three_packages_allocated",[Change(s,st with{Allocation=allocation})],new{allocation,reserved_only=true},items:items.ToImmutable());
    }
    public async ValueTask<CommandResult> AppointDutyAsync(ImmutableArray<MiningDuty> duties)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R3-S08.shift_duty");Require(State(s).BasicDelivered&&Closed(s,"Q-301"),"先完成基本救援保障。");Require(duties.Length==2&&duties.Select(d=>d.Post).Order().SequenceEqual(new[]{"pump_watch","ventilation_watch"})&&duties.Select(d=>d.Officer).Distinct().Count()==2&&duties.All(d=>d.Shift=="current_watch_only"&&new[]{"R3-LOCAL-PUMP-WATCH","R3-LOCAL-VENT-WATCH"}.Contains(d.Officer)),"须由两个实际独立当地岗位当班，不能把奥伦或未招募同伴凭空派成双岗。");var changes=ImmutableArray.CreateBuilder<EntityChange>();foreach(var d in duties)changes.Add(BranchOperations.Change(s,"R3:local-duty:"+d.Post,"local_supply_watch",d));changes.Add(Change(s,State(s) with{Duties=duties,Configuration=checked(State(s).Configuration+1),TowerTest=null}));return await Commit(s,"chapter_three_local_watch_appointed",changes.ToImmutable(),new{duties,not_six_tower_operator_posts=true});
    }
    public async ValueTask<CommandResult> ConfirmLoadPolicyAsync(MiningLoadPolicy policy)
    {
        policy.Validate();var s=await _repository.SnapshotAsync();Safe(s);At(s,"R3-S08.policy");var st=State(s);Require(st.ExtraDelivered&&st.Allocation is not null&&Sources(s,"R3-Q303-R-CHOICES"),"实际额外分配交付与两类合格政策来源缺失。");Require(!st.ShiftClosed||policy.Mode=="early_reduce","当前班窗口已实际交接，不得反复重开有限过渡来变成无期限超采。");if(policy.Extraction>0)Require(st.Allocation!.Production>0&&st.Duties.Length==2&&st.Duties.Select(x=>x.Officer).Distinct().Count()==2,"有限负荷过渡须有实际生产份额及两个独立当班岗位。");return await Commit(s,"chapter_three_load_policy_applied",[Change(s,st with{Policy=policy,ShiftClosed=st.ShiftClosed,Configuration=checked(st.Configuration+1),TowerTest=null})],new{policy,essential_load=7,capacity=10,window="world_shift_not_reading_time",unlimited_extraction_allowed=false});
    }
    public async ValueTask<CommandResult> CloseTransitionShiftAsync(){var s=await _repository.SnapshotAsync();Safe(s);At(s,"R3-S08.shift_duty");var st=State(s);Require(st.Policy is{Mode:"time_limited_transition"}&&!st.ShiftClosed,"没有当前有限过渡班可交接。");return await Commit(s,"chapter_three_transition_shift_closed",[Change(s,st with{ShiftClosed=true,Configuration=checked(st.Configuration+1),TowerTest=null})],new{extraction=0,next_shift_must_reduce=true,old_load_test_invalidated=true},"R3:transition_shift_closed");}
    public async ValueTask<CommandResult> ConfigureTowerLoadAsync(int extraction){var s=await _repository.SnapshotAsync();Safe(s);At(s,"R3-S09.tower_control");var st=State(s);Require(st.FeedStopped&&st.ArmBraced&&st.CurrentLoadAllowed&&extraction==st.CurrentExtraction,"先实际停送料并固定吊臂，塔站供给必须对应当前真实受限方案。");return await Commit(s,"chapter_three_tower_load_configured",[Change(s,st with{TowerExtraction=extraction,Configuration=checked(st.Configuration+1),TowerTest=null})],new{essential=7,extraction,total=7+extraction,capacity=10});}
}




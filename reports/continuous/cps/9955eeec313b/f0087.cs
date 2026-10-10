using Mist.Application.Commands;
using System.Collections.Immutable;
using System.Text.Json;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Application.Travel;
using Mist.Core.ChapterFour;
using Mist.Core.Exploration;
using Mist.Core.Quests;

namespace Mist.Application.ChapterFour;

public sealed partial class ChapterFourService
{
    public async ValueTask<CommandResult> ExecuteStationAsync(string action)
    {
        var s=await _repository.SnapshotAsync();Safe(s);var st=State(s);var station=Package.Stations.SingleOrDefault(x=>x.Action==action)??throw new InvalidOperationException("现场操作未登记。");At(s,station.Spot);var next=st;var changes=ImmutableArray.CreateBuilder<EntityChange>();var items=ImmutableArray.CreateBuilder<ItemChange>();string? key=null;
        switch(action)
        {
            case "receive_sample":
                if(st.SampleReceived&&BranchOperations.Receipt(s,"R4:sample_received")){Require(s.Items.Any(i=>i.Id==SampleId&&i.OwnerKind=="warehouse"&&i.OwnerId=="R4-public-verification-desk"),"已收样本的唯一保管位置不符。");return new(CommitStatus.AlreadyCommitted,null,[]);}
                var hand=ActualArrival(s);Require(Sources(s,"R4-Q401-R-LANDING"),"先核对本次样本接收条款。");var sample=s.Items.Single(i=>i.Id==SampleId);Require(sample.DefinitionId=="R3-SEALED-VERIFICATION-SAMPLE"&&sample.OwnerKind=="player"&&sample.OwnerId=="hero","唯一实样不在本次实际携带保管中。");
                var custody=BranchOperations.Entity(s,"R3:sample_custody")??throw new InvalidOperationException("原始封样保管记录缺失。");using(var j=JsonDocument.Parse(custody.Json)){var r=j.RootElement;Require(r.GetProperty("seal").GetString()==SampleId&&r.GetProperty("batch").GetString()=="MC-2"&&r.GetProperty("recipient").GetString()==hand.Recipient&&r.GetProperty("purpose").GetString()==hand.Purpose,"封样批次与用途未核对。");}
                items.Add(new(sample.Id,sample.DefinitionId,sample.OwnerKind,sample.OwnerId,"warehouse","R4-public-verification-desk",null));changes.Add(BranchOperations.Change(s,"R4:actual_sample_custody","sample_custody",new{seal=SampleId,batch="MC-2",source_versions=hand.SourceVersions,recipient=hand.Recipient,purpose=hand.Purpose,received=true,contents_assessed=false}));next=st with{SampleReceived=true};key="R4:sample_received";break;
            case "accept_public_verification":Require(st.SampleReceived&&Sources(s,"R4-Q401-R-BRIEF"),"实际样本接收与本次公共核验条款尚未成立。");next=st with{PublicAssignment=true};key="R4:public_assignment";break;
            case "request_old_hall_permit":Require(st.SampleReceived&&Sources(s,"R4-Q402-R-SAFETY"),"实际样本和旧厅现场安全条款未核对。");next=st with{OldPermit=true};key="R4:old_hall_permit";break;
            case "isolate_old_facility":Require(st.OldPermit,"旧厅现场许可尚未成立。");next=st with{OldIsolated=true,OldRevision=checked(st.OldRevision+1)};break;
            case "inspect_island_isolation":Require(Closed(s,"Q-401")&&Closed(s,"Q-402")&&Result(s,"ENC-R4-03"),"两份核验报告或校准防卫实际制止尚未成立。");next=st with{IslandIsolation=true,IslandRevision=checked(st.IslandRevision+1),ConfigurationCheckRevision=null};break;
            case "seat_island_reserve":Require(st.IslandIsolation,"连接本地已装储备前须实际隔离。");next=st with{IslandReserve=true,IslandRevision=checked(st.IslandRevision+1),ConfigurationCheckRevision=null};break;
            case "secure_island_guard":Require(st.IslandIsolation,"固定仪表护罩须先隔离。");next=st with{IslandGuard=true,IslandRevision=checked(st.IslandRevision+1),ConfigurationCheckRevision=null};break;
            case "verify_island_configuration":Require(Closed(s,"Q-401")&&Closed(s,"Q-402")&&Result(s,"ENC-R4-03")&&st.IslandIsolation&&st.IslandGuard&&Calibrated(st)&&st.IslandLoad is 40 or 70&&st.IslandContact=="limited"&&st.TrialOperator=="N-SIAN"&&st.TrialReviewer=="ROLE-R4-TRIAL-REVIEW"&&Sources(s,"R4-Q403-R-ISLAND"),"当前校准、现场隔离、岗位、有限联络或规则配置未成立。");next=st with{ConfigurationCheckRevision=st.IslandRevision};break;
            case "open_logistics_latch":Require(st.FieldEntryAuthorized&&Runs(s).Any(r=>r.Configuration.Controller=="C"&&r.Minutes==10&&r.Outcome is "stable" or "safe_stopped"),"实际运行观察尚未保存，后勤门未获放行。");next=st with{LogisticsLatch=true};key="R4:logistics_latch";break;
            case "request_prism_permit":Require(Closed(s,"Q-403"),"有限孤网试验尚未完成实际报告交接。");next=st with{PrismPermit=true};key="R4:prism_permit";break;
            case "inspect_prism_duty":Require(st.PrismPermit&&Sources(s,"R4-Q404-R-TOWER"),"本次塔站许可及当前班表未核对。");next=st with{PrismDuty=true};break;
            case "inspect_prism_safety":Require(st.PrismPermit&&st.PrismDuty,"当前值守与本次塔站许可尚未成立。");next=st with{PrismIsolation=true,PrismRevision=checked(st.PrismRevision+1),PrismCheckRevision=null,PrismReturn=false};break;
            case "check_prism_load":st.PrismLoad.Validate();Require(st.PrismPermit&&st.PrismDuty&&st.PrismIsolation,"现场保护与隔离未完成。");next=st with{PrismCheckRevision=st.PrismRevision,PrismReturn=false};break;
            case "check_prism_return":Require(st.PrismCheckRevision==st.PrismRevision&&st.PrismIsolation,"须先取得当前真实降载检查。");var geo=CurrentGeometry(s);var actor=Actor(s);var exit=geo[actor.Scene].Ports.Single(p=>p.TargetScene=="R4-S02");Require(new NavigationGrid(geo[actor.Scene]).FindPath(actor.Position,exit.Stand,true).Length>0&&new NavigationGrid(geo[exit.TargetScene]).CanStand(geo[exit.TargetScene].Ports.Single(p=>p.Id==exit.TargetPort).Stand,true),"当前实际返程通路不可达。");next=st with{PrismReturn=true};break;
            case "publish_preparation_list":Require(Closed(s,"Q-403")&&Sources(s,"R4-Q404-R-PREPARATION")&&st.PrismCheckRevision==st.PrismRevision&&st.PrismReturn,"当前棱镜检查或公开清单原文未取得。");next=st with{PreparationPublic=true};changes.Add(BranchOperations.Change(s,"R4:public_preparation","public_preparation",new{verified_scheme=true,six_tower_changes="actual_work_pending",transition_supply="actual_work_pending",common_protocol="actual_acceptances_pending",field_trials="per_tower_receipts_pending",requirements_are_not_stock=true}));key="R4:public_preparation";break;
            default:throw new InvalidOperationException("该现场工作需要明确typed参数、独立实验/遭遇或稿件提交接口，不能用无参数按钮填造完成。");
        }
        if(key is not null&&BranchOperations.Receipt(s,key))return new(CommitStatus.AlreadyCommitted,null,[]);if(next!=st)changes.Add(Change(s,next));return await Commit(s,"glasshaven_station_action",changes.ToImmutable(),new{action,actual_stand=station.Spot,experience_awarded=0},key,items.ToImmutable());
    }
    public async ValueTask<CommandResult> RequestReportScopeAsync(string version,ImmutableArray<string> fields,string recipient,string purpose)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R4-S03.custodian");Require(State(s).PublicAssignment&&Sources(s,"R4-Q401-R-ACCESS"),"本次核验委托与保管条款缺失。");string[] allowed=["observation_ids","test_conditions","outcomes","issue_version","circulation_dates"];Require(version=="7"&&recipient=="patrol_technician"&&purpose=="verify_public_data"&&!fields.IsDefaultOrEmpty&&fields.Distinct().Count()==fields.Length&&fields.All(allowed.Contains),"记录版本、必要字段、接收方或用途超出本次保管范围。");var scope=new GlasshavenRecordScope("GH-R7",version,fields,recipient,purpose,"R4-records-custodian");return await Commit(s,"glasshaven_report_scope_issued",[Change(s,State(s) with{ReportScope=true}),BranchOperations.Change(s,"R4:report_scope","record_scope",scope)],scope);
    }
    public async ValueTask<CommandResult> CompareFieldSourcesAsync(FieldSourceComparison comparison)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R4-S03.table");Require(State(s).PublicAssignment&&Sources(s,"R4-Q401-R-SURVEY-A","R4-Q401-R-SURVEY-B","R4-Q401-R-VISUAL"),"独立原始调查与派生表未实际取得。");Require(comparison is{IndependentTeams:true,Window:"W-1",Units:"local_field_indicator_not_trial_scale",Causality:"association_to_test",UncontrolledConditions:"location_and_history"},"应保留独立源、地点限制与可检验关联，不能以曲线证明全部原因。");return await Commit(s,"glasshaven_field_sources_compared",[Change(s,State(s) with{FieldCompared=true}),BranchOperations.Change(s,"R4:field_comparison","source_comparison",comparison)],comparison);
    }
    public async ValueTask<CommandResult> CompareReportVersionsAsync(ReportVersionComparison comparison)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R4-S04.circulation");Require(Sources(s,"R4-Q401-R-MANUSCRIPT","R4-Q401-R-ISSUED","R4-Q401-R-CIRCULATION"),"受限同版原稿、发行与流转来源未取得。");Require(comparison.Manuscript=="GH-R7-v7"&&comparison.Issued=="GH-R7-v7"&&comparison.Register=="GH-R7-v7"&&comparison.ReportingVersion=="7"&&comparison.RemovedRows.Order().SequenceEqual(new[]{"T2","T3"})&&!comparison.CopyIndependent,"同版链、特定删除字段或副本独立性不符。");return await Commit(s,"glasshaven_report_versions_compared",[Change(s,State(s) with{VersionsCompared=true}),BranchOperations.Change(s,"R4:version_comparison","source_comparison",comparison)],comparison);
    }
    public async ValueTask<CommandResult> PerformReserveStepAsync(string step)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R4-S05.reserve_case");var st=State(s);Require(st.OldPermit&&st.OldIsolated,"备用段工作前必须实际隔离。");if(Result(s,"ENC-R4-02"))return new(CommitStatus.AlreadyCommitted,null,[]);var next=step switch{"open"when !st.ReserveCaseSecured=>st with{ReserveCaseOpen=true},"clear"when st.ReserveCaseOpen=>st with{ReserveCaseCleared=true},"secure"when st.ReserveCaseOpen&&st.ReserveCaseCleared=>st with{ReserveCaseOpen=false,ReserveCaseSecured=true},"inspect"when st.ReserveCaseCleared&&st.ReserveCaseSecured=>st,_=>throw new InvalidOperationException("备用段实际前置步骤缺失。")};if(step=="inspect"){var result=new GlasshavenEncounterResult("ENC-R4-02",true,Guid.NewGuid(),Guid.NewGuid(),"isolated_manual_reserve_clear",st.OldRevision);return await Commit(s,"glasshaven_encounter_result",[BranchOperations.Change(s,"R4:result:ENC-R4-02","chapter_encounter_result",result)],result,"R4:ENC-R4-02:result");}return await Commit(s,"glasshaven_reserve_step",[Change(s,next with{OldRevision=checked(st.OldRevision+1)})],new{step,actual_stand="R4-S05.reserve_case"});
    }
    public async ValueTask<CommandResult> InspectCalibrationChannelAsync(string channel)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,channel=="Z"?"R4-S06.zero":channel=="R"?"R4-S06.reference":throw new InvalidOperationException("通道未登记。"));var st=State(s);var marks=BranchOperations.Entity(s,"R4:calibration_marks")??throw new InvalidOperationException("实际参考标未加载。");using var doc=JsonDocument.Parse(marks.Json);Require(doc.RootElement.GetProperty("instrument_revision").GetInt64()==st.InstrumentRevision&&doc.RootElement.GetProperty(channel=="Z"?"zero":"reference").GetInt32()==(channel=="Z"?0:10),"当前参考标读数不符，不能确认旧仪表校准。");var next=channel=="Z"?st with{ZeroCheckRevision=st.InstrumentRevision}:st with{ReferenceCheckRevision=st.InstrumentRevision};return await Commit(s,"glasshaven_reference_channel_inspected",[Change(s,next)],new{channel,revision=st.InstrumentRevision,actual_reading=channel=="Z"?0:10});
    }
    public async ValueTask<CommandResult> ConfigureIslandAsync(int load,bool reserve,string contact,string operatorId,string reviewerId)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R4-S06.load");var st=State(s);Require(st.IslandIsolation&&st.IslandGuard&&Calibrated(st)&&Result(s,"ENC-R4-03")&&Sources(s,"R4-Q403-R-ISLAND"),"实际隔离/护罩/参考标/校准防卫未就绪。");Require(load is 40 or 70,"55及其他负荷不在已验证有限域，不生成假读数。");Require(contact=="limited"&&operatorId=="N-SIAN"&&reviewerId=="ROLE-R4-TRIAL-REVIEW"&&st.IslandContact==contact&&st.TrialOperator==operatorId&&st.TrialReviewer==reviewerId,"有限联络及两试验岗位须在各自真实站位先登记。");return await Commit(s,"glasshaven_island_configured",[Change(s,st with{IslandLoad=load,IslandReserve=reserve,IslandRevision=checked(st.IslandRevision+1),ConfigurationCheckRevision=null})],new{load,reserve,contact,operatorId,reviewerId,preparation_is_not_execution=true});
    }
    public async ValueTask<CommandResult> AssignTrialDutyAsync(string operatorId,string reviewerId)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R4-S06.duty");var st=State(s);Require(st.IslandIsolation&&Result(s,"ENC-R4-03")&&Sources(s,"R4-Q403-R-ISLAND")&&operatorId=="N-SIAN"&&reviewerId=="ROLE-R4-TRIAL-REVIEW","仅受理当前已实际隔离的两个有限试验岗位，不授予永久六塔岗位。");return await Commit(s,"glasshaven_trial_duty_assigned",[Change(s,st with{TrialOperator=operatorId,TrialReviewer=reviewerId,IslandRevision=checked(st.IslandRevision+1),ConfigurationCheckRevision=null})],new{operatorId,reviewerId,scope="bounded_current_trial_only",actual_stand="R4-S06.duty"});
    }
    public async ValueTask<CommandResult> SetIslandContactAsync(string contact)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R4-S06.contact");var st=State(s);Require(st.IslandIsolation&&st.IslandGuard&&Sources(s,"R4-Q403-R-ISLAND")&&contact=="limited","仅允许本次现场隔离下的有限联络，不能连接全网。");return await Commit(s,"glasshaven_trial_contact_set",[Change(s,st with{IslandContact=contact,IslandRevision=checked(st.IslandRevision+1),ConfigurationCheckRevision=null})],new{contact,actual_stand="R4-S06.contact",network_connected=false});
    }
    public async ValueTask<CommandResult> PrepareOutputStepAsync(string step)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R4-S07.output");var st=State(s);var encounter=BranchOperations.Read<GlasshavenEncounterState>(s,CombatId("ENC-R4-04"));Require(st.FieldEntryAuthorized&&encounter is{ThreatControlled:true}&&Sources(s,"R4-Q403-R-OUTPUT"),"真实执行机制止与输出规程未取得。");var next=step switch{"isolate"=>st with{OutputIsolated=true},"select_TEST"when st.OutputIsolated=>st with{OutputRoute="TEST"},"seat_coupler"when st.OutputIsolated&&st.OutputRoute=="TEST"=>st with{CouplerSeated=true},"secure_guard"when st.CouplerSeated&&st.OutputIsolated=>st with{OutputGuard=true},"inspect"when st.OutputIsolated&&st.OutputRoute=="TEST"&&st.CouplerSeated&&st.OutputGuard=>st with{OutputCheckRevision=st.OutputRevision},_=>throw new InvalidOperationException("实际输出前置步骤缺失或通路超出TEST许可。")};if(step!="inspect")next=next with{OutputRevision=checked(st.OutputRevision+1),OutputCheckRevision=null};
        if(step=="inspect"&&!Result(s,"ENC-R4-04")){var result=new GlasshavenEncounterResult("ENC-R4-04",true,encounter!.Battle.Instance,encounter.Battle.Attempt,"actual_control_and_TEST_output",next.OutputRevision);return await Commit(s,"glasshaven_encounter_result",[Change(s,next),BranchOperations.Change(s,"R4:result:ENC-R4-04","chapter_encounter_result",result)],result,"R4:ENC-R4-04:result");}return await Commit(s,"glasshaven_output_step",[Change(s,next)],new{step,actual_revision=next.OutputRevision,execution_not_trial_data=true});
    }
    public async ValueTask<CommandResult> SetPrismLoadAsync(GlasshavenTowerLoad load)
    {
        load.Validate();var s=await _repository.SnapshotAsync();Safe(s);At(s,"R4-S08.selector");var st=State(s);Require(st.PrismPermit&&st.PrismDuty,"实际塔站工作与值守未核对。");return await Commit(s,"glasshaven_prism_load_changed",[Change(s,st with{PrismLoad=load,PrismRevision=checked(st.PrismRevision+1),PrismCheckRevision=null,PrismReturn=false})],new{load,scope="current_watch_not_permanent_conversion"});
    }
    public async ValueTask<CommandResult> RequestFacilityScopeAsync(ImmutableArray<string> fields,string recipient,string purpose,string version)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R4-S04.facility");Require(Closed(s,"Q-403")&&Sources(s,"R4-Q404-R-ACCESS"),"有限试验交接与设施限定条款未成立。");string[] allowed=["facility_code","supply_destination","watch_post","version_source"];Require(version=="1"&&recipient=="patrol_technician"&&purpose=="verify_holding_facility"&&!fields.IsDefaultOrEmpty&&fields.Distinct().Count()==fields.Length&&fields.All(allowed.Contains),"仅允许本次设施地点必要字段，不包含医疗现状/身份键/住址。");var scope=new GlasshavenRecordScope("F-17",version,fields,recipient,purpose,"R4-records-custodian");return await Commit(s,"glasshaven_facility_scope_issued",[BranchOperations.Change(s,"R4:facility_scope","record_scope",scope)],scope);
    }
    public async ValueTask<CommandResult> VerifyNorthStationAsync(string facility,string supply,string guard,string location,string maraCurrentStatus)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R4-S04.facility");Require(Closed(s,"Q-403")&&Sources(s,"R4-Q404-R-LOCATION","R4-Q404-R-SUPPLY","R4-Q404-R-GUARD"),"设施/物资/看守三个独立原始来源未获准取得。");Require(facility=="F-17"&&supply=="F-17"&&guard=="F-17"&&location=="old_city_north_station"&&maraCurrentStatus=="unknown","地点映射与当前生命状态必须分开，不得从代码编造现状。");return await Commit(s,"glasshaven_location_verified",[Change(s,State(s) with{NorthLocationVerified=true}),BranchOperations.Change(s,"R4:location_comparison","location_comparison",new{facility,supply,guard,location,mara_current_status=maraCurrentStatus,source_roots=new[]{"r4-facility-original-index","r4-north-station-supply-custody-original","r4-north-station-watch-register-original"}})],new{location,mara_current_status=maraCurrentStatus});
    }
    public async ValueTask<CommandResult> ExchangeVossAsync(bool acknowledgeTransitionNeeds,bool preserveIndependentReview,string currentMaraStatus)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R4-S02.voss");Require(Closed(s,"Q-403")&&Sources(s,"R4-Q404-L-VOSS"),"首次完整交涉的已知来源尚未成立。");Require(acknowledgeTransitionNeeds&&preserveIndependentReview&&currentMaraStatus=="unknown","须分别保留真实供给条件、独立复核与导师当前未知；政治偏好不会裁定责任。");return await Commit(s,"glasshaven_voss_exchange_recorded",[Change(s,State(s) with{VossExchange=true}),BranchOperations.Change(s,"R4:voss_exchange","attributed_exchange",new{voss_long_term_risk_knowledge="acknowledged_in_exchange",transition_week_supply_people_responsibility=true,independent_review_preserved=true,broader_authorization_finding="pending_Q503",final_policy="not_selected"})],new{acknowledgeTransitionNeeds,preserveIndependentReview,currentMaraStatus});
    }
    public async ValueTask<CommandResult> RegisterCurrentReturnAnchorAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);var actor=Actor(s);var st=State(s);string spot=actor.Scene switch{"R4-S01"when st.SampleReceived=>"R4-S01.berth","R4-S02"when st.PublicAssignment=>"R4-S02.sian","R4-S08"when Closed(s,"Q-404")&&st.PrismReturn&&st.PrismCheckRevision==st.PrismRevision=>"R4-S08.return_check",_=>throw new InvalidOperationException("不是当前已实际成立的地区安全点。")};At(s,spot);var anchor=new ReturnAnchor(actor.Scene,CurrentGeometry(s)[actor.Scene].Spots.Single(p=>p.Id==spot).Stand,actor.Scene=="R4-S08"?ReturnAnchorKind.TowerSafeExit:ReturnAnchorKind.Settlement,"");var issued=await Commit(s,"return_anchor_verified",[],anchor);Require(issued.Status==CommitStatus.Committed&&issued.Receipt is not null,"实际安全点来源未提交。");return await new TravelService(_repository,_catalog,CurrentGeometry(await _repository.SnapshotAsync())).RegisterAnchorAsync(issued.Receipt!.Id.Value.ToString());
    }
}

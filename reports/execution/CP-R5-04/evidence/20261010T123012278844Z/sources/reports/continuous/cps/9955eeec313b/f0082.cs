using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Roster;
using Mist.Application.Storage;
using Mist.Application.WorkOrders;
using Mist.Core.ChapterFive;
using Mist.Core.ChapterFour;
using Mist.Core.Contracts;
using Mist.Core.Engineering;

namespace Mist.Application.ChapterFive;

public sealed partial class ChapterFiveService
{
    public async ValueTask<CommandResult> ExecuteStationAsync(string action)
    {
        if(action=="begin_guard_group_encounter")return await BeginEncounterAsync("ENC-R5-03");if(action=="begin_archive_relay_encounter")return await BeginEncounterAsync("ENC-R5-04");if(action=="verify_archive_safety")return await VerifyCurrentTowerSafetyAsync();
        var s=await _repository.SnapshotAsync();Safe(s);var station=Package.Stations.Single(x=>x.Action==action);At(s,station.Spot);var st=State(s);var next=st;var changes=ImmutableArray.CreateBuilder<EntityChange>();var items=ImmutableArray<ItemChange>.Empty;object payload=new{action,scope="actual_local_work",language_understanding="not_claimed"};string? key=null;
        switch(action)
        {
            case "register_landing":Require(Sources(s,"R5-Q501-R-LANDING"),"请取得实际渡口原文。");next=st with{Landing=true};key="R5:landing";break;
            case "receive_basic_kit":
                Require(st.Landing&&Sources(s,"R5-Q501-R-STREET"),"实际登陆与地方任务工具来源缺失。");Require(!st.BasicKit,"唯一任务工具已经领用。");items=s.Items.Where(i=>i.Id is "R5-basic-kit" or "R5-basic-lamp" or "R5-basic-line").Select(i=>{Require(i.OwnerKind=="warehouse"&&i.OwnerId=="R5-local-mission-desk","当地唯一基础工具不在供给台。");return new ItemChange(i.Id,i.DefinitionId,i.OwnerKind,i.OwnerId,"player","hero",null);}).ToImmutableArray();Require(items.Length==3,"三件真实任务工具不足。");next=st with{BasicKit=true};key="R5:basic_kit_received";break;
            case "check_current_credentials":
                Require(st.Landing&&Sources(s,"R5-Q501-R-CREDENTIAL"),"实际本次登陆与查证规则缺失。");var commission=BranchOperations.Entity(s,"commission:next-inquiry")??throw new InvalidOperationException("当前学城区域委托缺失。");using(var doc=System.Text.Json.JsonDocument.Parse(commission.Json)){var j=doc.RootElement;Require(j.GetProperty("allowed").GetBoolean()&&j.GetProperty("region").GetString()=="R5"&&j.GetProperty("basis").GetString()=="Q-404:resolution"&&j.GetProperty("purpose").GetString()=="verify_north_station_and_legal_access","当前委托未覆盖北站核查。");}next=st with{CredentialChecked=true};break;
            case "inspect_checkpoint_passage":Require(st.CredentialChecked&&st.MapCompared&&Sources(s,"R5-Q501-R-MAP"),"当前身份、分图层核对与独立通道原文未成立。");next=st with{CheckpointPassageChecked=true};if(!Result(s,"ENC-R5-01")){var r=new OldCityEncounterResult("ENC-R5-01",true,Guid.NewGuid(),Guid.NewGuid(),"actual_current_credentials_and_independent_passage_check");changes.Add(BranchOperations.Change(s,"R5:result:ENC-R5-01","chapter_encounter_result",r));payload=r;action="old_city_encounter_result";key="R5:ENC-R5-01:result";}break;
            case "isolate_tunnel_drive":Require(st.BasicKit&&Sources(s,"R5-Q501-R-TUNNEL"),"当地基础工具与隧道规程缺失。");next=st with{TunnelIsolated=true};break;
            case "brace_tunnel_panel":Require(st.TunnelIsolated&&st.BasicKit,"先实际隔离，再用基础工具固定。");next=st with{TunnelBraced=true};break;
            case "clear_tunnel_obstruction":Require(st.TunnelIsolated&&st.TunnelBraced&&st.BasicKit,"隔离、固定和基础工具尚未共同成立。");next=st with{TunnelObstructionCleared=true};break;
            case "verify_tunnel_zero_motion":
                Require(st.TunnelIsolated&&st.TunnelBraced&&st.TunnelObstructionCleared&&Sources(s,"R5-Q501-R-TUNNEL"),"真实隔离、固定与清障尚未完成。");next=st with{TunnelZeroChecked=true};if(!Result(s,"ENC-R5-02")){var r=new OldCityEncounterResult("ENC-R5-02",true,Guid.NewGuid(),Guid.NewGuid(),"isolate_brace_clear_zero_motion");changes.Add(BranchOperations.Change(s,"R5:result:ENC-R5-02","chapter_encounter_result",r));payload=r;action="old_city_encounter_result";key="R5:ENC-R5-02:result";}break;
            case "inspect_tunnel_passage":Require(st.TunnelIsolated&&st.TunnelBraced&&st.TunnelObstructionCleared&&Result(s,"ENC-R5-02"),"处置真实隧道危险后才能检查通路。");next=st with{TunnelPassageChecked=true};break;
            case "inspect_detention_records":Require(st.RescueScope&&Closed(s,"Q-501")&&Sources(s,"R5-Q502-R-DUTY"),"合法北站入口、具体救援范围和原始值守资料缺失。");next=st with{DetentionRecordsChecked=true};break;
            case "isolate_detention_drive":Require(st.RescueScope&&st.DetentionRecordsChecked&&st.BasicKit,"当前救援范围、现场门控记录及工具未成立。");next=st with{DoorDriveIsolated=true};break;
            case "clear_duty_obstruction":Require(st.DoorDriveIsolated&&st.BasicKit,"先隔离扣押门驱动，再清值守障碍。");next=st with{DutyObstructionCleared=true};break;
            case "inspect_detention_door":Require(Result(s,"ENC-R5-03")&&st.DoorDriveIsolated&&Sources(s,"R5-Q502-R-RESCUE"),"先实际制止看守并取得真实释放规程。");next=st with{DetentionDoorChecked=true};break;
            case "release_mara":
                Require(st.DetentionDoorChecked&&Result(s,"ENC-R5-03")&&st.BasicKit&&!st.MaraReleased,"实体门未检查、看守未制止或已释放。");next=st with{MaraReleased=true};changes.Add(BranchOperations.Change(s,"R5:mara","mara_rescue",new MaraRescue("released_alive_direct_contact","R5-S06","actual_cell_open_and_direct_contact","not_received",Actor(s).ActionClock)));changes.Add(BranchOperations.Change(s,"finding:K-MARA-CURRENT-ALIVE","verified_finding",new{fact="K-MARA-CURRENT-ALIVE",source="R5:direct_mara_contact",current_status="alive_direct_contact",historical_D1_unchanged=true}));key="R5:direct_mara_contact";break;
            case "open_inner_evacuation":Require(st.MaraReleased&&st.BasicKit,"须从扣押区内侧实际释放后开闩。");next=st with{EvacuationBoltOpen=true};break;
            case "inspect_inner_evacuation":Require(st.MaraReleased&&st.EvacuationBoltOpen,"内侧撤离闩尚未打开。");next=st with{EvacuationChecked=true};break;
            case "receive_mara":
                var rescue=BranchOperations.Read<MaraRescue>(s,"R5:mara");Require(st.MaraEscorted&&rescue is{Status:"escorted_alive",Scene:"R5-S02",Source:"actual_inner_evacuation",Reception:"pending_reception"}&&Sources(s,"R5-Q502-L-RECEPTION"),"须实际经过内侧安全便道护送至街区，再接收。");next=st with{MaraReceived=true};changes.Add(BranchOperations.Change(s,"R5:mara","mara_rescue",new MaraRescue("received_alive","R5-S02","actual_inner_evacuation","N-ADA_and_R5-local-receiver",Actor(s).ActionClock)));key="R5:mara_received";break;
            case "request_tower_takeover":Require(Closed(s,"Q-503")&&st.Custody is not null&&RequiredSupervisors.All(st.Supervisors.Contains)&&Sources(s,"R5-Q504-R-OPTIONS"),"有来源审查、保管选择与逐岗共同监督未成立。");next=st with{JointPermission=true};break;
            case "isolate_archive_relay":Require(st.JointPermission&&Sources(s,"R5-Q504-R-TOWER"),"当次共同许可与塔站规程缺失。");next=st with{ArchiveRelayIsolated=true,TowerRevision=st.TowerRevision+1,TowerCheckRevision=null};break;
            case "secure_archive_rack":Require(st.ArchiveRelayIsolated&&st.BasicKit,"隔离继电并用基础工具固定机架。");next=st with{ArchiveRackSecured=true,TowerRevision=st.TowerRevision+1,TowerCheckRevision=null};break;
            case "appoint_archive_operator":Require(st.JointPermission&&st.Supervisors.Contains("ROLE-R5-OPERATOR"),"实际当地操作岗位尚未接受共同监督。");next=st with{Operator="ROLE-R5-OPERATOR",TowerRevision=st.TowerRevision+1,TowerCheckRevision=null};changes.Add(BranchOperations.Change(s,"R5:operator","local_operation_post",new{actor="ROLE-R5-OPERATOR",tower="TWR-R5",scope="current_local_protection",accepted=true,person_not_companion=true}));break;
            case "test_archive_configuration":Require(st.ArchiveRelayIsolated&&st.ArchiveRackSecured&&Result(s,"ENC-R5-04")&&st.Operator=="ROLE-R5-OPERATOR"&&RequiredSupervisors.All(st.Supervisors.Contains),"当前隔离、固定、独立防卫处置及岗位未共同成立。");st.Load.Validate();next=st with{TowerCheckRevision=st.TowerRevision};payload=new{tower="TWR-R5",configuration=st.TowerRevision,load=st.Load,actual_test=true,conversion_established=false};break;
            case "inspect_archive_return":Require(st.JointPermission&&st.TowerCheckRevision==st.TowerRevision&&Result(s,"ENC-R5-04"),"塔站当前配置未核验。");next=st with{TowerReturnChecked=true};break;
            case "record_tower_future_requirements":
                Require(TowerSafe(s,st)&&Sources(s,"R5-Q504-R-TOWER"),"先实际核验当前有限安全与公开未来清单。");var rules=new EngineeringRules(_catalog);changes.Add(BranchOperations.Change(s,"R5:future_preparation","future_preparation",new{tower="TWR-R5",baseline=rules.Required("TWR-R5",WorkType.BaselineCheck),enhanced=rules.Required("TWR-R5",WorkType.EnhancedInstall),transition=rules.Required("TWR-R5",WorkType.TransitionSupply),trial=rules.Required("TWR-R5",WorkType.FieldTrial),actual_operator_posts=2,installation_capacity=1,requirements_only=true,delivered_stock=false}));break;
            case "preserve_original_case":
                Require(st.Custody is not null&&Closed(s,"Q-503")&&!st.OriginalCasePreserved,"已核审查、明确保管与唯一原件交接尚未成立。");var original=s.Items.SingleOrDefault(i=>i.Id==OriginalCase&&i.DefinitionId=="R5-ORIGINAL-ARCHIVE-CASE"&&i.OwnerKind=="warehouse"&&i.OwnerId=="R5-local-original-custodian")??throw new InvalidOperationException("唯一原件箱未在真实保管人手中。");items=[new(original.Id,original.DefinitionId,original.OwnerKind,original.OwnerId,"installed",st.Custody!.Mode=="joint_sealed"?"R5-joint-sealed-vault":"R5-bounded-public-archive",null)];next=st with{OriginalCasePreserved=true};changes.Add(BranchOperations.Change(s,"R5:original_custody","original_custody",st.Custody!));payload=new{original_case=OriginalCase,custody=st.Custody,originals_duplicated=false};key="R5:original_case_preserved";break;
            case "send_transfer_demand":
                Require(TowerSafe(s,st)&&st.OriginalCasePreserved&&Closed(s,"Q-503"),"当前有限塔安全、唯一原件保管与有来源审查尚未成立。");next=st with{TransferDemandSent=true,RookRefusalRecorded=true};changes.Add(BranchOperations.Change(s,"R5:rook_reply","executive_transfer_reply",new{actor="N-ROOK",request="bounded_joint_transfer",reply="refused_transfer",movement="entered_seventh_tower",program="forced_normalisation",central_machinery_stopped=false}));changes.Add(BranchOperations.Change(s,"finding:K-ROOK-TRANSFER-REFUSAL","verified_finding",new{fact="K-ROOK-TRANSFER-REFUSAL",source="R5:transfer_demand",refusal=true,destination="seventh_tower"}));key="R5:transfer_demand";break;
            case "propose_local_sign":case "request_lost_property_check":throw new InvalidOperationException("该可选正文是未接入候选，尚无实体交接和奖励权益。");
            default:throw new InvalidOperationException("该站位需要明确 typed 参数或预览交接，不能以动作名直接完成。");
        }
        changes.Add(Change(s,next));return await Commit(s,action.StartsWith("old_city_",StringComparison.Ordinal)?action:"old_city_"+action,changes.ToImmutable(),payload,key,items);
    }
    private static readonly string[] RequiredSupervisors=["ROLE-R5-OPERATOR","N-ADA","ROLE-R5-ARCHIVE-CUSTODIAN"];
    public async ValueTask<CommandResult> CompareMapAsync(OldCityRouteComparison c)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R5-S03.map");Require(Sources(s,"R5-Q501-R-STREET","R5-Q501-R-MAP")&&c.Historic=="HIST-CENTRAL-SPAN"&&c.Current is "CHECKPOINT-DUTY-PASSAGE" or "TUNNEL-SERVICE-PASSAGE"&&c.Destination=="duty_hall_only"&&!c.HistoricalLineGrantsAccess,"须用已取得原文区分旧桥、当前可行路线及值守厅止境。");return await Commit(s,"old_city_route_compared",[Change(s,State(s) with{MapCompared=true}),BranchOperations.Change(s,"R5:route_comparison","route_comparison",c)],c);
    }
    public async ValueTask<CommandResult> RequestRescueScopeAsync(ImmutableArray<string> fields,string recipient,string purpose,string version)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R5-S05.scope");string[] allowed=["door_status","watch_arrangement","return_route_status","version_source"];Require(Closed(s,"Q-501")&&Sources(s,"R5-Q502-R-DUTY")&&recipient=="patrol_technician"&&purpose=="rescue_and_safe_return"&&version=="NORTH-DUTY-D0@1"&&!fields.IsDefault&&fields.Distinct().Count()==fields.Length&&fields.Length==allowed.Length&&allowed.All(fields.Contains),"限定救援范围不包括无关身份和医疗档案。");var scope=new OldCityRecordScope(version,fields,recipient,purpose,"R5-local-station-desk");return await Commit(s,"old_city_rescue_scope_granted",[Change(s,State(s) with{RescueScope=true}),BranchOperations.Change(s,"R5:rescue_scope","record_scope",scope)],scope);
    }
    public async ValueTask<CommandResult> RequestResponsibilityScopeAsync(ImmutableArray<string> fields,string recipient,string purpose,string version)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R5-S07.scope");string[] allowed=["design_limits","correction_history","policy_authorization","execution_order","version_source"];Require(Closed(s,"Q-502")&&State(s).MaraReceived&&Sources(s,"R5-Q503-R-SCOPE")&&recipient=="patrol_technician"&&purpose=="distinguish_design_policy_execution_responsibility"&&version=="OC-responsibility-set@1"&&!fields.IsDefault&&fields.Length==allowed.Length&&fields.Distinct().Count()==fields.Length&&allowed.All(fields.Contains),"当前限定审查不授权无关身份/地址。");var scope=new OldCityRecordScope(version,fields,recipient,purpose,"R5-local-record-custodian");return await Commit(s,"old_city_responsibility_scope_granted",[Change(s,State(s) with{ResponsibilityScope=true}),BranchOperations.Change(s,"R5:responsibility_scope","record_scope",scope)],scope);
    }
    public async ValueTask<CommandResult> CompareResponsibilitiesAsync(OldCityResponsibilityComparison c)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R5-S07.hearing");Require(State(s).ResponsibilityScope&&Sources(s,"R5-Q503-R-DESIGN","R5-Q503-R-REVISION","R5-Q503-R-POLICY","R5-Q503-R-EXECUTION"),"四组必要原件尚未获准取得。");Require(c is{DesignVersion:"OC-D18.design@1",RevisionVersion:"OC-D14.revision@2",CorrectionVersion:"OC-D30.correction@1",PolicyVersion:"VQ-D90.policy@1",TestVersion:"VT-D7.authorization@1",OverrideVersion:"RK-D6.override@2",DetentionVersion:"RK-D3.detention@1",CopySource:"same_as_original",DesignResponsibility:"design_limits_and_delayed_correction",PolicyResponsibility:"known_risk_quota_test_and_suppression",ExecutionResponsibility:"cancelled_review_and_expanded_detention"},"不同版本与设计/政策/执行责任必须分别保留；副本不算独立证人。");return await Commit(s,"old_city_responsibilities_compared",[Change(s,State(s) with{ResponsibilitiesCompared=true}),BranchOperations.Change(s,"R5:responsibility_comparison","responsibility_comparison",c)],c);
    }
    public async ValueTask<CommandResult> ChooseReviewAsync(string procedure)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R5-S07.hearing");Require(State(s).ResponsibilitiesCompared&&Sources(s,"R5-Q503-R-HEARING")&&procedure is "emergency_public_redacted" or "protected_review","审查须有真实核验并保留反证机会。");return await Commit(s,"old_city_review_procedure_chosen",[Change(s,State(s) with{ReviewProcedure=procedure,ResponseParties=[]})],new{procedure,private_identities_released=false});
    }
    public async ValueTask<CommandResult> RecordResponseOpportunityAsync(string party)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R5-S07.hearing");var st=State(s);Require(!string.IsNullOrEmpty(st.ReviewProcedure)&&Sources(s,"R5-Q503-R-HEARING")&&party is "N-MARA" or "N-VOSS" or "N-ROOK","限定程序与既定三方反证渠道未成立。");var response=party switch{"N-MARA"=>new OldCityResponse(party,"OC-D18.design@1/OC-D30.correction@1","onsite_rescued_mara","acknowledged_design_limit_and_delayed_correction","testimony_additional_not_sole_evidence"),"N-VOSS"=>new(party,"VQ-D90.policy@1/VT-D7.authorization@1","bounded_executive_review_channel","policy_explanation_does_not_cancel_responsibility","known_risk_policy_and_test"),_=>new(party,"RK-D6.override@2/RK-D3.detention@1","station_executive_channel","protection_claim_recorded_with_source_limits","silence_or_claim_not_proof")};return await Commit(s,"old_city_response_opportunity_recorded",[Change(s,st with{ResponseParties=st.ResponseParties.Contains(party)?st.ResponseParties:st.ResponseParties.Add(party)}),BranchOperations.Change(s,"R5:response:"+party,"review_response",response)],response);
    }
    public async ValueTask<CommandResult> ChooseCustodyAsync(string mode,string publication,string vossRole)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R5-S07.custody");var st=State(s);Require(Closed(s,"Q-503")&&Sources(s,"R5-Q504-R-OPTIONS")&&!st.OriginalCasePreserved,"原件实际安装后需新受控迁移，不以重选复制。");Require(mode is "public_archive_bounded" or "joint_sealed"&&publication is "redacted_findings" or "redacted_findings_and_conditions"&&vossRole is "supervised_technical" or "suspended","临时保管、公开及技术岗位必须在已公开有限选项中。");var custody=new OldCityCustody(mode,publication,OriginalCase,mode=="joint_sealed"?["N-ADA","ROLE-R5-ARCHIVE-CUSTODIAN"]:["ROLE-R5-ARCHIVE-CUSTODIAN"],true,false);return await Commit(s,"old_city_custody_chosen",[Change(s,st with{Custody=custody,JointPermission=false,Supervisors=[],TowerCheckRevision=null}),BranchOperations.Change(s,"R5:voss_role","bounded_technical_role",new{actor="N-VOSS",role=vossRole,exclusive_authority=false,responsibility_preserved=true,final_policy="not_selected"})],custody);
    }
    public async ValueTask<CommandResult> AcceptSupervisionAsync(string actor)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R5-S07.custody");var st=State(s);Require(st.Custody is not null&&RequiredSupervisors.Contains(actor)&&Sources(s,"R5-Q504-R-OPTIONS"),"共同监督仅接受已公布实际当地三个岗位。");return await Commit(s,"old_city_supervision_accepted",[Change(s,st with{Supervisors=st.Supervisors.Contains(actor)?st.Supervisors:st.Supervisors.Add(actor)}),BranchOperations.Change(s,"R5:supervision:"+actor,"local_supervision",new{actor,accepted=true,scope="TWR-R5_local_emergency_only",six_region_signature=false})],new{actor,actual_local_acceptance=true,scope="TWR-R5_local_emergency_only"});
    }
    public async ValueTask<CommandResult> ConfigureTowerLoadAsync(OldCityTowerLoad load)
    {
        load.Validate();var s=await _repository.SnapshotAsync();Safe(s);At(s,"R5-S08.control");var st=State(s);Require(st.JointPermission&&st.ArchiveRelayIsolated&&Sources(s,"R5-Q504-R-TOWER"),"当次许可、真实隔离和公开负荷规程不足。");return await Commit(s,"old_city_load_configured",[Change(s,st with{Load=load,TowerRevision=st.TowerRevision+1,TowerCheckRevision=null,TowerReturnChecked=false})],new{load,previous_test_invalidated=true,world_policy="not_selected"});
    }
    public async ValueTask<CommandResult> VerifyCurrentTowerSafetyAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R5-S08.load_test");var st=State(s);Require(TowerSafe(s,st),"当前实际继电、隔离、机架、负荷、岗位、配置检查和返程未共同成立。");var proof=new TowerSafety("TWR-R5",true,"R5:actual_archive_and_load:"+st.TowerRevision);var result=await Commit(s,"tower_safety_verified",[],proof,"R5:tower_safety:"+st.TowerRevision);Require(result.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,"当前塔安全来源未提交。");var latest=await _repository.SnapshotAsync();var source=latest.Receipts.Single(r=>r.SemanticKey=="R5:tower_safety:"+st.TowerRevision&&r.ActionType=="tower_safety_verified");return await new WorkOrderService(_repository,_catalog).RegisterSafetyAsync(source.ReceiptId);
    }
}

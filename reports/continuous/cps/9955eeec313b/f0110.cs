using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.EventScheduler;
using Mist.Application.Storage;
using Mist.Application.WorkOrders;
using Mist.Core.ChapterTwo;
using Mist.Core.Engineering;
using Mist.Core.Quests;

namespace Mist.Application.ChapterTwo;

public sealed partial class ChapterTwoService
{
    public async ValueTask<CommandResult> SetDisclosureScopeAsync(string path,ImmutableArray<string> fields,string recipient,string purpose,string version)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R2-S02.delivery");Require(Closed(s,"Q-202")&&Closed(s,"Q-203"),"核查与现场安全工作尚未共同完成。");
        string[] publicFields=["public_fact","record_dates","source_versions","local_safety_boundary","historical_signature_uncertainty","mining_batch"];
        Require(path is "redacted_public_summary" or "entrusted_review","具名身份发布需要尚未制作的逐人实际同意来源，当前候选不可猜补。");
        Require(version=="R2-facts-v1"&&purpose=="verify_public_facts"&&recipient==(path=="redacted_public_summary"?"priory_public_board":"priory_review_desk")&&!fields.IsDefault&&fields.Distinct().Count()==fields.Length&&publicFields.All(fields.Contains)&&fields.All(publicFields.Contains),"必要公共字段、接收方、用途或版本不符；受保护身份不能混入。");
        var scope=new ChapterRecordScope("R2-facts",version,fields,recipient,purpose,false,false,"player_explicit_scope");return await Commit(s,"chapter_two_disclosure_scope_confirmed",[BranchOperations.Change(s,"R2:disclosure_scope","record_scope",scope)],new{path,scope,selection_is_not_delivery=true});
    }
    public async ValueTask<CommandResult> DecideMiraInvitationAsync(bool? join)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R2-S04.mira");Require(Closed(s,"Q-202"),"米拉的专业邀请时点尚未成立。");if(join is null)return await Commit(s,"chapter_two_mira_invitation_deferred",[],new{decision="defer",main_not_blocked=true});
        if(_repository is not IEventLeaseStore leases)throw new InvalidOperationException("正式邀请需要原子前景占用接口。");
        string owner="chapter2:mira:"+Guid.NewGuid();Require(await leases.TryLeasesAsync(_repository.Session.Load,owner,["input:foreground"]),"另一项前景工作尚未交接。");
        try{var roster=new Mist.Application.Roster.RosterService(_repository,_catalog);var invited=await roster.InviteAsync("CP-MIRA");Require(invited.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,"专业邀请未提交。");return await roster.DecideAsync("CP-MIRA",join.Value);}finally{await leases.ReleaseLeasesAsync(_repository.Session.Load,owner);}
    }
    public async ValueTask<CommandResult> DecideW04InvitationAsync(string decision)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R2-S04.mira");Require(Closed(s,"Q-204"),"私人工作邀请须在主线范围协议之后。");Require(decision is "accept" or "decline" or "defer","请明确私人邀请决定。");var changes=ImmutableArray.CreateBuilder<EntityChange>();changes.Add(Change(s,State(s) with{W04Invited=decision=="accept"}));
        if(decision=="accept"&&Quest(s,"CQ-MIRA-01") is{Lifecycle:QuestLifecycle.Unaccepted} q)changes.Add(BranchOperations.Change(s,"quest:CQ-MIRA-01","quest",q.Accept()));
        return await Commit(s,"chapter_two_w04_invitation_decided",changes.ToImmutable(),new{decision,formal_recruitment_required=false,main_not_blocked=true});
    }
    public async ValueTask<CommandResult> RequestW04ScopeAsync(ImmutableArray<string> fields,string recipient,string purpose,string version)
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R2-S03.w04");Require(State(s).W04Invited&&Quest(s,"CQ-MIRA-01") is{Lifecycle:QuestLifecycle.Active},"需要当前接受的私人工作邀请。");
        string[] allowed=["observation_time","witness_duty","version_source"];Require(version=="W04-v1"&&recipient=="priory_review_desk"&&purpose=="verify_local_note"&&!fields.IsDefaultOrEmpty&&fields.Distinct().Count()==fields.Length&&fields.All(allowed.Contains),"W-04本人没有同意请求的字段、接收方、用途或版本。");
        var scope=new ChapterRecordScope("W-04",version,fields,recipient,purpose,false,true,"W-04");return await Commit(s,"chapter_two_w04_individual_scope_issued",[Change(s,State(s) with{W04Scope=true}),BranchOperations.Change(s,"R2:w04_scope","record_scope",scope)],scope);
    }
    public async ValueTask<ChapterTwoPreview> PreviewSubmissionAsync(string quest)
    {
        var s=await _repository.SnapshotAsync();Safe(s);var st=State(s);var task=Package.WritingTasks.Single(x=>x.Quest==quest);var draft=await _repository.GetDraftAsync(task.Id);var stamp=new DraftStamp(task.Id,draft?.Id,draft?.Revision??0,draft?.Hash);var meaning=BranchOperations.Read<ChapterWorldMeaning>(s,"proposal:"+quest);var issues=ImmutableArray.CreateBuilder<RuleIssue>();
        void Check(bool okay,string code,string text){if(!okay)issues.Add(new(code,quest,text));}
        try{At(s,task.Spot);}catch(InvalidOperationException){Check(false,"wrong_stand","请到真实提交站位。");}
        Check(Quest(s,quest) is{Lifecycle:QuestLifecycle.Active},"quest_inactive","任务未开启或已结算。");Check(meaning is not null&&meaning.DraftHash==draft?.Hash,"draft_changed","当前稿件须明确确认意思。");
        bool Field(string k,string value)=>meaning?.Fields.GetValueOrDefault(k)==value;
        var changes=ImmutableArray.CreateBuilder<EntityChange>();object result=new{quest,language_assessment="human_pending",mara_current_status="unknown"};string action="chapter_two_report_received";
        switch(quest)
        {
            case "Q-201":
                Check(st.Landing&&st.Reception&&st.VisitA&&st.VisitB&&st.VisitC,"visits_missing","实际接应与三门廊自愿查验缺失。");
                Check(Sources(s,"R2-Q201-R-REQUEST","R2-Q201-R-RATION","R2-Q201-R-ROOMS","R2-Q201-R-OFFICIAL"),"source_missing","地方请求、口粮、安置与现行名册原始来源缺失。");
                foreach(var room in new[]{"A","B","C"}){var group=BranchOperations.Read<LocalResidentGroup>(s,"R2:residents:"+room);var seen=BranchOperations.Read<ResidenceObservation>(s,"R2:observation:"+room);Check(group is not null&&seen is not null&&group.LocalCheckConsent&&group.Scene=="R2-S03"&&group.Spot=="R2-S03.living_"+room.ToLowerInvariant()&&seen.Room==room&&seen.People.SequenceEqual(group.People),"residents_changed","住民实际位置/同意/观察需要重新核查。");}
                Check(Field("scope","local_group")&&Field("register_meaning","classification_not_current_presence")&&Field("motive","unknown"),"meaning_invalid","明确地方范围、分类与当前居住不同、动机未知。");
                changes.Add(Change(s,st with{BasicArchive=true,InspectionScope=true}));foreach(var next in new[]{"Q-202","Q-203"})if(Quest(s,next) is{Lifecycle:QuestLifecycle.Unaccepted} nq)changes.Add(BranchOperations.Change(s,"quest:"+next,"quest",nq.Accept()));
                changes.Add(BranchOperations.Change(s,"R2:basic_scope","record_scope",new ChapterRecordScope("public_priory_records","1",["room_codes","dates","duties","version_source"],"patrol_technician","local_discrepancy_check",false,false,"N-JUNE")));break;
            case "Q-202":
                Check(st.M12Verified&&st.D1Verified,"chain_missing","M-12排序与独立D−1导师交接核验缺失。");Check(Sources(s,"PZTIME-R-ORDER","PZTIME-R-ARRIVAL","R2-Q202-R-SENDING","R2-Q202-R-D1","R2-Q202-R-COHORT"),"sources_missing","当前完整转移与比较来源缺失。");
                Check(Field("mara_time_scope","D-1_only")&&Field("mara_current_status","unknown")&&Field("identity_accountability","separate_fields")&&Field("comparison","different_measures"),"meaning_invalid","保留D−1、当前未知、身份与责任字段区别、对照列的不同计量。");
                changes.Add(BranchOperations.Change(s,"finding:K-MARA-D1-ALIVE","verified_finding",new{fact="K-MARA-D1-ALIVE",source="E-03",time_scope="D-1_only",transfer="TR-D1-7",current_mara_status="unknown",basis="R2:d1_chain",source_roots=new[]{"r2-d1-sending-custody-original","r2-d1-receiving-custody-original"}}));break;
            case "Q-203":
                Check(new[]{"ENC-R2-01","ENC-R2-02","ENC-R2-03"}.All(id=>HasResult(s,id)),"independent_results_missing","三个独立处置结果尚未共同成立。");Check(st.UpperSafe&&st.LocalStable&&st.KitchenFeed&&st.TowerPermit&&st.TowerDuty&&st.TowerIsolation&&st.TowerReturn,"physical_checks_missing","当前地方试验、厨房验收与塔站现场检查缺失。");
                Check(Sources(s,"R2-Q203-R-OLD","R2-Q203-R-INTERLOCK","R2-Q203-R-PANEL","R2-Q203-R-TOWER"),"boundary_sources_missing","旧图、后加联锁、机房规程和塔站范围来源未取得。");
                Check(Field("power_scope","local_only")&&Field("tower_preparation","safe_not_converted")&&Field("load_configuration",st.Load.ToString()),"meaning_invalid","报告必须对应真实地方配置，塔站Safe不是改造完成。");
                var readiness=BranchOperations.Read<TowerReadiness>(s,"readiness:TWR-R2")??new TowerReadiness("TWR-R2",false,false,null,false,false,false,null);changes.Add(BranchOperations.Change(s,"readiness:TWR-R2","tower_readiness",readiness with{Safe=true}));result=new TowerSafety("TWR-R2",true,"R2:actual_three_results:local-config:"+st.ReserveRevision+":onsite_tower_checks");action="tower_safety_verified";break;
            case "Q-204":
                Check(Closed(s,"Q-202")&&Closed(s,"Q-203"),"prerequisite_missing","实际转移核验与当前供能/塔站安全都须完成。");Check(Sources(s,"R2-Q204-R-ESSAY","R2-Q204-R-SIGNATURE","R2-Q204-R-BATCH"),"handover_sources_missing","长文、获准历史页与批次来源未取得。");
                var disclosure=BranchOperations.Read<ChapterRecordScope>(s,"R2:disclosure_scope");Check(disclosure is{Record:"R2-facts",Version:"R2-facts-v1",IdentityPublication:false}&&BranchOperations.Entity(s,"R2:basic_scope") is not null,"scope_missing","需要明确当前脱敏公共或受托范围与原稿查验路径。");
                Check(Field("mara_current_status","unknown")&&Field("signature_scope","historical_needs_verification")&&Field("public_scrutiny","preserved")&&Field("mining_direction","MC-2"),"meaning_invalid","保留当前未知、历史责任范围待核验、公共核查路径与下一批次方向。");
                if(disclosure is not null){changes.Add(BranchOperations.Change(s,"R2:actual_handover","archive_handover",new{scope=disclosure,public_facts=new{local_cohort_only=true,dated_transfer="D-1_only",local_station_only=true,tower_current_safe=true,historical_signature="scope_needs_verification",mining_batch="MC-2"},protected_original_custodian="N-JUNE",review_route="controlled_source_recheck",receipt_not_review_completion=true}));}
                changes.Add(BranchOperations.Change(s,"finding:K-MARA-SIGNATURE","verified_finding",new{fact="K-MARA-SIGNATURE",source="E-08-preliminary",time_scope="historical_signature",responsibility_scope="needs_verification",mara_current_status="unknown"}));changes.Add(BranchOperations.Change(s,"commission:next-inquiry","permission",new{allowed=true,region="R3",purpose="verify_MC-2_original_instruments_and_acceptance",basis="Q-204:resolution",arrival_occurred=false}));action="chapter_two_archive_handover_received";break;
            case "CQ-MIRA-01":
                var w04=BranchOperations.Read<ChapterRecordScope>(s,"R2:w04_scope");Check(st.W04Invited&&st.W04Scope&&Sources(s,"R2-CQMIRA01-R-W04")&&w04 is{Recipient:"priory_review_desk",Purpose:"verify_local_note",Version:"W04-v1",IndividualConsent:true}&&new[]{"observation_time","witness_duty","version_source"}.All(w04.Fields.Contains),"individual_scope_missing","私人邀请、本人完整必要字段同意与材料未取得。");
                Check(Field("path","limited_copy")||Field("path","custodian_relay"),"path_missing","明确选择限定副本或保管查验。");Check(Field("recipient","priory_review_desk")&&Field("purpose","verify_local_note")&&Field("version","W04-v1")&&Field("address","excluded"),"scope_invalid","W-04字段、接收方、用途、版本或私人地址限制不符。");
                changes.Add(BranchOperations.Change(s,"R2:w04_handover","individual_handover",new{fields=new[]{"observation_time","witness_duty","version_source"},recipient="priory_review_desk",purpose="verify_local_note",version="W04-v1",method=meaning?.Fields.GetValueOrDefault("path"),private_address_disclosed=false}));action="chapter_two_w04_handover_received";break;
            default:throw new InvalidOperationException("本候选没有该收束任务，未注册可选场景不能猜奖励。");
        }
        if(issues.Count>0)return new(Guid.Empty,false,issues.ToImmutable(),task.Id,draft?.Hash,meaning?.StructuredAssistance??false);
        changes.Add(BranchOperations.Change(s,"quest:"+quest,"quest",Quest(s,quest)!.Close(QuestOutcome.Completed)));
        if((quest=="Q-202"&&Closed(s,"Q-203"))||(quest=="Q-203"&&Closed(s,"Q-202")))changes.Add(BranchOperations.Change(s,"quest:Q-204","quest",Quest(s,"Q-204")!.Accept()));
        int[] reward=quest=="CQ-MIRA-01"?[25,20,0]:quest switch{"Q-201"=>[220,60,1],"Q-202"=>[260,80,1],"Q-203"=>[260,80,1],"Q-204"=>[280,100,2],_=>throw new InvalidOperationException()};
        if(quest!="CQ-MIRA-01"){var canonical=_catalog.Quests.Single(q=>q.Id==quest).Reward.Bundle;Require(canonical.Experience==reward[0]&&canonical.Marks==reward[1]&&canonical.Parts==reward[2],"原始奖励与运行目录不一致，不能猜补。");}
        var command=BranchOperations.Plan(_repository,s,quest+":resolution",action,GateChanges(s,changes.ToImmutable()),Reads(s),result,balances:[new("experience",reward[0]),new("marks",reward[1]),new("parts",reward[2])],draft:stamp);Guid token=Guid.NewGuid();_previews[token]=command;return new(token,true,[],task.Id,draft?.Hash,meaning!.StructuredAssistance);
    }
}

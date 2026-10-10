using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Application.WorkOrders;
using Mist.Core.ChapterTwo;
using Mist.Core.ChapterThree;
using Mist.Core.Engineering;
using Mist.Core.Quests;
using Mist.Core.Exploration;
using Mist.Application.Travel;

namespace Mist.Application.ChapterThree;

public sealed partial class ChapterThreeService
{
    public string SubmissionSpot(string quest)=>quest switch{"Q-301"=>"R3-S02.pera","Q-302"=>"R3-S03.comparison","Q-303"=>"R3-S08.delivery","Q-304"=>"R3-S09.handover","CQ-OREN-01"=>"R3-S03.alarm_check","CQ-OREN-02"=>"R3-S03.alarm_review",_=>throw new InvalidOperationException("委托尚未注册提交合同。")};
    public async ValueTask<CommandResult> RegisterCurrentReturnAnchorAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);var a=Actor(s);var st=State(s);string spot=a.Scene switch{"R3-S01"when st.Landing=>"R3-S01.berth","R3-S02"when st.Commission=>"R3-S02.pera","R3-S09"when st.TowerCurrent&&BranchOperations.Read<TowerReadiness>(s,"readiness:TWR-R3") is{Safe:true}=>"R3-S09.return_check",_=>throw new InvalidOperationException("这里不是当前已实际成立的安全返程锚点。")};At(s,spot);var p=CurrentGeometry(s)[a.Scene].Spots.Single(x=>x.Id==spot).Stand;var proof=await Commit(s,"return_anchor_verified",[],new ReturnAnchor(a.Scene,p,a.Scene=="R3-S09"?ReturnAnchorKind.TowerSafeExit:ReturnAnchorKind.Settlement,""));Require(proof.Status==CommitStatus.Committed&&proof.Receipt is not null,"实际安全锚点来源未提交。");return await new TravelService(_repository,_catalog,CurrentGeometry(await _repository.SnapshotAsync())).RegisterAnchorAsync(proof.Receipt!.Id.Value.ToString());
    }
    public async ValueTask<CommandResult> VerifyTowerSafetyAsync()
    {
        var s=await _repository.SnapshotAsync();Safe(s);At(s,"R3-S09.load_test");var st=State(s);Require(st.TowerCurrent&&HasResult(s,"ENC-R3-05")&&Closed(s,"Q-302")&&Closed(s,"Q-303"),"当前供能测试、返程值守、机械制止或任务前置尚未共同成立。");var old=BranchOperations.Read<TowerReadiness>(s,"readiness:TWR-R3")??new TowerReadiness("TWR-R3",false,false,null,false,false,false,null);return await Commit(s,"tower_safety_verified",[BranchOperations.Change(s,"readiness:TWR-R3","tower_readiness",old with{Safe=true})],new TowerSafety("TWR-R3",true,"R3:actual_machine_and_load:"+st.Configuration),"R3:tower_safe:"+st.Configuration);
    }
    public async ValueTask<ChapterThreePreview> PreviewSubmissionAsync(string quest)
    {
        var s=await _repository.SnapshotAsync();Safe(s);var st=State(s);var task=Package.WritingTasks.Single(x=>x.Quest==quest);var draft=await _repository.GetDraftAsync(task.Id);var stamp=new DraftStamp(task.Id,draft?.Id,draft?.Revision??0,draft?.Hash);var meaning=BranchOperations.Read<ChapterWorldMeaning>(s,"proposal:"+quest);var issues=ImmutableArray.CreateBuilder<RuleIssue>();void Check(bool value,string code,string text){if(!value)issues.Add(new(code,quest,text));}
        try{At(s,SubmissionSpot(quest));}catch(InvalidOperationException){Check(false,"wrong_stand","请到实际接收站位 "+SubmissionSpot(quest)+"；正文工作台和正式接收点分别核验。");}Check(Quest(s,quest) is{Lifecycle:QuestLifecycle.Active},"quest_inactive","委托未开启或已经结算。");Check(meaning is not null&&meaning.DraftHash==draft?.Hash,"draft_changed","当前正文须明确确认意思，修改后旧确认失效。");bool Field(string k,string value)=>meaning?.Fields.GetValueOrDefault(k)==value;
        Check(QuestSources(s,quest),"source_missing","当前必要原始材料版本尚未实际取得；听力脚本不冒充已听成品音轨。");var changes=ImmutableArray.CreateBuilder<EntityChange>();object result=new{quest,language_assessment="human_pending",mara_current_status="unknown"};string action="chapter_three_report_received";
        switch(quest)
        {
            case "Q-301":
                Check(st.Landing&&st.Commission&&st.LadderSafe&&st.MineRouteSafe&&st.BasicDelivered&&st.WorkersReceived&&st.FieldInstrument,"physical_rescue_missing","独立梯道、当前排水、三人护送、基本安置与实际仪表缺失。");Check(new[]{"ENC-R3-01","ENC-R3-02","ENC-R3-04"}.All(id=>HasResult(s,id)),"rescue_domains_missing","轨道、泵房与护送独立结果未共同成立。");Check(new[]{"W1","W2","W3"}.All(id=>BranchOperations.Read<MiningWorker>(s,"R3:worker:"+id) is{Status:"received",Scene:"R3-S02",Spot:"R3-S02.basic_handover"}),"actual_workers_missing","三名实际工人尚未到场接收。");Check(Field("return_route","independent_ladder")&&Field("workers","three_received")&&Field("basic_guarantee","local_delivered")&&Field("instrument","original_retained")&&Field("death_claim","not_inferred"),"meaning_invalid","书面说明须对应实际返程、接收、基本保障与原件，不把空白返程栏写成死亡。");
                if(Quest(s,"Q-303") is{Lifecycle:QuestLifecycle.Unaccepted} next)changes.Add(BranchOperations.Change(s,"quest:Q-303","quest",next.Accept()));break;
            case "Q-302":
                Check(Closed(s,"Q-301")&&st.Comparison&&st.Accountability&&st.FieldInstrument,"comparison_missing","核查可与救援并行，但正式收束还须实际Q301救援仪表与三方核验。");var comparison=BranchOperations.Read<MiningBatchComparison>(s,"R3:comparison");Check(comparison is{Batch:"MC-2",RawVersion:"MC-2.instrument.I@1",CopyVersion:"MC-2.retained.I@1",AcceptanceVersion:"MC-2.acceptance@3",CopyIndependent:false,OrenResponsibility:"unrepaired_bypass_accepted",FullCausality:"unproven"},"versions_changed","原始仪表、留存副本、正式签章业务版本不符。");Check(Field("incomplete_checks","3_of_24")&&Field("formal_accepted","23_of_24")&&Field("comparison","different_measures")&&Field("oren_responsibility","unrepaired_bypass_accepted")&&Field("causality","unproven")&&Field("mara_current_status","unknown"),"meaning_invalid","24份记录的未完成和合格列意义不同，奥伦责任固定，全部事故因果及导师当前状态未知。");changes.Add(BranchOperations.Change(s,"finding:R3-OREN-ACCEPTANCE","verified_finding",new{scope="unrepaired_bypass_accepted",batch="MC-2",raw_version="MC-2.instrument.I@1",copy_version="MC-2.retained.I@1",acceptance_version="MC-2.acceptance@3",copy_is_independent=false,source_roots=Package.Materials.Where(m=>new[]{"R3-Q302-R-RAW","R3-Q302-R-COPY","R3-Q302-R-SIGNED"}.Contains(m.Id)).Select(m=>m.SourceRoot).ToArray(),oren_confession_required=false,current_repair_does_not_erase_original=true,full_causality="unproven",mara_current_status="unknown"}));break;
            case "Q-303":
                Check(Closed(s,"Q-301")&&st.BasicDelivered&&st.WorkersReceived&&st.Allocation is not null&&st.ExtraDelivered&&st.CurrentLoadAllowed,"provisions_missing","基本保障、真实六件工包交付或受限值守供能未成立。");Check(s.Items.Count(i=>i.Id.StartsWith("R3-local:work_kit:",StringComparison.Ordinal)&&i.OwnerKind=="installed")==6,"actual_delivery_missing","六件地方额外工包须实际完成交付，草案不能充库存。");Check(Field("policy",st.Policy?.Mode??"")&&Field("shelter",st.Allocation?.Shelter.ToString()??"")&&Field("retrofit",st.Allocation?.Retrofit.ToString()??"")&&Field("production",st.Allocation?.Production.ToString()??"")&&Field("basic_priority","preserved")&&Field("unlimited_extraction","rejected"),"meaning_invalid","论述须对应实际分配与早降载/有限当班过渡，不批准无期限超采。");result=new{quest,allocation=st.Allocation,policy=st.Policy,actual_extra_delivered=6,basic_guarantee_preserved=true,language_assessment="human_pending"};break;
            case "Q-304":
                Check(Closed(s,"Q-302")&&Closed(s,"Q-303")&&st.TowerCurrent&&st.SamplePacked&&HasResult(s,"ENC-R3-05"),"tower_handover_missing","核查、民生方案、当前塔站检查、实际主机结果或封样未共同成立。");Check(BranchOperations.Read<TowerReadiness>(s,"readiness:TWR-R3") is{Safe:true}&&s.Receipts.Any(r=>r.ActionType=="tower_safety_verified"&&r.ResultJson.Contains("R3:actual_machine_and_load:"+st.Configuration,StringComparison.Ordinal)),"safety_source_missing","须明确提交与当前配置相同的实际塔站安全核验。");Check(Field("tower","Safe_only")&&Field("supply","bounded_current")&&Field("handover","sealed_sample_and_source_versions")&&Field("causality","association_requires_verification")&&Field("mara_current_status","unknown"),"meaning_invalid","保留当前安全而非改造、受限供给、合法封样与源版本，关联须由R4继续核验。");
                var versions=Package.Materials.Where(m=>Acquired(s,m)&&m.Quest is "Q-302" or "Q-304").ToImmutableDictionary(m=>m.Id,m=>m.Version+":"+m.Fingerprint).Add("MC-2.original-instrument","MC-2.instrument.I@1").Add("MC-2.workshop-copy","MC-2.retained.I@1").Add("MC-2.formal-acceptance","MC-2.acceptance@3");var handover=new MiningTowerHandover("TWR-R3","R4","MC-2","R3-MC2-SAMPLE-01",versions,st.Configuration,"R4-public-verification-desk","verify_materials_and_signatory_scope","association_requires_verification","unknown");result=handover;changes.Add(BranchOperations.Change(s,"R3:actual_handover","mining_tower_handover",handover));changes.Add(BranchOperations.Change(s,"commission:next-inquiry","permission",new{allowed=true,region="R4",purpose="verify_materials_and_signatory_scope",basis="Q-304:resolution",arrival_occurred=false}));action="chapter_three_tower_handover_received";break;
            case "CQ-OREN-01":
                Check(Closed(s,"Q-302")&&st.AlarmInvitation=="accept"&&st.AlarmInspected&&st.AlarmPromise,"auxiliary_scope_missing","实际个人邀请、报警器检查与具体旁修承诺未成立。");Check(Field("scope","auxiliary_alarm_only")&&Field("responsibility","preserved")&&Field("promise","repair_and_independent_review"),"meaning_invalid","承诺需限定报警器及独立复核，不抹去验收责任。");action="chapter_three_auxiliary_promise_received";break;
            case "CQ-OREN-02":
                Check(Closed(s,"Q-304")&&Closed(s,"CQ-OREN-01")&&st.AlarmInvitation=="accept"&&st.AlarmRepair&&st.AlarmReviewed&&st.AlarmReviewRevision==st.AlarmRevision,"independent_review_missing","实际独立维修及当前另一人复核缺失。");Check(BranchOperations.Entity(s,"workpost:CP-OREN") is{Kind:"released_post"}&&s.Items.Single(i=>i.Id=="R3-local:aux_alarm_kit:1") is{OwnerKind:"installed",OwnerId:"R3:auxiliary_alarm"},"work_handover_missing","实际独立件安装与奥伦岗位交接缺失。");Check(Field("reviewer","N-PERA")&&Field("repair","own_auxiliary_kit")&&Field("history","original_signature_preserved")&&Field("forgiveness","not_required"),"meaning_invalid","独立复核和旁修件用途须对应现场，不要求工人谅解。");action="chapter_three_auxiliary_review_received";break;
            default:throw new InvalidOperationException("CQ-OREN-03仅Q504以后，本候选可选合同未注册，不推定任务闭合。");
        }
        if(issues.Count>0)return new(Guid.Empty,false,issues.ToImmutable(),task.Id,draft?.Hash,meaning?.StructuredAssistance??false);
        changes.Add(BranchOperations.Change(s,"quest:"+quest,"quest",Quest(s,quest)!.Close(QuestOutcome.Completed)));
        if((quest=="Q-302"&&Closed(s,"Q-303"))||(quest=="Q-303"&&Closed(s,"Q-302")))changes.Add(BranchOperations.Change(s,"quest:Q-304","quest",Quest(s,"Q-304")!.Accept()));
        if((quest=="CQ-OREN-01"&&Closed(s,"Q-304"))||(quest=="Q-304"&&Closed(s,"CQ-OREN-01")))if(st.AlarmInvitation=="accept"&&Quest(s,"CQ-OREN-02") is{Lifecycle:QuestLifecycle.Unaccepted} cq)changes.Add(BranchOperations.Change(s,"quest:CQ-OREN-02","quest",cq.Accept()));
        int[] expected=quest switch{"Q-301"=>[420,100,1],"Q-302"=>[440,100,2],"Q-303"=>[500,120,2],"Q-304"=>[560,160,3],"CQ-OREN-01"=>[25,20,1],"CQ-OREN-02"=>[35,30,1],_=>throw new InvalidOperationException()};
        ImmutableArray<ItemChange> items=[];
        if(quest.StartsWith("CQ-OREN-",StringComparison.Ordinal))
        {
            // Part09 personal-node budgets are separate from the part04 main-quest catalog.
            // Check the shipped source entry as well as the fixed original NPC reward budget.
            Require(_catalog.Definitions.TryGetValue("personal_nodes",out var nodes),"个人节点原件目录缺失，不能推定奖励。");
            var node=nodes.Single(x=>x.Id==quest);using var budget=System.Text.Json.JsonDocument.Parse(node.PayloadJson);var b=budget.RootElement;
            Require(b.GetProperty("chapter_budget").GetInt32()==3&&b.GetProperty("companion").GetString()=="CP-OREN"&&b.GetProperty("xp").GetInt32()==expected[0]&&b.GetProperty("marks").GetInt64()==expected[1]&&b.GetProperty("parts").GetInt32()==expected[2],"个人节点原件预算与本次结算不一致，禁止猜补。");
            if(quest=="CQ-OREN-02")changes.Add(BranchOperations.Change(s,"R3:oren_toolframe","souvenir",new{actor="CP-OREN",source_node=quest,name="修复的旧工具框",decorative_only=true,combat_bonus=0,actual_independent_review=State(s).AlarmReviewRevision}));
        }
        else
        {
            var canonical=_catalog.Quests.Single(q=>q.Id==quest).Reward;Require(canonical.Bundle.Experience==expected[0]&&canonical.Bundle.Marks==expected[1]&&canonical.Bundle.Parts==expected[2],"原件与运行目录奖励不一致，禁止猜补。");
            items=canonical.Items.Select(id=>new ItemChange("R3:"+quest+":original:"+id,id,"new","","player","hero",null)).ToImmutableArray();
        }
        var c=BranchOperations.Plan(_repository,s,quest+":resolution",action,GateChanges(s,changes.ToImmutable()),Reads(s),result,items:items,balances:[new("experience",expected[0]),new("marks",expected[1]),new("parts",expected[2])],draft:stamp);Guid token=Guid.NewGuid();_previews[token]=c;return new(token,true,[],task.Id,draft?.Hash,meaning!.StructuredAssistance);
    }
}


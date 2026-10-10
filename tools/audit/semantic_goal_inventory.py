"""Read source bytes and named tests; never execute gameplay or certify 96 budget slots.

The IDs below are author-review grouping IDs, not invented baseline registrations.
Normal-path labels describe the selected C# case's source; observed results are separate.
"""
from __future__ import annotations
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
BUDGET = dict(R0=5, R1=9, R2=8, R3=9, R4=9, R5=8, R6=10, RF=6)
PACKAGES = {"R0":"campaign/first_entry.json", "R1":"campaign/first_entry.json",
            "R2":"chapter_two/learner_package.json", "R3":"chapter_three/learner_package.json",
            "R4":"chapter_four/learner_package.json", "R5":"chapter_five/learner_package.json",
            "R6":"chapter_six/learner_package.json", "RF":"finale/learner_package.json"}
NORMAL = {
 "R0":("Campaign/CampaignCases.cs", "Normal campaign hands the same actor and unique ferry devices to the existing physical Q102 transaction"),
 "R1":("ChapterOne/ChapterOneCases.cs", "Normal zero-XP rescue, commission, ferry, finding and tower handover persist continuously without fixtures"),
 "R2":("ChapterTwo/ChapterTwoCases.cs", "Continuous actual normal zero-XP voyage reaches all four R2 tasks without positive fixtures"),
 "R3":("ChapterThree/ChapterThreeCases.cs", "Continuous normal zero-XP through actual R2-R3 voyage and all four mining tasks uses no positive fixture"),
 "R4":("ChapterFour/ChapterFourCases.cs", "Continuous normal zero-XP voyage reaches all R4 tasks with no positive fixtures or inferred current mentor status"),
 "R5":("ChapterFive/ChapterFiveCases.cs", "Actual zero-XP mainline completes R5 with one vessel, one original case, alive mentor and bounded R6 commission"),
 "R6":("ChapterSix/ChapterSixCases.cs", "Normal 0 XP through six onsite baseline producers seals real checkpoint and launches one vessel atomically to RF"),
 "RF":("Finale/FinaleCases.cs", "Actual zero-XP finale "),
}
# region | meaningful goal | actual source IDs | application files | public producers | observable boundary
MAIN_ROWS = r"""
R0|避开失效舱道，实际加固后撤到岸线并选择可收人的落脚方向|QP01-R-ROUTE,QP01-R-LOG,QP02-R-TIDE|Campaign/CampaignService.cs|ChooseLandingAsync,ExecuteStationAsync|reinforce_cabin_path/evacuate_to_shore；保留同一救援对象
R0|按隔离、线缆、绳索和当前测试完成救援绞盘，再移交幸存者|QP02-R-WINCH|Campaign/CampaignService.cs|ExecuteStationAsync|当前绞盘检查与真实 sheltered/registered 状态；读材料不代替操作
R0|区分两次事件的时间与更正，向实际接收方向发送有限求援|QP02-L-CORRECTION,QP02-R-TIDE|Campaign/CampaignService.cs|ConfirmTimelineAsync,ConfirmRescueMeaningAsync,PreviewRescueMessageAsync,ConfirmSubmissionAsync|Q-P02:resolution；不把历史更正写成新事故
R0|实际制止中继入口威胁后修复当前供电，取得同一记录器和邮件|QP03-R-RELAY,QP03-R-RECORDER,QP03-R-MAIL,QP03-R-PICKUP|Campaign/CampaignService.cs,FirstApproaches/FirstApproachService.cs|BeginAsync,ExecuteStationAsync|Q-P03:resolution；DEFENCE只作设备前置，不另算战斗谜题
R0|用闭合证据、实际港口到达与哈维尔有限权限申请航道检修|Q101-R-ARRIVAL,Q101-R-CREDENTIAL,Q101-R-CLOSURE,Q101-R-EXCEPTION|Campaign/CampaignService.cs|ConfirmHarbourMeaningAsync,PreviewHarbourRequestAsync,ConfirmSubmissionAsync|Q-101:resolution 与 permission:Q-102，不提前发渡船物件
R1|组合A关闭、B1.2m与船1.4m及C现导航灯条件选择合法航线|PZ102-R-CHANNEL|Q102/Q102Service.cs|ConfirmMeaningAsync,PreviewDepartureAsync|选择C且当前检验有效；A/B不被自动改写
R1|工坊模式与双向危险决定支架取得和内闩安全返回|PZ102-R-WORKSHOP|Q102/Q102Service.cs|WorkshopModeAsync,PickBracketAsync,OpenLatchAsync,EnterPortAsync|三方法同一ACCESS单位；内闩与供电模式分开
R1|实际安装唯一支架/电池，当前配置试验失效后必须复测|PZ102-R-BEACON|Q102/Q102Service.cs|InstallBracketAsync,ConnectBatteryAsync,TestBeaconAsync|当前DeviceCertificate；连接顺序多解不增单位
R1|保存与确认同一航行说明，把有效安全结论交给实际渡船|PZ102-R-CHANNEL|Q102/Q102Service.cs|ConfirmMeaningAsync,PreviewDepartureAsync,DepartAsync|Q-102:resolution；草稿、取消和旧load不执行
R1|原事故记录的日期、版本和实际保管来源支持有限事件链|Q103-R-REPORT,Q103-R-ORDER|ChapterOne/ChapterOneService.cs|CollectAsync,AcceptInvestigationAsync|当前原文访问与独立来源，不改旧件
R1|向调查接收方区分制度责任、执行记录和未知更高意图|Q103-R-REPORT,Q103-R-ORDER|ChapterOne/ChapterOneService.cs|ConfirmFindingMeaningAsync,PreviewFindingAsync,ConfirmPreviewAsync|Q-103:resolution；保存正文不产生裁决
R1|用侧闸隔离支架零能或外闸制止取得同一塔基入口|R1-APP-R-SIDE,R1-APP-R-OUTER|FirstApproaches/FirstApproachService.cs,ChapterOne/ChapterOneService.Tidegate.cs|PerformSideStepAsync,BeginAsync,AcceptTowerWorkAsync|ENC-R1-02/03二选一当前typed结果；两方法同一入口单位
R1|核对地方保障，实际塔战控制后检查当前负荷与返程|Q104-R-SUPPLY,Q104-R-TOWER|ChapterOne/ChapterOneService.Tidegate.cs|InspectLocalSupplyAsync,BeginTowerAsync,VerifyTowerAfterControlAsync|当前塔安全记录；击败不直接交船
R1|在真实泊位交接塔工作和同一艘微光号产权|Q104-R-HANDOVER|ChapterOne/ChapterOneService.Tidegate.cs|PreviewHandoverAsync,ConfirmPreviewAsync|Q-104:resolution 与唯一 ship:glimmer，招聘独立
R2|核对官方名单与三处当事人同意的实际居住人数差别|R2-Q201-R-OFFICIAL,R2-Q201-R-ROOMS,R2-Q201-R-RATION|ChapterTwo/ChapterTwoService.Stations.cs,ChapterTwo/ChapterTwoService.Submissions.cs|ExecuteStationAsync,PreviewSubmissionAsync|Q-201:resolution；三门廊为一个人数比较目标
R2|从离开、抵达与抄录记录建立偏序，签发准确分钟保持未知|PZTIME-R-ORDER,PZTIME-R-ARRIVAL|ChapterTwo/ChapterTwoService.Stations.cs|ConfirmM12Async|R2:m12_order；同源副本不算第二证人
R2|有限字段许可与两端原件验证D-1交接身份，当前导师状态未知|R2-Q202-R-ACCESS,R2-Q202-R-SENDING,R2-Q202-R-D1|ChapterTwo/ChapterTwoService.Stations.cs,ChapterTwo/ChapterTwoService.Submissions.cs|RequestRecordScopeAsync,VerifyD1ChainAsync,PreviewSubmissionAsync|Q-202:resolution；D-1事实不升级为今日存活
R2|独立根道处置、输送器隔离护罩夹具与排水构成当前上层安全|R2-Q203-R-WATER,R2-Q203-R-INTERLOCK|ChapterTwo/ChapterTwoService.Stations.cs,ChapterTwo/ChapterTwoService.Encounters.cs|PerformConveyorStepAsync,ExecuteStationAsync|StairSafe/UpperSafe；各设备子步不再额外数单元
R2|观察两支路后选择采样、机械或战斗完成同一核心处置|R2-Q203-R-OLD,R2-Q203-R-PANEL|ChapterTwo/ChapterTwoService.Encounters.cs|ChooseCoreMethodAsync,PerformCoreStepAsync,BeginEncounterAsync|R2:ENC-R2-03:result；多方法不复制结果
R2|把唯一地方接头装入独立备用源，选择允许负荷并实测厨房供给|R2-Q203-R-PANEL,R2-Q203-R-INTERLOCK|ChapterTwo/ChapterTwoService.Stations.cs|SetLocalLoadAsync,ExecuteStationAsync|LocalStable/KitchenFeed当前修订，塔站出口不是允许负荷
R2|当地工作许可后检查塔站值守、隔离和真实回程，报告实际安全|R2-Q203-R-TOWER|ChapterTwo/ChapterTwoService.Stations.cs,ChapterTwo/ChapterTwoService.Submissions.cs|ExecuteStationAsync,PreviewSubmissionAsync|Q-203:resolution；Safe与Baseline/增强转换分开
R2|对历史签章和批次作限定范围交接，不宣布完整因果或当前导师|R2-Q204-R-SIGNATURE,R2-Q204-R-BATCH,R2-Q204-R-ESSAY|ChapterTwo/ChapterTwoService.Stations.cs,ChapterTwo/ChapterTwoService.Submissions.cs|RequestRecordScopeAsync,PreviewSubmissionAsync|Q-204:resolution 和实际MC-2后续委托
R3|在升降机不安全时检查并保留独立梯道及轨道救援入口|R3-Q301-R-LADDER,R3-Q301-R-RAIL,R3-Q301-L-ROUTE|ChapterThree/ChapterThreeService.Stations.cs|ExecuteStationAsync|LadderSafe/EntryInstructions及当前轨道结果
R3|实际隔离泵与排水、复查水位，新泵配置使旧通路证明失效|R3-Q301-R-PUMP|ChapterThree/ChapterThreeService.Stations.cs|SetPumpModeAsync,ExecuteStationAsync|MineRouteSafe当前检查；独立梯道仍保留
R3|找到三名工人、分别解困，经过护送路线和地方接收才发救援报告|R3-Q301-R-RESCUE,R3-Q301-R-BASIC,R3-Q301-L-WORKER|ChapterThree/ChapterThreeService.Stations.cs,ChapterThree/ChapterThreeService.Submissions.cs|RescueWorkerAsync,ExecuteStationAsync,PreviewSubmissionAsync|Q-301:resolution；三人唯一实际received，非一按钮到账
R3|区分原始仪表、工坊副本和签章的版本/责任，不用奥伦口供替代|R3-Q302-R-RAW,R3-Q302-R-COPY,R3-Q302-R-SIGNED,R3-Q302-R-BATCHES|ChapterThree/ChapterThreeService.Stations.cs|RequestBatchRecordScopeAsync,CompareBatchAsync,ConfirmAccountabilityAsync|Q-302:resolution；复制独立性false与完整因果unproven
R3|按有限额外保障和三处需求分配实际六套工作包并交付|R3-Q303-R-DEMAND,R3-Q303-R-STOCK,R3-Q303-R-CHOICES|ChapterThree/ChapterThreeService.Stations.cs|AllocatePackagesAsync,ExecuteStationAsync|MiningAllocation；基本保障不被挪用，多方案满足公开约束
R3|选择及时降载或单班有限过渡，真实两值守岗和交班约束负荷|R3-Q303-R-CHOICES,R3-Q303-L-MEETING|ChapterThree/ChapterThreeService.Stations.cs,ChapterThree/ChapterThreeService.Submissions.cs|AppointDutyAsync,ConfirmLoadPolicyAsync,CloseTransitionShiftAsync,PreviewSubmissionAsync|Q-303:resolution；交班归零使旧塔测试失效
R3|仪表工作通过实际隔离支架零能或战斗，修复货运升降并检验|R3-Q302-R-RAW,R3-Q301-R-SHIFT|ChapterThree/ChapterThreeService.Encounters.cs,ChapterThree/ChapterThreeService.Stations.cs|PerformInstrumentStepAsync,ExecuteStationAsync|ENC-R3-03同一仪表安全工作；不是明显拾取另计
R3|先停进料并支架，再处理抽取机和当前受限塔负荷/值守/返程|R3-Q304-R-SEQUENCE,R3-Q304-R-LOAD,R3-Q304-R-DUTY|ChapterThree/ChapterThreeService.Stations.cs,ChapterThree/ChapterThreeService.Submissions.cs|ConfigureTowerLoadAsync,ExecuteStationAsync,VerifyTowerSafetyAsync|TWR-R3 Safe当前检查，不预发二级校准
R3|封装实际单一样品，把安全结果和未知边界交给验证委托|R3-Q304-R-HANDOVER,R3-Q304-R-REQUEST,R3-Q304-L-TRANSFER|ChapterThree/ChapterThreeService.Stations.cs,ChapterThree/ChapterThreeService.Submissions.cs|RegisterCurrentReturnAnchorAsync,PreviewSubmissionAsync|Q-304:resolution 与 MiningTowerHandover；样品不是无限库存
R4|对独立现场调查和图表的角色/计数差异作限定概览|R4-Q401-R-SURVEY-A,R4-Q401-R-SURVEY-B,R4-Q401-R-VISUAL|ChapterFour/ChapterFourService.Stations.cs|CompareFieldSourcesAsync|当前FieldSourceComparison；合并来源不凭多数投票
R4|核对手稿、正式报告和流转版本的编辑范围，不补未知批准动机|R4-Q401-R-MANUSCRIPT,R4-Q401-R-ISSUED,R4-Q401-R-CIRCULATION|ChapterFour/ChapterFourService.Stations.cs,ChapterFour/ChapterFourService.Submissions.cs|RequestReportScopeAsync,CompareReportVersionsAsync,PreviewSubmissionAsync|Q-401:resolution；只拿当前具体字段范围
R4|隔离旧试台、接好独立备用与护罩后重建真实A试验|R4-Q402-R-SAFETY,R4-Q402-R-RECONSTRUCT|ChapterFour/ChapterFourService.Experiments.cs,ChapterFour/ChapterFourService.Stations.cs|PrepareOldBenchStepAsync,PerformReserveStepAsync,ConfigureControllerAAsync|实际试台准备，不把失效草稿当试验
R4|选择只变一个因素的A成对试验，真实五/十分钟观察保留失败|PZLAB-R-PROTOCOL|ChapterFour/ChapterFourService.Experiments.cs|StartExperimentAsync,ObserveExperimentAsync,CompareTrialPairAsync|多对合法比較同一目标；55未观察不能造结果
R4|由失败与校准通道记录区分E/C并提交有限故障总结|R4-Q402-R-CALIBRATION,R4-Q402-L-TECHNICIAN|ChapterFour/ChapterFourService.Stations.cs,ChapterFour/ChapterFourService.Submissions.cs|InspectCalibrationChannelAsync,PreviewSubmissionAsync|Q-402:resolution；假设非结论
R4|实际孤岛隔离备用护罩及两个岗位、负荷与接触范围配置|R4-Q403-R-ISLAND|ChapterFour/ChapterFourService.Stations.cs|ConfigureIslandAsync,AssignTrialDutyAsync,SetIslandContactAsync|IslandConfiguration当前核查与独立返程
R4|准备真实TEST输出，保留C反例后观测有边界的稳定试验|R4-Q403-R-OUTPUT,R4-Q403-L-OBSERVER|ChapterFour/ChapterFourService.Stations.cs,ChapterFour/ChapterFourService.Experiments.cs,ChapterFour/ChapterFourService.Submissions.cs|PrepareOutputStepAsync,StartExperimentAsync,ObserveExperimentAsync,PreviewSubmissionAsync|Q-403:resolution；reset只使当前证书失效，失败历史保留
R4|取得棱镜塔许可后核实当前受限负荷、安全和值守返程|R4-Q404-R-TOWER|ChapterFour/ChapterFourService.Stations.cs|SetPrismLoadAsync,ExecuteStationAsync|TWR-R4 Safe不作全塔转换，实际出口安全
R4|有限设施资料核对北站地点/供给/守卫并交接五项后续准备|R4-Q404-R-LOCATION,R4-Q404-R-SUPPLY,R4-Q404-R-GUARD,R4-Q404-R-PREPARATION|ChapterFour/ChapterFourService.Stations.cs,ChapterFour/ChapterFourService.Submissions.cs|RequestFacilityScopeAsync,VerifyNorthStationAsync,ExchangeVossAsync,PreviewSubmissionAsync|Q-404:resolution；导师当前unknown，PolicyDecision not_selected
R5|历史图层不授通行权，凭当前凭证或独立隧道实际进入值守厅|R5-Q501-R-MAP,R5-Q501-R-CREDENTIAL,R5-Q501-R-TUNNEL|ChapterFive/ChapterFiveService.Stations.cs|CompareMapAsync,ExecuteStationAsync|current route实际通行；同端点异通路ID保留
R5|向接收方报告当前路线权限仅到值守厅、未登记居民并非失忆|R5-Q501-R-STREET,R5-Q501-R-ESSAY,R5-Q501-L-DIRECTIONS|ChapterFive/ChapterFiveService.Submissions.cs|PreviewSubmissionAsync|Q-501:resolution；通信限定范围，非感谢重复计数
R5|有限救援范围核对当前看守/门/返程，并实际隔离清除拘留设备|R5-Q502-R-DUTY,R5-Q502-R-RESCUE|ChapterFive/ChapterFiveService.Stations.cs|RequestRescueScopeAsync,ExecuteStationAsync|当前门可查验；守卫结果不直接打开牢房
R5|真实开门看见活着的玛拉，打开内撤路线护送并实际接收|R5-Q502-R-RESCUE,R5-Q502-L-RECEPTION|ChapterFive/ChapterFiveService.Stations.cs,ChapterFive/ChapterFiveService.Submissions.cs|ExecuteStationAsync,PreviewSubmissionAsync|Q-502:resolution；旧D-1不重写，实际rescued_alive_directly_verified
R5|按七原件及同源副本区分玛拉设计、沃斯政策、鲁克执行责任|R5-Q503-R-DESIGN,R5-Q503-R-REVISION,R5-Q503-R-POLICY,R5-Q503-R-EXECUTION|ChapterFive/ChapterFiveService.Stations.cs|RequestResponsibilityScopeAsync,CompareResponsibilitiesAsync|三个责任范围并存，不互相免责
R5|保护或公开删节审查均提供真实回应机会，沉默不是有罪证据|R5-Q503-R-HEARING,R5-Q503-L-HEARING|ChapterFive/ChapterFiveService.Stations.cs,ChapterFive/ChapterFiveService.Submissions.cs|ChooseReviewAsync,RecordResponseOpportunityAsync,PreviewSubmissionAsync|Q-503:resolution；公开隐私范围有限
R5|暂时保管与公开边界有多解，真实操作/居民/档案三方接受监督|R5-Q504-R-OPTIONS,R5-Q504-L-COUNCIL|ChapterFive/ChapterFiveService.Stations.cs|ChooseCustodyAsync,AcceptSupervisionAsync|当前Custody/Supervision；不预选终局政策
R5|档案中继与货架当前隔离负荷测试保留唯一原案，交接R6准备边界|R5-Q504-R-TOWER,R5-Q504-R-TRANSFER|ChapterFive/ChapterFiveService.Stations.cs,ChapterFive/ChapterFiveService.Submissions.cs|ConfigureTowerLoadAsync,VerifyCurrentTowerSafetyAsync,PreviewSubmissionAsync|Q-504:resolution 与 OldCityHandover；Safe不冒充六塔准备
R6|现天气窗口下选择背风标记廊道、实际标记并保留撤回方向|R6-Q601-R-FORECAST,R6-Q601-R-BRIEFING|ChapterSix/ChapterSixService.Stations.cs|ChooseCorridorAsync,PerformAsync|当前survey corridor；坏天气不是随机丢失主线事实
R6|仪表隔离后设置当前Z/0/R/10并实测，报告过载放大非全部雾因果|R6-Q601-R-INSTRUMENT,R6-Q601-R-RISK,R6-Q601-L-SURVEY|ChapterSix/ChapterSixService.Stations.cs,ChapterSix/ChapterSixService.Submissions.cs|ConfigureSurveyAsync,PerformAsync,PreviewSubmissionAsync|Q-601:resolution；配置修改使旧证明失效
R6|六地当前合同、真实人员与有限serials匹配采购交付，不凭账面发料|R6-Q602-R-CONTRACTS,R6-Q602-R-WORKLIST|ChapterSix/ChapterSixService.Engineering.cs|InspectFacilityAsync,RegisterFacilityTeamAsync,InspectProcurementStepAsync|独立工程来源范围/实物唯一权；六次同模板不硬数六谜题
R6|六地现场运料安装当前基线，检查实际工作而非Requirements即准备|R6-Q602-R-STEPS,R6-Q602-R-OVERVIEW|ChapterSix/ChapterSixService.Engineering.cs|RegisterCurrentFacilitySafetyAsync,ReserveWorkAsync,ExecuteWorkStepAsync|六真实Baseline工作回执；父任务汇总不重复奖励
R6|验证有限技术包和增强/过渡/现场试验/代表签章的独立资格边界|R6-Q602-R-TECHNICAL,R6-Q602-R-PROTOCOL,R6-Q602-R-PREPARATION|ChapterSix/ChapterSixService.Engineering.cs,ChapterSix/ChapterSixService.Submissions.cs|VerifyTechnicalPackageAsync,GrantLocalTrialAsync,FieldTrialStepAsync,SignRegionalProtocolAsync,PreviewSubmissionAsync|Q-602:resolution；增强为主任务合法准备分支而非强制所有结局
R6|核对真实有限货载serials、两个护送岗与上个安全段退回方案|R6-Q603-R-MANIFEST,R6-Q603-R-SEGMENTS|ChapterSix/ChapterSixService.Stations.cs|PlanCargoAsync|FrontierCargoState当前manifest；不借私人额外补给
R6|护送沿真实分段行动并断现场输入、外刹车、零能保护当前货载|R6-Q603-R-PLATFORM,R6-Q603-L-ESCORT|ChapterSix/ChapterSixService.Stations.cs|ControlFieldDeviceAsync,PerformAsync|真实cargo stage/Protection；普通敌人不额外计谜题
R6|当地实际接收四件基础包与当前台站隔离、返程和负荷检验|R6-Q603-R-LOCAL,R6-Q603-R-LOAD,R6-Q603-L-RECEIPT|ChapterSix/ChapterSixService.Stations.cs,ChapterSix/ChapterSixService.Submissions.cs|PerformAsync,PreviewSubmissionAsync|Q-603:resolution；真实received/SafelyDeposited
R6|汇总当前六塔和合法船产权，确认准备方向而未选最终政策|R6-Q604-R-QUALIFICATION,R6-Q604-R-DEPARTURE|ChapterSix/ChapterSixService.Stations.cs|ConfirmPreparationDirectionAsync,PerformAsync|当前出发qualification；没有假导师证词/结局门
R6|当前到场承诺真实交接或暂缓、货载托管后封准备并原子出发|R6-Q604-R-COMMITMENTS,R6-Q604-L-HANDOVER|ChapterSix/ChapterSixService.Stations.cs,ChapterSix/FinaleLaunchService.cs|VerifyRiskRegisterAsync,SealPreparationAsync,PreviewAsync,ConfirmAsync|Q-604:resolution；checkpoint、实际RF到达、同一船、未预选政策
RF|在正式交班与实际检修轴中选择有权限的真实入口路线|RF-QF01-R-SCOPE,RF-QF01-R-AUTHORITY,RF-QF01-R-CORRIDOR,RF-QF01-R-SHAFT,RF-QF01-R-COIL|Finale/FinaleService.Stations.cs,Finale/FinaleService.Submissions.cs|FormalHandoverAsync,ExecuteStationAsync,PreviewSubmissionAsync|Q-F01:resolution；两路线一个进入目标，不授机组safe
RF|处理鲁克两个当地指挥点，实际审计看管与仍运行循环的不同状态|RF-QF02-R-COMMAND,RF-QF02-R-AUDIT,RF-QF02-L-ROOK|Finale/FinaleService.Encounters.cs,Finale/FinaleService.Stations.cs,Finale/FinaleService.Submissions.cs|PreviewEncounterActionAsync,ExecuteStationAsync,PreviewSubmissionAsync|Q-F02:resolution；alive custody与主机运行分开
RF|当前层两个隔离器实际配置与复查，不把本层隔离当全网关机|RF-QF03-R-ISOLATION,RF-QF03-R-RESTART|Finale/FinaleService.Stations.cs|ConfigureLayerIsolationAsync,ExecuteStationAsync|LayerReady当前版本；新配置过期旧核验
RF|在同一次三段资源池处置联结、续启和阻尼，当前批准真实受控停止|RF-QF03-R-LINK,RF-QF03-R-RENEWAL,RF-QF03-R-DAMPING,RF-QF03-R-CLOCK|Finale/FinaleService.Encounters.cs,Finale/FinaleService.Stations.cs,Finale/FinaleService.Submissions.cs|PreviewEncounterActionAsync,RecordCentralSafetyAsync,PreviewSubmissionAsync|Q-F03:resolution；每阶段/增援/重试均不另数单位
RF|按当前机组稳定、六塔实际准备及同一产权逐条核实三分支资格|RF-QF04-R-OPTIONS|Finale/FinaleService.Stations.cs,ReleaseReadiness/WorldPreparationProjection.cs,ReleaseReadiness/FinalReadinessService.cs|VerifyFinalControlAsync,PreviewAsync|当前final eligibility矩阵；选项本身不补世界准备
RF|以保存稿与明确意思确认合法终局指令，最后预览再原子执行|RF-QF04-R-COMMAND,RF-QF04-L-DISPATCH|Finale/FinaleService.Submissions.cs,ReleaseReadiness/FinalReadinessService.cs|ConfirmPolicyAsync,PreviewPolicyAsync,ConfirmAsync|Q-F04:resolution；Q-F05回顾不另加谜题
"""

def file_info(relative: str) -> dict:
    p = ROOT / relative
    if not p.is_file() and Path(str(p)+".pending").is_file(): p = Path(str(p)+".pending")
    if not p.is_file(): return {"path":relative,"exists":False}
    b=p.read_bytes()
    return {"path":p.relative_to(ROOT).as_posix(),"exists":True,"sha256":hashlib.sha256(b).hexdigest(),"bytes":len(b),"pending_file":p.name.endswith('.pending')}

def add_test(relative: str, name: str, normal_kind: str) -> dict:
    f=file_info("tests/Mist.Tests/"+relative)
    t=(ROOT/f["path"]).read_text(encoding="utf-8-sig") if f["exists"] else ""
    found=bool(name) and name in t
    if relative=='OptionalEncounterContracts/OptionalEncounterCases.cs':
        found=bool(re.fullmatch(r'normal_zero_XP_R[1-6]_both_independent_optional_contracts_actual_control_inspection_CAS_and_no_rewards',name)) and '"normal_zero_XP_"+region+"_both_independent_optional_contracts_actual_control_inspection_CAS_and_no_rewards"' in t
    expanded=[name] if found else []
    if relative=='Finale/FinaleCases.cs' and name=='Actual zero-XP finale ':
        expanded=[f'Actual zero-XP finale {style} keeps 28-level resources and closes {choice} once' for style,choice in [('H','Central'),('M','Island'),('S','Distributed'),('Mixed','Central')]]
    if relative=='LinaPersonalWorks/LinaPersonalWorkCases.cs' and name=='actual_zero_XP_Lina_three_producers_':
        expanded=['actual_zero_XP_Lina_three_producers_'+choice+'_once_original_rewards_current_permission_and_private_custody' for choice in ['继续通信','双方同意后见面','明确暂缓联系并保存转交方式']]
    return {**f,"name_or_registered_prefix":name,"registered_name_expansions":expanded,"literal_or_exact_registered_expansion_in_source":found,"literal_in_source":found,"route_kind":normal_kind,
            "observed_execution":"not_inferred_from_test_source"}

def goal(region, number, title, package, materials, app, methods, boundary, tests, kind="main"):
    paths=[file_info("src/Mist.Application/"+p) for p in app.split(',')]
    code='\n'.join((ROOT/p['path']).read_text(encoding='utf-8-sig') for p in paths if p['exists'])
    return {"id":f"AUTHOR-GOAL-{region}-{'M' if kind=='main' else 'O'}{number:02d}","region":region,"kind":kind,
            "goal":title,"public_source":file_info("content/source/"+package),"material_ids":materials.split(',') if materials else [],
            "production_files":paths,"producers":[{"method_or_action":m,"present_in_selected_files":m in code} for m in methods.split(',')],
            "observable_result":boundary,"named_cases":tests,"implementation_claim":"source_candidate_with_real_producer" if all(p['exists'] for p in paths) else "missing_producer",
            "human_language_review":"pending","unit_grouping":"new author-review grouping, not frozen baseline unit registration",
            "no_extra_reward_or_learning_credit_from_this_inventory":True}

def main_goals():
    counts={r:0 for r in BUDGET}; result=[]
    for line in MAIN_ROWS.strip().splitlines():
        region,title,ids,app,methods,boundary=line.split('|');counts[region]+=1
        p=PACKAGES[region]
        if region=='R1' and counts[region]<=4:
            p='part10-Q102_Material_Reference_v0.1.json'
            ids={1:'Q102-R-NOTICE,Q102-R-B,Q102-R-VESSEL',2:'Q102-R-BATTERY',3:'Q102-R-BATTERY',4:'Q102-R-NOTICE,Q102-L-REVISION'}[counts[region]]
            if counts[region] in (2,3):
                p='part06-Puzzle_Materials_v0.1.json'
                ids={2:'PZ102-R-WORKSHOP',3:'PZ102-R-PANEL'}[counts[region]]
        elif region=='R1' and counts[region] in (5,6,8,9):
            p='chapter_one/chapter_one.json'
            ids={5:'Q103-R-NOTICE,Q103-R-TEST,Q103-R-DUTY,Q103-R-CLOCK',6:'Q103-R-NOTICE,Q103-R-TEST,Q103-R-DUTY',8:'Q104-R-CONTROLS',9:'Q104-R-HANDOVER'}[counts[region]]
            if counts[region]==9:app+=',ChapterOne/ChapterOneService.cs'
        elif region=='R1':p='first_approaches/learner_package.json'
        test_file,test_name=NORMAL[region]
        result.append(goal(region,counts[region],title,p,ids,app,methods,boundary,[add_test(test_file,test_name,'actual_zero_xp_normal_path')]))
    assert counts==BUDGET,counts
    # These are communication goals with a distinct receiver/scope, but their boundary
    # must be reviewed against the physical work; never certify an independent unit merely by counting a submit method.
    for g in result:
        if g['id'] in {'AUTHOR-GOAL-R1-M04','AUTHOR-GOAL-R1-M06','AUTHOR-GOAL-R3-M09','AUTHOR-GOAL-R5-M02','AUTHOR-GOAL-RF-M05','AUTHOR-GOAL-RF-M06'}:
            g['grouping_caution']='Review whether receiver/scope decision adds an independent relation; merge if play shows only duplicate settlement. Producer exists; this is not a missing-code claim.'
        if g['id']=='AUTHOR-GOAL-R6-M05':
            g['named_cases'].append(add_test('ChapterSix/ChapterSixCases.cs','Normal finite enhanced source chain and all six local permissions, tests and distinct signatures prepare distributed direction','actual_zero_xp_normal_path'))
        if g['id']=='AUTHOR-GOAL-R3-M07':
            g['branch_limit']='Normal main helper controls instrument battle; manual instrument branch must not be called separately verified from this full-route case.'
    return result

def optional_goals():
    rows=[]; counts={r:0 for r in BUDGET}
    def add(region,title,package,ids,app,methods,boundary,test_file,name,normal='prior_source_fixture_not_normal_completion'):
        counts[region]+=1;rows.append(goal(region,counts[region],title,package,ids,app,methods,boundary,[add_test(test_file,name,normal)],'optional'))
    package=json.loads((ROOT/'content/source/optional_encounter_contracts/learner_package.json').read_text(encoding='utf-8-sig'))
    for c in package['contracts']:
        region=c['region'];add(region,'接受当前局部合同并实际控制、检查 '+c['target'],'optional_encounter_contracts/learner_package.json',c['material'],
          'OptionalEncounterContracts/OptionalEncounterService.cs','AcceptContractAsync,BeginAsync,PreviewActionAsync,InspectTargetAsync',
          'optional-contract:'+c['id']+':result；permission/current instance/source/target严格绑定，0XP/0marks/0parts',
          'OptionalEncounterContracts/OptionalEncounterCases.cs','normal_zero_XP_'+region+'_both_independent_optional_contracts_actual_control_inspection_CAS_and_no_rewards','actual_zero_xp_normal_path')
    entries=[
      ('R1','识别同订单的改期副本、两组实际客人与两房条件','r1_life/learner_package.json','R1-ROOM-R-OLD,R1-ROOM-R-AMEND,R1-ROOM-R-SEPARATE,R1-ROOM-R-CARDS','R1Life/R1LifeService.cs','ConfirmArrivedGuestsAsync,InspectRoomAsync,ArrangeRoomsAsync,PreviewSubmissionAsync','SC-R1-001:resolution','R1Life/R1LifeCases.cs','actual_zero_XP_prologue_registration_then_optional_inn_produces_one_25XP_result_without_mainline_changes','actual_zero_xp_normal_path'),
      ('R1','用当前自有灯具及真实演员/道具/时间组织完整或小型演出','r1_life/learner_package.json','R1-SHOW-R-OFFER,R1-SHOW-R-EQUIPMENT,R1-SHOW-L-CREW,R1-SHOW-R-STAGE','R1Life/R1LifeService.cs','LampActionAsync,PlacePropAsync,AssignRolesAsync,PreviewSubmissionAsync','SC-R1-002:resolution；两个方案一个工作','R1Life/R1LifeCases.cs','actual_theatre_','prior_source_fixture_not_normal_completion'),
      ('R2','本人当前稿许可下保留通信意图而不承诺见面/公开地址','r2_life/learner_contract.json','R2-O01-R-LETTER','R2Life/R2LifeService.cs','RequestLetterScopeAsync,RequestAuthorReviewAsync,PreviewSubmissionAsync','当前稿逐版许可与一次实际邮务/保留结果','R2Life/R2LifeCases.cs','private_reply_','prior_source_fixture_not_normal_completion'),
      ('R2','六实际床位在两种种植取舍中协商批准并逐床种植','r2_life/learner_contract.json','R2-O02-R-SEEDS','R2Life/R2LifeService.cs','InspectBedAsync,SetLayoutAsync,RequestGrowerApprovalAsync,PlantBedAsync','六真实种植回执，条件估算不是现成收成','R2Life/R2LifeCases.cs','six_bed_','prior_source_fixture_not_normal_completion'),
      ('R3','主人确认适合轮件后修同一个音乐盒并限度归还','chapter_three/learner_package.json','R3-O01-R-MUSIC','OptionalLocalWorks/MusicBoxRepairService.cs,OptionalLocalWorks/MusicBoxRepairService.Note.cs','SelectWheelAsync,RequestOwnerApprovalAsync,FitApprovedWheelAsync,CheckTurningAsync,PreviewDeliveryAsync','同一盒/轮/许可/草稿的实际归还','OptionalLocalWorks/MusicBoxRepairCases.cs','Normal zero-XP prior chain, owner-approved actual same-box repair and private note settle once without main or six-tower consequences','actual_zero_xp_normal_path'),
      ('R3','沿独立检查的侧井通路作公共对照，条件不足不作因果裁决','chapter_three/learner_package.json','R3-O02-R-OBSERVE','OptionalLocalWorks/SideWellObservationService.cs','InspectEntryAsync,RegisterEntrySafetyAsync,RecordWalkedRouteAsync,PreviewComparisonAsync','实际侧井到达/本地比較一次，0奖励','OptionalLocalWorks/SideWellObservationCases.cs','Actual inspected paired travel, original public readings and bounded local comparison register once without main evidence, extra sample, balances or items','actual_zero_xp_normal_path'),
      ('R3','两固定岗位与时间冲突下重排班表或请求当班可用新帮助','r3_shift_work/learner_package.json','','R3ShiftWork/R3ShiftWorkService.cs','RequestAvailabilityAsync,PreviewPostAsync,ReceiveShiftAsync,CheckHandoverAsync,CompleteAsync','同一班板实际两方接收/旧岗释放，无奖励','R3ShiftWork/R3ShiftWorkCases.cs','Actual normal shift reassignment receives the same board and releases both posts once with zero rewards','actual_zero_xp_normal_path'),
      ('R4','比较课程公告后交付同一询问纸并领取有限答复','chapter_four/learner_package.json','R4-O01-R-NOTICES','ChapterFour/StudyInquiryService.cs,ChapterFour/StudyInquiryService.Previews.cs','CompareNoticesAsync,PreviewAsync,TakeReplyAsync,AcknowledgeReplyAsync','同纸/本次答复，无招生或名额保证','ChapterFour/StudyInquiryCases.cs','Study normal zero-XP source preserved notices actual one-sheet delivery reply and zero reward','actual_zero_xp_normal_path'),
      ('R4','实际贡献范围决定更正信用卡，安装后现场检查','chapter_four/learner_package.json','R4-O02-R-CONTRIBUTION','ChapterFour/ExhibitionCreditService.cs,ChapterFour/ExhibitionCreditService.Previews.cs','CompareAsync,MountCardAsync,InspectMountedAsync,PreviewAsync','一次同卡现场交接，不占主线结果','ChapterFour/ExhibitionCreditCases.cs','Exhibition normal zero-XP origin actual public source comparison card mount and receipt once without reward','actual_zero_xp_normal_path'),
      ('R5','居民协商下选择保留两方公共记忆的地方路牌并装同一牌','optional_local_works/old_city_life_contract.json','SC-R5-SIGN','OptionalLocalWorks/OldCityLocalLifeService.cs','ConsultResidentsAsync,ChooseSignAsync,InstallChosenSignAsync,CheckVisibleSignAsync','牌文字/current原件/实物安装绑定','OptionalLocalWorks/OldCityLocalLifeCases.cs','Actual zero-XP local consultation permits either remembered name, installs one same inspected board and visible second line, and leaves the unused board and main history exact','actual_zero_xp_normal_path'),
      ('R5','据物件描述和本人限定封装许可核对原遗失物归还，不读家庭私信','optional_local_works/old_city_life_contract.json','SC-R5-LOST','OptionalLocalWorks/OldCityLocalLifeService.Property.cs','RequestPropertyScopeAsync,ComparePotClaimAsync,DecideWrapperPermissionAsync,CheckMementoTitleAsync,ReturnPotAsync,ReturnSealedMementoAsync,CorrectPrivateAssociationAsync','不同序列不替代同物，家庭纪念关联更正不动主案','OptionalLocalWorks/OldCityLocalLifeCases.cs','Uncertain local pot descriptions stay pending, different serials and family-content access refuse; current wrapper permission supports same object return without touching real evidence','actual_zero_xp_normal_path'),
      ('R6','比较当前与南部旧预测，额外地方有限包实际运送接收','optional_local_works/settlement_support_contracts.json','','OptionalLocalWorks/SettlementSupportService.cs','CompareForecastsAsync,InspectExtraStockAsync,DispatchPacketAsync,PreviewPacketDeliveryAsync','同一新地方运输，禁止借已保护主货载','OptionalLocalWorks/SettlementSupportCases.cs','Normal current-stage and earlier southern forecasts require exact finite optional batch and actual new delivery route; preview cancel and once receipt preserve main world','actual_zero_xp_normal_path'),
      ('R6','独立地方岗位与专用手动储备令共用压机安全零能交班','optional_local_works/settlement_support_contracts.json','','OptionalLocalWorks/SettlementSupportService.Press.cs','RequestPressPermissionAsync,InspectManualReserveAsync,VerifyPressZeroAsync,PreviewPressResponsibilityAsync','同机/许可/当前DRIVE零能，撤权使旧预览失效','OptionalLocalWorks/SettlementSupportCases.cs','Real shared press requires separate local posts, dedicated manual reserve and current DRIVE zero; permission withdrawal and Continue reject stale handovers','actual_zero_xp_normal_path'),
      ('R6','本人有限许可借灯清理后重新测零并原灯归还','optional_local_works/settlement_support_contracts.json','','OptionalLocalWorks/SettlementSupportService.Lamp.cs','RequestLampPermissionAsync,ServiceSameLampAsync,CarrySameLampAsync,OwnerReceiveLampAsync','不取得家信/原产权保留/新清理使旧零检查失效','OptionalLocalWorks/SettlementSupportCases.cs','Same borrowed lamp cannot expose family note, use another serial or reuse old zero after cleaning; actual return and bounded mutual aid keep original title once','actual_zero_xp_normal_path'),
      ('R6','当前辅中继断输入/专用销/AUX零能或战斗后实际重装复测','optional_local_works/field_relay_contract.json','','OptionalLocalWorks/FieldRelayRepairService.cs,OptionalLocalWorks/FieldRelayRepairService.Encounters.cs','SetAuxSwitchAsync,FitDedicatedPinAsync,VerifyZeroAsync,RefitSameRelayAsync,TestReturnAsync,CompleteAsync','ENC-R6-03-METHOD为原32main表附可选方法领域，不冒充新增可选槽；一地方工作','OptionalLocalWorks/FieldRelayRepairCases.cs','A current same-device OFF, dedicated pin and AUX zero method controls the optional relay without attacking, then requires refit and a new current return check','actual_zero_xp_normal_path'),
    ]
    for e in entries:add(*e)
    # Personal nodes are inventoried individually because each has its own work result.
    # Whether their communication is an independent puzzle is not inferred from its reward.
    for node,ids,title,methods in [
      ('01','LI01-APPENDIX,LI01-INDEX','本人范围对照旧回执，不据旧记录推知弟弟当前住址','InviteAsync,VerifyTransferAsync'),
      ('02','LI02-RELAY,LI02-DIRECT,LI02-DELIVERY','本人当前稿与通信路线许可后真实寄出有限内容','InviteAsync,ConfirmLetterMeaningAsync,DispatchLetterAsync'),
      ('03','LI03-REPLY,LI03-MEETING,LI03-TOPICS','收到当前回信后区分通信与双方见面许可及公开话题','VerifyReplyAsync,ConfirmCurrentContactAsync,DecidePublicTopicAsync')]:
        add('R1','莉娜CQ-LINA-'+node+'：'+title,'lina_personal/learner_package.json',ids,'LinaPersonalWorks/LinaPersonalWorkService.cs',methods,'当前个人节点同一实例的actual工作来源；本人目的独立','LinaPersonalWorks/LinaPersonalWorkCases.cs','actual_zero_XP_Lina_three_producers_','actual_zero_xp_normal_path')
    personal=[
      ('R2','CQ-MIRA-01','角色计数和有限W04副本范围','chapter_two/learner_package.json','R2-CQMIRA01-R-W04','ChapterTwo/ChapterTwoService.Submissions.cs','DecideW04InvitationAsync,RequestW04ScopeAsync,PreviewSubmissionAsync','ChapterTwo/ChapterTwoCases.cs','Individual W04 scope rejects a private address and completes either actual limited-copy path once'),
      ('R4','CQ-MIRA-02','当前角色数复核请求的派生副本实际交付','chapter_four/learner_package.json','R4-CQMIRA02-R-REQUEST','ChapterFour/ChapterFourService.Submissions.cs','AcceptMiraReviewAsync,GenerateReviewCopyAsync,PreviewSubmissionAsync','ChapterFour/ChapterFourCases.cs','CQ-MIRA02 requires actual prior limited handoff, private acceptance and derived delivery, without recruitment'),
      ('R5','CQ-MIRA-03','四步访问退出纠正练习及两人接受当前安排','chapter_five/learner_package.json','R5-CQMIRA03-R-EXERCISE','ChapterFive/ChapterFiveService.Personal.cs','BeginPersonalAccessExerciseAsync,PerformPersonalAccessStepAsync,RecordPersonalArrangementDecisionAsync','ChapterFive/ChapterFiveCases.cs','Both personal record participants must accept after actual four-step exercise'),
      ('R3','CQ-OREN-01','当前本人邀请下限定辅助警报修复承诺、责任不删','chapter_three/learner_package.json','R3-CQOREN01-R-AUX','ChapterThree/ChapterThreeService.Auxiliary.cs','DecideAlarmInvitationAsync,CommitAuxiliaryScopeAsync','ChapterThree/ChapterThreeCases.cs','Non-recruited Oren repairs own auxiliary kit under an actual work post and another person reviews without absolution'),
      ('R3','CQ-OREN-02','独立料实际警报修复并由另一人现场复核','chapter_three/learner_package.json','R3-CQOREN02-R-PARTS,R3-CQOREN02-L-REPAIR','ChapterThree/ChapterThreeService.Auxiliary.cs','PerformAuxiliaryRepairStepAsync,InviteIndependentAlarmReviewAsync,ConfirmIndependentAlarmReviewAsync','ChapterThree/ChapterThreeCases.cs','Non-recruited Oren repairs own auxiliary kit under an actual work post and another person reviews without absolution'),
      ('R5','CQ-OREN-03','不与战斗重叠的当地或远程岗位及实际交班','chapter_five/learner_package.json','R5-CQOREN03-R-DUTY','ChapterFive/ChapterFiveService.Personal.cs','ArrangeOrenDutyAsync,HandoffOrenDutyAsync','ChapterFive/ChapterFiveCases.cs',"Oren's accepted remote or local post cannot overlap battle and hands off without deleting responsibility"),
      ('R6','CQ-TESS-01','两本人当前联系边界而非真实地址公开','chapter_six/learner_package.json','R6-CQTESS01-R-CONTACTS','ChapterSix/ChapterSixService.Tess.cs,OptionalLocalWorks/TessLocalReceiptBridge.cs','ConfirmTessContactsAsync,ConfirmRecordAsync','OptionalLocalWorks/TessLocalReceiptBridgeCases.cs','Normal original five-box two-trip '),
      ('R6','CQ-TESS-02','五箱两趟的两可行方案及实际当班补充运输','chapter_six/learner_package.json','R6-CQTESS02-R-TRIPS','ChapterSix/ChapterSixService.Tess.cs,OptionalLocalWorks/TessLocalReceiptBridge.cs','PlanSupplementAsync,DeliverSupplementTripAsync,ConfirmRecordAsync','OptionalLocalWorks/TessLocalReceiptBridgeCases.cs','Actual uncommitted transport and two-contact same-version notice project completed private nodes while main receipts and all local stock stay unchanged'),
      ('R6','CQ-TESS-03','本人当前联系人许可下实际送有限公告','chapter_six/learner_package.json','R6-CQTESS03-R-NOTICE','ChapterSix/ChapterSixService.Tess.cs,OptionalLocalWorks/TessLocalReceiptBridge.cs','PrepareTessNoticeAsync,SendTessNoticeAsync,ConfirmRecordAsync','OptionalLocalWorks/TessLocalReceiptBridgeCases.cs','Actual uncommitted transport and two-contact same-version notice project completed private nodes while main receipts and all local stock stay unchanged'),
    ]
    for region,node,title,p,ids,app,methods,tf,tn in personal:
        add(region,node+'：'+title,p,ids,app,methods,node+':resolution；当前邀请和实际工作来源',tf,tn,'actual_zero_xp_normal_path' if region=='R6' else 'prior_source_fixture_not_normal_completion')
        if node.startswith(('CQ-MIRA-','CQ-OREN-')):
            current=rows[-1]
            current['named_cases'].append(add_test('ScopedPersonalWorks/ScopedPersonalWorkCases.cs',
                'Actual zero XP source chain delivers same paper then performs truthful consent exit correction and remote independent replacement once',
                'actual_zero_xp_normal_path'))
            details={
                'CQ-MIRA-02':('ScopedPersonalWorks/ScopedPersonalWorksService.cs','MakeReviewPaperAsync,PreviewReviewPaperAsync'),
                'CQ-MIRA-03':('ScopedPersonalWorks/ScopedPersonalWorksService.Mira.cs','RequestMiraRenewalAnswerAsync,MakeMiraDrillCopiesAsync,AttemptMiraReadAsync,WithdrawFutureMiraContactAsync,VerifyMiraExitAsync,AppendMiraCorrectionAsync'),
                'CQ-OREN-03':('ScopedPersonalWorks/ScopedPersonalWorksService.Oren.cs','BeginActualOrenWorkAsync,CheckOrenDeviceAsync,ReceiveOrenInstructionsAsync,PrepareActualOrenHandoverAsync'),
            }
            if node in details:
                detail_path,detail_methods=details[node];record=file_info('src/Mist.Application/'+detail_path)
                text=(ROOT/record['path']).read_text(encoding='utf-8-sig') if record['exists'] else ''
                current['production_files'].append(record)
                current['producers'].extend({'method_or_action':method,'present_in_selected_files':method in text} for method in detail_methods.split(','))
                current['supplementary_public_sources']=[file_info('content/source/scoped_personal_work/learner_package.json')]
            current['normal_source_case_note']='New actual zero-XP mainline through Q504 and real same-vessel return to R2/R3/R4 completes all six original Mira/Oren nodes. Test source exists; final observed pass requires Root current suite. Older fixture cases remain separate engineering coverage.'
    assert len(rows)==39,len(rows)
    return rows

def generate():
    m=main_goals();o=optional_goals()
    prior_native={
      ('R1','用当前自有灯具'):('reports/build/20261010T051036090105Z/r1_life_client_results.json',['actual_340XP_entry_no_fixture_history','receipt_SC-R1-002_once','finite_local_props_actual_install_no_main_item_payment']),
      ('R2','本人当前稿'):('reports/build/20261010T054835636932Z/r2_life_client_results.json',['actual_900XP_unique_vessel_no_fixture_entry','receipt_R2-O01_once','actual_short_ack_custody_current_hash_and_no_visit_publication']),
      ('R2','六实际床位'):('reports/build/20261010T054835636932Z/r2_life_client_results.json',['actual_900XP_unique_vessel_no_fixture_entry','receipt_R2-O02_once','six_actual_bed_records_bound_to_current_grower_approval'])}
    for g in o:
        for (region,prefix),(relative,required_checks) in prior_native.items():
            if g['region']!=region or not g['goal'].startswith(prefix):continue
            info=file_info(relative)
            if info['exists']:
                native=json.loads((ROOT/relative).read_text(encoding='utf-8-sig'))
                checks={c['name']:c['passed'] for c in native['checks']}
                g['observed_prior_normal_native']={**info,'required_checks':required_checks,'failures':native.get('failures'),
                  'normal_completion_observed':native.get('failures')==0 and all(checks.get(c) is True for c in required_checks),
                  'scope':native.get('source'),'current_tree_claim':False,'limit':'Actual earlier normal-native engineering run; current source/executable compatibility needs final Root manifest. C# prior-source fixture cases retain their separate boundary.'}
    missing=[]
    for g in m+o:
        for p in [g['public_source']]+g['production_files']:
            if not p['exists']:missing.append({'goal':g['id'],'missing_file':p['path'],'goal_text':g['goal']})
        for p in g['producers']:
            if not p['present_in_selected_files']:missing.append({'goal':g['id'],'missing_public_producer':p['method_or_action'],'goal_text':g['goal']})
        for t in g['named_cases']:
            if not t['exists'] or not t['literal_in_source']:missing.append({'goal':g['id'],'missing_named_test':t['name_or_registered_prefix'],'goal_text':g['goal']})
    return {'schema_version':1,'generated_utc':datetime.now(timezone.utc).isoformat(),
      'scope':'author-only semantic source/producer/named-case inventory; no execution, no gameplay, no acceptance',
      'baseline':file_info('docs/baseline/Puzzle_System_v0.1.md'),
      'budget':{'main_by_region':BUDGET,'main':64,'optional':32,'total':96,'budget_is_not_a_completed_count':True},
      'candidate_grouping_count':{'main':len(m),'optional_full_inventory':len(o),'total_full_inventory':len(m)+len(o),'optional_budget_allocation':'not frozen; 39 semantic candidates are not certified as 32 independent puzzles'},
      'main_goals':m,'optional_goals':o,'concrete_source_or_producer_or_named_test_gaps':missing,
      'normal_completion_test_gaps':[{'goal':g['id'],'goal_text':g['goal'],'current_test_kind':[t['route_kind'] for t in g['named_cases']],
          'required_action':'Add or locate an actual zero-XP continuous producer completion case/native recording. Prior-source fixtures are engineering coverage only.'}
          for g in o if not any(t['route_kind']=='actual_zero_xp_normal_path' and t['literal_in_source'] for t in g['named_cases']) and not g.get('observed_prior_normal_native',{}).get('normal_completion_observed')],
      'excluded_from_puzzle_count':['source acquisition alone','ordinary combat without independent control/result relation','travel links/berth registration','every device substep','three methods of one access goal','hints/retry/subtitle/translation','each six-tower repeated template site as another automatic unit','24 ship conversations','dictionary720/review sessions','Q-F05 epilogue','32 quest family bindings','nine worked reference units'],
      'limits':['Named cases are source references, not claims that they passed this current tree. Root final suite/native manifests provide observed execution.',
        'The six marked communication/verification boundaries need merge review. They are not missing production code; a method count cannot prove their independence.',
        'Source IDs are recorded labels for the stated goal; public availability still requires production access permissions. This author-only file must never be shipped as a player answer key.',
        'Current language, audio rights and numeric review remain pending; this does not hide the explicit implementation/test gaps above.'],
      'human_signoff':None}

def markdown(d):
    lines=['# 实质目标源／生产接口／具名用例清单（作者核对）','',
      f"生成：{d['generated_utc']}。只读源码核对，未执行游戏、测试或验收。",'',
      '原件第18.1节给出64主线＋32可选预算；没有冻结96个编号。以下64主线是新作者语义分组，39可选是完整工作候选库存。不能据此宣称103个独立谜题，不能把39删成32后宣称达标。',
      '9个worked reference只是一组局部案例，32任务家族方向只是一组绑定，均不替代以下生产目标。', '',
      '## 具体工程/证据缺口','']
    for x in d['concrete_source_or_producer_or_named_test_gaps']:lines.append('- '+json.dumps(x,ensure_ascii=False))
    lines+=['','## 尚无具名正常链完成用例的目标','']
    for x in d['normal_completion_test_gaps']:lines.append(f"- {x['goal']} {x['goal_text']}：当前 {', '.join(x['current_test_kind'])}。补从实际零XP前序到此目标的完整生产调用；可读现有真实native证据，不能把fixture改名为normal。")
    for heading,key in [('主线64语义候选','main_goals'),('可选完整39候选，32预算待按实质目标合并','optional_goals')]:
        lines+=['',f'## {heading}','','| ID | 公开目标／结果 | 源 | 生产接口 | 具名用例／来源 |','|---|---|---|---|---|']
        for g in d[key]:
            tests='<br>'.join(t['name_or_registered_prefix']+' ('+t['route_kind']+')' for t in g['named_cases'])
            if g.get('observed_prior_normal_native',{}).get('normal_completion_observed'):tests+='<br>已观察正常native：'+g['observed_prior_normal_native']['path']+'；当前树待最终快照复核'
            caution='<br>⚑ '+g['grouping_caution'] if 'grouping_caution' in g else ''
            lines.append('| '+g['id']+' | '+g['goal']+'<br>'+g['observable_result']+caution+' | '+g['public_source']['path']+'<br>'+', '.join(g['material_ids'])+' | '+'<br>'.join(p['path'] for p in g['production_files'])+'<br>'+', '.join(p['method_or_action'] for p in g['producers'])+' | '+tests+' |')
    lines+=['','排除项：'+ '；'.join(d['excluded_from_puzzle_count'])+'。','','人审/权利/真实英语评测均未签。']
    return '\n'.join(lines)+'\n'

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--output',default='reports/continuous/semantic_goal_inventory_20261010.author_review.json');a=p.parse_args()
    out=(ROOT/a.output).resolve();assert out.is_relative_to(ROOT/'reports/continuous')
    d=generate();out.parent.mkdir(parents=True,exist_ok=True);out.write_text(json.dumps(d,ensure_ascii=False,indent=2)+'\n',encoding='utf-8');out.with_suffix('.md').write_text(markdown(d),encoding='utf-8')
    print(json.dumps({'path':str(out),'main_candidates':64,'optional_candidates':39,'concrete_gaps':len(d['concrete_source_or_producer_or_named_test_gaps']),'normal_test_gaps':len(d['normal_completion_test_gaps'])},ensure_ascii=False))

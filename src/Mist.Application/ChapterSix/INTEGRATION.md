# R6 集成合同

Core、7个 Application 文件及 `ChapterSixCases.cs` 已由Root纳入，等待主任务串行回归；本文不报告尚未执行的测试为通过。源文本32份/6脚本版本已经冻结。注册类为 `Mist.Tests.ChapterSix.ChapterSixCases`，当前10项真实SQLite工程用例；所有正向主线从零XP正常路径抵达，只有拒绝裸Actor入口的负向用例使用显式非法fixture。采购故障用例在真实物理提交后的 after_commit 注入异常，关闭并重开同一SQLite档案，再以同批实物原回执恢复入账，核对不重复消费、不重复记账且拒绝另一批序号。

构造：`ChapterSixService(repository, catalog, ChapterSixPackage.ParseJson(json), authoritativeWorksites, actualCheckpointArchive)`。工位输入须合并 R1/Q102、R2、R3、R4、R5、R6 源几何和实际船舱几何。公开 `Geometry` 只含R6，`WorksiteGeometry` 含合并输入。旧地区仍由原地区服务负责移动与配对出口，不能借R6服务远程移动。

入口 `InitializeRegionAsync` 严格要求真实 Q504 的 `old_city_joint_handover_received`/`R5:actual_handover` 同体、当前R6核查委托、同一 Q104 GLIMMER、当前 R5/R6 海路和 R6泊位、实际 `sail_shared_glimmer`、同源 typed `actual_vessel_arrival` 及当前角色真实安全落点。Root 的 `r5_r6_voyage.json` 是航行生产者；初始化不授经验、已读材料或已完成工程。

主线现场 API：`ViewAsync`, `CurrentGeometryAsync`, `MoveAsync(Point2)`, `TraverseAsync(ImmutableArray<Point2>)`, `PortIssue(snapshot,port)`, `EnterPortAsync(portId)`, `DiscoverAsync(id)`, `OpenMaterialAsync(id)`, `PerformAsync(action)`。需要参数的站位不能经通用按钮执行：`ChooseCorridorAsync`, `ConfigureSurveyAsync`, `PlanCargoAsync`, `ControlFieldDeviceAsync`。五个遭遇使用 `EncounterAsync`, `BeginEncounterAsync`, `PreviewEncounterActionAsync`, `WithdrawEncounterAsync` 和同一 `ConfirmPreviewAsync`。当前校准等级须为3；实际设备外侧三步骤不由主机压力替代。

正文/提交：`SaveDraftAsync(task,revision,draftId,body)`、`ConfirmMeaningAsync(quest,fields,assisted)`、`SubmissionSpot(quest)`、`PreviewSubmissionAsync(quest)`、`ConfirmPreviewAsync(token)`、`ClosePreview(token)`。Q604 没有通用领奖提交入口。编辑、关闭、分支/读取世代变化撤销预览；保存不执行工作。意思字段见正常测试 Run.Node601/602/603/DepartureReady。

六塔物理工位为 R1-S10.safety_console、R2-S08.future_work、R3-S09.load_test、R4-S08.preparation、R5-S08.load_test、R6-S07.delivery。`InspectFacilityAsync(region,contractVersion,installer,watchPosts,serialIds)` 核对实际站位、原件闭合、当前Safe来源、源几何、明确合同和四件当地有限实物。随后 `RegisterFacilityTeamAsync`、`RegisterCurrentFacilitySafetyAsync`、`InspectProcurementStepAsync(source,step,work,serials)`、`ReserveWorkAsync(region,type,configuration)`、`ExecuteWorkStepAsync(region,type,expectedStep,task)`。每步都须实际到场；不会从清单自发产生资格。

R4 当前 Q404 只有 typed 技术移交，缺独立当前安全回执。新增 `ReinspectCurrentFacilitySafetyAsync("R4")` 只在真实 R4-S08.preparation，核对现时棱镜隔离/返程/值守/保护负荷、当前 C 配置与独立稳定试验、完整原 Q404 同体后签发当前 `tower_safety_verified`。它不改历史技术移交，也不因Safe标记独自授予安全。

增强 API：`VerifyTechnicalPackageAsync(controller,load,contact,evidence)`；在真实旧塔 `GrantLocalTrialAsync(region,configuration,posts)`、`FieldTrialStepAsync(region,stage,loadMode,isolation,backup,localReturn)`；`SignRegionalProtocolAsync(region,role,holder,scope,terms)`。先实际保留夹具并完成运输/交付/安装，再进行两阶段试验，最后工作验证归还可复用夹具。地方 operator 和 representative 独立签署，不以五个Faction或CP好感替代六地区。

Tess API：`DecideTessInvitationAsync(decision,recruit)`、`ConfirmTessContactsAsync`、`PlanSupplementAsync`、`DeliverSupplementTripAsync`、`PrepareTessNoticeAsync`、`SendTessNoticeAsync(expectedRevision)`。局部邀请与正式招募分别确认，支线不作为最终基本资格门。

Q604：在实际 R6-S08.qualification 查看公开缺项、`ConfirmPreparationDirectionAsync`、真实保存正文意思；在 R6-S08.commitments `VerifyRiskRegisterAsync`；再 `SealPreparationAsync`。必须传实际 `IEndingCheckpointArchive`，保护档不能为null或虚拟值。封存后仍能真实走到R6泊位并进入HUB-S02海图，准备变更须继续起航前保护档。

唯一最终起航生产者 `FinaleLaunchService(repository,catalog,interiorPackage,finalePackage,actualArchive)`：`PreviewAsync`、`ConfirmAsync(token)`、`ClosePreview(token)`。实际HUB-S02.chart，同Q104单船/当前R6源泊位/三个已登记当前船空间/真实冻结准备与保护档/完整当前风险均须成立。单事务将Q604闭合、固定奖励、同船DockRF及角色实际RF-S01安全落点写入。

Q604 semantic `Q-604:resolution`，action `network_finale_departure`，entity `final:actual-departure` kind `finale_departure`，body为 Core.ChapterSix.FinaleDeparture(VesselInstance,Q604Instance,PreparationId,PreFinalCheckpoint,SourceRegion,Destination,SourceBerthScene,SourceBoarding,SourceBerthReceipt,DestinationScene,Landing,DestinationGeometrySha256,SourceReceipts,RiskRegisterReceipt)。R6/RF、RF-S01和源包geometrySHA必须精确匹配。不是普通Travel.SailRF。

正常公共 helper：`ActualArrival(ProfileStore)`、`NormalPrepared(ProfileStore,bool enhanced=false)`、`NormalComplete(ProfileStore,bool enhanced=false)` 均返回 `(ChapterSixService Service,VesselInteriorService Interior,ChapterSixCases.CheckpointArchive Archive)`。最后一项包含真实Q604封存及起航；RF可消费同一实际Archive。Root正在提供 ProfileStore 的原子持久保护档生产实现，测试Archive将改为同一实现的薄适配，正式客户端直接传真实store；原先外部授权文件不作为正式客户端依赖。

原生UI `game/UI/Shell/Root.ChapterSix.cs.pending` 与工程驱动 `game/Diagnostics/Root.ChapterSixClientTests.cs.pending` 已单独冻结，等待Root纳入。Root提供 `_chapterSix` 和 `_finaleLaunch` 字段；实际store实现保护档接口后，两服务传同一store。

Root hooks：`TryChapterSixInteraction(spot)`；各旧章处理前 `TryChapterSixFacilityInteraction(spot)`（保留打开原有地方检查按钮）；`OpenChapterSixQuests/Archive/Workspace/Writer`；`SaveChapterSixDraft()` 纳入手动存档、Continue、Backup和退出；`CloseChapterSixUiPreview(title)` 纳入关闭；`ResetChapterSixUiState()` 纳入世代切换；`ChapterSixEncounterSelected` 与 `_chapterSixSelectedEncounter` 纳入共享战斗抽象；正文缩放为 `20*_scale/100`。HUB-S02.chart、DockR6、Q604已开启时提供 `OpenChapterSixLaunchPreview()`。Root负责实际RF初始化与显示。

native入口参数 `--chapter-six-client-test` 调用 `RunChapterSixClientTests()`。精确初始档生产任务为 CP-R6-05，输出 `chapter-six-native-entry-*/normal-r6-native-entry.json` 及同目录整个profile，10500XP、未取得R6材料、无R6工程余额；只能复制实际档案，不能改Actor、补经验或fixture。驱动先测实际Q601、Q603并行规则，再经船舱海图返航到R5/R4/R3/R2/R1各实际工位，逐件核对、领取、预留、安装和验证，最后返回R6提交Q602、保存Q604正文、实际风险登记、正式保护档、封存及最终起航。材料、编辑、意思、提交、工作单、登船/航行及最后确认使用真实原生控件；安全寻路和战斗请求由工程代码供应。

输出 `chapter_six_client_results.json` 只报告实际运行的工程界面检查；未运行时不能称为通过。正文/听力标定、真人原生测试、物理IME、屏幕阅读器、正式审核与后续产出仍pending。

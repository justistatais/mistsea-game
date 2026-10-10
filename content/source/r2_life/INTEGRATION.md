# R2Life 接线合同

不改 chapter_two/learner_package.json、原文版本、十空间/二十入口；新增公开 contract 是原创功能岗位与六床操作候选。作者卡和 provenance 不复制到玩家包。没有独立音轨；沿用两份阅读原文，没有声明听力播放或真人校准完成。

`R2LifeService(IAdventureRepository repository, IEventLeaseStore leases, ChapterTwoService chapter, R2LifeContract contract)`。`Contract` 从玩家 content/r2_life/learner_contract.json 读。只有现有 ChapterTwo 的真实同船海航/初始化/登陆受理后 `InitializeAsync`，不因裸 actor 或进入菜单制造历史。

共享 UI 由 Root 接线：LoadR2Life 在 LoadChapterTwo 后建立服务；登陆登记完成后 TryInitializeR2Life 初始化同一实例。当前章节 R2 的私信双向 port 先交 R2Life.EnterPortAsync；其余端口仍由原 ChapterTwo 处理。PortIssue 对两私信门用当前本人范围，撤回时出口保持开放；其余原门闸不变。导航仍 ChapterTwo.CurrentGeometry。新 R2Life 交互只处理公开 address 及原 O01/O02 三个原工位，主线按钮继续原处理。

实际接口包括 RequestLetterScopeAsync(fields,recipient,purpose,version)、WithdrawLetterScopeAsync、AcceptAsync/DeclineAsync/SetPausedAsync、DiscoverAsync/OpenMaterialAsync、InspectBedAsync、SetLayoutAsync、RequestLayoutRevisionAsync、RequestGrowerApprovalAsync、PlantBedAsync、SaveDraftAsync(quest,draftId,revision,body)、ConfirmMeaningAsync(quest,structuredAssistance,PrivateReplyMeaning?)、RequestAuthorReviewAsync(decision)、PreviewSubmissionAsync/ConfirmPreviewAsync/ClosePreview。`ViewAsync.Materials` 是取得且当前获准的原文切片；学习/词典每次调用端重取该视图，不复用已撤权范围。

离线学习不新增词义计数；复用原 720 sense 的 chapter_two 来源和现有 fingerprint。原文短写不输出分数。UI 表单所有作物/意义字段从“请选择”开始，不自动选择答案；原稿从空稿/保存稿开始。意思辅助与正式世界受理分别按钮。

测试注册 `Mist.Tests.R2Life.R2LifeCases.Register()`；`ActualEntry(ProfileStore)` 真正从零经验推进 Q104、唯一 GLIMMER 和原 R1R2 海航到 900XP 的 R2 登陆，地方实例未受理。`normal-r2-life-native-entry` 输出保存、entry_evidence.json 可用于真实原生驱动。其它测试明确使用先前 Q104/海图工程 fixture，再执行实际 SQLite 航行/登陆和地方事务。未本代理构建或跑测；Root 串行验证，不能提前记 passed。

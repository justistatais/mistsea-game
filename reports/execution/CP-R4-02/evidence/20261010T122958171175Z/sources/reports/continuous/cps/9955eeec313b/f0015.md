# R4-O01 有范围正式课程询问制作卡

版本 r4-study-inquiry-candidate-1。按 Root 授权追加独立地方生产者；状态 original_unreviewed / human_pending。原 `learner_package.json`、灰盒、原听力脚本和音轨绑定没有修改。CP-R4-04 本项工程仍待主进程构建/测试；真人内容、IME、读屏和实际操作验收 pending。

来源：Exploration_Level_Design_v0.1.md 9.5 登记 R4-O01 学生共学室，比较课程冲突与通知并写正式询问；R4-S02↔O01 是公开学生工作区双向口。冻结来源 R4-O01-R-NOTICES 保留原通知 Cedar14:00、房间更正 Birch14:00，以及 Cedar 已留给档案工作坊；更正不是取消课程。原提示中合同待完成的历史文字保留。新增零奖励课程台规程独立说明现在可执行的有限受理，不能把旧草稿直接认作完成。

地方原创细节：ROLE-R4-COURSE-DESK、SC-PUBLIC-01、两张有限工作纸与一条本地台账；公开提纲的外壳/参考标/来源次序都是普通教学内容候选。没有新增中央历史、主角身份、私人学生字段、实验安全、已上课、另一次已批准场次或结局条件。工作库存是地方器具，不是工程零件或可选奖励。奖励预算未登记，因此经验、刻币、零件都为0，32主线任务不受此工作影响。

真实站位：通知板 R4-O01.notices=(22,28)，台前 R4-O01.inquiry=(34,28)。先沿冻结 paired ports 实际进入，读冻结通知链；到台前取得 optional_inquiry.json 两公共规程，取得有范围许可和工作实例，领同一张纸；回通知板明确比较原房间/新房间/时刻/更正范围。保存同一 DraftTask R4-R4OPTIONALSTUDY-W，自由英文保持未评测；另行明确当前问题。台前只读预览后实际封口。纸件、稿件或来源改变则旧预览失效。封口与交付分别成立。

可行问题有三种，无默认正确请求：schedule_clarification 澄清公开安排并保留时间冲突；public_outline 请求当前公开提纲，答复不是出勤证明；alternate_practice_request 登记另一次练习申请，其分配仍 pending。请求私人身份、把房间更正当取消、改14:00或删除原件不在范围。未知字段保持未选；空稿仅通过明确结构化辅助继续，不称为独立英文写作或自动理解。

实际交付把唯一 R4-STUDY-INQUIRY-ONE 从 player/hero 移到 warehouse/ROLE-R4-COURSE-DESK；同一事务登记台账并产生唯一 R4-STUDY-REPLY-ONE 于该台前 warehouse。台账保留实际交付稿Hash、当前范围与来源版本。纸件不可取回改写已收版本；以后个人稿件可另改，但不改历史收件。答复有限文本按用户明确问题和公开规程生成，未解释或评分自由英文。

仍须实际到台前领取答复，同一回复纸从台前 warehouse→player/hero，并打开已取的英文/中文。明确收阅仅记录持件者声明，不能证明英语理解或实际上课。最终 semantic R4:optional-study:resolution / action study_bounded_reply_acknowledged，typed StudyInquiryReceipt 同 entity R4:study:acknowledged；精确一次0奖励。接收申请与分配申请批准是两件事，待分配状态不能由收阅回执改成已批准。

三级帮助：一级去通知板和课程台分别看来源；二级分别比较房间、时刻和课程是否取消，留意14:00仍重叠；三级可展示可行字段与三种有限问题的后果说明，但没有预选项，也不代做领纸、封件、交付和取答复。当前UI公开材料和显式辅助可用，帮助节奏仍待人审。

恢复：尚未交付的封件可实际拆封，复用同一纸并递增定稿版。旧封件/稿件来源回执保留；改稿须新意思确认及重新定稿。取消、移动、来源变更、并发修改、加载世代变化使旧CAS拒绝。取租约前检查 Profile/Branch/Load/ReleaseId；工作原子占 input:foreground 与 role:R4-course-desk。安全拒绝未结束战斗、运行事件、在途货物及终章出发后的新地方工作。物品kind仅既有 warehouse/player，不新增SQLite枚举。

冻结文件：Core StudyInquiryModels.cs.pending；App StudyInquiryService.cs.pending / StudyInquiryService.Previews.cs.pending；tests StudyInquiryCases.cs.pending；UI Root.ChapterFourStudy.cs.pending。Root 纳入后注册 Mist.Tests.ChapterFour.StudyInquiryCases.Register()，四个 CP-R4-04：真实0XP→R4首次到达正向纸件与提纲收阅；明示先前来源夹具的拒绝；取消/草稿CAS/移动/load；拆封复用/历史保留/另一练习仍待分配。不能把三夹具当正常生产者验收。

玩家内容只 ParseJson 验证并复制 optional_inquiry.json→game/content/chapter_four/optional_inquiry.json，列入 manifest；不导出作者卡，不动冻结正文、几何或音轨。

Root UI 接线：

- 在 TryChapterFourInteraction 前调用 `await TryChapterFourStudyInteraction(spot)`，仅两个 O01 站位。
- 在 OpenChapterFourStation switch 前调用 `await TryChapterFourStudyAction(action)`，仅 save_optional_inquiry。
- **在 OpenChapterFourWriter(quest) 开头**，R4-OPTIONAL-STUDY 派给 OpenChapterFourStudyWriter，R4-OPTIONAL-CREDIT 派给已纳入 OpenChapterFourCreditWriter。原材料阅读器也有编辑入口；只拦station会留下原writer的throw-pending消费者。
- 在 OpenChapterFourMeaning(quest,assisted) 开头将上述两个 optional quest 派到对应 Study/CreditMeaning，避免旧读档已打开writer遗留按钮。
- R4档案调用 AddChapterFourStudyArchiveButton(flow)；全局close调用 CloseChapterFourStudyPreview(title)，load/reset调用 ResetChapterFourStudyUi()。
- writer复用现有 _chapterFourEditors、ChapterFourWriterTitle、SaveChapterFourDraft 及主包版本，保持同一草稿与IME/保存流程；没有导入另一个世界写owner。

尚未运行构建或测试，不声称可玩、真人审校或 CP-R4-04 全部完成。未分配可选战斗槽、完整个人线/身份同意扩展、可选预算核定与其它未制作生活合同不由此小块完成。

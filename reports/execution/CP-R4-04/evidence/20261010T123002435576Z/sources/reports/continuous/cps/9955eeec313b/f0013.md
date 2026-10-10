# R4-O02 展签修订制作卡

版本 r4-exhibition-credit-candidate-1；原创作者候选，工程尚待主进程构建/测试，人工语言审校、读屏与真实玩家验收均 pending。没有新音轨，也没有自行授予人审通过。

此小块按当前授权扩展 CP-R4-04 的一个具体缺口。原来源为 Exploration_Level_Design_v0.1.md 9.5（R4-O02 核查署名与贡献证据、提出展签修订；公开展示入口双向），NPC_Faction_System_v0.1.md 12/13（普通地方生活零经验、有范围岗位接收、实际前景占用），冻结 learner_package.json 的 R4-O02-R-CONTRIBUTION 与 R4-R4OPTIONALCREDIT-W。新增地方制作细节为 CR-LABEL-v1、CR-BOOK-v1、CR-REVISION-01、匿名组织值班岗位和一张本地卡片；它们不新增主线历史事实、核心人物身份、试验数据、隐私同意或预算。

冻结地区原文明确旧可选合同仍待完成。本候选保持那些原文与提示不变；追加的独立三份来源公开说明新限定零奖励委托及实际现场执行范围，不能把旧稿保存或旧槽位直接当成交付。普通展签完成不关闭任何32主线任务，也不以未知奖励额度填入可选经济预算。

玩家材料仅 optional_credit.json：留存原签（展位）、贡献簿（台前）、有限修订通知（台前）。原签本来没单列参考检查；贡献簿说明建造、参考检查、介绍三任务，未知新增主张应询问。无需真实个人姓名或住所。此文件不能导出给玩家或送入 NPC 模型。

现场流程：沿冻结 R4-S03↔R4-O02 ports 实际进入，走到 label(22,28) 取得原签，再走 revision(34,28) 取得簿与通知；实际申请有限委托，取本份卡；明确比较，保存同一份自由英文提议并分别确认意思；只读预览后在台上真实制卡；持同一卡走到展位安装，展位核对当前制作版本，再走回台前，由本地组织岗位实际接收一次。每步均读当前角色/来源/许可/状态，实际世界命令 CAS 与 input:foreground + role:R4-exhibition-organiser 原子租约保护。

工作台刻字框与卡料是新公开通知明确的地方制作器具，不是可带走的 RPG 装备。唯一卡片 R4-EXHIBITION-CARD-ONE 的 definition 为 R4-LOCAL-EXHIBITION-CARD；许可事务从无实例建立本地 warehouse 工作库存，再实际领取到 player/hero，安装为 installed/R4-O02.label。不增工程零件、不退款成奖励、不产生第二张卡。接收前可取回同一卡重做，旧源/旧制作记录留存；接收后只读，无刷新奖励或重新领取。

可行字段：construction=workshop_team、calibration=student_reference_checks、calibrators=2、presentation=presenter；发言不等于唯一贡献、不改试验输出。提议保留原件、未知作问题、scope=public_credit_only_not_trial_approval。自由英文没有被自动评价或验证；现场公开卡按玩家另行确认的三岗位字段排版，保存的自由提议留作未评测稿件。显式空稿辅助可继续，不能声称已完成独立英文写作。

三级帮助：一级指向两个真实站位与原件差别；二级提示分别比较建造/参考检查/介绍，注意姓名范围和安全审批边界；三级可显示以上完整字段解释，但仍须玩家明确选择、实际制卡/安装/核对/交接。界面当前提供公开完整来源与显式辅助；真人帮助节奏仍需验收。

恢复：取消制作/接收预览不改世界；改稿、移动、读档、改变来源使旧 CAS 拒绝。已印或已挂、尚未接收的卡可在实际对应站位取回，使用同一实例重做；源与草稿保留。安全拒绝 active/defeat 战斗、运行中事件与运输、终章出发后新工作。作废 preview 在界面 close/reset 清理；旧加载上下文在取租约前拒绝。

交付语义 R4:optional-credit:resolution，action exhibition_public_revision_received；typed ExhibitionCreditReceipt 同 entity R4:exhibition:accepted-label，记录实际卡/版、岗位、来源指纹与零经验/刻币/零件。许可回执 exhibition_revision_permit_issued 与实际 typed permit 实体逐字一致，受理范围限三种公共岗位，不扩大私人记录许可。

注册：把四份新 runtime/tests `.cs.pending` 与一份 UI `.cs.pending` 纳入（Core 一、App 两、tests 一、UI 一）。tests 注册 Mist.Tests.ChapterFour.ExhibitionCreditCases.Register()，四个 CP-R4-04 工程测试，其中正向真实从0XP穿过实际前章、R3R4航行到3840XP，不使用正向 fixture；其余明示 prior-source fixture，只测拒绝/恢复/CAS。

内容导出仅把 optional_credit.json 原样复制到 game/content/chapter_four/optional_credit.json，并以 ExhibitionCreditPackage.ParseJson 验证、列入 manifest；不导出作者卡，不重写现有包/几何/音轨。UI Root.ChapterFourCredit.cs 提供：

- TryChapterFourCreditInteraction(spot)：在原 TryChapterFourInteraction 前调用，仅拦两个 R4-O02 站位；保留冻结原地区材料入口。
- TryChapterFourCreditAction(action)：在原 OpenChapterFourStation switch 前调用，只处理 save_optional_credit，避免旧 pending writer consumers。
- AddChapterFourCreditArchiveButton(flow)：在原 R4 档案 panel 增加独立工作/已得源入口。
- CloseChapterFourCreditPreview(title)：在共享关闭回调中调用，精确取消当前层 preview。
- ResetChapterFourCreditUi()：在 load/reset 调用；save/IME 已复用现有 _chapterFourEditors 与 SaveChapterFourDraft，原同一 DraftTask 不迁移、不复制。

未运行 build/test，尚不声称该小块可玩或 CP-R4-04 完成。R4-O01、剩余生活场景/个人线、预算审查、真人内容审校与端到端操作验收仍未由此小块完成。

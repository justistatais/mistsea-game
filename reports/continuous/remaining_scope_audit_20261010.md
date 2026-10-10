# 剩余范围审计：2026-10-10

这是当前文件树的只读盘点，不是全游戏验收，也不是人工通关。用户连续授权按 Scope_20261009 执行；baseline/reference 未改。机器可完成的新制作和工程继续做，人工剧情、6.5校准、审听、权利、IME/读屏与发布关不能自签。

已实际执行 `python -X utf8 tools/audit/remaining_scope_audit.py`，成功写出 `remaining_scope_inventory_20261010.json`。首次运行暴露 Q102 独立几何是字典而非列表，修正解析后重跑成功。脚本仅写 reports/continuous，不修改任何计划、正文、数据库或用户档案。JSON 绑定当前来源哈希，明确 source literal、目录和设计预算均不等于成品。

## 预算与当前成品口径

| 对象 | 冻结设计 | 实际当前树能查到什么 | 尚不能认定什么 |
|---|---:|---|---|
| 空间 | 78 | 66 个不同场景有新增几何候选；runtime_catalog 登记全部设计ID | 目录有ID不等于可进入、任务可玩或正式美术完成 |
| 实质解谜 | 96=64主线+32可选 | 原件只有9个worked reference单元；章节服务另有实际实作 | 没有96个已制作、来源/解法/保存/方法均绑定的独立单元清单，不能重复数三种方法/提示 |
| 遭遇 | 44槽=32主线+12可选 | 原件44槽、5个benchmark scenarios；源码另有独立章遭遇和机制测试 | literal命中、benchmark或敌人模板不是44个成品关卡，不等于每人都要打 |
| 词义搭配 | 360核心+360扩展 | lexicon目录成品0；旧词典13词形、9个switch语义组；13条候选词音 | 18种子、章节词表和词形变体不等于720独立sense条目 |

扫描时缺几何 ID 是 R1-O01、R1-O02 以及 R6 的8主场景/2可选场景。R6由另一位代理正在制作，脚本不会将其 `.pending` 或未落盘包计为完成。扫描后可重复执行，新增事实只追加到新快照。RF、R2–R5包有几何并不代表各自人审或原生通关完成。

## 具体可执行缺口

1. **CX-066 完整离线词典**：`src/Mist.Application/Materials/MaterialJournal.cs:Lookup` 目前硬编码 draught/draft、subject、bracket、isolate词形、operational、supersede词形、interlock、reserve、beacon。`tools/audio/Generate-DictionarySpeech.ps1` 仅生成13词形。没有 `content/source/lexicon` 的720条完整sense数据、来源版本/片段指纹、按语境检索及完整离线消费者。下一步制作360核心+360扩展的真实英文释义、中文辅助和原文来源；同一词的阅读/听力机会不双计新sense。词音和rights继续pending，不从TTS生成推断审核。

2. **离线复习**：`src/Mist.Core/Learning/EvidenceState.cs` 只做既有证据状态投影；没查到持久到期条目、1/3/7/14/30建议间隔、已知信息/许可/世界版本兼容筛选、未见材料选择及延后入口。下一步独立 Core/Application/Learning 模块和 Root.RegionalLearning 原生partial，最多建议3项、不抢写作焦点、无题可用允许延后、不写世界奖励。时长和听稿读取都不能作成功评测；延迟证据使用真实时间，不伪造游戏经过天数。

3. **五声道及独立音量**：当前 Root.Views/Root.RegionalAudio 的 AudioStreamPlayer 未显式分内容语音、环境、音乐、UI总线；没有AudioServer五声道控制/持久偏好证据。Root已负责此独立块，须真实监听当前属性、mute和重载、词典词音与主信息语音暂停衔接，环境声降低不算内容帮助。

4. **CX-073 实际28级终章**：planning execution 仍not_started，无 `reports/execution/CX-073.json`。`tests/Mist.Tests/Finale/FinaleBattleCases.cs` 明确是机制fixture：将友方HP改5000、攻击技术改100000、部分敌人改无害。这些8机制用例测试phase/溢出/有限增援有价值，但明确不是最低成长完整战。当前缺三主修+混合、真实28级主线装备/地方援勤/无付费补给下，鲁克和三阶段机组连续资源完整行动记录，回合/时间节奏、失败分析和明确局部数值变更单。30级benchmark不能替代。RF实现者需在真实R6正常入口到齐后补。

5. **CX-076 包内故障端到端**：历史执行报告有14项真正SQLite/DACL/进程终止案例并明确passed；范围是隔离路径服务层。真实拒写、max_page_count容量失败、提交后丢回复/查回执、坏迁移副本、坏源SHA、删除世代、控制库缺失均有证据。仍缺导出候选exe启动到故障UX/导出旧稿/恢复/重试同奖励的整矩阵。SQLite quota不是系统盘物理满盘；kill不是断电；另一台干净机器和物理断电仍not_run，不能改口径。可在本机导出exe隔离profile中继续做可控矩阵。

6. **CP-R0/R1作者与QA账**：十二CP任务仍not_started且各自execution报告不存在。已有 `content/source/campaign/production_cards.json` 只有 QP01..QP03 与Q101；`first_entry.json` 13材料8几何和 `chapter_one.json` 6材料3几何，Q102 原件完整材料另在导入包，实际正常序章→Q104服务已在 ChapterOneCases.NormalComplete 并有正常900XP同船回执测试。须编成不重写原文的R0/R1作者卡/全来源哈希/固定事实与局部原创差异/每节点提示和恢复/主线与私人及生活可选预算/QA用例索引，实际查各方法与连续证据。不能据CP账not_started说服务没做，也不能据服务通关自动标CP人审完成。

7. **R1两可选空间与跨章未完成候选**：R1-O01/R1-O02当前无几何候选。章节包中的可选路牌、失物、工坊公开提案等有的只有完整正文，正式NPC受理和奖励仍抛明确未接入；这些未完成权益和其余可选遭遇不能占满预算。12人物节点真实测试与框架不等于每节点的全部剧情/材料/人物表达完成，需每节点作者来源和原生UI检查。

8. **玩家词条与理解题消费**：R5 learner 包16词条/12来源解释题已静态制成，但当前 ChapterFivePackage DTO/UI没有展示它们；它们是作者可核的数据，不能认定成玩家已获得学习机会。完整Learning模块将提供安全投影消费者。所有解释题无评分，答案只留作者卡。

## 当前可复用证据与下一块

最新盘点时最近实际C#报告是 `reports/build/20261010T031901126174Z/test_results.json`（一项R4真实正常入口），另 `20261010T031436723455Z` 为两项R3正常链，`20261010T031349698711Z` 为8项终章机制。它们有明确任务和范围、人签null，不合并成全游戏通过。R5代码最新由Root确认Debug build零warning/error；R5的24用例和native驱动仍由Root串行执行，这里不提前写passed。

本代理完成审计后继续独占新R0/R1作者资料及QA索引，再做完整词典/离线复习模块和原生partial；Root做共享接线、五声道和统一构建，R6/RF代理继续实际终章链。新产物按自身真实结果记录，不改来源使哈希变绿，不代签CP-*-06或HV-*，默认在线provider保持关闭。

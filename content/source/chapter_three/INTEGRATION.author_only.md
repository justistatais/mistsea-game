# R3作者候选交接

版本 `r3-author-candidate-1`；review `original_unreviewed`、human/rights `human_pending`，scope `authoring_candidate_not_runtime`。本文 `player_projection=false`。`learner_package.json` 是唯一建议的玩家只读正文来源；生产卡、生成器与provenance不得进入玩家/NPC上下文或默认稿。没有在线provider、音轨、真人签核、正式内容发布或可玩整章声明。

## 文件

`learner_package.json` 提供35份完整双语正文：两个长读731/847英词、五关键听力脚本中两个长脚本731/749英词、另一个保留原件英文的个人维修对话；所有音轨asset空、actual duration null、script-only。八写作任务含150词完整视觉与250词完整论证机会，四组阅读/听记题初始unanswered/unassessed。16原创词义定义无词音。九主空间、两可选、22配对出口、53可见站位操作。机器语义、来源、权限均须实际运行服务绑定；JSON不携任意WorldCommand。

`production_cards.author_only.md` 含四主线、两个本章奥伦个人节点、两个可选生活任务的完整目标/证据/多解/提示/恢复；五主线遭遇合同、奖励预算、知识边界、完整题作者要点和缺项。CQ-OREN-03只是Q504后接口，不计入本章完成。两个原可选ENC槽未制作合同、不注册奖励、不算可玩遭遇。

`build.author_only.py` 只将本区明确正文和拓扑装配为UTF8 JSON，再执行本地结构断言与SHA快照。重跑会更新当前候选文件，正文变化需新内容版本及复核；不要用它生产任意即时剧情或绕过版本发布。它不启动游戏、生成音轨、评价英语或写世界事实。

`provenance.author_only.json` 保存实际读取原件SHA256、作者文件SHA256、逐材料词数、新增局部细节与缺项。mechanical checks只表示本地作者装配实际执行；不意味着runtime/playthrough/HV/rights/calibration通过。

## 入口、泊位和拓扑

必须由真实Q204实际范围交接、MC-2方向及同一微光号合法当前船路到R3。演员在R3-S01只是位置，不能凭位置初始化完成入口。与Root晚章船路对齐的公开职能泊位来源 `R3-LOCAL-BERTH-DUTY` 仅登记地方登陆，不授矿井/受控档案/私人谈话权限。

冻结 `R3-S01.berth=(48,90)`，walk=(left5,top20,right95,bottom95)，S01无blocked/danger；S01toS02出口 `(10,88)`，S02toS01入口同 `(10,88)`。同区空间内所有ports/spots有合法站位；全灰盒数字是原创建候选、没有原生路径/碰撞验收。港区货运站来源不虚构铁路线通海，微光号使用的是原区跨区入口的已核验同船泊位。

| 配对场景 | gate string |
|---|---|
| S01↔S02 | public_freight_path |
| S02↔S03 | local_inspection_or_public_negotiation |
| S02↔S04 | mine_briefing_and_basic_kit |
| S04↔S05 | verified_ladder_or_verified_lift |
| S05↔S06 | public_pump_passage |
| S06↔S07 | current_drainage_and_equipment_safe |
| S07↔S04 | tested_freight_return_loop |
| S02↔S08 | public_dispatch |
| S08↔S09 | basic_rescue_and_tower_permission |
| S02↔O01 | public_musicbox_shop |
| S05↔O02 | optional_sidewell_safe |

每port ID为`R3-Sxx.to.R3-Syy`，target_port反向。ports按本表邻接顺序使用(10,88)/(90,88)/(10,25)/(90,25)/(50,90)。场景平面存在非危险绕行站位，是否可操作仍由实体/任务/当前安全判断，不能因能走到工作台而免掉设备前置。

危险JSON完整标记：S05.rail_obstruction→`ENC-R3-01:result`；S06.wet_drive→`R3:current_drainage_check`；S07.instrument_guard→`ENC-R3-03:result`；S09.extraction_machine→`ENC-R3-05:result`。active_until_receipt是合同名；服务必须核验真实instance/配置/encounter结果，不接受任意文本回执名。

## 数据与任务接口

保持R2 learner结构：content_version/scope/review/human/target_bands/player_projection、scenes.walk/blocked/danger/ports/spots、materials.location/source_root/access_class/paragraphs、writing_tasks、stations、connections。R3应使用自身任务/状态校验；中性ChapterScene/Material/Station/Writing DTO可以复用，不能让R2 expected-ten-scenes验证直接认R3。

文书业务版本与内容版分开：`MC-2.instrument.I@1`、`MC-2.retained.I@1`、`MC-2.acceptance@3`。前二source_root同`r3-mc2-instrument-original`。三方核查只支持旁路05/06未完成而正式accepted及奥伦签章具体责任；数据包不预存玩家“已核验”或当前导师状态。

材料RAW/COPY/SIGNED/BATCHES为controlled_review；COMMISSION公开给出职能记录台 `R3-local-record-desk` 实际四字段授权：check_status/acceptance_status/signatory_scope/version_source，recipient patrol_technician，purpose verify_original_instruments_and_acceptance，source-set version MC2-current-source-set。`R3-BATCH-SCOPE` at S03.comparison action request_batch_record_scope；权限须真实签发后再Discover受控文。不是奥伦私人许可或他的招募。

CQ材料三份individual_permission，O01音乐盒individual_permission；其他public仍需实际到spot获取。读取脚本/译文可推进世界，但学习记录保持text/script支持。R3-Q304-R-REQUEST为沃斯正式渠道核验文本，符合原件本章揭示；其存在不生成后来审查结论或导师现址。

### 必须持久化的工作链

Q301实际领取本地基础kit与物资、独立固定梯道、轨道结果、泵房当前隔离/排水/复查；S07同组三人定位/救援、S05真正护送、S02真正接收；仪表与样本实际获取。Q302可与其准备并行，但最终Q302收束需Q301；三文书来源、当前权限、比对范围和具体签章结果独立。Oren招募在Q302之后，拒绝不影响来源。

Q303六地方包真实owner、分配/预留/交付、七基本单位/十容量、两不同当班岗位、明确policy/window。early_reduce抽取0；time_limited_transition抽取1..3、一世界班、总≤10；`R3-CLOSE-TRANSITION-SHIFT` at S08.shift_duty action close_transition_shift实际交班归0并失效旧load test。现实阅读/暂停不推进工作边界；未交货不能close。

Q304需当前机器停料+吊臂固定+真实ENC05结果，当前受限配置与测试、两岗/返程/隔离，S01真实样本保管与合法字段/接收方，S09真实交接。实际安全只产生有限TowerSafety，不写TWR-R3全部工程达成。未来R4方向/学城接收目的不代表已经航行/试验。

CQOren01/02需有效可撤回私人邀请、真实aux设备/独立维修包归属、真实安装和维修者之外合格人当前version review，复用现有CompanionArcService可要求Entity+nodeInstance结果来源。既有主线/署名/奖励不可重新支付。03至Q504后；本章不生成其closure。

每任务草稿仓库CAS，meaning confirmation绑定hash，预览只读冻结load/branch/actor/actionClock/权限/材料/配置/任务/稿件读戳，显式发送再核验同command。AI/自由英语解析未绑定时明确结构化确认，不用字符串关键词出合格意图。只有实际世界命令能改实体；未知来源、未知站位、未注册action不得返回true。foreground lease遵共享服务，活动战斗阻止不合法移动/工程/装备/旅行。

## 实际需验证的范围

建议SQLite连续路线从真正Q204→实际船路R3→四容器→R4 direction；同时测裸actor/假船/无scope拒绝；Q301/302并行及302需301；三人逐步救护接收；同根副本；MC2字段/版本/签章/未知因果；拒招募保留证据；两完整长稿与结构兜底分别存；两政策及非例示合格分配、超额/一人双岗/窗后拒绝；交付后改稿不退已用物资；真实交班归0重测；5enc正确instance/currentconfig；塔安全和未来工程缺项分离；实际样本/权限合法交接；保存/继续/load/重放不重奖；私人撤邀/独立复核/主物资不挪。

本作者分工没有运行C# build、正式Godot或测试runner。所有实际runtime、native路径/美术、语音、演出授权、正文译文人审、目标人群6.5校准和全文评分仍待负责流程，不能因作者文件完成改accepted。Root统一编译与执行记录是独立证据，本文件不代填次数。

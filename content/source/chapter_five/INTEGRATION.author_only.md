# R5 接入合同

本文件及生产卡、作者脚本、provenance 是 author_only；玩家只读取 learner_package 的安全投影。来源/知识/奖励保持原件。所有新正文及补足数值为 original_unreviewed / human_pending；不授人审或6.5评测完成。

## 入口与输出

入口仅真实 Q404 `glasshaven_technical_handover_received` + 同一 GLIMMER 的 R4R5 实际航行/到达/泊位。typed TechnicalHandover 必须是 R5/bounded_validated_alternative/E-06/E-07/E-11/old_city_north_station，MaraCurrentStatus unknown，五项公开准备 [verified_scheme,six_tower_changes,transition_supply,common_protocol,field_trials]，PolicyDecision not_selected；实体与原回执正文一致。当前委托 `verify_north_station_and_legal_access`。R5-LOCAL-BERTH-DUTY 只授公共登陆，绝不授档案和扣押室。R5-S01.berth=(48,90)，章初始化不发XP、不招聘、不改当前导师状态。

Q504语义 `Q-504:resolution`，action `old_city_joint_handover_received`，entity `R5:actual_handover` kind `old_city_handover`。Core OldCityHandover 依次字段：Destination,Tower,MaraCurrentStatus,ResponsibilityScope,Custody,Publication,Supervisors,TowerConfiguration,Authority,RookStatus,PolicyDecision。严格值 R6/TWR-R5/rescued_alive_directly_verified/design_policy_execution_separately_verified，保管 public_archive_bounded 或 joint_sealed，公开 redacted_findings 或 redacted_findings_and_conditions，Supervisors=[ROLE-R5-OPERATOR,N-ADA,ROLE-R5-ARCHIVE-CUSTODIAN]，当前真实TowerRevision，temporary_joint_emergency/refused_transfer_entered_seventh_tower/not_selected。

`commission:next-inquiry` permission: allowed=true,region=R6,purpose=verify_current_network_risk_and_preparation,basis=Q-504:resolution,arrival_occurred=false。Root 负责 shared R5R6 voyage；Q504不直接换场到R6。`R5:future_preparation` kind future_preparation 包含 EngineeringRules.Required(TWR-R5) 的baseline/enhanced/transition/trial实际字典、actual_operator_posts=2、installation_capacity=1、requirements_only=true、delivered_stock=false，不生产库存。实际安全来源 action tower_safety_verified / SourceWork R5:actual_archive_and_load:<revision>；只Safe，其他readiness领域仍需真正六塔作业。

## 服务、范围与实物

Core `ChapterFivePackage.ParseJson`/Validate 验证10场景、原件11边及配对站位。Application `ChapterFiveService` partial模块处理实际站位、来源、物理救援、战斗、审查、保管、塔站和CASSafe草稿提交。每次命令保留load代际/实体reads，预览过期不得照旧执行。跨工作点提交不授奖励。

救援 scope NORTH-DUTY-D0@1: door_status,watch_arrangement,return_route_status,version_source；recipient patrol_technician，purpose rescue_and_safe_return，grantor R5-local-station-desk。责任 scope OC-responsibility-set@1: design_limits,correction_history,policy_authorization,execution_order,version_source；purpose distinguish_design_policy_execution_responsibility，grantor R5-local-record-custodian。字段重复、扩大范围、错误用途/版本被拒；进入公共审查厅不授controlled_review材料。

比较固定版本 OC-D18.design@1 / OC-D14.revision@2 / OC-D30.correction@1 / VQ-D90.policy@1 / VT-D7.authorization@1 / RK-D6.override@2 / RK-D3.detention@1。CopySource same_as_original。三方责任值 design_limits_and_delayed_correction / known_risk_quota_test_and_suppression / cancelled_review_and_expanded_detention。录反证机会需实际有来源站位、各方渠道；不是人审签名。来源同源根 oc-d18-original-design保留。

初始化当地工具 `R5-basic-kit/R5-basic-lamp/R5-basic-line` warehouse R5-local-mission-desk；领取真实warehouse→player/hero，非升级或六塔库存。唯一 `OC-MASTER-01` warehouse R5-local-original-custodian→installed R5-joint-sealed-vault 或 R5-bounded-public-archive。schema2 owner_kind 全程合法；无虚构local_store/delivered/custody值。

## 写作站位与真实接收站位

Q501 workspace R5-S03.map / receiver R5-S02.report；Q502 R5-S05.records / receiver R5-S02.reception；Q503 workspace/receiver R5-S07.hearing；Q504 R5-S07.custody / receiver R5-S08.handover；两个人物节点各同 R5-S07.personal_review 与 R5-S07.oren_duty。材料/站位/写作精确ID来自learner包，不能以模糊标签定位。草稿明确结构化协助，不产生自然语言评分；真实提交预览解释缺工作和结果。

## 测试与原生驱动入口

`Mist.Tests.ChapterFive.ChapterFiveCases.Register()` 注册24候选，由Root统一编译/运行；此作者步骤未执行C#测试。`ActualArrival(ProfileStore s)` 返回 (ChapterFiveService Service,VesselInteriorService Interior)，调用ChapterFourCases.NormalComplete的零XP真实前序链并真实航行；`NormalComplete(ProfileStore s)` 同tuple，继续R5所有主线。native入口用例输出 normal-r5-native-entry.json 并保存真实SQLite profile，不能改以fixture入口。

带正向前序历史的隔离边界用例只作为explicit engineering fixture，报告须区分于零XP正常流程。全部序列实际读写当前SQLite、当前草稿和唯一实物；不代表人工完整章节试玩。原生界面及client驱动候选另有独立结果；音频转写阅读不能断言播放/听力已测。

## 尚未通过的发行条件

语言6.5真人评测、人审剧情与历史、音轨录制/实际时长/权利、人类原生完整章试玩、物理IME及读屏、可选路牌失物正式受理和剩余可选遭遇尚未完成。保持缺项，不认定Q201之后实体或发布关自动全过，不宣称正式发行。

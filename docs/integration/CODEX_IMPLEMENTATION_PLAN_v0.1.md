# 第十三部分：最终整合与Codex分步开发计划
版本0.1｜2026-10-09｜136张任务卡＋8个阶段门｜所有实际开发任务初始未开始

## 1. 现在可以交接什么
本包收录十二份完整规范、十个源设计包的原始文件、总纲、十八条整合决定、十六项未完成风险、136张独立任务卡、478条验收责任映射和可执行的计划检查工具。

没有Godot可执行游戏、已编译C#项目、真实音轨或真人标定。历史参考实现保留作为迁移和测试例子，不是交付了正式源码。不要以测试数量总和计算游戏完成百分比。

## 2. 首次使用
把整个包解压到一个项目目录；已有仓库时先核对冲突再合并，不覆盖用户代码。用Codex打开该目录，读取根AGENTS.md，并发送CODEX_START_HERE.md中的首条指令。不要只上传一张截图或只粘贴总纲就要求生成全游戏。

本轮不建立远程仓库，不下载Godot/SDK，不上传任何玩家数据或发布任何游戏。计划工具只查看和核验，不自动安装、付款、修改状态或启动全部任务。

## 3. 分工与执行状态
80张CX工程卡负责实现；48张CP章节卡负责制作、集成和章级校准，其中每章06为真人校准；8张HV跨阶段卡需要真实平台、质量、权利或项目负责人核验。Codex能准备材料、程序和报告，不能虚构声音、受试者或人审签字。

每张卡状态：not_started→in_progress→implemented→verified→accepted；遇到真实阻碍用blocked。可选在线特性默认不启用，相关卡显示feature_disabled，不标passed。一个任务只有实际产物、相应测试、证据和复核成立，才能accepted；上一个任务的代码编写完成不能自动解锁下一个Gate。

学习难度证据和技术证据分开；代码构建通过不让素材自动calibrated。原始正文不因提示CI报错而被自动润色。

## 4. 阶段不是严格按编号串行
CX编号是稳定身份，真实顺序在depends_on中。例：CX037离港依赖CX042物品账，CX038不能抢先伪造奖励；内容作者可以在CX001之后并行起草各章，但后期章节正式集成需要G4。

只允许无重叠写范围且共享契约已稳定的工作并行。两个任务都改核心DTO、SQL迁移或同一个content bundle时串行整合。分支合并需要重跑受影响测试，不根据局部通过直接放行。

## 5. 阶段表

| 阶段 | 目标 | 任务 | 交付门 | 注意 |
|---|---|---|---|---|
| P0 | 实际工具链与输入试件 | CX-001—005、HV-001 | G0 | 先确认能在Windows离线启动、输入、保存；不是先画所有菜单。 |
| P1 | 代码结构、内容与持久化 | CX-006—022 | G1 | Core独立，原子提交、草稿、分支和删除先成立。 |
| P2 | 六场景与非战斗Q102 | CX-023—039（部分依赖P3成长账） | 内部证据 | 真实走路、材料、写作、修复和离港；不直接关卡标成功。 |
| P3 | 成长与战斗闭环 | CX-040—053 | G2 | 3级工坊、5级塔站，三方法同世界结果。 |
| P4 | 人物、工程与完整首章 | CX-054—065、早期CP与HV-002 | G3 | 从序章真实进入首章，反馈、声音与相应人审不能省。 |
| P5 | 制作工具、学习与可选AI | CX-066—072 | G4 | 在线默认关闭；共享工具可支持逐区扩展。 |
| CONTENT | 逐章素材、关卡、回归与真人校准 | CP-R0/R1/R2/R3/R4/R5/R6/RF各01—06 | G5及章级证据 | 01可并行写作；02需声音/素材来源；06必须真实审校/试测。 |
| P6 | 28级终章与三结局 | CX-073—074 | 纳入G5/G6 | 修复已知长战问题，基础两路线与增强路线可检验。 |
| P7 | 全量质量、恢复、候选和批准 | CX-075—080、HV-003—007 | G6/G7 | 证据齐备才正式本地交付；不擅自上传商店。 |

## 6. 每张任务卡的共同格式
目标、范围、前置、必读文件、允许改动目录、交付物、可观察验收、上位条款、禁止事项、错误处理和证据位置都在planning/tasks/<ID>.md。

不提供含糊的“完成整个RPG系统”任务；当某章某卡仍超过一次可安全审查的工作量时，先提出有稳定父ID的子卡并更新依赖，不在一个会话里暗增大量功能。拆分不得减少验收，原父卡只在子卡齐备后完成。

任务允许写reports和对应代码/素材区域，不允许编辑reference或docs/baseline原件。需要变更既有设计时先使用Change_Request模板并记录影响。发现冲突后，能独立完成的无关工作可以继续；不能为让测试通过而自动改成更弱规则。

## 7. 完成定义与证据
每张卡提交真实artifact路径/hash、代码或工作树版本、执行命令、退出码、设备/运行时、测试范围、未执行项及所需复核。用Task_Report模板。证据不足时保留implemented或blocked，不能把not_run显示成0失败后声称通过。

产品验收478条全部保留；任务卡上的分配只说明谁负责。CX078在最终候选执行全量相关回归，人审项目保留实际人审证据。版本改变导致旧证据不再对应时，相关任务和Gate重新等待核验。

Gate不是手写字段。依赖全部accepted且证据有效，才派生开放；G6在online_model启用后额外要求CX069和HV008。当前任务列表所有项未开始，阶段门都关闭。

## 8. 第一可玩样章的边界
G2是六场景Q102内部机制闭环：实际移动/取件/安装/保存/书写/辅助确认/三种方法；缺正式音轨时只允许清楚标注的内部脚本通路。

G3是从序章开始、走完首章的内容与体验证据：Q101权限、Q102入口3级、Q103核验、Q104入口5级、真实微光号和莉娜邀请。关键声音、素材来源、所需语言校准和用户可用性齐备；不是只把测试入口打包。

只有G3成立才扩大正式地区集成；候选内容、审校和制作工具仍可并行，不需要让整队人等待同一段代码。

## 9. 每一章六张卡
01：把流程契约补成完整的材料、证据、谜题、后果与提示制作卡。
02：原文冻结后的中文、词条、真实音轨、字幕、图表和场景状态素材。
03：用共享系统实施真实场景、任务、机关、人物和战斗引用。
04：地方/同伴/生活支线与有限奖励预算。
05：内部连续流程、乱序、恢复、不同方案与最低成长配置验收。
06：实际人员审校、审听与6.5声明条件校准；Codex不代签。

素材改动重新核对03/05和06的关联证据，不把校准当一次全项目永久证书。未知材料缺项写到章节账，不让生成模型自动写成通过。

## 10. 发布不是点击“所有任务完成”
G5仅表示内部全章连续可通，尚非正式质量批准。G6还需真机安装恢复、素材权利、语言覆盖与校准、隐私范围和关键风险解决。G7由项目负责人作出明确交付决定。CX080生成本地发行文件与用户说明；对外商店上传需要另行授权。

不得自动启用模型、自动付费配音、自动上传私人文本或把未经授权文件作为素材。缺少外部服务时记录合法替代与实际未完成内容，不诱导用虚假的人审文件跳过Gate。

## 11. 本轮验证的范围
本轮只执行了计划文件、原件哈希、DAG、引用、预算与状态逻辑检查；不会执行136张工程任务，不把十个历史参考套件的通过数相加。没有安装Godot/.NET或执行Windows构建。实际结果见reports/Integration_Validation_v0.1.md。

## 12. 全部任务索引
下表用于定位；必须读取独立任务卡才能执行。编号不是开发顺序，前置与Gate才是。

| ID | 阶段 | 任务 | 前置 | 类型 |
|---|---|---|---|---|
| CX-001 | P0 | 接管文档、库存与执行账 | 无 | engineering |
| CX-002 | P0 | 核实并锁定真实工具链 | CX-001 | engineering |
| CX-003 | P0 | 原生编辑器、焦点和可访问性技术试件 | CX-002 | engineering |
| CX-004 | P0 | SQLite原生集成与事务试件 | CX-002 | engineering |
| CX-005 | P0 | 最小Windows离线导出候选 | CX-003, CX-004 | engineering |
| CX-006 | P1 | 建立四程序集与组合根 | G0 | engineering |
| CX-007 | P1 | 构建、测试与证据命令统一 | CX-006 | engineering |
| CX-008 | P1 | 稳定ID、版本、时钟与命令DTO | CX-006 | engineering |
| CX-009 | P1 | 九类源适配器与奖励旧名映射 | CX-008 | engineering |
| CX-010 | P1 | 内容编译与内部/正式发布检查 | CX-009 | engineering |
| CX-011 | P1 | 领域查询、动作计划和相关版本读取集 | CX-008 | engineering |
| CX-012 | P1 | 玩家主库与独立控制库schema | CX-007, CX-008 | engineering |
| CX-013 | P1 | 单写队列与原子提交 | CX-011, CX-012 | engineering |
| CX-014 | P1 | 命令去重、工作去重与表现outbox | CX-013 | engineering |
| CX-015 | P1 | 继续、旧档分支、槽位与检查点 | CX-014 | engineering |
| CX-016 | P1 | 战斗重试与局部回滚范围 | CX-015 | engineering |
| CX-017 | P1 | 规范草稿版本、CAS与多入口 | CX-013 | engineering |
| CX-018 | P1 | 学习观察、帮助与模态账 | CX-017 | engineering |
| CX-019 | P1 | 删除世代、外发许可与晚反馈 | CX-015, CX-017, CX-018 | engineering |
| CX-020 | P1 | 一致备份、损坏隔离与迁移副本 | CX-019 | engineering |
| CX-021 | P1 | 精确内容绑定与安全安装器 | CX-010, CX-020 | engineering |
| CX-022 | P1 | 真实持久化破坏性测试套件 | CX-014, CX-015, CX-016, CX-017, CX-018, CX-019, CX-020, CX-021 | engineering |
| CX-023 | P2 | Godot应用壳与输入/暂停/焦点 | G1, CX-003 | engineering |
| CX-024 | P2 | 主角移动、相机与可达操作站位 | G1, CX-008 | engineering |
| CX-025 | P2 | Q-102六场景与成对出口 | CX-024, CX-009 | engineering |
| CX-026 | P2 | 拾取、安装与设备归属链 | CX-025, CX-013 | engineering |
| CX-027 | P2 | 工坊停机、保持检查与侧道 | CX-026 | engineering |
| CX-028 | P2 | 任务生命周期与派生目标 | CX-009, CX-014 | engineering |
| CX-029 | P2 | 阅读器、词典和分范围帮助 | CX-023, CX-009 | engineering |
| CX-030 | P2 | 真实音频、字幕与缺失通路 | CX-029, CX-018 | engineering |
| CX-031 | P2 | 规范写作工作区与可靠保存提示 | CX-029, CX-017 | engineering |
| CX-032 | P2 | 六类证据与谜题通用控件 | CX-029, CX-028 | engineering |
| CX-033 | P2 | 离线学习反馈与证据界面 | CX-018, CX-029, CX-031 | engineering |
| CX-034 | P2 | 哈文交谈、提议与临时占用 | CX-023, CX-028 | engineering |
| CX-035 | P2 | 意思候选与显式结构化兜底 | CX-031, CX-028 | engineering |
| CX-036 | P2 | 导航灯当前检查与执行预览 | CX-026, CX-027, CX-035 | engineering |
| CX-037 | P2 | 离港原子结果与单次奖励 | CX-036, CX-014, CX-042 | engineering |
| CX-038 | P2 | Q102两条非战斗路线端到端 | CX-030, CX-032, CX-033, CX-034, CX-037 | engineering |
| CX-039 | P2 | 世界HUD、任务和存档菜单集成 | CX-023, CX-015, CX-031 | engineering |
| CX-040 | P3 | 经验、属性、校准和主线奖励 | CX-009, CX-013 | engineering |
| CX-041 | P3 | 36节点、前置与四主动槽 | CX-040 | engineering |
| CX-042 | P3 | 28收藏、8改件和个人资源账 | CX-040, CX-013 | engineering |
| CX-043 | P3 | 整队回合、个人计划槽与资源 | CX-008, CX-016, CX-041 | engineering |
| CX-044 | P3 | 伤害、护盾、暴露与触发管线 | CX-043, CX-042 | engineering |
| CX-045 | P3 | 稳定压力、装置、负面和倒地 | CX-044 | engineering |
| CX-046 | P3 | 四站位、目标类型和意图预告 | CX-043, CX-024, CX-039 | engineering |
| CX-047 | P3 | 四同伴与两援勤的基础动作 | CX-043, CX-045 | engineering |
| CX-048 | P3 | 3级工坊实战与方法汇合 | CX-044, CX-045, CX-046, CX-047, CX-027 | engineering |
| CX-049 | P3 | 5级潮门塔阀门与首领 | CX-048 | engineering |
| CX-050 | P3 | 战斗结算、撤回和继续存档 | CX-048, CX-014, CX-016 | engineering |
| CX-051 | P3 | Godot战斗表现与资料往返 | CX-049, CX-050, CX-039 | engineering |
| CX-052 | P3 | 全部天赋、装备、补给触发回归 | CX-041, CX-042, CX-045, CX-051 | engineering |
| CX-053 | P3 | Q102三方法真实样章内部验收 | CX-038, CX-048, CX-051, CX-052 | engineering |
| CX-054 | P4 | 十三人物与四层知识投影 | CX-028, CX-034 | engineering |
| CX-055 | P4 | 事件插槽与前景排他调度 | CX-054, CX-023 | engineering |
| CX-056 | P4 | 招募、拒绝、再邀请与安全离队 | CX-054, CX-047, CX-042 | engineering |
| CX-057 | P4 | 关系事件与字段化私人许可 | CX-054, CX-019 | engineering |
| CX-058 | P4 | 五组织服务与六地区双职能协议 | CX-057 | engineering |
| CX-059 | P4 | 六塔物资守恒与实际派工 | CX-058, CX-013, CX-042 | engineering |
| CX-060 | P4 | 世界旅行、快捷返程和共享微光号 | CX-025, CX-055, CX-056 | engineering |
| CX-061 | P4 | 事务/协议/准备视图与可见边界 | CX-058, CX-059, CX-060 | engineering |
| CX-062 | P4 | 角色、商店、收藏和重配正式UI | CX-041, CX-042, CX-039 | engineering |
| CX-063 | P4 | 个人线框架与不膨胀奖励 | CX-056, CX-057, CX-055 | engineering |
| CX-064 | P4 | 24页面状态矩阵和无障碍联调 | CX-033, CX-039, CX-061, CX-062, CX-063 | engineering |
| CX-065 | P4 | 序章至第一章连续内部集成 | G2, CX-063, CP-R0-03, CP-R1-03 | engineering |
| CX-066 | P5 | 音频/本地化/词典制作工具链 | CX-010 | engineering |
| CX-067 | P5 | 可选帮助网关与DisabledProvider | CX-033, CX-019 | engineering |
| CX-068 | P5 | 响应核验、缓存分区和晚到路由 | CX-067 | engineering |
| CX-069 | P5 | 实际供应商候选与回归适配 | CX-068 | engineering / 默认关闭 |
| CX-070 | P5 | 复习调度与同档主题内容索引 | CX-033, CX-066 | engineering |
| CX-071 | P5 | 完整读听写检查与校准证据管理 | CX-070, CX-031 | engineering |
| CX-072 | P5 | 隐私、注入和世界写权限防线回归 | CX-068, CX-019, CX-021, CX-055, CX-071 | engineering |
| CX-073 | P6 | 28级最低路径终章实战调参 | CX-051, CX-052, CX-059, CP-R6-03, CP-RF-03 | engineering |
| CX-074 | P6 | 三种结局与条件非干扰矩阵 | CX-073, CX-061, CP-RF-03 | engineering |
| CX-075 | P7 | 性能、容量和长会话负载 | CX-064, CX-074, G5 | engineering |
| CX-076 | P7 | Windows故障、升级与恢复矩阵 | CX-072, CX-075 | engineering |
| CX-077 | P7 | 干净Windows候选包与权利清单 | CX-076, CX-066 | engineering |
| CX-078 | P7 | 全量内容与全流程回归执行 | CX-077, G5 | engineering |
| CX-079 | P7 | 逐项发布门核对与证据汇总 | CX-078, G6 | engineering |
| CX-080 | P7 | 经批准的本地交付与用户操作说明 | G7 | engineering |
| CP-R0-01 | CONTENT | 序章：证据/动作完整制作卡与原创正文 | CX-001 | content_authoring |
| CP-R0-02 | CONTENT | 序章：中文、词条、音轨与视觉素材生产 | CP-R0-01, CX-066 | content_production |
| CP-R0-03 | CONTENT | 序章：正式场景、机关与任务逐节点接入 | CP-R0-01, G2, CX-061 | chapter_integration |
| CP-R0-04 | CONTENT | 序章：地方/同伴/生活支线与可选预算 | CP-R0-03, CX-063 | content_integration |
| CP-R0-05 | CONTENT | 序章：内部完整章回归、连续过渡与体验采样 | CP-R0-02, CP-R0-03, CP-R0-04, CX-064, CX-070, CX-071 | chapter_qa |
| CP-R0-06 | CONTENT | 序章：真人审校、审听与6.5条件校准 | CP-R0-05 | human_calibration |
| CP-R1-01 | CONTENT | 盐雾港：证据/动作完整制作卡与原创正文 | CX-001 | content_authoring |
| CP-R1-02 | CONTENT | 盐雾港：中文、词条、音轨与视觉素材生产 | CP-R1-01, CX-066 | content_production |
| CP-R1-03 | CONTENT | 盐雾港：正式场景、机关与任务逐节点接入 | CP-R1-01, G2, CX-061 | chapter_integration |
| CP-R1-04 | CONTENT | 盐雾港：地方/同伴/生活支线与可选预算 | CP-R1-03, CX-063 | content_integration |
| CP-R1-05 | CONTENT | 盐雾港：内部完整章回归、连续过渡与体验采样 | CP-R1-02, CP-R1-03, CP-R1-04, CX-064, CX-070, CX-071 | chapter_qa |
| CP-R1-06 | CONTENT | 盐雾港：真人审校、审听与6.5条件校准 | CP-R1-05 | human_calibration |
| CP-R2-01 | CONTENT | 苔林修院：证据/动作完整制作卡与原创正文 | CX-001 | content_authoring |
| CP-R2-02 | CONTENT | 苔林修院：中文、词条、音轨与视觉素材生产 | CP-R2-01, CX-066 | content_production |
| CP-R2-03 | CONTENT | 苔林修院：正式场景、机关与任务逐节点接入 | CP-R2-01, G4, CX-061 | chapter_integration |
| CP-R2-04 | CONTENT | 苔林修院：地方/同伴/生活支线与可选预算 | CP-R2-03, CX-063 | content_integration |
| CP-R2-05 | CONTENT | 苔林修院：内部完整章回归、连续过渡与体验采样 | CP-R2-02, CP-R2-03, CP-R2-04, CX-064, CX-070, CX-071 | chapter_qa |
| CP-R2-06 | CONTENT | 苔林修院：真人审校、审听与6.5条件校准 | CP-R2-05 | human_calibration |
| CP-R3-01 | CONTENT | 沉钟矿区：证据/动作完整制作卡与原创正文 | CX-001 | content_authoring |
| CP-R3-02 | CONTENT | 沉钟矿区：中文、词条、音轨与视觉素材生产 | CP-R3-01, CX-066 | content_production |
| CP-R3-03 | CONTENT | 沉钟矿区：正式场景、机关与任务逐节点接入 | CP-R3-01, G4, CX-061 | chapter_integration |
| CP-R3-04 | CONTENT | 沉钟矿区：地方/同伴/生活支线与可选预算 | CP-R3-03, CX-063 | content_integration |
| CP-R3-05 | CONTENT | 沉钟矿区：内部完整章回归、连续过渡与体验采样 | CP-R3-02, CP-R3-03, CP-R3-04, CX-064, CX-070, CX-071 | chapter_qa |
| CP-R3-06 | CONTENT | 沉钟矿区：真人审校、审听与6.5条件校准 | CP-R3-05 | human_calibration |
| CP-R4-01 | CONTENT | 玻璃学城：证据/动作完整制作卡与原创正文 | CX-001 | content_authoring |
| CP-R4-02 | CONTENT | 玻璃学城：中文、词条、音轨与视觉素材生产 | CP-R4-01, CX-066 | content_production |
| CP-R4-03 | CONTENT | 玻璃学城：正式场景、机关与任务逐节点接入 | CP-R4-01, G4, CX-061 | chapter_integration |
| CP-R4-04 | CONTENT | 玻璃学城：地方/同伴/生活支线与可选预算 | CP-R4-03, CX-063 | content_integration |
| CP-R4-05 | CONTENT | 玻璃学城：内部完整章回归、连续过渡与体验采样 | CP-R4-02, CP-R4-03, CP-R4-04, CX-064, CX-070, CX-071 | chapter_qa |
| CP-R4-06 | CONTENT | 玻璃学城：真人审校、审听与6.5条件校准 | CP-R4-05 | human_calibration |
| CP-R5-01 | CONTENT | 失名旧都：证据/动作完整制作卡与原创正文 | CX-001 | content_authoring |
| CP-R5-02 | CONTENT | 失名旧都：中文、词条、音轨与视觉素材生产 | CP-R5-01, CX-066 | content_production |
| CP-R5-03 | CONTENT | 失名旧都：正式场景、机关与任务逐节点接入 | CP-R5-01, G4, CX-061 | chapter_integration |
| CP-R5-04 | CONTENT | 失名旧都：地方/同伴/生活支线与可选预算 | CP-R5-03, CX-063 | content_integration |
| CP-R5-05 | CONTENT | 失名旧都：内部完整章回归、连续过渡与体验采样 | CP-R5-02, CP-R5-03, CP-R5-04, CX-064, CX-070, CX-071 | chapter_qa |
| CP-R5-06 | CONTENT | 失名旧都：真人审校、审听与6.5条件校准 | CP-R5-05 | human_calibration |
| CP-R6-01 | CONTENT | 风暴边境：证据/动作完整制作卡与原创正文 | CX-001 | content_authoring |
| CP-R6-02 | CONTENT | 风暴边境：中文、词条、音轨与视觉素材生产 | CP-R6-01, CX-066 | content_production |
| CP-R6-03 | CONTENT | 风暴边境：正式场景、机关与任务逐节点接入 | CP-R6-01, G4, CX-061 | chapter_integration |
| CP-R6-04 | CONTENT | 风暴边境：地方/同伴/生活支线与可选预算 | CP-R6-03, CX-063 | content_integration |
| CP-R6-05 | CONTENT | 风暴边境：内部完整章回归、连续过渡与体验采样 | CP-R6-02, CP-R6-03, CP-R6-04, CX-064, CX-070, CX-071 | chapter_qa |
| CP-R6-06 | CONTENT | 风暴边境：真人审校、审听与6.5条件校准 | CP-R6-05 | human_calibration |
| CP-RF-01 | CONTENT | 第七座灯塔：证据/动作完整制作卡与原创正文 | CX-001 | content_authoring |
| CP-RF-02 | CONTENT | 第七座灯塔：中文、词条、音轨与视觉素材生产 | CP-RF-01, CX-066 | content_production |
| CP-RF-03 | CONTENT | 第七座灯塔：正式场景、机关与任务逐节点接入 | CP-RF-01, G4, CX-061 | chapter_integration |
| CP-RF-04 | CONTENT | 第七座灯塔：地方/同伴/生活支线与可选预算 | CP-RF-03, CX-063 | content_integration |
| CP-RF-05 | CONTENT | 第七座灯塔：内部完整章回归、连续过渡与体验采样 | CP-RF-02, CP-RF-03, CP-RF-04, CX-064, CX-070, CX-071, CX-073, CX-074 | chapter_qa |
| CP-RF-06 | CONTENT | 第七座灯塔：真人审校、审听与6.5条件校准 | CP-RF-05 | human_calibration |
| HV-001 | P0 | 原生Windows输入、读屏和干净导出核验 | CX-003, CX-004, CX-005 | human_verification |
| HV-002 | P4 | 第一可玩章目标用户可用性核验 | CX-065, CP-R0-05, CP-R1-05 | human_verification |
| HV-003 | P7 | 完整候选的干净Windows安装与恢复核验 | CX-077 | human_verification |
| HV-004 | P7 | 素材、声音、字体与发布权利核验 | CX-077 | human_verification |
| HV-005 | P7 | 全游戏6.5覆盖与校准证据复核 | CP-R0-06, CP-R1-06, CP-R2-06, CP-R3-06, CP-R4-06, CP-R5-06, CP-R6-06, CP-RF-06, CX-071 | human_verification |
| HV-006 | P7 | 隐私、范围和阻断风险独立复核 | CX-072, CX-076 | human_verification |
| HV-007 | P7 | 项目负责人作出交付决定 | CX-079 | human_verification |
| HV-008 | P5 | 可选在线模型的真实质量和数据条款核验 | CX-069 | human_verification / 默认关闭 |

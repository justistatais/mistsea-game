# R2作者候选交接

版本`r2-author-candidate-1`，文字/几何ReviewStatus `original_unreviewed`，审校与权利`human_pending`。这里只冻结来源可追溯的作者候选，没有服务、实际音轨、原生验收或可玩声明。不要复制到`game/content`或manifest，等实际服务接入与独立验证。

## 文件与读取边界

`learner_package.json`：十场景二十配对出口、具名危险区域、实际站位、二十七份双语材料、五份无音轨脚本、七写作任务、四十二个可见操作。材料提供英文/译文，不提供任务答案；初始所有材料均未获取，画出材料位置不等于授予正文访问。

`production_cards.author_only.md`：四主线、米拉个人线、两个未注册可选材料卡、三独立处置完整制作条件、可行解、三类三级提示、恢复与剩余项。不得装入玩家/NPC上下文。原M-12四级提示另保留，不被三级表删除。

`provenance.author_only.json`：已读原件SHA256、作者包指纹及仅结构层的本地核对；不是运行测试记录或Gate通过证据。

可用`ChapterTwoPackage.ParseJson`将玩家候选JSON只读投影为typed材料、站位、出口和危险定义；解析器不得读取作者答案文件。实际导出只含经过明确决定的玩家候选文件，并保持未审校/无音轨状态。Listening只有`listening_script`/书面抄记，UI不能标成已听wav或完整听力验收。

## 原始条款定位

| 对象 | 位置 |
|---|---|
| 八主场景+两可选 | Exploration 389–400行；part03 R2区原登记 |
| 十双向连接 | Exploration 402–419行，严格406–415十行 |
| 四节点契约 | part04 quests[7..10]；World Story 410–453行 |
| 读听写保留 | part05 curriculum[7..10]；English Learning 786–789行 |
| M-12冻结顺序/未知分钟 | Puzzle 373–405、784–786行；part06 units[5]；part06-Puzzle_Materials两个PZTIME原文 |
| 奖励 | part07 main_rewards[7..10]、chapter_budgets R2；Character Progression 584–587行 |
| 第二塔真实需求 | part07 engineering.towers[1]及baseline_source、procurement；part09 regional_mandates[1] |
| 三独立处置 | part08 encounters[6..8]；Combat 304、501–503行 |
| 本章知识边界 | part09 K-MARA-D1-ALIVE、K-MARA-SIGNATURE；World Story 435–437、691–708行 |
| W-04个人线 | NPC 313–327行；part09 personal_nodes CQ-MIRA-01 |
| 稿件、资料与预览 | part10 UI21/UI22/UI30..UI33；权限/输入/战斗只读契约 |

所有A/B/C人数、TR-D1-7/MV/F-17、IL-2/TS-18/MC-2、地方6/4/3负荷及可选六床配置都在材料中明列原创局部细节，不是新增高层正史或未来地址映射。M-1214:10/14:40/16:05与准确授权未知沿冻结原件。

## 真实服务入口与接口

建议服务暴露：`Package`、`Geometry`、`CurrentGeometryAsync`、`InitializeRegionAsync`、`ViewAsync`、`MoveAsync`/`TraverseAsync`/`EnterPortAsync`、`DiscoverAtAsync`/`OpenMaterialAsync`、`ExecuteStationAsync`、`SaveDraftAsync`/`ConfirmMeaningAsync`、`PreviewSubmissionAsync`/`SubmitAsync`、共享`EncounterPreview/Begin/ActionPreview/Confirm/Withdraw`。构造`(IAdventureRepository, RuntimeCatalog, ChapterTwoPackage)`。不读取生产卡答案，不执行内容提供的任意实体写入。

入口必须检查真实Q104任务收束/使用权来源、唯一`ship:glimmer`、当前`DockRegion=R2`、`berth:R2`和有效`sea-route:R1↔R2`当前来源，以及实际`sail_shared_glimmer`到R2的回执；演员已在R2-S01安全登陆站位。不能只看到`ActorState.Scene=R2-S01`就初始化。可由实际合法跨区边界事件替代航行，仍须有同等可核验来源，不能裸传送。

原生船路接入应沿现有Travel：实际来源`sea_route_verified`→`RegisterSeaRouteAsync`，`vessel_berth_verified`→`RegisterBerthAsync`，实际船舱`HUB-S02`海图站位→`SailAsync("R2")`。传入R2真实几何；缺目标几何必须拒绝。候选`R2-S01.berth=(48,90)`，操作登记`arrival=(26,65)`。初始化只建立地方世界和未接受任务，不加经验，不搬演员，不复制船。

## 需要持久化的状态

材料回执分别保存ID、内容/翻译版本、英文指纹、source_root、实际获取站位、许可/观察来源。副本保持同根。仅已获取且权限仍满足的材料进入玩家档案与NPC上下文；未来章节知识没有入口。

地方住区演员与三组实际观察分开：初始存在不是已走访；自愿确认范围具体化；官方/厨房/安置/三组观察各保留用途。身份请求将字段、接收方、用途、版本分别存为权限，不以“档案有钥匙”推定本人同意。

输送器配置、四动作手动处置、夹具安装、排水与复查按当前修订；备用源配置、唯一连接头、负荷档、通电和试验按当前修订。危险区解除必须依赖有效实际ENC结果与安全检查，不依赖material-acquired。S05根道→ENC01；S06湿电区→隔离/ENC02/夹具/排水/当前复查；S07核心区→ENC03完整支路安全结果。保持旧检查文字可回看，派生安全不得复用失效检查。

每个任务有独立草稿、意思确认、预览与实际动作。保存使用仓库草稿CAS；改稿使旧hash确认失效。只读预览冻结会话load、分支、actor/actionClock、当前材料/权限/设备/任务读戳、草稿版本、明确结构化辅助选择和指令ID。实际发送再校验全部，未知结果只重试同指令；不根据英文字符串填正确事实。

NPC/发送/移动遵守前景事件租约；活动战斗期间编辑可以只读，移动/工程/旅行/装备等按现有域拒绝。ENC01/02/03使用共享引擎，尝试/阶段/结果唯一；不可将通用receipt字符串直接变成受控旗标。

Q203写当前TowerSafety后，Baseline/Trial/Operators/Supply/Enhanced仍缺。双岗位本班已有人是当班事实，不是新协议岗位授权。`ROLE-R2-OP`/`ROLE-R2-REP`和protocol-d3-v1要后续真实委托。第二塔保全物资及SUP-GROVE交付沿工程账独立流程；地方连接头不转工程资源。

Q204实际交接才形成R3核查方向；物理航行与第三章入口另有服务，不自动到矿区。CQ-MIRA01真实25XP/20刻币从原支线预算，未注册两可选卡不猜奖励。

## 必要验证与剩余

需要实际SQLite覆盖：裸R2 actor/伪造船/无航路拒绝；合法真实船路进入；远距离与未授权取件拒绝；三门廊实际核查；同源副本不增票；M-12准确分钟未知可完成；D−1不生成当前存活；Q203先于Q202仍可做；三遭遇各自结果；隔离/夹具/水位当前修订；两负荷方法与超载拒绝；塔站实际检查后Safe而基础增强空；改稿/改权限/旧load/旧预览CAS；实际三种交接路径；复读/继续/回访不重奖；米拉拒绝不阻断；W-04超范围和真实可选交付。

尚未执行：以上运行验证、真实场景AStar/碰撞和真人录像、三场候选平衡、正式美术、五条音轨/演出者与声音权利、人类文本译文审校、Academic6.5校准、完整Task1/Task2评价、两可选独立合同/奖励、第三章与后续章验收。作者文件完成不使这些项目完成。

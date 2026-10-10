from pathlib import Path
import json,re,collections
ROOT=Path('/mnt/data/game_design/part13/MistSea_Codex_Handoff_v0.1')
NAMES=['Game_Design_Core','World_Story_Bible','Exploration_Level_Design','Quest_Event_System','English_Learning_System','Puzzle_System','Character_Progression','Combat_System','NPC_Faction_System','UI_UX_Spec','Content_AI_Pipeline','Technical_Architecture']
DOCS={i+1:'docs/baseline/'+n+'_v0.1.md' for i,n in enumerate(NAMES)}
TASKS=[]
def write(rel,s):
 p=ROOT/rel;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(s.strip()+'\n',encoding='utf-8')
def dump(rel,x):
 p=ROOT/rel;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(x,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
def T(num,phase,title,deps,parts,paths,goal,outputs,checks,feature=None,kind='engineering'):
 ident=f'CX-{num:03d}' if isinstance(num,int) else num
 TASKS.append({'id':ident,'phase':phase,'title':title,'kind':kind,'owner_role':'Codex工程实现' if kind=='engineering' else '内容负责人＋Codex辅助','depends_on':deps.split() if isinstance(deps,str) else deps,'references':[DOCS[p] for p in parts], 'allowed_paths':paths.split(), 'objective':goal, 'deliverables':outputs.split('；'),'acceptance':checks.split('；'),'feature':feature,'initial_status':'not_started','evidence_path':f'reports/execution/{ident}.json','scope_note':'单独审查的一项工作；内容或设备条件不足应明确记录blocked，不允许用固定true/假音轨/假真人审批填补。','production_claim':'none_until_task_evidence_accepted'})
# P0: platform qualification is a real gate, not a claim made in this design package.
T(1,'P0','接管文档、库存与执行账','',[1,5,12], 'docs/ reports/ planning/state/ .gitignore',
  '把本交接目录作为项目根；核对原件、当前仓库与已有工作，建立事实上的起点，不生成游戏完成声明。',
  '报告12文档、10来源包和已核对版本；仓库现有文件与工具环境报告；可用与缺失项清单',
  '运行tools/verify_handoff.py；运行tools/plan.py next，CX-001为首项；保留任何既有用户代码，不初始化覆盖或强制清理；工程任务初始均未完成')
T(2,'P0','核实并锁定真实工具链','CX-001',[12], 'spikes/ toolchain/ reports/ global.json Directory.Packages.props',
  '核实Godot.NET编辑器与模板、.NET SDK和SQLite提供者，生成实际锁文件与环境证据。',
  '实际版本与二进制/包来源记录；还原生成的依赖锁与漏洞审计；离线失败和平台缺失报告',
  '版本与toolchain_baseline对照；不将Godot包版本与普通NuGet版本混配；没有环境只能blocked，禁止伪造哈希或输出；不为追逐最新版静默升级')
T(3,'P0','原生编辑器、焦点和可访问性技术试件','CX-002',[10,12], 'spikes/ui/ reports/',
  '在小Godot C#试件中验证中英混输、长文、200%字号、键盘路由和读屏入口。',
  '能实际运行的输入技术试件；3000词与组合字符样本；录屏或步骤与结果',
  '输入WASD/Enter不触发场景；大字重排可到达末行按钮；合成IME测试与真IME测试分开；无真实读屏时不得写兼容通过')
T(4,'P0','SQLite原生集成与事务试件','CX-002',[12], 'spikes/storage/ reports/',
  '在候选.NET环境上验证真实SQLite装载、显式事务、回滚和一致备份。',
  '可执行存储试件；记录实际原生版本；Windows候选导出中的库路径',
  '两表事务同成同败；数据库忙返回明确状态；原生库缺失可诊断；只在Linux通过不替代Windows证据')
T(5,'P0','最小Windows离线导出候选','CX-003 CX-004',[10,12], 'spikes/exports/ build/ reports/',
  '导出一个包含输入和SQLite试件的最小Windows候选；不要求终端用户安装SDK。',
  '候选运行目录与精确文件清单；版本和启动日志；干净环境检查说明',
  '离线启动与保存重开；无外发个人正文；读取失败不静默重置；未取得干净Windows证据时留待HV-001且不越过G0')
T(6,'P1','建立四程序集与组合根','G0',[12], 'src/ game/ tools/Mist.ContentCli/ services/Mist.Assist/ tests/ MistSea.sln',
  '建立Core、Application、Infrastructure、Godot客户端及可选网关空壳，规定单向依赖。',
  '真实可构建解法；模块端口和测试工程；离线组合根',
  'Core无需Godot/SQL/HTTP可编译；AI接口没有世界写端口；不将参考Python作为正式运行依赖')
T(7,'P1','构建、测试与证据命令统一','CX-006',[12], 'tests/ tools/ci/ .github/ Directory.Build.props reports/',
  '为当前实际代码提供可重复构建、单元测试和候选打包入口，记录未验证项目。',
  '构建/测试脚本；真实依赖锁检查；测试结果目录约定',
  '非零退出不得吞掉；跳过与通过分开；无生产源码的占位测试不计产品覆盖；不依赖本会话绝对路径')
T(8,'P1','稳定ID、版本、时钟与命令DTO','CX-006',[4,5,12], 'src/Mist.Core/Contracts/ src/Mist.Application/Contracts/ tests/Contracts/ docs/contracts/',
  '定义任务、实例、分支、尝试、命令、工作结算槽、草稿与删除世代，不合并不同身份。',
  '强类型契约；序列化样例；版本兼容策略',
  '重复JSON字段与未知关键枚举拒绝；实时时间不推进剧情危机；查询无副作用；钱/数量用整数，战斗公式使用约定精确计算')
T(9,'P1','九类源适配器与奖励旧名映射','CX-008',[3,4,5,6,7,8,9,10,11,12], 'tools/Mist.ContentCli/Import/ content/source/ tests/ContentImport/ docs/contracts/',
  '逐类导入第3—11部分参考数据，保留来源及旧ID，采用第七部分真实奖励映射。',
  '九类适配器；字段映射与拒绝报告；Q-102规范材料投影',
  '未认识字段不能默认为0；REWARD-*-TBD必须映射或阻断；英文正文逐字符/哈希守恒；作者解答不进入普通材料投影')
T(10,'P1','内容编译与内部/正式发布检查','CX-009',[5,11,12], 'tools/Mist.ContentCli/Validation/ schemas/ content/manifests/ tests/Content/',
  '检查引用、条件图、材料版本、依赖、声音和校准记录，内部候选与正式构建分开。',
  '内容lint与构建CLI；内部候选清单；正式发布拒绝原因',
  '同ID同版本改正文拒绝；缺关键音轨或真审核/校准阻断正式发布；SYNTHETIC仅测试；学习目标必须6.5且自动升档关闭')
T(11,'P1','领域查询、动作计划和相关版本读取集','CX-008',[4,6,12], 'src/Mist.Core/Conditions/ src/Mist.Application/Commands/ tests/Commands/',
  '把预览与执行分开，记录真正相关的配置/正文/权限版本，三值与故障明确处理。',
  '只读检查结果；候选变更计划；读取集与失败原因类型',
  '查词不使预览失效；电池改变使预览失效；未知不可视为安全；空草稿不冒充独立写作')
T(12,'P1','玩家主库与独立控制库schema','CX-007 CX-008',[12], 'src/Mist.Infrastructure/Storage/ schemas/sql/ tests/Storage/',
  '建立真实主库、控制库和外键/唯一约束；世界共同事务不能跨库分裂。',
  '可重复建库迁移；领域存储接口；表所有权表',
  '船/奖励/回执同事务域；删除世代不在可回档世界里；DELETE/FULL配置有实际记录；不存在每模块私有任务完成布尔值')
T(13,'P1','单写队列与原子提交','CX-011 CX-012',[4,12], 'src/Mist.Application/Transactions/ src/Mist.Infrastructure/Storage/ tests/Transactions/',
  '所有持久世界写入经单写队列，短事务一起应用经验证变更。',
  '命令执行器；真实SQLite集成测试；主线程只读投影',
  '写入中途异常全部回滚；数据库忙不冻结编辑器；不在线程中直接改Godot节点；失败不半扣物品')
T(14,'P1','命令去重、工作去重与表现outbox','CX-013',[4,12], 'src/Mist.Application/Receipts/ src/Mist.Infrastructure/Storage/ tests/Receipts/',
  '分别实现同请求与同世界工作的单次提交，表现消费不再次写奖励。',
  '命令账/工作槽/outbox；重传查询路径；丢响应场景测试',
  '同ID换载荷拒绝；换ID同工作不能重奖；提交后丢响应恢复旧回执；重播动画不改世界')
T(15,'P1','继续、旧档分支、槽位与检查点','CX-014',[1,10,12], 'src/Mist.Application/Saves/ src/Mist.Infrastructure/Storage/ tests/Saves/',
  '实现6手动5滚动与保护点；继续与显式读旧档使用不同语义。',
  '完整检查点；新分支继承回执；旧点击失效令牌',
  '离港前读档能在新分支领奖一次；离港后不能重领；学习不回退；继续不新增遭遇实例；自动槽不覆盖保护点')
T(16,'P1','战斗重试与局部回滚范围','CX-015',[4,8,12], 'src/Mist.Application/EncounterRetry/ tests/Retry/',
  '为战前快照和受控局部变量建立恢复边界，不用整库恢复处理战败。',
  '遭遇入口快照契约；尝试记录；恢复差异',
  '最新草稿和已得材料保留；已交付前段运输不退回；阶段/加载不补给；恢复消耗不能保留成功奖励')
T(17,'P1','规范草稿版本、CAS与多入口','CX-013',[4,10,12], 'src/Mist.Application/Drafts/ src/Mist.Infrastructure/Storage/ tests/Drafts/',
  '同任务一个规范草稿，持久修订与当前头指针；晚保存不能覆盖新稿。',
  '草稿仓储与版本比较；恢复入口；保存失败导出数据',
  '三个入口同一DraftId；旧版ACK不标新稿已存；设备修复不改正文；先前分支稿保留但不灌入当前分支')
T(18,'P1','学习观察、帮助与模态账','CX-017',[5,10,12], 'src/Mist.Core/Learning/ src/Mist.Application/Learning/ tests/Learning/',
  '记录材料家族、尝试、实际帮助和输入模态；不实现假分数。',
  '观察事件；支持范围；有来源的状态视图',
  '预加载/播放完成不等于理解；看字幕不算纯听力；同题重试不算陌生材料；失败持久化标条件不完整；无世界写权限')
T(19,'P1','删除世代、外发许可与晚反馈','CX-015 CX-017 CX-018',[5,9,10,11,12], 'src/Mist.Application/Privacy/ src/Mist.Infrastructure/Storage/ tests/Privacy/',
  '先记录删除控制再清理主库；撤回请求、旧稿与旧分支反馈分别处理。',
  '删除账与恢复拦截；请求撤权；异步路由测试',
  '旧主库恢复不复活已删记录；旧反馈只进入相应允许位置；正文删除不假装抹掉现实回执；外发同意不来自打开页面')
T(20,'P1','一致备份、损坏隔离与迁移副本','CX-019',[12], 'src/Mist.Infrastructure/Recovery/ tools/recovery/ tests/Recovery/',
  '用一致备份API生成候选副本，校验后切换；保留正常副本与损坏原件。',
  '备份/还原/迁移流程；失败报告；控制库缺失恢复说明',
  '迁移失败原库可用；高版本拒写；不直接复制活库主文件充当备份；清理受管理备份不误删用户外部文件')
T(21,'P1','精确内容绑定与安全安装器','CX-010 CX-020',[11,12], 'src/Mist.Infrastructure/Content/ tools/Mist.ContentCli/Packaging/ tests/ContentInstall/',
  '冒险固定兼容集合、任务固定依赖；本地包验证路径/大小/签名和迁移。',
  '包清单；安装暂存与切换；旧包恢复和隔离通路',
  '旧音轨不配新答案；越界路径与可执行夹带拒绝；哈希不冒充发布签名；严重问题可隔离并保留存档修复方向')
T(22,'P1','真实持久化破坏性测试套件','CX-014 CX-015 CX-016 CX-017 CX-018 CX-019 CX-020 CX-021',[12], 'tests/IntegrationStorage/ tools/ci/ reports/',
  '对真实.NET/SQLite实施子进程终止、双连接争抢和恢复，不只复跑Python模型。',
  '可复现故障脚本；实际版本日志；G1证据',
  '原状态或完整新状态二选一；同请求与工作不重复；旧档/草稿/删除矩阵成立；进程终止不宣称断电实验')
T(23,'P2','Godot应用壳与输入/暂停/焦点','G1 CX-003',[3,8,10,12], 'game/UI/Shell/ game/Input/ game/Composition/ tests/UIRouting/',
  '落实六入口、互斥输入拥有者和嵌套暂停；用技术试件结果建立正式控件壳。',
  '路由与返回栈；重映射按键；只读战斗界面边界',
  '关闭词典文献仍暂停；Enter不穿透；编辑器内快捷键只归编辑；背景点击不执行')
T(24,'P2','主角移动、相机与可达操作站位','G1 CX-008',[3,10,12], 'game/World/Movement/ game/World/Camera/ tests/Movement/',
  '实现点击和键盘等价移动，结合角色半径而非只验证点路径。',
  '导航碰撞；坐标转换；接近再交互',
  '不能隔墙取件；超宽/缩放不偏移点击；危险区先确认；装饰人群不永久堵路')
T(25,'P2','Q-102六场景与成对出口','CX-024 CX-009',[3,4,10], 'game/World/Scenes/R1/ content/source/R1/ tests/SceneLinks/',
  '用既有R1-S02至S07真实ID与灰盒几何建立六场景，不另造文字菜单版。',
  '七连接与落点；门与返回变体；场景状态读取',
  '14方向出口正确；不可达站位有原因；开内闩保持捷径；加载失败留在安全状态')
T(26,'P2','拾取、安装与设备归属链','CX-025 CX-013',[3,4,7,12], 'src/Mist.Core/Devices/ src/Mist.Application/Devices/ game/World/Interactions/ tests/Devices/',
  '让支架、电池与灯本体拥有稳定实例和唯一位置，场景不复制权威字段。',
  '货架→持有→安装转移；设备配置版本；保存恢复',
  '安装后背包不留副本；关键物不可误售；读档不重生；主发电机与工坊储备分开')
T(27,'P2','工坊停机、保持检查与侧道','CX-026',[3,6], 'src/Mist.Core/Puzzles/Workshop/ game/World/Scenes/R1/ tests/Workshop/',
  '实现active/focused/isolated/disabled与双向扫描风险，保留内闩捷径。',
  '固定测试灯模式；局部隔离；当前状态提示输入',
  '阅读不取消保持；无需拆船电池；潜入返回不穿扫描；改变方法不重置物品或奖励')
T(28,'P2','任务生命周期与派生目标','CX-009 CX-014',[4,6,12], 'src/Mist.Core/Quests/ src/Mist.Application/Quests/ tests/Quests/',
  '三生命周期、历史回执/现况/版本证明/确认方案；允许提前与并行工作。',
  '任务查询与合法转换；依赖图检查；不泄谜日志投影',
  '先取件后接受合法承接；ready可以失效；closed不由AI写；主线无普通永久失败；Q602/Q603无循环')
T(29,'P2','阅读器、词典和分范围帮助','CX-023 CX-009',[5,10], 'game/UI/Reader/ game/UI/Help/ src/Mist.Application/Materials/ tests/Reading/',
  '真实显示已得原文，保留段落、选区、词义和返回位置。',
  '中英字号布局；本地词条；帮助范围观察',
  '正文不烘焙图片；查词不弹整题答案；未发现材料不进搜索/读屏；帮助不扣资源')
T(30,'P2','真实音频、字幕与缺失通路','CX-029 CX-018',[5,10,11], 'game/Audio/ game/UI/Listening/ tests/Audio/',
  '播放器绑定音轨与脚本版本，提供暂停、定位、速度、字幕与明确失败。',
  '真实播放器；失败脚本入口；播放模态记录',
  '无文件不走假进度；查词音不互相抢音；读脚本不记听力；位置保留且不补算暂停损失')
T(31,'P2','规范写作工作区与可靠保存提示','CX-029 CX-017',[4,5,10], 'game/UI/Writer/ src/Mist.Application/Drafts/ tests/Writer/',
  '多入口同稿；材料/正文/反馈分区；保存、核对、评价、执行分别表达。',
  '可编辑真实正文；大字切换；保存失败导出',
  '真实回执才已保存；IME候选不上传；旧反馈不覆盖；Ctrl+Enter默认不发送')
T(32,'P2','六类证据与谜题通用控件','CX-029 CX-028',[6,10], 'game/UI/Evidence/ game/UI/Puzzles/ tests/PuzzleControls/',
  '比较、排序、地图、分配、设备、沟通复用现有工作区，键鼠等价。',
  '六类控件；玩家假设与事实分层；部分顺序与多解交互',
  '拖线不建立事实；键盘可完成；未知不强填；导航不直接标正确航道')
T(33,'P2','离线学习反馈与证据界面','CX-018 CX-029 CX-031',[5,10], 'game/UI/Learning/ src/Mist.Application/Learning/ tests/LearningUI/',
  '显示目标6.5与真实证据状态，未评估与0分区别，基础帮助离线可用。',
  '六类学习证据视图；局部固定反馈；可选练习入口',
  '短留言无完整band；播放/点击数不冒充掌握；高辅助仍能主线；无模型可正常使用')
T(34,'P2','哈文交谈、提议与临时占用','CX-023 CX-028',[4,9,10], 'game/UI/Dialogue/ src/Mist.Application/Events/ content/source/R1/ tests/Dialogue/',
  'Q102两个交接入口指向同一任务，交談读取当前已知事实。',
  '固定分支对白；NPC占用；暂停恢复',
  '不复制哈文；高优先事件不打断写作；已拿支架对白兼容；查看提议不自动作承诺')
T(35,'P2','意思候选与显式结构化兜底','CX-031 CX-028',[4,5,11], 'src/Mist.Application/Proposals/ game/UI/Proposal/ tests/Proposals/',
  '自由写作常态，受控选择是明确标注的辅助；意图确认不能执行世界。',
  '正文与意图版本；未知字段；澄清界面',
  'B不能静默改C；空白不记独立完成；正文修改失效旧确认；保留条件式计划不误报已施工')
T(36,'P2','导航灯当前检查与执行预览','CX-026 CX-027 CX-035',[4,6,10,12], 'src/Mist.Application/Q102/ game/UI/Confirmation/ tests/Q102/',
  '当前配置证书与精确读取集；查阅帮助不使预览无意义失效。',
  '两种安装顺序；检查结果；离港预览',
  '拔插后重测；未修好不能出发；预览过期不扣物；读档/关闭预览后旧令牌不可执行')
T(37,'P2','离港原子结果与单次奖励','CX-036 CX-014 CX-042',[3,4,7,12], 'src/Mist.Application/Q102/ game/World/Scenes/R1/ tests/Q102Transaction/',
  '按现行奖励和物品归属提交离港；动画通过outbox播放。',
  '160经验/60刻币/2零件/RE01；玩家留港；船与材料去向',
  '任一点中断原态或新态；同工作只结算一次；支架电池随船；不提前移交微光号或关闭Q104')
T(38,'P2','Q102两条非战斗路线端到端','CX-030 CX-032 CX-033 CX-034 CX-037',[3,4,5,6,10], 'tests/E2E/Q102/ reports/ game/Debug/',
  '在Godot六场景中走停机和潜入，使用实际存储、自由草稿及辅助确认。',
  '两条完整录像/日志；乱序路线；内部候选标识',
  '不通过debug直接完成；可暂缺音轨但明确未验证听力；不假造band；从场景回草稿再离港仍一致')
T(39,'P2','世界HUD、任务和存档菜单集成','CX-023 CX-015 CX-031',[1,3,4,10], 'game/UI/HUD/ game/UI/Tasks/ game/UI/SaveMenu/ tests/Navigation/',
  '让目标、来路、合法动作与保存范围清楚可见，24页面使用统一返回与焦点。',
  'HUD；任务列表；手动/自动/保护点界面',
  '取消跟踪不放弃；未知线索数量不泄露；读档明确覆盖范围；地图按钮不直接完成旅行')
T(40,'P3','经验、属性、校准和主线奖励','CX-009 CX-013',[7], 'src/Mist.Core/Progression/ src/Mist.Application/Progression/ tests/Progression/',
  '导入30级曲线、五属性、阶位与32奖励，关联任务回执而非击杀/答题。',
  '公式与配置；章节预算；角色查询',
  '起步3级工坊/5级塔站/28级终章；19140主线总经验；辅助使用不改变奖励；击杀经验0')
T(41,'P3','36节点、前置与四主动槽','CX-040',[7], 'src/Mist.Core/Skills/ game/UI/Builds/ tests/Skills/',
  '技能点16、各树前置与层级，已学被动自动生效；免费安全洗点。',
  '类型化技能定义；构筑预览；非法分配拒绝',
  '每节点1点；前置与层投入均检查；洗点不再领奖；战斗中只读不换构筑')
T(42,'P3','28收藏、8改件和个人资源账','CX-040 CX-013',[7,9], 'src/Mist.Core/Inventory/ src/Mist.Application/Inventory/ tests/Inventory/',
  '真实装备实例占用、校准、转移改件和独立个人账，基础型号免费持有。',
  '物品和装备交易；制作升级；奖励保管处',
  '同遗物不能多人装备；收藏不售换币；改件消耗原件且可转移；工程物资不能被个人购物耗尽')
T(43,'P3','整队回合、个人计划槽与资源','CX-008 CX-016 CX-041',[7,8], 'src/Mist.Core/Combat/Timing/ tests/CombatTiming/',
  '我方先阶段，阵营内先手；主角3AP援助2AP，CD1隔完整个人回合。',
  '确定性时间线；冷却与补给次数；遭遇版本',
  '读档/过阶段不回满；倒地槽推进时钟；没有额外AP返还；同先手按稳定ID')
T(44,'P3','伤害、护盾、暴露与触发管线','CX-043 CX-042',[7,8], 'src/Mist.Core/Combat/Resolution/ tests/CombatResolution/',
  '统一多段合并、取整、上限和非递归触发；明确临时状态来源及到期。',
  '伤害与护盾运算；事件顺序；属性参考对照',
  '多段不多消耗暴露；护盾取较大不相加；弱防护不为强防护续期；确定取整不随平台改变')
T(45,'P3','稳定压力、装置、负面和倒地','CX-044',[7,8], 'src/Mist.Core/Combat/States/ tests/CombatStates/',
  '普通失衡取消与首领削弱区别；装置触发/回收预算；复活次轮行动。',
  '状态与压力机；援救次数；装置实例',
  '不永久连控首领；取消攻击不触发装置；回收不生免费能量；全队倒地才失败；净化不解除剧情机制')
T(46,'P3','四站位、目标类型和意图预告','CX-043 CX-024 CX-039',[8,10], 'src/Mist.Core/Combat/Targeting/ game/UI/CombatIntent/ tests/Targeting/',
  '人物/位置/全场锁定清楚；意图公布后不按玩家选择暗换目标。',
  '合法目标与换位；队列显示；预计损失',
  '人物锁定跟人；列锁定结算位置；无效目标不暗换；增援下一轮行动且总数最多4')
T(47,'P3','四同伴与两援勤的基础动作','CX-043 CX-045',[7,8,9], 'src/Mist.Core/Combat/Allies/ content/source/actors/ tests/Allies/',
  '实现6角色12专属基础动作及援助0.65A普攻，共享成长。',
  '行动定义；合法建议策略；固定属性模板',
  '不必带指定同伴；每人2AP；支援不等于免费额外动作；故事NPC与工程人员不复制')
T(48,'P3','3级工坊实战与方法汇合','CX-044 CX-045 CX-046 CX-047 CX-027',[3,6,7,8], 'game/Combat/Workshop/ src/Mist.Application/EncounterResults/ tests/WorkshopBattle/',
  '190生命机械按真实先手/意图/站位运行，使用3级校准0最低路径。',
  '工坊遭遇；胜利回到取件；三种起步队伍记录',
  '不装结算后的RE01；控制机械不直接给支架或发船；无杀敌经验；可撤回改用潜入/停机')
T(49,'P3','5级潮门塔阀门与首领','CX-048',[7,8], 'game/Combat/Tidegate/ content/source/R1/ tests/TidegateBattle/',
  '真实四位置工作点、每角色每回合至多一个现场操作，两阀→检查供给→制止。',
  '塔站首领候选；机关与战斗联动；数值比较报告',
  '只用5级校准0；不预支Q104校准奖励；阀顺序可换；打空生命不绕安全条件；候选需要实际玩家验证')
T(50,'P3','战斗结算、撤回和继续存档','CX-048 CX-014 CX-016',[4,7,8,12], 'src/Mist.Application/CombatSessions/ tests/CombatSave/',
  '冻队伍与装备；记录局部状态与消耗；结果由父任务消费。',
  '稳定动作快照；入口恢复；同一尝试与新尝试区分',
  '重播不奖；失败补回消耗不保留胜利；阶段不新增急救；主角倒地资料草稿仍可保存')
T(51,'P3','Godot战斗表现与资料往返','CX-049 CX-050 CX-039',[8,10], 'game/Combat/Presentation/ game/UI/Combat/ tests/CombatUI/',
  '用固定图/短动画表现Core结果，菜单显示3/2AP、意图、盾、稳定和明示费用。',
  '实际可玩的战斗界面；动画速度/减少动态；键盘目标流程',
  '动画跳过不跳结算；资料暂停不回血；退出输入不触发技能；文字说明不盖住实际目标')
T(52,'P3','全部天赋、装备、补给触发回归','CX-041 CX-042 CX-045 CX-051',[7,8], 'tests/CombatLoadouts/ content/source/balance/ reports/',
  '逐项迁移36节点、28装备、8改件、4补给，不把元数据存在当效果生效。',
  '效果级测试清单；触发与保存组合；未实现清单',
  '每个主动被动独立验证；循环触发预算；同伴遗物不复制；无付费补给最低路线可行需实测')
T(53,'P3','Q102三方法真实样章内部验收','CX-038 CX-048 CX-051 CX-052',[1,3,4,5,6,7,8,10,12], 'tests/E2E/Q102/ reports/',
  '三条方法使用同一真实场景任务存档，验证乱序、退出、帮助和单次奖励。',
  '固定种子与路径录像；存档前后快照；G2报告',
  '战斗/停机/潜入基本奖励一致；正文可真实输入；离港留港；缺音轨只能内部标记；不把G2冒充可发布6.5认证')
T(54,'P4','十三人物与四层知识投影','CX-028 CX-034',[2,9], 'src/Mist.Core/NPC/ src/Mist.Application/Knowledge/ tests/NPC/',
  '分开真相、人物认知、玩家证据、发布范围，用实际获知回执更新。',
  '人物目录；时间范围知识；安全话题视图',
  '章节不广播秘密；拒答不确认玩家猜测；同报告副本不增独立来源；关键证据不受好感锁')
T(55,'P4','事件插槽与前景排他调度','CX-054 CX-023',[4,9,10], 'src/Mist.Application/EventScheduler/ game/UI/Dialogue/ tests/EventScheduler/',
  '人物/站位/物件全部取得或不取得，剧情与生活事件在安全点调度。',
  '候选队列/暂停恢复；过期清理；稳定随机实例',
  '只有一个输入拥有者；写作不被打断；哈文不复制；暂停释放临时锁但不释放真实工作岗位')
T(56,'P4','招募、拒绝、再邀请与安全离队','CX-054 CX-047 CX-042',[7,8,9], 'src/Mist.Application/Roster/ game/UI/Party/ tests/Roster/',
  '四邀请时点、地方援勤、共享等级与遗物归还，战斗冻结名册。',
  '入队与离队流；晚入队选择；同伴状态显示',
  '莉娜Q104后才正式入队；无剧情同伴仍有两援助；离队不丢收藏；施工者不同时出战')
T(57,'P4','关系事件与字段化私人许可','CX-054 CX-019',[9,10,11], 'src/Mist.Application/Relationships/ src/Mist.Application/Disclosure/ tests/Disclosure/',
  '工作/私人交流/议题分开；许可绑定字段、对象、用途、版本。',
  '关系依据；预览/发送回执；修复流程',
  '字幕和语法不扣关系；投递一次；同意见面不由转交人代签；撤回不删除已发生事实；不能验证任意正文时澄清')
T(58,'P4','五组织服务与六地区双职能协议','CX-057',[2,4,9], 'src/Mist.Application/Factions/ content/source/agreements/ tests/Factions/',
  '根据实际授权与资源合作，不设声望门槛；地区操作方与代表方分别确认。',
  '组织条件查询；协议版本；签约与替代任职',
  '五组织不是六塔；一签名不能复制六份；政策分歧不成为隐藏失约；同伴任务不新增终局门槛')
T(59,'P4','六塔物资守恒与实际派工','CX-058 CX-013 CX-042',[4,7,9,12], 'src/Mist.Core/Engineering/ src/Mist.Application/WorkOrders/ tests/Engineering/',
  '有限来源、真实预留、运输、安装、值守与试运行，个人消费不能占工程唯一物资。',
  '工作单与回执；岗位容量；取消和补齐入口',
  '已安装不退款复制；试运行许可和最终协议不循环；不做其他任务也可推进合法工作步；Q602与Q603并行可汇总')
T(60,'P4','世界旅行、快捷返程和共享微光号','CX-025 CX-055 CX-056',[2,3,9], 'game/World/Travel/ game/World/Ship/ src/Mist.Application/Travel/ tests/Travel/',
  '已走通路径合并，安全锚点重访，同一船实例按停靠改变外观。',
  '旅行条件；两类返程点；三个船内空间',
  '护送货物不瞬移；旧塔安全不重置；拒绝莉娜不锁航行；普通旅行不收死锁燃料门票')
T(61,'P4','事务/协议/准备视图与可见边界','CX-058 CX-059 CX-060',[4,9,10], 'game/UI/WorkOrders/ game/UI/Agreements/ game/UI/Departure/ tests/WorkUI/',
  '显示库存、预留、交付、安装、验证不同状态，提供实际缺项入口。',
  '工作与协议页面；Q604条件展示；相关版本差异',
  '不靠地图100%算准备；未满足分布式提前可补；人员与材料不能虚造；无关闲谈不使预览反复失效')
T(62,'P4','角色、商店、收藏和重配正式UI','CX-041 CX-042 CX-039',[7,10], 'game/UI/Progression/ game/UI/Inventory/ game/UI/Trade/ tests/TradeUI/',
  '展示真实校准后数值、安全整套重配、保管奖励与确切制作成本。',
  '构筑与交易界面；装备占用预览；参考预算',
  '非法新构筑保留旧；不重复领升级点；交易不明先查回执；免费物品不形成出售循环')
T(63,'P4','个人线框架与不膨胀奖励','CX-056 CX-057 CX-055',[2,4,7,9], 'src/Mist.Application/CompanionArcs/ content/source/companions/ tests/CompanionArcs/',
  '十二里程碑作为有依赖的个人工作，复用地图、许可与已有物流回执。',
  '个人线状态框架；350经验分配；纪念物而非伤害加成',
  '运输复用不重奖；联系与见面分别同意；过去责任不被礼物删除；不独占主线材料或结局资格')
T(64,'P4','24页面状态矩阵和无障碍联调','CX-033 CX-039 CX-061 CX-062 CX-063',[10], 'game/UI/ tests/UIAcceptance/ reports/',
  '按24视图逐项测试空、失败、过期、只读、恢复与未知结果；不只做成功路径。',
  '页面验收矩阵；真键鼠与200%录屏；旧稿和焦点用例',
  '六入口稳定；读屏不读隐藏秘密；按钮名明确保存/确认/执行；无模型音轨时不假成功')
T(65,'P4','序章至第一章连续内部集成','G2 CX-063 CP-R0-03 CP-R1-03',[1,2,3,4,5,6,7,8,9,10], 'tests/E2E/FirstChapter/ reports/',
  '取消测试直达入口，从序章建立权限、等级与物品到Q104和微光号，验证真实连续路径。',
  '正常新档录像；旅舍事件冲突；第一次晋升与邀请',
  'Q102战前3级校准0；Q104战前5级校准0；地图/个人线顺序真实；无debug授予许可；对外发行仍需G3')
T(66,'P5','音频/本地化/词典制作工具链','CX-010',[5,11], 'tools/Mist.ContentCli/Assets/ content/source/lexicon/ schemas/ tests/AssetPipeline/',
  '将脚本、音轨、字幕、派生译文与权利记录绑定，管理审核失效而非自动认证。',
  '资产登记与审听表；字幕校验；差异传播',
  '只脚本不算音轨；关键否定/数值逐项核对；既有原文不被导入润色；无字体二进制来自交接包')
T(67,'P5','可选帮助网关与DisabledProvider','CX-033 CX-019',[11,12], 'services/Mist.Assist/ src/Mist.Application/Assist/ tests/Assist/',
  '三个用途隔离，默认关闭实际模型；本地无服务仍完整推进。',
  '请求DTO与最小权限；取消与超时；假服务明确标注',
  '客户端无密钥；打开写作不是同意上传；没有世界工具端口；未评估不是0分')
T(68,'P5','响应核验、缓存分区和晚到路由','CX-067',[5,9,10,11,12], 'services/Mist.Assist/Validation/ src/Mist.Application/Assist/ tests/AssistSafety/',
  '限制来源、帮助级别、正文版本、删除世代与实际意图，结构检查不冒充保义。',
  '结构验证；出处范围；保守降级；有限重试成本账',
  '错误自然语言即使JSON合法也不能当已核验；L4不缓存给L1；旧稿不执行当前动作；未检查结果不先流式显示')
T(69,'P5','实际供应商候选与回归适配','CX-068',[5,11,12], 'services/Mist.Assist/Providers/ tests/AssistLive/ reports/',
  '只有明确启用在线模型特性时实施，核对当前条款/价格/留存并用真实请求验证。',
  '供应商决策与有限预算；实际语义结果；模型版本报告',
  '不使用虚构在线通过记录；B保留B；反例/注入/短文不造band；未通过则功能保持关闭',feature='online_model')
T(70,'P5','复习调度与同档主题内容索引','CX-033 CX-066',[5,9,11], 'src/Mist.Application/Review/ content/source/practice/ tests/Review/',
  '按真实能力与支持记录选已验证同档内容，保留家族与主题多样性。',
  '最多三推荐；真实间隔；离线练习库索引',
  '拒绝不反复弹；剧情未知不入题；同模板换名不算迁移；不通过经验诱导无限刷题；离线天数不扣属性')
T(71,'P5','完整读听写检查与校准证据管理','CX-070 CX-031',[5,11], 'game/UI/Assessment/ tools/Mist.ContentCli/Calibration/ tests/Assessment/',
  '提供长文、长音轨、Task1/2任务与条件记录，不把内部正确率直接换band。',
  '完整任务入口；观察导出；真实人审与试测登记',
  '短留言没有完整评分；新内容/音轨/支持变化使相关记录复核；训练库与未见测试隔离；人工签核不能由模型自报')
T(72,'P5','隐私、注入和世界写权限防线回归','CX-068 CX-019 CX-021 CX-055 CX-071',[9,10,11,12], 'tests/Security/ services/Mist.Assist/ src/Mist.Application/Privacy/ reports/',
  '在实际实现验证权限前置过滤、恶意正文、重复响应、删除与受控发布。',
  '攻击族清单；真实结果；不适用项',
  '正文不是指令；标题/日志不泄未来；撤权结果弃用；恢复备份重新套删除；不执行生成代码')
T(73,'P6','28级最低路径终章实战调参','CX-051 CX-052 CX-059 CP-R6-03 CP-RF-03',[7,8,12], 'content/source/balance/ tests/CombatFinal/ reports/',
  '用28级主线装备、地方援勤、无付费补给验证鲁克及三阶段机组；把长战问题实测解决。',
  '三主修与混合战斗记录；同阶段资源连续性；明确数值变更单',
  '30级压力台不替代28级；不靠友情加伤修平衡；每阶段不回满；阶段门不能被伤害越过；超过节奏目标记录并修订')
T(74,'P6','三种结局与条件非干扰矩阵','CX-073 CX-061 CP-RF-03',[2,4,5,7,8,9], 'src/Mist.Application/Endings/ tests/Endings/ reports/',
  '验证两条基础路线与五增强条件，政策确认后生成固定分支尾声。',
  '结局谓词与快照；资源/关系/帮助组合测试；可重访终局前档',
  '全部条件提前可见；英语与同伴偏好不读入资格；三种结局不抹历史；最终机组停机不是直接选政策')
T(75,'P7','性能、容量和长会话负载','CX-064 CX-074 G5',[3,10,12], 'tests/Performance/ game/Diagnostics/ reports/',
  '在标明硬件和构建版本下测试长文、78场景按需加载、分支库增长和多稿切换。',
  '真实基准；瓶颈清单；缓存与清理边界',
  '不把容器脚本耗时当游戏FPS；长会话不漏NPC占用；主线程不卡持久写；不为性能删除合法草稿')
T(76,'P7','Windows故障、升级与恢复矩阵','CX-072 CX-075',[10,11,12], 'tests/WindowsRecovery/ tools/recovery/ reports/',
  '对实际发行候选检查权限拒写、满盘、库损坏、旧包与删除恢复、提交丢响应。',
  '故障注入与恢复日志；迁移副本；数据损失说明',
  '原库不被坏迁移覆盖；旧稿和学习范围正确；进程终止与物理断电分开；无法验证明确not_run')
T(77,'P7','干净Windows候选包与权利清单','CX-076 CX-066',[10,11,12], 'tools/release/ build/candidate/ reports/',
  '构建可离线运行的完整候选；准备来源、声音、字库和依赖授权证据，不自动公开发布。',
  '安装运行目录；SBOM/权利清单；签名工作流但私钥不入库',
  '不带SDK/Python需求；不带开发密钥/作者答案/测试存档；内部候选标识不能伪装正式；缺权利或声音可列明但不能最终通过')
T(78,'P7','全量内容与全流程回归执行','CX-077 G5',[1,2,3,4,5,6,7,8,9,10,11,12], 'tests/ReleaseE2E/ reports/',
  '使用最终候选核对预算、主线32任务、可选个人线与世界分支；按真实路线测试。',
  '覆盖矩阵；已测/未测场景；序章到三结局保存点',
  '槽位不当成完成关卡；不叠加Python测试数充覆盖；缺项和阻断缺陷不可隐藏；使用提示和离线也有完整路线')
T(79,'P7','逐项发布门核对与证据汇总','CX-078 G6',[1,5,11,12], 'reports/release/ planning/state/ docs/releases/',
  '汇总代码、内容、音频、语言、权利、Windows与隐私证据，供人类发布负责人决定。',
  '逐项已满足/阻断清单；启用特性；对外说明草稿',
  '无真实证据不通过；在线关闭不宣称AI已实现；统计不是官方认证；所有严重缺陷有解决或正式缩范围变更')
T(80,'P7','经批准的本地交付与用户操作说明','G7',[1,10,11,12], 'build/release/ docs/releases/ reports/',
  '在实际获准后生成本地发行产物、版本说明和恢复教程；外部上传仍需用户明确授权。',
  '校验清单；离线启动教程；数据/支持范围说明；已知限制',
  '签名与版本可核对；宣传不承诺通关IELTS总分；无默认外发；不自动发布到商店或第三方账户')
# Region production, not hidden code-writing megatasks. Every chapter has fixed content IDs and explicit six stages.
quests=json.loads((ROOT/'planning/main_quest_index.json').read_text())['quests']
region_cfg=[
 ('R0','序章','Q-P','R0-','救援先于导师邮包；五场景；安全离船、绞盘、接应、中继；三种语言小闭环。','熄灯先于偏航，不提前透露是谁下令；阅读耗时不杀死幸存者。'),
 ('R1','盐雾港','Q-1','R1-','Q101权限、Q102六场景三方法、Q103核验、Q104塔站；旅舍与船上初始互动。','A关闭/B1.2米/本船1.4米/C需当前导航灯；工坊3级、塔站5级；莉娜Q104后正式邀请。'),
 ('R2','苔林修院','Q-2','R2-','名册身份、转移时间、地下转供、公开范围；地下返程；米拉首段。','D-1交接只说明导师当时活着；不公开私人姓名也可核验公共事实；小站不代表全塔改造。'),
 ('R3','沉钟矿区','Q-3','R3-','救援、检修签章、配给与降载；轨道泵房；奥伦维修和证言。','减载后仍有梯道返回；奥伦不入队也有证据；实际修复不抹历史；个人物资与主线供给分开。'),
 ('R4','玻璃学城','Q-4','R4-','数据比较、失败试验、孤网试验、带边界报告；保留二视觉一论证机会。','反例不能被删；有限10分钟记录不证明全网安全；Q404起公开增强准备条件。'),
 ('R5','失名旧都','Q-5','R5-','新旧地图双路进入、导师救出、十八年前责任、监督交接；两入口与救援返程。','导师第五章活着获救；玛拉/沃斯/鲁克责任分别保留；私人许可不堵唯一主线依据。'),
 ('R6','风暴边境','Q-6','R6-','测站、六塔工作单、分段护送、基础/增强准备和起航；泰丝个人线。','Q602与Q603不循环；基础保全保证两个结局；增强五项可见可补；不靠现实等待施工。'),
 ('RF','第七座灯塔','Q-F','RF-','双入口、鲁克受控、三阶段机组、最终命令与阶段尾声；三方向并存。','主线28级进入；鲁克制止不等于停机；阶段不回满；QF05无新必做作文；英语不决定结局。')]
region_tasks={}
for code,title,qpre,spre,scope,red in region_cfg:
 qs=[q for q in quests if q['id'].startswith(qpre)]
 scenes=sorted({s for q in qs for s in q['scene_refs']})
 qids=[q['id'] for q in qs]
 integration_gate='G2' if code in ['R0','R1'] else 'G4'
 ids=[]
 for stage in range(1,7):
  ident=f'CP-{code}-{stage:02d}';ids.append(ident)
  if stage==1:
   title_s='证据/动作完整制作卡与原创正文'
   deps=['CX-001'];goal=f'逐个完成{title}的{", ".join(qids)}生产卡：{scope} 不只是复制流程概要。'
   paths=f'content/source/{code}/ docs/content/{code}/ tests/Content/{code}/'
   outs='每个主线节点的完整目标/来源/可行解/提示/失败恢复；缺原文逐项起草并标未审校；固定事实和新增局部设定差异'
   checks=f'{red}；每个主要谜题至少有充分可得来源；英文目标6.5不提高；新增重大设定先变更而非偷偷写入'
   kind='content_authoring'
  elif stage==2:
   title_s='中文、词条、音轨与视觉素材生产'
   deps=[ids[0],'CX-066'];goal=f'按{title}已审定候选制作派生素材；补齐实际关键音轨、字幕、图表/背景/交互状态，不造时间码。'
   paths=f'content/source/{code}/ assets/{code}/ reports/assets/{code}/'
   outs='对应版本中英内容与词条；真实音轨/字幕/审听记录或明确缺项；图像分层、素材来源和授权记录'
   checks='没有声音工具或授权则blocked；脚本不得标成音轨；图表来自结构化数据；不得以他章音轨冒充本章；不重写受控事实'
   kind='content_production'
  elif stage==3:
   title_s='正式场景、机关与任务逐节点接入'
   deps=[ids[0],integration_gate,'CX-061'];goal=f'用共享系统实现{title}，按登记场景逐节点配置；先真实可达，再正式美术。'
   paths=f'content/source/{code}/ game/World/Scenes/{code}/ game/Combat/{code}/ tests/Chapters/{code}/'
   outs='实际可进入场景和成对出口；目标/机关/人物/战斗引用；保存与重访状态'
   checks=f'{red}；不能把close_contract变成直接完成按钮；关键证据无需指定加点；缺通用能力回缺陷任务，不复制第二套规则'
   kind='chapter_integration'
  elif stage==4:
   title_s='地方/同伴/生活支线与可选预算'
   deps=[ids[2],'CX-063'];goal=f'填充{title}声明的可选空间与个人线里程碑，使用地区预算；普通生活不全部挂中央阴谋。'
   paths=f'content/source/{code}/ content/source/companions/ tests/Chapters/{code}/ reports/content/{code}/'
   outs='本地区支线与纪念物；生活内容种子转成脚本；经验/场景/事件占用清单'
   checks='复用物流不重奖；可选不藏必需结局条件；同一物品/人不复制；没有本区个人线则说明不适用，不虚增任务'
   kind='content_integration'
  elif stage==5:
   title_s='内部完整章回归、连续过渡与体验采样'
   deps=[ids[1],ids[2],ids[3],'CX-064','CX-070','CX-071']
   if code=='RF':deps+=['CX-073','CX-074']
   goal=f'从上一章真实状态进入{title}并形成下一章合法出口；测试所有重要方法、选择与保存边界。'
   paths=f'tests/Chapters/{code}/ reports/chapters/{code}/ content/source/{code}/'
   outs='完整与乱序路线；最低成长配置测试；语言机会/时间分布；内部缺陷与明确未测项'
   checks='不得只靠测试关卡seed证明连续主线；字幕/完整提示不改变基本奖励；撤回不重置外部进展；声画/材料/规则版本一致；内部通过不冒充校准'
   kind='chapter_qa'
  else:
   title_s='真人审校、审听与6.5条件校准'
   deps=[ids[4]];goal=f'由实际人员对{title}完成故事、英语、听力与任务负担复核及声明条件试测；Codex只组织材料和分析。'
   paths=f'reports/human/{code}/ content/manifests/{code}/ docs/content/{code}/'
   outs='具名角色的真实审核/审听；匿名试测证据和适用条件；通过或返工决定及版本绑定'
   checks='模型/自动脚本不能签真人名字；没有受试或音轨就未通过；内容改动触发复核；不宣称每位玩家必得6.5或官方认证'
   kind='human_calibration'
  T(ident,'CONTENT',f'{title}：{title_s}',deps,[2,3,4,5,6,7,8,9,10,11],paths,goal,outs,checks,kind=kind)
  TASKS[-1]['quest_refs']=qids;TASKS[-1]['scene_refs']=scenes
  TASKS[-1]['owner_role']='内容编辑/音频审校/目标组评审；Codex不得代签' if stage==6 else ('Codex＋内容负责人' if kind.startswith('content') else 'Codex工程实现')
 region_tasks[code]=ids
# Explicit cross-project human gates. A browser or tool return cannot fabricate any of these.
H=[
 ('HV-001','P0','原生Windows输入、读屏和干净导出核验','CX-003 CX-004 CX-005',[10,12],'在真实Windows按规定检查IME、键盘、200%、读屏、离线启动与SQLite。','测试设备/软件/构建ID；逐项操作与真实结果；可行或阻断结论','不能用Linux合成IME或网页替代；未运行项标not_run；失败回技术试件'),
 ('HV-002','P4','第一可玩章目标用户可用性核验','CX-065 CP-R0-05 CP-R1-05',[1,5,10],'观察目标用户能否识别目标、回到原文、保存草稿、区分确认/执行和敌方锁定。','实际参与记录；匿名观察与退出点；修复清单和复测','小样本是发现可用性问题，不证明提分；重大误发/死锁/丢稿零容忍；保留语言支持条件'),
 ('HV-003','P7','完整候选的干净Windows安装与恢复核验','CX-077',[10,12],'在发行目标机验证安装、无开发工具运行、保存升级和故障恢复。','版本/设备/安装目录；实际运行录像；已测/未测结果','不得只在开发机确认；断网可玩；真实音轨/输入/恢复可用；性能记录型号不泛化最低配置'),
 ('HV-004','P7','素材、声音、字体与发布权利核验','CX-077',[11,12],'实际负责人检查所用素材、演员/声音、字库和依赖许可，处理范围及权利限制。','来源与授权清单；署名要求；不能发布的资产清单','计划包中没有字体文件不等于将来成品已有字体授权；AI生成不自动代表可商用；未解决权利阻断相关发行'),
 ('HV-005','P7','全游戏6.5覆盖与校准证据复核','CP-R0-06 CP-R1-06 CP-R2-06 CP-R3-06 CP-R4-06 CP-R5-06 CP-R6-06 CP-RF-06 CX-071',[5,11],'复核16长文/32听力/12长写作等实际机会与适用条件，确认不是同材料反复测成高分。','全局覆盖与复现；真实标定版本；未见材料隔离与不确定性说明','难度校准不等于教学效果；无口语不报完整总分；不把token数或通过率直接称6.5；没有数据不得通过'),
 ('HV-006','P7','隐私、范围和阻断风险独立复核','CX-072 CX-076',[9,10,11,12],'核对外发、删除、缓存、发布边界和实际缺陷，审查不依赖同一个生成器自报。','真实独立审阅；攻击/失败场景；关键风险签核','不得掩盖未实测自由文本敏感识别；关闭服务不假称已验证；严重阻断无豁免式假绿灯'),
 ('HV-007','P7','项目负责人作出交付决定','CX-079',[1,5,11,12],'按真实证据决定本地正式交付或返工；外部商店上传另行明确授权。','发行范围/启用特性/已知限制；实际审批；版本记录','不能由Codex冒充负责人；有关键未完成应返工或正式缩范围；不许偷偷删除口语外写作目标'),
 ('HV-008','P5','可选在线模型的真实质量和数据条款核验','CX-069',[5,11,12],'只有启用online_model时，实际复核语义、立场偏差、注入风险、费用和数据策略。','真实样本/人工结论；版本与误差；明确启用或继续关闭','合成夹具不算模型结果；未评估不得完整band；更换模型需再核验')]
for ident,phase,title,deps,parts,goal,outs,checks in H:
 T(ident,phase,title,deps,parts,'reports/human/ docs/releases/ planning/state/',goal,outs,checks,feature='online_model' if ident=='HV-008' else None,kind='human_verification')
 TASKS[-1]['owner_role']='对应真实验收人员；Codex只准备、整理和修复，不得代签'
GATES=[
 {'id':'G0','title':'候选工具链实际可用','depends_on':['CX-005','HV-001'],'scope':'真正的Windows原生输入/数据库/导出；不是网页演示'},
 {'id':'G1','title':'数据与恢复基础可用','depends_on':['CX-022'],'scope':'真实.NET持久化结果，不继承Python通过结论'},
 {'id':'G2','title':'Q102内部机制样章','depends_on':['CX-053'],'scope':'六场景三方法与真实保存；缺音轨/校准只能内部明示'},
 {'id':'G3','title':'首章内容与体验获得相应实证','depends_on':['CX-065','CP-R0-06','CP-R1-06','HV-002'],'scope':'序章至首章，有真实素材/校准/可用性，不是完整游戏'},
 {'id':'G4','title':'共享系统可供后续地区复用','depends_on':['G3','CX-064','CX-066','CX-068','CX-070','CX-071','CX-072'],'scope':'在线供应商默认关闭；不影响离线主线'},
 {'id':'G5','title':'全部章节内部连续可通','depends_on':[ids[4] for ids in region_tasks.values()],'scope':'各章真实集成含终章；不是正式校准批准'},
 {'id':'G6','title':'发布质量证据齐备','depends_on':['G5','CX-076','HV-003','HV-004','HV-005','HV-006'],'optional_when_enabled':{'online_model':['CX-069','HV-008']},'scope':'代码、声音、语言、权限、平台均独立有证据'},
 {'id':'G7','title':'本地正式交付得到批准','depends_on':['CX-079','HV-007'],'scope':'不授权自动对外上传'}]
# Later content requires G4, early content integration uses G2; no chapter metadata is blocked from being authored.
byid={t['id']:t for t in TASKS};gby={g['id']:g for g in GATES}
assert len(byid)==len(TASKS)
# Acceptance definitions: assign a concrete owner plus full-project verification; these are allocation, NOT executed tests.
raw=json.loads((ROOT/'planning/acceptance_raw.json').read_text())
ROUTING={
 1:[('CX-053',4),('CX-015',6),('CX-018',8),('CX-078',99)],
 2:[('CX-054',5),('CX-074',10),('CX-078',99)],
 3:[('CX-025',9),('CX-027',16),('CX-038',22),('CX-060',28),('CX-078',99)],
 4:[('CX-028',8),('CX-036',15),('CX-037',22),('CX-055',28),('CX-059',35),('CX-078',99)],
 5:[('CX-010',5),('CX-018',12),('CX-031',18),('CX-070',24),('CX-071',30),('HV-005',99)],
 6:[('CX-032',8),('CX-027',15),('CX-036',22),('CP-R2-05',28),('CP-R5-05',33),('CP-R6-05',37),('CP-R4-05',41),('CX-078',99)],
 7:[('CX-040',7),('CX-041',11),('CX-052',20),('CX-042',29),('CX-059',35),('CX-056',38),('CX-078',99)],
 8:[('CX-043',7),('CX-046',13),('CX-044',22),('CX-045',30),('CX-050',35),('CX-049',39),('CX-073',47),('CX-078',99)],
 9:[('CX-054',7),('CX-057',17),('CX-058',23),('CX-056',29),('CX-055',34),('CX-063',37),('CX-074',99)],
 10:[('CX-023',10),('CX-029',18),('CX-030',24),('CX-031',32),('CX-036',38),('CX-064',46),('HV-001',50),('CX-076',99)],
 11:[('CX-010',13),('CX-066',20),('CX-068',33),('CX-019',40),('CX-021',45),('CX-072',49),('HV-005',99)],
 12:[('CX-006',6),('CX-009',11),('CX-014',19),('CX-015',26),('CX-019',33),('CX-020',39),('CX-052',43),('CX-068',48),('CX-076',99)]}
# Route by actual requirement keywords when a strongly specific target is present.
KEYWORDS=[('输入法','CX-023'),('6.5','CX-071'),('音轨','CX-030'),('字幕','CX-030'),('删除','CX-019'),('反馈','CX-068'),('校准','CX-010'),('同伴','CX-056'),('离队','CX-056'),('工程','CX-059'),('派工','CX-059'),('双花','CX-059'),('暴露','CX-044'),('失衡','CX-045'),('护盾','CX-044'),('冷却','CX-043'),('重奖','CX-014'),('幂等','CX-014')]
for a in raw:
 number=int(a['id'].rsplit('-',1)[1]);owner=next(x for x,end in ROUTING[a['part']] if number<=end)
 text=' '.join([a['title']]+a['requirement_cells'])
 for kw,target in KEYWORDS:
  if kw in text:owner=target;break
 a['assigned_task']=owner;a['verification_task']='CX-078';a['assignment_status']='planned_mapping_not_execution';
 byid[owner].setdefault('upstream_acceptance',[]).append(a['id'])
# Crosswalk old package -> relevant engineering tasks. Not full semantic equivalence of Python tests.
legacy=json.loads((ROOT/'planning/legacy_work_packages.json').read_text())
map_prefix={ 'EXP':[24,25,26,27,60], 'QEV':[28,35,36,37,55,59,15,14], 'ENG':[18,29,30,31,33,70,71], 'PZ':[27,32,36], 'RPG':[40,41,42,52,59,56], 'COM':[43,44,45,46,47,48,49,50,51,52,73], 'NPC':[54,55,56,57,58,63,74], 'UI':[23,29,30,31,32,39,46,61,62,64], 'CAI':[9,10,66,67,68,69,70,71,72,21], 'TECH':[2,3,4,5,6,7,8,9,11,12,13,14,15,16,17,18,19,20,21,22,76,77] }
for x in legacy:
 prefix=x['legacy_id'].split('-')[0]
 x['covered_by']=[f'CX-{i:03d}' for i in map_prefix.get(prefix,[78])]
 x['mapping_note']='责任覆盖索引，不表示每个列出任务重复实现整个上游包；具体范围以独立任务卡为准。'
# All newly planned tasks are not started; this builder never records production pass.
plan={'version':'0.1','baseline':'MASTER-GDD-0.1','date':'2026-10-09','language_target':{'reading':6.5,'listening':6.5,'writing':6.5,'speaking':None,'auto_raise':False},'features':{'online_model':False},'tasks':TASKS,'gates':GATES,'first_task':'CX-001','production_status':'not_implemented_in_this_turn'}
dump('planning/plan.json',plan)
dump('planning/acceptance_traceability.json',{'count':len(raw),'status':'all_product_tests_unrun_by_this_turn','requirements':raw})
dump('planning/legacy_work_packages.json',legacy)
(ROOT/'planning/acceptance_raw.json').unlink()
dump('planning/state/execution.json',{'version':'0.1','note':'可修改执行账；当前没有任何正式实现任务完成。验证报告在reports/execution/逐项保存。','tasks':{t['id']:{'status':'not_started','evidence':None,'review':None} for t in TASKS}})
# Build one bounded task file for every planned work item.
for t in TASKS:
 lines=[f"# {t['id']} — {t['title']}",'',f"阶段：{t['phase']}｜类型：{t['kind']}｜责任：{t['owner_role']}",f"初始状态：未开始。{'仅当 online_model 明确启用才纳入执行与发布门。' if t['feature'] else ''}",'','## 目标',t['objective'],'','## 前置条件', ', '.join(t['depends_on']) if t['depends_on'] else '无；先检查当前仓库，不能覆盖已有用户工作。','所有前置必须实际验收；Gate通过由其依赖证据派生，不手写passed。','', '## 先读', '- AGENTS.md', '- docs/integration/MASTER_GDD_v0.1.md', '- docs/integration/Integration_Decisions_v0.1.md']
 lines += ['- '+p for p in t['references']]
 if 'quest_refs' in t:lines += ['','涉及任务：'+', '.join(t['quest_refs']),'涉及场景：'+', '.join(t['scene_refs'])]
 lines+=['','## 允许修改范围']+['- `'+p+'`' for p in t['allowed_paths']]
 lines+=['','## 必须交付']+['- '+v for v in t['deliverables']]
 lines+=['','## 验收']+['- '+v for v in t['acceptance']]
 lines+=['','## 上位验收责任', ', '.join(t.get('upstream_acceptance',[])) or '按本卡条款与本阶段集成验收；不得据此称上位全部验收已通过。', '', '## 禁止与遇阻处理',
 '不得编辑 docs/baseline/ 或 reference/ 原件；不得删除用户代码、强制重置Git、安装未声明付费服务、上传正文或发布产品。跨出允许范围时先登记依赖/变更并停止相关修改。',
 '不得把未实现分支返回true、把SYNTHETIC当真实、把脚本当音轨、把结构化选择当自由英文理解；不得擅改6.5目标、奖励、事实、结局和资源所有权。',
 '本卡过大或发现缺失制作条件时，保留已完成可检验内容并登记blocked；先提出有范围的子卡，不直接扩大到全游戏。没有原生Windows/真人/模型权限时如实保留未执行项目。',
 '', '## 交付证据', f"写入 `{t['evidence_path']}`，采用 planning/templates/Task_Report.template.json。记录真实提交/工作树差异、命令、退出码、环境、测试名称、未执行项和来源版本；不能填写估计的通过数。",'','任务状态只有在产物、证据和所需复核均成立后才改为accepted。代码已编写不等于已验证；Gate、模型评价与真人签核不能自行伪造。']
 write('planning/tasks/'+t['id']+'.md','\n'.join(lines))
# Validate graph now, including currently disabled optional tasks.
nodes={**{k:v['depends_on'] for k,v in byid.items()}, **{k:v['depends_on'] for k,v in gby.items()}}
seen=set();active=set();order=[]
def visit(n):
 if n not in nodes:raise ValueError('unknown dependency '+n)
 if n in active:raise ValueError('cycle at '+n)
 if n in seen:return
 active.add(n)
 for p in nodes[n]:visit(p)
 active.remove(n);seen.add(n);order.append(n)
for n in nodes:visit(n)
dump('planning/topological_order.json',{'note':'拓扑次序仅供依赖检查；不表示排期估算或实际完成顺序','nodes':order})
print('PLAN',len(TASKS),'engineering',sum(t['kind']=='engineering' for t in TASKS),'chapter',sum(t['id'].startswith('CP-') for t in TASKS),'human_cross',sum(t['id'].startswith('HV-') for t in TASKS),'gates',len(GATES),'acceptance',len(raw),'DAG_OK')

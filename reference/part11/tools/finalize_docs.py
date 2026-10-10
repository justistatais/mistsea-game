from pathlib import Path
import json,hashlib
ROOT=Path(__file__).resolve().parents[1]
def dump(path,x):
 p=ROOT/path;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(x,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
criteria=[
('6.5锁定','主线与正式材料的读听写目标均6.5，口语null，不出现自动7分门槛。','static+pilot'),
('校准不是标签','没有当前任务条件的有效标定记录，不能宣布已校准或生成成绩。','review+pilot'),
('源文不可变','同ID同版本正文改变时构建失败，不能按加载顺序覆盖。','static'),
('复用去重','第五/十部分Q102同文复用不增加材料、奖励或独立学习次数。','static+integration'),
('依赖完整','不存在缺失引用、循环依赖或无法恢复的内容绑定。','static'),
('玩家信息充分','不用作者隐藏事实才能解题；独立解题者能指出有效来源或真实未知。','human+playtest'),
('多解认可','所有公开约束内可行方案都能进入实际验证。','logic+playtest'),
('局部事实审批','新增事实单独提出，不在成文中悄悄改写主线。','editorial'),
('人物不全知','重要对白在当前分支只使用实际可见和已获知信息。','integration+semantic'),
('没有假签核','模型或任意候选文件的approved字段不能取得真实审核权限。','security'),
('版本化审核','文本、任务、声音或支持相关改动使对应签核待复核。','static+review'),
('独立人工复核','故事和语言最终核查来自不同实际审核人，而非两个模型角色。','human'),
('无假音轨','没有真实文件时播放器与清单均保持脚本状态。','media+UI'),
('真实审听','关键数字、否定、更正对象和意图经整轨审听，不能只看转写。','human_audio'),
('字幕对齐','每个必需段落在真实音轨有合法位置；版本与顺序相符。','media+human'),
('说明不朗读','答案、元数据、舞台说明与角色标签不被当成正文录音。','human_audio'),
('声音来源','实际声音许可、使用范围与素材版本记录完整。','rights'),
('词典来源','商业词典未获许可字段不进入离线库；原创条目也核对词义与来源。','rights+language'),
('中文保义','中文辅助保留否定、条件、时序、数量、立场和信息不足。','language'),
('图文同源','图表、数据、答案和可访问文本一致，不靠AI绘图推断数值。','static+visual'),
('帮助隔离','L1不拿到L4全文；完整提示按当前状态，不重复取走物品。','integration'),
('校准条件','按相关单项能力分组，记录工具/音频/信息暴露条件。','pilot'),
('留出隔离','生产、回归、难度试测与迁移集按家族隔离。','dataset_review'),
('未知不造分','短稿、服务失败或证据不足不被换算成完整band。','semantic+UI'),
('无世界工具','AI适配器无奖励、发船、改关系、改工程与任意执行工具。','security+integration'),
('正文是数据','正文含角色冒充或指令时，不改变模型权限和世界。','actual_model_redteam'),
('先过滤再检索','未来剧情、答案键、受限身份及其标题摘要不进入投影请求。','security+integration'),
('可见不等外发','有游戏内访问但无在线处理许可的资料不能外发。','security'),
('传输同意','打开编辑器不上传；明确同意可撤回并覆盖具体用途。','UI+network'),
('输出契约','未声明字段、伪来源、错版本、越界片段不可送入执行面。','static+runtime'),
('引用不保真','用真实引用却表达错误结论的样例进入语义评估，不能被schema通过掩盖。','actual_model+human'),
('意思不代改','玩家明确写B时不得为了正确答案改成C，未知保持未知。','actual_model'),
('建议需采纳','反馈不自动改正文、签字、发信或执行世界。','integration'),
('不流式泄漏','未经核验的生成文字不先出现到玩家正式界面。','UI+network'),
('安全呈现','生成HTML/脚本只按转义文本展示，不执行外部请求。','security+UI'),
('旧稿路由','旧稿反馈不替换新稿，当前光标与保存状态不变。','integration'),
('读档隔离','未来分支反馈不会泄漏到当前旧分支任务与人物知识。','integration'),
('删除生效','删除世代、许可撤回与请求取消能阻止晚到结果复活。','privacy+network'),
('缓存范围','模型/提示/草稿/权限/帮助范围不同不能命中不兼容私人缓存。','runtime'),
('最小日志','默认不留全文提示、个人身份、凭证或完整剧情。','security_audit'),
('有限重试','限流和短时服务错最多一次重试，其他错误不无限重试。','runtime'),
('预算真实','实际调用收费即使结果废弃仍记账，重放不重复计费，超额停止新调用。','runtime+provider'),
('无网可玩','网络关闭时任务、草稿、固定帮助与显式确认可完成主线。','full_playtest'),
('包绑定','进行中任务使用其绑定版本，新任务才用新包。','integration'),
('缺包不偷换','旧包缺失时恢复/迁移明确，不默认拿新答案替换。','integration'),
('隔离与迁移','严重缺陷版本停止新用，旧存档迁移记录不重复奖励和伪新证据。','recovery'),
('发布可信','构建签名和人工权限由真实可信基础设施核验，不以自写JSON为凭。','security'),
('读听写范围','16长文、32关键音轨、12长写作等既有预算按独立定义统计。','content_audit'),
('同档主题变化','复习新情境不是同模板换人名数字，也不偷偷升到7分。','editorial+pilot'),
('批量返工控制','同模板共因错误暂停该批，不继续批量扩散或无限重生成。','production_trial'),
('Q102端到端','三种工坊路径、实际修灯、同稿写作、离港确认与受限AI均兼容。','engine_playtest'),
('不假称已完成','报告区分静态检查、合成fixture、真实模型、真人审校和目标人群试测。','release_review')]
accept=[{'id':f'CAI-AT-{i:02d}','title':t,'expected':e,'verification':v,'production_status':'not_run_by_part11_reference_tests'} for i,(t,e,v) in enumerate(criteria,1)]
dump('data/Content_AI_Acceptance_v0.1.json',accept)
tasks=[
('登记与依赖','第4/5/6/10部分','不可变源登记、别名、重复与影响图','不得覆盖上游v0.1正文或把alias当新题',[1,3,4,5,44,45]),
('制作工作台','CAI-DEV-01','任务卡、候选隔离、12模板调用抽象','不能让生成候选自行发布或新增全局事实',[8,10,11,50]),
('审核与缺陷流程','CAI-DEV-02','真实角色审核、缺项、分级、修订链','参考fixture不是实际签字',[6,7,9,10,12,52]),
('音频及派生材料','CAI-DEV-03','脚本/声音/字幕/中文/词条/提示绑定','没文件不造音轨时长，不搬商业词库',[13,14,15,16,17,18,19,20,21]),
('6.5校准登记','CAI-DEV-03/04','任务级条件、样本范围、结果和发布门','不以模型自报或短稿生成band',[1,2,22,23,24,48,49]),
('运行时投影','第9/10/12部分及CAI-DEV-01','授权先行、最小上下文、外发同意','无世界工具，无全剧情/答案检索',[25,27,28,29,40]),
('候选输出与路由','CAI-DEV-06','严格字段、原文锚、语义候选、旧稿路由','结构通过不等于真实；不流式漏出再撤回',[26,30,31,32,33,34,35,36]),
('故障成本与删除','CAI-DEV-07','有限重试、按请求预算、缓存分区、删除世代','不静默上传/训练用户写作；不捏造供应商保留承诺',[37,38,39,40,41,42,43]),
('发布与迁移','CAI-DEV-01/03/05及第12部分','签名清单、活跃包保留、隔离、回滚','不静默改变进行中谜题，不重复奖励',[3,11,44,45,46,47]),
('六卡端到端','CAI-DEV-04/05/07/08/09','真音轨、实际模型回归、场景实测、生产报告','继承第8部分未完战斗问题，不靠AI或关系加成掩盖',[14,22,26,31,43,48,49,50,51,52])]
rows=[]
for i,(name,deps,out,ban,ats) in enumerate(tasks,1):
 rows.append({'id':f'CAI-DEV-{i:02d}','title':name,'dependencies':deps,'output':out,'prohibited':ban,
              'acceptance_ids':[f'CAI-AT-{a:02d}' for a in ats],'status':'specification_only_not_engine_implemented'})
dump('data/Codex_Content_AI_Tasks_v0.1.json',rows)
text=['# Codex交接：第十一部分 v0.1','','此文件是局部实施卡。先阅读Content_AI_Pipeline_v0.1.md与上游文件；第十二部分确定最终引擎、存储、真实身份/签名和服务适配。不得将此参考代码当成已经完成的真实语义评分器。','']
for row in rows:
 text.extend([f"## {row['id']} {row['title']}",f"依赖：{row['dependencies']}。",f"交付：{row['output']}。",f"禁止：{row['prohibited']}。",'验收：'+', '.join(row['acceptance_ids'])+'。',''])
text+=['## 所有工作包的共同要求','本轮90项检查只处理有限数据与契约；真实生产验收52项均需各自落实。显示mock/unassessed，不可临时把所有请求返回成功。生成文本不写世界；批准来自可信角色；6.5目标不自动升级。','']
(ROOT/'Codex_Handoff_Part11_v0.1.md').write_text('\n'.join(text),encoding='utf-8')
refs='''# 外部来源及上位依据\n\n核对日期：2026-10-09。以下是工程/评价参照，不是允许复制第三方正文、试卷、词典或声音的授权。\n\n| ID | 官方来源 | 本文使用范围 |\n|---|---|---|\n| S1 | [IELTS scoring in detail](https://ielts.org/take-a-test/your-results/ielts-scoring-in-detail) | 总分/单项区别，读听版本换档及写作维度 |\n| S2 | [IELTS Academic Writing format](https://ielts.org/take-a-test/test-types/ielts-academic-test/ielts-academic-format-writing) | Academic两种写作任务的范围与评价 |\n| S3 | [OWASP LLM01:2025 Prompt Injection](https://genai.owasp.org/llmrisk/llm01-prompt-injection/) | 提示注入并非由检索或微调单独消除 |\n| S4 | [OWASP LLM06:2025 Excessive Agency](https://genai.owasp.org/llmrisk/llm062025-excessive-agency/) | 权限、功能、自主执行要限制 |\n| S5 | [JSON Schema Object](https://json-schema.org/understanding-json-schema/reference/object) 与 [Creating your first schema](https://json-schema.org/learn/getting-started-step-by-step) | 字段、类型、额外属性检查；结构检查不承担语义判真 |\n\n项目上位依据：Game_Design_Core、World_Story_Bible、Exploration_Level_Design、Quest_Event_System、English_Learning_System、Puzzle_System、Character_Progression、Combat_System、NPC_Faction_System、UI_UX_Spec，各v0.1；其中难度变更CR-EN-005-001优先。实际文件哈希见data/Upstream_Manifest_v0.1.json。\n'''
(ROOT/'References_v0.1.md').write_text(refs,encoding='utf-8')
(ROOT/'requirements.txt').write_text('jsonschema==4.26.0\n',encoding='utf-8')
(ROOT/'README.md').write_text('''# 第十一部分设计包\n\n这是内容生产与AI辅助的设计规格及有限参考，不是游戏工程、在线服务或IELTS评分器。\n\n## 阅读顺序\nContent_AI_Pipeline_v0.1.md → data/Production_Jobs_v0.1.json → Codex_Handoff_Part11_v0.1.md → Content_AI_Validation_v0.1.md。\n\n## 自包含检查\nPython 3.10+；验证JSON Schema需要requirements.txt中的版本。安装依赖可能需要联网，测试本身不联网。\n\n```sh\npython -m pip install -r requirements.txt\npython tools/verify_package.py\npython validation/run_checks.py --output-dir validation/rerun\n```\n\n通过verify_package后再运行可保留初始日志不变。run_checks默认会重写validation目录下报告；--output-dir可另存。测试用到的人工审核人与音轨都是SYNTHETIC夹具，不是真实审核或录音。\n\n## 源盘点复算\n源材料已复制为设计参考，独立测试不要求原始压缩包。要重新核对原始8个压缩包/10个上位文档，可运行：\n\n```sh\npython tools/build_inventory.py --source-root /path/to/game_design --output /path/to/audit_output\n```\n\n不要把此命令输出覆盖已发布的正式资产。源盘点覆盖指定材料集合而非所有文件里的任意文本。\n\n## 真实段落投影示例\n```sh\npython tools/preview_q102_request.py\n```\n只生成受限请求和手工说明fixture，无模型调用。\n\n## 内容边界\n没有真实音轨、没有新模型/供应商接入、没有实际人群校准；语义金标准等待真实模型与人工评估。data/Q102_Production_Bundle_v0.1.json应当无法通过player_release，缺项必须保留。\n\n包内源正文沿用项目已有材料并标注出处；不附字体、第三方试卷或商业词库。文件哈希不是发布签名，真实密钥与身份在第十二部分实现。\n''',encoding='utf-8')
append=['','---','','# 附录B：52项正式产品验收','','以下均是产品要求；90项参考测试不会将其自动标为生产通过。','','| 编号 | 项目 | 验收要求 |','|---|---|---|']
for a in accept:append.append(f"| {a['id']} | {a['title']} | {a['expected']} |")
p=ROOT/'Content_AI_Pipeline_v0.1.md';s=p.read_text(encoding='utf-8');s=s.split('\n---\n\n# 附录B：')[0];p.write_text(s+'\n'+'\n'.join(append)+'\n',encoding='utf-8')
print('Acceptance:',len(accept),'Codex tasks:',len(rows))

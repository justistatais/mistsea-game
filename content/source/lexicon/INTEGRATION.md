# 离线词典与自愿复习

learner_lexicon.json 为720个实际源绑定词义，CORE360/EXTENDED360；每项含独立英文释义、中文辅助、sense、真实原文版本、文件及正文指纹、段落和Unicode坐标。build_regional_lexicon.py 从玩家正文绑定，不读取作者答案；senses.author_only.tsv 是人工审核前的原创词义候选，仍需sense/语境人工审查。数量不代表完成真实6.5教学评测。

RegionalLearningService.Lookup 仅在当前SourceAccess版本/指纹相符时给原文语境；一般释义可查，未获资料正文不能泄漏。Root的材料菜单调用 OpenRegionalDictionary，并以当前实际材料重读许可提供scope。复习通过 OpenRegionalReview自愿进入，默认最多三项，优先兼容未见材料family，可明确延后1/3/7天。回答为空、只显示卡或预加载音轨均不记录完成；脚本阅读不算音频证据。自评good和词典帮助不自动升级未经评测的掌握程度。

ProfileStore.RegionalLearning 使用已有 observations.payload_json/support_state，历史属于学习隐私epoch，不放settings。删除学习记录清除回答与复习，旧备份恢复受control tombstone约束，旧epoch/load/worldsequence追加拒绝。世界奖励、物品、招募和结局没有本模块写入API。

RegionalLearningCases.Register 原有24项源核验、许可、复习、实际SQLite删除/恢复/并发用例，v3另加2项新源/对白许可隔离，共26项。首轮统一执行发现列名不符现有schema及私人词义测试选择器误用Single，两项已修复，Root随后实际重新执行37项CX066全部通过（含当时本模块24项和已有音频测试）；这是v3修改之前的历史证据，当前26项须等待Root对最终源码重新执行。没有词条发音资产，词义、音轨审听/rights、语言校准和人类使用评价均未签署。
# 2026-10-10 v2 来源增补

LEX-20261010-v2 保留720个现有 sense ID、定义、别名和360/360分类。新增 R1生活、R2回信与六床原文、三个中继/塔基说明、R6旁修、R4展签/课程台的实际英文出现语境；不是新增词义。旧v1完整字节保存在 history.author_only，仅供作者追溯，不复制玩家。v2同版本重复生成必须完全相等；任何源字节变化须继续升版，不能覆盖同版本旧context。

词典context fingerprint一律是实际显示的完整English body SHA256；版本、source文件SHA、段落和Unicode标量截取一并核验。FieldRelayContract的机械许可Fingerprint是完整JSON SHA，词典则是三个显示段落用两个换行连接后的body SHA，二者不可混用；调用端先核当前真实许可及合同，再为MaterialId R6-FIELD-RELAY-R-SCOPE签发此body的LearningSourceAccess。R2回信保持原本individual_consent；加入语境不取得许可。没有当前范围时只显示释义，不显示来源原文、不给世界奖励。无新增词音或人工校准结论。

# 2026-10-10 v3 当前许可来源

LEX-20261010-v3 保留720项全部词义元数据，增至4556个实际词形上下文（阅读3928、听力书面脚本628）。新增来源为莉娜8份个人材料、矿区换班6份材料、12份独立可选安全合同、聚落2份合同、旧都2份地方合同和米拉/奥伦6份当前实际应答。船上24段生活对白按界面分别绑定121句有现有词形的当前句与24份完成后的回看；45句短对白没有这720词义的真实词形，不另造词义、段落或音频。v2原字节保留在history.author_only/LEX-20261010-v2.json，作者历史不复制进玩家包。

Lina与R3Shift保持材料自身Id/Version，显示body为其原english字符串。Settlement为settlement:加section.Id；旧都为old-city-life:加section.Id；两类section以两个换行拼接English段落并使用Contract.Version。可选安全合同用原material.Id/content_version。ScopedPersonal应答为SP-CP-MIRA-RENEW、SP-W04-RENEW、SP-CP-MIRA-DENY、SP-W04-DENY、SP-LOCAL-RECEIVER、SP-REMOTE-REVIEW，版本SCOPED-PERSONAL-20261010-v1；原段id为材料Id加-original，body严格为response.en，不再加人物标签。只有ListReadableResponsesAsync/OpenResponseAsync对本次已收到的答复及当前邀请世代签发范围。

NPC当前句的materialID为script.Id加冒号及line.Id，body仅line.English。完成回看的ID为script.Id加:replay，body以两个换行拼接Actor加冒号空格及English。两者指纹和许可独立，当前句不能开之后的句子或全文；完成旧稿也须重读当前授权。所有这些source adapter只保存静态上下文，不授source权限。原声明的private范围、角色本人同意、实际收件、话题撤回和版本世代仍由生产服务核验。

生成工具默认v3；再次运行同版本必须得到相同package，历史版本原字节不同即拒覆盖。实际静态检查reports/continuous/lexicon_v3_source_binding_20261010.json逐项核验4556个context的source文件SHA、英文显示bodySHA、原段、Unicode坐标、实际词形和旧词义元数据，未运行世界动作、C#测试或真人评测；听力脚本不冒充实际听音，词条发音仍为零。现有RegionalLearningCases.Register即可包含新增2项，RegionalLexicalSourceProjection只读helper已纳入.cs，不需要新的注册入口。

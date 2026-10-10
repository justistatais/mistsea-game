# MistSea Codex Handoff v0.1
第十三部分：总设计整合与分步实现计划。完整解压后打开这个目录。

**先读 CODEX_START_HERE.md，再从 CX-001 开始。不是让Codex一次生成全游戏。**

## 文件入口
- docs/integration/MASTER_GDD_v0.1.md：总纲与唯一权威。
- docs/integration/CODEX_IMPLEMENTATION_PLAN_v0.1.md：阶段与136任务总表。
- docs/integration/Integration_Decisions_v0.1.md：18条整合决定。
- docs/integration/OPEN_ISSUES_v0.1.md：16项真实未完成风险。
- planning/tasks/：136张独立卡，含依赖、输入、输出、允许范围与验收。
- planning/plan.json与planning/state/execution.json：机器可读计划与初始执行账。
- planning/acceptance_traceability.json：478条上位产品验收及责任映射，全部未在本轮产品实现执行。
- docs/baseline/：12份原始规范；reference/part03—part12：10个历史包的277个原始文件。
- reports/Integration_Validation_v0.1.md：本轮实际计划检查，不是游戏验收。

语言目标保持Academic读听写各6.5，不训练口语。所有游戏实现任务未开始；两个在线特性任务默认不启用，绝非标为通过。素材和代码完成分开、听力脚本和音轨分开、人工校准不得伪造。

历史包中的绝对路径、原始运行日志和旧README保持原样，仅作为来源。不要直接运行所有历史build脚本；它们可能重建自己的文档或依赖会话路径。正式项目用新适配器导入并写真实Godot/C#实现，不用它们替代产品。

本包不包含字体二进制、模型密钥、签名私钥或Windows游戏可执行文件。包清单是哈希核对，不是发布者签名或6.5认证。

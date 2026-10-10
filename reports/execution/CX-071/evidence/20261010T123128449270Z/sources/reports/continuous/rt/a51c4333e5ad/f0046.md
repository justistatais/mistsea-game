# 本地复习与校准审计工具

从项目根目录执行：

```powershell
dotnet run --project tools/Mist.ReviewCalibration -- audit-source reference/part05/data/Original_Materials_v0.1.json
dotnet run --project tools/Mist.ReviewCalibration -- audit-protocol path/to/frozen-calibration-protocol.json
```

工具只读取指定本地 JSON 并输出 JSON，不改冻结来源、执行账或世界状态，不联网。`audit-source` 列出材料登记、完整任务候选入口和仍缺失的制作证据；数量不代表已经游玩的任务。音轨未经过此工具实际检查时，时长保持空值。

`audit-protocol` 使用 Core 中的 `CalibrationProtocol` 契约：固定 IELTS Academic、相关单项与 6.5 目标，绑定正文、题目、音轨、支持、观看顺序、时间/工具、能力、评分器和规则版本。未知字段、重复字段、错误目标和不完整哈希被拒绝。此命令不导入任何可信人审记录，默认保持 `draft_target_65`，缺项时退出码为 3。输入无效为 1，参数错误为 2。作者写入 `approved=true` 不会产生签核。

当前完整任务候选来自原始独立阅读、听力脚本、Task 1 表格和 Task 2 议题。短撤离说明仍只有局部反馈范围。没有实际音轨审听、双人审校、相关单项参照、真实受试者、盲评分员和原生界面/观察适配器，因此不能宣称实际校准到 6.5、学习有效或正式发行已通过。

本次新增代码未在子代理中执行编译或测试；统一验证由主代理运行。

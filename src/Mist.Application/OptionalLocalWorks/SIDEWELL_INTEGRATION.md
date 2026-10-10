# 观测侧井集成

四个 `.cs.pending` 静态 READY：Core `SideWellModels`、Application `SideWellObservationService`、tests `SideWellObservationCases`、UI `Root.SideWellObservation`。Root 统一纳入、注册 `Mist.Tests.OptionalLocalWorks.SideWellObservationCases.Register()`，过滤 `CP-OPTIONAL-SIDEWELL-03`，四项真实正常来源工程测试。未改冻结 ChapterThreeService、玩家 source、几何、音轨、shared Root 或 build.py。

构造 `(repository,catalog,ChapterThreePackage)`。`OwnPort(id)` 精确识别 `R3-S05.to.R3-O02` 和 `R3-O02.to.R3-S05`。`InspectEntryAsync()` 在当前配对口核实际安全；`RegisterEntrySafetyAsync()` 再明确注册真实 typed 连接；`PortIssue(snapshot,port)` 只读当前来源；`EnterPortAsync(id)` 真正通过配对口；对返回 receipt 调 `RecordWalkedRouteAsync(receiptId)` 才登记已实际走通的路线。没有初始化自动注册或无条件通过。

Root hooks：

- CurrentPortIssue 遇 `SideWellObservationService.OwnPort(port.Id)` 时调用 `SideWellPortIssue(snapshot,port)`，以独立版本化安全合同处理这一条连接。
- port 交互进入旧 R3 EnterPort 分支之前调用 `if(await TrySideWellPortInteraction(target.Id)) return;`。该面板三个明确动作分别检查、登记、实际通行。
- 原 R3 spot 分支之前调用 `if(await TrySideWellObservationInteraction(target.Id)) return;`。record 取得真实公开原文；sample 只显示无独立采样委托；side_route 开真实写作、意思和登记预览。
- shared ClosePanel 调 `CloseSideWellObservationPreview(title)`；各 load/reset 调 `ResetSideWellObservationUi()`；保存退出/手动槽/Continue/Backup 流程先 `await SaveSideWellObservationDraft()`（false 时保持正文，不继续读档或退出）。

正文存原 `R3-O02-W-CONTEXT` task：`SaveDraftAsync` 真实 CAS，`ConfirmMeaningAsync(fields,assisted)` 需要当前保存正文与两份已取得原件；`PreviewComparisonAsync()` 只读；`ConfirmPreviewAsync(token)` 明确登记有限比较；`ClosePreview` 取消。字段是 side/main/comparison/unknown_conditions/causality/next_check/sampling，UI 每项默认尚未选择。IME 候选未提交时不写，CtrlEnter 无提交行为，保存不登记世界结果，正文改动关闭旧清单。

没有新的 ContentCompiler 玩家包，不生成额外样本或音轨。当前 Godot 原生驱动尚未执行，不能用这四个 SQLite 测试宣称正式原生可玩或语言合格。

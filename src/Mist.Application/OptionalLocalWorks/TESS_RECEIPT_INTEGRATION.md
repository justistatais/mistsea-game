# 泰丝回执桥集成

四个 `.cs.pending` 已静态完成，Root 统一纳入并串行构建：Core `TessLocalReceiptModels`、Application `TessLocalReceiptBridge`、tests `TessLocalReceiptBridgeCases`、UI `Root.TessLocalReceiptBridge`。没有修改冻结 ChapterSixService、几何、音轨或 shared Root。

测试注册：`Mist.Tests.OptionalLocalWorks.TessLocalReceiptBridgeCases.Register()`；过滤 `CP-TESS-BRIDGE-03`，共五项：两种正常补充运输先后、未承诺加本地通知完整三节点、站位/本人许可/Continue/错误实物、已提交中断重开恢复。同一主线和预算保持原结算归属。

构造 `TessLocalReceiptBridge(repository,catalog,chapterSix.Package)`。只读 `PreviewRecordAsync(node)` 返回当前实际核对与 token；`ClosePreview(token)` 取消；`ConfirmRecordAsync(token)` 明确登记；`ReadProjectionAsync(node)` 查已登记来源。当前站位固定为 contacts、supplement、local_notice，与原正式提交点相同。关闭清单与世代变化不得继续旧 token；封存准备后不登记新结果。

UI hook：现有 `TryChapterSixInteraction` 新建 `flow` 后调用 `AddTessLocalReceiptBridgeButton(flow,spot)`；shared ClosePanel 调 `CloseTessLocalReceiptBridgePreview(title)`；新档/Continue/槽读取 reset 调 `ResetTessLocalReceiptBridgeUi()`。按钮只核对已闭合原 CQ，不能接受或执行正文提交。人物面板继续用原 CompanionArcService.StatesAsync 自动展示同实例完成状态。

本桥没有新的正文或音轨，不需要额外 ContentCompiler 数据包；不得由初始化或 Refresh 自动发布。主线四件 FrontierLocalResult 不能替代原五箱 PZ-Q603-LOAD；只读开页不生产私人邀约与工作完成。

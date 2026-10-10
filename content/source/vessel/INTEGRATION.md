# 共享微光号船舱候选接入

本包补充 CX-060 三个共享空间的实际导航几何与工位。新范围由当前整合任务明确授权；冻结 baseline/reference 不变。几何是可审查灰盒，来源和未制作部分保存在 vessel_interior.json。它不创建船、航路、工程物资、同伴或故事事实。

`VesselInteriorService(repo, catalog, package, authoritativeGeometry)` 应传入当前 ChapterOneService.Geometry；公开 Geometry 会合并全部当前外部场景与三个 HUB 场景。客户端仅在 HUB 调用 MoveAsync，其他场景仍使用当前章节移动服务。

首次登船由玩家在 R1-S01.glimmer_handover 的实际站位明确办理。先调用 InspectBerthAsync，取得 `vessel_berth_verified` typed VesselBerth 回执，再将回执 ID 交 TravelService.RegisterBerthAsync。检查核对 Q104 `tidegate_vessel_handover` 的实例、GLIMMER 定义、N-DECKWATCH 与三个空间，并要求当前船 DockRegion=R1。缺真实移交、角色未到场或当前几何缺站位时拒绝。

在同一实际泊位明确登记三个空间：依次 PublishSpaceAsync("HUB-S01"/"HUB-S02"/"HUB-S03")，将各自 `ship_space_verified` typed VesselSpace 回执 ID 交 TravelService.RegisterShipSpaceAsync。来源绑定当前同船实例和包几何哈希；只登记经过 Core 校验的基础空间，不授予船体改造。TravelService 也必须传入服务公开的完整 Geometry。

随后 PreviewBoardAsync 生成捕获当前角色、船、泊位、空间和版本的命令，ConfirmPreviewAsync(token) 才实际登船。PreviewLeaveAsync 从甲板 gangway 回当前合法泊位。内部出口使用 PreviewExitAsync(portId)＋ConfirmPreviewAsync，或 EnterPortAsync(portId) 的同一实际预览与事务封装。ClosePreview 清除待提交意图；同一已提交 token 可读取幂等结果。每次写操作取得并释放原子 `input:foreground` lease，当前战斗、护送货物、运行中前景事件与 Q604 起航检查均会阻止外区船舱通行。

WorkspaceAtAsync(spot) 也检查实际到场，返回只读当前原件、分支稿头、唯一收藏、当前人物安排或工程工作单。查看材料不发现未取得材料，查看来信不制造通信或导师当前位置，查看工单不运输或安装，队伍投影不自动招募。PartyService 当前修改装备和采购仍要求 R1-S02/R1-S03，HUB-S03 本候选提供真实只读收藏与工位，尚未绕过此规则。草稿和帮助仍可从全局入口访问，不以回船为条件。

工位为 HUB-S01 gangway/deckwatch/harbor_view；HUB-S02 archive/correspondence/draft_desk/chart；HUB-S03 collection/workbench/roster/work_orders。两条相邻连接均双向；没有甲板直达检修舱的出口。R2、RF 与其他地区未制作真实泊位检查生产者，无法由本包开启旅行。

新增静态 Register 类为 Mist.Tests.VesselInterior.VesselInteriorCases，共六项工程用例。正常登船用例从空 SQLite ProfileStore 实际走过序章、Q101、Q102、Q103、Q104 后再检查泊位和三个空间，不使用所有权 fixture。冲突、最终阶段和缺失状态测试使用明确标注的工程 fixture。它们不替代原生操作、玩家录制或 6.5 语言校准。三个新 C# 文件已按整合者指令由 .cs.pending 同目录重命名为 .cs，现等待统一编译及执行，尚未宣称通过。

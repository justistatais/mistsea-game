# R1生活工作接入

learner_package.json 是仅含公开双语资料、具体几何、受理/操作站位及公开物件约束的候选投影。production_cards、provenance和本说明均author_only，不由运行时读出答案。主世界入口必须由真实Q101委托回执证明，不能仅设置R1 actor。正文版本 R1-LIFE-20261010-v2；新细节与冻结事实的边界见作者卡。

R1LifeService 构造参数为 IAdventureRepository、IEventLeaseStore、R1LifePackage 和既有 harbourGeometry。Geometry 保留 R1-S02旧几何，仅追加 inn_notice/to_inn；另追加O01/O02。InitializeRegionAsync 创建独立地方状态、既有 SC-R1-001 和 SC-R1-002未受理实例、四件唯一当地库存。Core/Application不读作者包。

Root.R1Life.cs 提供 LoadR1Life、OpenR1LifeQuests、TryR1LifeInteraction、SaveR1LifeDraft、CloseR1LifeUiPreview、ResetR1LifeUiState。街区新港口和两个生活空间由真实 TraverseAsync/EnterPortAsync 路径接入；PortIssue控制已发现入口。旅舍写作/接收分别 O01.writer/front_desk，剧场分别 O02.writer/programme，按钮不跨工位发奖。学习scope只提供当前View已得原文，查词不新增世界奖励。

tests 的 R1LifeCases.Register() 注册 CP-R1-03 20项；ActualEntry(ProfileStore) 真零XP完成序章/Q101并走到R1-S02，仅初始化地方世界，余额340XP，未发现、未接受生活委托。导出 normal-r1-life-native-entry 后，DEBUG RunR1LifeClientTests 使用该入口的独立副本。工程控制器填写明确候选正文和选择，不算自动英语理解、人类游玩或人工语言评测。

两条真实离线SAPI试音已分角色exact source slice生成，独立报告 r1-life-20261010T051032919483Z；共享绑定待合并，人审/时间对齐/多角色声线/rights仍pending。两个 listening_script 仍可书面辅助继续，不能计成实际听力练习。人工校准、审听、rights、IME、screenreader及真人流程审查仍pending/not_run。测试结果应由Root串行构建后的原始报告追加索引，不凭作者说明回填planning状态。

# R5 地方生活运行候选接入

本模块覆盖原件 Exploration_Level_Design_v0.1 §9.6（510–531）以及 World_Story_Bible_v0.1 §8.1（764–778）的《给街道一个名字》《失物不是证据》。冻结 chapter_five learner 中 R5-O01-R-SIGN/R5-O02-R-CLAIM 和 R5-O01/R5-O02 几何、正文、音轨未改。新增 old_city_life_contract.json 是公开的独立 r5-local-life-candidate-1；两居民岗位、样板序号、同锅外观、封装引介与有限流程是明确标识的原创未人审候选，不冒称原稿已有细节。

路牌流程实际询议两不同居民、明确选两种合格方案之一、核两替代样板、仅将选中的原同板从工坊安装到一个街口、检查当前两行可见字，再以实际保存正文、明确辅助意思和只读预览一次接收。另一板留工坊，局部标识可读显示；不重写地图、产权、沉降时间或通行法定规则。

失物流程从地方保管人检查不同的原同锅和封装纪念物，具体同锅许可与独立主人封装许可分别版本化。错误柄/印组合实际留下 uncertain_claim_pending，不准放行或指控；不同序号或接收人被拒。同物实际归还获准接收人，误关联只追加地方纠正，原 tag 保留。家庭画面、附信没有授权素材，内容读取/公开不提供；撤回当前封装核查使旧预览失效，但已实际交付的同物保持真实保管，不复制或远程回收。主线真实证据链不变。

两个 result 的 experience/marks/parts 均0。新本地实体不生产六塔物资、安全、权限、同伴奖励、救援唯一钥匙或结局资格，不充当12可选遭遇槽或96解谜预算。campaign 的 R5 个人任务 producer/reward 不受本模块修改。

## Root 接入

纳入六个文件（当前均 .cs.pending，待 Root 串行编译前统一改名）：

- Core/OptionalLocalWorks/OldCityLifeModels。
- Application/OptionalLocalWorks/OldCityLocalLifeService 和 .Property。
- tests/OptionalLocalWorks/OldCityLocalLifeCases。
- UI/Shell/Root.OldCityLocalLife。
- Diagnostics/Root.OldCityLocalLifeClientTests。

依赖已纳入的 SettlementSupportSection、SettlementNoteMeaning、SettlementPreview 和 FieldRelayParagraph。新 source 编译到 res://content/old_city_life_contract.json；Godot.FileAccess 读取 PCK，不依赖导出包裸文件路径。

共享 hooks：

- spot dispatch 在旧都泛型 propose_local_sign/request_lost_property_check 前调用 await TryOldCityLifeInteraction(spot)。本模块只截 R5-O01/R5-O02 并要求实际 R5 landing；其他 spot 或未登陆 return false。仍由服务核真实站位。
- ClosePanel(title) 调 CloseOldCityLifePreview(title)。SaveAdventureDrafts/退出/槽位/备份链加入 await SaveOldCityLifeDrafts() 并检查 bool。Continue/slot/profile reset 调 ResetOldCityLifeUi()。
- 实际档案入口可调用 await OpenOldCityLifeArchive()，只列已取得条款；完整原公开阅读仍由 ChapterFiveService 保存显示。本模块不会后台 Discover。
- R5-S02 公共标识显示可调用 await OldCityCurrentLocalSignLabel()；非 null 表明同板已实际安装，正式接收后也保留当前两行。R5-O01 现场已直接展示牌面。
- UI 初始化已自动向既有 RegisterRegionalLearningScope 注册 old-city-life 的实际取得版本/英文正文 hash；Reset 自动移除，无需改共享 learning provider。没有无源家庭内容 scope。
- 正文 task 为 R5-O01-W-NAME / R5-O02-W-RETURN。状态 R5-O01:local-sign / R5-O02:local-property；正式一次 semantic 为相应 :resolution，action 为 old_city_local_public_sign_received / old_city_local_private_property_handover_received。
- Program 注册 OldCityLocalLifeCases.Register()。CP-R5-LIFE-03 三个业务例；CP-R5-LIFE-05 一个原生入口例。
- Root DEBUG CLI dispatch 增 --old-city-local-life-client-test -> await RunOldCityLocalLifeClientTests()。build.py 新 native action 从 CP-R5-LIFE-05 的 normal-r5-local-life-native-entry.json 复制真实 ProfileStore 目录再启动原生驱动，输出 old_city_local_life_client_results.json。boot error flags 同步增加这一旗标。

所有正向测试通过 ChapterFiveCases.ActualArrival，实际零 XP 创建档→序章→前四章→真实 R4R5 航程→R5 register_landing 到6400XP；没有正向 fixture。业务覆盖两合格名称、同板/余板、只读取消、同物/接收人错误拒绝、不确定认领、私人内容不可用、撤回后的旧预览、原标签不覆写、CAS冲突/改文、Continue旧世代拒绝，以及主线实体/其他实物/余额逐项不变。原生 driver 从未接受地方任务的正常档起，实际UI完成完整公开阅读、邀请、无默认选择、安装/外观核查、CAS正文、明确辅助意思、取消预览与一次交付；安全导航使用当前真实配对出口。

当前仅进行了 JSON/站位合同与词法静态核对；尚未编译、SQLite 执行或原生运行。人工自然玩、IME、读屏、语言校准、私人素材权限与视觉音频人工审查仍 pending。


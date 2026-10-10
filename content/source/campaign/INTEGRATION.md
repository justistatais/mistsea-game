本包实现正常新档的序章与Q101工程候选。起点为R0-S01、0经验；100刻币是第七部分明示的起始预算。Q-P01、Q-P02、Q-P03、Q101的实际工作分别产生100、40、100、100经验，进入Q102时累计340，等级3、校准0。没有测试许可或直达序章完成记录。

`first_entry.json` 是原创待审的玩家材料与灰盒场景数据，可复制到 `game/content/campaign/first_entry.json`，使用 `StrictJson.Deserialize<CampaignPackage>` 读取。`production_cards.json` 仅供制作审查，不能复制到玩家材料目录或送入NPC上下文。运行时动作由CampaignService中的类型化白名单决定；源JSON里的Actions只提供控件引用，没有任意写世界的能力。

集成接口在 `src/Mist.Application/Campaign/CampaignService.cs`。正常入口使用 `InitializeNewAsync()`、`ViewAsync()`、`Geometry`。位置通过共享NavigationGrid寻路，再调用TraverseAsync与配对EnterPortAsync。资料必须在物件站位通过DiscoverAsync获取；OpenMaterialAsync只打开已得资料。场景操作调用ExecuteStationAsync；以下代码由独立工作区明确确认：

- confirm_coast_timeline → ConfirmTimelineAsync(CoastTimeline)
- choose_receiving_direction → ChooseLandingAsync(RescueLanding)
- write_rescue_message → 保存 `Q-P02:rescue-message` 草稿，ConfirmRescueMeaningAsync，再PreviewRescueMessageAsync与ConfirmSubmissionAsync
- write_harbour_request → 保存 `Q-101:work-request` 草稿，ConfirmHarbourMeaningAsync，再PreviewHarbourRequestAsync与ConfirmSubmissionAsync
- begin_shore_encounter → BeginShoreEncounterAsync，再使用共享BattleRequest、PreviewShoreActionAsync与ConfirmShoreActionAsync；撤回调用WithdrawShoreAsync

离线意思确认是明确类型化确认，没有自动理解自由英文，也没有语言band。正文保存使用既有IDraftStore CAS。旧稿、旧加载上下文或相关世界状态变动使预览拒绝；未知提交重试保留同一不可变命令。求援所用救援棚短距设备独立于QP03待修复的港口中继，材料明确说明这层边界。

人员实体 `coast:survivors` 保存四个唯一ID，包含临时协作的莉娜。离船、进入避风棚、登记、接应入港、到港登记分别与真实位置动作共同提交。近岸战斗不发杀敌经验、不正式招募莉娜，也不提前启用Q101之后的地方援勤。救援索与工具为两个唯一实物；索具安装后留在海岸绞盘，不成为渡轮电池或工程库存。

QP03的独立记录获取槽为 `material:QP03-R-RECORDER:acquired`，ActionType=`collect_coast_record`，source_root=`coast-independent-recorder`。导师邮包为 `material:QP03-R-MAIL:acquired`，ActionType=`collect_inspection_mail`，source_root=`inspection-post-Mara-letter`。原件只在QP02实际结算及当前中继检查成立后可领取。它们不产生玛拉当前状态或所在地的知识。

Q101实际委托的同一事务创建现有Q102所需的workshop、ferry、quest、proposal、权限和唯一支架/电池。权限source为真实 `Q-101:resolution`、授权方N-HAVEL、主发电机排除。Q102Ready=true后，角色仍在R1-S03哈文站位，可由既有Q102服务继续，不能再调用切片初始化制造经验。Q103/Q104与微光号移交由首章后续服务实现。

新增存储使用已有实体、物品、余额、草稿和回执事务，不新增SQL表。起始收藏调用既有PartyService，因而沿用其库存schema2迁移。调用方需同时持有CampaignPackage与RuntimeCatalog。

测试入口为 `Mist.Tests.Campaign.CampaignCases.Register()`。本分工未运行构建或测试，由主代理统一执行并保存实测证据。C#脚本驱动不代替真实玩家录像、Godot操作验收、语言人测、声音审听或素材权利签核。QP02更正仅有完整脚本，真实音轨尚未提供；读取脚本不能算真实听力。正式美术、后续章节、完整首章Gate仍待完成。

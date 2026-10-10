# 解谜样例材料与局部数据 v0.1

本文件为制作资料，含答案。英语目标固定6.5，所有新增材料均是未实测候选。短牌与操作文字只构成任务的一部分，不单独代表完整6.5能力。

## 继承的Q-102材料

继续使用 `dependencies/Original_Materials_v0.1.json` 的 Q102-R-NOTICE、Q102-R-B、Q102-R-VESSEL、Q102-R-BATTERY、Q102-L-REVISION，原文、段落ID及版本不变。完整短写作沿用第五部分，不额外生成第二份提交。

## 新增材料目录

| ID | 标题 | 模态 | 约词数 |
|---|---|---|---:|
| PZ102-R-WORKSHOP | West workshop: local maintenance modes | 阅读 | 169 |
| PZ102-R-PANEL | Main generator inspection tag | 阅读 | 46 |
| PZHOT-R-ORIGINAL | Original confirmation: BKG-17 | 阅读 | 81 |
| PZHOT-L-AMENDMENT | Guest confirmation of the date amendment | 听力脚本（无音轨） | 96 |
| PZHOT-R-ROOMS | Current booking and room inspection sheet | 阅读 | 120 |
| PZTIME-R-ORDER | Transfer M-12: authorisation and movement log | 阅读 | 80 |
| PZTIME-R-ARRIVAL | Receiving desk: arrival and copying record | 阅读 | 102 |
| PZMAP-R-HISTORY | Old city route sheet and current inspection | 阅读 | 144 |
| PZMAP-L-DIRECTIONS | Resident’s route clarification | 听力脚本（无音轨） | 79 |
| PZLOAD-R-MANIFEST | Supplementary load: packing list | 阅读 | 98 |
| PZLOAD-L-SEQUENCE | Installer’s delivery condition | 听力脚本（无音轨） | 86 |
| PZLAB-R-PROTOCOL | Local-control trial: scope and recorded conditions | 阅读 | 170 |

## PZ102-R-WORKSHOP — West workshop: local maintenance modes

父任务：Q-102；版本0.1；状态draft_target_65；家族FAM-PZ102-LOCAL-PUZZLE。

**PZ102-R-WORKSHOP-p01**

The workshop controller and the maintenance unit use a local reserve supply. They do not depend on the harbour main generator, and the ferry battery must remain on the ferry. The test lamp fixed to the workshop frame is part of this local system; it is not the navigation beacon needed for the evacuation.

**PZ102-R-WORKSHOP-p02**

To use the inspection passage without crossing the front scan area, either isolate the maintenance unit at this controller or select Fixed Inspection. In Fixed Inspection, the unit continues examining the fixed lamp until the operator cancels the mode. There is no automatic return to normal operation. This mode releases the side-door interlock, but it does not make the front scan area safe.

**PZ102-R-WORKSHOP-p03**

Isolation stops the unit and also releases the side-door interlock. Once the unit has been physically disabled, changing a switch cannot repair it. The inner bolt opens a separate return door to the square; that bolt is independent of the inspection-mode interlock. Leave the harbour generator off while its inspection is incomplete.


## PZ102-R-PANEL — Main generator inspection tag

父任务：Q-102；版本0.1；状态draft_target_65；家族FAM-PZ102-LOCAL-PUZZLE。

**PZ102-R-PANEL-p01**

Inspection incomplete. The flooded cables have not yet been isolated. Do not start the harbour main generator. This restriction does not prevent the use of the workshop’s separate local maintenance controller. For work on the ferry beacon, use the charged battery already recorded on that vessel.


## PZHOT-R-ORIGINAL — Original confirmation: BKG-17

父任务：SC-R1-001；版本0.1；状态draft_target_65；家族FAM-PZHOT-LOCAL-PUZZLE。

**PZHOT-R-ORIGINAL-p01**

Booking BKG-17 was originally confirmed for two guests arriving on the 16th of the current booking month. The guests requested two separate beds in a quiet room. A copied confirmation is kept with this letter for the accounts desk; it does not represent an additional booking.

**PZHOT-R-ORIGINAL-p02**

The room request and the arrival date are separate parts of the record. A later amendment may change one without changing the other. Please retain the earlier version so that the change can be checked.


## PZHOT-L-AMENDMENT — Guest confirmation of the date amendment

父任务：SC-R1-001；版本0.1；状态draft_target_65；家族FAM-PZHOT-LOCAL-PUZZLE。

**PZHOT-L-AMENDMENT-p01**

Clerk: I can see the original letter for the 16th and another confirmation for the 18th. Are these two groups travelling separately?

**PZHOT-L-AMENDMENT-p02**

Guest: No. We moved our arrival to the 18th, which is today in your booking register. We are still the same two guests under BKG-17. The revised confirmation replaces the arrival date, not the booking number. We still need separate beds and a quiet room.

**PZHOT-L-AMENDMENT-p03**

Clerk: Thank you. I will retain the old letter as a previous version rather than enter it as another group. The other arrival, BKG-22, belongs to a different guest.


## PZHOT-R-ROOMS — Current booking and room inspection sheet

父任务：SC-R1-001；版本0.1；状态draft_target_65；家族FAM-PZHOT-LOCAL-PUZZLE。

**PZHOT-R-ROOMS-p01**

BKG-22 is an independent booking for one guest arriving on the 18th. The guest requires a room that can be reached without stairs. This booking has not replaced or amended BKG-17. Today’s arrivals are to be checked against booking identities, not the number of pieces of paper on the desk.

**PZHOT-R-ROOMS-p02**

Room 2 has two separate beds and faces the quiet rear courtyard. A flight of stairs is the only access. Room 5 is a single room on the ground floor, reached without stairs; it faces the public square and is not offered as a quiet room. Both rooms have been inspected and are available for the current stay. The room cards and the current inspection refer to these same rooms.


## PZTIME-R-ORDER — Transfer M-12: authorisation and movement log

父任务：Q-202；版本0.1；状态draft_target_65；家族FAM-PZTIME-LOCAL-PUZZLE。

**PZTIME-R-ORDER-p01**

This file concerns transfer M-12, not the detained engineer. Personal names are held in a restricted attachment and are unnecessary for establishing the sequence below. All times on these sheets refer to the same local day.

**PZTIME-R-ORDER-p02**

The departure officer checked the authorisation before allowing the vehicle to leave at 14:10. The authorisation sheet does not carry a reliable issue time. It establishes that approval existed before departure, but it does not justify assigning an exact minute to that approval.


## PZTIME-R-ARRIVAL — Receiving desk: arrival and copying record

父任务：Q-202；版本0.1；状态draft_target_65；家族FAM-PZTIME-LOCAL-PUZZLE。

**PZTIME-R-ARRIVAL-p01**

The receiving desk recorded the vehicle’s arrival at 14:40. The entry was made by the receiving officer when the vehicle reached the site. At 16:05 the same day, an archive clerk copied the movement and arrival entries into the central ledger. The later ledger stamp marks the copying, not a second journey or a later physical arrival.

**PZTIME-R-ARRIVAL-p02**

A duplicate of the departure entry was attached to the ledger. It is a copy of the departure officer’s record, not another observer’s account. Nothing in these documents states what the central office knew before departure or why its ledger was updated later.


## PZMAP-R-HISTORY — Old city route sheet and current inspection

父任务：Q-501；版本0.1；状态draft_target_65；家族FAM-PZMAP-LOCAL-PUZZLE。

**PZMAP-R-HISTORY-p01**

The old route sheet shows the main bridge as a direct crossing from the checkpoint towards North Station. That line describes the former route; it is not current permission to cross. The central bridge span is now closed following subsidence.

**PZMAP-R-HISTORY-p02**

A separate controlled passage through the checkpoint remains available after the traveller’s current inspection credentials have been checked. It does not require crossing the closed span. Travellers unable to use that passage can reach the station through the existing service tunnel once its obstruction and local hazard have been dealt with. The tunnel work can be completed with the basic inspection tools provided for the mission.

**PZMAP-R-HISTORY-p03**

Both routes reach the station’s duty hall, not the detention rooms. Entering the hall does not itself grant access to the detained people. No current route is made safe merely because it still appears on the older map.


## PZMAP-L-DIRECTIONS — Resident’s route clarification

父任务：Q-501；版本0.1；状态draft_target_65；家族FAM-PZMAP-LOCAL-PUZZLE。

**PZMAP-L-DIRECTIONS-p01**

From the ferry landing, follow the inhabited street to the checkpoint. When I say the bridge is closed, I mean the old central crossing shown on the historic sheet, not every entrance at the checkpoint. Have your present inspection credentials checked for the controlled passage, or use the service tunnel after making it passable. Both bring you to the duty hall. Do not mark the detention rooms as accessible just because you have found a way to the station.


## PZLOAD-R-MANIFEST — Supplementary load: packing list

父任务：Q-603；版本0.1；状态draft_target_65；家族FAM-PZLOAD-LOCAL-PUZZLE。

**PZLOAD-R-MANIFEST-p01**

The essential safety shipment and all supplies reserved for the six-tower programme have already been allocated separately. This plan concerns five additional packages and must not draw on those protected stocks. Each package must travel once, intact, in one of the two supplementary trips.

**PZLOAD-R-MANIFEST-p02**

The installation-tool crate occupies three cargo spaces, and the mounting kit occupies one. The lighting module occupies four spaces. Weather panels occupy two spaces, as does the archive case. Each trip can carry at most six spaces. These numbers describe declared crate sizes, not weights that the crew may reduce by leaving contents behind.


## PZLOAD-L-SEQUENCE — Installer’s delivery condition

父任务：Q-603；版本0.1；状态draft_target_65；家族FAM-PZLOAD-LOCAL-PUZZLE。

**PZLOAD-L-SEQUENCE-p01**

The tools and the mounting kit must arrive on an earlier trip than the lighting module, so that we can prepare the installation position before it gets here. Sending them on the same trip as the module will not meet that requirement. The weather panels and the archive case can go on either trip. Choose which of those two you would prefer to receive earlier, but keep both trips within their capacity. These are additional deliveries; the basic safety work is not waiting for this choice.


## PZLAB-R-PROTOCOL — Local-control trial: scope and recorded conditions

父任务：Q-402；版本0.1；状态draft_target_65；家族FAM-PZLAB-LOCAL-PUZZLE。

**PZLAB-R-PROTOCOL-p01**

These are short local trials of Controller A. Each run lasted ten minutes with the isolation check completed. The reported load is measured in the trial apparatus’s own units; no outside engineering calculation is required. “Stable” means the apparatus met the declared condition for those ten minutes, not that it has been approved for every tower or for permanent use.

**PZLAB-R-PROTOCOL-p02**

Run T1 used load 40 with the reserve connected and remained stable. T2 used load 70 with the reserve connected and stopped. T3 used load 40 without the reserve and stopped. T4 repeated the T1 configuration and remained stable. No run tested load 55, and none tested full-scale operation across the six regional towers.

**PZLAB-R-PROTOCOL-p03**

To compare the recorded effect of changing load while holding the listed configuration constant, compare T1 and T2. T1 and T3 address a different change. A careful report should describe which conditions have actually been observed and leave untested combinations open. Further trials and field checks remain necessary before any wider operating proposal can be accepted.

## 局部操作数据与解法核验

### 旅舍

当地住宿登记日18日；旧到店日16日。BKG-17保留两床安静要求，分配2号；BKG-22需单人平层，分配5号。副本不新增订单；改期仅覆盖arrival字段。日期不是用户设备实时时间。

### M-12

授权（时刻未知）在14:10出发之前；14:40到达；16:05抄入总账。顺序唯一，授权精确时刻和中央知情/意图没有证据。它不是导师转移记录。

### 当前旧都路线

街区—检查点—已核查通道—值守厅，或街区—检查点—已处置的检修隧道—值守厅。历史桥跨关闭。两者的实现均沿用第三部分，不等于扣押区已可访问。

### 两趟补充运输

A：工具3＋安装件1＋面板2先到，灯4＋档案2后到。B：工具3＋安装件1＋档案2先到，灯4＋面板2后到。两种均可行，无隐藏最优；与基础和结局物资不共享。

### 试验记录

| 记录 | 负荷 | 储备 | 10分钟结果 |
|---|---:|---|---|
| T1 | 40 | 有 | stable |
| T2 | 70 | 有 | stopped |
| T3 | 40 | 无 | stopped |
| T4 | 40 | 有 | stable |

T1/T2及T2/T4都能比较负荷变化。T1/T3适于另一个储备问题。未测55，不批准六塔永久运行。试验结论只对应声明条件。

## 制作提醒

这些英文稿只能进入固定事实下的编辑、审听和试测流程。配套求解器证明的是有限候选集中的逻辑性质，不证明英文表述已经达到6.5，不自动评分自由文本。资料权限与提示暴露由前五部分决定。

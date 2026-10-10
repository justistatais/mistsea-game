"""Produce only the twelve new optional contract sources; original design files stay immutable."""
from pathlib import Path
import hashlib, json, re

ROOT=Path(__file__).resolve().parents[3]
OUT=Path(__file__).resolve().parent
VERSION='OPTIONAL-ENCOUNTERS-20261010-v1'
# New local details fill the twelve explicitly unassigned Combat_System appendix slots.
# These are complete source candidates, never retroactive statements about the original narrative.
specs=[
('R1','R1-S09',(22,48),'Gate counterweight cage','闸门配重笼','gate_counterweight_cage','ROLE-R1-SPARE-GATE-WATCH',
 'A spare counterweight has begun to swing inside its service cage beside the tower base. The gate watch offers a separate, voluntary job: stop the cage actuator and examine its retaining pin. The public path and the working tower gate remain under their existing permits. This cage carries no load from the tower and has been disconnected from the live drive. The watch keeps the cage, pin and actuator under local ownership.',
 '塔基旁备用配重在检修笼内摆动。闸门值守提出一项独立自愿工作：制止笼内执行器并检查固定销。公共道路和正在使用的塔闸沿用现有许可。检修笼不承载塔站负荷，已与在线驱动断开；笼、销与执行器均由当地值守保管。',
 'After the actuator stops, check that the retaining pin is seated and that the cage has no remaining motion. Record this cage only. Its inspection leaves the tower cooling valves, main isolation and ship handover to their own procedures. Leaving the additional job unfinished is an acceptable choice; the original gate route remains available.',
 '执行器停止后，检查固定销就位及检修笼无残余运动，只记录这只笼。塔站冷却阀、主机隔离和船务交接各有自己的规程。可以不完成这项追加工作，原闸门路线仍可使用。',[(180,500,3,'unit',32),(90,0,3,'column',18)]),
('R1','R1-S09',(77,48),'Emergency bell shutter','应急铃遮板','emergency_bell_shutter','ROLE-R1-BELL-KEEPER',
 'The bell keeper has removed a spare shutter from the emergency bell housing and placed it on a disconnected test frame. A small striker repeatedly catches its edge. The keeper asks for the striker to be brought under control before the shutter is checked for free movement. The working alarm stays in service on another circuit; this contract authorises access to the test frame alone.',
 '守铃人将备用遮板从应急铃壳取下，放在断开的测试架上。小型敲击器反复勾住边缘。守铃人请人在检查遮板自由行程前制止敲击器。现用警报仍由另一线路维持；本合同只许可使用测试架。',
 'Check the stopped striker and the shutter edge at the frame. The result means this spare shutter can be handled safely. It provides no general alarm certificate and no permission to alter the public signal. If another person needs the frame, withdraw the contract and return the original entry condition before yielding the work area.',
 '在测试架检查已停敲击器及遮板边缘。结果只表示这块备用遮板可安全操作，不是整套警报合格证，也不许可修改公共信号。若别人需要测试架，应撤回合同并恢复原入口后交出工区。',[(210,1000,4,'all',16)]),
('R2','R2-S06',(22,24),'Seed trolley parking brake','种子车驻车制动','seed_trolley_parking_brake','ROLE-R2-TROLLEY-KEEPER',
 'An empty seed trolley is parked in a dry alcove above the transfer station. Its parking-brake tester has begun pulling against the locked wheel. The trolley keeper offers a local contract to stop the tester and inspect the brake pawl. The trolley is empty, its frame is tied down, and its test lead is separate from the conveyor. Seed stocks and the live greenhouse water supply remain outside this work.',
 '转供站上层干燥壁龛停着一辆空种子车，驻车制动测试器正牵拉锁定车轮。保管人提出当地合同，请人制止测试器并检查制动棘爪。车内无货，车架固定，测试线与输送器分离。种子库存及温室在线供水不在工作范围内。',
 'Once both testing arms are controlled, inspect the pawl against the wheel notch. Accept a result for this empty trolley only. No seeds are transferred, no beds are planted and no conveyor safety record is created. The greenhouse route keeps its own isolation, drainage and return checks.',
 '两支测试臂均制止后，检查棘爪与轮槽的配合，只受理这辆空车的结果。不转移种子、不栽苗床，也不生成输送器安全记录；温室路线仍需自己的隔离、排水及回路复核。',[(220,600,3,'unit',38),(130,0,3,'column',22)]),
('R2','R2-S06',(74,24),'Dry gauge rack latch','干式仪表架锁扣','dry_gauge_rack_latch','ROLE-R2-GAUGE-CUSTODIAN',
 'The gauge custodian is clearing a dry rack for instruments that have already been removed from service. A latch motor cycles against an empty bracket while a checking head sweeps the upper rail. The offered contract concerns these two devices and the rack latch. No reading from this rack describes the present water level, and no retained archive is opened by accepting the work.',
 '仪表保管人正在清理干式架，存放已经停用的仪表。锁扣电机在空支架上循环动作，检查头扫过上轨。所提合同只涉及这两件设备和架锁。架上读数不说明当前水位，接受工作也不开放留存档案。',
 'Control both devices, then check that the latch holds the empty bracket without movement. A completed rack inspection leaves current transfer records and June’s custody permissions unchanged. The rack remains local property and can be declined without delaying the main investigation.',
 '制止两件设备后，检查锁扣能无运动地扣住空支架。完成仪表架检查不会改变当前转运记录及琼的保管许可。架仍为当地财产，可以拒绝这项工作而不延误主调查。',[(240,1400,5,'column',30),(150,300,3,'unit',25)]),
('R3','R3-O02',(40,35),'Observation cradle clamp','观测托架夹具','observation_cradle_clamp','ROLE-R3-SIDEWELL-TECHNICIAN',
 'In the safe side-well workshop, an unloaded observation cradle is secured to the bench. Its clamp tester has continued squeezing after the sample holder was removed. The technician asks for a voluntary control and inspection job on the tester and clamp. No one enters the shaft during this work. The cradle is a separate spare, and the rescue route and original mining instruments retain their own records.',
 '安全侧井工坊里，无负荷观测托架固定在工作台上。样本座已取下，夹具测试器仍在挤压。技术员请人自愿制止并检查测试器与夹具；工作期间无人入井。托架是独立备用件，救援路线和原矿区仪表继续使用各自记录。',
 'When the clamp tester is controlled, inspect its open stop and the empty cradle. This permits the spare to be handled; it adds no evidence about a historical batch, no measurement from inside the shaft and no judgment about Oren. Declining the contract preserves access to the public side-well observation procedure.',
 '夹具测试器制止后，检查张开限位及空托架。结果只许可安全操作备用件，不增加历史批次证据、井内测量或对奥伦的判断。拒绝合同后，公开侧井观测规程仍可使用。',[(310,1200,4,'unit',48)]),
('R3','R3-O02',(60,65),'Survey lamp swivel','测绘灯回转架','survey_lamp_swivel','ROLE-R3-SIDEWELL-LAMP-WATCH',
 'A spare survey lamp stands behind the side-well safety rail. The lamp has been disconnected, but the swivel tester and its rail follower still repeat their motions. The lamp watch has fixed the stand to the floor and offers a separate job to bring these devices under control. The work uses no mine pumping capacity and draws nothing from the miners’ current shift stock.',
 '备用测绘灯放在侧井安全栏后。灯已断开，但回转测试器及轨道随动器仍重复运动。守灯人将灯架固定在地面，提出独立工作，请人制止两设备。工作不使用矿区抽水能力，也不提取矿工当前班次库存。',
 'Inspect the stopped swivel, the secured base and the empty lamp holder. The resulting record covers safe handling of this spare stand. It leaves the mining tower load policy and shift handover untouched. The watch will keep the spare in storage if you decline or withdraw.',
 '检查已停回转架、固定底座及空灯座。记录只覆盖这只备用架的安全操作，不改变采矿塔负荷策略或交班。当事人拒绝或撤回时，守灯人继续保管备用架。',[(320,1800,5,'column',40),(170,0,3,'all',22)]),
('R4','R4-S07',(22,72),'Blank test shutter','空载试验遮板','blank_test_shutter','ROLE-R4-TEST-FRAME-CUSTODIAN',
 'At the edge of the island test yard, a blank shutter sits on an independent, disconnected frame. Its test actuator has begun striking a mechanical stop. The frame custodian offers a voluntary contract to control the actuator and inspect the shutter restraint. No sample is fitted and no output from the frame feeds the current experiment. The original experiment configurations and failed runs remain available separately.',
 '孤网试验场边缘，空遮板装在独立且断开的测试架上。执行器开始撞击机械限位。保管人提出自愿合同，请人制止执行器并检查遮板固定件。架上无样本，输出不进入当前试验；原试验配置和失败记录分别保留。',
 'After control, inspect the restraint and confirm that the blank shutter remains motionless. This record concerns handling the spare frame. It neither validates a material formula nor overwrites a retained failure. The main trial and calibration route can continue without this additional contract.',
 '制止后检查固定件，确认空遮板保持静止。记录只涉及备用架操作，不核定材料配方，也不改写留存失败；主试验及校准路线可独立继续。',[(360,1800,5,'column',52)]),
('R4','R4-S07',(62,60),'Disconnected return coupler','断开回线耦合器','disconnected_return_coupler','ROLE-R4-SPARE-COUPLER-WATCH',
 'A return coupler awaiting inspection lies in a closed tray beside the test-yard store. Two checking heads move along its separated halves. The store watch offers a bounded job to stop both heads and inspect the coupling faces. The tray has no connection to a tower return line. All parts remain assigned to this tray, so accepting the work reserves no component from the regional conversion plan.',
 '待检回线耦合器放在试验场仓旁封闭托盘里，两只检查头沿已分离的两半移动。仓储值守提出有限工作，请人停止两检查头并检查耦合面。托盘不连接塔回线，零件仍归该托盘；接受工作不预留区域转换方案的组件。',
 'Check the separated faces only after both heads stop. Record the spare coupler and its local keeper. No regional installation, current output test or transition-supply delivery follows from this result. The work area can be yielded through a full entry withdrawal while the original source stays in the journal.',
 '两检查头停止后才检查分离的耦合面，记录备用耦合器及当地保管人。结果不产生区域安装、当前输出试验或过渡供给交付。可以完整撤回入口后让出工区，原说明继续保存在资料日志。',[(300,1200,4,'unit',45),(220,600,4,'all',28)]),
('R5','R5-S04',(22,24),'Tunnel sign carrier','隧道标牌运架','tunnel_sign_carrier','ROLE-R5-PUBLIC-SIGN-KEEPER',
 'The public sign keeper has parked an empty sign carrier in a side recess of the maintenance tunnel. A guide motor and its retaining runner continue moving inside a tied-down frame. The offered contract concerns these devices and the carrier brake. No sign wording is changed, no checkpoint credential is granted and no detention door is connected to this frame.',
 '公共标牌保管人将空运架停在检修隧道侧壁龛，固定框内导向电机及固定滑块仍在运动。所提合同只涉及这两件设备及运架制动，不改变标牌文字、不授查验凭证，也不与拘留区门相连。',
 'Control both devices and inspect the carrier brake against its parked wheel. The result records safe handling of one empty sign carrier. It gives no testimony about Voss or Mara and reveals no private record. The lawful rescue and review routes remain governed by their existing permits.',
 '制止两件设备后，检查运架制动与停驻轮的配合。结果只记录一辆空标牌架可安全操作，不提供沃斯或玛拉的证言，不披露私人记录。合法救援及复核路线仍由原许可约束。',[(390,1600,5,'unit',56),(250,0,3,'column',32)]),
('R5','R5-S04',(74,24),'Drain cover lifting frame','排水盖起吊架','drain_cover_lifting_frame','ROLE-R5-TUNNEL-STORE-WATCH',
 'A lifting frame for spare drain covers is locked to the tunnel store wall. Its hoist tester has begun pulling against a dummy weight. The store watch offers a separate contract to stop the tester and inspect the retaining chain. The real drain cover remains in place, and the dummy weight has no link to the evacuation route. Only the closed test frame is included in this permission.',
 '备用排水盖起吊架锁在隧道仓墙上，卷扬测试器开始牵拉假负荷。仓储值守提出独立合同，请人停止测试器并检查固定链。实际排水盖仍在原位，假负荷不连接撤离路线；许可只覆盖封闭测试架。',
 'Inspect the chain and locked hoist after control. Keep the result with the lifting frame’s own contract. It does not open the tunnel exit, establish tower safety or release a person. Refusing this job leaves the original mission kit and lawful path available.',
 '制止后检查链及锁定卷扬，将结果留在起吊架自己的合同下。它不打开隧道出口、不建立塔站安全，也不释放人员。拒绝工作后，原任务装备及合法道路仍可使用。',[(470,2200,6,'all',34)]),
('R6','R6-S06',(22,24),'Empty sled runner clamp','空雪橇滑条夹具','empty_sled_runner_clamp','ROLE-R6-OUTER-SLED-KEEPER',
 'Once the protected cargo has reached its receiving point, the outer-line sled keeper can offer work on an empty spare sled. Two runner testers cycle inside its closed maintenance rack. The keeper asks for both testers to be controlled and the runner clamp inspected. No cargo, baseline component or enhanced transition crate is assigned to this rack. The current freight receipt stays with the freight operation.',
 '受保护货物到达接收点后，外线雪橇保管人可以提供空备用雪橇工作。两个滑条测试器在封闭检修架内循环；保管人请人制止两者并检查滑条夹具。架内未分配货物、基础组件或增强过渡箱，当前货运回执仍归货运工作。',
 'Check the clamp only after both testers stop. The resulting record permits handling this empty sled. It supplies no transport result, no six-tower preparation and no authority for a final departure. A paused or declined contract keeps the spare with its keeper and preserves the protected main cargo.',
 '两测试器停止后才检查夹具。结果只许可操作空雪橇，不提供运输结果、六塔准备或最终起航权限。暂停或拒绝时，备用雪橇留在保管人处，主线受保护货物继续保留。',[(470,1800,5,'column',64),(270,500,4,'unit',40)]),
('R6','R6-S06',(74,24),'Spare storm screen frame','备用风暴挡屏架','spare_storm_screen_frame','ROLE-R6-SPARE-SCREEN-WATCH',
 'The screen watch has placed a folded spare storm screen in a closed test frame off the freight line. Its tensioning head keeps drawing against a fixed limit. The watch offers a voluntary control and inspection contract after the current cargo has been handed over. The frame is disconnected from all six tower worksites and contains no common-protocol equipment.',
 '挡屏值守将折叠备用风暴屏放在货运线外封闭测试架上，张紧头持续牵拉固定限位。当前货物交接后，值守提出自愿制止与检查合同。架与六塔工位均断开，也不含共同协议设备。',
 'After the tensioning head is controlled, inspect the limit pin and folded frame. The local inspection records safe handling of this spare screen. Regional signatures, current field trials and the three final policy choices retain their own conditions. This contract can remain unaccepted when the ship departs.',
 '张紧头制止后，检查限位销及折叠架。当地检查只记录这只备用屏可安全操作；区域签署、当前现场试验及三项最终政策选择保留自己的条件。船舶起航时，本合同可以仍未接受。',[(560,2500,6,'all',40)]),
]

def main():
 contracts=[];materials=[];spots=[];cards=[]
 for idx,s in enumerate(specs):
  r,arena,pos,title,zh,target,keeper,en1,zh1,en3,zh3,enemies=s
  number=idx%2+1;cid=f'ENC-{r}-O{number:02}';spot=arena+('.optional_contract_one' if number==1 else '.optional_contract_two');mid=f'{r}-OPT-O{number:02}-R-CONTRACT';action=f'inspect_{target}_zero_motion'
  es=[]
  for j,(hp,armor,threshold,lock,damage) in enumerate(enemies):
   es.append(dict(id=f'{r.lower()}_o{number:02}_device_{j+1}',name=f'{title} testing device {j+1}',name_zh=f'{zh}测试设备{j+1}',slot=j,hp=hp,armor_bp=armor,pressure_threshold=threshold,initiative=12+j,pattern=[dict(name=f'{target} '+('direct pull' if lock=='unit' else 'column sweep' if lock=='column' else 'frame-wide pulse'),damage=damage,lock=lock,attack=True,interruptible=True),dict(name=f'{target} reset and pause',damage=0,lock='unit',attack=False,interruptible=True)]))
  details='; '.join(f"device {j+1}: control progress {e['hp']}, armour reduction {e['armor_bp']/100:g}%, stability threshold {e['pressure_threshold']}, {e['pattern'][0]['lock']} impact {e['pattern'][0]['damage']}" for j,e in enumerate(es))
  detailszh='；'.join(f"设备{j+1}：制止进度{e['hp']}、护甲减伤{e['armor_bp']/100:g}%、稳定阈{e['pressure_threshold']}、{'单体' if e['pattern'][0]['lock']=='unit' else '同列' if e['pattern'][0]['lock']=='column' else '全场'}冲击{e['pattern'][0]['damage']}" for j,e in enumerate(es))
  en2=f"Read this complete notice at the marked stand, then explicitly accept contract {cid}. The permission covers reading this notice, controlling the declared devices and inspecting {target}. The public standard configuration is {details}. Each device alternates its attack with a non-attacking reset. Use your current equipment and two lawful assistance seats; declared difficulty changes are fixed at entry. Previewing and cancelling an action change nothing. Withdrawal restores the full combat entry, including its carried supplies. Continue resumes the same instance. After control, perform {action} at this stand. The contract awards no experience, marks or parts."
  zh2=f"在标记站位取得完整说明，再明确接受合同{cid}。许可覆盖本说明、已声明设备的制止及{target}检查。公开标准配置为{detailszh}。每设备攻击与不攻击复位轮替。使用当前装备及两个合法援助席，难度在入口固定。动作预览和取消不改变现场；撤回完整恢复战前入口及所带补给，继续沿用同一实例。制止后在本站执行{action}。合同不奖励经验、币或零件。"
  contract=dict(id=cid,region=r,scene=arena,spot=spot,material=mid,title=title,title_zh=zh,target=target,inspection_action=action,local_custodian=keeper,permission_actions=['read_declared_contract','control_declared_threats','inspect_declared_target'],enemies=es)
  paragraphs=[dict(id=f'{mid}.p{j+1}',en=en,zh=cn) for j,(en,cn) in enumerate([(en1,zh1),(en2,zh2),(en3,zh3)])]
  contracts.append(contract);materials.append(dict(id=mid,scene=arena,spot=spot,content_version=VERSION,access_class='acquired_local_contract',paragraphs=paragraphs));spots.append(dict(scene=arena,spot=dict(id=spot,stand=dict(x=pos[0],y=pos[1]),purpose=f'独立可选安全合同 · {zh}')))
  cards.append(dict(encounter=cid,baseline_slot=f'docs/baseline/Combat_System_v0.1.md:{527+idx}',authorship='new_original_local_detail_candidate',review='human_pending',answers_author_only=dict(target=target,operation=action,completion='actual_current_permit + current complete shared combat + onsite exact target inspection',no_main_reward=True),qa=['current stand and discovered current-version source','explicit acceptance and revocation','readonly preview/cancel','all enemies controlled before inspection','one result + replay + load CAS','full entry/supply withdrawal','original mainline gates and reward balances unchanged']))
 p=dict(content_version=VERSION,player_projection=True,scope='twelve_independent_local_safety_contracts_candidate',review_status='original_unreviewed',human_review='human_pending',numeric_review='original_candidate_not_calibrated',target_bands=dict(reading=6.5,listening=6.5,writing=6.5),contracts=contracts,materials=materials,extra_spots=spots)
 def save(name,obj): (OUT/name).write_text(json.dumps(obj,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
 save('learner_package.json',p);save('cards.author_only.json',dict(version=VERSION,player_projection=False,cards=cards,human_review='pending',audio='not_produced_reading_contracts'))
 baseline=ROOT/'docs/baseline/Combat_System_v0.1.md';save('provenance.author_only.json',dict(version=VERSION,player_projection=False,baseline_file=str(baseline.relative_to(ROOT)).replace('\\','/'),baseline_sha256=hashlib.sha256(baseline.read_bytes()).hexdigest(),baseline_slots_only=True,new_authorship='all detailed targets, local keepers, safe stands, source prose, numeric configurations are new unreviewed candidates',learner_sha256=hashlib.sha256((OUT/'learner_package.json').read_bytes()).hexdigest(),source_word_counts={m['id']:len(re.findall(r"[A-Za-z]+(?:['’-][A-Za-z]+)*",' '.join(p['en'] for p in m['paragraphs']))) for m in materials},calibration='not_run',human_signature=None,rights_review='pending'))
 print(json.dumps(dict(contracts=len(contracts),materials=len(materials),paragraphs=sum(len(m['paragraphs']) for m in materials),version=VERSION)))

if __name__=='__main__':main()

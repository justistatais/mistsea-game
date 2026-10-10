from pathlib import Path
r=Path.cwd();p=r/'game/UI/Shell/Root.Combat.cs';s=p.read_text(encoding='utf8')
s=s.replace('(FirstApproachEncounterSelected?"塔基','(FieldRelayEncounterSelected?"辅助机卫已实际制止。返回原维修工位，继续同机隔离、固定、复位与当前零能检查。":FirstApproachEncounterSelected?"塔基',1)
s=s.replace('Button(links,"暂停编辑同一份草稿",()=>{if(FinaleEncounterSelected)','Button(links,FieldRelayEncounterSelected||FirstApproachEncounterSelected?"暂停查处置原说明":"暂停编辑同一份草稿",()=>{if(FieldRelayEncounterSelected)Execute(OpenFieldRelayContract);else if(FirstApproachEncounterSelected)OpenArchive();else if(FinaleEncounterSelected)',1)
p.write_text(s,encoding='utf8')

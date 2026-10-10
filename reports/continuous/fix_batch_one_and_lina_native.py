from pathlib import Path
r=Path(__file__).resolve().parents[2]
def edit(path,old,new):
 p=r/path;s=p.read_text(encoding='utf-8-sig');assert old in s,(path,old);p.write_text(s.replace(old,new,1),encoding='utf8')
edit('game/Diagnostics/Root.VesselLifeClientTests.cs','string output=Environment.GetEnvironmentVariable','string output=System.Environment.GetEnvironmentVariable')
edit('game/UI/Shell/Root.LinaPersonalWork.cs','private static OptionButton LinaChoice','private OptionButton LinaChoice')
edit('game/UI/Shell/Root.LinaPersonalWork.cs','Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)','System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments)')
edit('game/Root.cs','        if(OS.GetCmdlineUserArgs().Contains("--vessel-life-client-test"))','        if(OS.GetCmdlineUserArgs().Contains("--lina-personal-client-test"))await RunLinaPersonalWorkClientTests();\n        if(OS.GetCmdlineUserArgs().Contains("--vessel-life-client-test"))')
edit('game/Root.cs','or "--vessel-life-client-test" or','or "--lina-personal-client-test" or "--vessel-life-client-test" or')
p=r/'tools/ci/build.py';s=p.read_text(encoding='utf8');s=s.replace('"optional-r6-client-test"}', '"optional-r6-client-test","lina-personal-client-test"}')
for old,new in [('"optional-r6-client-test":"CP-R6-03"}', '"optional-r6-client-test":"CP-R6-03","lina-personal-client-test":"CP-LINA-PERSONAL-05"}'),('"optional-r6-client-test":"normal_zero_XP_R6_both_independent_optional_contracts"}', '"optional-r6-client-test":"normal_zero_XP_R6_both_independent_optional_contracts","lina-personal-client-test":"actual_zero_XP_Lina_native_entry"}'),('"optional-r6-client-test":"normal-optional-r6-native-entry.json"}', '"optional-r6-client-test":"normal-optional-r6-native-entry.json","lina-personal-client-test":"normal-lina-personal-native-entry.json"}'),('"optional-r6-client-test":"optional_r6_client_results.json"}', '"optional-r6-client-test":"optional_r6_client_results.json","lina-personal-client-test":"lina_personal_client_results.json"}')]:
 assert old in s; s=s.replace(old,new)
p.write_text(s,encoding='utf8')
edit('game/UI/Shell/Root.Combat.cs','s.Outcome==CombatOutcome.Controlled?(FieldRelayEncounterSelected?', 's.Outcome==CombatOutcome.Controlled?(OptionalEncounterSelected?"声明设备已制止。返回同一合同工位，另行检查本合同的具体目标。":FieldRelayEncounterSelected?')
print('First batch compile fixes and Lina native driver wired, execution pending.')

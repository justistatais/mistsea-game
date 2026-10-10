from pathlib import Path
r=Path(__file__).resolve().parents[2]
def edit(path,old,new):
 p=r/path;s=p.read_text(encoding='utf-8-sig');assert s.count(old)==1,(path,old,s.count(old));p.write_text(s.replace(old,new,1),encoding='utf8')
p=r/'game/Diagnostics/Root.ScopedPersonalClientTests.cs.pending';assert not p.with_suffix('').exists();p.rename(p.with_suffix(''))
for name in ['Four','Five']:
 edit('game/UI/Shell/Root.Chapter'+name+'.cs','AddRegionalDictionary(flow,reader);','AddRegionalDictionary(flow,reader);AddScopedPersonalSourceControls(flow,m.Quest);')
edit('game/Root.cs','        if(OS.GetCmdlineUserArgs().Contains("--old-city-local-life-client-test"))','        if(OS.GetCmdlineUserArgs().Contains("--scoped-personal-client-test"))await RunScopedPersonalClientTests();\n        if(OS.GetCmdlineUserArgs().Contains("--old-city-local-life-client-test"))')
edit('game/Root.cs','or "--old-city-local-life-client-test" or','or "--scoped-personal-client-test" or "--old-city-local-life-client-test" or')
edit('game/UI/Campaign/CampaignClient.ReleaseProbe.cs','"RunOldCityLocalLifeClientTests"','"RunOldCityLocalLifeClientTests","RunScopedPersonalClientTests"')
edit('tools/ci/package_windows.py','"old_city_local_life_client_results.json"]','"old_city_local_life_client_results.json","scoped_personal_client_results.json"]')
p=r/'tools/ci/build.py';s=p.read_text(encoding='utf8');s=s.replace('"old-city-local-life-client-test"}', '"old-city-local-life-client-test","scoped-personal-client-test"}')
for old,new in [('"old-city-local-life-client-test":"CP-R5-LIFE-05"}', '"old-city-local-life-client-test":"CP-R5-LIFE-05","scoped-personal-client-test":"CP-PERSONAL-DETAIL"}'),('"old-city-local-life-client-test":None}', '"old-city-local-life-client-test":None,"scoped-personal-client-test":"Normal source-backed prior nodes produce a native entry"}'),('"old-city-local-life-client-test":"normal-r5-local-life-native-entry.json"}', '"old-city-local-life-client-test":"normal-r5-local-life-native-entry.json","scoped-personal-client-test":"normal-scoped-personal-native-entry.json"}'),('"old-city-local-life-client-test":"old_city_local_life_client_results.json"}', '"old-city-local-life-client-test":"old_city_local_life_client_results.json","scoped-personal-client-test":"scoped_personal_client_results.json"}')]:
 assert old in s,old;s=s.replace(old,new)
p.write_text(s,encoding='utf8');print('Actual original personal-node native flow wired; compile and execution pending.')

from pathlib import Path
r=Path(__file__).resolve().parents[2]
def edit(path,old,new):
 p=r/path;s=p.read_text(encoding='utf-8-sig');assert old in s,(path,old);p.write_text(s.replace(old,new,1),encoding='utf8')
for name in ['src/Mist.Core/OptionalLocalWorks/OldCityLifeModels.cs.pending','src/Mist.Application/OptionalLocalWorks/OldCityLocalLifeService.cs.pending','src/Mist.Application/OptionalLocalWorks/OldCityLocalLifeService.Property.cs.pending','tests/Mist.Tests/OptionalLocalWorks/OldCityLocalLifeCases.cs.pending','game/UI/Shell/Root.OldCityLocalLife.cs.pending','game/Diagnostics/Root.OldCityLocalLifeClientTests.cs.pending']:
 p=r/name;assert not p.with_suffix('').exists();p.rename(p.with_suffix(''))
edit('tests/Mist.Tests/Program.cs','return TestRegistry.Run(args);','Mist.Tests.OptionalLocalWorks.OldCityLocalLifeCases.Register();\nreturn TestRegistry.Run(args);')
edit('game/UI/Shell/Root.Views.cs','            if(await TrySettlementSupportInteraction(target.Id))return;','            if(await TryOldCityLifeInteraction(target.Id))return;\n            if(await TrySettlementSupportInteraction(target.Id))return;')
edit('game/UI/Shell/Root.Views.cs','CloseR3ShiftUiPreview(title);','CloseOldCityLifePreview(title);CloseR3ShiftUiPreview(title);')
edit('game/UI/Shell/Root.Views.cs','        Button(flow,"换班的人当前公开材料",','        Button(flow,"旧都已取得地方公开材料",()=>Execute(OpenOldCityLifeArchive));\n        Button(flow,"换班的人当前公开材料",')
edit('game/Root.cs','SaveAdventureDrafts()=>','SaveAdventureDrafts()=>await SaveOldCityLifeDrafts()&&')
edit('game/UI/Shell/Root.RegionalTravel.cs','ResetRegionalUiState(){','ResetRegionalUiState(){ResetOldCityLifeUi();')
edit('game/Root.cs','        if(OS.GetCmdlineUserArgs().Contains("--r3-shift-client-test"))','        if(OS.GetCmdlineUserArgs().Contains("--old-city-local-life-client-test"))await RunOldCityLocalLifeClientTests();\n        if(OS.GetCmdlineUserArgs().Contains("--r3-shift-client-test"))')
edit('game/Root.cs','or "--r3-shift-client-test" or','or "--old-city-local-life-client-test" or "--r3-shift-client-test" or')
edit('tools/Mist.ContentCli/Validation/ContentCompiler.cs','        string r3ShiftSource=', '''        string oldCityLifeSource=Path.Combine(root,"content/source/optional_local_works/old_city_life_contract.json");
        var oldCityLife=Mist.Core.OptionalLocalWorks.OldCityLifeContract.ParseJson(File.ReadAllText(oldCityLifeSource));
        using(var projection=JsonDocument.Parse(File.ReadAllText(oldCityLifeSource)))DenyAuthorAnswers(projection.RootElement);
        string oldCityLifeKey="old-city-life:"+oldCityLife.Version,oldCityLifeHash=CatalogImporter.HashFile(oldCityLifeSource);
        if(history.TryGetValue(oldCityLifeKey,out var priorOldCityLife)&&priorOldCityLife!=oldCityLifeHash)throw new ContentException("Same old-city life version changed.");
        string r3ShiftSource=''')
edit('tools/Mist.ContentCli/Validation/ContentCompiler.cs','            string relayTarget=', '''}
        {
            string target=Path.Combine(output,"old_city_life_contract.json");File.Copy(oldCityLifeSource,target,true);
            assets=assets.Add(new("old_city_life_contract.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));history.TryAdd(oldCityLifeKey,oldCityLifeHash);
        }
        {
            string relayTarget=''')
edit('game/UI/Campaign/CampaignClient.ReleaseProbe.cs','"RunR3ShiftWorkClientTests"','"RunR3ShiftWorkClientTests","RunOldCityLocalLifeClientTests"')
edit('game/UI/Campaign/CampaignClient.ReleaseProbe.cs','"settlement_support_contracts.json",','"settlement_support_contracts.json","old_city_life_contract.json",')
edit('tools/ci/package_windows.py','"r3_shift_work_client_results.json"]','"r3_shift_work_client_results.json","old_city_local_life_client_results.json"]')
p=r/'tools/ci/build.py';s=p.read_text(encoding='utf8');s=s.replace('"r3-shift-client-test"}', '"r3-shift-client-test","old-city-local-life-client-test"}')
for old,new in [('"r3-shift-client-test":"CP-R3-05"}', '"r3-shift-client-test":"CP-R3-05","old-city-local-life-client-test":"CP-R5-LIFE-05"}'),('"r3-shift-client-test":"Shift native entry has actual zero-XP"}', '"r3-shift-client-test":"Shift native entry has actual zero-XP","old-city-local-life-client-test":None}'),('"r3-shift-client-test":"normal-r3-shift-native-entry.json"}', '"r3-shift-client-test":"normal-r3-shift-native-entry.json","old-city-local-life-client-test":"normal-r5-local-life-native-entry.json"}'),('"r3-shift-client-test":"r3_shift_work_client_results.json"}', '"r3-shift-client-test":"r3_shift_work_client_results.json","old-city-local-life-client-test":"old_city_local_life_client_results.json"}')]:
 assert old in s,old;s=s.replace(old,new)
p.write_text(s,encoding='utf8')
edit('game/Diagnostics/Root.OldCityLocalLifeClientTests.cs','File.WriteAllText(Path.Combine(output,','System.IO.File.WriteAllText(Path.Combine(output,')
print('R5 local life source, UI, native driver and release source wired; runtime verification pending.')

from pathlib import Path
r=Path(__file__).resolve().parents[2]
def edit(path,old,new):
 p=r/path;s=p.read_text(encoding='utf-8-sig');assert old in s,(path,old);p.write_text(s.replace(old,new,1),encoding='utf8')
for name in ['src/Mist.Core/R3ShiftWork/R3ShiftWorkModels.cs.pending','src/Mist.Application/R3ShiftWork/R3ShiftWorkService.cs.pending','tests/Mist.Tests/R3ShiftWork/R3ShiftWorkCases.cs.pending','game/UI/Shell/Root.R3ShiftWork.cs.pending','game/Diagnostics/Root.R3ShiftWorkClientTests.cs.pending']:
 p=r/name;assert not p.with_suffix('').exists();p.rename(p.with_suffix(''))
edit('tests/Mist.Tests/Program.cs','return TestRegistry.Run(args);','Mist.Tests.R3ShiftWork.R3ShiftWorkCases.Register();\nreturn TestRegistry.Run(args);')
edit('game/UI/Shell/Root.ChapterThree.cs','var flow=Panel("沉钟矿区现场 · "+spot,InputOwner.Workspace);','var flow=Panel("沉钟矿区现场 · "+spot,InputOwner.Workspace);AddR3ShiftButton(flow,spot);')
edit('game/Root.cs','SaveAdventureDrafts()=>','SaveAdventureDrafts()=>await SaveR3ShiftDraft()&&')
edit('game/UI/Shell/Root.RegionalTravel.cs','ResetRegionalUiState(){','ResetRegionalUiState(){ResetR3ShiftUiState();')
edit('game/UI/Shell/Root.Views.cs','CloseLinaWorkPreview(title);','CloseR3ShiftUiPreview(title);CloseLinaWorkPreview(title);')
edit('game/UI/Shell/Root.Views.cs','        Button(flow,"莉娜当前获准个人材料",','        Button(flow,"换班的人当前公开材料",()=>Execute(async()=>{if(!await HasR3ShiftArchive())throw new InvalidOperationException("尚未实际取得本班原件。");await OpenR3ShiftArchive();}));\n        Button(flow,"莉娜当前获准个人材料",')
edit('game/Root.cs','        if(OS.GetCmdlineUserArgs().Contains("--lina-personal-client-test"))','        if(OS.GetCmdlineUserArgs().Contains("--r3-shift-client-test"))await RunR3ShiftWorkClientTests();\n        if(OS.GetCmdlineUserArgs().Contains("--lina-personal-client-test"))')
edit('game/Root.cs','or "--lina-personal-client-test" or','or "--r3-shift-client-test" or "--lina-personal-client-test" or')
edit('tools/Mist.ContentCli/Validation/ContentCompiler.cs','        string vesselLifeSource=', '''        string r3ShiftSource=Path.Combine(root,"content/source/r3_shift_work/learner_package.json");
        var r3Shift=Mist.Core.R3ShiftWork.R3ShiftWorkPackage.ParseJson(File.ReadAllText(r3ShiftSource));
        using(var projection=JsonDocument.Parse(File.ReadAllText(r3ShiftSource)))DenyAuthorAnswers(projection.RootElement);
        string r3ShiftKey="r3-shift:"+r3Shift.Version,r3ShiftHash=CatalogImporter.HashFile(r3ShiftSource);
        if(history.TryGetValue(r3ShiftKey,out var priorR3Shift)&&priorR3Shift!=r3ShiftHash)throw new ContentException("Same R3 shift version changed.");
        string vesselLifeSource=''')
edit('tools/Mist.ContentCli/Validation/ContentCompiler.cs','            string relayTarget=', '''}
        {
            string target=Path.Combine(output,"r3_shift_work/learner_package.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(r3ShiftSource,target,true);
            assets=assets.Add(new("r3_shift_work/learner_package.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));history.TryAdd(r3ShiftKey,r3ShiftHash);
        }
        {
            string relayTarget=''')
edit('game/UI/Campaign/CampaignClient.ReleaseProbe.cs','"lina_personal_player.json",','"lina_personal_player.json","r3_shift_work/learner_package.json",')
p=r/'tools/ci/build.py';s=p.read_text(encoding='utf8');s=s.replace('"lina-personal-client-test"}', '"lina-personal-client-test","r3-shift-client-test"}')
for old,new in [('"lina-personal-client-test":"CP-LINA-PERSONAL-05"}', '"lina-personal-client-test":"CP-LINA-PERSONAL-05","r3-shift-client-test":"CP-R3-05"}'),('"lina-personal-client-test":"actual_zero_XP_Lina_native_entry"}', '"lina-personal-client-test":"actual_zero_XP_Lina_native_entry","r3-shift-client-test":"actual_zero_XP_r3_shift_native_entry"}'),('"lina-personal-client-test":"normal-lina-personal-native-entry.json"}', '"lina-personal-client-test":"normal-lina-personal-native-entry.json","r3-shift-client-test":"normal-r3-shift-native-entry.json"}'),('"lina-personal-client-test":"lina_personal_client_results.json"}', '"lina-personal-client-test":"lina_personal_client_results.json","r3-shift-client-test":"r3_shift_work_client_results.json"}')]:
 assert old in s; s=s.replace(old,new)
p.write_text(s,encoding='utf8')
print('R3 additional living shift producer wired; execution pending.')

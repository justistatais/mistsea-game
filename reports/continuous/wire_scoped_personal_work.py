from pathlib import Path
r=Path(__file__).resolve().parents[2]
def edit(path,old,new):
 p=r/path;s=p.read_text(encoding='utf-8-sig');assert s.count(old)==1,(path,old,s.count(old));p.write_text(s.replace(old,new,1),encoding='utf8')
for name in ['src/Mist.Core/ScopedPersonalWorks/ScopedPersonalWorkModels.cs.pending','src/Mist.Application/ScopedPersonalWorks/ScopedPersonalWorksService.cs.pending','src/Mist.Application/ScopedPersonalWorks/ScopedPersonalWorksService.Mira.cs.pending','src/Mist.Application/ScopedPersonalWorks/ScopedPersonalWorksService.Oren.cs.pending','src/Mist.Application/ScopedPersonalWorks/ScopedPersonalWorksService.Readers.cs.pending','src/Mist.Application/ScopedPersonalWorks/PersonalProductionRules.cs.pending','tests/Mist.Tests/ScopedPersonalWorks/ScopedPersonalWorkCases.cs.pending','game/UI/Shell/Root.ScopedPersonalWorks.cs.pending']:
 p=r/name;assert not p.with_suffix('').exists();p.rename(p.with_suffix(''))
edit('tests/Mist.Tests/Program.cs','return TestRegistry.Run(args);','Mist.Tests.ScopedPersonalWorks.ScopedPersonalWorkCases.Register();\nreturn TestRegistry.Run(args);')
edit('game/UI/Shell/Root.ChapterThree.cs','AddR3ShiftButton(flow,spot);','AddR3ShiftButton(flow,spot);AddScopedPersonalDetailButton(flow,spot);')
edit('game/UI/Shell/Root.ChapterFour.cs','var flow=Panel("第四章现场 · "+spot,InputOwner.Workspace);','var flow=Panel("第四章现场 · "+spot,InputOwner.Workspace);AddScopedPersonalDetailButton(flow,spot);')
edit('game/UI/Shell/Root.ChapterFive.cs','var flow=Panel("第五章现场 · "+spot,InputOwner.Workspace);','var flow=Panel("第五章现场 · "+spot,InputOwner.Workspace);AddScopedPersonalDetailButton(flow,spot);')
edit('game/UI/Shell/Root.ChapterFour.cs','var flow=Panel("第四章工作台 · "+ChapterFourQuestName(quest),InputOwner.Workspace);','var flow=Panel("第四章工作台 · "+ChapterFourQuestName(quest),InputOwner.Workspace);AddScopedPersonalSourceControls(flow,quest);')
edit('game/UI/Shell/Root.ChapterFive.cs','var flow=Panel("第五章工作台 · "+ChapterFiveQuestName(quest),InputOwner.Workspace);','var flow=Panel("第五章工作台 · "+ChapterFiveQuestName(quest),InputOwner.Workspace);AddScopedPersonalSourceControls(flow,quest);')
edit('game/UI/Shell/Root.ChapterFive.cs','            case "arrange_oren_duty":OpenChapterFiveOrenDuty();return;','            case "arrange_oren_duty":OpenChapterFiveOrenDuty();return;\n            case "handoff_oren_duty":CheckCommit(await _chapterFive.HandoffOrenDutyAsync("R3-local-maintenance-keeper"));ClosePanel();return;')
edit('game/UI/Shell/Root.Views.cs','CloseOldCityLifePreview(title);','CloseScopedPersonalUiPreview(title);CloseOldCityLifePreview(title);')
edit('game/UI/Shell/Root.Views.cs','        Button(flow,"旧都已取得地方公开材料",','        Button(flow,"当前获准个人工作应答",()=>Execute(OpenScopedPersonalArchive));\n        Button(flow,"旧都已取得地方公开材料",')
edit('game/UI/Shell/Root.RegionalTravel.cs','ResetRegionalUiState(){','ResetRegionalUiState(){ResetScopedPersonalUiState();')
edit('game/UI/Shell/Root.ScopedPersonalWorks.cs','_scopedPersonal=null;}','_scopedPersonal=null;_regionalLearningScopes.Remove("scoped-personal-current");}')
edit('tools/Mist.ContentCli/Validation/ContentCompiler.cs','        string oldCityLifeSource=', '''        string scopedPersonalSource=Path.Combine(root,"content/source/scoped_personal_work/learner_package.json");
        var scopedPersonal=Mist.Core.ScopedPersonalWorks.ScopedPersonalPackage.ParseJson(File.ReadAllText(scopedPersonalSource));
        using(var projection=JsonDocument.Parse(File.ReadAllText(scopedPersonalSource)))DenyAuthorAnswers(projection.RootElement);
        string scopedPersonalKey="scoped-personal:"+scopedPersonal.Version,scopedPersonalHash=CatalogImporter.HashFile(scopedPersonalSource);
        if(history.TryGetValue(scopedPersonalKey,out var priorScopedPersonal)&&priorScopedPersonal!=scopedPersonalHash)throw new ContentException("Same scoped personal version changed.");
        string oldCityLifeSource=''')
edit('tools/Mist.ContentCli/Validation/ContentCompiler.cs','            string relayTarget=', '''}
        {
            string target=Path.Combine(output,"scoped_personal_work/learner_package.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(scopedPersonalSource,target,true);
            assets=assets.Add(new("scoped_personal_work/learner_package.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));history.TryAdd(scopedPersonalKey,scopedPersonalHash);
        }
        {
            string relayTarget=''')
edit('game/UI/Campaign/CampaignClient.ReleaseProbe.cs','"old_city_life_contract.json",','"old_city_life_contract.json","scoped_personal_work/learner_package.json",')
print('Existing original Mira/Oren production details, current readers, consumer guards and source wired; verification pending.')

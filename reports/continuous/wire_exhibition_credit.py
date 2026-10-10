from pathlib import Path
R=Path(__file__).resolve().parents[2]
def edit(path,old,new):
 p=R/path;s=p.read_text(encoding='utf-8-sig');assert old in s,(path,old);p.write_text(s.replace(old,new,1),encoding='utf8')
for p in ['src/Mist.Core/ChapterFour/ExhibitionCreditModels.cs.pending','src/Mist.Application/ChapterFour/ExhibitionCreditService.cs.pending','src/Mist.Application/ChapterFour/ExhibitionCreditService.Previews.cs.pending','tests/Mist.Tests/ChapterFour/ExhibitionCreditCases.cs.pending','game/UI/Shell/Root.ChapterFourCredit.cs.pending']:
 (R/p).rename(R/p.removesuffix('.pending'))
edit('tests/Mist.Tests/Program.cs','return TestRegistry.Run(args);','Mist.Tests.ChapterFour.ExhibitionCreditCases.Register();\nreturn TestRegistry.Run(args);')
edit('game/UI/Shell/Root.Views.cs','            if(await TryChapterFourInteraction(target.Id))return;','            if(await TryChapterFourCreditInteraction(target.Id))return;\n            if(await TryChapterFourInteraction(target.Id))return;')
edit('game/UI/Shell/Root.Views.cs','CloseR1LifeUiPreview(title);','CloseR1LifeUiPreview(title);CloseChapterFourCreditPreview(title);')
edit('game/UI/Shell/Root.RegionalTravel.cs','ResetR1LifeUiState();','ResetR1LifeUiState();ResetChapterFourCreditUi();')
edit('game/UI/Shell/Root.ChapterFour.cs','        if(_chapterFour is null)return;switch(action)','        if(_chapterFour is null)return;if(await TryChapterFourCreditAction(action))return;switch(action)')
edit('game/UI/Shell/Root.ChapterFour.cs','var flow=Panel("第四章已得档案",InputOwner.Workspace);','var flow=Panel("第四章已得档案",InputOwner.Workspace);AddChapterFourCreditArchiveButton(flow);')
edit('tools/Mist.ContentCli/Validation/ContentCompiler.cs','        string chapterFiveSource=','''        string creditSource=Path.Combine(root,"content/source/chapter_four/optional_credit.json");
        var credit=Mist.Core.ChapterFour.ExhibitionCreditPackage.ParseJson(File.ReadAllText(creditSource));
        using(var projection=JsonDocument.Parse(File.ReadAllText(creditSource)))DenyAuthorAnswers(projection.RootElement);
        foreach(var m in credit.Materials)
        {
            string key="r4-credit:"+m.Id+"@"+m.Version,hash=CatalogImporter.Hash(m.English+"\\n"+m.Chinese);
            if(history.TryGetValue(key,out var old)&&old!=hash)throw new ContentException("Same exhibition source changed: "+m.Id);
        }
        string chapterFiveSource=''')
edit('tools/Mist.ContentCli/Validation/ContentCompiler.cs','        if (formal) return new(','        blockers.Add("exhibition_credit_original_editorial_and_native_validation_pending");\n        if (formal) return new(')
edit('tools/Mist.ContentCli/Validation/ContentCompiler.cs','        if(chapterFive is not null)\n        {\n            string target=','''        {
            string target=Path.Combine(output,"chapter_four/optional_credit.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(creditSource,target,true);
            assets=assets.Add(new("chapter_four/optional_credit.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));
            foreach(var m in credit.Materials)history.TryAdd("r4-credit:"+m.Id+"@"+m.Version,CatalogImporter.Hash(m.English+"\\n"+m.Chinese));
        }
        if(chapterFive is not null)
        {
            string target=''')
edit('game/UI/Campaign/CampaignClient.ReleaseProbe.cs','"r1_life/learner_package.json",','"r1_life/learner_package.json","chapter_four/optional_credit.json",')
print('exhibition credit candidate wired; no build claim')

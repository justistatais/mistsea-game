from pathlib import Path
R=Path(__file__).resolve().parents[2]
def edit(path,old,new):
 p=R/path;s=p.read_text(encoding='utf-8-sig');assert old in s,(path,old);p.write_text(s.replace(old,new,1),encoding='utf8')
for p in ['game/UI/Shell/Root.R1Life.cs.pending','game/Diagnostics/Root.R1LifeClientTests.cs.pending']:
 (R/p).rename(R/p.removesuffix('.pending'))
edit('tests/Mist.Tests/Program.cs','return TestRegistry.Run(args);','Mist.Tests.R1Life.R1LifeCases.Register();\nreturn TestRegistry.Run(args);')
edit('game/Root.cs','        LoadRegionalChapters();','        LoadR1Life(_chapter.Geometry);\n        LoadRegionalChapters();\n        InitializeOptionalLocalWorks();')
edit('game/Root.cs','MergeRegionalGeometry(_chapter.Geometry.SetItems(_chapterTwo.Geometry))','MergeRegionalGeometry(_r1Life!.Geometry.SetItems(_chapterTwo.Geometry))')
edit('game/Root.cs','&&await SaveFinaleDraft();','&&await SaveFinaleDraft()&&await SaveR1LifeDraft()&&await SaveOptionalLocalWorksDraft();')
edit('game/Root.cs','        _regionalGeometry=_voyage?.Geometry;','''        if(_r1Life is not null&&_view.Actor.Scene.StartsWith("R1-",StringComparison.Ordinal)&&!_view.Snapshot.Entities.Any(e=>e.Id==Mist.Application.R1Life.R1LifeService.StateId)&&_view.Snapshot.Receipts.Any(r=>r.SemanticKey=="Q-101:resolution"))
        {
            CheckCommit(await _r1Life.InitializeRegionAsync());_view=await _q.ViewAsync();
        }
        _regionalGeometry=_voyage?.Geometry;
        if(_r1Life is not null)_regionalGeometry=(_regionalGeometry??_vessel!.Geometry).SetItems(_r1Life.Geometry);''')
edit('game/Root.cs','else if(_chapter is null)await _q.TraverseAsync','else if(_r1Life is not null&&_view.Actor.Scene is "R1-O01" or "R1-O02" or "R1-S02")await _r1Life.TraverseAsync(_trace.ToImmutableArray());else if(_chapter is null)await _q.TraverseAsync')
edit('game/Root.cs','        if(OS.GetCmdlineUserArgs().Contains("--vessel-client-test"))','        if(OS.GetCmdlineUserArgs().Contains("--r1-life-client-test"))await RunR1LifeClientTests();\n        if(OS.GetCmdlineUserArgs().Contains("--vessel-client-test"))')
edit('game/Root.cs','or "--release-probe"','or "--r1-life-client-test" or "--release-probe"')
edit('game/UI/Shell/Root.RegionalTravel.cs','        _finale?.Package.Stations.FirstOrDefault','        _r1Life?.Package.Stations.FirstOrDefault(s=>s.Spot==spot.Id)?.Label??\n        _finale?.Package.Stations.FirstOrDefault')
edit('game/UI/Shell/Root.RegionalTravel.cs','        _view.Actor.Scene.StartsWith("RF-"','        _r1Life is not null&&_r1Life.Package.PortGates.ContainsKey(port.Id)?_r1Life.PortIssue(_view.Snapshot,port):\n        _view.Actor.Scene.StartsWith("RF-"')
edit('game/UI/Shell/Root.RegionalTravel.cs','private bool RegionalMaterialId(string id)=>','private bool RegionalMaterialId(string id)=>(_r1Life?.Package.Materials.Any(m=>m.Id==id)??false)||')
edit('game/UI/Shell/Root.RegionalTravel.cs','private void ResetRegionalUiState(){','private void ResetRegionalUiState(){ResetR1LifeUiState();ResetOptionalLocalWorksUiState();')
edit('game/UI/Shell/Root.Views.cs','        if(title is not null)CloseChapterUiPreview(title);','        if(title is not null){CloseR1LifeUiPreview(title);CloseOptionalLocalWorksUiPreview(title);}\n        if(title is not null)CloseChapterUiPreview(title);')
edit('game/UI/Shell/Root.Views.cs','        if(_chapter is not null)Button(flow,"序章与首章原始记录"','        if(_r1Life is not null&&_view.Snapshot.Entities.Any(e=>e.Id==Mist.Application.R1Life.R1LifeService.StateId))Button(flow,"盐雾港生活委托与档案",()=>Execute(OpenR1LifeQuests));\n        Button(flow,"查看音乐盒地方工作档案",()=>Execute(async()=>{if(!await HasOptionalLocalWorksArchive())throw new InvalidOperationException("尚未取得当前可读的地方音乐盒档案。");await OpenOptionalLocalWorksArchive();}));\n        if(_chapter is not null)Button(flow,"序章与首章原始记录"')
edit('game/UI/Shell/Root.Views.cs','                if(_vessel is not null&&_view.Actor.Scene.StartsWith("HUB-"','                if(_r1Life is not null&&_r1Life.Package.PortGates.ContainsKey(port.Id)){CheckCommit(await _r1Life.EnterPortAsync(target.Id));await _store.CreateCheckpointAsync("automatic");return;}\n                if(_vessel is not null&&_view.Actor.Scene.StartsWith("HUB-"')
edit('game/UI/Shell/Root.Views.cs','            if(await TryChapterThreeInteraction(target.Id))return;','            if(await TryOptionalLocalWorksInteraction(target.Id))return;\n            if(await TryR1LifeInteraction(target.Id))return;\n            if(await TryChapterThreeInteraction(target.Id))return;')
edit('game/UI/Shell/Root.RegionalLearning.cs','        if(_chapterTwo is not null&&snapshot.Entities','        if(_r1Life is not null&&snapshot.Entities.Any(e=>e.Id==Mist.Application.R1Life.R1LifeService.StateId))foreach(var m in (await _r1Life.ViewAsync()).Materials)sources[m.Id]=RegionalLearningAccess(m);\n        if(_chapterTwo is not null&&snapshot.Entities')
edit('game/UI/Campaign/CampaignClient.ReleaseProbe.cs','"campaign/first_entry.json","chapter_one/chapter_one.json"','"lexicon/learner_lexicon.json","r1_life/learner_package.json","campaign/first_entry.json","chapter_one/chapter_one.json"')
edit('tools/Mist.ContentCli/Validation/ContentCompiler.cs','        string vesselSource=','''        string r1LifeSource=Path.Combine(root,"content/source/r1_life/learner_package.json");
        var r1Life=Mist.Core.R1Life.R1LifePackage.Parse(File.ReadAllText(r1LifeSource));
        using(var projection=JsonDocument.Parse(File.ReadAllText(r1LifeSource)))DenyAuthorAnswers(projection.RootElement);
        if(r1Life.Scenes.Any(s=>!catalog.Scenes.Any(c=>c.Id==s.Id)))throw new ContentException("R1 optional space is not registered.");
        foreach(var m in r1Life.Materials)
        {
            string key="r1-life:"+m.Id+"@"+m.Version,hash=CatalogImporter.Hash(m.English+"\\n"+m.Chinese);
            if(history.TryGetValue(key,out var old)&&old!=hash)throw new ContentException("Same local material ID/version changed: "+m.Id);
        }
        string vesselSource=''')
edit('tools/Mist.ContentCli/Validation/ContentCompiler.cs','        if (formal) return new(','        blockers.Add("r1_life_original_editorial_language_calibration_and_two_audio_tracks_pending");\n        if (formal) return new(')
edit('tools/Mist.ContentCli/Validation/ContentCompiler.cs','        if(vessel is not null)\n        {\n            string vesselTarget=','''        {
            string target=Path.Combine(output,"r1_life/learner_package.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(r1LifeSource,target,true);
            assets=assets.Add(new("r1_life/learner_package.json",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));
            foreach(var m in r1Life.Materials)history.TryAdd("r1-life:"+m.Id+"@"+m.Version,CatalogImporter.Hash(m.English+"\\n"+m.Chinese));
        }
        if(vessel is not null)
        {
            string vesselTarget=''')
p=R/'tools/ci/build.py';s=p.read_text(encoding='utf8')
s=s.replace('"chapter-six-client-test","finale-client-test"','"chapter-six-client-test","finale-client-test","r1-life-client-test"')
s=s.replace('"finale-client-test":"CP-RF-05"','"finale-client-test":"CP-RF-05","r1-life-client-test":"CP-R1-03"')
s=s.replace('"finale-client-test":"normal-rf-native-entry.json"','"finale-client-test":"normal-rf-native-entry.json","r1-life-client-test":"entry_evidence.json"')
s=s.replace('"finale-client-test":"finale_client_results.json"','"finale-client-test":"finale_client_results.json","r1-life-client-test":"r1_life_client_results.json"')
p.write_text(s,encoding='utf8')
print('R1 life and musicbox shared hooks wired; build not claimed')

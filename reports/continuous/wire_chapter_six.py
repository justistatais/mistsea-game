from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
def edit(path, before, after):
    p=ROOT/path
    text=p.read_text(encoding='utf-8-sig')
    assert before in text, (path,before)
    p.write_text(text.replace(before,after),encoding='utf-8')
for path in ['game/UI/Shell/Root.ChapterSix.cs','game/Diagnostics/Root.ChapterSixClientTests.cs','game/UI/Shell/Root.RegionalLearning.cs']:
    p=ROOT/path
    assert not p.exists(), path
    p.with_suffix(p.suffix+'.pending').rename(p)
edit('game/Root.cs','&&await SaveFinaleDraft();','&&await SaveChapterSixDraft()&&await SaveFinaleDraft();')
edit('game/Root.cs','or "--finale-client-test"','or "--chapter-six-client-test" or "--finale-client-test"')
edit('game/Root.cs','        if(OS.GetCmdlineUserArgs().Contains("--finale-client-test"))','        if(OS.GetCmdlineUserArgs().Contains("--chapter-six-client-test"))await RunChapterSixClientTests();\n        if(OS.GetCmdlineUserArgs().Contains("--finale-client-test"))')
edit('game/Root.cs','        var activeFinale=','        var activeSix=_view.Snapshot.Entities.Where(e=>e.Kind=="combat"&&e.Id.StartsWith("combat:ENC-R6-",StringComparison.Ordinal)).Select(e=>StrictJson.Deserialize<Mist.Core.ChapterSix.FrontierEncounterState>(e.Json)).FirstOrDefault(e=>e.Battle.Outcome is Mist.Core.Combat.CombatOutcome.Active or Mist.Core.Combat.CombatOutcome.Defeat);\n        if(activeSix is not null)_chapterSixSelectedEncounter=activeSix.Encounter;else if(!_view.Actor.Scene.StartsWith("R6-",StringComparison.Ordinal))_chapterSixSelectedEncounter=null;\n        var activeFinale=')
edit('game/UI/Shell/Root.RegionalTravel.cs','ResetChapterFiveUiState();ResetFinaleUiState();','ResetChapterFiveUiState();ResetChapterSixUiState();ResetFinaleUiState();')
edit('game/UI/Shell/Root.Views.cs','        if(title is not null)CloseFinaleUiPreview(title);','        if(title is not null)CloseChapterSixUiPreview(title);\n        if(title is not null)CloseFinaleUiPreview(title);')
edit('game/UI/Shell/Root.Views.cs','        if(region=="RF"','        if(region=="R6"&&_chapterSix is not null&&_view.Snapshot.Entities.Any(e=>e.Id=="chapter6")){await OpenChapterSixQuests();return;}\n        if(region=="RF"')
edit('game/UI/Shell/Root.Views.cs','        if(_view.Snapshot.Entities.Any(e=>e.Id=="finale"))Button(flow,"终章工作与当前状态"','        if(_view.Snapshot.Entities.Any(e=>e.Id=="chapter6"))Button(flow,"第六章工作与当前状态",()=>Execute(OpenChapterSixQuests));\n        if(_view.Snapshot.Entities.Any(e=>e.Id=="finale"))Button(flow,"终章工作与当前状态"')
edit('game/UI/Shell/Root.Views.cs','        if(_view.Snapshot.Entities.Any(e=>e.Id=="finale"))Button(flow,"终章已得档案"','        if(_view.Snapshot.Entities.Any(e=>e.Id=="chapter6"))Button(flow,"第六章已得档案",()=>Execute(OpenChapterSixArchive));\n        if(_view.Snapshot.Entities.Any(e=>e.Id=="finale"))Button(flow,"终章已得档案"')
edit('game/UI/Shell/Root.Views.cs','本档案保留实际序章、港口委托和首章回执；尚未制作的后续章节不计作已完成。','任务日志显示本档案的实际委托和回执；各章只按现场取得、实际操作和正式交接推进。')
edit('game/UI/Shell/Root.Views.cs','            if(await TryFinaleInteraction(target.Id))return;','            if(await TryChapterSixFacilityInteraction(target.Id))return;\n            if(await TryChapterSixInteraction(target.Id))return;\n            if(await TryFinaleInteraction(target.Id))return;')
edit('game/UI/Shell/Root.Vessel.cs','or "R5-S01.berth")','or "R5-S01.berth" or "R6-S01.berth")')
edit('game/UI/Shell/Root.Vessel.cs','                AddRegionalChartActions(flow,view.Ship.DockRegion);','                AddRegionalChartActions(flow,view.Ship.DockRegion);\n                if(view.Ship.DockRegion=="R6"&&_view.Snapshot.Entities.Any(e=>e.Id=="chapter6"))Button(flow,"核对最后起航的实际准备",()=>Execute(OpenChapterSixLaunchPreview));')
edit('game/UI/Shell/Root.Vessel.cs','e.Id=="chapter5");','e.Id=="chapter5")||berth.Region=="R6"&&_view.Snapshot.Entities.Any(e=>e.Id=="chapter6");')
edit('game/UI/Shell/Root.ChapterSix.cs','CheckCommit(await _finaleLaunch.ConfirmAsync(p.Token));CloseAllPanels();','CheckCommit(await _finaleLaunch.ConfirmAsync(p.Token));CheckCommit(await _finale!.InitializeRegionAsync());CloseAllPanels();')
edit('game/UI/Shell/Root.EncounterRoute.cs','        if(FinaleEncounterSelected){var current=','        if(ChapterSixEncounterSelected){var current=await _chapterSix!.EncounterAsync(_chapterSixSelectedEncounter!);return current is null?null:new(current.Battle,current.Entry,current.ReturnActor,current.ThreatControlled);}\n        if(FinaleEncounterSelected){var current=')
edit('game/UI/Shell/Root.EncounterRoute.cs','=>FinaleEncounterSelected?_finale!.PreviewEncounterActionAsync','=>ChapterSixEncounterSelected?_chapterSix!.PreviewEncounterActionAsync(_chapterSixSelectedEncounter!,r):FinaleEncounterSelected?_finale!.PreviewEncounterActionAsync')
edit('game/UI/Shell/Root.EncounterRoute.cs','=>FinaleEncounterSelected?_finale!.ConfirmPreviewAsync','=>ChapterSixEncounterSelected?_chapterSix!.ConfirmPreviewAsync(token):FinaleEncounterSelected?_finale!.ConfirmPreviewAsync')
edit('game/UI/Shell/Root.EncounterRoute.cs','        if(FinaleEncounterSelected){var result=','        if(ChapterSixEncounterSelected){var result=await _chapterSix!.WithdrawEncounterAsync(_chapterSixSelectedEncounter!);CheckCommit(result);return await _chapterSix.BeginEncounterAsync(_chapterSixSelectedEncounter!);}\n        if(FinaleEncounterSelected){var result=')
edit('game/UI/Shell/Root.EncounterRoute.cs','=>FinaleEncounterSelected?_finale!.WithdrawEncounterAsync','=>ChapterSixEncounterSelected?_chapterSix!.WithdrawEncounterAsync(_chapterSixSelectedEncounter!):FinaleEncounterSelected?_finale!.WithdrawEncounterAsync')
edit('game/UI/Shell/Root.EncounterRoute.cs','{_finale?.ClosePreview(token);','{_chapterSix?.ClosePreview(token);_finale?.ClosePreview(token);')
edit('game/UI/Shell/Root.Combat.cs','Panel(FinaleEncounterSelected?','Panel(ChapterSixEncounterSelected?"边境现场处置 · "+_chapterSixSelectedEncounter:FinaleEncounterSelected?')
edit('game/UI/Shell/Root.Combat.cs','else if(ChapterFiveEncounterSelected)Execute','else if(ChapterSixEncounterSelected)Execute(async()=>await OpenChapterSixWriter(_chapterSixSelectedEncounter=="ENC-R6-01"?"Q-601":"Q-603"));else if(ChapterFourEncounterSelected)Execute(async()=>await OpenChapterFourWriter("Q-403"));else if(ChapterThreeEncounterSelected)Execute(async()=>await OpenChapterThreeWriter("Q-301"));else if(ChapterFiveEncounterSelected)Execute')
edit('game/UI/Shell/Root.Combat.cs','||ChapterFiveEncounterSelected?','||ChapterFiveEncounterSelected||ChapterSixEncounterSelected?')
edit('tests/Mist.Tests/Program.cs','return TestRegistry.Run(args);','Mist.Tests.Learning.RegionalLearningCases.Register();\nreturn TestRegistry.Run(args);')
edit('tools/ci/build.py','"finale-client-test"','"chapter-six-client-test","finale-client-test"')
# In mappings the common insertion above is a bare string; turn it into the named entry.
edit('tools/ci/build.py','"chapter-six-client-test","finale-client-test":"CP-RF-05"','"chapter-six-client-test":"CP-R6-05","finale-client-test":"CP-RF-05"')
edit('tools/ci/build.py','"chapter-six-client-test","finale-client-test":"normal-rf-native-entry.json"','"chapter-six-client-test":"normal-r6-native-entry.json","finale-client-test":"normal-rf-native-entry.json"')
edit('tools/ci/build.py','"chapter-six-client-test","finale-client-test":"finale_client_results.json"','"chapter-six-client-test":"chapter_six_client_results.json","finale-client-test":"finale_client_results.json"')
print('R6 UI, native driver, and learning UI included; shared hooks wired.')

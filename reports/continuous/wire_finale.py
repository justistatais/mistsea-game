from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
def change(file,old,new,count=1):
    p=ROOT/file;s=p.read_text(encoding='utf8')
    if s.count(new)==count:return
    assert s.count(old)==count,(file,old,s.count(old));p.write_text(s.replace(old,new),encoding='utf8')
change('game/UI/Shell/Root.RegionalTravel.cs','    private readonly Dictionary<string,RegionalVoyageService>', '''    private Mist.Application.ChapterSix.ChapterSixService? _chapterSix;
    private Mist.Application.Finale.FinaleService? _finale;
    private Mist.Application.ChapterSix.FinaleLaunchService? _finaleLaunch;
    private readonly Dictionary<string,RegionalVoyageService>''')
change('game/UI/Shell/Root.RegionalTravel.cs','    private string ReadableSpotName(Spot spot)=>','''    private string ReadableSpotName(Spot spot)=>
        _finale?.Package.Stations.FirstOrDefault(s=>s.Spot==spot.Id)?.Label??
        _chapterSix?.Package.Stations.FirstOrDefault(s=>s.Spot==spot.Id)?.Label??''')
change('game/UI/Shell/Root.RegionalTravel.cs','    private string? CurrentPortIssue(Port port)=>','''    private string? CurrentPortIssue(Port port)=>
        _view.Actor.Scene.StartsWith("RF-",StringComparison.Ordinal)&&_finale is not null?_finale.PortIssue(_view.Snapshot,port):
        _view.Actor.Scene.StartsWith("R6-",StringComparison.Ordinal)&&_chapterSix is not null?_chapterSix.PortIssue(_view.Snapshot,port):''')
change('game/UI/Shell/Root.RegionalTravel.cs','private bool RegionalMaterialId(string id)=>','private bool RegionalMaterialId(string id)=>(_finale?.Package.Materials.Any(m=>m.Id==id)??false)||(_chapterSix?.Package.Materials.Any(m=>m.Id==id)??false)||')
change('game/UI/Shell/Root.RegionalTravel.cs','ResetChapterFiveUiState();}','ResetChapterFiveUiState();ResetFinaleUiState();}')
change('game/UI/Shell/Root.RegionalTravel.cs','        using var five=Godot.FileAccess.Open','''        using var six=Godot.FileAccess.Open("res://content/chapter_six/learner_package.json",Godot.FileAccess.ModeFlags.Read);
        using var finale=Godot.FileAccess.Open("res://content/finale/learner_package.json",Godot.FileAccess.ModeFlags.Read);
        using var five=Godot.FileAccess.Open''')
change('game/UI/Shell/Root.RegionalTravel.cs','if(three is null||four is null||five is null)','if(three is null||four is null||five is null||six is null||finale is null)')
change('game/UI/Shell/Root.RegionalTravel.cs','        _chapterFive=new(_store,_catalog,Mist.Core.ChapterFive.ChapterFivePackage.ParseJson(five.GetAsText()));','''        _chapterFive=new(_store,_catalog,Mist.Core.ChapterFive.ChapterFivePackage.ParseJson(five.GetAsText()));
        var worksites=_chapter!.Geometry.SetItems(_chapterTwo!.Geometry).SetItems(_chapterThree.Geometry).SetItems(_chapterFour.Geometry).SetItems(_chapterFive.Geometry);
        _chapterSix=new(_store,_catalog,Mist.Core.ChapterSix.ChapterSixPackage.ParseJson(six.GetAsText()),worksites,_store);
        _finale=new(_store,_catalog,Mist.Core.Finale.FinalePackage.ParseJson(finale.GetAsText()),_store);''')
change('game/UI/Shell/Root.RegionalTravel.cs','=>geometry.SetItems(_chapterThree!.Geometry).SetItems(_chapterFour!.Geometry).SetItems(_chapterFive!.Geometry);','=>geometry.SetItems(_chapterThree!.Geometry).SetItems(_chapterFour!.Geometry).SetItems(_chapterFive!.Geometry).SetItems(_chapterSix!.Geometry).SetItems(_finale!.Geometry);')
change('game/UI/Shell/Root.RegionalTravel.cs','new[]{"r2_r3","r3_r4","r4_r5"}','new[]{"r2_r3","r3_r4","r4_r5","r5_r6"}')
change('game/UI/Shell/Root.RegionalTravel.cs','else if(d.To=="R5")await _chapterFive!.InitializeRegionAsync();','else if(d.To=="R5")await _chapterFive!.InitializeRegionAsync();\n            else if(d.To=="R6")await _chapterSix!.InitializeRegionAsync();')
change('game/Root.cs','        LoadRegionalVoyages();','        _finaleLaunch=new(_store,_catalog,_vessel.Package,_finale!.Package,_store);\n        LoadRegionalVoyages();')
change('game/Root.cs','&&await SaveChapterFiveDraft();','&&await SaveChapterFiveDraft()&&await SaveFinaleDraft();')
change('game/Root.cs','        var activeRegional=', '''        if(_chapterSix is not null&&_view.Snapshot.Entities.Any(e=>e.Id=="chapter6"))_regionalGeometry=(_regionalGeometry??_vessel!.Geometry).SetItems(_chapterSix.CurrentGeometry(_view.Snapshot));
        if(_finale is not null&&_view.Snapshot.Entities.Any(e=>e.Id=="finale"))_regionalGeometry=(_regionalGeometry??_vessel!.Geometry).SetItems(_finale.CurrentGeometry(_view.Snapshot));
        var activeFinale=_view.Snapshot.Entities.Where(e=>e.Kind=="combat"&&e.Id.StartsWith("combat:ENC-RF-",StringComparison.Ordinal)).Select(e=>StrictJson.Deserialize<Mist.Core.Finale.FinaleEncounterState>(e.Json)).FirstOrDefault(e=>e.Battle.Outcome is Mist.Core.Combat.CombatOutcome.Active or Mist.Core.Combat.CombatOutcome.Defeat);
        if(activeFinale is not null)_finaleSelectedEncounter=activeFinale.Encounter;else if(!_view.Actor.Scene.StartsWith("RF-",StringComparison.Ordinal))_finaleSelectedEncounter=null;
        var activeRegional=''')
change('game/Root.cs','else if(_chapterFive is not null&&_view.Actor.Scene.StartsWith("R5-",StringComparison.Ordinal))await _chapterFive.TraverseAsync', 'else if(_finale is not null&&_view.Actor.Scene.StartsWith("RF-",StringComparison.Ordinal))await _finale.TraverseAsync(_trace.ToImmutableArray());else if(_chapterSix is not null&&_view.Actor.Scene.StartsWith("R6-",StringComparison.Ordinal))await _chapterSix.TraverseAsync(_trace.ToImmutableArray());else if(_chapterFive is not null&&_view.Actor.Scene.StartsWith("R5-",StringComparison.Ordinal))await _chapterFive.TraverseAsync')
change('game/UI/Shell/Root.Views.cs','        if(title is not null)CloseChapterFiveUiPreview(title);','        if(title is not null)CloseChapterFiveUiPreview(title);\n        if(title is not null)CloseFinaleUiPreview(title);')
change('game/UI/Shell/Root.Views.cs','        if(region=="R3"','        if(region=="RF"&&_finale is not null&&_view.Snapshot.Entities.Any(e=>e.Id=="finale")){await OpenFinaleQuests();return;}\n        if(region=="R3"')
change('game/UI/Shell/Root.Views.cs','            if(await TryVesselInteraction(target.Id))return;','''            if(await TryFinaleInteraction(target.Id))return;
            if(await TryVesselInteraction(target.Id))return;''')
change('game/UI/Shell/Root.Views.cs','                if(_chapterFive is not null&&_view.Actor.Scene.StartsWith("R5-",StringComparison.Ordinal))','''                if(_finale is not null&&_view.Actor.Scene.StartsWith("RF-",StringComparison.Ordinal)){CheckCommit(await _finale.EnterPortAsync(target.Id));await _store.CreateCheckpointAsync("automatic");return;}
                if(_chapterSix is not null&&_view.Actor.Scene.StartsWith("R6-",StringComparison.Ordinal)){CheckCommit(await _chapterSix.EnterPortAsync(target.Id));await _store.CreateCheckpointAsync("automatic");return;}
                if(_chapterFive is not null&&_view.Actor.Scene.StartsWith("R5-",StringComparison.Ordinal))''')
change('game/UI/Shell/Root.Views.cs','        if(_view.Snapshot.Entities.Any(e=>e.Id=="chapter5"))Button(flow,"第五章工作与当前状态",()=>Execute(OpenChapterFiveQuests));','        if(_view.Snapshot.Entities.Any(e=>e.Id=="chapter5"))Button(flow,"第五章工作与当前状态",()=>Execute(OpenChapterFiveQuests));\n        if(_view.Snapshot.Entities.Any(e=>e.Id=="finale"))Button(flow,"终章工作与当前状态",()=>Execute(OpenFinaleQuests));')
change('game/UI/Shell/Root.Views.cs','        if(_view.Snapshot.Entities.Any(e=>e.Id=="chapter5"))Button(flow,"第五章已得档案",()=>Execute(OpenChapterFiveArchive));','        if(_view.Snapshot.Entities.Any(e=>e.Id=="chapter5"))Button(flow,"第五章已得档案",()=>Execute(OpenChapterFiveArchive));\n        if(_view.Snapshot.Entities.Any(e=>e.Id=="finale"))Button(flow,"终章已得档案",()=>Execute(OpenFinaleArchive));')
change('game/UI/Shell/Root.EncounterRoute.cs','        if(ChapterFiveEncounterSelected){var current=','        if(FinaleEncounterSelected){var current=await _finale!.EncounterAsync(_finaleSelectedEncounter!);return current is null?null:new(current.Battle,current.Entry,current.ReturnActor,current.ThreatControlled);}\n        if(ChapterFiveEncounterSelected){var current=')
for method,route in [('PreviewEncounter(BattleRequest r)','_finale!.PreviewEncounterActionAsync(_finaleSelectedEncounter!,r)'),('ConfirmEncounter(Guid token)','_finale!.ConfirmPreviewAsync(token)'),('WithdrawEncounter()','_finale!.WithdrawEncounterAsync(_finaleSelectedEncounter!)')]:
    change('game/UI/Shell/Root.EncounterRoute.cs',method+'=>ChapterFiveEncounterSelected?',method+'=>FinaleEncounterSelected?'+route+':ChapterFiveEncounterSelected?')
change('game/UI/Shell/Root.EncounterRoute.cs','        if(ChapterFiveEncounterSelected){var result=','        if(FinaleEncounterSelected){var result=await _finale!.WithdrawEncounterAsync(_finaleSelectedEncounter!);CheckCommit(result);return await _finale.BeginEncounterAsync(_finaleSelectedEncounter!);}\n        if(ChapterFiveEncounterSelected){var result=')
change('game/UI/Shell/Root.EncounterRoute.cs','CloseEncounterPreview(Guid token){','CloseEncounterPreview(Guid token){_finale?.ClosePreview(token);')
for file in ['game/Root.cs','game/UI/Campaign/CampaignClient.cs']:
    change(file,'or "--chapter-five-client-test" or "--release-probe"','or "--chapter-five-client-test" or "--finale-client-test" or "--release-probe"')
change('game/Root.cs','        if(OS.GetCmdlineUserArgs().Contains("--chapter-five-client-test"))await RunChapterFiveClientTests();','        if(OS.GetCmdlineUserArgs().Contains("--chapter-five-client-test"))await RunChapterFiveClientTests();\n        if(OS.GetCmdlineUserArgs().Contains("--finale-client-test"))await RunFinaleClientTests();')
p=ROOT/'game/Diagnostics/Root.FinaleClientTests.cs.pending';p.rename(p.with_suffix(''))
change('tools/ci/build.py','"chapter-five-client-test"}', '"chapter-five-client-test","finale-client-test"}',3)
change('tools/ci/build.py','"chapter-five-client-test":"CP-R5-05"}', '"chapter-five-client-test":"CP-R5-05","finale-client-test":"CP-RF-05"}')
change('tools/ci/build.py','"chapter-five-client-test":"normal-r5-native-entry.json"}', '"chapter-five-client-test":"normal-r5-native-entry.json","finale-client-test":"normal-rf-native-entry.json"}')
change('tools/ci/build.py','"chapter-five-client-test":"chapter_five_client_results.json"}', '"chapter-five-client-test":"chapter_five_client_results.json","finale-client-test":"finale_client_results.json"}')
print('Wired actual R6 packages/voyage and RF scene, storage, writing, encounters and native test dispatch.')

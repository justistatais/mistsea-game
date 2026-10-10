"""One-time bounded additions to shared chapter routing and native test entry."""
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
def change(file,old,new,count=1):
    p=ROOT/file;s=p.read_text(encoding="utf8")
    if s.count(new)==count:return
    assert s.count(old)==count,(file,old,s.count(old));p.write_text(s.replace(old,new),encoding="utf8")
change("game/Root.cs",'else if(_chapter is null)await _q.TraverseAsync', 'else if(_chapterFive is not null&&_view.Actor.Scene.StartsWith("R5-",StringComparison.Ordinal))await _chapterFive.TraverseAsync(_trace.ToImmutableArray());else if(_chapter is null)await _q.TraverseAsync')
for file in ["game/Root.cs","game/UI/Campaign/CampaignClient.cs"]:
    change(file,'or "--chapter-four-client-test" or "--release-probe"','or "--chapter-four-client-test" or "--chapter-five-client-test" or "--release-probe"')
for method,route in [("PreviewEncounter(BattleRequest r)",'_chapterFive!.PreviewEncounterActionAsync(_chapterFiveSelectedEncounter!,r)'),("ConfirmEncounter(Guid token)",'_chapterFive!.ConfirmPreviewAsync(token)'),("WithdrawEncounter()",'_chapterFive!.WithdrawEncounterAsync(_chapterFiveSelectedEncounter!)')]:
    change("game/UI/Shell/Root.EncounterRoute.cs",method+'=>ChapterFourEncounterSelected?',method+'=>ChapterFiveEncounterSelected?'+route+':ChapterFourEncounterSelected?')
change("tools/ci/build.py",'"chapter-four-client-test"}', '"chapter-four-client-test","chapter-five-client-test"}',3)
change("tools/ci/build.py",'"chapter-four-client-test":"CP-R4-05"}', '"chapter-four-client-test":"CP-R4-05","chapter-five-client-test":"CP-R5-05"}')
change("tools/ci/build.py",'"chapter-four-client-test":"normal-r4-native-entry.json"}', '"chapter-four-client-test":"normal-r4-native-entry.json","chapter-five-client-test":"normal-r5-native-entry.json"}')
change("tools/ci/build.py",'"chapter-four-client-test":"chapter_four_client_results.json"}', '"chapter-four-client-test":"chapter_four_client_results.json","chapter-five-client-test":"chapter_five_client_results.json"}')
print("Added exact R5 movement, encounter and native-client routing; no world-state migration.")

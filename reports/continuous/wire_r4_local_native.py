from pathlib import Path
R=Path(__file__).resolve().parents[2]
p=R/'game/Diagnostics/Root.ChapterFourLocalClientTests.cs.pending';p.rename(p.with_suffix(''))
def edit(path,old,new):
 p=R/path;s=p.read_text(encoding='utf-8-sig');assert old in s,(path,old);p.write_text(s.replace(old,new,1),encoding='utf8')
edit('game/Root.cs','        if(OS.GetCmdlineUserArgs().Contains("--chapter-four-client-test"))','        if(OS.GetCmdlineUserArgs().Contains("--chapter-four-local-client-test"))await RunChapterFourLocalClientTests();\n        if(OS.GetCmdlineUserArgs().Contains("--chapter-four-client-test"))')
edit('game/Root.cs','or "--chapter-four-client-test"','or "--chapter-four-local-client-test" or "--chapter-four-client-test"')
p=R/'tools/ci/build.py';s=p.read_text(encoding='utf8');s=s.replace('"r2-life-client-test"','"r2-life-client-test","chapter-four-local-client-test"')
# The global list replacement must not split dictionary key/value entries.
s=s.replace('"r2-life-client-test","chapter-four-local-client-test":','"r2-life-client-test":')
s=s.replace('"r2-life-client-test":"CP-R2-03"','"r2-life-client-test":"CP-R2-03","chapter-four-local-client-test":"CP-R4-05"');s=s.replace('"r2-life-client-test":"entry_evidence.json"','"r2-life-client-test":"entry_evidence.json","chapter-four-local-client-test":"normal-r4-native-entry.json"');s=s.replace('"r2-life-client-test":"r2_life_client_results.json"','"r2-life-client-test":"r2_life_client_results.json","chapter-four-local-client-test":"chapter_four_local_client_results.json"');p.write_text(s,encoding='utf8')
edit('tools/ci/package_windows.py','"chapter_four_optional_client_results.json"','"chapter_four_local_client_results.json"')
print('R4 local real UI probe wired; still unexecuted')

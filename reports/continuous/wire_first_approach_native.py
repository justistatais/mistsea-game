from pathlib import Path
R=Path(__file__).resolve().parents[2]
def edit(path,old,new):
 p=R/path;s=p.read_text(encoding='utf-8-sig');assert old in s,(path,old);p.write_text(s.replace(old,new,1),encoding='utf8')
for name in ['game/Diagnostics/CampaignClient.FirstApproachTests.cs.pending','game/Diagnostics/Root.FirstApproachClientTests.cs.pending']:
 p=R/name;p.rename(p.with_suffix(''))
edit('game/UI/Campaign/CampaignClient.cs','            if(OS.GetCmdlineUserArgs().Contains("--campaign-client-test"))await ClientChecks();','            if(OS.GetCmdlineUserArgs().Contains("--first-relay-client-test"))await RunFirstRelayClientTests();\n            if(OS.GetCmdlineUserArgs().Contains("--campaign-client-test"))await ClientChecks();')
edit('game/UI/Campaign/CampaignClient.cs','a is "--campaign-client-test" or','a is "--first-relay-client-test" or "--campaign-client-test" or')
edit('game/Root.cs','        if(OS.GetCmdlineUserArgs().Contains("--r1-life-client-test"))','        if(OS.GetCmdlineUserArgs().Any(a=>a is "--first-side-client-test" or "--first-outer-client-test"))await RunFirstTowerClientTests();\n        if(OS.GetCmdlineUserArgs().Contains("--r1-life-client-test"))')
edit('game/Root.cs','or "--r1-life-client-test" or','or "--first-relay-client-test" or "--first-side-client-test" or "--first-outer-client-test" or "--r1-life-client-test" or')
p=R/'tools/ci/build.py';s=p.read_text(encoding='utf8')
extra=['first-relay-client-test','first-side-client-test','first-outer-client-test','field-relay-client-test']
# Only set/list entries; dictionary keys are handled explicitly below.
import re
s=re.sub(r'"chapter-four-local-client-test"(?!\s*:)',lambda m:m.group()+','+','.join('"'+v+'"' for v in extra),s)
s=s.replace('"chapter-four-local-client-test":"CP-R4-05"','"chapter-four-local-client-test":"CP-R4-05","first-relay-client-test":"CP-R0-03","first-side-client-test":"CP-R1-03","first-outer-client-test":"CP-R1-03","field-relay-client-test":"CP-FIELD-RELAY-05"')
s=s.replace('"chapter-four-local-client-test":"normal-r4-native-entry.json"','"chapter-four-local-client-test":"normal-r4-native-entry.json","first-relay-client-test":"normal-first-relay-native-entry.json","first-side-client-test":"normal-first-tower-native-entry.json","first-outer-client-test":"normal-first-tower-native-entry.json","field-relay-client-test":"normal-field-relay-native-entry.json"')
s=s.replace('"chapter-four-local-client-test":"chapter_four_local_client_results.json"','"chapter-four-local-client-test":"chapter_four_local_client_results.json","first-relay-client-test":"first_relay_client_results.json","first-side-client-test":"first_side_client_results.json","first-outer-client-test":"first_outer_client_results.json","field-relay-client-test":"field_relay_client_results.json"')
s=s.replace('"test","content","chapter-six-client-test"','"test","content","campaign-client-test","chapter-six-client-test"')
p.write_text(s,encoding='utf8')
edit('tools/ci/package_windows.py','"chapter_four_local_client_results.json"','"chapter_four_local_client_results.json","first_relay_client_results.json","first_side_client_results.json","first_outer_client_results.json","field_relay_client_results.json"')
edit('game/Root.cs','{ ClosePanel(); GetViewport().SetInputAsHandled(); return; }','{ Execute(async()=>{if(!await SaveAdventureDrafts())throw new InvalidOperationException("当前正文尚未保存，请完成输入法候选、重试或导出；窗口保留。");ClosePanel();}); GetViewport().SetInputAsHandled(); return; }')
for region,stem in [('Two','R2'),('Three','R3')]:
 path='game/UI/Shell/Root.Chapter'+region+'.cs'
 if region=='Two':
  writer='if(quest is "R2-O01" or "R2-O02"){await OpenR2LifeWriter(quest);return;}'
  meaning='if(quest is "R2-O01" or "R2-O02"){if(!await SaveR2LifeDraft())throw new InvalidOperationException("当前地方正文尚未保存。");if(quest=="R2-O01")OpenR2LifeReplyMeaning();else CheckCommit(await _r2Life!.ConfirmMeaningAsync(quest,assisted));return;}'
  submit='if(quest is "R2-O01" or "R2-O02"){await OpenR2LifeSubmission(quest);return;}'
 else:
  writer='if(quest=="R3-O02"){await OpenSideWellWriter();return;}'
  meaning='if(quest=="R3-O02"){await OpenSideWellMeaning(assisted);return;}'
  submit='if(quest=="R3-O02"){await OpenSideWellComparison();return;}'
 for method,args,prefix in [('Writer','string quest',writer),('Meaning','string quest,bool assisted',meaning),('Submission','string quest',submit)]:
  signature=f'private async ValueTask OpenChapter{region}{method}({args})\n    {{'
  edit(path,signature,signature+'\n        '+prefix)
print('Actual First native probes, archive producers and guarded Escape wired; execution still pending.')

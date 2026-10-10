from pathlib import Path
R=Path(__file__).resolve().parents[2]
for p in ['src/Mist.Core/OptionalLocalWorks/TessLocalReceiptModels.cs.pending','src/Mist.Application/OptionalLocalWorks/TessLocalReceiptBridge.cs.pending','tests/Mist.Tests/OptionalLocalWorks/TessLocalReceiptBridgeCases.cs.pending','game/UI/Shell/Root.TessLocalReceiptBridge.cs.pending']:
 (R/p).rename(R/p.removesuffix('.pending'))
def edit(p,old,new):
 path=R/p;s=path.read_text(encoding='utf-8-sig');assert old in s,(p,old);path.write_text(s.replace(old,new,1),encoding='utf8')
edit('tests/Mist.Tests/Program.cs','return TestRegistry.Run(args);','Mist.Tests.OptionalLocalWorks.TessLocalReceiptBridgeCases.Register();\nreturn TestRegistry.Run(args);')
edit('game/UI/Shell/Root.ChapterSix.cs','var flow=Panel("风暴边境现场 · "+spot,InputOwner.Workspace);','var flow=Panel("风暴边境现场 · "+spot,InputOwner.Workspace);AddTessLocalReceiptBridgeButton(flow,spot);')
edit('game/UI/Shell/Root.Views.cs','CloseR1LifeUiPreview(title);','CloseR1LifeUiPreview(title);CloseTessLocalReceiptBridgePreview(title);')
edit('game/UI/Shell/Root.RegionalTravel.cs','ResetR1LifeUiState();','ResetR1LifeUiState();ResetTessLocalReceiptBridgeUi();')
edit('game/UI/Shell/Root.Views.cs','还没有取得材料。请在现场走近公告、资料或录音台。','已得材料按下方档案入口查看。走近现场可取得新的公告、资料和录音。')
print('Tess existing-result bridge wired; no rewards or new content package; not yet tested')

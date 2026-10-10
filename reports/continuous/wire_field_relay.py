from pathlib import Path
import hashlib
R=Path(__file__).resolve().parents[2]
def edit(path,old,new):
 p=R/path;s=p.read_text(encoding='utf-8-sig');assert old in s,(path,old);p.write_text(s.replace(old,new,1),encoding='utf8')
names=['src/Mist.Core/OptionalLocalWorks/FieldRelayModels.cs.pending','src/Mist.Application/OptionalLocalWorks/FieldRelayRepairService.cs.pending','src/Mist.Application/OptionalLocalWorks/FieldRelayRepairService.Encounters.cs.pending','tests/Mist.Tests/OptionalLocalWorks/FieldRelayRepairCases.cs.pending','game/UI/Shell/Root.FieldRelayRepair.cs.pending','game/Diagnostics/Root.FieldRelayClientTests.cs.pending']
for name in names:
 p=R/name;p.rename(p.with_suffix(''))
assert hashlib.sha256((R/'content/source/optional_local_works/field_relay_contract.json').read_bytes()).hexdigest()=='cb06b027843a15779a24ff9f6219ba3fa380fbbabfa742ea9a8df6aa564309f6'
edit('tests/Mist.Tests/Program.cs','Mist.Tests.FirstApproaches.FirstApproachCases.Register();','Mist.Tests.FirstApproaches.FirstApproachCases.Register();\nMist.Tests.OptionalLocalWorks.FieldRelayRepairCases.Register();')
edit('game/UI/Shell/Root.Views.cs','            if(await TryChapterSixInteraction(target.Id))return;','            if(await TryFieldRelayRepairInteraction(target.Id))return;\n            if(await TryChapterSixInteraction(target.Id))return;')
edit('game/UI/Shell/Root.RegionalTravel.cs','private void ResetRegionalUiState(){ResetFirstApproachUiState();','private void ResetRegionalUiState(){ResetFieldRelayRepairUi();ResetFirstApproachUiState();')
edit('game/Root.cs','        if(OS.GetCmdlineUserArgs().Contains("--finale-client-test"))','        if(OS.GetCmdlineUserArgs().Contains("--field-relay-client-test"))await RunFieldRelayClientTests();\n        if(OS.GetCmdlineUserArgs().Contains("--finale-client-test"))')
edit('game/Root.cs','or "--finale-client-test" or','or "--field-relay-client-test" or "--finale-client-test" or')
path='game/UI/Shell/Root.EncounterRoute.cs'
edit(path,'        if(FirstApproachEncounterSelected)return await FirstApproachCurrentEncounterAsync();','        if(FieldRelayEncounterSelected){var current=await FieldRelayService().EncounterAsync();return current is null?null:new(current.Battle,current.Entry,current.ReturnActor,current.Battle.Outcome==CombatOutcome.Controlled);}\n        if(FirstApproachEncounterSelected)return await FirstApproachCurrentEncounterAsync();')
edit(path,'PreviewEncounter(BattleRequest r)=>','PreviewEncounter(BattleRequest r)=>FieldRelayEncounterSelected?FieldRelayService().PreviewActionAsync(r):')
edit(path,'ConfirmEncounter(Guid token)=>','ConfirmEncounter(Guid token)=>FieldRelayEncounterSelected?FieldRelayService().ConfirmPreviewAsync(token):')
edit(path,'        if(FirstApproachEncounterSelected)return await FirstApproachRetryEncounter();','        if(FieldRelayEncounterSelected)return await RetryFieldRelayEncounterAsync();\n        if(FirstApproachEncounterSelected)return await FirstApproachRetryEncounter();')
edit(path,'WithdrawEncounter()=>','WithdrawEncounter()=>FieldRelayEncounterSelected?WithdrawFieldRelayEncounterAsync():')
edit(path,'{CloseFirstApproachEncounterPreview(token);','{_fieldRelayRepair?.ClosePreview(token);CloseFirstApproachEncounterPreview(token);')
edit('game/UI/Shell/Root.Combat.cs','Panel(FirstApproachEncounterSelected?','Panel(FieldRelayEncounterSelected?"外线辅助继电处置":FirstApproachEncounterSelected?')
compiler='tools/Mist.ContentCli/Validation/ContentCompiler.cs'
edit(compiler,'        string firstApproachSource=','        string fieldRelaySource=Path.Combine(root,"content/source/optional_local_works/field_relay_contract.json");\n        var fieldRelay=Mist.Core.OptionalLocalWorks.FieldRelayContract.ParseJson(File.ReadAllText(fieldRelaySource));\n        using(var projection=JsonDocument.Parse(File.ReadAllText(fieldRelaySource)))DenyAuthorAnswers(projection.RootElement);\n        string fieldRelayKey="field-relay-contract:"+fieldRelay.Version,fieldRelayHash=CatalogImporter.HashFile(fieldRelaySource);\n        if(history.TryGetValue(fieldRelayKey,out var priorFieldRelay)&&priorFieldRelay!=fieldRelayHash)throw new ContentException("Same field relay contract version changed.");\n        string firstApproachSource=')
edit(compiler,'            string target=Path.Combine(output,"first_approaches/learner_package.json");','            string relayTarget=Path.Combine(output,"field_relay_contract.json");File.Copy(fieldRelaySource,relayTarget,true);\n            assets=assets.Add(new("field_relay_contract.json",CatalogImporter.HashFile(relayTarget),new FileInfo(relayTarget).Length,"runtime_rules"));history.TryAdd(fieldRelayKey,fieldRelayHash);\n            string target=Path.Combine(output,"first_approaches/learner_package.json");')
edit('game/UI/Campaign/CampaignClient.ReleaseProbe.cs','"first_approaches/learner_package.json"','"first_approaches/learner_package.json","field_relay_contract.json"')
print('Field relay registered and routed to real producer; execution pending.')

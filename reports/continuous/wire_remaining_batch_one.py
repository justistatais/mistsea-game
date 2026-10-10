from pathlib import Path
R=Path(__file__).resolve().parents[2]
def edit(path,old,new):
 p=R/path;s=p.read_text(encoding='utf-8-sig');assert old in s,(path,old);p.write_text(s.replace(old,new,1),encoding='utf8')
files=[
'src/Mist.Core/VesselLife/VesselLifeModels.cs.pending','src/Mist.Application/VesselLife/VesselLifeService.cs.pending','tests/Mist.Tests/VesselLife/VesselLifeCases.cs.pending','game/UI/Shell/Root.VesselLife.cs.pending','game/Diagnostics/Root.VesselLifeClientTests.cs.pending',
'src/Mist.Core/OptionalEncounterContracts/OptionalEncounterModels.cs.pending','src/Mist.Application/OptionalEncounterContracts/OptionalEncounterService.cs.pending','tests/Mist.Tests/OptionalEncounterContracts/OptionalEncounterCases.cs.pending','game/UI/Shell/Root.OptionalEncounterContracts.cs.pending','game/Diagnostics/Root.OptionalEncounterClientTests.cs.pending',
'src/Mist.Core/OptionalLocalWorks/SettlementSupportModels.cs.pending','src/Mist.Application/OptionalLocalWorks/SettlementSupportService.cs.pending','src/Mist.Application/OptionalLocalWorks/SettlementSupportService.Press.cs.pending','src/Mist.Application/OptionalLocalWorks/SettlementSupportService.Lamp.cs.pending','tests/Mist.Tests/OptionalLocalWorks/SettlementSupportCases.cs.pending','game/UI/Shell/Root.SettlementSupport.cs.pending','game/Diagnostics/Root.SettlementSupportClientTests.cs.pending']
for name in files:
 p=R/name;assert not p.with_suffix('').exists();p.rename(p.with_suffix(''))
edit('tests/Mist.Tests/Program.cs','return TestRegistry.Run(args);','Mist.Tests.VesselLife.VesselLifeCases.Register();\nMist.Tests.OptionalEncounterContracts.OptionalEncounterCases.Register();\nMist.Tests.OptionalLocalWorks.SettlementSupportCases.Register();\nreturn TestRegistry.Run(args);')
edit('game/Root.cs','        LoadRegionalVoyages();','        LoadRegionalVoyages();\n        LoadOptionalEncounterContracts(MergeRegionalGeometry(_firstApproaches!.Geometry));')
edit('game/Root.cs','private async ValueTask<bool> SaveAdventureDrafts()=>','private async ValueTask<bool> SaveAdventureDrafts()=>await PauseVesselLifeAsync()&&await SaveLinaLetter()&&await SaveSettlementDrafts()&&')
edit('game/Root.cs','        await RestoreFirstApproachSelection();','        if(_regionalGeometry is not null)_regionalGeometry=ExtendOptionalEncounterGeometry(_regionalGeometry);\n        await RestoreFirstApproachSelection();')
# Never deserialize an optional contract snapshot as a main chapter encounter.
p=R/'game/Root.cs';s=p.read_text(encoding='utf8')
for region in ['R2','R3','R4','R5','R6']:
 old='e.Id.StartsWith("combat:ENC-'+region+'-",StringComparison.Ordinal)'
 if old in s:s=s.replace(old,old+'&&!Mist.Core.OptionalEncounterContracts.OptionalContractRules.Ids.Any(id=>e.Id=="combat:"+id)')
p.write_text(s,encoding='utf8')
edit('game/Root.cs','        if(OS.GetCmdlineUserArgs().Contains("--musicbox-client-test"))','        if(OS.GetCmdlineUserArgs().Contains("--vessel-life-client-test"))await RunVesselLifeClientTests();\n        if(OS.GetCmdlineUserArgs().Contains("--settlement-support-client-test"))await RunSettlementSupportClientTests();\n        if(OS.GetCmdlineUserArgs().Any(a=>a.StartsWith("--optional-r",StringComparison.Ordinal)&&a.EndsWith("-client-test",StringComparison.Ordinal)))await RunOptionalEncounterClientTests();\n        if(OS.GetCmdlineUserArgs().Contains("--musicbox-client-test"))')
edit('game/Root.cs','or "--field-relay-client-test" or','or "--vessel-life-client-test" or "--settlement-support-client-test" or "--optional-r1-client-test" or "--optional-r2-client-test" or "--optional-r3-client-test" or "--optional-r4-client-test" or "--optional-r5-client-test" or "--optional-r6-client-test" or "--field-relay-client-test" or')
edit('game/UI/Shell/Root.RegionalTravel.cs','private void ResetRegionalUiState(){','private void ResetRegionalUiState(){ResetVesselLifeUiState();ResetLinaWorkUi();ResetSettlementSupportUi();ResetOptionalEncounterUi();')
edit('game/UI/Shell/Root.Views.cs','if(title is not null){CloseR2LifeUiPreview(title);','if(title is not null){CloseLinaWorkPreview(title);CloseSettlementPreview(title);CloseR2LifeUiPreview(title);')
edit('game/UI/Shell/Root.Views.cs','            if(await TryFieldRelayRepairInteraction(target.Id))return;','            if(await TryOptionalEncounterInteraction(target.Id))return;\n            if(await TrySettlementSupportInteraction(target.Id))return;\n            if(await TryFieldRelayRepairInteraction(target.Id))return;')
edit('game/UI/Shell/Root.Views.cs','        Button(flow,"撤离草稿",()=>OpenWriter());','        Button(flow,"莉娜当前获准个人材料",()=>Execute(OpenLinaWorkArchive));\n        Button(flow,"当前已得独立安全合同",()=>Execute(async()=>{var archive=Panel("独立安全合同档案",InputOwner.Workspace);await AddOptionalEncounterArchive(archive);}));\n        Button(flow,"撤离草稿",()=>OpenWriter());')
edit('game/UI/Shell/Root.Vessel.cs','var flow=Panel(view.Station.Label,InputOwner.Workspace);','var flow=Panel(view.Station.Label,InputOwner.Workspace);AddVesselLifeButton(flow,spot);\n        if(spot is "HUB-S01.deckwatch" or "HUB-S02.correspondence")Button(flow,"莉娜当前受邀个人工作",()=>Execute(OpenLinaWorkList));')
edit('game/UI/Shell/Root.PeopleWork.cs','        if(identity.Role!="companion")return;','        if(id=="CP-LINA")Button(flow,"莉娜当前受邀个人工作",()=>Execute(OpenLinaWorkList));\n        if(identity.Role!="companion")return;')
edit('game/UI/Shell/Root.RegionalLearning.cs','        foreach(var read in _regionalLearningScopes.Where','        if(_linaWork is not null)foreach(var m in await _linaWork.AccessibleMaterialsAsync())sources[m.Id]=new(m.Id,m.Version,LearningBodyHash(m.English));\n        foreach(var read in _regionalLearningScopes.Where')
route='game/UI/Shell/Root.EncounterRoute.cs'
edit(route,'        if(FieldRelayEncounterSelected){','        if(OptionalEncounterSelected)return await OptionalCurrentEncounterAsync();\n        if(FieldRelayEncounterSelected){')
edit(route,'PreviewEncounter(BattleRequest r)=>','PreviewEncounter(BattleRequest r)=>OptionalEncounterSelected?OptionalPreviewEncounter(r):')
edit(route,'ConfirmEncounter(Guid token)=>','ConfirmEncounter(Guid token)=>OptionalEncounterSelected?OptionalConfirmEncounter(token):')
edit(route,'        if(FieldRelayEncounterSelected)return await RetryFieldRelayEncounterAsync();','        if(OptionalEncounterSelected)return await OptionalRetryEncounter();\n        if(FieldRelayEncounterSelected)return await RetryFieldRelayEncounterAsync();')
edit(route,'WithdrawEncounter()=>','WithdrawEncounter()=>OptionalEncounterSelected?OptionalWithdrawEncounter():')
edit(route,'{_fieldRelayRepair?.ClosePreview(token);','{OptionalCloseEncounterPreview(token);_fieldRelayRepair?.ClosePreview(token);')
edit('game/UI/Shell/Root.Combat.cs','Panel(FieldRelayEncounterSelected?','Panel(OptionalEncounterSelected?OptionalEncounterTitle:FieldRelayEncounterSelected?')
edit('game/UI/Shell/Root.Combat.cs','FieldRelayEncounterSelected||FirstApproachEncounterSelected?','OptionalEncounterSelected||FieldRelayEncounterSelected||FirstApproachEncounterSelected?')
edit('game/UI/Shell/Root.Combat.cs','()=>{if(FieldRelayEncounterSelected)Execute(OpenFieldRelayContract);','()=>{if(OptionalEncounterSelected)Execute(async()=>await OpenOptionalEncounterMaterial(_optionalEncounters!.Package.Contracts.Single(c=>c.Id==_optionalEncounterSelected).Material));else if(FieldRelayEncounterSelected)Execute(OpenFieldRelayContract);')
# Versioned player projections, never author cards or answer manifests.
packages=[('vesselLife','content/source/vessel_life/learner_package.json','Mist.Core.VesselLife.VesselLifePackage.ParseJson','Version','vessel_life_player.json'),('linaPersonal','content/source/lina_personal/learner_package.json','Mist.Core.LinaPersonalWorks.LinaWorkPackage.Parse','Version','lina_personal_player.json'),('optionalContracts','content/source/optional_encounter_contracts/learner_package.json','Mist.Core.OptionalEncounterContracts.OptionalContractPackage.Parse','ContentVersion','optional_encounter_contracts/learner_package.json'),('settlementSupport','content/source/optional_local_works/settlement_support_contracts.json','Mist.Core.OptionalLocalWorks.SettlementSupportContract.ParseJson','Version','settlement_support_contracts.json')]
validation=[];copies=[]
for name,source,parser,version,target in packages:
 validation.append(f'''        string {name}Source=Path.Combine(root,"{source}");
        var {name}={parser}(File.ReadAllText({name}Source));
        using(var projection=JsonDocument.Parse(File.ReadAllText({name}Source)))DenyAuthorAnswers(projection.RootElement);
        string {name}Key="{name}:"+{name}.{version},{name}Hash=CatalogImporter.HashFile({name}Source);
        if(history.TryGetValue({name}Key,out var prior{name})&&prior{name}!={name}Hash)throw new ContentException("Same {name} version changed.");''')
 copies.append(f'''        {{
            string target=Path.Combine(output,"{target}");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy({name}Source,target,true);
            assets=assets.Add(new("{target}",CatalogImporter.HashFile(target),new FileInfo(target).Length,"runtime_rules"));history.TryAdd({name}Key,{name}Hash);
        }}''')
edit('tools/Mist.ContentCli/Validation/ContentCompiler.cs','        string fieldRelaySource=', '\n'.join(validation)+'\n        string fieldRelaySource=')
edit('tools/Mist.ContentCli/Validation/ContentCompiler.cs','            string relayTarget=', '}\n'+ '\n'.join(copies)+'\n        {\n            string relayTarget=')
edit('game/UI/Campaign/CampaignClient.ReleaseProbe.cs','"field_relay_contract.json"','"field_relay_contract.json",'+','.join('"'+x[-1]+'"' for x in packages))
# Add native actions consistently in all four policy/action sets and producer maps.
native={'vessel-life-client-test':('CP-NPC-LIFE','Normal native-entry profile','normal-vessel-life-native-entry.json','vessel_life_client_results.json'),'settlement-support-client-test':('CP-SETTLEMENT-05',None,'normal-settlement-native-entry.json','settlement_support_client_results.json')}
for n in range(1,7):native[f'optional-r{n}-client-test']=(f'CP-R{n}-03',f'normal_zero_XP_R{n}_both_independent_optional_contracts',f'normal-optional-r{n}-native-entry.json',f'optional_r{n}_client_results.json')
p=R/'tools/ci/build.py';s=p.read_text(encoding='utf-8-sig')
added=','.join('"'+k+'"' for k in native)
s=s.replace('"field-relay-client-test"}', '"field-relay-client-test",'+added+'}')
def mapadd(anchor,field):
 global s
 assert anchor in s
 tail=','.join('"'+k+'":'+('None' if v[field] is None else '"'+v[field]+'"') for k,v in native.items())
 s=s.replace(anchor,anchor[:-1]+','+tail+'}')
mapadd('"musicbox-client-test":"CP-OPTIONAL-MUSICBOX-05"}',0)
mapadd('"finale-client-test":"leaves untouched RF native entry"}',1)
mapadd('"musicbox-client-test":"normal-musicbox-native-entry.json"}',2)
mapadd('"musicbox-client-test":"musicbox_client_results.json"}',3)
p.write_text(s,encoding='utf8')
print('17 frozen production files + Lina wired. No execution/acceptance claim.')

from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
def edit(path,before,after):
    p=ROOT/path;t=p.read_text(encoding='utf-8-sig');assert before in t,(path,before);p.write_text(t.replace(before,after),encoding='utf-8')
edit('game/Root.cs','        LoadRegionalVoyages();','        LoadRegionalVoyages();\n        RegisterRegionalLearningScope("current-permissions",ReadGrantedLearningSources);')
for p in sorted((ROOT/'game/UI/Shell').glob('Root.Chapter*.cs'))+[ROOT/'game/UI/Shell/Root.Finale.cs']:
    t=p.read_text(encoding='utf-8-sig')
    if 'flow.AddChild(reader);' in t:t=t.replace('flow.AddChild(reader);','flow.AddChild(reader);AddRegionalDictionary(flow,reader);')
    if p.name=='Root.ChapterOne.cs':t=t.replace('flow.AddChild(text);','flow.AddChild(text);AddRegionalDictionary(flow,text);')
    p.write_text(t,encoding='utf-8')
edit('game/UI/Shell/Root.ChapterOne.cs','var materials=(_coastArchive?.Package.Materials??[]).Concat(_chapter?.Package.Materials??[]).Where(m=>snapshot.Entities.Any(e=>e.Id=="material:"+m.Id)).DistinctBy(m=>m.Id).ToArray();','var materials=GrantedChapterMaterials(snapshot).ToArray();')
edit('game/UI/Shell/Root.ChapterOne.cs','if(!(await _store.SnapshotAsync()).Entities.Any(e=>e.Id=="material:"+material.Id))throw new InvalidOperationException("该材料尚未实际取得。");','if(!GrantedChapterMaterials(await _store.SnapshotAsync()).Any(m=>m.Id==material.Id&&m.Version==material.Version))throw new InvalidOperationException("该版本材料尚未实际取得，或当前来源许可不可用。");')
p=ROOT/'game/UI/Shell/Root.Views.cs';t=p.read_text(encoding='utf-8-sig');a=t.index('    private void OpenDictionary(');b=t.index('    private async void OpenWriter()',a)
t=t[:a]+'''    private void OpenDictionary(string word,MaterialDefinition material)
    {
        _support.Add(new("dictionary",material.Id,material.Paragraphs[0].Id,0,material.Paragraphs[0].Text.EnumerateRunes().Count()));
        Execute(async()=>await OpenRegionalDictionary(word,ReadGrantedLearningSources));
    }
'''+t[b:]
t=t.replace('var flow=Panel("已得档案",InputOwner.Workspace); var visible=','var flow=Panel("已得档案",InputOwner.Workspace);Button(flow,"离线查词",OpenRegionalDictionarySearch);Button(flow,"主动查看到期复习",()=>Execute(OpenRegionalReview)); var visible=')
p.write_text(t,encoding='utf-8')
print('Permission-aware dictionary readers and explicit review entry wired.')

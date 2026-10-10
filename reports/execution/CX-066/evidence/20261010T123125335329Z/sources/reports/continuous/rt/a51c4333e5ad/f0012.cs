using System.Collections.Immutable;
using Godot;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Application.Learning;
using Mist.Application.UI;
using Mist.Core.ChapterTwo;
using Mist.Core.Contracts;
using Mist.Core.Learning;

public partial class Root
{
    private sealed class RegionalLearningClock : IClock { public DateTimeOffset UtcNow=>DateTimeOffset.UtcNow; }
    private RegionalLearningService? _regionalLearning;
    private readonly Dictionary<string,Func<ValueTask<IReadOnlyCollection<LearningSourceAccess>>>> _regionalLearningScopes=new(StringComparer.Ordinal);
    private RegionalLearningService RegionalLearning()
    {
        if(_regionalLearning is not null)return _regionalLearning;
        using var json=Godot.FileAccess.Open("res://content/lexicon/learner_lexicon.json",Godot.FileAccess.ModeFlags.Read);
        if(json is null)throw new InvalidDataException("离线词典尚未安装，当前存档和正文保留。");
        return _regionalLearning=new(RegionalLexicon.Parse(json.GetAsText()),_store,new RegionalLearningClock());
    }
    // Root registers current permission reads, including later regions only after their service is present.
    private void RegisterRegionalLearningScope(string key,Func<ValueTask<IReadOnlyCollection<LearningSourceAccess>>> currentRead)=>_regionalLearningScopes[key]=currentRead;
    private async ValueTask<IReadOnlyCollection<LearningSourceAccess>> CurrentRegionalLearningScope()
    {
        var sources=new Dictionary<string,LearningSourceAccess>(StringComparer.Ordinal);
        foreach(var read in _regionalLearningScopes.Values)foreach(var source in await read())sources[source.MaterialId]=source;
        return sources.Values.ToArray();
    }
    private async ValueTask<IReadOnlyCollection<LearningSourceAccess>> ReadGrantedLearningSources()
    {
        var snapshot=await _store.SnapshotAsync();var sources=new Dictionary<string,LearningSourceAccess>(StringComparer.Ordinal);
        foreach(var m in Mist.Application.Materials.MaterialJournal.Visible(_catalog,snapshot).Where(m=>!RegionalMaterialId(m.Id)))
            sources[m.Id]=new(m.Id,m.Version,m.BodySha256);
        foreach(var material in GrantedChapterMaterials(snapshot))sources[material.Id]=new(material.Id,material.Version,LearningBodyHash(material.English));
        if(_r1Life is not null&&snapshot.Entities.Any(e=>e.Id==Mist.Application.R1Life.R1LifeService.StateId))foreach(var m in (await _r1Life.ViewAsync()).Materials)sources[m.Id]=RegionalLearningAccess(m);
        if(_chapterTwo is not null&&snapshot.Entities.Any(e=>e.Id=="chapter2"))foreach(var m in (await _chapterTwo.ViewAsync()).Materials)sources[m.Id]=RegionalLearningAccess(m);
        if(_chapterThree is not null&&snapshot.Entities.Any(e=>e.Id=="chapter3"))foreach(var m in (await _chapterThree.ViewAsync()).Materials)sources[m.Id]=RegionalLearningAccess(m);
        if(_chapterFour is not null&&snapshot.Entities.Any(e=>e.Id=="chapter4"))foreach(var m in (await _chapterFour.ViewAsync()).Materials)sources[m.Id]=RegionalLearningAccess(m);
        if(_chapterFive is not null&&snapshot.Entities.Any(e=>e.Id=="chapter5"))foreach(var m in (await _chapterFive.ViewAsync()).Materials)sources[m.Id]=RegionalLearningAccess(m);
        if(_chapterSix is not null&&snapshot.Entities.Any(e=>e.Id=="chapter6"))foreach(var m in (await _chapterSix.ViewAsync()).Materials)sources[m.Id]=RegionalLearningAccess(m);
        if(_finale is not null&&snapshot.Entities.Any(e=>e.Id=="finale"))foreach(var m in (await _finale.ViewAsync()).Materials)sources[m.Id]=RegionalLearningAccess(m);
        if(_firstApproaches is not null)foreach(var m in await _firstApproaches.MaterialsAsync())sources[m.Id]=new(m.Id,m.Version,LearningBodyHash(string.Join("\n\n",m.Paragraphs.Select(p=>p.En))));
        if(_chapterSix is not null&&snapshot.Entities.Any(e=>e.Id=="material:"+Mist.Application.OptionalLocalWorks.FieldRelayRepairService.MaterialId)){var m=await FieldRelayService().OpenContractAsync();sources[Mist.Application.OptionalLocalWorks.FieldRelayRepairService.MaterialId]=new(Mist.Application.OptionalLocalWorks.FieldRelayRepairService.MaterialId,m.Version,LearningBodyHash(string.Join("\n\n",m.Paragraphs.Select(p=>p.English))));}
        if(_linaWork is not null)foreach(var m in await _linaWork.AccessibleMaterialsAsync())sources[m.Id]=new(m.Id,m.Version,LearningBodyHash(m.English));
        if(_r3Shift is not null)foreach(var m in (await _r3Shift.ViewAsync()).Materials)sources[m.Id]=new(m.Id,m.Version,LearningBodyHash(m.English));
        foreach(var source in await ReadVesselLifeLearningSources())sources[source.MaterialId]=source;
        if(_settlementSupport is not null)foreach(var id in (await _settlementSupport.ViewAsync()).Acquired){var section=await _settlementSupport.OpenContractAsync(id);sources["settlement:"+id]=new("settlement:"+id,_settlementSupport.Contract.Version,LearningBodyHash(string.Join("\n\n",section.Paragraphs.Select(p=>p.English))));}
        foreach(var read in _regionalLearningScopes.Where(p=>p.Key!="current-permissions").Select(p=>p.Value))foreach(var source in await read())sources[source.MaterialId]=source;
        return sources.Values.ToArray();
    }
    private static string LearningBodyHash(string body)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    private IEnumerable<Mist.Core.Campaign.CampaignMaterial> GrantedChapterMaterials(BranchSnapshot snapshot)
    {
        bool Permission(string id)
        {
            var entity=snapshot.Entities.SingleOrDefault(e=>e.Id==id);if(entity is null)return false;
            using var json=System.Text.Json.JsonDocument.Parse(entity.Json);
            return json.RootElement.TryGetProperty("allowed",out var allowed)&&allowed.ValueKind==System.Text.Json.JsonValueKind.True;
        }
        foreach(var material in (_coastArchive?.Package.Materials??[]).Concat(_chapter?.Package.Materials??[]).DistinctBy(m=>m.Id))
        {
            var entity=snapshot.Entities.SingleOrDefault(e=>e.Id=="material:"+material.Id);if(entity is null)continue;
            using var json=System.Text.Json.JsonDocument.Parse(entity.Json);var access=json.RootElement;
            if(!access.TryGetProperty("version",out var version)||version.GetString()!=material.Version)continue;
            if(_chapter?.Package.Materials.Any(m=>m.Id==material.Id)==true)
            {
                if(!Permission(material.Id.StartsWith("Q103",StringComparison.Ordinal)?"permission:Q-103":"permission:Q-104")||
                    !access.TryGetProperty("body_sha256",out var hash)||hash.GetString()!=LearningBodyHash(material.English))continue;
            }
            else if(!access.TryGetProperty("acquired",out var acquired)||acquired.ValueKind!=System.Text.Json.JsonValueKind.True)continue;
            yield return material;
        }
    }
    private void AddRegionalDictionary(VBoxContainer flow,TextEdit reader)
    {
        var query=new LineEdit{PlaceholderText="选中原文中的词，或在这里输入词/短语",AccessibilityName="离线查词输入"};flow.AddChild(query);
        Button(flow,"查选区或输入的词义",()=>Execute(async()=>{string word=reader.GetSelectedText();await OpenRegionalDictionary(string.IsNullOrWhiteSpace(word)?query.Text:word,ReadGrantedLearningSources);}));
    }
    private void OpenRegionalDictionarySearch()
    {
        var flow=Panel("离线查词",InputOwner.Workspace);var input=new LineEdit{PlaceholderText="English word or phrase",AccessibilityName="离线词典查询"};flow.AddChild(input);
        Button(flow,"查词",()=>Execute(async()=>await OpenRegionalDictionary(input.Text,ReadGrantedLearningSources)));Button(flow,"主动查看到期复习",()=>Execute(OpenRegionalReview));
    }
    private LearningSourceAccess RegionalLearningAccess(ChapterMaterial material)
    {
        bool available=false,played=false;
        if(material.Kind=="listening_script"&&RegionalAudioBindings().TryGetValue(material.Id,out var binding)&&binding is not null)
        {
            if(!System.Text.RegularExpressions.Regex.IsMatch(binding.AudioPath,"^audio/regional/[A-Za-z0-9_.-]+\\.wav$"))throw new InvalidDataException("候选音轨路径未登记。");
            var bytes=Godot.FileAccess.GetFileAsBytes("res://content/"+binding.AudioPath);
            if(bytes.Length>0){Mist.Core.Assets.RegionalAudioRules.Validate(material,binding,bytes);available=true;}
            played=available&&_activeAudioId==material.Id&&_lessonAudio is not null&&(_lessonAudio.Playing||_lessonAudio.GetPlaybackPosition()>0);
        }
        return new(material.Id,material.Version,material.Fingerprint,available,played,false);
    }
    private async ValueTask OpenRegionalDictionary(string query,Func<ValueTask<IReadOnlyCollection<LearningSourceAccess>>> currentRead)
    {
        if(_lessonAudio is{Playing:true})_lessonAudio.StreamPaused=true;
        var service=RegionalLearning();var scope=await currentRead();var views=service.Lookup(query,scope);
        var flow=Panel("离线词典",InputOwner.Workspace);
        Label(flow,"360 CORE + 360 EXTENDED 词义 · 读、听、写目标各6.5 · 词义语境和词音仍待人工复核。",18);
        if(views.IsEmpty){Label(flow,"未收录这个选区。可保留原文选区和笔记。",22);return;}
        foreach(var view in views)
        {
            Label(flow,view.Term+" · "+view.Tier+" · "+view.SenseKey,22);Label(flow,view.EnglishDefinition,20);Label(flow,view.ChineseAid,20);
            foreach(var context in view.PermittedContexts)
            {
                Label(flow,context.ParagraphEnglish,20);Label(flow,"当前获准原文："+context.MaterialId+" / "+context.Version+" / "+context.ParagraphId,16);
            }
            if(view.PermittedContexts.IsEmpty)Label(flow,"当前未取得相应原文；只显示词义，不显示其他地区或私人材料。",18);
            var snapshot=await service.SnapshotAsync();
            await service.ObserveLookupAsync(snapshot.Identity,view.Id,view.PermittedContexts.FirstOrDefault(),await currentRead(),Guid.NewGuid().ToString());
        }
        Button(flow,"主动查看到期复习",()=>Execute(OpenRegionalReview));
    }
    private async ValueTask OpenRegionalReview()
    {
        var service=RegionalLearning();var cards=await service.DueAsync(await CurrentRegionalLearningScope(),limit:3);
        var flow=Panel("离线到期复习",InputOwner.Workspace);
        Label(flow,"每次最多3项，可随时关闭或延后。真实日期用于复习；剧情时间、奖励和结局不受影响。",18);
        if(cards.IsEmpty){Label(flow,"当前没有到期项。查词后会安排后续复习。",22);return;}
        foreach(var card in cards)Button(flow,card.Term+" · "+(card.UnfamiliarFamily?"当前兼容的新材料族":"熟悉语境或独立词义练习"),()=>Execute(async()=>await OpenRegionalReviewCard(card.SenseId)));
    }
    private async ValueTask OpenRegionalReviewCard(string senseId)
    {
        var service=RegionalLearning();var scope=await CurrentRegionalLearningScope();var card=(await service.DueAsync(scope,limit:100)).SingleOrDefault(c=>c.SenseId==senseId);
        if(card is null)throw new InvalidOperationException("该项已复习或延后，请刷新列表。");
        var snapshot=await service.SnapshotAsync();var flow=Panel("复习："+card.Term,InputOwner.Workspace);
        Label(flow,"用英文解释当前词义，或写一个符合语境的句子。正文与自评只保存在本机学习记录中。",20);
        if(card.Context is{} context){Label(flow,context.ParagraphEnglish,20);Label(flow,context.MaterialId+" / "+context.Version+" / "+context.ParagraphId,16);}
        if(card.ScriptOnly)Label(flow,"当前是听力脚本阅读；未播放实际音轨，不计作听力证据。",18);
        bool support=false;var answer=new TextEdit{CustomMinimumSize=new Vector2(0,160),PlaceholderText="Your explanation or contextual sentence",AccessibilityName="本机复习英文回答"};flow.AddChild(answer);
        Button(flow,"查看词义辅助",()=>{support=true;Label(flow,card.EnglishDefinition+"\n"+card.ChineseAid,20);});
        var production=new CheckBox{Text="这次写了语境句子（产出练习）",AccessibilityName="产出练习"};flow.AddChild(production);
        var choices=new HFlowContainer();flow.AddChild(choices);
        foreach(var rating in new[]{("again","再练一次"),("hard","仍觉得困难"),("good","我觉得理解了")})
        {
            string attempt=Guid.NewGuid().ToString();
            Button(choices,rating.Item2,()=>Execute(async()=>
            {
                await service.ReviewAsync(snapshot.Identity,senseId,rating.Item1,card.Context,await CurrentRegionalLearningScope(),attempt,answer.Text,support,production.ButtonPressed);
                ClosePanel();_status.Text="本机复习回答与自评已保存；尚未评测，不生成能力分数或掌握认证。";
            }));
        }
        var later=new HFlowContainer();flow.AddChild(later);
        foreach(int days in new[]{1,3,7})
        {
            string attempt=Guid.NewGuid().ToString();Button(later,"延后"+days+"天",()=>Execute(async()=>
            {await service.DeferAsync(snapshot.Identity,senseId,days,attempt);ClosePanel();_status.Text="复习已按你的选择延后。";}));
        }
        answer.GrabFocus();
    }
}

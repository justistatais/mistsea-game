using Godot;
using Mist.Application.UI;
using Mist.Core.Assets;
using Mist.Core.ChapterTwo;
using Mist.Core.Contracts;
using Mist.Core.Learning;

public partial class Root
{
    private IReadOnlyDictionary<string,RegionalAudioBinding>? _regionalAudioBindings;
    private IReadOnlyDictionary<string,RegionalAudioBinding> RegionalAudioBindings()
    {
        if(_regionalAudioBindings is not null)return _regionalAudioBindings;
        using var source=Godot.FileAccess.Open("res://content/audio/regional_runtime_bindings.json",Godot.FileAccess.ModeFlags.Read);
        _regionalAudioBindings=source is null?new Dictionary<string,RegionalAudioBinding>():StrictJson.Deserialize<RegionalAudioBinding[]>(source.GetAsText()).ToDictionary(b=>b.MaterialId,StringComparer.Ordinal);
        return _regionalAudioBindings;
    }
    private void AddRegionalAudioButton(VBoxContainer flow,ChapterMaterial material,Func<ValueTask<ChapterMaterial>> permissionQuery)
    {
        if(material.Kind=="listening_script")Button(flow,"听力音轨与脚本",()=>Execute(async()=>await OpenRegionalAudio(await permissionQuery(),permissionQuery,scriptAlreadyShown:true)));
    }
    private async ValueTask OpenRegionalAudio(ChapterMaterial material,Func<ValueTask<ChapterMaterial>> permissionQuery,bool scriptAlreadyShown=false)
    {
        material=await permissionQuery();CloseLessonAudio();var flow=Panel("听力："+material.Title,InputOwner.Workspace);
        Label(flow,"读、听、写目标各6.5。候选音轨的播放次数和完成度不会生成能力分数或世界奖励。",18);
        var state=Label(flow,"当前仅有脚本，可用原文和中文辅助继续任务。",20);
        bool available=RegionalAudioBindings().TryGetValue(material.Id,out var binding),played=false,subtitles=scriptAlreadyShown;int replays=0,pauses=0;decimal speedValue=1m;string attempt=Guid.NewGuid().ToString();
        if(scriptAlreadyShown)Label(flow,"刚才的材料页已显示原文脚本；本次笔记保留脚本辅助记录。",18);
        if(available)
        {
            if(binding is null||!System.Text.RegularExpressions.Regex.IsMatch(binding.AudioPath,"^audio/regional/[A-Za-z0-9_.-]+\\.wav$"))throw new InvalidDataException("候选音轨路径未登记。");
            string path="res://content/"+binding.AudioPath;var bytes=Godot.FileAccess.GetFileAsBytes(path);var facts=RegionalAudioRules.Validate(material,binding,bytes);
            var stream=AudioStreamWav.LoadFromBuffer(bytes,new Godot.Collections.Dictionary());
            if(stream is null){available=false;state.Text="实际音轨无法解码；脚本与正文仍可使用。";}
            else
            {
                _lessonAudio=new AudioStreamPlayer{Stream=stream,Bus=MistAudioChannels.Content};AddChild(_lessonAudio);_activeAudioId=material.Id;var player=_lessonAudio;
                state.Text="本地合成候选音轨 · 尚待审听、声音区分、字幕对齐、语言校准和权利复核。";
                var progress=Label(flow,"实际 WAV 时长："+(facts.DurationMicroseconds/1_000_000d).ToString("F1")+" 秒；尚未播放。",18);
                Button(flow,"播放 / 暂停候选音轨",()=>Execute(async()=>
                {
                    var permitted=await permissionQuery();if(permitted.Id!=material.Id||permitted.Version!=material.Version||permitted.Fingerprint!=material.Fingerprint)throw new InvalidOperationException("当前原文许可或版本已改变，请重新打开音轨。");
                    if(player.StreamPaused)player.StreamPaused=false;
                    else if(player.Playing){player.StreamPaused=true;_audioPositions[material.Id]=player.GetPlaybackPosition();pauses++;}
                    else{player.Play((float)_audioPositions.GetValueOrDefault(material.Id));if(played)replays++;played=player.Playing;}
                    state.Text=player.StreamPaused?"已暂停，实际播放位置保留。":"正在播放本地候选音轨。";
                }));
                Button(flow,"保留当前播放位置",()=>{_audioPositions[material.Id]=player.GetPlaybackPosition();progress.Text="已保留实际位置："+_audioPositions[material.Id].ToString("F1")+" 秒。";});
                var speed=new OptionButton{AccessibilityName="候选音轨播放速度"};foreach(string label in new[]{"0.75×","1×","1.25×"})speed.AddItem(label);speed.Select(1);speed.ItemSelected+=index=>Execute(async()=>{var current=await permissionQuery();if(current.Id!=material.Id||current.Version!=material.Version||current.Fingerprint!=material.Fingerprint)throw new InvalidOperationException("当前原文许可或版本已改变，请重新打开音轨。");speedValue=new[]{.75m,1m,1.25m}[index];player.PitchScale=(float)speedValue;});flow.AddChild(speed);
                player.Finished+=()=>{if(IsInstanceValid(state)){state.Text="实际候选音轨播放结束；尚未评价理解。";_audioPositions[material.Id]=0;}};
                var timer=new Godot.Timer{WaitTime=.25,Autostart=true};flow.AddChild(timer);timer.Timeout+=()=>{if(IsInstanceValid(player)&&player.Playing&&IsInstanceValid(progress))progress.Text=$"实际位置 {player.GetPlaybackPosition():F1} / {player.Stream!.GetLength():F1} 秒";};
            }
        }
        Button(flow,"查看原文脚本 / 字幕",()=>Execute(async()=>
        {
            var current=await permissionQuery();if(current.Id!=material.Id||current.Version!=material.Version||current.Fingerprint!=material.Fingerprint)throw new InvalidOperationException("当前原文许可或版本已改变，请重新打开脚本。");
            subtitles=true;var reader=Panel("脚本："+material.Title,InputOwner.Workspace);
            var text=new TextEdit{Text=material.English,Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,360),AccessibilityName=material.Title+"完整原文脚本"};text.AddThemeFontSizeOverride("font_size",20*_scale/100);reader.AddChild(text);AddRegionalDictionary(reader,text);Button(reader,"查看中文辅助",()=>Execute(async()=>Label(reader,(await permissionQuery()).Chinese,20)));
            Label(reader,"原文脚本按完整段落提供。当前没有经实际对齐的逐句时间码。",18);
        }));
        var notes=new TextEdit{WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,150),AccessibilityName=material.Title+"听力证据笔记"};notes.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(notes);
        Button(flow,"保存当前听力证据笔记",()=>Execute(async()=>
        {
            var current=await permissionQuery();if(current.Id!=material.Id||current.Version!=material.Version||current.Fingerprint!=material.Fingerprint)throw new InvalidOperationException("当前原文许可或版本已改变，笔记保留，请重新打开音轨。");var support=subtitles?material.Paragraphs.Select(p=>new SupportUse("transcript",material.Id,p.Id,0,AssetText.Scalars(p.English))).ToArray():[];
            var observation=new LearningObservation(Guid.NewGuid().ToString(),material.SourceRoot,attempt,available?InputModality.Listening:InputModality.Reading,!string.IsNullOrWhiteSpace(notes.Text),true,available,subtitles,support,material.Version,notes.Text,played,speedValue,replays,pauses);
            var evidence=await _store.ObserveAsync(observation);state.Text="笔记已保存 · "+evidence.State+"；没有能力分数或奖励。";
        }));
    }
}

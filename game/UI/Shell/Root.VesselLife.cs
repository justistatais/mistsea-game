using Godot;
using Mist.Application.UI;
using Mist.Application.VesselLife;
using Mist.Core.VesselLife;

public partial class Root
{
    private VesselLifeService? _vesselLife;private string? _vesselLifeActiveId;private string? _vesselLifeActiveTitle;
    private VesselLifeService VesselLife()
    {
        if(_vessel is null)throw new InvalidOperationException("同一微光号尚未初始化。");
        return _vesselLife??=new(_store,_vessel,VesselLifePackage.ParseJson(Godot.FileAccess.GetFileAsString("res://content/vessel_life_player.json")));
    }
    private void ResetVesselLifeUiState(){_vesselLife=null;_vesselLifeActiveId=null;_vesselLifeActiveTitle=null;}
    private bool CanLeaveVesselLifePanel(string? title)=>_vesselLifeActiveId is null||title!=_vesselLifeActiveTitle;
    private ValueTask<bool> TryPauseVesselLifeBeforeCloseAsync(string? title)=>CanLeaveVesselLifePanel(title)?ValueTask.FromResult(true):PauseVesselLifeAsync();
    private void AddVesselLifeButton(VBoxContainer flow,string spot)
    {if(spot is "HUB-S01.harbor_view" or "HUB-S02.archive" or "HUB-S03.workbench")Button(flow,"船上生活 · 当前人物与话题",()=>Execute(async()=>await OpenVesselLifeList(spot)));}
    private async ValueTask OpenVesselLifeList(string spot)
    {
        var service=VesselLife();var entries=await service.ListAsync();var flow=Panel("船上生活 · 当前可取范围",InputOwner.Workspace);Label(flow,"每次实际登船可开启一段新片段。原场景手动推进，可以暂停后恢复；回看仍核对当前资料范围。没有经验、货币、主线或结局奖励。",20);
        foreach(var e in entries.Where(x=>x.Spot==spot))
        {
            string id=e.Id;Label(flow,e.Title+(e.Completed?" · 已展示":e.Paused?" · 已暂停":""),20);
            if(e.Issue is not null){Label(flow,e.Issue,18);continue;}
            if(e.Completed)Button(flow,"回看 · "+e.Title,()=>Execute(async()=>await OpenVesselLifeReplay(id)));
            else
            {
                Button(flow,"邀请当前人物 · "+e.Title,()=>Execute(async()=>{CheckCommit(await service.InviteAsync(id));_status.Text="本人接受了这个有限话题。尚未展示；请选择开始或恢复。";}));
                Button(flow,(e.Paused?"恢复 · ":"开始 · ")+e.Title,()=>Execute(async()=>{CheckCommit(await service.StartAsync(id));_vesselLifeActiveId=id;await OpenVesselLifeCurrent(id);}));
            }
        }
    }
    private async ValueTask OpenVesselLifeCurrent(string id)
    {
        var view=await VesselLife().CurrentAsync(id);string title="船上生活对话 · "+view.Script.Title;_vesselLifeActiveTitle=title;var flow=Panel(title,InputOwner.Workspace);var back=flow.GetChild<HBoxContainer>(0).GetChild<Button>(1);back.Pressed-=ClosePanel;back.Pressed+=()=>Execute(async()=>{if(!await TryPauseVesselLifeBeforeCloseAsync(title))throw new InvalidOperationException("当前句尚未可靠保存，窗口保留。");ClosePanel();});
        Label(flow,$"第 {view.State.NextLine+1}/{view.Script.Lines.Length} 句 · {view.CurrentLine!.Actor}",18);
        var reader=new TextEdit{Text=view.CurrentLine.English,Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,220),AccessibilityName="船上生活当前获准英文对话"};flow.AddChild(reader);AddRegionalDictionary(flow,reader);Button(flow,"查看这句中文辅助",()=>Label(flow,view.CurrentLine.Chinese,20));
        Button(flow,view.State.NextLine+1==view.Script.Lines.Length?"读完本段并保存一次展示记录":"明确继续下一句",()=>Execute(async()=>
        {
            CheckCommit(await VesselLife().AdvanceAsync(id));_vesselLifeActiveTitle=null;ClosePanel();if(view.State.NextLine+1<view.Script.Lines.Length)await OpenVesselLifeCurrent(id);else{_vesselLifeActiveId=null;_status.Text="这段生活已保存。后果只属于当前小事，没有世界奖励。";await Refresh();}
        }));
        Button(flow,"暂停到当前句并释放人物",()=>Execute(async()=>{if(!await PauseVesselLifeAsync())throw new InvalidOperationException("当前句尚未可靠保存，窗口保留。");ClosePanel();await Refresh();}));Label(flow,"这是书面生活对白。候选没有声明已配音或完成真人语言审校。",18);
    }
    private async ValueTask OpenVesselLifeReplay(string id)
    {
        var script=await VesselLife().ReplayAsync(id);var flow=Panel("船上生活只读回看 · "+script.Title,InputOwner.Workspace);var reader=new TextEdit{Text=string.Join("\n\n",script.Lines.Select(l=>l.Actor+": "+l.English)),Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,330),AccessibilityName="当前授权船上对白只读回看"};flow.AddChild(reader);AddRegionalDictionary(flow,reader);Button(flow,"查看中文辅助",()=>Label(flow,string.Join("\n\n",script.Lines.Select(l=>l.Actor+"："+l.Chinese)),20));Label(flow,"只读回看不会再次取得物件、奖励或事件完成。",18);
    }
    private async ValueTask<IReadOnlyCollection<Mist.Core.Learning.LearningSourceAccess>> ReadVesselLifeLearningSources()
    {
        var sources=new List<Mist.Core.Learning.LearningSourceAccess>();if(_vesselLife is null)return sources;
        foreach(var entry in await _vesselLife.ListAsync())if(entry.Completed&&entry.Issue is null)
        {
            var script=await _vesselLife.ReplayAsync(entry.Id);sources.Add(new(script.Id+":replay",script.Version,LearningBodyHash(string.Join("\n\n",script.Lines.Select(l=>l.Actor+": "+l.English)))));
        }
        if(_vesselLifeActiveId is not null&&_vesselLifeActiveTitle is not null)
        {
            var view=await _vesselLife.CurrentAsync(_vesselLifeActiveId);if(view.CurrentLine is{} line)sources.Add(new(view.Script.Id+":"+line.Id,view.Script.Version,LearningBodyHash(line.English)));
        }
        return sources;
    }
    // Root must await this before generic Escape/close, system save/load/continue, or another foreground.
    private async ValueTask<bool> PauseVesselLifeAsync()
    {
        if(_vesselLife is null||_vesselLifeActiveId is null)return true;
        try{CheckCommit(await _vesselLife.PauseAsync(_vesselLifeActiveId));_vesselLifeActiveId=null;_vesselLifeActiveTitle=null;return true;}
        catch(Exception e){_status.Text="当前句与人物交接未保存，窗口保留，请重试："+e.Message;return false;}
    }
}

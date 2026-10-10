using Godot;
using Mist.Application.UI;
using Mist.Application.Materials;
using Mist.Core.Contracts;
using Mist.Core.Puzzles;
using System.Collections.Immutable;

public partial class Root
{
    private readonly Dictionary<string,EvidenceAnnotation> _annotations=new();
    private ImmutableArray<string> _workSteps=["bracket","battery","test","depart"];
    private readonly Dictionary<string,int> _allocations=new();
    private async void OpenPuzzleWork()
    {
        try
        {
            string key="puzzle-work:"+_store.Session.Branch.Value;string? saved=await _store.GetSettingAsync(key);
            if(saved is not null){var work=StrictJson.Deserialize<LocalPuzzleWork>(saved);_annotations.Clear();foreach(var a in work.Annotations)_annotations[a.Material+":"+a.Paragraph]=a;_workSteps=work.Steps;_allocations.Clear();foreach(var pair in work.Allocations??ImmutableDictionary<string,int>.Empty)_allocations[pair.Key]=pair.Value;}
            var flow=Panel("调查工作台",InputOwner.Puzzle);Label(flow,"标记、排序和分配是你的工作记录。它们不改现场事实，也不产生维修或离港回执。",22);
            Button(flow,"证据比较板",OpenComparison);Button(flow,"维修步骤轨道",OpenStepTrack);Button(flow,"证据地图与标注",OpenEvidenceMap);Button(flow,"设备分配草案",OpenAllocation);
            Button(flow,"查看实体设备",()=>{ClosePanel();_status.Text="返回场景，走近导航灯或控制器后操作实体设备。";});Button(flow,"共用通信草稿",OpenWriter);
            Button(flow,"明确清空本分支工作台笔记",()=>Execute(async()=>{_annotations.Clear();_allocations.Clear();_workSteps=["bracket","battery","test","depart"];await SavePuzzleWork();_status.Text="仅清空工作台笔记，材料、正文和世界结果保留。";}));
        }
        catch(Exception error){_status.Text=error.Message;}
    }
    private sealed record LocalPuzzleWork(ImmutableArray<EvidenceAnnotation> Annotations,ImmutableArray<string> Steps,ImmutableDictionary<string,int>? Allocations=null);
    private async ValueTask SavePuzzleWork()=>await _store.SetSettingAsync("puzzle-work:"+_store.Session.Branch.Value,StrictJson.Serialize(new LocalPuzzleWork(_annotations.Values.ToImmutableArray(),_workSteps,_allocations.ToImmutableDictionary())));
    private void OpenComparison()
    {
        var flow=Panel("证据比较板",InputOwner.Puzzle);Label(flow,"未发现的材料不会出现在板上。支持、矛盾和未说明由你标记；这里不把作者答案显示为事实。",20);
        foreach(var material in MaterialJournal.Visible(_catalog,_view.Snapshot))foreach(var paragraph in material.Paragraphs)
        {
            Label(flow,material.Title+" · "+paragraph.Id,22);Label(flow,paragraph.Text,20);string key=material.Id+":"+paragraph.Id;
            var option=new OptionButton { FitToLongestItem=false };foreach(string value in new[]{"尚未判断","支持","矛盾","材料未说明","需要核对"})option.AddItem(value);
            var existing=_annotations.GetValueOrDefault(key);option.Select((int)(existing?.Mark??EvidenceMark.Unknown));flow.AddChild(option);
            var note=new LineEdit { Text=existing?.Note??"",PlaceholderText="记录适用对象、范围或待核对之处" };flow.AddChild(note);
            option.ItemSelected+=index=>_annotations[key]=new(material.Id,paragraph.Id,(EvidenceMark)index,note.Text);note.TextChanged+=text=>_annotations[key]=new(material.Id,paragraph.Id,(EvidenceMark)option.Selected,text);
        }
        Button(flow,"保存比较笔记",()=>Execute(SavePuzzleWork));
    }
    private void OpenStepTrack()
    {
        var flow=Panel("维修步骤轨道",InputOwner.Puzzle);
        foreach(var entry in _workSteps.Select((id,index)=>(id,index)))
        {
            int index=entry.index;var row=new HFlowContainer();flow.AddChild(row);Label(row,StepName(entry.id),22).CustomMinimumSize=new(280,40);
            if(index>0)Button(row,"向前",()=>{_workSteps=PuzzleWork.Move(_workSteps,index,-1);ClosePanel();OpenStepTrack();});if(index<_workSteps.Length-1)Button(row,"向后",()=>{_workSteps=PuzzleWork.Move(_workSteps,index,1);ClosePanel();OpenStepTrack();});
        }
        Button(flow,"检查公开步骤关系",()=>
        {
            var issues=PuzzleWork.CheckOrder(_workSteps,[new("bracket","test"),new("battery","test"),new("test","depart")]);Label(flow,issues.Length==0?"候选顺序满足这些先后关系。实体设备仍需逐项实际准备。":"顺序仍有冲突："+string.Join("；",issues),20);
        });Button(flow,"保存这份排序",()=>Execute(SavePuzzleWork));
    }
    private static string StepName(string id)=>id switch {"bracket"=>"安装替换支架","battery"=>"连接船内备用电池","test"=>"测试当前配置","depart"=>"明确执行撤离",_=>id};
    private async void OpenEvidenceMap()
    {
        try
        {
            var flow=Panel("证据地图与标注",InputOwner.Puzzle);Label(flow,_world.SceneTitle+" · 当前合法出口",24);
            foreach(var port in ActiveGeometry[_view.Actor.Scene].Ports)Label(flow,port.TargetScene+" · "+((_chapter is null?_q.PortIssue(_view,port):_chapter.PortIssue(_view.Snapshot,port))??"当前通路可用"),20);
            string key="map-note:"+_store.Session.Branch.Value+":"+_view.Actor.Scene;var note=new TextEdit { Text=await _store.GetSettingAsync(key) is { } saved?StrictJson.Deserialize<string>(saved):"",CustomMinimumSize=new(0,180),WrapMode=TextEdit.LineWrappingMode.Boundary,AccessibilityName="本场景假设与标注" };flow.AddChild(note);
            Button(flow,"保存本场景标注",()=>Execute(async()=>await _store.SetSettingAsync(key,StrictJson.Serialize(note.Text))));
        }
        catch(Exception error){_status.Text=error.Message;}
    }
    private void OpenAllocation()
    {
        var flow=Panel("设备分配草案",InputOwner.Puzzle);Label(flow,"只为已有设备写分配提议。不会移交实物；同一实例的当前持有人由存档显示。",22);
        foreach(var item in _view.Snapshot.Items)
        {
            Label(flow,item.DefinitionId+" · 当前 "+item.OwnerKind+" / "+item.OwnerId,20);var destination=new OptionButton { FitToLongestItem=false };foreach(string value in new[]{"尚未提出分配","保留当前归属","提议用于民用渡轮"})destination.AddItem(value);destination.Select(_allocations.GetValueOrDefault(item.Id));destination.ItemSelected+=index=>_allocations[item.Id]=(int)index;flow.AddChild(destination);
        }
        Button(flow,"保存分配草案",()=>Execute(SavePuzzleWork));
        Label(flow,"这份草案不代替取得支架、安装、连接、测试或行动确认。",18);
    }
}

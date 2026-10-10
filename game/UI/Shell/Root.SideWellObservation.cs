using System.Collections.Immutable;
using Godot;
using Mist.Application.Drafts;
using Mist.Application.OptionalLocalWorks;
using Mist.Application.UI;

public partial class Root
{
    private SideWellObservationService? _sideWell;
    private DraftEditor? _sideWellEditor;private TextEdit? _sideWellWriter;private Label? _sideWellSaveStatus;private Godot.Timer? _sideWellTimer;private Task<bool>? _sideWellSaveTask;
    private const string SideWellWriterTitle="侧井观察比较便条";
    private readonly Dictionary<string,Stack<Guid>> _sideWellUiPreviews=[];
    private void ResetSideWellObservationUi()
    {foreach(var stack in _sideWellUiPreviews.Values)foreach(var token in stack)_sideWell?.ClosePreview(token);_sideWellUiPreviews.Clear();_sideWellEditor=null;_sideWellWriter=null;_sideWellSaveStatus=null;_sideWellTimer=null;_sideWellSaveTask=null;_sideWell=null;}
    private void CloseSideWellObservationPreview(string title)
    {if(_sideWellUiPreviews.TryGetValue(title,out var stack)&&stack.TryPop(out var token))_sideWell?.ClosePreview(token);if(title==SideWellWriterTitle){_sideWellWriter=null;_sideWellSaveStatus=null;_sideWellTimer=null;_=SaveSideWellObservationDraft();}}
    private SideWellObservationService SideWellService()=>_sideWell??=new(_store,_catalog,_chapterThree?.Package??throw new InvalidOperationException("当前矿区现场未加载。"));
    // Return null for other ports so shared dispatch can keep its existing contracts.
    private string? SideWellPortIssue(Mist.Application.Storage.BranchSnapshot snapshot,Mist.Core.Exploration.Port port)=>SideWellObservationService.OwnPort(port.Id)&&_chapterThree is not null?SideWellService().PortIssue(snapshot,port):null;
    private async ValueTask<bool> TrySideWellPortInteraction(string port)
    {
        if(!SideWellObservationService.OwnPort(port)||_chapterThree is null)return false;var service=SideWellService();var snapshot=await _store.SnapshotAsync();var actual=_chapterThree.CurrentGeometry(snapshot).Values.SelectMany(g=>g.Ports).Single(p=>p.Id==port);var flow=Panel("侧井配对入口核对",InputOwner.Workspace);Label(flow,"这是公开可选观察入口。需要当前真实防护、已处置轨道、持续独立梯道和两端标记站位；仅允许观察，不授予追加取样或主线证据。",22);Label(flow,service.PortIssue(snapshot,actual)??"这条双向连接当前已由实际安全来源登记。",20);
        Button(flow,"在当前出口实际检查观察点与独立返程",()=>Execute(async()=>{CheckCommit(await service.InspectEntryAsync());ClosePanel();await Refresh();}));Button(flow,"明确登记刚核对的当前双向安全连接",()=>Execute(async()=>{CheckCommit(await service.RegisterEntrySafetyAsync());ClosePanel();await Refresh();}));Button(flow,"实际通过这一配对口并登记已走通路径",()=>Execute(async()=>{var result=await service.EnterPortAsync(port);CheckCommit(result);if(result.Receipt is not null)CheckCommit(await service.RecordWalkedRouteAsync(result.Receipt.Id.Value.ToString()));ClosePanel();await Refresh();}));return true;
    }
    private async ValueTask<bool> TrySideWellObservationInteraction(string spot)
    {
        if(!spot.StartsWith("R3-O02.",StringComparison.Ordinal)||_chapterThree is null)return false;var service=SideWellService();var flow=Panel("侧井标记观察站位",InputOwner.Workspace);Label(flow,"此处观察资料不是受控试验。条件未知时保留未知，必要主线样本仍在主路。比较便条只留在本地玩家记录，不重复领经验或工程物资。",22);
        if(spot=="R3-O02.record")
        {
            Button(flow,"实际取得此站位公开的原观察页",()=>Execute(async()=>{CheckCommit(await service.DiscoverObservationAsync());ClosePanel();await Refresh();}));Button(flow,"打开已经取得的侧井原记录",()=>Execute(async()=>{var material=await service.OpenObservationAsync();var reader=Panel(material.Title,InputOwner.Workspace);var text=new TextEdit{Text=material.English,Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,280),AccessibilityName=material.Title+"英文原文"};reader.AddChild(text);AddRegionalDictionary(reader,text);Button(reader,"显示此原文的中文辅助",()=>Execute(async()=>{var current=await service.OpenObservationAsync();foreach(var p in current.Paragraphs)Label(reader,p.Chinese,20);}));}));
        }
        else if(spot=="R3-O02.sample")Label(flow,"原始标牌要求留在标记观察点。当前没有另行签发的安全采样委托，因此这里不生产新样本。",22);
        else if(spot=="R3-O02.side_route")
        {Button(flow,"编辑并保存同一份侧井比较便条",()=>Execute(OpenSideWellWriter));Button(flow,"逐项核对当前便条意思",()=>Execute(async()=>await OpenSideWellMeaning(false)));Button(flow,"明确使用结构化辅助",()=>Execute(async()=>await OpenSideWellMeaning(true)));Button(flow,"查看当前比较便条登记清单",()=>Execute(OpenSideWellComparison));}return true;
    }
    private async ValueTask OpenSideWellWriter()
    {
        var service=SideWellService();if(!await SaveSideWellObservationDraft())throw new InvalidOperationException("请先完成输入法候选并保存便条。");if(_routing.Contains(SideWellWriterTitle)){while(_routing.Top!=SideWellWriterTitle)ClosePanel();_sideWellWriter?.GrabFocus();return;}if(_sideWellEditor is null){var d=await _store.GetDraftAsync(SideWellObservationService.Task);var ses=_store.Session;_sideWellEditor=new(_store,new(ses.Profile,ses.Branch,ses.Load,SideWellObservationService.Task,d?.Id,d?.Revision??0,d?.Body??"",service.Observation.Version));}var editor=_sideWellEditor!;var flow=Panel(SideWellWriterTitle,InputOwner.Editable);Label(flow,service.WritingTask.EnglishPrompt,22);Button(flow,"显示当前写作中文辅助",()=>Label(flow,service.WritingTask.ChinesePrompt,20));Label(flow,"离线模式不自动理解英文；保存正文不会登记世界结果。对未知泵状态、仪表条件和阶段保持原记录范围。",18);var writer=new TextEdit{Text=editor.Text,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,330),AccessibilityName="侧井英文观察比较便条"};flow.AddChild(writer);_sideWellWriter=writer;_sideWellSaveStatus=Label(flow,editor.IsSaved?"稿件与实际保存版本一致。":"稿件待保存。",18);var timer=new Godot.Timer{OneShot=true,WaitTime=.5};flow.AddChild(timer);_sideWellTimer=timer;timer.Timeout+=()=>{if(Composing()){timer.Start(.2);return;}_=SaveSideWellObservationDraft();};writer.TextChanged+=()=>{editor.Edit(writer.Text);foreach(var stack in _sideWellUiPreviews.Values)foreach(var token in stack)service.ClosePreview(token);_sideWellUiPreviews.Clear();if(_sideWellSaveStatus is{} label&&IsInstanceValid(label))label.Text="当前便条有未保存修改。";timer.Start();};writer.FocusExited+=()=>{foreach(var key in _keys.Values.Append(Key.Enter))if(Input.IsPhysicalKeyPressed(key))_releaseBarrier.Add(key);};var back=flow.GetChild<HBoxContainer>(0).GetChild<Button>(1);back.Pressed-=ClosePanel;back.Pressed+=()=>Execute(async()=>{if(!await SaveSideWellObservationDraft())throw new InvalidOperationException("当前便条未保存，可先完成输入法候选或导出。");ClosePanel();});Button(flow,"保存侧井比较便条",()=>Execute(async()=>{if(!await SaveSideWellObservationDraft())throw new InvalidOperationException("便条尚未保存完成。");}));Button(flow,"导出当前比较便条",()=>{string dir=ProjectSettings.GlobalizePath("user://exports");Directory.CreateDirectory(dir);string path=Path.Combine(dir,"sidewell-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+".txt");File.WriteAllText(path,editor.Text,new System.Text.UTF8Encoding(false));_status.Text="当前比较便条已导出："+path;});writer.GrabFocus();
    }
    private async ValueTask<bool> SaveSideWellObservationDraft()
    {if(_sideWellEditor is null)return true;if(Composing())return false;if(_sideWellSaveTask is not null&&!_sideWellSaveTask.IsCompleted)return await _sideWellSaveTask;_sideWellSaveTask=SaveSideWellDraftCore();return await _sideWellSaveTask;}
    private async Task<bool> SaveSideWellDraftCore()
    {try{if(_sideWellEditor is{} editor&&!editor.IsSaved)await editor.SaveAsync();bool saved=_sideWellEditor?.IsSaved??true;if(_sideWellSaveStatus is{} label&&IsInstanceValid(label))label.Text=saved?"比较便条已由真实 CAS 保存回执确认。":"当前改动尚未全数保存。";return saved;}catch(Exception error){if(_sideWellSaveStatus is{} label&&IsInstanceValid(label))label.Text="保存失败，便条仍可导出："+error.Message;_status.Text="侧井便条保存失败："+error.Message;return false;}}
    private async ValueTask OpenSideWellMeaning(bool assisted)
    {
        var service=SideWellService();if(!await SaveSideWellObservationDraft())throw new InvalidOperationException("请先完成输入法候选并保存正文。");var flow=Panel("侧井比较便条意思核对",InputOwner.Confirmation);Label(flow,assisted?"这是明确结构化辅助，不计作独立理解英文或语言成绩。":"当前不自动理解英文，请逐项确认当前稿件意思。",20);(string Key,string Name,string[] Labels,string[] Values)[] rows=[
            ("side","侧井页所载读数",["尚未选择","一单位抽取，读数五","一单位抽取，读数四"],["","one_load_five_reading","one_load_four_reading"]),
            ("main","主采点页所载读数",["尚未选择","一单位抽取，读数四","一单位抽取，读数五"],["","one_load_four_reading","one_load_five_reading"]),
            ("comparison","比较条件",["尚未选择","两处地点与条件不同","相同抽取即受控试验"],["","different_locations_conditions","controlled_experiment"]),
            ("unknown_conditions","未记明的条件",["尚未选择","保留未知，不抄主采点填空","复制主采点泵状态"],["","unknown_not_copied","copied_from_main"]),
            ("causality","这次记录支持的结论",["尚未选择","最终因果未证实","已经证实雾的唯一成因"],["","not_proven","mist_cause_proven"]),
            ("next_check","下一次核验应记录",["尚未选择","仪表、泵状态与阶段","只看一个读数"],["","record_instrument_pump_stage","one_number_only"]),
            ("sampling","追加取样权限",["尚未选择","当前没有另行安全采样委托","直接领取追加样本"],["","no_additional_assignment","sample_granted"])
        ];var options=new List<OptionButton>();foreach(var row in rows){Label(flow,row.Name,20);var option=new OptionButton{AccessibilityName=row.Name};foreach(string label in row.Labels)option.AddItem(label);flow.AddChild(option);options.Add(option);}Button(flow,"明确记录当前比较便条的所选意思",()=>Execute(async()=>{if(!await SaveSideWellObservationDraft())throw new InvalidOperationException("当前正文未保存。");if(options.Any(o=>o.Selected==0))throw new InvalidOperationException("请逐项明确选择，不填入默认答案。");CheckCommit(await service.ConfirmMeaningAsync(rows.Select((r,i)=>new KeyValuePair<string,string>(r.Key,r.Values[options[i].Selected])).ToImmutableDictionary(),assisted));ClosePanel();await Refresh();}));
    }
    private async ValueTask OpenSideWellComparison()
    {
        var service=SideWellService();if(!await SaveSideWellObservationDraft())throw new InvalidOperationException("请先完成输入法候选并保存正文。");var p=await service.PreviewComparisonAsync();const string title="侧井比较便条登记预览";var flow=Panel(title,InputOwner.Confirmation);if(!p.Ready){foreach(var issue in p.Issues)Label(flow,issue.SafeMessage,20);return;}if(!_sideWellUiPreviews.TryGetValue(title,out var stack)){stack=new();_sideWellUiPreviews[title]=stack;}stack.Push(p.Token);Label(flow,"核对当前同版原材料、真实配对到场、安全观察范围及同一保存稿件。确认后保留本地比较便条；没有额外样本、奖励、主线因果结论或结局权限。",22);Button(flow,"明确登记当前保存稿件的有限比较",()=>Execute(async()=>{CheckCommit(await service.ConfirmPreviewAsync(p.Token));ClosePanel();await Refresh();}));
    }
}

using Godot;
using Mist.Application.OptionalLocalWorks;
using Mist.Application.UI;
using Mist.Application.Contracts;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.OptionalLocalWorks;
using Mist.Core.Quests;
using Mist.Core.ReleaseReadiness;

public partial class Root
{
    private FieldRelayRepairService? _fieldRelayRepair;
    private bool _fieldRelaySelected;
    private void ResetFieldRelayRepairUi(){_fieldRelayRepair=null;_fieldRelaySelected=false;}
    private FieldRelayRepairService FieldRelayService()
    {
        if(_fieldRelayRepair is not null)return _fieldRelayRepair;
        using var file=Godot.FileAccess.Open("res://content/field_relay_contract.json",Godot.FileAccess.ModeFlags.Read)??throw new InvalidDataException("当前导出包缺少辅助继电原说明。");
        return _fieldRelayRepair=new(_store,_catalog,FieldRelayContract.ParseJson(file.GetAsText()),_chapterSix??throw new InvalidOperationException("当前边境服务未加载。"));
    }
    private bool FieldRelayEncounterSelected
    {
        get
        {
            if(_chapterSix is null||_view is null||_view.Actor.Scene!="R6-S06"){_fieldRelaySelected=false;return false;}
            bool active=_view is not null&&_view.Snapshot.Entities.SingleOrDefault(e=>e.Id==FieldRelayRepairService.CombatId) is{} entity&&StrictJson.Deserialize<FieldRelayEncounter>(entity.Json).Battle.Outcome is CombatOutcome.Active or CombatOutcome.Defeat;
            if(!_fieldRelaySelected&&!active)return false;FieldRelayService();return true;
        }
    }
    private async ValueTask<CommandResult> WithdrawFieldRelayEncounterAsync(){var result=await FieldRelayService().WithdrawEncounterAsync();if(result.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted)_fieldRelaySelected=false;return result;}
    private async ValueTask<CommandResult> RetryFieldRelayEncounterAsync(){CheckCommit(await FieldRelayService().WithdrawEncounterAsync());var result=await FieldRelayService().BeginEncounterAsync();if(result.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted)_fieldRelaySelected=true;return result;}
    private async ValueTask<bool> TryFieldRelayRepairInteraction(string spot)
    {
        if(spot!="R6-S06.repair"||_chapterSix is null)return false;
        var actual=await _store.SnapshotAsync();var quest=actual.Entities.SingleOrDefault(e=>e.Id=="quest:Q-603") is{} entity?StrictJson.Deserialize<QuestState>(entity.Json):null;var source=actual.Receipts.SingleOrDefault(r=>r.SemanticKey=="Q-603:resolution"&&r.ActionType=="main_quest_resolution");
        if(quest is not{Lifecycle:QuestLifecycle.Closed,Outcome:QuestOutcome.Completed}||quest.Instance==Guid.Empty||source is null)return false;
        var resolution=StrictJson.Deserialize<MainQuestResolution>(source.ResultJson);if(resolution.Quest!="Q-603"||resolution.Instance!=quest.Instance||resolution.Outcome!="completed")return false;
        var service=FieldRelayService();var view=await service.ViewAsync();var st=view.State;if((await service.EncounterAsync())?.Battle.Outcome is not(CombatOutcome.Active or CombatOutcome.Defeat))_fieldRelaySelected=false;var flow=Panel("外线辅助继电现场检修",InputOwner.Workspace);Label(flow,"这是可拒绝的同机辅助检修。受保护Q603交付须已真实闭合；本地设备与专用销不归入玩家物资、主塔基础或五箱补充。没有额外经验、奖励或起航资格。",22);
        Button(flow,"实际取得此站位的辅助检修原说明",()=>Execute(async()=>{CheckCommit(await service.DiscoverContractAsync());ClosePanel();await OpenFieldRelayContract();}));if(view.SourceAcquired)Button(flow,"打开已取得的辅助检修原说明",()=>Execute(OpenFieldRelayContract));if(st?.Completed==true){Label(flow,"同一设备当前维修与本地返路回执已登记，不重新结算。",20);return true;}if(st is not{ScopeDecision:"accept"})
        {Button(flow,"明确承接原说明限定的同机旁修",()=>Execute(async()=>{CheckCommit(await service.DecideScopeAsync("accept"));ClosePanel();await Refresh();}));Button(flow,"明确拒绝这项追加旁修",()=>Execute(async()=>{CheckCommit(await service.DecideScopeAsync("decline"));ClosePanel();await Refresh();}));return true;}
        Label(flow,$"当前同机配置 {st.Configuration}；AUX {view.Gauge!.ActualReading}；REF 10；专用销固定 {view.Gauge.TrackFixed}；威胁制止 {st.ThreatControlled}；同机复位 {st.Repaired}。",20);
        var position=new OptionButton{AccessibilityName="辅助输入明确档位"};foreach(string x in new[]{"尚未选择","OFF · 隔离辅助输入","LIVE · 原带电档位"})position.AddItem(x);flow.AddChild(position);Button(flow,"明确设置本台辅助输入开关",()=>Execute(async()=>{if(position.Selected==0)throw new InvalidOperationException("请明确选择实际辅助档位。");CheckCommit(await service.SetAuxSwitchAsync(position.Selected==1?"OFF":"LIVE"));ClosePanel();await Refresh();}));Button(flow,"实际安装同机专用制动销",()=>Execute(async()=>{CheckCommit(await service.FitDedicatedPinAsync(service.Contract.PinSerial));ClosePanel();await Refresh();}));var channel=new OptionButton{AccessibilityName="零能核验实际通道"};foreach(string x in new[]{"尚未选择","AUX · 当前辅助通道","REF · 参考通道"})channel.AddItem(x);flow.AddChild(channel);var reading=new SpinBox{MinValue=0,MaxValue=100,Step=1,AccessibilityName="当前通道实际读数"};flow.AddChild(reading);Button(flow,"核对所选通道与当前配置零能",()=>Execute(async()=>{if(channel.Selected==0)throw new InvalidOperationException("请明确核对当前实际通道。");CheckCommit(await service.VerifyZeroAsync(channel.Selected==1?"AUX":"REF",(int)reading.Value,st.Configuration));ClosePanel();await Refresh();}));
        if(!st.ThreatControlled)Button(flow,"明确选择制止本台机卫的战斗尝试",()=>Execute(async()=>{CheckCommit(await service.BeginEncounterAsync());_fieldRelaySelected=true;CloseAllPanels();await Refresh();await OpenBattle();}));Button(flow,"实际复位原同一继电器",()=>Execute(async()=>{CheckCommit(await service.RefitSameRelayAsync(service.Contract.RelaySerial));ClosePanel();await Refresh();}));Button(flow,"检查当前同机的本地返路回路",()=>Execute(async()=>{CheckCommit(await service.TestReturnAsync(st.Configuration));ClosePanel();await Refresh();}));Button(flow,"明确登记当前同机维修及返路回执",()=>Execute(async()=>{CheckCommit(await service.CompleteAsync());ClosePanel();await Refresh();}));return true;
    }
    private async ValueTask OpenFieldRelayContract()
    {var source=await FieldRelayService().OpenContractAsync();var flow=Panel("辅助继电同机检修原说明",InputOwner.Workspace);var reader=new TextEdit{Text=string.Join("\n\n",source.Paragraphs.Select(p=>p.English)),Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,420),AccessibilityName="当前取得的辅助继电完整英文原说明"};flow.AddChild(reader);AddRegionalDictionary(flow,reader);Button(flow,"显示这一原说明的中文辅助",()=>{foreach(var p in source.Paragraphs)Label(flow,p.Chinese,20);});Label(flow,"此为独立新局部候选，原正文与音轨不变，尚未经人工语言评阅。本次读取是书面原说明。",18);}
}

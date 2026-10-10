using Godot;
using Mist.Application.OptionalLocalWorks;
using Mist.Application.UI;

public partial class Root
{
    private TessLocalReceiptBridge? _tessReceiptBridge;
    private readonly Dictionary<string,Stack<Guid>> _tessReceiptUiPreviews=[];
    private void ResetTessLocalReceiptBridgeUi()
    {foreach(var stack in _tessReceiptUiPreviews.Values)foreach(var token in stack)_tessReceiptBridge?.ClosePreview(token);_tessReceiptUiPreviews.Clear();_tessReceiptBridge=null;}
    private void CloseTessLocalReceiptBridgePreview(string title)
    {if(_tessReceiptUiPreviews.TryGetValue(title,out var stack)&&stack.TryPop(out var token))_tessReceiptBridge?.ClosePreview(token);}
    // Root calls this on the existing station panel; opening it never accepts or completes a quest.
    private void AddTessLocalReceiptBridgeButton(VBoxContainer flow,string spot)
    {if(_chapterSix is null)return;string? node=spot switch{"R6-S05.contacts"=>"CQ-TESS-01","R6-S05.supplement"=>"CQ-TESS-02","R6-S08.local_notice"=>"CQ-TESS-03",_=>null};if(node is not null)Button(flow,"核对已提交的泰丝个人结果并登记人物记录",()=>Execute(async()=>await OpenTessLocalReceiptBridge(node)));}
    private async ValueTask OpenTessLocalReceiptBridge(string node)
    {
        if(_chapterSix is null)return;_tessReceiptBridge??=new(_store,_catalog,_chapterSix.Package);var service=_tessReceiptBridge;var preview=await service.PreviewRecordAsync(node);string title="泰丝已有回执核对 · "+node;var flow=Panel(title,InputOwner.Confirmation);Label(flow,"先由原章节服务接收正文并按原预算结算。这里只将已成立的许可、实际运输或地方通知登记到人物记录；不会再领货、重跑运输或重复奖励。",22);
        if(!preview.Ready){foreach(var issue in preview.Issues)Label(flow,issue.SafeMessage,20);return;}var proof=preview.Proof!;Label(flow,"实际收束："+proof.Outcome+"\n节点实例："+proof.NodeInstance+"\n原正式收件："+proof.ChapterResolution,20);Label(flow,proof.SegmentInstance is null?"本项没有声称完成补充运输。":"引用同一 PZ-Q603-LOAD 原计划和两趟实际到货；原五箱保持当前接收归属。",20);Label(flow,"这一登记不签地区协议，不增加结局资格；语言质量仍待人工评阅。",18);if(preview.Recorded){Label(flow,"已有同一来源的人物记录，不能重复结算。",20);return;}if(!_tessReceiptUiPreviews.TryGetValue(title,out var stack)){stack=new();_tessReceiptUiPreviews[title]=stack;}stack.Push(preview.Token);Button(flow,"明确登记这些已有实际回执",()=>Execute(async()=>{CheckCommit(await service.ConfirmRecordAsync(preview.Token));ClosePanel();await Refresh();}));
    }
}

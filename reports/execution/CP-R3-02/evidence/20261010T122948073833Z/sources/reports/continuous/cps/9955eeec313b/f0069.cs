using Godot;
using Mist.Application.Drafts;
using Mist.Application.R3ShiftWork;
using Mist.Application.UI;
using Mist.Core.R3ShiftWork;

public partial class Root
{
    private R3ShiftWorkService? _r3Shift;private DraftEditor? _r3ShiftEditor;private TextEdit? _r3ShiftWriter;private Label? _r3ShiftSaveStatus;private Task<bool>? _r3ShiftSaveTask;private readonly Dictionary<string,Stack<Guid>> _r3ShiftPreviews=new();
    private const string R3ShiftWriterTitle="换班的人 · 英文交接便条";
    private R3ShiftWorkService R3Shift()
    {if(_chapterThree is null)throw new InvalidOperationException("矿区当前正常接入缺失。");return _r3Shift??=new(_store,_chapterThree,R3ShiftPackage.ParseJson(Godot.FileAccess.GetFileAsString("res://content/r3_shift_work/learner_package.json")));}
    private void ResetR3ShiftUiState(){foreach(var stack in _r3ShiftPreviews.Values)foreach(var token in stack)_r3Shift?.ClosePreview(token);_r3ShiftPreviews.Clear();_r3Shift=null;_r3ShiftEditor=null;_r3ShiftWriter=null;_r3ShiftSaveStatus=null;_r3ShiftSaveTask=null;}
    private void CloseR3ShiftUiPreview(string title){if(_r3ShiftPreviews.TryGetValue(title,out var stack)&&stack.TryPop(out var token))_r3Shift?.ClosePreview(token);if(title==R3ShiftWriterTitle){_r3ShiftWriter=null;_r3ShiftSaveStatus=null;}}
    private void AddR3ShiftButton(VBoxContainer flow,string spot)
    {if(spot is "R3-S08.demand_board" or "R3-S08.shift_duty" or "R3-S08.auxiliary_receipt")Button(flow,"换班的人 · 有限公共台账工作",()=>Execute(async()=>await OpenR3ShiftWork(spot)));}
    private void ShiftButton(VBoxContainer flow,string text,Func<ValueTask> action)=>Button(flow,text,()=>Execute(async()=>{await action();ClosePanel();await Refresh();}));
    private async ValueTask OpenR3ShiftWork(string spot)
    {
        var service=R3Shift();var v=await service.ViewAsync();var flow=Panel("换班的人 · "+spot,InputOwner.Workspace);Label(flow,"本班 R3-WATCH-S1 的住区维护台。原口头估计保留；本人实际可用时间分别询问。此工作不安排矿泵、塔站或下一班，没有奖励。",20);Label(flow,v.State is null?"尚未领取有限工作。":$"同板领取 {v.State.BoardTaken} · 实际贴出 {v.State.Posted} · 上午接收 {v.State.MorningReceived} · 未完项 {v.State.HandoverChecked} · 下午接收 {v.State.AfternoonReceived} · 有限收束 {v.State.Completed}",18);
        foreach(var m in service.Package.Materials.Where(m=>m.Spot==spot&&m.ResponseActor==""))
        {string id=m.Id;Button(flow,(v.Materials.Any(x=>x.Id==id)?"查看原件 · ":"实际取得原件 · ")+m.Title,()=>Execute(async()=>{if(!v.Materials.Any(x=>x.Id==id))CheckCommit(await service.DiscoverAsync(id));await OpenR3ShiftMaterial(id);}));}
        if(v.State is not null)
        {
            var person=service.Package.Staff.Single(x=>x.Spot==spot);ShiftButton(flow,"当面询问 · "+person.Name,async()=>CheckCommit(await service.RequestAvailabilityAsync(person.Actor)));if(v.Materials.Any(x=>x.Id==person.Response))Button(flow,"查看本人已取得应答 · "+person.Name,()=>Execute(async()=>await OpenR3ShiftMaterial(person.Response)));
            if(v.State.Posted&&!v.State.Completed)ShiftButton(flow,"由本人接收本时段同一账板 · "+person.Name,async()=>CheckCommit(await service.ReceiveShiftAsync(person.Actor,person.Window)));
        }
        if(spot==service.Package.BoardSpot)
        {
            ShiftButton(flow,"领取本班有限公共工作",async()=>CheckCommit(await service.AcceptWorkAsync()));ShiftButton(flow,"实际取同一账板",async()=>CheckCommit(await service.TakeBoardAsync()));Button(flow,"编辑当前英文交接便条",()=>Execute(OpenR3ShiftWriter));Button(flow,"逐项明确当前排班意思",()=>Execute(async()=>await OpenR3ShiftMeaning(false)));Button(flow,"明确使用结构化辅助",()=>Execute(async()=>await OpenR3ShiftMeaning(true)));Button(flow,"预览保留原承诺的本班贴出",()=>Execute(OpenR3ShiftPostPreview));
        }
        if(spot==service.Package.HandoverSpot)
        {
            var unfinished=new OptionButton{AccessibilityName="未完检查明确范围"};foreach(string text in new[]{"尚未确认","已核公共账，待查损坏件标在同一板上","删除旧估计并承诺下一班"})unfinished.AddItem(text);flow.AddChild(unfinished);ShiftButton(flow,"实际标未完项并核同板交接",async()=>{if(unfinished.Selected!=1)throw new InvalidOperationException("请明确核对本次实际未完项。");CheckCommit(await service.CheckHandoverAsync("ledger_checked_fittings_pending_marked"));});
        }
        if(spot==service.Package.ReceiptSpot)ShiftButton(flow,"接收本班实际两段交接并释放岗位",async()=>CheckCommit(await service.CompleteAsync()));
        Button(flow,"查看已取得本班档案",()=>Execute(OpenR3ShiftArchive));for(int n=0;n<service.Package.Hints.Length;n++){int level=n;Button(flow,"查看本班提示 "+(n+1),()=>Label(flow,service.Package.Hints[level],20));}
    }
    private async ValueTask OpenR3ShiftMaterial(string id)
    {var m=await R3Shift().OpenMaterialAsync(id);var flow=Panel("换班的人原件 · "+m.Title,InputOwner.Workspace);var reader=new TextEdit{Text=m.English,Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,310),AccessibilityName="换班当前获准公共英文原件"};flow.AddChild(reader);AddRegionalDictionary(flow,reader);Button(flow,"查看中文辅助",()=>Label(flow,m.Chinese,20));Button(flow,"编辑对应交接便条",()=>Execute(OpenR3ShiftWriter));Label(flow,"书面原件与当前本人应答分别登记。家庭具体原因未取得；文本不是已播放音轨。",18);}
    private async ValueTask<bool> HasR3ShiftArchive()=>_chapterThree is not null&&(await R3Shift().ViewAsync()).Materials.Length>0;
    private async ValueTask OpenR3ShiftArchive(){var v=await R3Shift().ViewAsync();var flow=Panel("换班的人当前档案",InputOwner.Workspace);foreach(var m in v.Materials){string id=m.Id;Button(flow,m.Title,()=>Execute(async()=>await OpenR3ShiftMaterial(id)));}Button(flow,"编辑当前英文交接便条",()=>Execute(OpenR3ShiftWriter));}
    private void SyncR3ShiftWriter(){if(_r3ShiftEditor is not null&&_r3ShiftWriter is{} w&&IsInstanceValid(w)&&w.Text!=_r3ShiftEditor.Text)_r3ShiftEditor.Edit(w.Text);}
    private async ValueTask<bool> SaveR3ShiftDraft()
    {
        if(Composing())return false;SyncR3ShiftWriter();if(_r3ShiftSaveTask is{} pending){bool saved=await pending;SyncR3ShiftWriter();return saved&&!Composing()&&(_r3ShiftEditor?.IsSaved??true);}if(_r3ShiftEditor is null||_r3ShiftEditor.IsSaved)return true;_r3ShiftSaveTask=SaveCore();try{return await _r3ShiftSaveTask;}finally{_r3ShiftSaveTask=null;}
        async Task<bool> SaveCore(){try{await _r3ShiftEditor!.SaveAsync();SyncR3ShiftWriter();bool saved=!Composing()&&_r3ShiftEditor.IsSaved;if(_r3ShiftSaveStatus is{} label&&IsInstanceValid(label))label.Text=saved?"当前交接便条已保存。":"保存后仍有新修改，窗口保留。";return saved;}catch(Exception e){if(_r3ShiftSaveStatus is{} label&&IsInstanceValid(label))label.Text="保存失败，可重试或导出："+e.Message;return false;}}
    }
    private async ValueTask OpenR3ShiftWriter()
    {
        var service=R3Shift();if(!await SaveR3ShiftDraft())throw new InvalidOperationException("请先完成输入法组合并保存，或导出当前便条。");if(_routing.Contains(R3ShiftWriterTitle)){while(_routing.Top!=R3ShiftWriterTitle)ClosePanel();_r3ShiftWriter?.GrabFocus();return;}if(_r3ShiftEditor is null){var d=await _store.GetDraftAsync(service.Package.DraftTask);var c=_store.Session;_r3ShiftEditor=new(_store,new(c.Profile,c.Branch,c.Load,service.Package.DraftTask,d?.Id,d?.Revision??0,d?.Body??"",service.Package.Version));}
        var editor=_r3ShiftEditor!;var flow=Panel(R3ShiftWriterTitle,InputOwner.Editable);Label(flow,service.Package.PromptEnglish,22);Button(flow,"查看写作中文辅助",()=>Label(flow,service.Package.PromptChinese,20));Label(flow,"自由正文与明确意思分开。保存不贴出、不交班、不认证英文；没有默认正确人员。",18);var writer=new TextEdit{Text=editor.Text,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,320),AccessibilityName="换班英文交接便条"};flow.AddChild(writer);_r3ShiftWriter=writer;_r3ShiftSaveStatus=Label(flow,editor.IsSaved?"正文已保存。":"正文待保存。",18);var timer=new Godot.Timer{OneShot=true,WaitTime=.5};flow.AddChild(timer);timer.Timeout+=()=>{if(Composing()){timer.Start(.2);return;}_=SaveR3ShiftDraft();};writer.TextChanged+=()=>{editor.Edit(writer.Text);foreach(var stack in _r3ShiftPreviews.Values)foreach(var token in stack)service.ClosePreview(token);_r3ShiftPreviews.Clear();timer.Start();};var back=flow.GetChild<HBoxContainer>(0).GetChild<Button>(1);back.Pressed-=ClosePanel;back.Pressed+=()=>Execute(async()=>{if(!await SaveR3ShiftDraft())throw new InvalidOperationException("便条尚未保存，完成输入法候选或导出后重试；窗口保留。");ClosePanel();});Button(flow,"保存当前换班便条",()=>Execute(async()=>{if(!await SaveR3ShiftDraft())throw new InvalidOperationException("保存仍待处理，窗口保留。");}));Button(flow,"导出当前换班便条",()=>{if(Composing()){_status.Text="先完成输入法候选后再导出。";return;}SyncR3ShiftWriter();string dir=ProjectSettings.GlobalizePath("user://exports");Directory.CreateDirectory(dir);string path=Path.Combine(dir,"r3-shift-"+Guid.NewGuid()+".txt");File.WriteAllText(path,editor.Text,new System.Text.UTF8Encoding(false));_status.Text="便条已导出："+path;});Button(flow,"逐项明确当前排班意思",()=>Execute(async()=>await OpenR3ShiftMeaning(false)));Button(flow,"明确使用结构化辅助",()=>Execute(async()=>await OpenR3ShiftMeaning(true)));writer.GrabFocus();
    }
    private static OptionButton ShiftChoice(VBoxContainer flow,string name,params string[] values){var c=new OptionButton{AccessibilityName=name};foreach(string s in values)c.AddItem(s);flow.AddChild(c);return c;}
    private async ValueTask OpenR3ShiftMeaning(bool assisted)
    {
        if(!await SaveR3ShiftDraft())throw new InvalidOperationException("先保存当前便条。");var flow=Panel("换班的人 · 明确排班意思",InputOwner.Confirmation);Label(flow,assisted?"你明确选择结构化辅助。此处不认证独立英文理解。":"请手动核对已保存正文，没有自动英文理解。",20);var morning=ShiftChoice(flow,"上午本人","尚未选择","瑟拉","托宾","妮娅");var afternoon=ShiftChoice(flow,"下午本人","尚未选择","瑟拉","托宾","妮娅");var path=ShiftChoice(flow,"有限路径","尚未选择","重新分工","额外一个下午");var scope=ShiftChoice(flow,"工作窗口","尚未确认","只限本班R3-WATCH-S1","每个未来班");var privacy=ShiftChoice(flow,"私人字段","尚未确认","排除家庭具体安排","公开家庭安排");var original=ShiftChoice(flow,"旧承诺","尚未确认","保留原估计与后来更正","删除旧估计");var checks=ShiftChoice(flow,"未完项","尚未确认","同一账板标记未完检查","把贴出当已交班");var next=ShiftChoice(flow,"下一班","尚未确认","尚未同意","默认已答应");
        Button(flow,"明确保存当前排班意思",()=>Execute(async()=>{if(!await SaveR3ShiftDraft()||new[]{morning,afternoon,path,scope,privacy,original,checks,next}.Any(c=>c.Selected==0))throw new InvalidOperationException("先保存并逐项明确选择。");string[] actors=["","ROLE-R3-SHIFT-SELA","ROLE-R3-SHIFT-TOBIN","ROLE-R3-SHIFT-NIA"];CheckCommit(await R3Shift().ConfirmMeaningAsync(new(actors[morning.Selected],actors[afternoon.Selected],path.Selected==1?"reassign":"extra_help",scope.Selected==1?"R3-WATCH-S1":"all_future",privacy.Selected==1?"excluded":"included",original.Selected==1?"retained":"deleted",checks.Selected==1?"mark_on_same_board":"post_is_handover",next.Selected==1?"not_agreed":"agreed"),assisted));ClosePanel();}));
    }
    private async ValueTask OpenR3ShiftPostPreview()
    {
        if(!await SaveR3ShiftDraft())throw new InvalidOperationException("当前便条尚未保存。");var service=R3Shift();var p=await service.PreviewPostAsync();const string title="换班的人 · 同板实际贴出预览";var flow=Panel(title,InputOwner.Confirmation);Label(flow,p.Description,20);foreach(var issue in p.Issues)Label(flow,issue.SafeMessage,18);if(!p.Ready)return;if(!_r3ShiftPreviews.TryGetValue(title,out var stack))_r3ShiftPreviews.Add(title,stack=new());stack.Push(p.Token);Button(flow,"明确贴出当前有限安排",()=>Execute(async()=>{if(!await SaveR3ShiftDraft())throw new InvalidOperationException("当前稿有未保存修改。");CheckCommit(await service.ConfirmPreviewAsync(p.Token));ClosePanel();await Refresh();}));
    }
}

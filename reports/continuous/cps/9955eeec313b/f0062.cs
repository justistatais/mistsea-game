using Godot;
using Mist.Application.ChapterOne;
using Mist.Application.Drafts;
using Mist.Application.UI;
using Mist.Core.Campaign;
using Mist.Core.ChapterOne;
using Mist.Core.Contracts;
using Mist.Core.Quests;

public partial class Root
{
    private TextEdit? _findingWriter;
    private Label? _findingSaveStatus;
    private Godot.Timer? _findingTimer;
    private Task<bool>? _findingSaveTask;
    private readonly List<Guid> _findingPreviews=new();
    private readonly Dictionary<string,Stack<Guid>> _chapterUiPreviews=new();
    private void TrackChapterUiPreview(string title,Guid token)
    {
        if(!_chapterUiPreviews.TryGetValue(title,out var stack)){stack=new();_chapterUiPreviews[title]=stack;}stack.Push(token);
    }
    private void CloseChapterUiPreview(string title)
    {
        if(_chapterUiPreviews.TryGetValue(title,out var stack)&&stack.TryPop(out var token)){_chapter?.ClosePreview(token);_findingPreviews.Remove(token);}
    }
    private static bool HasReceipt(Mist.Application.Storage.BranchSnapshot s,string key)=>s.Receipts.Any(r=>r.SemanticKey==key||r.SemanticKey==key+":resolution");
    private async ValueTask<bool> TryChapterInteraction(string spot)
    {
        if(_chapter is null)return false;var snapshot=await _store.SnapshotAsync();
        var materials=_chapter.Package.Materials.Where(m=>m.Spot==spot).ToArray();
        if(materials.Length>0)
        {
            foreach(var material in materials){CheckCommit(await _chapter.CollectAsync(material.Id));await OpenChapterMaterial(material);}return true;
        }
        switch(spot)
        {
            case "R1-S03.havel":
                if(!HasReceipt(snapshot,"Q-102"))return false;
                string lease="chapter-havel:"+Guid.NewGuid();if(!await _store.TryLeaseAsync("N-HAVEL",lease))throw new InvalidOperationException("哈文正在处理另一项现场工作，请先完成安全交接。");_leaseEvent=lease;
                var havel=Panel("哈文",InputOwner.Workspace);Label(havel,"把测试计划、独立记录器、值班记录和通报分开核对。预写的通报不能独自证明具体意图。",22);
                var quest=snapshot.Entities.SingleOrDefault(e=>e.Id=="quest:Q-103");var state=quest is null?null:StrictJson.Deserialize<QuestState>(quest.Json);
                if(state is null||state.Lifecycle==QuestLifecycle.Unaccepted)Button(havel,"接下事故档案核查与调阅委托",()=>Execute(async()=>{CheckCommit(await _chapter.AcceptInvestigationAsync());ClosePanel();_status.Text="调阅委托已取得；从调度室登记出口进入记录保管间。";}));
                if(state is{Lifecycle:QuestLifecycle.Active}){Button(havel,"编辑同一份事故核查正文",()=>Execute(OpenFindingWriter));Button(havel,"核对实际来源与提交预览",()=>Execute(OpenFindingPreview));}
                if(state is{Lifecycle:QuestLifecycle.Closed})Label(havel,"事故核验已经提交。保留原始时刻与结论限度，下一步在潮门塔基申请现场工作。",22);
                return true;
            case "R1-S09.warden":
                var warden=Panel("潮门塔现场许可",InputOwner.Workspace);Label(warden,"本塔维护机械与工坊分别登记。现场处置需要两阀、局部供给隔离与威胁制止；只有实际安全结果才进入后续移交。",22);Button(warden,"申请当前塔站现场工作",()=>Execute(async()=>{CheckCommit(await _chapter.AcceptTowerWorkAsync());ClosePanel();_status.Text="塔站工作许可已取得。先读取现场说明并检查局部供给。";}));return true;
            case "R1-S09.supply_indicator":
                var supply=Panel("本塔局部供给检查",InputOwner.Confirmation);Label(supply,"检查当前本地供给配置。该读数不会消耗民用渡轮的备用电池，也不会预付六塔工程物资。",22);Button(supply,"实际检查当前局部供给",()=>Execute(async()=>{CheckCommit(await _chapter.InspectLocalSupplyAsync());ClosePanel();_status.Text="当前配置的局部供给检查已记录。";}));return true;
            case "R1-S10.encounter_entry":
                var encounter=await _chapter.TowerAsync();if(encounter is not null&&encounter.Battle.Outcome!=Mist.Core.Combat.CombatOutcome.Withdrawn){await OpenBattle();return true;}
                var entry=Panel("潮门塔遭遇入口",InputOwner.Confirmation);Label(entry,"左前左阀、右前右阀、左后隔离。阀门顺序可以交换；每人每个个人回合至多一个现场操作。仍可先查档案或回安全点调整装备。",22);
                var difficulty=new OptionButton{FitToLongestItem=false,AccessibilityName="塔站战斗难度"};foreach(string d in new[]{"标准","宽容","挑战"})difficulty.AddItem(d);entry.AddChild(difficulty);
                Label(entry,"不携带增强补给也可进场。共享基础急救仍按本次遭遇规则提供。",18);
                Button(entry,"明确进入潮门塔现场战斗",()=>Execute(async()=>{CheckCommit(await _chapter.BeginTowerAsync(new[]{"standard","relaxed","challenge"}[difficulty.Selected]));ClosePanel();await Refresh();await OpenBattle();}));return true;
            case "R1-S10.safety_console":
                var inspect=Panel("塔站制止后复核",InputOwner.Confirmation);Label(inspect,"复核两阀、供给隔离与机械制止的实际记录。此处只确认本塔当前安全，增强安装与六塔准备分别核验。",22);Button(inspect,"实际复核当前塔站结果",()=>Execute(async()=>{CheckCommit(await _chapter.VerifyTowerAfterControlAsync());ClosePanel();_status.Text="塔站现场复核完成。回接应泊位明确办理巡灯船移交。";}));return true;
            case "R1-S01.glimmer_handover":
                await OpenHandoverPreview();return true;
            default:return false;
        }
    }
    private async ValueTask OpenChapterArchive()
    {
        if(_chapter is null&&_coastArchive is null)return;var snapshot=await _store.SnapshotAsync();var materials=GrantedChapterMaterials(snapshot).ToArray();
        var flow=Panel("第一程与塔站档案",InputOwner.Workspace);if(materials.Length==0)Label(flow,"尚未取得这些原始材料。请在实际现场阅读或领取。",22);
        foreach(var material in materials)Button(flow,material.Title,()=>Execute(async()=>await OpenChapterMaterial(material)));
        Button(flow,"事故核查共用正文",()=>Execute(OpenFindingWriter));
    }
    private async ValueTask OpenChapterMaterial(CampaignMaterial material)
    {
        if(!GrantedChapterMaterials(await _store.SnapshotAsync()).Any(m=>m.Id==material.Id&&m.Version==material.Version))throw new InvalidOperationException("该版本材料尚未实际取得，或当前来源许可不可用。");
        var flow=Panel(material.Title,InputOwner.Workspace);AddFirstApproachSourceControls(flow,material.Spot);Label(flow,material.Scene+" · "+material.Version,18);var text=new TextEdit{Text=material.English,Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,350),AccessibilityName=material.Title+"原文"};text.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(text);AddRegionalDictionary(flow,text);
        Label(flow,material.Modality=="listening_script"?"此处保留听力原始脚本；没有可用音轨时记录为文本辅助。":"原始记录与实际核查分开保留。",18);Button(flow,"查看中文辅助",()=>Label(flow,material.Chinese,20));if(_chapter is not null)Button(flow,"事故核查同一份正文",()=>Execute(OpenFindingWriter));
    }
    private async ValueTask OpenFindingWriter()
    {
        if(_chapter is null)throw new InvalidOperationException("当前事故核查工作区尚未接入。");if(_routing.Contains("事故核查草稿")){while(_routing.Top!="事故核查草稿")ClosePanel();_findingWriter?.GrabFocus();return;}
        if(_findingDraft is null){var head=await _store.GetDraftAsync(ChapterOneService.FindingDraft);var s=_store.Session;_findingDraft=new DraftEditor(_store,new(s.Profile,s.Branch,s.Load,ChapterOneService.FindingDraft,head?.Id,head?.Revision??0,head?.Body??"","chapter-one-candidate-1"));}
        var flow=Panel("事故核查草稿",InputOwner.Editable);Label(flow,"写一份核查摘要：分别说明计划、实际记录和被取消的检查步骤，并说明资料尚不足以证明什么。保留原始时刻及不同仪器的比较范围。",22);Label(flow,"读听写目标各6.5。当前英文表达尚未评估；字数和语言分数不会发奖或裁定责任。",18);
        var writer=new TextEdit{Text=_findingDraft.Text,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,320),AccessibilityName="事故核查正文"};writer.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(writer);_findingWriter=writer;_findingSaveStatus=Label(flow,"尚未收到本次正文的保存回执。",18);
        var timer=new Godot.Timer{OneShot=true,WaitTime=0.5};flow.AddChild(timer);_findingTimer=timer;timer.Timeout+=()=>{if(Composing()){timer.Start(0.2);return;}_=SaveFindingDraft();};
        writer.TextChanged+=()=>{_findingDraft.Edit(writer.Text);if(_findingSaveStatus is not null&&IsInstanceValid(_findingSaveStatus))_findingSaveStatus.Text="有未保存修改。";foreach(var token in _findingPreviews)_chapter.ClosePreview(token);_findingPreviews.Clear();timer.Start();};writer.FocusExited+=()=>{foreach(var key in _keys.Values.Append(Key.Enter))if(Input.IsPhysicalKeyPressed(key))_releaseBarrier.Add(key);};
        var back=flow.GetChild<HBoxContainer>(0).GetChild<Button>(1);back.Pressed-=ClosePanel;back.Pressed+=()=>Execute(async()=>{if(!await SaveFindingDraft())throw new InvalidOperationException("当前正文尚未保存，请完成输入法候选后重试或导出。");ClosePanel();_findingWriter=null;_findingSaveStatus=null;_findingTimer=null;});
        Button(flow,"保存核查草稿",()=>Execute(async()=>{if(!await SaveFindingDraft())throw new InvalidOperationException("保存尚未完成，请完成输入法候选或重试。");}));Button(flow,"导出当前正文到本地",()=>{string directory=ProjectSettings.GlobalizePath("user://exports");Directory.CreateDirectory(directory);string path=Path.Combine(directory,"Q103-finding-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+".txt");File.WriteAllText(path,_findingDraft.Text,new System.Text.UTF8Encoding(false));_status.Text="核查正文已导出："+path;});
        Button(flow,"核对我写下的意思",()=>Execute(async()=>await OpenFindingMeaning(false)));Button(flow,"明确使用结构化辅助",()=>Execute(async()=>await OpenFindingMeaning(true)));Button(flow,"核查提交预览",()=>Execute(OpenFindingPreview));writer.GrabFocus();
    }
    private async ValueTask<bool> SaveFindingDraft()
    {
        if(_findingDraft is null)return true;if(Composing())return false;if(_findingSaveTask is not null&&!_findingSaveTask.IsCompleted)return await _findingSaveTask;_findingSaveTask=SaveFindingCore();return await _findingSaveTask;
    }
    private async Task<bool> SaveFindingCore()
    {
        try{var ack=await _findingDraft!.SaveAsync();if(_findingSaveStatus is not null&&IsInstanceValid(_findingSaveStatus))_findingSaveStatus.Text=_findingDraft.IsSaved?"正文已保存 · 版本 "+ack.Revision:"当前修改尚未保存，旧回执不覆盖新正文。";if(!_findingDraft.IsSaved&&_findingTimer is not null&&IsInstanceValid(_findingTimer))_findingTimer.Start(0.2);return _findingDraft.IsSaved;}
        catch(Exception error){if(_findingSaveStatus is not null&&IsInstanceValid(_findingSaveStatus))_findingSaveStatus.Text="保存失败，正文仍可导出："+error.Message;_status.Text="事故核查稿保存失败："+error.Message;return false;}
    }
    private async ValueTask OpenFindingMeaning(bool assisted)
    {
        if(_chapter is null)return;if(!await SaveFindingDraft())throw new InvalidOperationException("请先保存当前正文或完成输入法候选。");var flow=Panel(assisted?"结构化辅助 · 核查意思":"核对核查意思",InputOwner.Confirmation);Label(flow,assisted?"这些选择是明确辅助，不计作独立自由写作。它们只形成核查提议。":"当前离线模式未自动理解英文，请逐项确认你写下的意思。",22);
        string[][] labels=[["尚未确认","已批准的预定测试","仅是自然故障"],["尚未确认","在已记钟差范围内一致","所有仪器秒数完全一致"],["尚未确认","测试前取消了第二次检查","所有检查都已完成"],["尚未确认","通报本身不足以证明意图","通报标题已足够定罪"],["尚未确认","未确定具体高层意图","所有相关人员共同谋害"]];
        string[][] values=[["","approved_preplanned_test","natural_fault_only"],["","within_recorded_clock_uncertainty","identical_instrument_second"],["","second_check_removed_before_test","all_checks_completed"],["","insufficient_on_its_own","notice_alone_proves_intent"],["","specific_higher_intent_not_established","all_officers_deliberately_caused_the_accident"]];var choices=new List<OptionButton>();string[] names=["断供动作的来源","时间比较","必要保护步骤","通报的证明力","责任结论的限度"];
        for(int i=0;i<labels.Length;i++){Label(flow,names[i],20);var choice=new OptionButton{FitToLongestItem=false,AccessibilityName=names[i]};foreach(string label in labels[i])choice.AddItem(label);flow.AddChild(choice);choices.Add(choice);}
        Button(flow,"明确确认这份提议的意思",()=>Execute(async()=>{if(!await SaveFindingDraft())throw new InvalidOperationException("当前正文尚未保存。");var v=choices.Select((choice,i)=>values[i][choice.Selected]).ToArray();if(v.Any(string.IsNullOrWhiteSpace))throw new InvalidOperationException("请逐项明确当前提议。");CheckCommit(await _chapter.ConfirmFindingMeaningAsync(new(v[0],v[1],v[2],v[3],v[4]),assisted));ClosePanel();_status.Text="核查意思已记录。实际来源与提交站位仍分别检查。";}));
    }
    private async ValueTask OpenFindingPreview()
    {
        if(_chapter is null)return;if(!await SaveFindingDraft())throw new InvalidOperationException("当前正文尚未保存，不能预览旧稿的核查结果。");var p=await _chapter.PreviewFindingAsync();var flow=Panel("事故核查提交预览",InputOwner.Confirmation);Label(flow,p.Description,22);foreach(string issue in p.Issues)Label(flow,issue,20);if(!p.Ready)return;_findingPreviews.Add(p.Token);TrackChapterUiPreview("事故核查提交预览",p.Token);Button(flow,"明确提交这份事故核查结果",()=>Execute(async()=>{CheckCommit(await _chapter.ConfirmPreviewAsync(p.Token));_findingPreviews.Remove(p.Token);CloseAllPanels();_status.Text="事故结论已提交。原始记录、钟差与未确定的意图继续保留。";}));
    }
    private async ValueTask OpenHandoverPreview()
    {
        if(_chapter is null)return;var p=await _chapter.PreviewHandoverAsync();var flow=Panel("微光号使用权移交预览",InputOwner.Confirmation);Label(flow,p.Description,22);foreach(string issue in p.Issues)Label(flow,issue,20);if(!p.Ready)return;TrackChapterUiPreview("微光号使用权移交预览",p.Token);Button(flow,"明确接收旧巡灯船与跨区核查委托",()=>Execute(async()=>{CheckCommit(await _chapter.ConfirmPreviewAsync(p.Token));CloseAllPanels();_status.Text="微光号已按实际塔站结果移交，第一校准与本次固定奖励已结算。";}));
    }
}

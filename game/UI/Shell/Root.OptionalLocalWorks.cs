using System.Collections.Immutable;
using Godot;
using Mist.Application.Drafts;
using Mist.Application.OptionalLocalWorks;
using Mist.Application.UI;

public partial class Root
{
    private MusicBoxRepairService? _musicBoxRepair;
    private DraftEditor? _musicBoxEditor;
    private TextEdit? _musicBoxWriter;
    private Label? _musicBoxSaveStatus;
    private Godot.Timer? _musicBoxSaveTimer;
    private Task<bool>? _musicBoxSaveTask;
    private const string MusicBoxWriterTitle="音乐盒私人维修便条";
    private readonly Dictionary<string,Stack<Guid>> _musicBoxUiPreviews=new();
    private void InitializeOptionalLocalWorks(){_musicBoxRepair=_chapterThree is null?null:new MusicBoxRepairService(_store,_catalog,_chapterThree.Package);}
    private void ResetOptionalLocalWorksUiState()
    {
        foreach(var stack in _musicBoxUiPreviews.Values)foreach(var token in stack)_musicBoxRepair?.ClosePreview(token);
        _musicBoxUiPreviews.Clear();_musicBoxEditor=null;_musicBoxWriter=null;_musicBoxSaveStatus=null;_musicBoxSaveTimer=null;_musicBoxSaveTask=null;_musicBoxRepair=null;
    }
    private void CloseOptionalLocalWorksUiPreview(string title)
    {
        if(_musicBoxUiPreviews.TryGetValue(title,out var stack)&&stack.TryPop(out var token))_musicBoxRepair?.ClosePreview(token);
        if(title==MusicBoxWriterTitle){_musicBoxWriter=null;_musicBoxSaveStatus=null;_musicBoxSaveTimer=null;_=SaveOptionalLocalWorksDraft();}
    }
    private void MusicBoxButton(VBoxContainer flow,string caption,Func<ValueTask> action)=>Button(flow,caption,()=>Execute(async()=>{await action();ClosePanel();await Refresh();}));
    private async ValueTask<bool> TryOptionalLocalWorksInteraction(string spot)
    {
        if(!spot.StartsWith("R3-O01.",StringComparison.Ordinal)||_chapterThree is null)return false;
        if(_musicBoxRepair is null)InitializeOptionalLocalWorks();if(_musicBoxRepair is null)return false;var service=_musicBoxRepair;var view=await service.ViewAsync();var st=view.State;
        var flow=Panel("音乐盒修理铺 · "+spot,InputOwner.Workspace);Label(flow,"主人保留同一音乐盒的所有权。这项本地维修没有登记奖励，不影响主线、同行或六塔资格。维修机构许可不包括公开家庭回忆。",20);
        Label(flow,st is null?"尚未接受主人邀请。":$"邀请：{st.Invitation}；当前配置 {st.Configuration}；当前检查 {(st.TestedConfiguration==st.Configuration?"已完成":"尚未完成或需重查")}；同盒归还 {st.Returned}；便条交付 {st.Completed}",18);
        switch(spot)
        {
            case "R3-O01.owner":
                if(st?.Completed!=true)
                {
                    foreach(var pair in new[]{("accept","接受主人提出的同盒机构维修与规格阅读邀请"),("defer","暂缓本次主人邀请"),("decline","拒绝本次主人邀请")}){string decision=pair.Item1;MusicBoxButton(flow,pair.Item2,async()=>CheckCommit(await service.DecideInvitationAsync(decision)));}
                    MusicBoxButton(flow,"向主人核对已选件并请求审批",async()=>CheckCommit(await service.RequestOwnerApprovalAsync()));
                    MusicBoxButton(flow,"向主人实际归还已检查的同一盒子",async()=>CheckCommit(await service.ReturnSameBoxAsync()));
                    MusicBoxButton(flow,"暂停后安全归还同盒（不算完成）",async()=>CheckCommit(await service.ReturnPausedBoxAsync()));
                    MusicBoxButton(flow,"重新获准后由主人交回同盒继续检查",async()=>CheckCommit(await service.ResumeSameBoxAsync()));
                    Button(flow,"核对当前私人便条的交付预览",()=>Execute(OpenMusicBoxDelivery));
                }
                MusicBoxButton(flow,"撤回主人本次许可；保留已发生的维修历史",async()=>CheckCommit(await service.DecideInvitationAsync("withdraw")));
                break;
            case "R3-O01.parts":
                Button(flow,view.Specification is null?"在主人当前许可内实际取得规格说明":"查看当前获准且已取得的规格说明",()=>Execute(async()=>{if(view.Specification is null)CheckCommit(await service.DiscoverSpecificationAsync());await OpenOptionalLocalWorksArchive();}));
                if(view.Specification is not null)Label(flow,"请依据原规格明确选件，再到主人处请求审批。十四齿件属于另一装置。",20);
                var choice=new OptionButton{AccessibilityName="音乐盒明确选件"};foreach(string text in new[]{"尚未选择","MBOX-12 · 十二齿","MBOX-14 · 十四齿"})choice.AddItem(text);flow.AddChild(choice);
                MusicBoxButton(flow,"实际比较原规格与所选件",async()=>{if(choice.Selected==0)throw new InvalidOperationException("请明确选择要比较的部件。");CheckCommit(await service.SelectWheelAsync(choice.Selected==1?"MBOX-12":"MBOX-14",choice.Selected==1?12:14));});
                break;
            case "R3-O01.musicbox":
                MusicBoxButton(flow,"检查原同一音乐盒的机构",async()=>CheckCommit(await service.InspectMechanismAsync()));
                MusicBoxButton(flow,"停止同盒上弦，为装件隔离机构",async()=>CheckCommit(await service.IsolateMechanismAsync()));
                MusicBoxButton(flow,"实际安装主人当前批准的同一部件",async()=>CheckCommit(await service.FitApprovedWheelAsync()));
                MusicBoxButton(flow,"检查当前配置的转动机构",async()=>CheckCommit(await service.CheckTurningAsync()));
                break;
            case "R3-O01.note":
                Button(flow,"编辑实际保存的私人维修便条",()=>Execute(OpenMusicBoxWriter));
                Button(flow,"逐项核对当前便条意思",()=>Execute(async()=>await OpenMusicBoxMeaning(false)));
                Button(flow,"明确使用结构化辅助",()=>Execute(async()=>await OpenMusicBoxMeaning(true)));
                Label(flow,"意思确认在此处进行；正式交付须返回主人站位。正文保存不会执行维修或提交。",18);
                break;
        }
        return true;
    }
    private async ValueTask OpenOptionalLocalWorksArchive()
    {
        if(_musicBoxRepair is null)return;var material=await _musicBoxRepair.OpenSpecificationAsync();var flow=Panel("音乐盒当前获准的原件",InputOwner.Workspace);Label(flow,material.Title+" · "+material.Version,20);
        var reader=new TextEdit{Text=material.English,Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,330),AccessibilityName="音乐盒规格英文原文"};reader.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(reader);AddRegionalDictionary(flow,reader);Button(flow,"查看中文辅助",()=>Label(flow,material.Chinese,20));Label(flow,"当前为书面规格说明。家庭回忆没有公开许可；旧录音定位缺件的完整原件分支仍待制作。",18);
    }
    private async ValueTask<bool> HasOptionalLocalWorksArchive()=>_musicBoxRepair is not null&&(await _musicBoxRepair.ViewAsync()).Specification is not null;
    private async ValueTask OpenMusicBoxWriter()
    {
        if(_musicBoxRepair is null)return;if(!await SaveOptionalLocalWorksDraft())throw new InvalidOperationException("请先完成输入法候选并保存当前便条。");
        if(_routing.Contains(MusicBoxWriterTitle)){while(_routing.Top!=MusicBoxWriterTitle)ClosePanel();_musicBoxWriter?.GrabFocus();return;}
        if(_musicBoxEditor is null){var d=await _store.GetDraftAsync(MusicBoxRepairService.Task);var session=_store.Session;_musicBoxEditor=new(_store,new(session.Profile,session.Branch,session.Load,MusicBoxRepairService.Task,d?.Id,d?.Revision??0,d?.Body??"",_musicBoxRepair.Specification.Version));}
        var editor=_musicBoxEditor!;var flow=Panel(MusicBoxWriterTitle,InputOwner.Editable);Label(flow,_musicBoxRepair.WritingTask.EnglishPrompt,22);Button(flow,"查看写作中文辅助",()=>Label(flow,_musicBoxRepair.WritingTask.ChinesePrompt,20));Label(flow,"从当前同一份保存稿件继续；不自动理解英文、不计语言分数。便条归主人保管，不含公开家庭记忆的许可。",18);
        var writer=new TextEdit{Text=editor.Text,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,330),AccessibilityName="音乐盒私人英文维修便条"};flow.AddChild(writer);_musicBoxWriter=writer;_musicBoxSaveStatus=Label(flow,editor.IsSaved?"当前便条与保存版本一致。":"当前便条待保存。",18);
        var timer=new Godot.Timer{OneShot=true,WaitTime=.5};flow.AddChild(timer);_musicBoxSaveTimer=timer;timer.Timeout+=()=>{if(Composing()){timer.Start(.2);return;}_=SaveOptionalLocalWorksDraft();};
        writer.TextChanged+=()=>{editor.Edit(writer.Text);if(_musicBoxSaveStatus is{} label&&IsInstanceValid(label))label.Text="当前便条有未保存修改。";foreach(var stack in _musicBoxUiPreviews.Values)foreach(var token in stack)_musicBoxRepair.ClosePreview(token);_musicBoxUiPreviews.Clear();timer.Start();};
        writer.FocusExited+=()=>{foreach(var key in _keys.Values.Append(Key.Enter))if(Input.IsPhysicalKeyPressed(key))_releaseBarrier.Add(key);};var back=flow.GetChild<HBoxContainer>(0).GetChild<Button>(1);back.Pressed-=ClosePanel;back.Pressed+=()=>Execute(async()=>{if(!await SaveOptionalLocalWorksDraft())throw new InvalidOperationException("便条未保存，完成输入法候选后再试或导出。");ClosePanel();});
        Button(flow,"保存私人便条",()=>Execute(async()=>{if(!await SaveOptionalLocalWorksDraft())throw new InvalidOperationException("便条尚未全数保存。");}));Button(flow,"导出当前便条",()=>{string dir=ProjectSettings.GlobalizePath("user://exports");Directory.CreateDirectory(dir);string path=Path.Combine(dir,"musicbox-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+".txt");File.WriteAllText(path,editor.Text,new System.Text.UTF8Encoding(false));_status.Text="当前私人便条已导出："+path;});Button(flow,"逐项核对便条意思",()=>Execute(async()=>await OpenMusicBoxMeaning(false)));Button(flow,"明确使用结构化辅助",()=>Execute(async()=>await OpenMusicBoxMeaning(true)));writer.GrabFocus();
    }
    private async ValueTask<bool> SaveOptionalLocalWorksDraft()
    {
        if(_musicBoxEditor is null)return true;if(Composing())return false;if(_musicBoxSaveTask is not null&&!_musicBoxSaveTask.IsCompleted)return await _musicBoxSaveTask;_musicBoxSaveTask=SaveMusicBoxDraftCore();return await _musicBoxSaveTask;
    }
    private async Task<bool> SaveMusicBoxDraftCore()
    {
        try{if(_musicBoxEditor is{} editor&&!editor.IsSaved)await editor.SaveAsync();bool saved=_musicBoxEditor?.IsSaved??true;if(_musicBoxSaveStatus is{} label&&IsInstanceValid(label))label.Text=saved?"私人便条已由实际 CAS 保存回执确认。":"修改尚未全数保存，旧回执不会覆盖新稿。";if(!saved&&_musicBoxSaveTimer is{} timer&&IsInstanceValid(timer))timer.Start(.2);return saved;}
        catch(Exception error){if(_musicBoxSaveStatus is{} label&&IsInstanceValid(label))label.Text="保存失败，便条仍可导出："+error.Message;_status.Text="音乐盒便条保存失败："+error.Message;return false;}
    }
    private async ValueTask OpenMusicBoxMeaning(bool assisted)
    {
        if(_musicBoxRepair is null)return;if(!await SaveOptionalLocalWorksDraft())throw new InvalidOperationException("请完成输入法候选并保存当前便条。");var flow=Panel("音乐盒便条意思核对",InputOwner.Confirmation);Label(flow,assisted?"明确使用结构化辅助解释当前稿件，不计作自动理解英文。所有选项仍须对应实际原件与维修事实。":"当前不自动理解英文；逐项确认你在当前便条中表达的意思。未选择的项不会填入默认答案。",20);
        (string Key,string Name,string[] Labels,string[] Values)[] rows=[
            ("part","实际使用部件",["尚未选择","MBOX-12 十二齿","MBOX-14 十四齿"],["","MBOX-12","MBOX-14"]),
            ("check","实际检查",["尚未选择","仅转动机构检查","整套设备全面安全"],["","turning_mechanism_only","whole_device_safe"]),
            ("box","归还对象",["尚未选择","原同一盒子","另一盒代替"],["",MusicBoxRepairService.Box,"another_box"]),
            ("return","实际接收者",["尚未选择","同一主人","收入玩家收藏"],["","same_owner","player_collection"]),
            ("limits","工作边界",["尚未选择","仅机构维修","证明矿区事故原因"],["","mechanism_only","accident_cause_proven"]),
            ("memory","家庭回忆的处理",["尚未选择","保持私密未公开","公开到住区告示"],["","private_not_published","published"])
        ];var options=new List<OptionButton>();foreach(var row in rows){Label(flow,row.Name,20);var option=new OptionButton{AccessibilityName=row.Name};foreach(string label in row.Labels)option.AddItem(label);flow.AddChild(option);options.Add(option);}
        MusicBoxButton(flow,"明确记录当前便条的所选意思",async()=>{if(!await SaveOptionalLocalWorksDraft())throw new InvalidOperationException("当前便条尚未保存。");if(options.Any(x=>x.Selected==0))throw new InvalidOperationException("请逐项明确选择便条意思。");var fields=rows.Select((r,i)=>new KeyValuePair<string,string>(r.Key,r.Values[options[i].Selected])).ToImmutableDictionary();CheckCommit(await _musicBoxRepair.ConfirmNoteMeaningAsync(fields,assisted));});
    }
    private async ValueTask OpenMusicBoxDelivery()
    {
        if(_musicBoxRepair is null)return;if(!await SaveOptionalLocalWorksDraft())throw new InvalidOperationException("请完成输入法候选并保存当前便条。");var preview=await _musicBoxRepair.PreviewDeliveryAsync();const string title="音乐盒私人便条交付预览";var flow=Panel(title,InputOwner.Confirmation);
        if(!preview.Ready){foreach(var issue in preview.Issues)Label(flow,issue.SafeMessage,20);return;}
        if(!_musicBoxUiPreviews.TryGetValue(title,out var stack)){stack=new();_musicBoxUiPreviews[title]=stack;}stack.Push(preview.Token);
        Label(flow,"当前预览核对了主人许可、获批十二齿实物、当前机构检查、同盒归还以及同一保存稿件。确认后便条由主人私下保管；没有 XP、刻币或零件奖励，不改变主线及六塔资格。",20);
        MusicBoxButton(flow,"明确将当前私人维修便条交给主人",async()=>{CheckCommit(await _musicBoxRepair.ConfirmPreviewAsync(preview.Token));_status.Text="同盒维修与私人便条交付已记录。";});
    }
}

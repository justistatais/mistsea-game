using System.Collections.Immutable;
using Godot;
using Mist.Application.Contracts;
using Mist.Application.Drafts;
using Mist.Application.R2Life;
using Mist.Application.Storage;
using Mist.Application.UI;
using Mist.Core.Contracts;
using Mist.Core.Quests;
using Mist.Core.R2Life;

public partial class Root
{
    private R2LifeService? _r2Life;
    private readonly Dictionary<string,DraftEditor> _r2LifeEditors=new();
    private readonly Dictionary<string,Guid> _r2LifePreviewTokens=new();
    private TextEdit? _r2LifeWriter;
    private Task<bool>? _r2LifeSaveTask;
    private sealed class R2LifeDraftStore(R2LifeService service,string quest,Func<SessionView> session):IDraftStore
    {
        public ValueTask<DraftSaved> SaveAsync(SaveDraft command,CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested();var current=session();if(command.Profile!=current.Profile||command.Branch!=current.Branch||command.Load!=current.Load||command.MaterialVersion!=service.Contract.ContentVersion||command.TaskInstanceId!=service.Writing.Single(w=>w.Quest==quest).Id)throw new StoreConflictException("旧载入的地方编辑器不能写入当前实例；原稿保留。");
            return service.SaveDraftAsync(quest,command.Draft,command.ExpectedHeadRevision,command.Text);
        }
    }
    private void LoadR2Life()
    {
        if(_chapterTwo is null)throw new InvalidOperationException("第二章尚未接入。");using var source=Godot.FileAccess.Open("res://content/r2_life/learner_contract.json",Godot.FileAccess.ModeFlags.Read);if(source is null)throw new InvalidDataException("第二章地方工作合同缺失，原存档保留。");
        var service=new R2LifeService(_store,_store,_chapterTwo,R2LifeContract.Parse(source.GetAsText()));_r2Life=service;
        RegisterRegionalLearningScope("R2-life",async()=>(await _store.SnapshotAsync()).Entities.Any(e=>e.Id==R2LifeService.StateId)?(await service.ViewAsync()).Materials.Select(RegionalLearningAccess).ToArray():Array.Empty<Mist.Core.Learning.LearningSourceAccess>());
    }
    private async ValueTask<bool> TryInitializeR2Life()
    {
        if(_r2Life is null||_chapterTwo is null)return false;var snapshot=await _store.SnapshotAsync();if(snapshot.Entities.Any(e=>e.Id==R2LifeService.StateId))return true;
        if(!snapshot.Entities.Any(e=>e.Id==Mist.Application.ChapterTwo.ChapterTwoService.StateId))return false;var main=await _chapterTwo.ViewAsync();if(!main.State.Landing||!main.Actor.Scene.StartsWith("R2-",StringComparison.Ordinal))return false;CheckCommit(await _r2Life.InitializeAsync());return true;
    }
    private void CloseR2LifeUiPreview(string title){if(_r2LifePreviewTokens.Remove(title,out var token))_r2Life?.ClosePreview(token);}
    private void ResetR2LifeUiState(){foreach(var token in _r2LifePreviewTokens.Values)_r2Life?.ClosePreview(token);_r2LifePreviewTokens.Clear();_r2LifeEditors.Clear();_r2LifeWriter=null;_r2LifeSaveTask=null;}
    private async Task<bool> SaveR2LifeDraft()
    {
        if(Composing())return false;if(_r2LifeSaveTask is{IsCompleted:false})return await _r2LifeSaveTask;_r2LifeSaveTask=SaveR2LifeDraftCore();return await _r2LifeSaveTask;
    }
    private async Task<bool> SaveR2LifeDraftCore()
    {
        bool saved=true;foreach(var editor in _r2LifeEditors.Values.ToArray().Where(e=>!e.IsSaved))try{await editor.SaveAsync();}catch(Exception error){saved=false;_status.Text="地方正文尚未保存，原稿和窗口保留，可导出："+error.Message;}bool current=saved&&!Composing()&&_r2LifeEditors.Values.All(e=>e.IsSaved);if(!current&&saved)_status.Text="当前新修改尚未保存，旧回执不会关闭正文窗口。";return current;
    }
    private async ValueTask OpenR2LifeQuests()
    {
        if(!await TryInitializeR2Life()||_r2Life is null)throw new InvalidOperationException("先在真实 R2 泊位登记登陆。");var view=await _r2Life.ViewAsync();var flow=Panel("修会地方生活工作",InputOwner.Workspace);
        Label(flow,"没寄出的回信 · 六块空床。两项均零经验、刻币、零件，可暂缓或在受理前拒绝，不限制公共调查与结局。",20);
        foreach(var quest in view.Quests){Label(flow,quest.Id+" · "+quest.Lifecycle,20);if(quest.Lifecycle==QuestLifecycle.Active){string id=quest.Id;Button(flow,"暂缓"+id,()=>Execute(async()=>{if(!await SaveR2LifeDraft())throw new InvalidOperationException("当前地方原稿尚未保存，暂缓未执行，窗口保留。");CheckCommit(await _r2Life.SetPausedAsync(id,true));ClosePanel();}));}}
        Button(flow,"查看公开工作范围说明",()=>OpenR2LifePublicContract());Button(flow,"查看当前许可内的已得原文",()=>Execute(OpenR2LifeArchive));
    }
    private void OpenR2LifePublicContract()
    {
        if(_r2Life is null)return;var flow=Panel("地方公开工作范围",InputOwner.Workspace);foreach(var paragraph in _r2Life.Contract.Paragraphs){Label(flow,paragraph.En,20);Label(flow,paragraph.Zh,18);}
    }
    private async ValueTask OpenR2LifeArchive()
    {if(_r2Life is null)return;var flow=Panel("地方已得原文",InputOwner.Workspace);foreach(var material in (await _r2Life.ViewAsync()).Materials){string id=material.Id;Button(flow,material.Title,()=>Execute(async()=>await OpenR2LifeMaterial(id)));}}
    private async ValueTask OpenR2LifeMaterial(string id)
    {
        if(_r2Life is null)return;var material=await _r2Life.OpenMaterialAsync(id);var flow=Panel(material.Title,InputOwner.Workspace);Label(flow,"原文范围随本人许可核验；短留言不输出能力评分。人工校准待完成。",18);
        var text=new TextEdit{Text=material.English,Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,280),AccessibilityName=material.Title+"英文原文"};flow.AddChild(text);text.AddThemeFontSizeOverride("font_size",20*_scale/100);
        Button(flow,"查看派生中文辅助",()=>Execute(async()=>Label(flow,(await _r2Life.OpenMaterialAsync(id)).Chinese,20)));
        Button(flow,"查询选中文本的离线词义",()=>Execute(async()=>{string selected=text.GetSelectedText();if(string.IsNullOrWhiteSpace(selected))throw new InvalidOperationException("请先选择需要查阅的词或短语。");await OpenRegionalDictionary(selected,async()=>new[]{RegionalLearningAccess(await _r2Life.OpenMaterialAsync(id))});}));
    }
    private async ValueTask<bool> TryR2LifeInteraction(string spot)
    {
        if(_r2Life is null||spot is not("R2-S03.address" or "R2-O01.letter" or "R2-O01.reply" or "R2-O02.seed_note" or "R2-O02.allocation"))return false;
        if(!await TryInitializeR2Life())throw new InvalidOperationException("实际登陆尚未登记。");var view=await _r2Life.ViewAsync();var flow=Panel("地方现场 · "+spot,InputOwner.Workspace);Button(flow,"查看公开工作范围说明",()=>OpenR2LifePublicContract());
        if(spot=="R2-S03.address")
        {
            Button(flow,"查看此处公共房间说明",()=>Execute(async()=>{await TryChapterTwoInteraction(spot);}));
            Button(flow,"向作者请求这封信的有限协助许可",()=>OpenR2LifeScope());if(view.Scope is{Allowed:true})Button(flow,"作者撤回当前信件协助范围",()=>Execute(async()=>{if(!await SaveR2LifeDraft())throw new InvalidOperationException("当前地方原稿尚未保存，撤回未执行，窗口保留。");CheckCommit(await _r2Life.WithdrawLetterScopeAsync());CloseAllPanels();await Refresh();}));
            if(view.Quests.Single(q=>q.Id==R2LifeRules.Reply).Lifecycle==QuestLifecycle.Unaccepted)Button(flow,"拒绝本次可选回信请求",()=>Execute(async()=>{if(!await SaveR2LifeDraft())throw new InvalidOperationException("当前地方原稿尚未保存，拒绝未执行，窗口保留。");CheckCommit(await _r2Life.DeclineAsync(R2LifeRules.Reply));ClosePanel();}));return true;
        }
        foreach(var material in _r2Life.Materials.Where(m=>m.Spot==spot)){string id=material.Id;Button(flow,(view.Materials.Any(m=>m.Id==id)?"查看已得原文：":"现场读取原文：")+material.Title,()=>Execute(async()=>{CheckCommit(await _r2Life.DiscoverAsync(id));await OpenR2LifeMaterial(id);}));}
        if(spot is "R2-O01.reply" or "R2-O02.allocation")
        {
            string quest=spot=="R2-O01.reply"?R2LifeRules.Reply:R2LifeRules.Seeds;var q=view.Quests.Single(q=>q.Id==quest);
            if(q.Lifecycle==QuestLifecycle.Unaccepted){Button(flow,"受理同一地方请求",()=>Execute(async()=>{CheckCommit(await _r2Life.AcceptAsync(quest));ClosePanel();}));if(quest==R2LifeRules.Seeds)Button(flow,"拒绝本次可选试种请求",()=>Execute(async()=>{if(!await SaveR2LifeDraft())throw new InvalidOperationException("当前地方原稿尚未保存，拒绝未执行，窗口保留。");CheckCommit(await _r2Life.DeclineAsync(quest));ClosePanel();}));}
            if(q.Lifecycle==QuestLifecycle.Active)
            {
                Button(flow,"恢复同一地方实例",()=>Execute(async()=>{CheckCommit(await _r2Life.SetPausedAsync(quest,false));ClosePanel();}));Button(flow,"暂缓同一地方实例",()=>Execute(async()=>{if(!await SaveR2LifeDraft())throw new InvalidOperationException("当前地方原稿尚未保存，暂缓未执行，窗口保留。");CheckCommit(await _r2Life.SetPausedAsync(quest,true));ClosePanel();}));Button(flow,"编辑地方英文短说明",()=>Execute(async()=>await OpenR2LifeWriter(quest)));Button(flow,"预览正式地方提交",()=>Execute(async()=>await OpenR2LifeSubmission(quest)));
                if(quest==R2LifeRules.Reply){Button(flow,"请求作者审阅当前保存稿",()=>OpenR2LifeAuthorReview());Button(flow,"作者撤回当前信件协助范围",()=>Execute(async()=>{if(!await SaveR2LifeDraft())throw new InvalidOperationException("当前地方原稿尚未保存，撤回未执行，窗口保留。");CheckCommit(await _r2Life.WithdrawLetterScopeAsync());CloseAllPanels();await Refresh();}));}
                else
                {
                    foreach(string bed in _r2Life.Contract.BedIds){string id=bed;Button(flow,"实际检查床位 "+id,()=>Execute(async()=>{CheckCommit(await _r2Life.InspectBedAsync(id));ClosePanel();}));Button(flow,"实际种植床位 "+id,()=>Execute(async()=>{CheckCommit(await _r2Life.PlantBedAsync(id));ClosePanel();}));}
                    Button(flow,"逐床标出当前试种配置",()=>OpenR2LifeLayout());Button(flow,"向种植者请求当前布局批准",()=>Execute(async()=>{CheckCommit(await _r2Life.RequestGrowerApprovalAsync());ClosePanel();}));Button(flow,"申请尚未获准的布局修订",()=>OpenR2LifeRevision());
                    Label(flow,"已检查："+view.State.InspectedBeds.Length+"/6；已实际种植："+view.State.Planted.Length+"/6。估计不是收成库存。",18);
                }
            }
        }
        return true;
    }
    private static OptionButton R2LifeChoice(VBoxContainer flow,string name,params string[] choices){var option=new OptionButton{AccessibilityName=name,FitToLongestItem=false};foreach(string choice in choices)option.AddItem(choice);flow.AddChild(option);return option;}
    private void OpenR2LifeScope()
    {
        if(_r2Life is null)return;var flow=Panel("这封信的本人有限许可",InputOwner.Confirmation);var draft=new CheckBox{Text="当前草稿",AccessibilityName="当前草稿许可"};var intent=new CheckBox{Text="通信意图",AccessibilityName="通信意图许可"};flow.AddChild(draft);flow.AddChild(intent);var material=_r2Life.Materials.Single(m=>m.Id==R2LifeRules.LetterMaterial);
        Label(flow,"接收方 patrol_technician；用途 help_this_reply；版本 "+material.Version+"。不授权公开街道地址、见面或其它证言。",18);
        Button(flow,"向作者明确请求所选当前字段",()=>Execute(async()=>{var fields=ImmutableArray.CreateBuilder<string>();if(draft.ButtonPressed)fields.Add("current_draft");if(intent.ButtonPressed)fields.Add("correspondence_intent");CheckCommit(await _r2Life.RequestLetterScopeAsync(fields.ToImmutable(),"patrol_technician","help_this_reply",material.Version));ClosePanel();}));
    }
    private void OpenR2LifeLayout()
    {
        if(_r2Life is null)return;var flow=Panel("逐床试种配置",InputOwner.Confirmation);Label(flow,"四叶两根或两叶四根均可；估计供餐与储藏有取舍，选中配置尚未种植。",18);var choices=_r2Life.Contract.BedIds.ToDictionary(id=>id,id=>R2LifeChoice(flow,id,"请选择","较早叶菜","较晚储藏根菜"));
        Button(flow,"明确标出当前六床配置",()=>Execute(async()=>{if(choices.Values.Any(c=>c.Selected==0))throw new InvalidOperationException("每个实际床位须明确选择作物。");CheckCommit(await _r2Life.SetLayoutAsync(choices.Select(c=>new BedAssignment(c.Key,c.Value.Selected==1?TrialCrop.QuickLeaves:TrialCrop.StorageRoots)).ToImmutableArray()));ClosePanel();}));
    }
    private void OpenR2LifeRevision()
    {
        if(_r2Life is null)return;var flow=Panel("待议试种修订",InputOwner.Editable);var note=new TextEdit{CustomMinimumSize=new(0,160),AccessibilityName="试种修订请求"};flow.AddChild(note);Label(flow,"保存申请保持待种植者共识，不授第三方案，不实际种植。",18);Button(flow,"提出具体待议修订",()=>Execute(async()=>{if(Composing())throw new InvalidOperationException("请先完成输入法候选。");CheckCommit(await _r2Life.RequestLayoutRevisionAsync(note.Text));ClosePanel();}));
    }
    private void OpenR2LifeAuthorReview()
    {
        if(_r2Life is null)return;var flow=Panel("作者审阅当前保存稿",InputOwner.Confirmation);Label(flow,"此岗位只会决定当前短确认或留稿。玩家的意思确认不能代替作者决定；这不是英语质量评测。",18);var choice=R2LifeChoice(flow,"作者当前处理路径","请选择","短确认经保管人转送","保留修订稿");
        Button(flow,"向作者提交当前保存稿供决定",()=>Execute(async()=>{if(choice.Selected==0)throw new InvalidOperationException("请明确提出本次短确认或留稿路径。");if(!await SaveR2LifeDraft())throw new InvalidOperationException("当前稿尚未保存。");CheckCommit(await _r2Life.RequestAuthorReviewAsync(choice.Selected==1?ReplyDecision.SendShortAcknowledgement:ReplyDecision.KeepRevision));ClosePanel();}));
    }
    private async ValueTask OpenR2LifeWriter(string quest)
    {
        if(_r2Life is null)return;if(!await SaveR2LifeDraft())throw new InvalidOperationException("当前地方原稿保存尚未完成。");var task=_r2Life.Writing.Single(w=>w.Quest==quest);var view=await _r2Life.ViewAsync();if(!_r2Life.Geometry[view.Actor.Scene].Spots.Any(s=>s.Id==task.Spot&&s.Stand.Distance(view.Actor.Position)<=2.5))throw new InvalidOperationException("请先到原登记写作工位。");
        if(!_r2LifeEditors.TryGetValue(quest,out var editor)){var head=await _store.GetDraftAsync(task.Id);var session=_store.Session;editor=new(new R2LifeDraftStore(_r2Life,quest,()=>_store.Session),new(session.Profile,session.Branch,session.Load,task.Id,head?.Id,head?.Revision??0,head?.Body??"",_r2Life.Contract.ContentVersion));_r2LifeEditors[quest]=editor;}
        string title="地方英文短说明 · "+quest;if(_routing.Contains(title)){while(_routing.Top!=title)ClosePanel();_r2LifeWriter?.GrabFocus();return;}var flow=Panel(title,InputOwner.Editable);Label(flow,task.EnglishPrompt,22);Label(flow,task.ChinesePrompt,18);var writer=new TextEdit{Text=editor.Text,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,280),AccessibilityName=quest+"地方英文短说明"};writer.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(writer);_r2LifeWriter=writer;var status=Label(flow,"从空稿或原保存稿开始；短说明不输出能力评分。",18);
        var timer=new Godot.Timer{OneShot=true,WaitTime=.5};flow.AddChild(timer);timer.Timeout+=()=>{if(Composing()){timer.Start(.2);return;}_=SaveR2LifeDraft();};writer.TextChanged+=()=>{editor.Edit(writer.Text);status.Text="有未保存修改。";foreach(var token in _r2LifePreviewTokens.Values)_r2Life.ClosePreview(token);_r2LifePreviewTokens.Clear();timer.Start();};writer.FocusExited+=()=>{if(!Composing())_=SaveR2LifeDraft();};
        var back=flow.GetChild<HBoxContainer>(0).GetChild<Button>(1);back.Pressed-=ClosePanel;back.Pressed+=()=>Execute(async()=>{if(!await SaveR2LifeDraft())throw new InvalidOperationException("当前地方原稿尚未保存，请完成输入法候选、重试保存或导出；窗口保留。");ClosePanel();});
        Button(flow,"导出当前地方英文短说明",()=>{if(Composing()){_status.Text="请先完成输入法候选，当前窗口保留。";return;}string directory=ProjectSettings.GlobalizePath("user://exports");Directory.CreateDirectory(directory);string path=Path.Combine(directory,quest+"-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+Guid.NewGuid().ToString("N")+".txt");File.WriteAllText(path,writer.Text,new System.Text.UTF8Encoding(false));writer.SetMeta("last_export_path",path);_status.Text="当前地方正文已导出："+path;});
        Button(flow,"保存地方英文短说明",()=>Execute(async()=>{if(!await SaveR2LifeDraft())throw new InvalidOperationException("保存未完成，原稿保留。");status.Text="已收到本机保存回执。";}));Button(flow,"明确当前说明的意思",()=>Execute(async()=>{if(!await SaveR2LifeDraft())throw new InvalidOperationException("请先保存。");if(quest==R2LifeRules.Reply)OpenR2LifeReplyMeaning();else{CheckCommit(await _r2Life.ConfirmMeaningAsync(quest,true));status.Text="当前完整实际种植与正文已明确；正式受理另行预览。";}}));writer.GrabFocus();
    }
    private void OpenR2LifeReplyMeaning()
    {
        if(_r2Life is null)return;var flow=Panel("当前回信的明确意思",InputOwner.Confirmation);var invite=R2LifeChoice(flow,"回信邀请范围","请选择","仅通信","来访与通信");var visit=R2LifeChoice(flow,"来访承诺","请选择","不承诺来访","承诺来访");var address=R2LifeChoice(flow,"地址发布范围","请选择","不公开街道地址","公开街道地址");var delivery=R2LifeChoice(flow,"送达路径","请选择","保管人沿既有房间代码","公开看板全文");
        Button(flow,"明确确认当前稿的这些意思（结构化辅助）",()=>Execute(async()=>{if(new[]{invite,visit,address,delivery}.Any(c=>c.Selected==0))throw new InvalidOperationException("各字段须明确选择。");if(!await SaveR2LifeDraft())throw new InvalidOperationException("当前地方原稿尚未保存，意思确认未执行。");CheckCommit(await _r2Life.ConfirmMeaningAsync(R2LifeRules.Reply,true,new(invite.Selected==1?"correspondence_only":"visit_and_correspondence",visit.Selected==1?"not_promised":"promised",address.Selected==1?"not_public":"public",delivery.Selected==1?"custodian_existing_room_code":"public_board")));ClosePanel();}));
    }
    private async ValueTask OpenR2LifeSubmission(string quest)
    {
        if(_r2Life is null)return;if(!await SaveR2LifeDraft())throw new InvalidOperationException("当前地方稿保存尚未完成。");var preview=await _r2Life.PreviewSubmissionAsync(quest);string title="地方正式提交预览 · "+quest;var flow=Panel(title,InputOwner.Confirmation);if(!preview.Ready){foreach(string issue in preview.Issues)Label(flow,issue,20);return;}_r2LifePreviewTokens[title]=preview.Token;
        Label(flow,quest==R2LifeRules.Reply?"只接收作者明确决定的当前短确认或留稿；不承诺来访、不公开地址。零奖励。":"只接收当前完整六床实际种植和说明。原八/四或四/八筐仍为条件估计，不入玩家或塔站库存。零奖励。",20);
        Button(flow,"明确提交当前地方结果",()=>Execute(async()=>{if(!await SaveR2LifeDraft())throw new InvalidOperationException("当前地方原稿尚未保存，正式提交未执行。");CheckCommit(await _r2Life.ConfirmPreviewAsync(preview.Token));ClosePanel();await Refresh();}));
    }
}

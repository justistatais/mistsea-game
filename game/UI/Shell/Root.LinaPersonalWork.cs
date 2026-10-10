using Godot;
using Mist.Application.CompanionArcs;
using Mist.Application.Contracts;
using Mist.Application.Drafts;
using Mist.Application.LinaPersonalWorks;
using Mist.Application.Storage;
using Mist.Application.UI;
using Mist.Core.Contracts;
using Mist.Core.LinaPersonalWorks;

public partial class Root
{
    private LinaPersonalWorkService? _linaWork;
    private DraftEditor? _linaLetterEditor;
    private TextEdit? _linaLetterWriter;
    private Task<bool>? _linaSaveTask;
    private readonly Dictionary<string,Guid> _linaWorkPreviews=[];
    private LinaPersonalWorkService LinaWork()=>_linaWork??=new(_store,_catalog,LinaWorkPackage.Parse(Godot.FileAccess.GetFileAsString("res://content/lina_personal_player.json")));
    private sealed class LinaLetterDraftStore(LinaPersonalWorkService service,Func<SessionView> session):IDraftStore
    {
        public ValueTask<DraftSaved> SaveAsync(SaveDraft command,CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested();var current=session();
            if(command.Profile!=current.Profile||command.Branch!=current.Branch||command.Load!=current.Load||command.MaterialVersion!=service.Package.Version||command.TaskInstanceId!=LinaPersonalWorkService.LetterTask)throw new StoreConflictException("旧载入的信件编辑器不能写入当前冒险，正文保留。");
            return service.SaveLetterAsync(command.Draft,command.ExpectedHeadRevision,command.Text);
        }
    }
    private void CloseLinaWorkPreview(string title){if(_linaWorkPreviews.Remove(title,out var token))_linaWork?.ClosePreview(token);}
    private void ResetLinaWorkUi(){foreach(var token in _linaWorkPreviews.Values)_linaWork?.ClosePreview(token);_linaWorkPreviews.Clear();_linaWork=null;_linaLetterEditor=null;_linaLetterWriter=null;_linaSaveTask=null;}
    private async Task<bool> SaveLinaLetter()
    {
        if(Composing())return false;if(_linaSaveTask is{IsCompleted:false})return await _linaSaveTask;_linaSaveTask=SaveLinaLetterCore();return await _linaSaveTask;
    }
    private async Task<bool> SaveLinaLetterCore()
    {
        try{if(_linaLetterEditor is{IsSaved:false})await _linaLetterEditor.SaveAsync();return !Composing()&&(_linaLetterEditor?.IsSaved??true);}
        catch(Exception e){_status.Text="询问信未保存，窗口和正文保留；可导出当前稿："+e.Message;return false;}
    }
    private static string LinaMaterialName(string id)=>id switch
    {
        "LI01-APPENDIX"=>"LT-17历史转移附页","LI01-INDEX"=>"I-LT-17独立索引","LI02-RELAY"=>"保管人有限中继说明","LI02-DIRECT"=>"本人直接书信答复","LI02-DELIVERY"=>"同一封原信送达回执","LI03-REPLY"=>"弟弟当前本人回信","LI03-MEETING"=>"单独询问本次见面意愿","LI03-TOPICS"=>"莉娜对有限公开话题的答复",_=>id
    };
    private async ValueTask OpenLinaWorkList()
    {
        var service=LinaWork();var states=await service.ViewAsync();var flow=Panel("莉娜 · 当前受邀的个人工作",InputOwner.Workspace);
        Label(flow,"历史转移、当前送达、本人回复分别核验。每项保留原公开门槛与前项结果；是否入队不代替私人协助许可。原固定奖励各结算一次。",20);
        foreach(string node in new[]{"CQ-LINA-01","CQ-LINA-02","CQ-LINA-03"})
        {
            string id=node;Label(flow,id+(states.Any(s=>s.Node==id)?" · 已受理":" · 尚未受理"),20);
            Button(flow,"莉娜明确邀请本项有限协助 · "+id,()=>Execute(async()=>{if(!await SaveLinaLetter())throw new InvalidOperationException("当前信件未保存。");CheckCommit(await service.InviteAsync(id,true));_status.Text="本人仅许可这一项有限工作；请另外受理。";}));
            Button(flow,"撤回本项协助许可 · "+id,()=>Execute(async()=>{if(!await SaveLinaLetter())throw new InvalidOperationException("当前信件未保存。");CheckCommit(await service.InviteAsync(id,false));CloseAllPanels();}));
            Button(flow,"受理或打开同一项 · "+id,()=>Execute(async()=>{CheckCommit(await service.AcceptAsync(id));await OpenLinaWorkNode(id);}));
        }
        Button(flow,"只读查看当前获准材料",()=>Execute(OpenLinaWorkArchive));
        Label(flow,"操作地点：第一项港口记录室或甲板值守点；第二项修会范围/回信台或船内通信端；第三项芬的通信台或甲板值守点。",18);
    }
    private async ValueTask OpenLinaWorkArchive()
    {var flow=Panel("莉娜 · 当前获准原文",InputOwner.Workspace);foreach(var m in await LinaWork().AccessibleMaterialsAsync()){string id=m.Id;Button(flow,LinaMaterialName(id),()=>Execute(async()=>await OpenLinaMaterial(id)));}}
    private async ValueTask OpenLinaMaterial(string id)
    {
        var m=await LinaWork().OpenMaterialAsync(id);var flow=Panel("莉娜原文 · "+LinaMaterialName(id),InputOwner.Workspace);Label(flow,m.Version+" · 原创候选，人工语言审校待完成",18);
        var reader=new TextEdit{Text=m.English,Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,330),AccessibilityName="莉娜当前获准英文原件"};reader.SetMeta("mistsea_base_font",22);reader.AddThemeFontSizeOverride("font_size",22*_scale/100);flow.AddChild(reader);AddRegionalDictionary(flow,reader);
        Button(flow,"查看当前原件中文辅助",()=>Execute(async()=>Label(flow,(await LinaWork().OpenMaterialAsync(id)).Chinese,20)));
    }
    private async ValueTask OpenLinaWorkNode(string node)
    {
        var service=LinaWork();var flow=Panel("莉娜实际工作 · "+node,InputOwner.Workspace);
        foreach(var m in service.Package.Materials.Where(m=>m.Node==node)){string id=m.Id;Button(flow,"实际取得并读取 · "+LinaMaterialName(id),()=>Execute(async()=>{CheckCommit(await service.AcquireAsync(id));await OpenLinaMaterial(id);}));}
        if(node=="CQ-LINA-01")Button(flow,"比较历史附页与独立索引",()=>OpenLinaTransfer());
        if(node=="CQ-LINA-02")
        {
            Button(flow,"编辑本人的有限询问信",()=>Execute(OpenLinaLetter));Button(flow,"明确核对当前保存稿意思",()=>Execute(OpenLinaLetterMeaning));
            foreach(string path in new[]{"custodian","direct"}){string choice=path;Button(flow,path=="custodian"?"实际转交同一封信给保管中继":"依据本人有限答复实际发送直接书信",()=>Execute(async()=>{if(!await SaveLinaLetter())throw new InvalidOperationException("询问信未保存。");CheckCommit(await service.DispatchLetterAsync(choice));_status.Text="同一封原信已转交；请另行取得实际送达回执。";}));}
        }
        if(node=="CQ-LINA-03")
        {
            Button(flow,"核验当前本人回信的限定范围",()=>OpenLinaReplyCheck());
            foreach(string actor in new[]{"CP-LINA","N-LINA-BROTHER"})foreach(ContactPurpose purpose in Enum.GetValues<ContactPurpose>())
            {
                string who=actor;ContactPurpose use=purpose;string title=who+" · "+(use==ContactPurpose.Meeting?"本次见面":"继续通信");
                Button(flow,"分别记录本人当前同意 · "+title,()=>Execute(async()=>{CheckCommit(await service.ConfirmCurrentContactAsync(who,use));_status.Text="只记录这位本人当前所选用途。";}));
                Button(flow,"本人当前暂缓或撤回 · "+title,()=>Execute(async()=>{CheckCommit(await service.ConfirmCurrentContactAsync(who,use,true));_status.Text="当前用途已暂缓，旧许可不再有效。";}));
            }
            foreach(string topic in new[]{"folding_only","keep_original_words"})foreach(bool allowed in new[]{true,false})
            {string purpose=topic;bool decision=allowed;Button(flow,(decision?"本人许可有限公开话题 · ":"本人撤回有限公开话题 · ")+purpose,()=>Execute(async()=>{CheckCommit(await service.DecidePublicTopicAsync(purpose,decision));_status.Text="只记录这个有限话题，信件正文与地址不公开。";}));}
        }
        foreach(string outcome in new Mist.Application.CompanionArcs.CompanionArcService(_store,_catalog).Definitions[node].Outcomes){string choice=outcome;Button(flow,"只读预览原结果 · "+choice,()=>Execute(async()=>await OpenLinaWorkPreview(node,choice)));}
    }
    private OptionButton LinaChoice(VBoxContainer flow,string name,params string[] choices)
    {Label(flow,name,20);var option=new OptionButton{AccessibilityName=name,FitToLongestItem=false};option.AddItem("尚未明确选择");foreach(string choice in choices)option.AddItem(choice);flow.AddChild(option);return option;}
    private void OpenLinaTransfer()
    {
        var flow=Panel("莉娜 · 两份历史原件比较",InputOwner.Confirmation);var transfer=LinaChoice(flow,"转移记录编号","LT-17","LT-71");var index=LinaChoice(flow,"独立索引编号","I-LT-17","I-LT-71");var time=LinaChoice(flow,"时间范围","只支持历史转移","支持当前住址");var current=LinaChoice(flow,"当前状况","仍未知","已证明当前存活和住址");
        Button(flow,"明确保存这次实际比较",()=>Execute(async()=>{CheckCommit(await LinaWork().VerifyTransferAsync(transfer.Selected==1?"LT-17":"",index.Selected==1?"I-LT-17":"",time.Selected==1?"historical_transfer_only":"",current.Selected==1?"unknown":""));ClosePanel();}));
    }
    private async ValueTask OpenLinaLetter()
    {
        const string title="莉娜 · 本人的询问信";if(_routing.Contains(title)){while(_routing.Top!=title)ClosePanel();_linaLetterWriter?.GrabFocus();return;}
        var service=LinaWork();if(_linaLetterEditor is null){var d=await _store.GetDraftAsync(LinaPersonalWorkService.LetterTask);var s=_store.Session;_linaLetterEditor=new(new LinaLetterDraftStore(service,()=>_store.Session),new(s.Profile,s.Branch,s.Load,LinaPersonalWorkService.LetterTask,d?.Id,d?.Revision??0,d?.Body??"",service.Package.Version));}
        var flow=Panel(title,InputOwner.Editable);Label(flow,"Ask whether the sealed LT-17 recipient wishes to exchange letters now. Keep the address private and do not promise a visit. Free prose starts from your saved draft; meaning is confirmed separately.",22);
        var editor=_linaLetterEditor;var writer=new TextEdit{Text=editor.Text,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,300),AccessibilityName="莉娜询问信英文正文"};_linaLetterWriter=writer;flow.AddChild(writer);
        var timer=new Godot.Timer{OneShot=true,WaitTime=.5};flow.AddChild(timer);timer.Timeout+=()=>{if(Composing()){timer.Start(.2);return;}_=SaveLinaLetter();};
        writer.TextChanged+=()=>{editor.Edit(writer.Text);foreach(var token in _linaWorkPreviews.Values)service.ClosePreview(token);_linaWorkPreviews.Clear();timer.Start();};
        var back=flow.GetChild<HBoxContainer>(0).GetChild<Button>(1);back.Pressed-=ClosePanel;back.Pressed+=()=>Execute(async()=>{if(!await SaveLinaLetter())throw new InvalidOperationException("正文未可靠保存，窗口保留。");ClosePanel();});
        Button(flow,"保存当前询问信正文",()=>Execute(async()=>{if(!await SaveLinaLetter())throw new InvalidOperationException("正文未保存，窗口保留。");_status.Text="当前询问信已收到实际保存回执。";}));
        Button(flow,"导出当前询问信正文",()=>Execute(async()=>{string folder=Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),"MistSeaExports");Directory.CreateDirectory(folder);string file=Path.Combine(folder,"lina-letter-"+DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff")+".txt");await System.IO.File.WriteAllTextAsync(file,editor.Text,new System.Text.UTF8Encoding(false));_status.Text="原稿已导出："+file;}));
        _linaLetterWriter.GrabFocus();
    }
    private async ValueTask OpenLinaLetterMeaning()
    {
        if(!await SaveLinaLetter())throw new InvalidOperationException("正文未保存。");var flow=Panel("莉娜 · 当前信件意思确认",InputOwner.Confirmation);
        var recipient=LinaChoice(flow,"收信范围","密封LT-17收件人","公开全体居民");var purpose=LinaChoice(flow,"询问用途","本人当前书信意愿","要求本人见面");var address=LinaChoice(flow,"住址处理","不公开","发布现址");var visit=LinaChoice(flow,"来访承诺","不作承诺","已承诺见面");
        Button(flow,"明确记录当前稿意思",()=>Execute(async()=>{if(!await SaveLinaLetter())throw new InvalidOperationException("当前稿未保存。");CheckCommit(await LinaWork().ConfirmLetterMeaningAsync(recipient.Selected==1?"sealed_LT17_recipient":"",purpose.Selected==1?"ask_current_contact_preference":"",address.Selected==1?"not_public":"",visit.Selected==1?"not_promised":"",false));ClosePanel();}));
        Label(flow,"当前离线候选不自动理解英文，不据这些选项生成英语能力分数。",18);
    }
    private void OpenLinaReplyCheck()
    {
        var flow=Panel("莉娜 · 当前本人回信核验",InputOwner.Confirmation);var sender=LinaChoice(flow,"当前发送人","N-LINA-BROTHER本人","旧记录代答");var purpose=LinaChoice(flow,"本人当前答复范围","愿意有限书信","已经同意见面");var address=LinaChoice(flow,"私人住址","保留私密","可以公开");var meeting=LinaChoice(flow,"见面状态","还须分别问双方","由通信答复推定");
        Button(flow,"明确核验当前回信",()=>Execute(async()=>{CheckCommit(await LinaWork().VerifyReplyAsync(sender.Selected==1?"N-LINA-BROTHER":"",purpose.Selected==1?"current_correspondence_preference":"",address.Selected!=1,meeting.Selected!=1));ClosePanel();}));
    }
    private async ValueTask OpenLinaWorkPreview(string node,string outcome)
    {
        if(!await SaveLinaLetter())throw new InvalidOperationException("当前询问信未保存。");var p=await LinaWork().PreviewAsync(node,outcome);string title="莉娜原结果预览 · "+node;var flow=Panel(title,InputOwner.Confirmation);_linaWorkPreviews[title]=p.Token;
        Label(flow,"当前原件、许可、实例和实际工作符合："+outcome+"。预览不结算；关闭取消；任何稿件或世界修改会使预览失效。",22);
        Button(flow,"明确提交莉娜同一项实际结果",()=>Execute(async()=>{if(!await SaveLinaLetter())throw new InvalidOperationException("当前信件未保存。");CheckCommit(await LinaWork().ConfirmPreviewAsync(p.Token));CloseAllPanels();_status.Text="莉娜实际个人工作已收束，原奖励只结算一次。";}));
    }
}

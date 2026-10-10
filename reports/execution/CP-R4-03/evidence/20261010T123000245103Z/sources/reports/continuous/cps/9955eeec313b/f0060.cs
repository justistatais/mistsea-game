using Godot;
using Mist.Application.ChapterFour;
using Mist.Application.Drafts;
using Mist.Application.UI;
using Mist.Core.ChapterFour;

public partial class Root
{
    private ExhibitionCreditService? _chapterFourCredit;
    private readonly Dictionary<string,Stack<Guid>> _chapterFourCreditPreviews=new();
    private ExhibitionCreditService ChapterFourCredit()
    {
        if(_chapterFour is null)throw new InvalidOperationException("学城实际到达尚未建立。");
        if(_chapterFourCredit is not null)return _chapterFourCredit;
        using var json=Godot.FileAccess.Open("res://content/chapter_four/optional_credit.json",Godot.FileAccess.ModeFlags.Read);
        if(json is null)throw new InvalidDataException("贡献展签候选尚未安装；原文和草稿仍可保存。");
        return _chapterFourCredit=new(_store,_chapterFour,ExhibitionCreditPackage.ParseJson(json.GetAsText()));
    }
    private void CloseChapterFourCreditPreview(string title)
    {if(_chapterFourCreditPreviews.TryGetValue(title,out var stack)&&stack.TryPop(out var token))_chapterFourCredit?.ClosePreview(token);}
    private void ResetChapterFourCreditUi()
    {foreach(var stack in _chapterFourCreditPreviews.Values)foreach(var token in stack)_chapterFourCredit?.ClosePreview(token);_chapterFourCreditPreviews.Clear();_chapterFourCredit=null;}
    private async ValueTask<bool> TryChapterFourCreditAction(string action)
    {if(action!="save_optional_credit")return false;await OpenChapterFourCreditWriter();return true;}
    private async ValueTask<bool> TryChapterFourCreditInteraction(string spot)
    {
        if(_chapterFour is null||spot is not("R4-O02.label" or "R4-O02.revision"))return false;
        var service=ChapterFourCredit();var view=await service.ViewAsync();var flow=Panel("贡献展示室 · 现场工作",InputOwner.Workspace);
        Label(flow,"这是一项零奖励地方候选。公共贡献岗位、真实卡片、现场安装和实际接收分别记录；不会认证试验安全或增加主线资格。",18);
        foreach(var m in _chapterFour.Package.Materials.Where(m=>m.Spot==spot)){string id=m.Id;Button(flow,"查看留存地区材料："+m.Title,()=>Execute(async()=>{CheckCommit(await _chapterFour.DiscoverAsync(id));await OpenChapterFourMaterial(id);}));}
        foreach(var m in service.Package.Materials.Where(m=>m.Spot==spot))
        {string id=m.Id;Button(flow,(view.Materials.Any(a=>a.Id==id)?"查看已得来源：":"实际读取来源：")+m.Title,()=>Execute(async()=>{CheckCommit(await service.DiscoverAsync(id));await OpenChapterFourCreditMaterial(id);}));}
        if(spot=="R4-O02.revision")
        {
            Button(flow,"请求本次限定展签委托",()=>Execute(async()=>{CheckCommit(await service.AcceptCommissionAsync());_status.Text="本次有限公共岗位修订许可已实际登记。";}));
            Button(flow,"实际取出本份空白展示卡",()=>Execute(async()=>CheckCommit(await service.TakeCardAsync())));
            Button(flow,"核对三种实际贡献岗位",OpenChapterFourCreditComparison);
            Button(flow,"编辑同一份展签提议",()=>Execute(OpenChapterFourCreditWriter));
            Button(flow,"预览工作台制作当前卡片",()=>Execute(async()=>await OpenChapterFourCreditPreview(ExhibitionCreditOperation.Print)));
            Button(flow,"预览组织岗位接收实际已挂卡",()=>Execute(async()=>await OpenChapterFourCreditPreview(ExhibitionCreditOperation.Receive)));
        }
        else
        {
            Button(flow,"将持有的制成卡挂到原签旁",()=>Execute(async()=>CheckCommit(await service.MountCardAsync())));
            Button(flow,"在展位对照留存来源检查已挂卡",()=>Execute(async()=>CheckCommit(await service.InspectMountedAsync())));
        }
        Button(flow,"明确取回未接收候选，复用同一张卡",()=>Execute(async()=>CheckCommit(await service.ReturnCardToDeskAsync())));
        Button(flow,"查看已得展签来源与工作状态",()=>Execute(OpenChapterFourCreditArchive));
        return true;
    }
    private void AddChapterFourCreditArchiveButton(VBoxContainer flow)
    {if(_chapterFour is not null)Button(flow,"贡献展示室：已得来源与实际卡片",()=>Execute(OpenChapterFourCreditArchive));}
    private async ValueTask OpenChapterFourCreditArchive()
    {
        var view=await ChapterFourCredit().ViewAsync();var flow=Panel("贡献展签 · 已得来源与实际工作",InputOwner.Workspace);
        Label(flow,view.State is{} st?"实际卡片状态："+st.Stage+" · 制作版 "+st.CardRevision+" · 展位检查版 "+(st.InspectionRevision?.ToString()??"尚未检查"):"尚未实际取得限定展签工作。保存旧稿仍不等于交付。",20);
        foreach(var material in view.Materials){string id=material.Id;Button(flow,material.Title,()=>Execute(async()=>await OpenChapterFourCreditMaterial(id)));}
        if(view.Materials.IsEmpty)Label(flow,"还未在贡献展示室两个真实站位取得来源。",20);
        Label(flow,"原签、贡献簿及试验记录保留；没有经验、刻币、零件、能力分数或结局许可。",18);
        if(view.State?.Stage==ExhibitionCardStage.Received)
        {var receipt=view.Snapshot.Receipts.Single(r=>r.SemanticKey=="R4:optional-credit:resolution");Label(flow,"真实公共展签接收回执："+receipt.ReceiptId,18);}
    }
    private async ValueTask OpenChapterFourCreditMaterial(string id)
    {
        var m=await ChapterFourCredit().OpenMaterialAsync(id);var flow=Panel(m.Title,InputOwner.Workspace);Label(flow,"公共来源 "+m.Id+" · "+m.Version+" · 原创待人审",16);
        var reader=new TextEdit{Text=m.English,Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,350),AccessibilityName=m.Title+"原文"};reader.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(reader);
        Button(flow,"显示这份已得原文的中文辅助",()=>Label(flow,m.Chinese,20));Button(flow,"离线查词（词义候选）",OpenRegionalDictionarySearch);reader.GrabFocus();
    }
    private void OpenChapterFourCreditComparison()
    {
        var flow=Panel("展签与贡献簿 · 明确比较",InputOwner.Confirmation);
        var construction=ChapterFourChoice(flow,"外壳建造","尚未确认","工坊团队","介绍人");var calibration=ChapterFourChoice(flow,"参考检查岗位","尚未确认","学生参考检查","没有参考检查");
        var count=ChapterFourChoice(flow,"参考检查人数","尚未确认","一人","两人");var presentation=ChapterFourChoice(flow,"介绍岗位","尚未确认","介绍人","所有建造者");
        var only=ChapterFourChoice(flow,"发言是否等于唯一贡献","尚未确认","不等于","等于");var output=ChapterFourChoice(flow,"展签是否改写试验输出","尚未确认","不改写","改写");
        Button(flow,"明确记录当前来源比较",()=>Execute(async()=>
        {
            if(new[]{construction,calibration,count,presentation,only,output}.Any(x=>x.Selected==0))throw new InvalidOperationException("请逐项确认，不能沿用未知项。");
            CheckCommit(await ChapterFourCredit().CompareAsync(new(construction.Selected==1?"workshop_team":"presenter",calibration.Selected==1?"student_reference_checks":"none",count.Selected,
                presentation.Selected==1?"presenter":"all_builders",only.Selected==2,output.Selected==2)));ClosePanel();
        }));
    }
    private async ValueTask OpenChapterFourCreditWriter()
    {
        if(!await SaveChapterFourDraft())throw new InvalidOperationException("请先完成输入法候选并保存当前正文。");
        var service=ChapterFourCredit();const string key="R4-OPTIONAL-CREDIT";string title=ChapterFourWriterTitle(key);
        if(_routing.Contains(title)){while(_routing.Top!=title)ClosePanel();_chapterFourWriter?.GrabFocus();return;}
        if(!_chapterFourEditors.TryGetValue(key,out var editor))
        {var head=await _store.GetDraftAsync(service.Package.DraftTask);var session=_store.Session;editor=new DraftEditor(_store,new(session.Profile,session.Branch,session.Load,service.Package.DraftTask,head?.Id,head?.Revision??0,head?.Body??"",_chapterFour!.Package.Version));_chapterFourEditors.Add(key,editor);}
        var flow=Panel(title,InputOwner.Editable);Label(flow,"Propose a public display label using the retained construction, calibration and presentation roles. Keep unsupported additions as questions and preserve the original records.",22);
        Label(flow,"默认自由写作，正文从现有同一份草稿恢复。离线系统不自动理解英文；明确的岗位提议与实际制卡另行确认，零奖励。",18);
        var writer=new TextEdit{Text=editor.Text,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,330),AccessibilityName="展签修订英文正文"};writer.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(writer);_chapterFourWriter=writer;_chapterFourSaveStatus=Label(flow,editor.IsSaved?"当前正文已保存。":"正文待保存。",18);
        var timer=new Godot.Timer{OneShot=true,WaitTime=.5};flow.AddChild(timer);_chapterFourSaveTimer=timer;timer.Timeout+=()=>{if(Composing()){timer.Start(.2);return;}_=SaveChapterFourDraft();};
        writer.TextChanged+=()=>{editor.Edit(writer.Text);foreach(var stack in _chapterFourCreditPreviews.Values)foreach(var token in stack)service.ClosePreview(token);_chapterFourCreditPreviews.Clear();if(_chapterFourSaveStatus is{} label&&IsInstanceValid(label))label.Text="修改尚未保存。";timer.Start();};
        writer.FocusExited+=()=>{foreach(var physical in _keys.Values.Append(Key.Enter))if(Input.IsPhysicalKeyPressed(physical))_releaseBarrier.Add(physical);};
        var back=flow.GetChild<HBoxContainer>(0).GetChild<Button>(1);back.Pressed-=ClosePanel;back.Pressed+=()=>Execute(async()=>{if(!await SaveChapterFourDraft())throw new InvalidOperationException("正文尚未保存，请完成输入法组合或导出。");ClosePanel();});
        Button(flow,"保存当前展签正文",()=>Execute(async()=>{if(!await SaveChapterFourDraft())throw new InvalidOperationException("正文尚未保存。");}));
        Button(flow,"导出当前展签正文",()=>{string dir=ProjectSettings.GlobalizePath("user://exports");Directory.CreateDirectory(dir);string path=Path.Combine(dir,"R4-exhibition-"+Guid.NewGuid()+".txt");File.WriteAllText(path,editor.Text,new System.Text.UTF8Encoding(false));_status.Text="正文已导出："+path;});
        Button(flow,"明确核对当前提议意思",()=>Execute(async()=>await OpenChapterFourCreditMeaning(false)));Button(flow,"明确使用结构化辅助",()=>Execute(async()=>await OpenChapterFourCreditMeaning(true)));
        Button(flow,"预览实际制作当前卡片",()=>Execute(async()=>await OpenChapterFourCreditPreview(ExhibitionCreditOperation.Print)));writer.GrabFocus();
    }
    private async ValueTask OpenChapterFourCreditMeaning(bool assisted)
    {
        if(!await SaveChapterFourDraft())throw new InvalidOperationException("请完成输入法组合并保存正文。");var flow=Panel("贡献展签 · 明确意思确认",InputOwner.Confirmation);
        Label(flow,assisted?"这是明确结构化辅助，不计为独立英文理解；不替你制卡、安装或交付。":"当前离线没有自动理解英文；以下选择是你对当前稿件的明确提议。",20);
        var construction=ChapterFourChoice(flow,"建造岗位","尚未确认","工坊团队","介绍人");var count=ChapterFourChoice(flow,"参考检查人数","尚未确认","一人","两人");var presentation=ChapterFourChoice(flow,"介绍岗位","尚未确认","介绍人","仅建造者");
        var unknown=ChapterFourChoice(flow,"无来源的新增主张","尚未确认","保留为问题","写成已证事实");var history=ChapterFourChoice(flow,"原签与贡献簿","尚未确认","保留原件","删除原件");var safety=ChapterFourChoice(flow,"修订权限","尚未确认","只说明公共贡献岗位","批准试验安全");
        Button(flow,"明确保存当前提议意思",()=>Execute(async()=>
        {
            if(!await SaveChapterFourDraft()||new[]{construction,count,presentation,unknown,history,safety}.Any(x=>x.Selected==0))throw new InvalidOperationException("先保存正文并逐项明确选择。");
            CheckCommit(await ChapterFourCredit().ConfirmMeaningAsync(new(construction.Selected==1?"workshop_team":"presenter",count.Selected,presentation.Selected==1?"presenter":"builder_only",
                unknown.Selected==1?"leave_as_questions":"assert_unverified",history.Selected==1?"retain_originals":"erase",safety.Selected==1?"public_credit_only_not_trial_approval":"approve_trial"),assisted));ClosePanel();
        }));
    }
    private async ValueTask OpenChapterFourCreditPreview(ExhibitionCreditOperation operation)
    {
        if(!await SaveChapterFourDraft())throw new InvalidOperationException("当前正文尚未保存。");var service=ChapterFourCredit();var p=await service.PreviewAsync(operation);string title=operation==ExhibitionCreditOperation.Print?"贡献展签 · 实际制作预览":"贡献展签 · 组织岗位接收预览";
        var flow=Panel(title,InputOwner.Confirmation);Label(flow,"预览尚未执行。零经验、零刻币、零零件；不批准试验安全。自由正文未被自动评测，公开卡片按你明确确认的岗位字段排版。",18);
        foreach(var issue in p.Issues)Label(flow,issue.SafeMessage,20);if(!p.Ready)return;if(!_chapterFourCreditPreviews.TryGetValue(title,out var stack)){stack=new();_chapterFourCreditPreviews.Add(title,stack);}stack.Push(p.Token);Label(flow,p.PublicText??"",22);
        Button(flow,operation==ExhibitionCreditOperation.Print?"明确在工作台制成当前卡片":"明确由组织岗位接收实际已挂卡",()=>Execute(async()=>{if(!await SaveChapterFourDraft())throw new InvalidOperationException("正文尚未保存。");CheckCommit(await service.ConfirmPreviewAsync(p.Token));ClosePanel();_status.Text=operation==ExhibitionCreditOperation.Print?"当前真实卡片已制成；仍须亲自到展位安装、核对并回台交接。":"本份公共展签实际已接收，原件保留，没有奖励。";}));
    }
}

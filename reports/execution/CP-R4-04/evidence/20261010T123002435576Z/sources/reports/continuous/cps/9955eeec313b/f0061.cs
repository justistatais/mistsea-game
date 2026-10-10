using Godot;
using Mist.Application.ChapterFour;
using Mist.Application.Drafts;
using Mist.Application.UI;
using Mist.Core.ChapterFour;

public partial class Root
{
    private StudyInquiryService? _chapterFourStudy;
    private readonly Dictionary<string,Stack<Guid>> _chapterFourStudyPreviews=new();
    private StudyInquiryService ChapterFourStudy()
    {
        if(_chapterFour is null)throw new InvalidOperationException("需要当前存档的实际学城接入。");if(_chapterFourStudy is not null)return _chapterFourStudy;
        using var json=Godot.FileAccess.Open("res://content/chapter_four/optional_inquiry.json",Godot.FileAccess.ModeFlags.Read);if(json is null)throw new InvalidDataException("课程询问候选尚未安装，原文与旧草稿保留。");
        return _chapterFourStudy=new(_store,_chapterFour,StudyInquiryPackage.ParseJson(json.GetAsText()));
    }
    private void CloseChapterFourStudyPreview(string title)
    {if(_chapterFourStudyPreviews.TryGetValue(title,out var stack)&&stack.TryPop(out var token))_chapterFourStudy?.ClosePreview(token);}
    private void ResetChapterFourStudyUi()
    {foreach(var stack in _chapterFourStudyPreviews.Values)foreach(var token in stack)_chapterFourStudy?.ClosePreview(token);_chapterFourStudyPreviews.Clear();_chapterFourStudy=null;}
    private async ValueTask<bool> TryChapterFourStudyAction(string action)
    {if(action!="save_optional_inquiry")return false;await OpenChapterFourStudyWriter();return true;}
    private async ValueTask<bool> TryChapterFourStudyInteraction(string spot)
    {
        if(_chapterFour is null||spot is not("R4-O01.notices" or "R4-O01.inquiry"))return false;var service=ChapterFourStudy();var view=await service.ViewAsync();var flow=Panel("学生共学室 · 课程询问",InputOwner.Workspace);
        Label(flow,"课程台只受理公共安排，实际纸件交付和答复分别记录。不会改课表、认证已上课或增加主线与结局权益。",19);
        foreach(var material in _chapterFour.Package.Materials.Where(m=>m.Spot==spot)){string id=material.Id;Button(flow,"读取并保留课程通知原文",()=>Execute(async()=>{CheckCommit(await _chapterFour.DiscoverAsync(id));await OpenChapterFourMaterial(id);}));}
        if(spot=="R4-O01.notices")Button(flow,"在通知板明确比较原件与更正",OpenChapterFourStudyComparison);
        else
        {
            foreach(var m in service.Package.Materials){string id=m.Id;Button(flow,(view.Materials.Any(x=>x.Id==id)?"查看已得公共规程：":"实际读取公共规程：")+m.Title,()=>Execute(async()=>{CheckCommit(await service.DiscoverAsync(id));await OpenChapterFourStudyMaterial(id);}));}
            Button(flow,"申请本次有限公共课程询问工作",()=>Execute(async()=>CheckCommit(await service.AcceptCommissionAsync())));
            Button(flow,"从课程台领取本份空白纸",()=>Execute(async()=>CheckCommit(await service.TakePaperAsync())));
            Button(flow,"编辑同一份正式询问正文",()=>Execute(OpenChapterFourStudyWriter));
            Button(flow,"预览实际定稿封口",()=>Execute(async()=>await OpenChapterFourStudyPreview("seal")));
            Button(flow,"明确拆封未交付的同一纸件",()=>Execute(async()=>CheckCommit(await service.ReopenPaperAsync())));
            Button(flow,"预览交付当前封件到课程台",()=>Execute(async()=>await OpenChapterFourStudyPreview("deliver")));
            Button(flow,"实际领取课程台本份答复",()=>Execute(async()=>{CheckCommit(await service.TakeReplyAsync());await OpenChapterFourStudyReply();}));
        }
        Button(flow,"查看课程询问已得来源与纸件记录",()=>Execute(OpenChapterFourStudyArchive));return true;
    }
    private void AddChapterFourStudyArchiveButton(VBoxContainer flow)
    {if(_chapterFour is not null)Button(flow,"学生共学室：正式询问与已取答复",()=>Execute(OpenChapterFourStudyArchive));}
    private async ValueTask OpenChapterFourStudyArchive()
    {
        var service=ChapterFourStudy();var v=await service.ViewAsync();var flow=Panel("课程询问 · 已得记录",InputOwner.Workspace);Label(flow,v.State is{} st?"当前实际纸件状态："+st.Stage+" · 定稿版 "+st.Revision:"尚未实际领取限定课程工作；保存草稿不等于交付。",20);
        foreach(var m in v.Materials){string id=m.Id;Button(flow,m.Title,()=>Execute(async()=>await OpenChapterFourStudyMaterial(id)));}
        if(v.CollectedReply is not null)Button(flow,"查看已经实际领取的答复",()=>Execute(OpenChapterFourStudyReply));else Label(flow,"答复尚未实际领取；没有把课程时间冲突标成已解决。",19);
        Label(flow,"零经验、零刻币、零零件。纸件交到课程台保管，答复属于持件人；原通知与更正仍留存。",18);
    }
    private async ValueTask OpenChapterFourStudyMaterial(string id)
    {
        var m=await ChapterFourStudy().OpenMaterialAsync(id);var flow=Panel(m.Title,InputOwner.Workspace);Label(flow,m.Version+" · 公共原创文，人工审校待定",17);
        var reader=new TextEdit{Text=m.English,Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,350),AccessibilityName=m.Title+"英文原文"};reader.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(reader);Button(flow,"显示已得原文中文辅助",()=>Label(flow,m.Chinese,20));Button(flow,"离线查词（词义候选）",OpenRegionalDictionarySearch);reader.GrabFocus();
    }
    private async ValueTask OpenChapterFourStudyReply()
    {
        var service=ChapterFourStudy();var reply=await service.OpenReplyAsync();var flow=Panel("课程台 · 已领取书面答复",InputOwner.Workspace);Label(flow,"实际本地答复 · 当前后果 "+reply.ActualConsequence+" · 人工语言审校待定",18);
        var reader=new TextEdit{Text=reply.English,Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,300),AccessibilityName="课程台实际书面答复"};reader.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(reader);Button(flow,"显示这份答复的中文辅助",()=>Label(flow,reply.Chinese,20));Button(flow,"离线查词（词义候选）",OpenRegionalDictionarySearch);
        Label(flow,"查看不是英文理解测评；申请另一场次仍待分配，不自动生成上课记录。请在真实课程台确认收阅。",18);
        Button(flow,"明确确认已查看本份答复",()=>Execute(async()=>{CheckCommit(await service.AcknowledgeReplyAsync());ClosePanel();_status.Text="本地询问与有限答复已记录，通知、课表和主线奖励保持原有事实。";}));reader.GrabFocus();
    }
    private void OpenChapterFourStudyComparison()
    {
        var flow=Panel("课程通知 · 原件与更正比较",InputOwner.Confirmation);var old=ChapterFourChoice(flow,"原通知房间","尚未确认","Cedar","Birch");var corrected=ChapterFourChoice(flow,"更正房间","尚未确认","Cedar","Birch");var time=ChapterFourChoice(flow,"仪表练习时刻","尚未确认","14:00","15:00");var scope=ChapterFourChoice(flow,"更正对象","尚未确认","房间","取消课程");var cancel=ChapterFourChoice(flow,"是否证明课程取消","尚未确认","没有证明","已经取消");var conflict=ChapterFourChoice(flow,"是否解决同时段参加问题","尚未确认","未解决","已解决");
        Button(flow,"明确记录通知链比较",()=>Execute(async()=>{if(new[]{old,corrected,time,scope,cancel,conflict}.Any(c=>c.Selected==0))throw new InvalidOperationException("请逐项确认，不能默认填入答案。");CheckCommit(await ChapterFourStudy().CompareNoticesAsync(new(old.Selected==1?"Cedar":"Birch",corrected.Selected==1?"Cedar":"Birch",time.Selected==1?"14:00":"15:00",scope.Selected==1?"room_only":"cancel_course",cancel.Selected==2,conflict.Selected==2)));ClosePanel();}));
    }
    private async ValueTask OpenChapterFourStudyWriter()
    {
        if(!await SaveChapterFourDraft())throw new InvalidOperationException("请先完成输入法候选并保存当前正文。");var service=ChapterFourStudy();const string key="R4-OPTIONAL-STUDY";string title=ChapterFourWriterTitle(key);
        if(_routing.Contains(title)){while(_routing.Top!=title)ClosePanel();_chapterFourWriter?.GrabFocus();return;}
        if(!_chapterFourEditors.TryGetValue(key,out var editor)){var h=await _store.GetDraftAsync(service.Package.DraftTask);var s=_store.Session;editor=new DraftEditor(_store,new(s.Profile,s.Branch,s.Load,service.Package.DraftTask,h?.Id,h?.Revision??0,h?.Body??"",_chapterFour!.Package.Version));_chapterFourEditors.Add(key,editor);}
        var flow=Panel(title,InputOwner.Editable);Label(flow,"Write a formal enquiry about the retained notices. Keep the original and corrected rooms, preserve the published time and ask a bounded question that remains open.",22);Label(flow,"默认自由写作，恢复同一份草稿；课程台接收的是实际交付版本。系统不自动理解英文，问题与范围由你另行明确。",18);
        var writer=new TextEdit{Text=editor.Text,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,330),AccessibilityName="正式课程询问英文正文"};writer.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(writer);_chapterFourWriter=writer;_chapterFourSaveStatus=Label(flow,editor.IsSaved?"当前正文已保存。":"正文待保存。",18);
        var timer=new Godot.Timer{OneShot=true,WaitTime=.5};flow.AddChild(timer);_chapterFourSaveTimer=timer;timer.Timeout+=()=>{if(Composing()){timer.Start(.2);return;}_=SaveChapterFourDraft();};
        writer.TextChanged+=()=>{editor.Edit(writer.Text);foreach(var stack in _chapterFourStudyPreviews.Values)foreach(var token in stack)service.ClosePreview(token);_chapterFourStudyPreviews.Clear();if(_chapterFourSaveStatus is{} label&&IsInstanceValid(label))label.Text="当前修改尚未保存。";timer.Start();};writer.FocusExited+=()=>{foreach(var physical in _keys.Values.Append(Key.Enter))if(Input.IsPhysicalKeyPressed(physical))_releaseBarrier.Add(physical);};
        var back=flow.GetChild<HBoxContainer>(0).GetChild<Button>(1);back.Pressed-=ClosePanel;back.Pressed+=()=>Execute(async()=>{if(!await SaveChapterFourDraft())throw new InvalidOperationException("请完成输入法组合后保存，或导出当前正文。");ClosePanel();});
        Button(flow,"保存当前询问正文",()=>Execute(async()=>{if(!await SaveChapterFourDraft())throw new InvalidOperationException("正文尚未保存。");}));Button(flow,"导出当前询问正文",()=>{string dir=ProjectSettings.GlobalizePath("user://exports");Directory.CreateDirectory(dir);string path=Path.Combine(dir,"R4-study-"+Guid.NewGuid()+".txt");File.WriteAllText(path,editor.Text,new System.Text.UTF8Encoding(false));_status.Text="正文已导出："+path;});
        Button(flow,"明确核对当前询问意思",()=>Execute(async()=>await OpenChapterFourStudyMeaning(false)));Button(flow,"明确使用结构化辅助",()=>Execute(async()=>await OpenChapterFourStudyMeaning(true)));Button(flow,"预览实际纸件定稿",()=>Execute(async()=>await OpenChapterFourStudyPreview("seal")));writer.GrabFocus();
    }
    private async ValueTask OpenChapterFourStudyMeaning(bool assisted)
    {
        if(!await SaveChapterFourDraft())throw new InvalidOperationException("请先完成输入法组合并保存。");var flow=Panel("正式课程询问 · 明确意思",InputOwner.Confirmation);Label(flow,assisted?"这是明确结构化辅助，不认证独立英文理解，也不代交纸件。":"当前没有自动理解英文；下列选择是你对当前正文的明确问题。",20);
        var old=ChapterFourChoice(flow,"原房间","尚未确认","Cedar","Birch");var corrected=ChapterFourChoice(flow,"更正房间","尚未确认","Cedar","Birch");var time=ChapterFourChoice(flow,"保留时刻","尚未确认","14:00","15:00");var scope=ChapterFourChoice(flow,"更正范围","尚未确认","只改房间","取消课程");
        var request=ChapterFourChoice(flow,"待答问题","尚未选择","澄清公开安排与时间冲突","请求公开学习提纲","申请另一练习机会（待分配）","索取私人学生身份");var history=ChapterFourChoice(flow,"通知原件","尚未确认","保留原通知与更正","删除原通知");var safety=ChapterFourChoice(flow,"询问权限","尚未确认","只问公共课程安排","批准试验安全");
        Button(flow,"明确保存当前询问意思",()=>Execute(async()=>{if(!await SaveChapterFourDraft()||new[]{old,corrected,time,scope,request,history,safety}.Any(c=>c.Selected==0))throw new InvalidOperationException("先保存并逐项选择；待答问题没有默认答案。");CheckCommit(await ChapterFourStudy().ConfirmMeaningAsync(new(old.Selected==1?"Cedar":"Birch",corrected.Selected==1?"Cedar":"Birch",time.Selected==1?"14:00":"15:00",scope.Selected==1?"room_only":"cancel_course",new[]{"","schedule_clarification","public_outline","alternate_practice_request","private_student_identity"}[request.Selected],history.Selected==1?"retain_original_and_correction":"erase",safety.Selected==1?"public_course_arrangements_only":"approve_trial"),assisted));ClosePanel();}));
    }
    private async ValueTask OpenChapterFourStudyPreview(string operation)
    {
        if(!await SaveChapterFourDraft())throw new InvalidOperationException("当前正文尚未保存。");var service=ChapterFourStudy();var p=await service.PreviewAsync(operation);string title=operation=="seal"?"课程询问 · 实际定稿预览":"课程询问 · 实际交付预览";var flow=Panel(title,InputOwner.Confirmation);
        Label(flow,"预览尚未执行。正文未被自动评测；零经验、零刻币、零零件，不改课表或私人权限。",18);foreach(var issue in p.Issues)Label(flow,issue.SafeMessage,20);if(!p.Ready)return;
        if(!_chapterFourStudyPreviews.TryGetValue(title,out var stack)){stack=new();_chapterFourStudyPreviews.Add(title,stack);}stack.Push(p.Token);Label(flow,p.Description??"",22);
        Button(flow,operation=="seal"?"明确封口本份当前询问":"明确交本份封件到实际课程台",()=>Execute(async()=>{if(!await SaveChapterFourDraft())throw new InvalidOperationException("正文尚未保存。");CheckCommit(await service.ConfirmPreviewAsync(p.Token));ClosePanel();_status.Text=operation=="seal"?"本份纸已实际定稿，尚未交付。":"课程台已实际收件并准备有限答复，请实际领取。";}));
    }
}

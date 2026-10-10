using System.Collections.Immutable;
using Godot;
using Mist.Application.Drafts;
using Mist.Application.R1Life;
using Mist.Application.UI;
using Mist.Core.Contracts;
using Mist.Core.Exploration;
using Mist.Core.Quests;
using Mist.Core.R1Life;

public partial class Root
{
    private R1LifeService? _r1Life;
    private readonly Dictionary<string,DraftEditor> _r1LifeEditors=new();
    private readonly Dictionary<string,Guid> _r1LifePreviewTokens=new();
    private TextEdit? _r1LifeWriter;
    private Task<bool>? _r1LifeSaveTask;
    private void LoadR1Life(ImmutableDictionary<string,SceneGeometry> harbourGeometry)
    {
        using var source=Godot.FileAccess.Open("res://content/r1_life/learner_package.json",Godot.FileAccess.ModeFlags.Read);
        if(source is null)throw new InvalidDataException("盐雾港生活支线内容缺失，存档保留。");
        var service=new R1LifeService(_store,_store,R1LifePackage.Parse(source.GetAsText()),harbourGeometry);_r1Life=service;
        RegisterRegionalLearningScope("R1-life",async()=>(await _store.SnapshotAsync()).Entities.Any(e=>e.Id==R1LifeService.StateId)?(await service.ViewAsync()).Materials.Select(RegionalLearningAccess).ToArray():Array.Empty<Mist.Core.Learning.LearningSourceAccess>());
    }
    private void CloseR1LifeUiPreview(string title){if(_r1LifePreviewTokens.Remove(title,out var token))_r1Life?.ClosePreview(token);}
    private void ResetR1LifeUiState(){foreach(var token in _r1LifePreviewTokens.Values)_r1Life?.ClosePreview(token);_r1LifePreviewTokens.Clear();_r1LifeEditors.Clear();_r1LifeWriter=null;_r1LifeSaveTask=null;}
    private async Task<bool> SaveR1LifeDraft()
    {
        if(Composing())return false;if(_r1LifeSaveTask is{IsCompleted:false})return await _r1LifeSaveTask;_r1LifeSaveTask=SaveR1LifeDraftCore();return await _r1LifeSaveTask;
    }
    private async Task<bool> SaveR1LifeDraftCore()
    {
        bool saved=true;foreach(var editor in _r1LifeEditors.Values.ToArray().Where(e=>!e.IsSaved))try{await editor.SaveAsync();}catch(Exception error){saved=false;_status.Text="地方留言尚未保存，原稿和窗口保留，可导出："+error.Message;}
        bool current=saved&&!Composing()&&_r1LifeEditors.Values.All(e=>e.IsSaved);if(!current&&saved)_status.Text="当前新修改尚未保存，旧回执不会关闭正文窗口。";return current;
    }
    private async ValueTask OpenR1LifeQuests()
    {
        if(_r1Life is null)return;var view=await _r1Life.ViewAsync();var flow=Panel("盐雾港生活委托",InputOwner.Workspace);
        Label(flow,"两份相同的订房 · 最后一场戏。可选生活工作，可暂停；不会锁定港口主线或结局。",20);
        foreach(var quest in view.Quests)
        {
            Label(flow,quest.Id+" · "+quest.Lifecycle,22);
            if(quest.Lifecycle==QuestLifecycle.Active)
            {
                string id=quest.Id;Button(flow,"查看"+id+"留言工位",()=>{var task=_r1Life.Package.Writing.Single(t=>t.Quest==id);Label(flow,"先走到"+task.Spot+"，正式提交在"+(id==R1LifeRules.Rooms?"R1-O01.front_desk":"R1-O02.programme"),20);});
                Button(flow,"暂停"+id,()=>Execute(async()=>{if(!await SaveR1LifeDraft())throw new InvalidOperationException("当前地方正文尚未保存，暂停未执行，窗口保留。");CheckCommit(await _r1Life.PauseAsync(id));ClosePanel();}));
            }
        }
        Button(flow,"查看当前已得生活档案",()=>Execute(OpenR1LifeArchive));
    }
    private async ValueTask OpenR1LifeArchive()
    {
        if(_r1Life is null)return;var flow=Panel("盐雾港生活档案",InputOwner.Workspace);foreach(var material in (await _r1Life.ViewAsync()).Materials){string id=material.Id;Button(flow,material.Title,()=>Execute(async()=>await OpenR1LifeMaterial(id)));}
    }
    private async ValueTask OpenR1LifeMaterial(string id)
    {
        if(_r1Life is null)return;var material=await _r1Life.OpenMaterialAsync(id);var flow=Panel(material.Title,InputOwner.Workspace);
        Label(flow,"原创地方候选 · 目标读听写各6.5 · 人工校准待完成。取得原文不等于完成工作。",18);
        var text=new TextEdit{Text=material.English,Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,280),AccessibilityName=material.Title+"英文原文"};flow.AddChild(text);text.AddThemeFontSizeOverride("font_size",20*_scale/100);
        Button(flow,"查看派生中文辅助",()=>Label(flow,material.Chinese,20));
        Button(flow,"查询选中文本的离线词义",()=>Execute(async()=>
        {
            string selected=text.GetSelectedText();if(string.IsNullOrWhiteSpace(selected))throw new InvalidOperationException("请先选择需要查阅的词或短语。");
            await OpenRegionalDictionary(selected,async()=>new[]{RegionalLearningAccess(await _r1Life.OpenMaterialAsync(id))});
        }));
        AddRegionalAudioButton(flow,material,()=>_r1Life.OpenMaterialAsync(id));
    }
    private async ValueTask<bool> TryR1LifeInteraction(string spot)
    {
        if(_r1Life is null||!(_r1Life.Package.Stations.Any(s=>s.Spot==spot)||_r1Life.Package.Materials.Any(m=>m.Spot==spot)||_r1Life.Package.Writing.Any(w=>w.Spot==spot)))return false;
        var view=await _r1Life.ViewAsync();var flow=Panel("地方现场 · "+spot,InputOwner.Workspace);
        foreach(var material in _r1Life.Package.Materials.Where(m=>m.Spot==spot))
        {
            if(material.Id=="R1-SHOW-R-SCENE"&&!view.Quests.Any(q=>q.Id==R1LifeRules.Show&&q.Outcome==QuestOutcome.Completed))continue;
            string id=material.Id;Button(flow,(view.Materials.Any(m=>m.Id==id)?"查看已得材料：":"实际领取/读取：")+material.Title,()=>Execute(async()=>{CheckCommit(await _r1Life.DiscoverAsync(id));await OpenR1LifeMaterial(id);}));
        }
        foreach(var station in _r1Life.Package.Stations.Where(s=>s.Spot==spot)){string action=station.Action;Button(flow,station.Label,()=>Execute(async()=>await OpenR1LifeStation(action)));}
        foreach(var writing in _r1Life.Package.Writing.Where(w=>w.Spot==spot)){string quest=writing.Quest;Button(flow,"编辑地方英文留言",()=>Execute(async()=>await OpenR1LifeWriter(quest)));}
        if(spot is "R1-O01.front_desk" or "R1-O02.programme")
        {
            string quest=spot=="R1-O01.front_desk"?R1LifeRules.Rooms:R1LifeRules.Show;var q=view.Quests.Single(q=>q.Id==quest);
            if(q.Lifecycle==QuestLifecycle.Unaccepted)Button(flow,"明确拒绝这项可选请求",()=>Execute(async()=>{if(!await SaveR1LifeDraft())throw new InvalidOperationException("当前地方正文尚未保存，拒绝未执行，窗口保留。");CheckCommit(await _r1Life.DeclineAsync(quest));ClosePanel();}));
            if(q.Lifecycle==QuestLifecycle.Active)Button(flow,"恢复同一地方委托",()=>Execute(async()=>{CheckCommit(await _r1Life.ResumeAsync(quest));ClosePanel();}));
        }
        return true;
    }
    private async ValueTask OpenR1LifeStation(string action)
    {
        if(_r1Life is null)return;
        switch(action)
        {
            case "arrange_rooms":OpenR1LifeRooms();return;
            case "assign_show_roles":OpenR1LifeRoles();return;
            case "submit_rooms":await OpenR1LifeSubmission(R1LifeRules.Rooms);return;
            case "submit_show":await OpenR1LifeSubmission(R1LifeRules.Show);return;
            case "watch_show":
                var flow=Panel("恢复后的短场景",InputOwner.Workspace);Button(flow,"观看已安排节目",()=>Execute(async()=>{CheckCommit(await _r1Life.WatchShowAsync(false));CheckCommit(await _r1Life.DiscoverAsync("R1-SHOW-R-SCENE"));await OpenR1LifeMaterial("R1-SHOW-R-SCENE");}));Button(flow,"跳过演出并保留实际结果",()=>Execute(async()=>{CheckCommit(await _r1Life.WatchShowAsync(true));ClosePanel();}));return;
        }
        var result=action switch
        {
            "discover_inn"=>await _r1Life.DiscoverInnAsync(),"discover_theatre"=>await _r1Life.DiscoverTheatreAsync(),
            "accept_rooms"=>await _r1Life.AcceptAsync(R1LifeRules.Rooms),"accept_show"=>await _r1Life.AcceptAsync(R1LifeRules.Show),
            "confirm_arrived_guests"=>await _r1Life.ConfirmArrivedGuestsAsync(),"inspect_quiet_room"=>await _r1Life.InspectRoomAsync("QT-1"),"inspect_ground_room"=>await _r1Life.InspectRoomAsync("GS-1"),
            "inspect_stage"=>await _r1Life.InspectStageAsync(),"isolate_theatre_lamp"=>await _r1Life.LampActionAsync("isolate"),"install_theatre_brace"=>await _r1Life.LampActionAsync("brace"),
            "install_theatre_cell"=>await _r1Life.LampActionAsync("cell"),"power_theatre_lamp"=>await _r1Life.LampActionAsync("power"),"test_theatre_lamp"=>await _r1Life.LampActionAsync("test"),
            "place_screen"=>await _r1Life.PlacePropAsync("screen"),"place_drum"=>await _r1Life.PlacePropAsync("drum"),_=>throw new InvalidOperationException("地方行动未登记。")
        };CheckCommit(result);ClosePanel();await Refresh();
    }
    private static OptionButton R1LifeChoice(VBoxContainer flow,string name,params string[] options)
    {var choice=new OptionButton{AccessibilityName=name,FitToLongestItem=false};foreach(var option in options)choice.AddItem(option);flow.AddChild(choice);return choice;}
    private void OpenR1LifeRooms()
    {
        if(_r1Life is null)return;var flow=Panel("明确分房与账本更正",InputOwner.Confirmation);
        Label(flow,"编号与修订关系决定有效订单；现场房牌决定床位和通行属性。这里记录实际安排，不自动解释英文。",18);
        var first=R1LifeChoice(flow,"BKG-17房间","请选择","QT-1 静室双人间","GS-1 平层单人间");var second=R1LifeChoice(flow,"BKG-22房间","请选择","QT-1 静室双人间","GS-1 平层单人间");var extra=R1LifeChoice(flow,"一次额外安排","请选择","旅舍账内住宿折让","一次当地信息服务");
        Button(flow,"明确记录两笔当前订单的实际分房",()=>Execute(async()=>
        {
            if(first.Selected==0||second.Selected==0||extra.Selected==0)throw new InvalidOperationException("请选择两组的房间及一次额外安排。");
            CheckCommit(await _r1Life.ArrangeRoomsAsync([new("BKG-17",first.Selected==1?"QT-1":"GS-1"),new("BKG-22",second.Selected==1?"QT-1":"GS-1")],extra.Selected==1?ExtraLodgingArrangement.LodgingDiscount:ExtraLodgingArrangement.LocalInformation));ClosePanel();
        }));
    }
    private void OpenR1LifeRoles()
    {
        if(_r1Life is null)return;var flow=Panel("明确安排节目与两人分工",InputOwner.Confirmation);
        var programme=R1LifeChoice(flow,"实际节目","请选择","完整室内节目","白天小型节目");var speaker=R1LifeChoice(flow,"讲述者","请选择","舞台组织者","第二位演员");var role=R1LifeChoice(flow,"第二岗位","请选择","灯具操作员","鼓手");var person=R1LifeChoice(flow,"第二岗位人员","请选择","第二位演员","舞台组织者");var time=R1LifeChoice(flow,"节目时段","请选择","18:00–18:30","16:00–16:15");
        Button(flow,"明确记录实际节目和分工",()=>Execute(async()=>
        {
            if(new[]{programme,speaker,role,person,time}.Any(o=>o.Selected==0))throw new InvalidOperationException("各项须明确选择。");
            CheckCommit(await _r1Life.AssignRolesAsync(new(programme.Selected==1?TheatreProgramme.FullIndoor:TheatreProgramme.SmallDaylight,speaker.Selected==1?R1LifeRules.Speaker:R1LifeRules.Second,role.Selected==1?"lamp_operator":"drummer",person.Selected==1?R1LifeRules.Second:R1LifeRules.Speaker,time.Selected==1?"18:00":"16:00",time.Selected==1?"18:30":"16:15")));ClosePanel();
        }));
    }
    private async ValueTask OpenR1LifeWriter(string quest)
    {
        if(_r1Life is null)return;if(!await SaveR1LifeDraft())throw new InvalidOperationException("先完成输入法候选并保存当前地方正文。");var task=_r1Life.Package.Writing.Single(w=>w.Quest==quest);var view=await _r1Life.ViewAsync();
        if(!view.Actor.Scene.Equals(task.Spot.Split('.')[0],StringComparison.Ordinal)||!_r1Life.Geometry[view.Actor.Scene].Spots.Any(s=>s.Id==task.Spot&&s.Stand.Distance(view.Actor.Position)<=2.5))throw new InvalidOperationException("请先到登记留言工位。");
        if(!_r1LifeEditors.TryGetValue(quest,out var editor)){var head=await _store.GetDraftAsync(task.Id);var session=_store.Session;editor=new(_store,new(session.Profile,session.Branch,session.Load,task.Id,head?.Id,head?.Revision??0,head?.Body??"",_r1Life.Package.Version));_r1LifeEditors[quest]=editor;}
        string title="地方正文 · "+quest;if(_routing.Contains(title)){while(_routing.Top!=title)ClosePanel();_r1LifeWriter?.GrabFocus();return;}var flow=Panel(title,InputOwner.Editable);Label(flow,task.EnglishPrompt,22);Label(flow,task.ChinesePrompt,18);Label(flow,"短留言不输出能力分数。确认意思与正式受理分开，旅舍在前台，剧场在节目牌。",18);
        var writer=new TextEdit{Text=editor.Text,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,280),AccessibilityName=quest+"地方英文留言"};writer.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(writer);_r1LifeWriter=writer;
        var status=Label(flow,"正文从空稿或原保存稿开始。",18);var timer=new Godot.Timer{OneShot=true,WaitTime=.5};flow.AddChild(timer);timer.Timeout+=()=>{if(Composing()){timer.Start(.2);return;}_=SaveR1LifeDraft();};writer.TextChanged+=()=>{editor.Edit(writer.Text);status.Text="有未保存修改。";foreach(var token in _r1LifePreviewTokens.Values)_r1Life.ClosePreview(token);_r1LifePreviewTokens.Clear();timer.Start();};writer.FocusExited+=()=>{if(!Composing())_=SaveR1LifeDraft();};
        var back=flow.GetChild<HBoxContainer>(0).GetChild<Button>(1);back.Pressed-=ClosePanel;back.Pressed+=()=>Execute(async()=>{if(!await SaveR1LifeDraft())throw new InvalidOperationException("当前地方正文尚未保存，请完成输入法候选、重试保存或导出；窗口保留。");ClosePanel();});
        Button(flow,"导出当前地方英文留言",()=>{if(Composing()){_status.Text="请先完成输入法候选，当前窗口保留。";return;}string directory=ProjectSettings.GlobalizePath("user://exports");Directory.CreateDirectory(directory);string path=Path.Combine(directory,quest+"-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+Guid.NewGuid().ToString("N")+".txt");File.WriteAllText(path,writer.Text,new System.Text.UTF8Encoding(false));writer.SetMeta("last_export_path",path);_status.Text="当前地方正文已导出："+path;});
        Button(flow,"保存地方英文留言",()=>Execute(async()=>{if(!await SaveR1LifeDraft())throw new InvalidOperationException("保存未完成，正文保留。");status.Text="已收到本机保存回执。";}));
        Button(flow,"明确确认当前地方安排（结构化辅助，不评分）",()=>Execute(async()=>{if(!await SaveR1LifeDraft())throw new InvalidOperationException("请先保存。");CheckCommit(await _r1Life.ConfirmMeaningAsync(quest,true));status.Text="当前正文版本的地方安排已明确确认；去实际接收站位提交。";}));writer.GrabFocus();
    }
    private async ValueTask OpenR1LifeSubmission(string quest,bool correspondence=false)
    {
        if(_r1Life is null)return;if(!await SaveR1LifeDraft())throw new InvalidOperationException("请先保存当前地方正文。");var preview=await _r1Life.PreviewSubmissionAsync(quest,correspondence);string title="地方提交预览 · "+quest;var flow=Panel(title,InputOwner.Confirmation);
        if(!preview.Ready){foreach(var issue in preview.Issues)Label(flow,issue,20);return;}_r1LifePreviewTokens[title]=preview.Token;
        Label(flow,quest==R1LifeRules.Rooms?"店主将接收已核对住宿和当前留言。既有25经验只结算一次，补偿由旅舍承担。":"组织者将接收当前实际设备、两人分工、时段和节目说明。此生活演出零经验、零刻币、零零件。",20);
        Button(flow,"明确提交当前地方结果",()=>Execute(async()=>{if(!await SaveR1LifeDraft())throw new InvalidOperationException("当前地方正文尚未保存，正式提交未执行。");CheckCommit(await _r1Life.ConfirmPreviewAsync(preview.Token));ClosePanel();await Refresh();}));
    }
}

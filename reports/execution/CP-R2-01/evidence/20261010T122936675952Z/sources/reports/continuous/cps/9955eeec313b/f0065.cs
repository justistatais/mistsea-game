using System.Collections.Immutable;
using Godot;
using Mist.Application.ChapterTwo;
using Mist.Application.Drafts;
using Mist.Application.UI;
using Mist.Core.ChapterTwo;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Quests;

public partial class Root
{
    private string? _chapterTwoSelectedEncounter;
    private bool ChapterTwoEncounterSelected=>_chapterTwo is not null&&_chapterTwoSelectedEncounter is not null;
    private readonly Dictionary<string,DraftEditor> _chapterTwoEditors=new();
    private readonly Dictionary<string,Stack<Guid>> _chapterTwoUiPreviews=new();
    private TextEdit? _chapterTwoWriter;
    private Label? _chapterTwoSaveStatus;
    private Godot.Timer? _chapterTwoSaveTimer;
    private Task<bool>? _chapterTwoSaveTask;
    private static string ChapterTwoWriterTitle(string quest)=>"第二章正文 · "+quest;
    private static string ChapterTwoQuestName(string quest)=>quest switch{"Q-201"=>"地方住民与名册差异","Q-202"=>"转移记录与时间比较","Q-203"=>"地方供能与塔站安全","Q-204"=>"事实交付与范围协议","CQ-MIRA-01"=>"W-04限定副本与保管查验",_=>quest};
    private void TrackChapterTwoPreview(string title,Guid token){if(!_chapterTwoUiPreviews.TryGetValue(title,out var stack)){stack=new();_chapterTwoUiPreviews[title]=stack;}stack.Push(token);}
    private void CloseChapterTwoUiPreview(string title)
    {
        if(_chapterTwoUiPreviews.TryGetValue(title,out var stack)&&stack.TryPop(out var token))_chapterTwo?.ClosePreview(token);
        if(title.StartsWith("第二章正文 · ",StringComparison.Ordinal)){_chapterTwoWriter=null;_chapterTwoSaveStatus=null;_chapterTwoSaveTimer=null;_=SaveChapterTwoDraft();}
    }
    private void ResetChapterTwoUiState()
    {
        foreach(var stack in _chapterTwoUiPreviews.Values)foreach(var token in stack)_chapterTwo?.ClosePreview(token);
        _chapterTwoUiPreviews.Clear();_chapterTwoEditors.Clear();_chapterTwoSelectedEncounter=null;_chapterTwoWriter=null;_chapterTwoSaveStatus=null;_chapterTwoSaveTimer=null;_chapterTwoSaveTask=null;
    }
    private void ChapterTwoButton(VBoxContainer flow,string caption,Func<ValueTask> action,bool close=true)=>Button(flow,caption,()=>Execute(async()=>{await action();if(close)ClosePanel();}));
    private static OptionButton ChapterTwoChoice(VBoxContainer flow,string name,params string[] choices)
    {
        var option=new OptionButton{FitToLongestItem=false,AccessibilityName=name};foreach(string choice in choices)option.AddItem(choice);flow.AddChild(option);return option;
    }
    private static string Selected(OptionButton option,string[] values)=>option.Selected>=0&&option.Selected<values.Length?values[option.Selected]:"";
    private async ValueTask<bool> TryChapterTwoInteraction(string spot)
    {
        if(_chapterTwo is null||!spot.StartsWith("R2-",StringComparison.Ordinal))return false;
        var materials=_chapterTwo.Package.Materials.Where(m=>m.Spot==spot).ToArray();var stations=_chapterTwo.Package.Stations.Where(s=>s.Spot==spot).ToArray();
        if(materials.Length==0&&stations.Length==0&&spot is not("R2-S01.berth" or "R2-S08.return_check"))return false;
        var view=await _chapterTwo.ViewAsync();var flow=Panel("第二章现场 · "+spot,InputOwner.Workspace);Label(flow,"原创灰盒候选 · 内容人审待完成。操作须在当前实际安全站位，读到说明不会代替处置。",18);
        foreach(var material in materials)
        {
            string id=material.Id;Button(flow,(view.Materials.Any(m=>m.Id==id)?"查看已得材料：":"实际领取/读取：")+material.Title,()=>Execute(async()=>{CheckCommit(await _chapterTwo.DiscoverAsync(id));await OpenChapterTwoMaterial(id);}));
        }
        foreach(var station in stations)Button(flow,station.Label,()=>Execute(async()=>await OpenChapterTwoStation(station.Action)));
        if(spot=="R2-S06.conveyor")Button(flow,"输送器实际手动四步",()=>OpenChapterTwoConveyor());
        if(spot is "R2-S07.branch_a" or "R2-S07.branch_b")
        {
            string branch=spot.EndsWith("_a",StringComparison.Ordinal)?"a":"b";
            ChapterTwoButton(flow,"实际固定"+branch.ToUpperInvariant()+"支路外侧阀",async()=>CheckCommit(await _chapterTwo.PerformCoreStepAsync("secure_"+branch)));
            ChapterTwoButton(flow,"处置后复查"+branch.ToUpperInvariant()+"支路外侧阀",async()=>CheckCommit(await _chapterTwo.PerformCoreStepAsync("inspect_valve_"+branch)));
        }
        if(spot is "R2-S01.berth" or "R2-S02.june" or "R2-S08.return_check")ChapterTwoButton(flow,"登记当前实际安全返程点",async()=>CheckCommit(await _chapterTwo.RegisterCurrentReturnAnchorAsync()));
        return true;
    }
    private async ValueTask OpenChapterTwoStation(string action)
    {
        if(_chapterTwo is null)return;
        switch(action)
        {
            case "submit_difference_request":await OpenChapterTwoWorkspace("Q-201");return;
            case "submit_transfer_report":await OpenChapterTwoWorkspace("Q-202");return;
            case "submit_local_safety_report":await OpenChapterTwoWorkspace("Q-203");return;
            case "submit_archive_handover":await OpenChapterTwoWorkspace("Q-204");return;
            case "submit_w04_handover":await OpenChapterTwoWorkspace("CQ-MIRA-01");return;
            case "request_controlled_review":OpenChapterTwoRecordScope();return;
            case "confirm_m12_order":OpenChapterTwoM12();return;
            case "verify_d1_chain":OpenChapterTwoD1();return;
            case "decide_mira_invitation":OpenChapterTwoMira();return;
            case "decide_w04_invitation":OpenChapterTwoW04Invitation();return;
            case "request_w04_scope":OpenChapterTwoW04Scope();return;
            case "begin_root_route_encounter":await OpenChapterTwoEncounterEntry("ENC-R2-01");return;
            case "begin_or_perform_conveyor_procedure":OpenChapterTwoConveyor();return;
            case "begin_core_procedure":await OpenChapterTwoCore();return;
            case "set_local_selector":OpenChapterTwoLoad();return;
            case "propose_optional_reply":case "propose_optional_planting":
                var pending=Panel("第二章可选候选工作",InputOwner.Workspace);Label(pending,"该地点和公开材料已制作。私人合同、本人许可与固定奖励仍待原任务定义核定，当前不能用填槽提交冒充完成。",22);return;
            default:CheckCommit(await _chapterTwo.ExecuteStationAsync(action));ClosePanel();_status.Text="实际操作已记录；当前配置改变后，旧检查不会替代重测。";return;
        }
    }
    private async ValueTask OpenChapterTwoQuests()
    {
        if(_chapterTwo is null)return;var view=await _chapterTwo.ViewAsync();var flow=Panel("第二章工作与当前状态",InputOwner.Workspace);Label(flow,"当前候选只核验世界操作和明确提议；英文能力、听力音轨与人工内容审校待完成。",18);
        foreach(var quest in view.Quests)
        {
            Label(flow,ChapterTwoQuestName(quest.Id)+" · "+quest.Lifecycle+(quest.Outcome is{} outcome?" / "+outcome:""),22);if(quest.Lifecycle==QuestLifecycle.Active){string id=quest.Id;Button(flow,"编辑 "+id+" 同一份正文",()=>Execute(async()=>await OpenChapterTwoWriter(id)));Button(flow,"核对 "+id+" 当前提交预览",()=>Execute(async()=>await OpenChapterTwoSubmission(id)));}
        }
        Label(flow,$"住地确认 {new[]{view.State.VisitA,view.State.VisitB,view.State.VisitC}.Count(v=>v)}/3 · 当前上层安全 {view.State.UpperSafe} · 当前地方供能 {view.State.LocalStable}",20);
        Label(flow,"Q202与Q203可分别推进。当前安全回执与完整塔改造分别核验；历史署名保留待查范围，导师当前状态未知。",20);
        foreach(string id in new[]{"ENC-R2-01","ENC-R2-02","ENC-R2-03"})if(await _chapterTwo.EncounterAsync(id) is{} encounter){string key=id;Button(flow,"继续查看 "+id+" · "+encounter.Battle.Outcome,()=>Execute(async()=>{_chapterTwoSelectedEncounter=key;await OpenBattle();}));}
    }
    private async ValueTask OpenChapterTwoArchive()
    {
        if(_chapterTwo is null)return;var view=await _chapterTwo.ViewAsync();var flow=Panel("第二章已得档案",InputOwner.Workspace);if(view.Materials.Length==0)Label(flow,"尚未在实际现场取得当前获准的材料。",22);
        foreach(var material in view.Materials){string id=material.Id;Button(flow,material.Title,()=>Execute(async()=>await OpenChapterTwoMaterial(id)));}
        Button(flow,"工作与当前状态",()=>Execute(OpenChapterTwoQuests));
    }
    private async ValueTask OpenChapterTwoMaterial(string id)
    {
        if(_chapterTwo is null)return;var material=await _chapterTwo.OpenMaterialAsync(id);var flow=Panel("第二章材料 · "+material.Title,InputOwner.Workspace);Label(flow,material.Scene+" · "+material.Version+" · 原创未人审",18);
        var reader=new TextEdit{Text=material.English,Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,350),AccessibilityName=material.Title+"英文原文"};reader.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(reader);AddRegionalDictionary(flow,reader);
        Label(flow,material.Kind=="listening_script"?"当前查看书面脚本；候选音轨须另行打开并实际播放，本次读取不会记为已听录音。":"取得原文与得出结论分别记录；当前来源不能扩展为未知人物现状。",18);
        AddRegionalAudioButton(flow,material,()=>_chapterTwo.OpenMaterialAsync(id));
        Button(flow,"查看中文辅助",()=>Label(flow,material.Chinese,20));if(_chapterTwo.Package.WritingTasks.Any(t=>t.Quest==material.Quest))Button(flow,"编辑对应的同一份正文",()=>Execute(async()=>await OpenChapterTwoWriter(material.Quest)));
    }
    private async ValueTask OpenChapterTwoWorkspace(string quest)
    {
        var flow=Panel("第二章工作台 · "+ChapterTwoQuestName(quest),InputOwner.Workspace);Label(flow,"正文可以提前保存。实体、材料、当前许可与明确意思均会在提交时重新核验。",22);Button(flow,"编辑同一份正文",()=>Execute(async()=>await OpenChapterTwoWriter(quest)));Button(flow,"明确核对正文意思",()=>Execute(async()=>await OpenChapterTwoMeaning(quest,false)));Button(flow,"明确使用结构化辅助",()=>Execute(async()=>await OpenChapterTwoMeaning(quest,true)));Button(flow,"查看当前提交预览",()=>Execute(async()=>await OpenChapterTwoSubmission(quest)));if(quest=="Q-204")Button(flow,"选择实际交付范围",OpenChapterTwoDisclosure);
    }
    private async ValueTask OpenChapterTwoWriter(string quest)
    {
        if(quest is "R2-O01" or "R2-O02"){await OpenR2LifeWriter(quest);return;}
        if(_chapterTwo is null)return;if(!await SaveChapterTwoDraft())throw new InvalidOperationException("请先完成输入法候选并保存当前正文。");string title=ChapterTwoWriterTitle(quest);
        if(_routing.Contains(title)){while(_routing.Top!=title)ClosePanel();_chapterTwoWriter?.GrabFocus();return;}
        var task=_chapterTwo.Package.WritingTasks.Single(t=>t.Quest==quest);if(!_chapterTwoEditors.TryGetValue(quest,out var editor)){var head=await _store.GetDraftAsync(task.Id);var session=_store.Session;editor=new(_store,new(session.Profile,session.Branch,session.Load,task.Id,head?.Id,head?.Revision??0,head?.Body??"",_chapterTwo.Package.Version));_chapterTwoEditors.Add(quest,editor);}
        var flow=Panel(title,InputOwner.Editable);Label(flow,task.EnglishPrompt,22);Button(flow,"查看任务中文辅助",()=>Label(flow,task.ChinesePrompt,20));Label(flow,"读、听、写目标各6.5；当前不自动理解英文，不生成能力分数。自由写作从空稿开始，结构化辅助须另行明确选择。",18);
        if(task.Visual is{} table)
        {
            var view=await _chapterTwo.ViewAsync();if(view.Materials.Any(m=>m.Id=="R2-Q202-R-COHORT")){Label(flow,table.Title+"\n"+string.Join(" | ",table.Columns)+"\n"+string.Join("\n",table.Rows.Select(r=>$"{r.Label} | {r.RollEntries} | {r.ResidenceConfirmations}")),20);}else Label(flow,"比较图表须在现场实际取得并获准查看对照材料后显示。",18);
        }
        var writer=new TextEdit{Text=editor.Text,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,330),AccessibilityName=quest+"英文正文"};writer.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(writer);_chapterTwoWriter=writer;_chapterTwoSaveStatus=Label(flow,editor.IsSaved?"当前正文与保存版本一致。":"当前正文待保存。",18);
        var timer=new Godot.Timer{OneShot=true,WaitTime=.5};flow.AddChild(timer);_chapterTwoSaveTimer=timer;timer.Timeout+=()=>{if(Composing()){timer.Start(.2);return;}_=SaveChapterTwoDraft();};writer.TextChanged+=()=>{editor.Edit(writer.Text);if(_chapterTwoSaveStatus is{} status&&IsInstanceValid(status))status.Text="有未保存修改。";foreach(var stack in _chapterTwoUiPreviews.Values)foreach(var token in stack)_chapterTwo.ClosePreview(token);_chapterTwoUiPreviews.Clear();timer.Start();};
        writer.FocusExited+=()=>{foreach(var key in _keys.Values.Append(Key.Enter))if(Input.IsPhysicalKeyPressed(key))_releaseBarrier.Add(key);};
        var back=flow.GetChild<HBoxContainer>(0).GetChild<Button>(1);back.Pressed-=ClosePanel;back.Pressed+=()=>Execute(async()=>{if(!await SaveChapterTwoDraft())throw new InvalidOperationException("正文尚未保存，完成输入法候选后重试或导出。");ClosePanel();});
        Button(flow,"保存第二章正文",()=>Execute(async()=>{if(!await SaveChapterTwoDraft())throw new InvalidOperationException("正文保存尚未完成。");}));Button(flow,"导出当前第二章正文",()=>{string dir=ProjectSettings.GlobalizePath("user://exports");Directory.CreateDirectory(dir);string path=Path.Combine(dir,quest+"-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+".txt");File.WriteAllText(path,editor.Text,new System.Text.UTF8Encoding(false));_status.Text="当前正文已导出："+path;});Button(flow,"明确核对正文意思",()=>Execute(async()=>await OpenChapterTwoMeaning(quest,false)));Button(flow,"明确使用结构化辅助",()=>Execute(async()=>await OpenChapterTwoMeaning(quest,true)));Button(flow,"查看当前提交预览",()=>Execute(async()=>await OpenChapterTwoSubmission(quest)));writer.GrabFocus();
    }
    private async ValueTask<bool> SaveChapterTwoDraft()
    {
        if(_chapterTwoEditors.Count==0)return true;if(Composing())return false;if(_chapterTwoSaveTask is not null&&!_chapterTwoSaveTask.IsCompleted)return await _chapterTwoSaveTask;_chapterTwoSaveTask=SaveChapterTwoDraftCore();return await _chapterTwoSaveTask;
    }
    private async Task<bool> SaveChapterTwoDraftCore()
    {
        try{foreach(var editor in _chapterTwoEditors.Values)if(!editor.IsSaved)await editor.SaveAsync();bool saved=_chapterTwoEditors.Values.All(e=>e.IsSaved);if(_chapterTwoSaveStatus is{} label&&IsInstanceValid(label))label.Text=saved?"正文已由实际保存回执确认。":"修改尚未全数保存，旧回执不会覆盖新稿。";if(!saved&&_chapterTwoSaveTimer is{} timer&&IsInstanceValid(timer))timer.Start(.2);return saved;}
        catch(Exception error){if(_chapterTwoSaveStatus is{} label&&IsInstanceValid(label))label.Text="保存失败，正文仍可导出："+error.Message;_status.Text="第二章正文保存失败："+error.Message;return false;}
    }
    private async ValueTask OpenChapterTwoMeaning(string quest,bool assisted)
    {
        if(quest is "R2-O01" or "R2-O02"){if(!await SaveR2LifeDraft())throw new InvalidOperationException("当前地方正文尚未保存。");if(quest=="R2-O01")OpenR2LifeReplyMeaning();else CheckCommit(await _r2Life!.ConfirmMeaningAsync(quest,assisted));return;}
        if(_chapterTwo is null)return;if(!await SaveChapterTwoDraft())throw new InvalidOperationException("请先完成输入法候选并保存正文。");var flow=Panel("第二章意思确认 · "+quest,InputOwner.Confirmation);Label(flow,assisted?"这是明确结构化辅助，不计作独立自由英文理解。所选意思仍须与实际来源、权限和设备结果共同核验。":"当前离线模式没有自动理解英文。请逐项确认你写下的意思；尚未确认的项不会填入默认答案。",22);
        var rows=ChapterTwoMeaningRows(quest);var choices=new List<OptionButton>();foreach(var row in rows){Label(flow,row.Name,20);choices.Add(ChapterTwoChoice(flow,row.Name,row.Labels));}
        Button(flow,"明确记录当前第二章提议",()=>Execute(async()=>{if(!await SaveChapterTwoDraft())throw new InvalidOperationException("当前正文尚未保存。");var fields=rows.Select((r,i)=>(r.Key,Value:Selected(choices[i],r.Values))).ToImmutableDictionary(x=>x.Key,x=>x.Value);if(fields.Values.Any(string.IsNullOrWhiteSpace))throw new InvalidOperationException("请逐项明确选择，不能沿用未确认项。");CheckCommit(await _chapterTwo.ConfirmMeaningAsync(quest,fields,assisted));ClosePanel();_status.Text="当前稿件的明确提议已记录，尚未提交世界结果。";}));
    }
    private sealed record ChapterTwoMeaningRow(string Key,string Name,string[] Labels,string[] Values);
    private static ChapterTwoMeaningRow[] ChapterTwoMeaningRows(string quest)=>quest switch
    {
        "Q-201"=>[new("scope","报告范围",["尚未确认","本地已核查群体","全世界住民"],["","local_group","global_population"]),new("register_meaning","名册分类与当前居住",["尚未确认","分类不直接等于当前居住","分类足以确定现状"],["","classification_not_current_presence","classification_proves_presence"]),new("motive","记录差异的动机",["尚未确认","尚未确定","已知所有人的隐瞒意图"],["","unknown","all_intent_known"])],
        "Q-202"=>[new("mara_time_scope","导师观察时间范围",["尚未确认","只证明D−1","证明当前"],["","D-1_only","current"]),new("mara_current_status","导师当前状态",["尚未确认","未知","已确认存活","已确认死亡"],["","unknown","alive","dead"]),new("identity_accountability","身份字段与责任查验",["尚未确认","分别限定处理","删除所有责任查验"],["","separate_fields","erase_accountability"]),new("comparison","图表两列含义",["尚未确认","记录条目与居住确认计量不同","两列可直接视作同一计量"],["","different_measures","identical_measures"])],
        "Q-203"=>[new("power_scope","供能范围",["尚未确认","当前地方回路","六塔全部供给"],["","local_only","all_six_towers"]),new("tower_preparation","塔站工作边界",["尚未确认","当前安全，尚未完成改造","所有工程阶段完成"],["","safe_not_converted","fully_converted"]),new("load_configuration","报告对应的实际档位",["尚未确认","厨房","洗涤","轮替","并联","塔站出口"],["","Kitchen","Wash","Alternating","Concurrent","TowerOutlet"])],
        "Q-204"=>[new("mara_current_status","导师当前状态",["尚未确认","未知","目前已找到"],["","unknown","found_current"]),new("signature_scope","历史署名的证明范围",["尚未确认","历史线索，范围待查","足以裁定全部责任"],["","historical_needs_verification","complete_guilt"]),new("public_scrutiny","公共事实核查",["尚未确认","保留公共核查路线","删除责任字段"],["","preserved","erased"]),new("mining_direction","下一核查线索",["尚未确认","材料中的MC-2批号","已获得矿区结论"],["","MC-2","mine_truth_known"])],
        "CQ-MIRA-01"=>[new("path","实际交付方式",["尚未确认","限定副本","保管人陪同查验","公开完整个人档案"],["","limited_copy","custodian_relay","full_private_publication"]),new("recipient","本次接收方",["尚未确认","修会复核台","所有公开看板"],["","priory_review_desk","public_boards"]),new("purpose","本次用途",["尚未确认","核验地方笔记","寻找私人住址"],["","verify_local_note","find_private_address"]),new("version","本次版本",["尚未确认","W04-v1","任意新版本"],["","W04-v1","any_version"]),new("address","私人住址",["尚未确认","不包含","包含"],["","excluded","included"])],
        _=>throw new InvalidOperationException("该候选写作任务尚无世界提交接口。")
    };
    private async ValueTask OpenChapterTwoSubmission(string quest)
    {
        if(quest is "R2-O01" or "R2-O02"){await OpenR2LifeSubmission(quest);return;}
        if(_chapterTwo is null)return;if(!await SaveChapterTwoDraft())throw new InvalidOperationException("当前正文尚未保存，不能提交旧稿。");var preview=await _chapterTwo.PreviewSubmissionAsync(quest);string title="第二章提交预览 · "+quest;var flow=Panel(title,InputOwner.Confirmation);Label(flow,preview.Ready?"当前来源、明确提议与实际工作可共同提交。关闭预览会取消；确认前的世界变化或正文修改会使该预览失效。":"尚未满足当前提交条件。",22);foreach(var issue in preview.Issues)Label(flow,issue.SafeMessage,20);Label(flow,"预览不发奖，不推进任务。英文能力评分与人审仍待完成。",18);if(!preview.Ready)return;TrackChapterTwoPreview(title,preview.Token);Button(flow,"明确提交第二章当前结果",()=>Execute(async()=>{if(!await SaveChapterTwoDraft())throw new InvalidOperationException("正文尚未保存。");CheckCommit(await _chapterTwo.ConfirmPreviewAsync(preview.Token));CloseAllPanels();_status.Text="实际工作结果已提交，固定奖励只结算一次。";}));
    }
    private void OpenChapterTwoRecordScope()
    {
        if(_chapterTwo is null)return;var flow=Panel("第二章具体调阅范围",InputOwner.Confirmation);Label(flow,"选择记录、必要字段、指定接收方和本次用途。申请不等于取得材料，也不等于许可公开身份。",22);
        var record=ChapterTwoChoice(flow,"记录与用途","尚未选择","TR-D1-7 · 核验转移交接","TS-18 · 核验历史署名范围");string[] names=["transfer_id","identity_link","dated_condition","destination_code","version_source","signature_reference"];string[] labels=["交接编号","身份链接","有日期的状态","去向代码","版本来源","署名索引"];
        var fields=new Dictionary<string,CheckBox>();for(int i=0;i<names.Length;i++){var check=new CheckBox{Text=labels[i],AccessibilityName=labels[i]+"调阅字段"};flow.AddChild(check);fields.Add(names[i],check);}Label(flow,"当前版本：1 · 接收方：本次巡灯技术员。改变用途或版本须重新申请。",18);
        ChapterTwoButton(flow,"明确提出当前限定字段申请",async()=>{if(record.Selected==0)throw new InvalidOperationException("请明确选择记录和用途。");string id=record.Selected==1?"TR-D1-7":"TS-18",purpose=record.Selected==1?"verify_transfer":"verify_historical_scope";CheckCommit(await _chapterTwo.RequestRecordScopeAsync(id,"1",fields.Where(p=>p.Value.ButtonPressed).Select(p=>p.Key).ToImmutableArray(),"patrol_technician",purpose));});
    }
    private void OpenChapterTwoM12()
    {
        if(_chapterTwo is null)return;var flow=Panel("M-12原始记录顺序核对",InputOwner.Confirmation);Label(flow,"先读取两篇原始来源。该核对属于明确结构化辅助；记录抄写时刻与事件时刻分别保留。",22);
        Label(flow,"授权时刻",20);var auth=ChapterTwoChoice(flow,"M12授权时刻","尚未确认","早于14:10；准确分钟未知","准确13:50","在16:05后才授权");Label(flow,"到达时刻",20);var arrival=ChapterTwoChoice(flow,"M12到达时刻","尚未确认","14:40","16:05");Label(flow,"抄写时刻",20);var copy=ChapterTwoChoice(flow,"M12抄写时刻","尚未确认","16:05","14:40");Label(flow,"抄本是否独立证明授权",20);var independent=ChapterTwoChoice(flow,"M12抄本独立性","尚未确认","同源抄本，不独立","独立新来源");Label(flow,"中央当时掌握的资料",20);var knowledge=ChapterTwoChoice(flow,"M12中央所知","尚未确认","当前来源不足以确定","已经全部确定");Label(flow,"较晚抄写的意图",20);var intent=ChapterTwoChoice(flow,"M12抄写意图","尚未确认","未知","已证明故意隐瞒");
        ChapterTwoButton(flow,"明确提交M-12顺序与未知项",async()=>{if(new[]{auth,arrival,copy,independent,knowledge,intent}.Any(c=>c.Selected==0))throw new InvalidOperationException("请逐项明确当前核对，不能默认填入答案。");CheckCommit(await _chapterTwo.ConfirmM12Async(Selected(auth,["","before_14:10_exact_minute_unknown","13:50","after_16:05"]),Selected(arrival,["","14:40","16:05"]),Selected(copy,["","16:05","14:40"]),independent.Selected==2,Selected(knowledge,["","unknown","all_known"]),Selected(intent,["","unknown","concealment_proven"])));});
    }
    private void OpenChapterTwoD1()
    {
        if(_chapterTwo is null)return;var flow=Panel("两端实际交接核验",InputOwner.Confirmation);Label(flow,"同一交接编号须对应实际发送与接收两端独立来源。日期范围不能改写为导师当前状态。",22);var transfer=ChapterTwoChoice(flow,"交接编号","尚未确认","TR-D1-7","M-12");var time=ChapterTwoChoice(flow,"观察日期范围","尚未确认","D−1当日","当前");var current=ChapterTwoChoice(flow,"导师当前状态","尚未确认","未知","存活","死亡");
        ChapterTwoButton(flow,"明确核对两端编号与日期范围",async()=>{if(new[]{transfer,time,current}.Any(c=>c.Selected==0))throw new InvalidOperationException("请逐项明确核对。");CheckCommit(await _chapterTwo.VerifyD1ChainAsync(Selected(transfer,["","TR-D1-7","M-12"]),Selected(time,["","D-1_only","current"]),Selected(current,["","unknown","alive","dead"])));});
    }
    private void OpenChapterTwoLoad()
    {
        if(_chapterTwo is null)return;var flow=Panel("地方备用源负荷选择",InputOwner.Confirmation);Label(flow,"改档位须先隔离备用源。改变档位会使旧试验和厨房验收失效；选档本身没有供能结果。",22);var choice=ChapterTwoChoice(flow,"地方负荷档位","尚未选择","厨房回路","洗涤回路","两回路轮替","两回路并联","接塔站出口");LocalLoad[] values=[LocalLoad.Unselected,LocalLoad.Kitchen,LocalLoad.Wash,LocalLoad.Alternating,LocalLoad.Concurrent,LocalLoad.TowerOutlet];ChapterTwoButton(flow,"实际设置所选档位",async()=>{CheckCommit(await _chapterTwo.SetLocalLoadAsync(values[choice.Selected]));});
    }
    private void OpenChapterTwoConveyor()
    {
        if(_chapterTwo is null)return;var flow=Panel("输送器实际处置",InputOwner.Confirmation);Label(flow,"先到隔离站实际隔离。手动护罩、卡滞与零运动复核分别执行，也可选择独立共享战斗处置。取得规程或选择方法都不产生结果。",22);
        foreach(var pair in new[]{("open_conveyor_guard","实际打开已隔离的护罩"),("release_conveyor_jam","实际释放卡滞"),("secure_conveyor_guard","实际重新固定护罩"),("inspect_conveyor_stopped","现场检查当前零运动")}){string action=pair.Item1;ChapterTwoButton(flow,pair.Item2,async()=>CheckCommit(await _chapterTwo.PerformConveyorStepAsync(action)));}
        Button(flow,"查看独立输送器战斗入口",()=>Execute(async()=>await OpenChapterTwoEncounterEntry("ENC-R2-02")));
    }
    private async ValueTask OpenChapterTwoCore()
    {
        if(_chapterTwo is null)return;var state=(await _chapterTwo.ViewAsync()).State;var flow=Panel("缠根核心现场工作",InputOwner.Confirmation);Label(flow,"须先观察两支路并实际固定外侧阀。核心威胁控制后仍要执行所选方法，再分别复查两外侧阀；读到档案不会解除危险区。",22);Label(flow,"当前方法："+(string.IsNullOrEmpty(state.CoreMethod)?"尚未选择":state.CoreMethod),20);
        ChapterTwoButton(flow,"明确选择取样处置方法",async()=>CheckCommit(await _chapterTwo.ChooseCoreMethodAsync("sample")));ChapterTwoButton(flow,"明确选择机械清理方法",async()=>CheckCommit(await _chapterTwo.ChooseCoreMethodAsync("mechanical")));Button(flow,"查看核心实际遭遇入口",()=>Execute(async()=>await OpenChapterTwoEncounterEntry("ENC-R2-03")));ChapterTwoButton(flow,"威胁控制后实际取样",async()=>CheckCommit(await _chapterTwo.PerformCoreStepAsync("take_sample")));ChapterTwoButton(flow,"威胁控制后实际清理缠绕",async()=>CheckCommit(await _chapterTwo.PerformCoreStepAsync("clear_winding")));
    }
    private async ValueTask OpenChapterTwoEncounterEntry(string id)
    {
        if(_chapterTwo is null)return;_chapterTwoSelectedEncounter=id;var existing=await _chapterTwo.EncounterAsync(id);if(existing is not null&&existing.Battle.Outcome!=CombatOutcome.Withdrawn){await OpenBattle();return;}
        var flow=Panel("第二章实际遭遇入口 · "+id,InputOwner.Confirmation);Label(flow,"此处使用真实当前主角、装备、两名援助与共享战斗规则。没有击杀经验；本地任务提交后只结算原固定奖励。",22);Label(flow,"难度与配置仍属待人工试玩候选。进入与撤回不填造现场结果。",18);var difficulty=ChapterTwoChoice(flow,"第二章遭遇难度","标准","宽容","挑战");var view=await _chapterTwo.ViewAsync();var supplies=new Dictionary<string,CheckBox>();foreach(var item in view.Snapshot.Items.Where(i=>i.OwnerKind=="player"&&i.OwnerId=="hero"&&i.DefinitionId.StartsWith("CON-",StringComparison.Ordinal))){var check=new CheckBox{Text=item.DefinitionId+" · "+item.Id};flow.AddChild(check);supplies.Add(item.Id,check);}Label(flow,"最多携带两件实际持有补给；不携带也可进入。",18);
        Button(flow,"明确进入第二章所选遭遇",()=>Execute(async()=>{CheckCommit(await _chapterTwo.BeginEncounterAsync(id,new[]{"standard","relaxed","challenge"}[difficulty.Selected],supplies.Where(p=>p.Value.ButtonPressed).Select(p=>p.Key).ToArray()));CloseAllPanels();await Refresh();await OpenBattle();}));
    }
    private void OpenChapterTwoDisclosure()
    {
        if(_chapterTwo is null)return;var flow=Panel("本次事实交付范围",InputOwner.Confirmation);Label(flow,"公共事实核查与受保护身份字段分别处理。具名身份发布的逐人实际同意来源尚未制作，此候选保持不可用。",22);var path=ChapterTwoChoice(flow,"事实交付方式","尚未选择","匿名公共事实摘要 → 修会公共看板","委托事实复核 → 修会复核台");string[] names=["public_fact","record_dates","source_versions","local_safety_boundary","historical_signature_uncertainty","mining_batch"],labels=["公共事实","记录日期","来源版本","本地安全边界","历史署名的不确定范围","矿业批号"];var fields=new Dictionary<string,CheckBox>();for(int i=0;i<names.Length;i++){var check=new CheckBox{Text=labels[i],AccessibilityName=labels[i]+"交付字段"};flow.AddChild(check);fields.Add(names[i],check);}Label(flow,"版本：R2-facts-v1 · 用途：核验公共事实。选择不是实际交付；提交正文时另行预览确认。",18);
        ChapterTwoButton(flow,"明确确认当前交付字段与接收方",async()=>{if(path.Selected==0)throw new InvalidOperationException("请明确选择本次交付方式。");CheckCommit(await _chapterTwo.SetDisclosureScopeAsync(path.Selected==1?"redacted_public_summary":"entrusted_review",fields.Where(p=>p.Value.ButtonPressed).Select(p=>p.Key).ToImmutableArray(),path.Selected==1?"priory_public_board":"priory_review_desk","verify_public_facts","R2-facts-v1"));});
    }
    private void OpenChapterTwoMira()
    {
        if(_chapterTwo is null)return;var flow=Panel("米拉的地方协作邀请",InputOwner.Dialogue);Label(flow,"专业联系、正式同行与私人工作分别决定。拒绝或暂缓同行不阻断本章公共核查。",22);ChapterTwoButton(flow,"接受正式同行邀请",async()=>CheckCommit(await _chapterTwo.DecideMiraInvitationAsync(true)));ChapterTwoButton(flow,"暂不加入同行，保留地方联系",async()=>CheckCommit(await _chapterTwo.DecideMiraInvitationAsync(false)));ChapterTwoButton(flow,"暂缓正式同行决定",async()=>CheckCommit(await _chapterTwo.DecideMiraInvitationAsync(null)));
    }
    private void OpenChapterTwoW04Invitation()
    {
        if(_chapterTwo is null)return;var flow=Panel("米拉的私人笔记工作邀请",InputOwner.Dialogue);Label(flow,"这项工作不要求米拉先正式加入。主线范围协议完成后才可受理；本人字段同意在W-04实际站位另行核验。",22);foreach(var pair in new[]{("accept","接受本次私人工作邀请"),("decline","拒绝本次私人工作邀请"),("defer","暂缓本次私人工作邀请")}){string decision=pair.Item1;ChapterTwoButton(flow,pair.Item2,async()=>CheckCommit(await _chapterTwo.DecideW04InvitationAsync(decision)));}
    }
    private void OpenChapterTwoW04Scope()
    {
        if(_chapterTwo is null)return;var flow=Panel("W-04本人限定字段同意",InputOwner.Confirmation);Label(flow,"当前本地委托只允许必要的观察时间、当时职务与版本来源。本人住址不是核验此笔记的必要字段；接收方或用途改变后须重新请求。",22);string[] names=["observation_time","witness_duty","version_source","private_address"],labels=["观察时间","当时职务","版本来源","私人住址（当前未获同意）"];var fields=new Dictionary<string,CheckBox>();for(int i=0;i<names.Length;i++){var check=new CheckBox{Text=labels[i],AccessibilityName=labels[i]+"本人同意字段"};flow.AddChild(check);fields.Add(names[i],check);}Label(flow,"接收方：修会复核台 · 用途：核验地方笔记 · 版本：W04-v1",18);ChapterTwoButton(flow,"向本人明确请求本次必要字段",async()=>CheckCommit(await _chapterTwo.RequestW04ScopeAsync(fields.Where(p=>p.Value.ButtonPressed).Select(p=>p.Key).ToImmutableArray(),"priory_review_desk","verify_local_note","W04-v1")));
    }
}

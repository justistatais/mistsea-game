using System.Collections.Immutable;
using Godot;
using Mist.Application.ChapterThree;
using Mist.Application.Drafts;
using Mist.Application.UI;
using Mist.Core.ChapterThree;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Quests;

public partial class Root
{
    private string? _chapterThreeSelectedEncounter;
    private bool ChapterThreeEncounterSelected=>_chapterThree is not null&&_chapterThreeSelectedEncounter is not null;
    private readonly Dictionary<string,DraftEditor> _chapterThreeEditors=new();
    private readonly Dictionary<string,Stack<Guid>> _chapterThreeUiPreviews=new();
    private TextEdit? _chapterThreeWriter;
    private Label? _chapterThreeSaveStatus;
    private Godot.Timer? _chapterThreeSaveTimer;
    private Task<bool>? _chapterThreeSaveTask;
    private static string ChapterThreeWriterTitle(string quest)=>"第三章正文 · "+quest;
    private static string ChapterThreeQuestName(string quest)=>quest switch{"Q-301"=>"钟声以下 · 矿区救援","Q-302"=>"最后一道检修章 · 三方核验","Q-303"=>"三炉一线 · 资源与降载","Q-304"=>"停止超采 · 深井塔与核验交接","CQ-OREN-01"=>"辅助住区报警器承诺","CQ-OREN-02"=>"报警器独立维修复核",_=>quest};
    private void TrackChapterThreePreview(string title,Guid token){if(!_chapterThreeUiPreviews.TryGetValue(title,out var stack)){stack=new();_chapterThreeUiPreviews[title]=stack;}stack.Push(token);}
    private void CloseChapterThreeUiPreview(string title)
    {
        if(_chapterThreeUiPreviews.TryGetValue(title,out var stack)&&stack.TryPop(out var token))_chapterThree?.ClosePreview(token);
        if(title.StartsWith("第三章正文 · ",StringComparison.Ordinal)){_chapterThreeWriter=null;_chapterThreeSaveStatus=null;_chapterThreeSaveTimer=null;_=SaveChapterThreeDraft();}
    }
    private void ResetChapterThreeUiState()
    {
        foreach(var stack in _chapterThreeUiPreviews.Values)foreach(var token in stack)_chapterThree?.ClosePreview(token);
        _chapterThreeUiPreviews.Clear();_chapterThreeEditors.Clear();_chapterThreeSelectedEncounter=null;_chapterThreeWriter=null;_chapterThreeSaveStatus=null;_chapterThreeSaveTimer=null;_chapterThreeSaveTask=null;
    }
    private void ChapterThreeButton(VBoxContainer flow,string caption,Func<ValueTask> action,bool close=true)=>Button(flow,caption,()=>Execute(async()=>{await action();if(close)ClosePanel();}));
    private static OptionButton ChapterThreeChoice(VBoxContainer flow,string name,params string[] choices)
    {
        var option=new OptionButton{FitToLongestItem=false,AccessibilityName=name};foreach(string choice in choices)option.AddItem(choice);flow.AddChild(option);return option;
    }
    private static string ChapterThreeSelected(OptionButton option,string[] values)=>option.Selected>=0&&option.Selected<values.Length?values[option.Selected]:"";

    private async ValueTask<bool> TryChapterThreeInteraction(string spot)
    {
        if(_chapterThree is null||!spot.StartsWith("R3-",StringComparison.Ordinal))return false;var materials=_chapterThree.Package.Materials.Where(m=>m.Spot==spot).ToArray();var stations=_chapterThree.Package.Stations.Where(s=>s.Spot==spot).ToArray();
        if(materials.Length==0&&stations.Length==0&&spot is not("R3-S01.berth" or "R3-S02.pera" or "R3-S09.return_check" or "R3-S09.load_test" or "R3-S08.demand_board" or "R3-S08.shift_duty" or "R3-S08.auxiliary_receipt"))return false;
        var view=await _chapterThree.ViewAsync();var flow=Panel("沉钟矿区现场 · "+spot,InputOwner.Workspace);AddR3ShiftButton(flow,spot);AddScopedPersonalDetailButton(flow,spot);
        foreach(var material in materials){string id=material.Id;Button(flow,(view.Materials.Any(m=>m.Id==id)?"查看已得材料：":"实际领取/读取：")+material.Title,()=>Execute(async()=>{CheckCommit(await _chapterThree.DiscoverAsync(id));await OpenChapterThreeMaterial(id);}));}
        foreach(var station in stations){string action=station.Action;Button(flow,station.Label,()=>Execute(async()=>await OpenChapterThreeStation(action)));}
        if(spot=="R3-S06.drain_control")Button(flow,"明确设置隔离排水档",()=>OpenChapterThreePump());
        if(spot=="R3-S09.load_test")ChapterThreeButton(flow,"现场签发当前配置安全核验",async()=>CheckCommit(await _chapterThree.VerifyTowerSafetyAsync()));
        if(spot is "R3-S01.berth" or "R3-S02.pera" or "R3-S09.return_check")ChapterThreeButton(flow,"登记当前实际安全返程点",async()=>CheckCommit(await _chapterThree.RegisterCurrentReturnAnchorAsync()));
        if(spot=="R3-S02.pera")ChapterThreeButton(flow,"向佩拉邀请独立报警器复核",async()=>CheckCommit(await _chapterThree.InviteIndependentAlarmReviewAsync("N-PERA")));
        foreach(var task in _chapterThree.Package.WritingTasks.Where(t=>t.Spot==spot&&view.Quests.Any(q=>q.Id==t.Quest&&q.Lifecycle==QuestLifecycle.Active))){string quest=task.Quest;Button(flow,"打开 "+quest+" 正文工作台",()=>Execute(async()=>await OpenChapterThreeWorkspace(quest)));}
        return true;
    }
    private async ValueTask OpenChapterThreeStation(string action)
    {
        if(_chapterThree is null)return;
        switch(action)
        {
            case "submit_rescue_report":await OpenChapterThreeWorkspace("Q-301");return;
            case "submit_resource_arrangement":await OpenChapterThreeWorkspace("Q-303");return;
            case "submit_tower_handover":await OpenChapterThreeWorkspace("Q-304");return;
            case "request_batch_record_scope":OpenChapterThreeRecordScope();return;
            case "clear_rail_encounter":await OpenChapterThreeEncounterEntry("ENC-R3-01");return;
            case "clear_pump_encounter":await OpenChapterThreeEncounterEntry("ENC-R3-02");return;
            case "clear_instrument_guard":OpenChapterThreeInstrument();return;
            case "escort_workers":await OpenChapterThreeEncounterEntry("ENC-R3-04");return;
            case "begin_extraction_machine_encounter":await OpenChapterThreeEncounterEntry("ENC-R3-05");return;
            case "rescue_workers":OpenChapterThreeRescue();return;
            case "compare_batch_records":OpenChapterThreeBatchComparison();return;
            case "decide_oren_accountability":OpenChapterThreeAccountability();return;
            case "decide_oren_recruitment":OpenChapterThreeOren();return;
            case "allocate_extra_packages":OpenChapterThreeAllocation();return;
            case "appoint_shift_duty":OpenChapterThreeDuty();return;
            case "confirm_load_policy":OpenChapterThreePolicy();return;
            case "close_transition_shift":ChapterThreeCloseShift();return;
            case "configure_tower_load":OpenChapterThreeTowerLoad();return;
            case "invite_auxiliary_alarm_work":OpenChapterThreeAuxiliaryInvitation();return;
            case "commit_auxiliary_repair_scope":OpenChapterThreeAuxiliaryScope();return;
            case "repair_auxiliary_alarm":OpenChapterThreeAuxiliaryRepair();return;
            case "confirm_independent_alarm_review":OpenChapterThreeAlarmReview();return;
            case "propose_musicbox_repair":case "propose_sidewell_observation":var pending=Panel("矿区可选工作",InputOwner.Workspace);Label(pending,"这项可选合同尚未核定，当前不能结算。已实际获准并取得的材料仍可保存查看。",22);return;
            default:CheckCommit(await _chapterThree.ExecuteStationAsync(action));ClosePanel();_status.Text="现场操作已记录。配置改变后请重新核验。";return;
        }
    }
    private async ValueTask OpenChapterThreeQuests()
    {
        if(_chapterThree is null)return;var view=await _chapterThree.ViewAsync();var flow=Panel("沉钟矿区工作与当前状态",InputOwner.Workspace);
        foreach(var quest in view.Quests){Label(flow,ChapterThreeQuestName(quest.Id)+" · "+quest.Lifecycle+(quest.Outcome is{} outcome?" / "+outcome:""),22);if(quest.Lifecycle==QuestLifecycle.Active){string id=quest.Id;Button(flow,"编辑 "+id+" 同一份正文",()=>Execute(async()=>await OpenChapterThreeWriter(id)));Button(flow,"核对 "+id+" 当前提交预览",()=>Execute(async()=>await OpenChapterThreeSubmission(id)));}}
        Label(flow,$"接收工人 {(view.State.WorkersReceived?3:0)}/3 · 基本安置 {view.State.BasicDelivered} · 当前矿道排水安全 {view.State.MineRouteSafe} · 独立梯道 {view.State.LadderSafe}",20);
        Label(flow,$"当前抽取负荷 {view.State.CurrentExtraction}；基本供给7，总量上限10 · 额外工包交付 {view.State.ExtraDelivered} · 当前塔站检查 {view.State.TowerCurrent}",20);
        Label(flow,"救援与档案核查可并行。资源安排在救援后推进；正式塔站工作须在核查与资源方案都完成后进入。",20);
        foreach(string id in new[]{"ENC-R3-01","ENC-R3-02","ENC-R3-03","ENC-R3-04","ENC-R3-05"})if(await _chapterThree.EncounterAsync(id) is{} encounter){string key=id;Button(flow,"继续查看 "+id+" · "+encounter.Battle.Outcome,()=>Execute(async()=>{_chapterThreeSelectedEncounter=key;await OpenBattle();}));}
    }
    private async ValueTask OpenChapterThreeArchive()
    {
        if(_chapterThree is null)return;var view=await _chapterThree.ViewAsync();var flow=Panel("沉钟矿区已得档案",InputOwner.Workspace);if(view.Materials.Length==0)Label(flow,"尚未在现场取得当前获准的材料。",22);foreach(var material in view.Materials){string id=material.Id;Button(flow,material.Title,()=>Execute(async()=>await OpenChapterThreeMaterial(id)));}Button(flow,"工作与当前状态",()=>Execute(OpenChapterThreeQuests));
    }
    private async ValueTask OpenChapterThreeMaterial(string id)
    {
        if(_chapterThree is null)return;var material=await _chapterThree.OpenMaterialAsync(id);var flow=Panel("矿区材料 · "+material.Title,InputOwner.Workspace);Label(flow,material.Scene+" · "+material.Version,18);var reader=new TextEdit{Text=material.English,Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,350),AccessibilityName=material.Title+"英文原文"};reader.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(reader);AddRegionalDictionary(flow,reader);if(material.Kind=="listening_script")Label(flow,"当前查看书面脚本；候选音轨须另行打开并实际播放，本次读取不会记为已听录音。",18);if(material.Kind=="listening_script")AddRegionalAudioButton(flow,material,()=>_chapterThree.OpenMaterialAsync(id));Button(flow,"查看中文辅助",()=>Label(flow,material.Chinese,20));if(_chapterThree.Package.WritingTasks.Any(t=>t.Quest==material.Quest))Button(flow,"编辑对应的同一份正文",()=>Execute(async()=>await OpenChapterThreeWriter(material.Quest)));
    }
    private async ValueTask OpenChapterThreeWorkspace(string quest)
    {
        if(_chapterThree is null)return;var task=_chapterThree.Package.WritingTasks.Single(t=>t.Quest==quest);var flow=Panel("矿区正文工作台 · "+ChapterThreeQuestName(quest),InputOwner.Workspace);Label(flow,"正文可以提前保存。意思确认站位："+task.Spot+"；正式接收站位："+_chapterThree.SubmissionSpot(quest)+"。",20);Button(flow,"编辑同一份正文",()=>Execute(async()=>await OpenChapterThreeWriter(quest)));Button(flow,"明确核对正文意思",()=>Execute(async()=>await OpenChapterThreeMeaning(quest,false)));Button(flow,"明确使用结构化辅助",()=>Execute(async()=>await OpenChapterThreeMeaning(quest,true)));Button(flow,"查看当前提交预览",()=>Execute(async()=>await OpenChapterThreeSubmission(quest)));
    }
    private async ValueTask OpenChapterThreeWriter(string quest)
    {
        if(quest=="R3-O02"){await OpenSideWellWriter();return;}
        if(_chapterThree is null)return;if(!await SaveChapterThreeDraft())throw new InvalidOperationException("请先完成输入法候选并保存当前正文。");string title=ChapterThreeWriterTitle(quest);
        if(_routing.Contains(title)){while(_routing.Top!=title)ClosePanel();_chapterThreeWriter?.GrabFocus();return;}
        var task=_chapterThree.Package.WritingTasks.Single(t=>t.Quest==quest);if(!_chapterThreeEditors.TryGetValue(quest,out var editor)){var head=await _store.GetDraftAsync(task.Id);var session=_store.Session;editor=new(_store,new(session.Profile,session.Branch,session.Load,task.Id,head?.Id,head?.Revision??0,head?.Body??"",_chapterThree.Package.Version));_chapterThreeEditors.Add(quest,editor);}
        var flow=Panel(title,InputOwner.Editable);Label(flow,task.EnglishPrompt,22);Button(flow,"查看任务中文辅助",()=>Label(flow,task.ChinesePrompt,20));Label(flow,"读、听、写目标各6.5；当前不自动理解英文，不生成能力分数。自由写作从空稿开始，结构化辅助须另行明确选择。",18);
        if(task.Visual is{} table)
        {
            var view=await _chapterThree.ViewAsync();if(view.Materials.Any(m=>m.Id=="R3-Q302-R-BATCHES")){Label(flow,table.Title+"\n"+string.Join(" | ",table.Columns)+"\n"+string.Join("\n",table.Rows.Select(r=>$"{r.Label} | {r.RollEntries} | {r.ResidenceConfirmations}")),20);}else Label(flow,"比较图表须在现场实际取得并获准查看对照材料后显示。",18);
        }
        var writer=new TextEdit{Text=editor.Text,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,330),AccessibilityName=quest+"英文正文"};writer.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(writer);_chapterThreeWriter=writer;_chapterThreeSaveStatus=Label(flow,editor.IsSaved?"当前正文与保存版本一致。":"当前正文待保存。",18);
        var timer=new Godot.Timer{OneShot=true,WaitTime=.5};flow.AddChild(timer);_chapterThreeSaveTimer=timer;timer.Timeout+=()=>{if(Composing()){timer.Start(.2);return;}_=SaveChapterThreeDraft();};writer.TextChanged+=()=>{editor.Edit(writer.Text);if(_chapterThreeSaveStatus is{} status&&IsInstanceValid(status))status.Text="有未保存修改。";foreach(var stack in _chapterThreeUiPreviews.Values)foreach(var token in stack)_chapterThree.ClosePreview(token);_chapterThreeUiPreviews.Clear();timer.Start();};
        writer.FocusExited+=()=>{foreach(var key in _keys.Values.Append(Key.Enter))if(Input.IsPhysicalKeyPressed(key))_releaseBarrier.Add(key);};
        var back=flow.GetChild<HBoxContainer>(0).GetChild<Button>(1);back.Pressed-=ClosePanel;back.Pressed+=()=>Execute(async()=>{if(!await SaveChapterThreeDraft())throw new InvalidOperationException("正文尚未保存，完成输入法候选后重试或导出。");ClosePanel();});
        Button(flow,"保存第三章正文",()=>Execute(async()=>{if(!await SaveChapterThreeDraft())throw new InvalidOperationException("正文保存尚未完成。");}));Button(flow,"导出当前第三章正文",()=>{string dir=ProjectSettings.GlobalizePath("user://exports");Directory.CreateDirectory(dir);string path=Path.Combine(dir,quest+"-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+".txt");File.WriteAllText(path,editor.Text,new System.Text.UTF8Encoding(false));_status.Text="当前正文已导出："+path;});Button(flow,"明确核对正文意思",()=>Execute(async()=>await OpenChapterThreeMeaning(quest,false)));Button(flow,"明确使用结构化辅助",()=>Execute(async()=>await OpenChapterThreeMeaning(quest,true)));Button(flow,"查看当前提交预览",()=>Execute(async()=>await OpenChapterThreeSubmission(quest)));writer.GrabFocus();
    }
    private async ValueTask<bool> SaveChapterThreeDraft()
    {
        if(_chapterThreeEditors.Count==0)return true;if(Composing())return false;if(_chapterThreeSaveTask is not null&&!_chapterThreeSaveTask.IsCompleted)return await _chapterThreeSaveTask;_chapterThreeSaveTask=SaveChapterThreeDraftCore();return await _chapterThreeSaveTask;
    }
    private async Task<bool> SaveChapterThreeDraftCore()
    {
        try{foreach(var editor in _chapterThreeEditors.Values)if(!editor.IsSaved)await editor.SaveAsync();bool saved=_chapterThreeEditors.Values.All(e=>e.IsSaved);if(_chapterThreeSaveStatus is{} label&&IsInstanceValid(label))label.Text=saved?"正文已由实际保存回执确认。":"修改尚未全数保存，旧回执不会覆盖新稿。";if(!saved&&_chapterThreeSaveTimer is{} timer&&IsInstanceValid(timer))timer.Start(.2);return saved;}
        catch(Exception error){if(_chapterThreeSaveStatus is{} label&&IsInstanceValid(label))label.Text="保存失败，正文仍可导出："+error.Message;_status.Text="第三章正文保存失败："+error.Message;return false;}
    }
    private async ValueTask OpenChapterThreeMeaning(string quest,bool assisted)
    {
        if(quest=="R3-O02"){await OpenSideWellMeaning(assisted);return;}
        if(_chapterThree is null)return;if(!await SaveChapterThreeDraft())throw new InvalidOperationException("请先完成输入法候选并保存正文。");var flow=Panel("第三章意思确认 · "+quest,InputOwner.Confirmation);Label(flow,assisted?"这是明确结构化辅助，不计作独立自由英文理解。所选意思仍须与实际来源、权限和设备结果共同核验。":"当前离线模式没有自动理解英文。请逐项确认你写下的意思；尚未确认的项不会填入默认答案。",22);
        var rows=ChapterThreeMeaningRows(quest);var choices=new List<OptionButton>();foreach(var row in rows){Label(flow,row.Name,20);choices.Add(ChapterThreeChoice(flow,row.Name,row.Labels));}
        Button(flow,"明确记录当前第三章提议",()=>Execute(async()=>{if(!await SaveChapterThreeDraft())throw new InvalidOperationException("当前正文尚未保存。");var fields=rows.Select((r,i)=>(r.Key,Value:ChapterThreeSelected(choices[i],r.Values))).ToImmutableDictionary(x=>x.Key,x=>x.Value);if(fields.Values.Any(string.IsNullOrWhiteSpace))throw new InvalidOperationException("请逐项明确选择，不能沿用未确认项。");CheckCommit(await _chapterThree.ConfirmMeaningAsync(quest,fields,assisted));ClosePanel();_status.Text="当前稿件的明确提议已记录，尚未提交世界结果。";}));
    }


    private sealed record ChapterThreeMeaningRow(string Key,string Name,string[] Labels,string[] Values);
    private static ChapterThreeMeaningRow[] ChapterThreeMeaningRows(string quest)
    {
        ChapterThreeMeaningRow Number(string key,string name)=>new(key,name,["尚未确认","0","1","2","3","4","5","6"],["","0","1","2","3","4","5","6"]);
        return quest switch
        {
            "Q-301"=>[new("return_route","实际安全返程",["尚未确认","独立梯道保持可用","只依赖带电升降机"],["","independent_ladder","powered_lift_only"]),new("workers","实际救援接收",["尚未确认","三名已到场接收","三名只在记录中出现"],["","three_received","listed_only"]),new("basic_guarantee","基本保障",["尚未确认","地方基本份额已交付","先承诺以后再给"],["","local_delivered","promised_only"]),new("instrument","现场仪表",["尚未确认","保留可核验原件","修复后替换原表"],["","original_retained","replace_original"]),new("death_claim","空白返程栏的范围",["尚未确认","不据此推断死亡","据空白确定死亡"],["","not_inferred","death_proven"])],
            "Q-302"=>[new("incomplete_checks","未完成现场检查数",["尚未确认","3/24","23/24","24/24"],["","3_of_24","23_of_24","24_of_24"]),new("formal_accepted","正式验收合格数",["尚未确认","23/24","3/24","24/24"],["","23_of_24","3_of_24","24_of_24"]),new("comparison","两列的计量含义",["尚未确认","计量不同","完全相同"],["","different_measures","identical_measures"]),new("oren_responsibility","奥伦的具体责任",["尚未确认","未修旁路被合格交付","当前维修撤销旧责任"],["","unrepaired_bypass_accepted","repair_erases_history"]),new("causality","全部事故因果",["尚未确认","尚未证实","已证明唯一全因果"],["","unproven","fully_proven"]),new("mara_current_status","导师当前状态",["尚未确认","未知","存活","死亡"],["","unknown","alive","dead"])],
            "Q-303"=>[new("policy","实际执行政策",["尚未确认","早降载安置","有限当班负荷过渡","无限期超采"],["","early_reduce","time_limited_transition","unlimited_extraction"]),Number("shelter","安置工包数量"),Number("retrofit","改造工包数量"),Number("production","生产工包数量"),new("basic_priority","基本保障优先",["尚未确认","保留已落实保障","挪用救援份额"],["","preserved","diverted"]),new("unlimited_extraction","无期限超采",["尚未确认","拒绝","批准"],["","rejected","approved"])],
            "Q-304"=>[new("tower","深井塔工作范围",["尚未确认","当前Safe","全部六塔改造完成"],["","Safe_only","all_converted"]),new("supply","实际用电边界",["尚未确认","当前受限供给","无期限满负荷"],["","bounded_current","unlimited"]),new("handover","核验交接内容",["尚未确认","封样及来源版本","重写历史原件"],["","sealed_sample_and_source_versions","rewrite_original"]),new("causality","样本和记录关系",["尚未确认","关联仍需学城验证","关联已证实全部因果"],["","association_requires_verification","complete_cause"]),new("mara_current_status","导师当前状态",["尚未确认","未知","已找到本人"],["","unknown","found_current"])],
            "CQ-OREN-01"=>[new("scope","本次旁修范围",["尚未确认","辅助住区报警器","所有六塔"],["","auxiliary_alarm_only","all_six_towers"]),new("responsibility","原验收责任",["尚未确认","保留","取消"],["","preserved","erased"]),new("promise","具体维修承诺",["尚未确认","维修并由另一人复核","只表示后悔"],["","repair_and_independent_review","apology_only"])],
            "CQ-OREN-02"=>[new("reviewer","实际独立复核者",["尚未确认","佩拉","奥伦自己"],["","N-PERA","CP-OREN"]),new("repair","实际维修材料",["尚未确认","独立旁修件","六塔准备份额"],["","own_auxiliary_kit","tower_stock"]),new("history","旧签章记录",["尚未确认","保留原签章","改为无责"],["","original_signature_preserved","absolved"]),new("forgiveness","工人谅解",["尚未确认","不要求","强制要求"],["","not_required","required"])],
            _=>throw new InvalidOperationException("这项可选正文尚未注册世界提交合同。")
        };
    }
    private async ValueTask OpenChapterThreeSubmission(string quest)
    {
        if(quest=="R3-O02"){await OpenSideWellComparison();return;}
        if(_chapterThree is null)return;if(!await SaveChapterThreeDraft())throw new InvalidOperationException("当前正文尚未保存，不能提交旧稿。");var preview=await _chapterThree.PreviewSubmissionAsync(quest);string title="第三章提交预览 · "+quest;var flow=Panel(title,InputOwner.Confirmation);Label(flow,preview.Ready?"当前来源、明确提议与实际工作可共同提交。关闭预览会取消；确认前的世界变化或正文修改会使该预览失效。":"尚未满足当前提交条件。",22);foreach(var issue in preview.Issues)Label(flow,issue.SafeMessage,20);Label(flow,"预览不发奖，不推进任务。英文能力评分与人审仍待完成。",18);if(!preview.Ready)return;TrackChapterThreePreview(title,preview.Token);Button(flow,"明确提交第三章当前结果",()=>Execute(async()=>{if(!await SaveChapterThreeDraft())throw new InvalidOperationException("正文尚未保存。");CheckCommit(await _chapterThree.ConfirmPreviewAsync(preview.Token));CloseAllPanels();_status.Text="实际工作结果已提交，固定奖励只结算一次。";}));
    }


    private void OpenChapterThreeRecordScope()
    {
        if(_chapterThree is null)return;var flow=Panel("MC-2具体调阅范围",InputOwner.Confirmation);Label(flow,"当前受托切片：MC2-current-source-set · 接收方：本次巡灯技术员 · 用途：核验原仪表与正式验收。选择字段后申请，不会自动取得材料。",20);string[] values=["check_status","acceptance_status","signatory_scope","version_source"],labels=["现场检查状态","正式验收状态","签章责任范围","版本来源"];var fields=new Dictionary<string,CheckBox>();for(int i=0;i<values.Length;i++){var check=new CheckBox{Text=labels[i],AccessibilityName=labels[i]+"调阅字段"};flow.AddChild(check);fields.Add(values[i],check);}ChapterThreeButton(flow,"明确提出MC-2限定字段申请",async()=>CheckCommit(await _chapterThree.RequestBatchRecordScopeAsync(fields.Where(x=>x.Value.ButtonPressed).Select(x=>x.Key).ToImmutableArray(),"patrol_technician","verify_original_instruments_and_acceptance","MC2-current-source-set")));
    }
    private void OpenChapterThreePump()
    {
        if(_chapterThree is null)return;var flow=Panel("泵房实际排水档",InputOwner.Confirmation);Label(flow,"须先在隔离站实际隔离。设置档位会撤销旧排水检查，之后仍须处理泵房、排水并重新核验。",20);var mode=ChapterThreeChoice(flow,"泵房档位","尚未选择","隔离排水","重新运行旁路");ChapterThreeButton(flow,"实际设置所选泵房档位",async()=>{if(mode.Selected==0)throw new InvalidOperationException("请明确选择档位。");CheckCommit(await _chapterThree.SetPumpModeAsync(mode.Selected==1?"isolated_drain":"running_bypass"));});
    }
    private void OpenChapterThreeRescue()
    {
        if(_chapterThree is null)return;var flow=Panel("采掘面实际救援",InputOwner.Confirmation);Label(flow,"先确认现场求援与当前安全返程，再分别接出实际工人。三人接出后仍须经过轨道护送和地方接收，记录不替代到场。",20);foreach(string worker in new[]{"W1","W2","W3"}){string id=worker;ChapterThreeButton(flow,"实际接出工人 "+id,async()=>CheckCommit(await _chapterThree.RescueWorkerAsync(id)));}
    }
    private void OpenChapterThreeInstrument()
    {
        if(_chapterThree is null)return;var flow=Panel("仪表支路实际处置",InputOwner.Confirmation);Label(flow,"可实际隔离、固定支架并核验零能状态，也可选择共享战斗。两种方法产生同一份工作结果；原始检查字段仍保留。",20);foreach(var pair in new[]{("isolate","实际隔离仪表机卫"),("brace","实际固定仪表支架"),("inspect_zero_motion","现场核验当前零能状态")}){string step=pair.Item1;ChapterThreeButton(flow,pair.Item2,async()=>CheckCommit(await _chapterThree.PerformInstrumentStepAsync(step)));}Button(flow,"查看仪表支路实际战斗入口",()=>Execute(async()=>await OpenChapterThreeEncounterEntry("ENC-R3-03")));
    }
    private void OpenChapterThreeBatchComparison()
    {
        if(_chapterThree is null)return;var flow=Panel("MC-2三方原始版本比较",InputOwner.Confirmation);Label(flow,"须实际取得原仪表、留存副本和正式签章。修复现状不改变旧表，副本同源性与事故全因果分别确认。",20);var raw=ChapterThreeChoice(flow,"原仪表业务版本","尚未确认","MC-2.instrument.I@1","MC-2.instrument.I@3");var copy=ChapterThreeChoice(flow,"工坊副本业务版本","尚未确认","MC-2.retained.I@1","MC-2.retained.I@2");var signed=ChapterThreeChoice(flow,"正式签章业务版本","尚未确认","MC-2.acceptance@3","MC-2.acceptance@1");var incomplete=new Dictionary<string,CheckBox>();foreach(string checkId in Enumerable.Range(1,6).Select(i=>"MC-2-"+i.ToString("D2"))){var check=new CheckBox{Text=checkId,AccessibilityName=checkId+"未完成检查"};flow.AddChild(check);incomplete.Add(checkId,check);}var independent=ChapterThreeChoice(flow,"副本独立性","尚未确认","保留副本与原表同源","副本属于独立新来源");var responsibility=ChapterThreeChoice(flow,"奥伦验收责任","尚未确认","未修旁路仍被合格交付","维修后责任撤销");var cause=ChapterThreeChoice(flow,"全部事故因果范围","尚未确认","尚未证实","已全部证实");var mara=ChapterThreeChoice(flow,"导师当前状态","尚未确认","未知","已确认存活");
        ChapterThreeButton(flow,"明确提交三方版本与字段比较",async()=>{if(new[]{raw,copy,signed,independent,responsibility,cause,mara}.Any(x=>x.Selected==0))throw new InvalidOperationException("请逐项明确版本与证明范围。");CheckCommit(await _chapterThree.CompareBatchAsync(new("MC-2",ChapterThreeSelected(raw,["","MC-2.instrument.I@1","MC-2.instrument.I@3"]),ChapterThreeSelected(copy,["","MC-2.retained.I@1","MC-2.retained.I@2"]),ChapterThreeSelected(signed,["","MC-2.acceptance@3","MC-2.acceptance@1"]),incomplete.Where(x=>x.Value.ButtonPressed).Select(x=>x.Key).ToImmutableArray(),independent.Selected==2,ChapterThreeSelected(responsibility,["","unrepaired_bypass_accepted","repair_erases_history"]),ChapterThreeSelected(cause,["","unproven","fully_proven"]),ChapterThreeSelected(mara,["","unknown","alive"]))));});Button(flow,"打开核验比较正文工作台",()=>Execute(async()=>await OpenChapterThreeWorkspace("Q-302")));
    }
    private void OpenChapterThreeAccountability()
    {
        if(_chapterThree is null)return;var flow=Panel("具体验收责任确认",InputOwner.Confirmation);Label(flow,"责任以三方原件核验为依据。是否同行、是否表示后悔与是否得到谅解分别处理。",20);var scope=ChapterThreeChoice(flow,"责任结论范围","尚未确认","签署未修旁路的合格交付不能被抹去","维修后取消原验收责任");ChapterThreeButton(flow,"明确记录当前责任范围",async()=>{if(scope.Selected==0)throw new InvalidOperationException("请明确责任范围。");CheckCommit(await _chapterThree.ConfirmAccountabilityAsync(scope.Selected==1?"signed_unrepaired_bypass_cannot_be_excused":"repair_erases_history"));});
    }
    private void OpenChapterThreeOren()
    {
        if(_chapterThree is null)return;var flow=Panel("奥伦的正式同行邀请",InputOwner.Dialogue);Label(flow,"正式邀请须在三方核查完成之后。拒绝或暂缓不阻断主线与本人同意的地方旁修。",20);ChapterThreeButton(flow,"接受奥伦正式同行邀请",async()=>CheckCommit(await _chapterThree.DecideOrenInvitationAsync(true)));ChapterThreeButton(flow,"暂不加入同行，保留地方联系",async()=>CheckCommit(await _chapterThree.DecideOrenInvitationAsync(false)));ChapterThreeButton(flow,"暂缓奥伦正式同行决定",async()=>CheckCommit(await _chapterThree.DecideOrenInvitationAsync(null)));
    }
    private SpinBox ChapterThreeCount(VBoxContainer flow,string label,int maximum=6){Label(flow,label,20);var spin=new SpinBox{MinValue=0,MaxValue=maximum,Step=1,Value=0,AccessibilityName=label};flow.AddChild(spin);return spin;}
    private void OpenChapterThreeAllocation()
    {
        if(_chapterThree is null)return;var flow=Panel("地方六件额外工包分配",InputOwner.Confirmation);Label(flow,"基本救援份额先落实。六件额外工包的安置、改造和生产份额合计须为6，安置至少2、改造至少1、生产至多3。保留配置后仍须亲自交付。",20);var shelter=ChapterThreeCount(flow,"安置工包");var retrofit=ChapterThreeCount(flow,"改造工包");var production=ChapterThreeCount(flow,"生产工包");ChapterThreeButton(flow,"实际保留所选六件地方工包",async()=>CheckCommit(await _chapterThree.AllocatePackagesAsync(new((int)shelter.Value,(int)retrofit.Value,(int)production.Value))));
    }
    private void OpenChapterThreeDuty()
    {
        if(_chapterThree is null)return;var flow=Panel("有限过渡的当地值守",InputOwner.Confirmation);Label(flow,"本班泵房和通风要由两名不同当地值守员承担。该安排只在当前班有效，不占六塔工程岗位。",20);var pump=ChapterThreeChoice(flow,"泵房值守员","尚未选择","当地泵房值守员","当地通风值守员");var vent=ChapterThreeChoice(flow,"通风值守员","尚未选择","当地泵房值守员","当地通风值守员");ChapterThreeButton(flow,"实际安排两个独立当班岗位",async()=>{if(pump.Selected==0||vent.Selected==0)throw new InvalidOperationException("请选择两名值守员。");string[] officers=["","R3-LOCAL-PUMP-WATCH","R3-LOCAL-VENT-WATCH"];CheckCommit(await _chapterThree.AppointDutyAsync([new("pump_watch",officers[pump.Selected],"current_watch_only"),new("ventilation_watch",officers[vent.Selected],"current_watch_only")]));});
    }
    private void OpenChapterThreePolicy()
    {
        if(_chapterThree is null)return;var flow=Panel("实际降载与当班窗口",InputOwner.Confirmation);Label(flow,"基本供给占7，上限10。早降载抽取为0；有限过渡仅本班1至3并须两岗值守，交班前回落0。阅读不会推动班次。",20);var mode=ChapterThreeChoice(flow,"实际负荷政策","尚未选择","早降载安置","有限当班负荷过渡","无限期超采");var extraction=ChapterThreeCount(flow,"当前抽取负荷",4);var window=ChapterThreeChoice(flow,"工作窗口","尚未确认","不设抽取窗口","仅当前值守班","无限期");ChapterThreeButton(flow,"实际应用明确的地方负荷方案",async()=>{if(mode.Selected==0||window.Selected==0)throw new InvalidOperationException("请明确政策与窗口。");CheckCommit(await _chapterThree.ConfirmLoadPolicyAsync(new(ChapterThreeSelected(mode,["","early_reduce","time_limited_transition","unlimited_extraction"]),(int)extraction.Value,ChapterThreeSelected(window,["","none","current_watch_only","unlimited"]))));});
    }
    private void ChapterThreeCloseShift(){if(_chapterThree is null)return;var flow=Panel("有限过渡班实际交接",InputOwner.Confirmation);Label(flow,"交班把抽取降到0，并使旧塔站负荷测试失效。这个窗口结束后不能反复重开。",20);ChapterThreeButton(flow,"明确结束当前过渡班并降载到0",async()=>CheckCommit(await _chapterThree.CloseTransitionShiftAsync()));}
    private void OpenChapterThreeTowerLoad(){if(_chapterThree is null)return;var flow=Panel("深井塔实际供能配置",InputOwner.Confirmation);Label(flow,"先停送料并固定吊臂，设置与当前地方政策一致的抽取负荷。设置后须实际测试和检查返程值守。",20);var extraction=ChapterThreeCount(flow,"塔站抽取负荷",4);ChapterThreeButton(flow,"实际设置塔站当前负荷",async()=>CheckCommit(await _chapterThree.ConfigureTowerLoadAsync((int)extraction.Value)));}
    private void OpenChapterThreeAuxiliaryInvitation()
    {
        if(_chapterThree is null)return;var flow=Panel("独立报警器旁修邀请",InputOwner.Dialogue);Label(flow,"这项个人工作不要求奥伦先加入同行。拒绝或暂缓保留主线进展；已承接实物维修须完成安全交接。",20);foreach(var pair in new[]{("accept","接受本次独立报警器旁修"),("decline","拒绝本次报警器旁修"),("defer","暂缓本次报警器旁修"),("withdraw","撤回未开工的旁修邀请")}){string decision=pair.Item1;ChapterThreeButton(flow,pair.Item2,async()=>CheckCommit(await _chapterThree.DecideAlarmInvitationAsync(decision)));}Button(flow,"打开报警器承诺正文",()=>Execute(async()=>await OpenChapterThreeWorkspace("CQ-OREN-01")));
    }
    private void OpenChapterThreeAuxiliaryScope()
    {
        if(_chapterThree is null)return;var flow=Panel("报警器具体维修承诺",InputOwner.Confirmation);Label(flow,"当前只修辅助住区报警器，使用独立旁修件并请另一人复核。原旁路验收责任保留，维修不要求住民谅解。",20);var preserve=new CheckBox{Text="保留原验收责任记录",AccessibilityName="保留原验收责任"};flow.AddChild(preserve);ChapterThreeButton(flow,"明确承诺限定维修与独立复核",async()=>CheckCommit(await _chapterThree.CommitAuxiliaryScopeAsync("auxiliary_residential_alarm_only","repair_and_independent_review",preserve.ButtonPressed)));
    }
    private void OpenChapterThreeAuxiliaryRepair()
    {
        if(_chapterThree is null)return;var flow=Panel("报警器实际独立维修",InputOwner.Confirmation);Label(flow,"须先完成正式塔站工作及具体承诺。开工会保留独立旁修件并占用奥伦，直至另一人现场复核后交接。",20);ChapterThreeButton(flow,"明确开始独立件旁修并占用奥伦",async()=>CheckCommit(await _chapterThree.BeginAuxiliaryRepairAsync()));foreach(var pair in new[]{("inspect_compatibility","实际核对旁修件兼容性"),("isolate","实际隔离辅助报警器"),("fit_own_kit","实际安装独立旁修件"),("test_alarm","实际测试修复后的报警回路")}){string step=pair.Item1;ChapterThreeButton(flow,pair.Item2,async()=>CheckCommit(await _chapterThree.PerformAuxiliaryRepairStepAsync(step)));}
    }
    private void OpenChapterThreeAlarmReview()
    {
        if(_chapterThree is null)return;var flow=Panel("另一人现场复核报警器",InputOwner.Confirmation);Label(flow,"先在此处请求复核，再到佩拉的地方接收点邀请她。当前复核需要同一维修版本的实际测试与邀请，奥伦不能自审。",20);var reviewer=ChapterThreeChoice(flow,"实际报警器复核者","尚未选择","佩拉","奥伦自己");ChapterThreeButton(flow,"明确提交实际独立复核",async()=>{if(reviewer.Selected==0)throw new InvalidOperationException("请选择实际复核者。");CheckCommit(await _chapterThree.ConfirmIndependentAlarmReviewAsync(reviewer.Selected==1?"N-PERA":"CP-OREN"));});Button(flow,"打开维修复核正文",()=>Execute(async()=>await OpenChapterThreeWorkspace("CQ-OREN-02")));
    }
    private async ValueTask OpenChapterThreeEncounterEntry(string id)
    {
        if(_chapterThree is null)return;_chapterThreeSelectedEncounter=id;var old=await _chapterThree.EncounterAsync(id);if(old is not null&&old.Battle.Outcome!=CombatOutcome.Withdrawn){await OpenBattle();return;}var flow=Panel("矿区实际遭遇入口 · "+id,InputOwner.Confirmation);Label(flow,"使用当前主角、装备与实际可援助人员。难度在进场时冻结，撤回恢复战前入口；战斗不发击杀经验。",20);var difficulty=ChapterThreeChoice(flow,"矿区遭遇难度","标准","宽容","挑战");var view=await _chapterThree.ViewAsync();var supplies=new Dictionary<string,CheckBox>();foreach(var item in view.Snapshot.Items.Where(i=>i.OwnerKind=="player"&&i.OwnerId=="hero"&&i.DefinitionId.StartsWith("CON-",StringComparison.Ordinal))){var check=new CheckBox{Text=item.DefinitionId+" · "+item.Id};flow.AddChild(check);supplies.Add(item.Id,check);}Button(flow,"明确进入矿区所选遭遇",()=>Execute(async()=>{CheckCommit(await _chapterThree.BeginEncounterAsync(id,new[]{"standard","relaxed","challenge"}[difficulty.Selected],supplies.Where(x=>x.Value.ButtonPressed).Select(x=>x.Key).ToArray()));CloseAllPanels();await Refresh();await OpenBattle();}));
    }
}


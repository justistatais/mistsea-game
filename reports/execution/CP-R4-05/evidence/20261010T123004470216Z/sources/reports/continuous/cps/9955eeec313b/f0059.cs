using System.Collections.Immutable;
using Godot;
using Mist.Application.ChapterFour;
using Mist.Application.Drafts;
using Mist.Application.UI;
using Mist.Core.ChapterFour;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Quests;

public partial class Root
{
    private string? _chapterFourSelectedEncounter;
    private bool ChapterFourEncounterSelected=>_chapterFour is not null&&_chapterFourSelectedEncounter is not null;
    private readonly Dictionary<string,DraftEditor> _chapterFourEditors=new();
    private readonly Dictionary<string,Stack<Guid>> _chapterFourUiPreviews=new();
    private TextEdit? _chapterFourWriter;
    private Label? _chapterFourSaveStatus;
    private Godot.Timer? _chapterFourSaveTimer;
    private Task<bool>? _chapterFourSaveTask;
    private static string ChapterFourWriterTitle(string quest)=>"第四章正文 · "+quest;
    private static string ChapterFourQuestName(string quest)=>quest switch{"Q-401"=>"独立数据与同版差异","Q-402"=>"失败复原与参考标","Q-403"=>"有限孤网实际试验","Q-404"=>"带边界的技术交接","CQ-MIRA-02"=>"限定角色审阅副本",_=>quest};
    private void TrackChapterFourPreview(string title,Guid token){if(!_chapterFourUiPreviews.TryGetValue(title,out var stack)){stack=new();_chapterFourUiPreviews[title]=stack;}stack.Push(token);}
    private void CloseChapterFourUiPreview(string title)
    {
        if(_chapterFourUiPreviews.TryGetValue(title,out var stack)&&stack.TryPop(out var token))_chapterFour?.ClosePreview(token);
        if(title.StartsWith("第四章正文 · ",StringComparison.Ordinal)){_chapterFourWriter=null;_chapterFourSaveStatus=null;_chapterFourSaveTimer=null;_=SaveChapterFourDraft();}
    }
    private void ResetChapterFourUiState()
    {
        foreach(var stack in _chapterFourUiPreviews.Values)foreach(var token in stack)_chapterFour?.ClosePreview(token);
        _chapterFourUiPreviews.Clear();_chapterFourEditors.Clear();_chapterFourSelectedEncounter=null;_chapterFourWriter=null;_chapterFourSaveStatus=null;_chapterFourSaveTimer=null;_chapterFourSaveTask=null;
    }
    private void ChapterFourButton(VBoxContainer flow,string caption,Func<ValueTask> action,bool close=true)=>Button(flow,caption,()=>Execute(async()=>{await action();if(close)ClosePanel();}));
    private static OptionButton ChapterFourChoice(VBoxContainer flow,string name,params string[] choices)
    {
        var option=new OptionButton{FitToLongestItem=false,AccessibilityName=name};foreach(string choice in choices)option.AddItem(choice);flow.AddChild(option);return option;
    }
    private static string ChapterFourSelected(OptionButton option,string[] values)=>option.Selected>=0&&option.Selected<values.Length?values[option.Selected]:"";
    private async ValueTask OpenChapterFourWriter(string quest)
    {
        if(quest=="R4-OPTIONAL-STUDY"){await OpenChapterFourStudyWriter();return;}
        if(quest=="R4-OPTIONAL-CREDIT"){await OpenChapterFourCreditWriter();return;}
        if(_chapterFour is null)return;if(!await SaveChapterFourDraft())throw new InvalidOperationException("请先完成输入法候选并保存当前正文。");string title=ChapterFourWriterTitle(quest);
        if(_routing.Contains(title)){while(_routing.Top!=title)ClosePanel();_chapterFourWriter?.GrabFocus();return;}
        var task=_chapterFour.Package.WritingTasks.Single(t=>t.Quest==quest);if(!_chapterFourEditors.TryGetValue(quest,out var editor)){var head=await _store.GetDraftAsync(task.Id);var session=_store.Session;editor=new(_store,new(session.Profile,session.Branch,session.Load,task.Id,head?.Id,head?.Revision??0,head?.Body??"",_chapterFour.Package.Version));_chapterFourEditors.Add(quest,editor);}
        var flow=Panel(title,InputOwner.Editable);Label(flow,task.EnglishPrompt,22);Button(flow,"查看任务中文辅助",()=>Label(flow,task.ChinesePrompt,20));Label(flow,"读、听、写目标各6.5；当前不自动理解英文，不生成能力分数。自由写作从空稿开始，结构化辅助须另行明确选择。",18);
        if(task.Visual is{} table)
        {
            var view=await _chapterFour.ViewAsync();if(quest=="Q-401"?new[]{"R4-Q401-R-SURVEY-A","R4-Q401-R-SURVEY-B","R4-Q401-R-VISUAL"}.All(id=>view.Materials.Any(m=>m.Id==id)):view.Runs.Any(r=>r.Minutes==10)){Label(flow,table.Title+"\n"+string.Join(" | ",table.Columns)+"\n"+string.Join("\n",table.Rows.Select(r=>$"{r.Label} | {r.RollEntries} | {r.ResidenceConfirmations}")),20);}else Label(flow,"图表须有实际来源；流程图须有实际已保存运行后显示。",18);
        }
        var writer=new TextEdit{Text=editor.Text,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,330),AccessibilityName=quest+"英文正文"};writer.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(writer);_chapterFourWriter=writer;_chapterFourSaveStatus=Label(flow,editor.IsSaved?"当前正文与保存版本一致。":"当前正文待保存。",18);
        var timer=new Godot.Timer{OneShot=true,WaitTime=.5};flow.AddChild(timer);_chapterFourSaveTimer=timer;timer.Timeout+=()=>{if(Composing()){timer.Start(.2);return;}_=SaveChapterFourDraft();};writer.TextChanged+=()=>{editor.Edit(writer.Text);if(_chapterFourSaveStatus is{} status&&IsInstanceValid(status))status.Text="有未保存修改。";foreach(var stack in _chapterFourUiPreviews.Values)foreach(var token in stack)_chapterFour.ClosePreview(token);_chapterFourUiPreviews.Clear();timer.Start();};
        writer.FocusExited+=()=>{foreach(var key in _keys.Values.Append(Key.Enter))if(Input.IsPhysicalKeyPressed(key))_releaseBarrier.Add(key);};
        var back=flow.GetChild<HBoxContainer>(0).GetChild<Button>(1);back.Pressed-=ClosePanel;back.Pressed+=()=>Execute(async()=>{if(!await SaveChapterFourDraft())throw new InvalidOperationException("正文尚未保存，完成输入法候选后重试或导出。");ClosePanel();});
        Button(flow,"保存第四章正文",()=>Execute(async()=>{if(!await SaveChapterFourDraft())throw new InvalidOperationException("正文保存尚未完成。");}));Button(flow,"导出当前第四章正文",()=>{string dir=ProjectSettings.GlobalizePath("user://exports");Directory.CreateDirectory(dir);string path=Path.Combine(dir,quest+"-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+".txt");File.WriteAllText(path,editor.Text,new System.Text.UTF8Encoding(false));_status.Text="当前正文已导出："+path;});Button(flow,"明确核对正文意思",()=>Execute(async()=>await OpenChapterFourMeaning(quest,false)));Button(flow,"明确使用结构化辅助",()=>Execute(async()=>await OpenChapterFourMeaning(quest,true)));Button(flow,"查看当前提交预览",()=>Execute(async()=>await OpenChapterFourSubmission(quest)));writer.GrabFocus();
    }
    private async ValueTask<bool> SaveChapterFourDraft()
    {
        if(_chapterFourEditors.Count==0)return true;if(Composing())return false;if(_chapterFourSaveTask is not null&&!_chapterFourSaveTask.IsCompleted)return await _chapterFourSaveTask;_chapterFourSaveTask=SaveChapterFourDraftCore();return await _chapterFourSaveTask;
    }
    private async Task<bool> SaveChapterFourDraftCore()
    {
        try{foreach(var editor in _chapterFourEditors.Values)if(!editor.IsSaved)await editor.SaveAsync();bool saved=_chapterFourEditors.Values.All(e=>e.IsSaved);if(_chapterFourSaveStatus is{} label&&IsInstanceValid(label))label.Text=saved?"正文已由实际保存回执确认。":"修改尚未全数保存，旧回执不会覆盖新稿。";if(!saved&&_chapterFourSaveTimer is{} timer&&IsInstanceValid(timer))timer.Start(.2);return saved;}
        catch(Exception error){if(_chapterFourSaveStatus is{} label&&IsInstanceValid(label))label.Text="保存失败，正文仍可导出："+error.Message;_status.Text="第四章正文保存失败："+error.Message;return false;}
    }
    private async ValueTask<bool> TryChapterFourInteraction(string spot)
    {
        if(_chapterFour is null||!spot.StartsWith("R4-",StringComparison.Ordinal))return false;var materials=_chapterFour.Package.Materials.Where(m=>m.Spot==spot).ToArray();var stations=_chapterFour.Package.Stations.Where(s=>s.Spot==spot).ToArray();
        if(materials.Length==0&&stations.Length==0&&spot is not("R4-S01.berth" or "R4-S06.zero" or "R4-S06.reference"))return false;
        var view=await _chapterFour.ViewAsync();var flow=Panel("第四章现场 · "+spot,InputOwner.Workspace);AddScopedPersonalDetailButton(flow,spot);Label(flow,"原创灰盒候选 · 人工内容审校待完成。实体工作、配置检查、实际运行和稿件交接分别记录。",18);
        foreach(var material in materials){string id=material.Id;Button(flow,(view.Materials.Any(m=>m.Id==id)?"查看已得材料：":"实际领取/读取：")+material.Title,()=>Execute(async()=>{CheckCommit(await _chapterFour.DiscoverAsync(id));await OpenChapterFourMaterial(id);}));}
        foreach(var station in stations)Button(flow,station.Label,()=>Execute(async()=>await OpenChapterFourStation(station.Action)));
        if(spot is "R4-S06.zero" or "R4-S06.reference"){string channel=spot.EndsWith(".zero",StringComparison.Ordinal)?"Z":"R";ChapterFourButton(flow,"实际检查当前"+channel+"参考标",async()=>CheckCommit(await _chapterFour.InspectCalibrationChannelAsync(channel)));}
        if(spot is "R4-S01.berth" or "R4-S02.sian" or "R4-S08.return_check")ChapterFourButton(flow,"登记当前实际安全返程点",async()=>CheckCommit(await _chapterFour.RegisterCurrentReturnAnchorAsync()));return true;
    }
    private async ValueTask OpenChapterFourStation(string action)
    {
        if(_chapterFour is null)return;if(await TryChapterFourStudyAction(action))return;if(await TryChapterFourCreditAction(action))return;switch(action)
        {
            case "submit_data_overview":await OpenChapterFourWorkspace("Q-401");return;
            case "submit_failure_summary":await OpenChapterFourWorkspace("Q-402");return;
            case "submit_process_report":await OpenChapterFourWorkspace("Q-403");return;
            case "submit_bounded_report":await OpenChapterFourWorkspace("Q-404");return;
            case "request_report_scope":OpenChapterFourScope(false);return;
            case "request_facility_scope":OpenChapterFourScope(true);return;
            case "compare_field_sources":OpenChapterFourFieldComparison();return;
            case "compare_report_versions":OpenChapterFourVersionComparison();return;
            case "compare_trial_pair":OpenChapterFourTrialPair();return;
            case "reserve_procedure":OpenChapterFourReserve();return;
            case "controller_a_run":await OpenChapterFourExperiment("A");return;
            case "controller_c_run":await OpenChapterFourExperiment("C");return;
            case "check_calibration":var flow=Panel("当前仪表校准",InputOwner.Workspace);Label(flow,"零位与参考位须分别走到Z/R真实站位检查当前读数；校准台不远程代做两项。",22);return;
            case "assign_trial_duty":OpenChapterFourDuty();return;
            case "set_island_load":OpenChapterFourIslandLoad();return;
            case "set_island_contact":OpenChapterFourContact();return;
            case "prepare_output_route":OpenChapterFourOutput();return;
            case "set_prism_load":OpenChapterFourPrismLoad();return;
            case "verify_north_station":OpenChapterFourLocation();return;
            case "exchange_voss":OpenChapterFourVoss();return;
            case "accept_mira_review":OpenChapterFourMiraInvitation();return;
            case "deliver_mira_review":OpenChapterFourReviewCopy();return;
            case "begin_debris_encounter":await OpenChapterFourEncounterEntry("ENC-R4-01");return;
            case "begin_calibration_encounter":await OpenChapterFourEncounterEntry("ENC-R4-03");return;
            case "begin_execution_encounter":await OpenChapterFourEncounterEntry("ENC-R4-04");return;
            case "save_optional_inquiry":await OpenChapterFourWriter("R4-OPTIONAL-STUDY");return;
            case "save_optional_credit":await OpenChapterFourWriter("R4-OPTIONAL-CREDIT");return;
            default:CheckCommit(await _chapterFour.ExecuteStationAsync(action));ClosePanel();_status.Text="实际操作已记录；配置变更会使旧当前检查失效。";return;
        }
    }
    private async ValueTask OpenChapterFourQuests()
    {
        if(_chapterFour is null)return;var v=await _chapterFour.ViewAsync();var flow=Panel("第四章工作与实际记录",InputOwner.Workspace);Label(flow,"Q401与Q402可并行。Q403需两份真实报告。推荐等级不作为硬门；同行招募不作为公共核查条件。",20);
        foreach(var q in v.Quests){Label(flow,ChapterFourQuestName(q.Id)+" · "+q.Lifecycle,22);if(q.Lifecycle==QuestLifecycle.Active){string id=q.Id;Button(flow,"编辑 "+id+" 同一份正文",()=>Execute(async()=>await OpenChapterFourWriter(id)));Button(flow,"核对 "+id+" 当前提交预览",()=>Execute(async()=>await OpenChapterFourSubmission(id)));}}
        Label(flow,$"样品实际收件 {v.State.SampleReceived} · 当前孤网配置版本 {v.State.IslandRevision} · 棱镜配置版本 {v.State.PrismRevision}",20);
        foreach(var run in v.Runs)Label(flow,$"{run.Configuration.Controller} · 负荷{run.Configuration.Load} / 储备{run.Configuration.Reserve} · {run.Minutes}模拟分钟 · {run.Outcome} · {run.Id}",18);
        Label(flow,"已保存失败保留为反例；当前安全与永久六塔改造分别核验。导师当前状态未知。",18);
        foreach(string id in new[]{"ENC-R4-01","ENC-R4-02","ENC-R4-03","ENC-R4-04"})if(await _chapterFour.EncounterAsync(id) is{} encounter){string key=id;Button(flow,"查看 "+id+" · "+encounter.Battle.Outcome,()=>Execute(async()=>{_chapterFourSelectedEncounter=key;await OpenBattle();}));}
    }
    private async ValueTask OpenChapterFourArchive()
    {
        if(_chapterFour is null)return;var v=await _chapterFour.ViewAsync();var flow=Panel("第四章已得档案",InputOwner.Workspace);AddChapterFourStudyArchiveButton(flow);AddChapterFourCreditArchiveButton(flow);if(v.Materials.Length==0)Label(flow,"尚未在实际现场取得当前获准材料。",22);foreach(var m in v.Materials){string id=m.Id;Button(flow,m.Title,()=>Execute(async()=>await OpenChapterFourMaterial(id)));}Button(flow,"工作与实际记录",()=>Execute(OpenChapterFourQuests));
    }
    private async ValueTask OpenChapterFourMaterial(string id)
    {
        if(_chapterFour is null)return;var m=await _chapterFour.OpenMaterialAsync(id);var flow=Panel("第四章材料 · "+m.Title,InputOwner.Workspace);Label(flow,m.Scene+" · "+m.Version+" · 原创未人审",18);var reader=new TextEdit{Text=m.English,Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,350),AccessibilityName=m.Title+"英文原文"};reader.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(reader);AddRegionalDictionary(flow,reader);AddScopedPersonalSourceControls(flow,m.Quest);
        Label(flow,m.Kind=="listening_script"?"当前查看书面脚本；候选音轨须另行打开并实际播放，本次读取不记作听录音。":"原文取得与世界核验分别记录；既有失败不会随新实验或观点改变。",18);AddRegionalAudioButton(flow,m,()=>_chapterFour.OpenMaterialAsync(id));Button(flow,"查看中文辅助",()=>Label(flow,m.Chinese,20));if(_chapterFour.Package.WritingTasks.Any(t=>t.Quest==m.Quest))Button(flow,"编辑对应的同一份正文",()=>Execute(async()=>await OpenChapterFourWriter(m.Quest)));
    }
    private async ValueTask OpenChapterFourWorkspace(string quest)
    {
        await Task.CompletedTask;var flow=Panel("第四章工作台 · "+ChapterFourQuestName(quest),InputOwner.Workspace);AddScopedPersonalSourceControls(flow,quest);Label(flow,"自由正文可提前保存，当前来源、实际处置、运行配置和明确意思将在交接时重新核验。",22);Button(flow,"编辑同一份正文",()=>Execute(async()=>await OpenChapterFourWriter(quest)));Button(flow,"明确核对正文意思",()=>Execute(async()=>await OpenChapterFourMeaning(quest,false)));Button(flow,"明确使用结构化辅助",()=>Execute(async()=>await OpenChapterFourMeaning(quest,true)));Button(flow,"查看当前提交预览",()=>Execute(async()=>await OpenChapterFourSubmission(quest)));
    }
    private sealed record ChapterFourMeaningRow(string Key,string Name,string[] Labels,string[] Values);
    private static ChapterFourMeaningRow ChapterFourRow(string key,string name,string yesLabel,string yes,string otherLabel,string other)=>new(key,name,["尚未确认",yesLabel,otherLabel],["",yes,other]);
    private static ChapterFourMeaningRow[] ChapterFourMeaningRows(string quest)=>quest switch
    {
        "Q-401"=>[ChapterFourRow("hypothesis","调查支持的假设","关联需受控检验","association_requires_controlled_test","已证明全部机制","complete_causation"),ChapterFourRow("field_limits","调查条件","地点与历史尚未控制","location_and_history_uncontrolled","所有条件均已控制","all_controlled"),ChapterFourRow("report_difference","同版发行差异","同版不利行被移除","same_version_unfavourable_rows_removed","仅日期不同即可判定删除","dates_alone"),ChapterFourRow("replacement_safety","替代方案状态","尚未验证","not_yet_validated","全部安全已获认证","fully_approved")],
        "Q-402"=>[ChapterFourRow("trial_scope","失败记录范围","A控制器十分钟","Controller_A_ten_minutes","六塔永久使用","all_towers_permanent"),ChapterFourRow("load55","55负荷","未测","untested","插值即可认定安全","interpolated_safe"),ChapterFourRow("whole_network","全网范围","尚未证明","not_established","所有地区均已证明","proved"),ChapterFourRow("failed_result","实际失败的含义","保留失败，不能证明所有本地方案不可能","retained_not_all_local_impossible","失败应删除","erase_failure")],
        "Q-403"=>[ChapterFourRow("process","实际流程","准备、执行、观察、接受分别成立","prepare_execute_observe_accept","准备等同实验完成","prepare_is_complete"),ChapterFourRow("scope","稳定记录范围","当前配置十分钟","current_configuration_ten_minutes","永久全网","all_network_forever"),ChapterFourRow("battle_meaning","制止执行机的含义","设备安全，不等于实验数据","device_safe_not_trial_data","战斗即生成稳定读数","battle_proves_data"),ChapterFourRow("counterexamples","反例记录","保留","retained","删除","erased"),ChapterFourRow("load55","55负荷","未测","untested","已认证","certified")],
        "Q-404"=>[ChapterFourRow("technical_scope","技术建议边界","有限验证，非全网认证","bounded_not_full_network","完整全网已批准","full_network_approved"),ChapterFourRow("mist","停超载与雾","不会立即消除既有雾","not_immediately_cleared","雾立即消失","immediate_clear"),ChapterFourRow("independent_review","独立核验","保留","preserved","取消","cancelled"),ChapterFourRow("mara_current_status","导师当前状态","未知","unknown","已确认获救","rescued"),ChapterFourRow("location","地点核查","三来源对应旧都北站","old_city_north_station","凭猜测确定全部现状","all_current_known"),ChapterFourRow("preparation","公开准备清单","公开需求，实际工程仍待完成","public_actual_work_pending","发布即可完成全部工程","all_work_complete")],
        "CQ-MIRA-02"=>[ChapterFourRow("fields","本次必要字段","角色数量与来源","role_counts_and_source_only","完整身份键","full_identity_key"),ChapterFourRow("recipient","本次接收方","学城复核台","R4-review-desk","所有公开看板","public_boards"),ChapterFourRow("purpose","本次用途","核验角色数量","verify_role_counts","寻找私人住址","find_private_address"),ChapterFourRow("identity_publication","身份发布","未获许可","not_permitted","已准全部公开","all_permitted"),ChapterFourRow("copy_independence","副本来源","同一来源","same_source","新的独立来源","independent")],
        _=>throw new InvalidOperationException("该可选任务可保存稿件，正式交付权益仍待制作。")
    };
    private async ValueTask OpenChapterFourMeaning(string quest,bool assisted)
    {
        if(quest=="R4-OPTIONAL-STUDY"){await OpenChapterFourStudyMeaning(assisted);return;}
        if(quest=="R4-OPTIONAL-CREDIT"){await OpenChapterFourCreditMeaning(assisted);return;}
        if(_chapterFour is null)return;if(!await SaveChapterFourDraft())throw new InvalidOperationException("请先完成输入法候选并保存正文。");var flow=Panel("第四章意思确认 · "+quest,InputOwner.Confirmation);Label(flow,assisted?"这是明确结构化辅助，不计作独立英文理解；仍须核验实际来源、权限和操作结果。":"当前离线模式没有自动理解英文，请逐项明确确认你写下的意思。",22);var rows=ChapterFourMeaningRows(quest);var choices=new List<OptionButton>();foreach(var row in rows){Label(flow,row.Name,20);choices.Add(ChapterFourChoice(flow,row.Name,row.Labels));}
        Button(flow,"明确记录当前第四章提议",()=>Execute(async()=>{if(!await SaveChapterFourDraft())throw new InvalidOperationException("当前正文尚未保存。");var fields=rows.Select((r,i)=>(r.Key,Value:ChapterFourSelected(choices[i],r.Values))).ToImmutableDictionary(x=>x.Key,x=>x.Value);if(fields.Values.Any(string.IsNullOrWhiteSpace))throw new InvalidOperationException("请逐项明确选择，不能沿用未确认项。");CheckCommit(await _chapterFour.ConfirmMeaningAsync(quest,fields,assisted));ClosePanel();_status.Text="当前稿件意思已记录，世界结果尚未提交。";}));
    }
    private async ValueTask OpenChapterFourSubmission(string quest)
    {
        if(_chapterFour is null)return;if(!await SaveChapterFourDraft())throw new InvalidOperationException("正文尚未保存。");var p=await _chapterFour.PreviewSubmissionAsync(quest);string title="第四章提交预览 · "+quest;var flow=Panel(title,InputOwner.Confirmation);Label(flow,p.Ready?"当前来源、配置、实际工作与明确提议可共同提交。关闭将取消，稿件和世界变化会使预览失效。":"尚未满足当前交接条件。",22);foreach(var issue in p.Issues)Label(flow,issue.SafeMessage,20);Label(flow,"预览不发奖、不推进任务；语言评分和人审仍待完成。",18);if(!p.Ready)return;TrackChapterFourPreview(title,p.Token);Button(flow,"明确提交第四章当前结果",()=>Execute(async()=>{if(!await SaveChapterFourDraft())throw new InvalidOperationException("当前正文尚未保存。");CheckCommit(await _chapterFour.ConfirmPreviewAsync(p.Token));CloseAllPanels();_status.Text="实际工作已交接，原固定奖励只结算一次。";}));
    }
    private Dictionary<string,CheckBox> ChapterFourFields(VBoxContainer flow,string[] names,string[] labels)
    {var fields=new Dictionary<string,CheckBox>();for(int i=0;i<names.Length;i++){var check=new CheckBox{Text=labels[i],AccessibilityName=labels[i]+"字段"};flow.AddChild(check);fields.Add(names[i],check);}return fields;}
    private void OpenChapterFourScope(bool facility)
    {
        if(_chapterFour is null)return;var flow=Panel(facility?"设施地点限定调阅":"第7版报告限定调阅",InputOwner.Confirmation);Label(flow,facility?"这是F17第1版设施资料的单独申请；报告GH-R7许可不会自动扩展到地点资料。":"同版原稿、发行稿与流转册按必要字段核验，政治观点不作为许可条件。",22);string[] names=facility?["facility_code","supply_destination","watch_post","version_source"]:["observation_ids","test_conditions","outcomes","issue_version","circulation_dates"],labels=facility?["设施代号","供给去向","看守岗位","来源版本"]:["观察编号","受试条件","结果条目","发行版本","流转日期"];var fields=ChapterFourFields(flow,names,labels);Label(flow,"接收方：本次巡灯技术员。私人住址、医疗现状和身份键不在范围。",18);ChapterFourButton(flow,"明确申请本次必要字段",async()=>{var selected=fields.Where(p=>p.Value.ButtonPressed).Select(p=>p.Key).ToImmutableArray();CheckCommit(facility?await _chapterFour.RequestFacilityScopeAsync(selected,"patrol_technician","verify_holding_facility","1"):await _chapterFour.RequestReportScopeAsync("7",selected,"patrol_technician","verify_public_data"));});
    }
    private void OpenChapterFourFieldComparison()
    {
        if(_chapterFour is null)return;var flow=Panel("独立调查与可检验假设",InputOwner.Confirmation);Label(flow,"逐项核对材料声明的团队、窗口、计量和未控制条件；比较表不是第三个独立调查。",22);var independent=ChapterFourChoice(flow,"两队来源","尚未确认","独立原始团队","同一复制件");var window=ChapterFourChoice(flow,"调查窗口","尚未确认","W-1","所有时段");var units=ChapterFourChoice(flow,"读数计量","尚未确认","现场指标，非试验台同一尺度","全部尺度等同");var causality=ChapterFourChoice(flow,"调查证明范围","尚未确认","关联待受控检验","已证明全部因果");var limits=ChapterFourChoice(flow,"未控制条件","尚未确认","地点与历史","没有限制");ChapterFourButton(flow,"明确记录调查比较",async()=>{if(new[]{independent,window,units,causality,limits}.Any(c=>c.Selected==0))throw new InvalidOperationException("请逐项核对。");CheckCommit(await _chapterFour.CompareFieldSourcesAsync(new(independent.Selected==1,ChapterFourSelected(window,["","W-1","all_time"]),ChapterFourSelected(units,["","local_field_indicator_not_trial_scale","all_identical"]),ChapterFourSelected(causality,["","association_to_test","all_proven"]),ChapterFourSelected(limits,["","location_and_history","none"]))));});
    }
    private void OpenChapterFourVersionComparison()
    {
        if(_chapterFour is null)return;var flow=Panel("同版原稿与发行链",InputOwner.Confirmation);var version=ChapterFourChoice(flow,"报告版本链","尚未确认","GH-R7-v7同版三来源","仅日期不同");var rows=ChapterFourFields(flow,["T1","T2","T3","T4"],["T1行被移除","T2行被移除","T3行被移除","T4行被移除"]);var independent=ChapterFourChoice(flow,"发行副本独立性","尚未确认","同一来源链","新的独立试验");ChapterFourButton(flow,"明确记录同版发行差异",async()=>{if(version.Selected==0||independent.Selected==0)throw new InvalidOperationException("先明确核对版本和副本来源。");string id=version.Selected==1?"GH-R7-v7":"date_only";CheckCommit(await _chapterFour.CompareReportVersionsAsync(new(id,id,id,version.Selected==1?"7":"unknown",rows.Where(p=>p.Value.ButtonPressed).Select(p=>p.Key).ToImmutableArray(),independent.Selected==2)));});
    }
    private void OpenChapterFourTrialPair()
    {
        if(_chapterFour is null)return;var flow=Panel("冻结试验的具体比较问题",InputOwner.Confirmation);Label(flow,"负荷问题与储备问题分别核验；55未测，不能插值生造记录。",22);var question=ChapterFourChoice(flow,"比较问题","尚未选择","负荷变化","储备变化");var a=ChapterFourChoice(flow,"第一记录","尚未选择","T1","T2","T3","T4");var b=ChapterFourChoice(flow,"第二记录","尚未选择","T1","T2","T3","T4");ChapterFourButton(flow,"明确记录可比问题与两行",async()=>{if(new[]{question,a,b}.Any(c=>c.Selected==0))throw new InvalidOperationException("先明确选择问题与两记录。");CheckCommit(await _chapterFour.CompareTrialPairAsync(question.Selected==1?"load":"reserve","T"+a.Selected,"T"+b.Selected));});
    }
    private void OpenChapterFourReserve()
    {
        if(_chapterFour is null)return;var flow=Panel("隔离备用段实际处置",InputOwner.Confirmation);Label(flow,"须先在隔离位完成实际隔离；手動四步与独立战斗处置择一完成此领域。",22);foreach(var pair in new[]{("open","实际打开备用段护罩"),("clear","实际清理备用段"),("secure","实际重新固定备用护罩"),("inspect","复核备用段零运动")}){string step=pair.Item1;ChapterFourButton(flow,pair.Item2,async()=>CheckCommit(await _chapterFour.PerformReserveStepAsync(step)));}Button(flow,"查看备用段独立战斗入口",()=>Execute(async()=>await OpenChapterFourEncounterEntry("ENC-R4-02")));
    }
    private async ValueTask OpenChapterFourExperiment(string controller)
    {
        if(_chapterFour is null)return;var v=await _chapterFour.ViewAsync();var flow=Panel("实际运行与观察 · "+controller,InputOwner.Confirmation);Label(flow,"选择配置是准备。启动后须在声明模拟钟第5、第10分钟实际观察；没有阅读限时。配置变化会拒绝给旧跑次写新读数，历史失败仍保留。",22);
        if(controller=="A")
        {
            ChapterFourButton(flow,"实际固定旧台护罩",async()=>CheckCommit(await _chapterFour.PrepareOldBenchStepAsync("secure_guard")));ChapterFourButton(flow,"实际检查旧台插座",async()=>CheckCommit(await _chapterFour.PrepareOldBenchStepAsync("inspect_socket")));var load=ChapterFourChoice(flow,"A负荷","尚未选择","40","70","55（未测）");var reserve=ChapterFourChoice(flow,"A储备","尚未选择","连接","不连接");ChapterFourButton(flow,"实际设置A配置",async()=>{if(load.Selected==0||reserve.Selected==0)throw new InvalidOperationException("先明确选择配置。");CheckCommit(await _chapterFour.ConfigureControllerAAsync(new[]{0,40,70,55}[load.Selected],reserve.Selected==1));});
        }
        else Label(flow,$"当前C：负荷{v.State.IslandLoad} / 储备{v.State.IslandReserve} / 联络{v.State.IslandContact} / 岗位{v.State.TrialOperator}、{v.State.TrialReviewer} / 配置{v.State.IslandRevision} / 输出{v.State.OutputRevision}",18);
        ChapterFourButton(flow,"实际启动"+controller+"本次运行",async()=>CheckCommit(await _chapterFour.StartExperimentAsync(controller)));
        foreach(var run in v.Runs.Where(r=>r.Configuration.Controller==controller))
        {
            Label(flow,$"{run.Id} · 配置{run.Configuration.Revision}/{run.Configuration.OutputRevision} · {run.Minutes}模拟分钟 · {run.Outcome}",18);foreach(var obs in run.Observations)Label(flow,$"第{obs.Minute}分钟：{obs.Result}",18);if(run.Outcome=="running"){Guid id=run.Id;int expected=run.Minutes;ChapterFourButton(flow,"实际观察下一模拟分钟 · "+(expected+5),async()=>CheckCommit(await _chapterFour.ObserveExperimentAsync(id,expected)));}
        }
        ChapterFourButton(flow,"明确局部复位"+controller+"，保留历史跑次",async()=>CheckCommit(await _chapterFour.ResetExperimentAsync(controller)));
    }
    private void OpenChapterFourDuty()
    {
        if(_chapterFour is null)return;var flow=Panel("当前有限试验岗位",InputOwner.Confirmation);Label(flow,"只登记本次已隔离试验窗口的操作员和复核员，不授予六塔永久岗位。",22);var operatorId=ChapterFourChoice(flow,"试验操作员","尚未选择","西安 N-SIAN");var reviewer=ChapterFourChoice(flow,"试验复核员","尚未选择","本地试验复核岗位");ChapterFourButton(flow,"实际登记当前两试验岗位",async()=>{if(operatorId.Selected==0||reviewer.Selected==0)throw new InvalidOperationException("须明确选择两个岗位。");CheckCommit(await _chapterFour.AssignTrialDutyAsync("N-SIAN","ROLE-R4-TRIAL-REVIEW"));});
    }
    private void OpenChapterFourContact()
    {
        if(_chapterFour is null)return;var flow=Panel("当前试验联络",InputOwner.Confirmation);var contact=ChapterFourChoice(flow,"C联络","尚未选择","有限本地联络","全网连接（未获许可）");ChapterFourButton(flow,"实际设置当前联络",async()=>{CheckCommit(await _chapterFour.SetIslandContactAsync(ChapterFourSelected(contact,["","limited","network"])));});
    }
    private void OpenChapterFourIslandLoad()
    {
        if(_chapterFour is null)return;var flow=Panel("当前C试验配置",InputOwner.Confirmation);Label(flow,"两个岗位与有限联络须在各自站位已登记，配置之后再到校准台复核当前状态。",22);var load=ChapterFourChoice(flow,"C负荷","尚未选择","40","70","55（未测）");var reserve=ChapterFourChoice(flow,"C储备","尚未选择","连接当前已装储备","不连接储备");ChapterFourButton(flow,"实际设置C负荷与储备",async()=>{if(load.Selected==0||reserve.Selected==0)throw new InvalidOperationException("先明确选择配置。");CheckCommit(await _chapterFour.ConfigureIslandAsync(new[]{0,40,70,55}[load.Selected],reserve.Selected==1,"limited","N-SIAN","ROLE-R4-TRIAL-REVIEW"));});
    }
    private void OpenChapterFourOutput()
    {
        if(_chapterFour is null)return;var flow=Panel("执行机之后的实际输出",InputOwner.Confirmation);Label(flow,"实际制止执行机只确认威胁受控；仍须隔离、选TEST、座好连接器、固定护罩及检查当前通路。输出检查不是十分钟试验数据。",22);foreach(var pair in new[]{("isolate","实际隔离输出段"),("select_TEST","实际选择TEST输出"),("seat_coupler","实际座好输出连接器"),("secure_guard","实际固定输出护罩"),("inspect","复查当前TEST通路")}){string step=pair.Item1;ChapterFourButton(flow,pair.Item2,async()=>CheckCommit(await _chapterFour.PrepareOutputStepAsync(step)));}
    }
    private void OpenChapterFourPrismLoad()
    {
        if(_chapterFour is null)return;var flow=Panel("棱镜当前班次降载",InputOwner.Confirmation);Label(flow,"保护回路必须保留2。演示负荷归零，校准可保留1或停止0；当前操作不完成永久六塔改造。",22);var calibration=ChapterFourChoice(flow,"棱镜校准负荷","尚未选择","保留1","停止0");ChapterFourButton(flow,"实际设置保护2与演示0",async()=>{if(calibration.Selected==0)throw new InvalidOperationException("明确选择校准档。");CheckCommit(await _chapterFour.SetPrismLoadAsync(new(2,calibration.Selected==1?1:0,0)));});
    }
    private void OpenChapterFourLocation()
    {
        if(_chapterFour is null)return;var flow=Panel("三个独立来源的地点核验",InputOwner.Confirmation);Label(flow,"先分别取得获准设施、物资及看守原始记录，地点不等于当前生命状态或已完成救援。",22);var facility=ChapterFourChoice(flow,"设施代号","尚未确认","F-17","GH-R7");var supply=ChapterFourChoice(flow,"物资去向代号","尚未确认","F-17","全部地区");var guard=ChapterFourChoice(flow,"看守岗位代号","尚未确认","F-17","未知人物现状");var place=ChapterFourChoice(flow,"三源对应地点","尚未确认","旧都北站","当前已救回导师");var status=ChapterFourChoice(flow,"导师当前状态","尚未确认","未知","存活","死亡");ChapterFourButton(flow,"明确核对三来源地点与未知项",async()=>{if(new[]{facility,supply,guard,place,status}.Any(c=>c.Selected==0))throw new InvalidOperationException("逐项明确核对。");CheckCommit(await _chapterFour.VerifyNorthStationAsync(ChapterFourSelected(facility,["","F-17","GH-R7"]),ChapterFourSelected(supply,["","F-17","all_regions"]),ChapterFourSelected(guard,["","F-17","current_life"]),ChapterFourSelected(place,["","old_city_north_station","rescued"]),ChapterFourSelected(status,["","unknown","alive","dead"])));});
    }
    private void OpenChapterFourVoss()
    {
        if(_chapterFour is null)return;var flow=Panel("沃斯首次完整交涉",InputOwner.Dialogue);Label(flow,"核对已取得交涉脚本。真实供给、值守和责任条件与独立公共复核分别保留；政治偏好不裁定历史责任。",22);var supply=ChapterFourChoice(flow,"过渡周实际条件","尚未确认","承認供给、值守与责任需要","删除全部需求");var review=ChapterFourChoice(flow,"独立复核","尚未确认","保留","取消");var status=ChapterFourChoice(flow,"当前导师状态","尚未确认","未知","已救回");ChapterFourButton(flow,"实际记录本次交涉范围",async()=>{if(new[]{supply,review,status}.Any(c=>c.Selected==0))throw new InvalidOperationException("逐项明确核对。");CheckCommit(await _chapterFour.ExchangeVossAsync(supply.Selected==1,review.Selected==1,ChapterFourSelected(status,["","unknown","rescued"])));});
    }
    private void OpenChapterFourMiraInvitation()
    {
        if(_chapterFour is null)return;var flow=Panel("米拉限定审阅工作邀请",InputOwner.Dialogue);Label(flow,"需实际先前W04限定交付与本章公共报告，不要求正式同行。拒绝不阻断主线。",22);ChapterFourButton(flow,"接受本次限定审阅邀请",async()=>CheckCommit(await _chapterFour.AcceptMiraReviewAsync(true)));ChapterFourButton(flow,"暂不接受本次私人工作",async()=>CheckCommit(await _chapterFour.AcceptMiraReviewAsync(false)));
    }
    private void OpenChapterFourReviewCopy()
    {
        if(_chapterFour is null)return;var flow=Panel("必要角色审阅副本",InputOwner.Confirmation);Label(flow,"仅角色数量、版本与来源根；身份键留原保管处，不是新的独立证据。扩大字段须在个人工作现场单独询问本人；当前应答拒绝公开身份键或私人住址。",22);var fields=ChapterFourFields(flow,["role_counts","source_version","source_root"],["角色数量","来源版本","来源根"]);ChapterFourButton(flow,"实际生成本次必要字段副本",async()=>CheckCommit(await _chapterFour.GenerateReviewCopyAsync("R4-review-desk","verify_role_counts","W04-review-v1",fields.Where(p=>p.Value.ButtonPressed).Select(p=>p.Key).ToImmutableArray())));Button(flow,"编辑并核对实际交付正文",()=>Execute(async()=>await OpenChapterFourWorkspace("CQ-MIRA-02")));
    }
    private async ValueTask OpenChapterFourEncounterEntry(string id)
    {
        if(_chapterFour is null)return;_chapterFourSelectedEncounter=id;var old=await _chapterFour.EncounterAsync(id);if(old is not null&&old.Battle.Outcome!=CombatOutcome.Withdrawn){await OpenBattle();return;}var flow=Panel("第四章实际遭遇入口 · "+id,InputOwner.Confirmation);Label(flow,"使用真实当前队伍、装备、援助和共享战斗规则。没有击杀经验；装置制止、输出连接和实验结果各自核验。",22);var difficulty=ChapterFourChoice(flow,"第四章遭遇难度","标准","宽容","挑战");var v=await _chapterFour.ViewAsync();var supplies=new Dictionary<string,CheckBox>();foreach(var item in v.Snapshot.Items.Where(i=>i.OwnerKind=="player"&&i.OwnerId=="hero"&&i.DefinitionId.StartsWith("CON-",StringComparison.Ordinal))){var c=new CheckBox{Text=item.DefinitionId+" · "+item.Id};flow.AddChild(c);supplies.Add(item.Id,c);}Label(flow,"最多两件不同实际持有补给；配置与平衡仍待人工试玩。",18);Button(flow,"明确进入第四章所选遭遇",()=>Execute(async()=>{CheckCommit(await _chapterFour.BeginEncounterAsync(id,new[]{"standard","relaxed","challenge"}[difficulty.Selected],supplies.Where(p=>p.Value.ButtonPressed).Select(p=>p.Key).ToArray()));CloseAllPanels();await Refresh();await OpenBattle();}));
    }
}

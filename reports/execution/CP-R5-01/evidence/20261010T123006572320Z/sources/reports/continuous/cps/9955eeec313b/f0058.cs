using System.Collections.Immutable;
using Godot;
using Mist.Application.ChapterFive;
using Mist.Application.Drafts;
using Mist.Application.UI;
using Mist.Core.ChapterFive;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Quests;

public partial class Root
{
    private string? _chapterFiveSelectedEncounter;
    private bool ChapterFiveEncounterSelected=>_chapterFive is not null&&_chapterFiveSelectedEncounter is not null;
    private readonly Dictionary<string,DraftEditor> _chapterFiveEditors=new();
    private readonly Dictionary<string,Stack<Guid>> _chapterFiveUiPreviews=new();
    private TextEdit? _chapterFiveWriter;
    private Label? _chapterFiveSaveStatus;
    private Godot.Timer? _chapterFiveSaveTimer;
    private Task<bool>? _chapterFiveSaveTask;
    private static string ChapterFiveWriterTitle(string quest)=>"第五章正文 · "+quest;
    private static string ChapterFiveQuestName(string quest)=>quest switch{"Q-501"=>"现行道路与受限准入","Q-502"=>"实际救援与接应","Q-503"=>"分开的三种责任","Q-504"=>"临时共同处置与归档塔","CQ-MIRA-03"=>"可再次查验的限定副件","CQ-OREN-03"=>"可执行的有限维护岗位",_=>quest};
    private void TrackChapterFivePreview(string title,Guid token){if(!_chapterFiveUiPreviews.TryGetValue(title,out var stack)){stack=new();_chapterFiveUiPreviews[title]=stack;}stack.Push(token);}
    private void CloseChapterFiveUiPreview(string title)
    {
        if(_chapterFiveUiPreviews.TryGetValue(title,out var stack)&&stack.TryPop(out var token))_chapterFive?.ClosePreview(token);
        if(title.StartsWith("第五章正文 · ",StringComparison.Ordinal)){_chapterFiveWriter=null;_chapterFiveSaveStatus=null;_chapterFiveSaveTimer=null;_=SaveChapterFiveDraft();}
    }
    private void ResetChapterFiveUiState()
    {
        foreach(var stack in _chapterFiveUiPreviews.Values)foreach(var token in stack)_chapterFive?.ClosePreview(token);
        _chapterFiveUiPreviews.Clear();_chapterFiveEditors.Clear();_chapterFiveSelectedEncounter=null;_chapterFiveWriter=null;_chapterFiveSaveStatus=null;_chapterFiveSaveTimer=null;_chapterFiveSaveTask=null;
    }
    private void ChapterFiveButton(VBoxContainer flow,string caption,Func<ValueTask> action,bool close=true)=>Button(flow,caption,()=>Execute(async()=>{await action();if(close)ClosePanel();}));
    private static OptionButton ChapterFiveChoice(VBoxContainer flow,string name,params string[] choices)
    {
        var option=new OptionButton{FitToLongestItem=false,AccessibilityName=name};foreach(string choice in choices)option.AddItem(choice);flow.AddChild(option);return option;
    }
    private static string ChapterFiveSelected(OptionButton option,string[] values)=>option.Selected>=0&&option.Selected<values.Length?values[option.Selected]:"";
    private async ValueTask OpenChapterFiveWriter(string quest)
    {
        if(_chapterFive is null)return;if(!await SaveChapterFiveDraft())throw new InvalidOperationException("请先完成输入法候选并保存当前正文。");string title=ChapterFiveWriterTitle(quest);
        if(_routing.Contains(title)){while(_routing.Top!=title)ClosePanel();_chapterFiveWriter?.GrabFocus();return;}
        var task=_chapterFive.Package.WritingTasks.Single(t=>t.Quest==quest);if(!_chapterFiveEditors.TryGetValue(quest,out var editor)){var head=await _store.GetDraftAsync(task.Id);var session=_store.Session;editor=new(_store,new(session.Profile,session.Branch,session.Load,task.Id,head?.Id,head?.Revision??0,head?.Body??"",_chapterFive.Package.Version));_chapterFiveEditors.Add(quest,editor);}
        var flow=Panel(title,InputOwner.Editable);Label(flow,"意思确认站位 "+task.Spot+"；正式接收站位 "+_chapterFive.SubmissionSpot(quest),18);Label(flow,task.EnglishPrompt,22);Button(flow,"查看任务中文辅助",()=>Label(flow,task.ChinesePrompt,20));Label(flow,"读、听、写目标各6.5；当前不自动理解英文，不生成能力分数。自由写作从空稿开始，结构化辅助须另行明确选择。",18);
        var writer=new TextEdit{Text=editor.Text,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,330),AccessibilityName=quest+"英文正文"};writer.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(writer);_chapterFiveWriter=writer;_chapterFiveSaveStatus=Label(flow,editor.IsSaved?"当前正文与保存版本一致。":"当前正文待保存。",18);
        var timer=new Godot.Timer{OneShot=true,WaitTime=.5};flow.AddChild(timer);_chapterFiveSaveTimer=timer;timer.Timeout+=()=>{if(Composing()){timer.Start(.2);return;}_=SaveChapterFiveDraft();};writer.TextChanged+=()=>{editor.Edit(writer.Text);if(_chapterFiveSaveStatus is{} status&&IsInstanceValid(status))status.Text="有未保存修改。";foreach(var stack in _chapterFiveUiPreviews.Values)foreach(var token in stack)_chapterFive.ClosePreview(token);_chapterFiveUiPreviews.Clear();timer.Start();};
        writer.FocusExited+=()=>{foreach(var key in _keys.Values.Append(Key.Enter))if(Input.IsPhysicalKeyPressed(key))_releaseBarrier.Add(key);};
        var back=flow.GetChild<HBoxContainer>(0).GetChild<Button>(1);back.Pressed-=ClosePanel;back.Pressed+=()=>Execute(async()=>{if(!await SaveChapterFiveDraft())throw new InvalidOperationException("正文尚未保存，完成输入法候选后重试或导出。");ClosePanel();});
        Button(flow,"保存第五章正文",()=>Execute(async()=>{if(!await SaveChapterFiveDraft())throw new InvalidOperationException("正文保存尚未完成。");}));Button(flow,"导出当前第五章正文",()=>{string dir=ProjectSettings.GlobalizePath("user://exports");Directory.CreateDirectory(dir);string path=Path.Combine(dir,quest+"-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+".txt");File.WriteAllText(path,editor.Text,new System.Text.UTF8Encoding(false));_status.Text="当前正文已导出："+path;});Button(flow,"明确核对正文意思",()=>Execute(async()=>await OpenChapterFiveMeaning(quest,false)));Button(flow,"明确使用结构化辅助",()=>Execute(async()=>await OpenChapterFiveMeaning(quest,true)));Button(flow,"查看当前提交预览",()=>Execute(async()=>await OpenChapterFiveSubmission(quest)));writer.GrabFocus();
    }
    private async ValueTask<bool> SaveChapterFiveDraft()
    {
        if(_chapterFiveEditors.Count==0)return true;if(Composing())return false;if(_chapterFiveSaveTask is not null&&!_chapterFiveSaveTask.IsCompleted)return await _chapterFiveSaveTask;_chapterFiveSaveTask=SaveChapterFiveDraftCore();return await _chapterFiveSaveTask;
    }
    private async Task<bool> SaveChapterFiveDraftCore()
    {
        try{foreach(var editor in _chapterFiveEditors.Values)if(!editor.IsSaved)await editor.SaveAsync();bool saved=_chapterFiveEditors.Values.All(e=>e.IsSaved);if(_chapterFiveSaveStatus is{} label&&IsInstanceValid(label))label.Text=saved?"正文已由实际保存回执确认。":"修改尚未全数保存，旧回执不会覆盖新稿。";if(!saved&&_chapterFiveSaveTimer is{} timer&&IsInstanceValid(timer))timer.Start(.2);return saved;}
        catch(Exception error){if(_chapterFiveSaveStatus is{} label&&IsInstanceValid(label))label.Text="保存失败，正文仍可导出："+error.Message;_status.Text="第五章正文保存失败："+error.Message;return false;}
    }
    private async ValueTask OpenChapterFiveArchive()
    {
        if(_chapterFive is null)return;var v=await _chapterFive.ViewAsync();var flow=Panel("第五章已得档案",InputOwner.Workspace);if(v.Materials.Length==0)Label(flow,"尚未在实际现场取得当前获准材料。",22);foreach(var m in v.Materials){string id=m.Id;Button(flow,m.Title,()=>Execute(async()=>await OpenChapterFiveMaterial(id)));}Button(flow,"工作与实际记录",()=>Execute(OpenChapterFiveQuests));
    }
    private async ValueTask OpenChapterFiveMaterial(string id)
    {
        if(_chapterFive is null)return;var m=await _chapterFive.OpenMaterialAsync(id);var flow=Panel("第五章材料 · "+m.Title,InputOwner.Workspace);Label(flow,m.Scene+" · "+m.Version+" · 原创未人审",18);var reader=new TextEdit{Text=m.English,Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,350),AccessibilityName=m.Title+"英文原文"};reader.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(reader);AddRegionalDictionary(flow,reader);AddScopedPersonalSourceControls(flow,m.Quest);
        Label(flow,m.Kind=="listening_script"?"当前查看书面脚本；候选音轨须另行打开并实际播放，本次读取不记作听录音。":"原文取得与世界核验分别记录；历史责任不随偏好或救援改变。",18);AddRegionalAudioButton(flow,m,()=>_chapterFive.OpenMaterialAsync(id));Button(flow,"查看中文辅助",()=>Label(flow,m.Chinese,20));if(_chapterFive.Package.WritingTasks.Any(t=>t.Quest==m.Quest))Button(flow,"编辑对应的同一份正文",()=>Execute(async()=>await OpenChapterFiveWriter(m.Quest)));
    }
    private async ValueTask OpenChapterFiveWorkspace(string quest)
    {
        await Task.CompletedTask;var flow=Panel("第五章工作台 · "+ChapterFiveQuestName(quest),InputOwner.Workspace);AddScopedPersonalSourceControls(flow,quest);Label(flow,"正文可提前保存；请到写作任务所示工作台确认意思，再到正式接收站位预览交接。",22);Button(flow,"编辑同一份正文",()=>Execute(async()=>await OpenChapterFiveWriter(quest)));Button(flow,"明确核对正文意思",()=>Execute(async()=>await OpenChapterFiveMeaning(quest,false)));Button(flow,"明确使用结构化辅助",()=>Execute(async()=>await OpenChapterFiveMeaning(quest,true)));Button(flow,"查看当前提交预览",()=>Execute(async()=>await OpenChapterFiveSubmission(quest)));
    }
    private async ValueTask OpenChapterFiveMeaning(string quest,bool assisted)
    {
        if(_chapterFive is null)return;if(!await SaveChapterFiveDraft())throw new InvalidOperationException("请先完成输入法候选并保存正文。");var flow=Panel("第五章意思确认 · "+quest,InputOwner.Confirmation);Label(flow,assisted?"这是明确结构化辅助，不计作独立英文理解；仍须核验实际来源、权限和操作结果。":"当前离线模式没有自动理解英文，请逐项明确确认你写下的意思。",22);var rows=ChapterFiveMeaningRows(quest);var choices=new List<OptionButton>();foreach(var row in rows){Label(flow,row.Name,20);choices.Add(ChapterFiveChoice(flow,row.Name,row.Labels));}
        Button(flow,"明确记录当前第五章提议",()=>Execute(async()=>{if(!await SaveChapterFiveDraft())throw new InvalidOperationException("当前正文尚未保存。");var fields=rows.Select((r,i)=>(r.Key,Value:ChapterFiveSelected(choices[i],r.Values))).ToImmutableDictionary(x=>x.Key,x=>x.Value);if(fields.Values.Any(string.IsNullOrWhiteSpace))throw new InvalidOperationException("请逐项明确选择，不能沿用未确认项。");CheckCommit(await _chapterFive.ConfirmMeaningAsync(quest,fields,assisted));ClosePanel();_status.Text="当前稿件意思已记录，世界结果尚未提交。";}));
    }
    private async ValueTask OpenChapterFiveSubmission(string quest)
    {
        if(_chapterFive is null)return;if(!await SaveChapterFiveDraft())throw new InvalidOperationException("正文尚未保存。");var p=await _chapterFive.PreviewSubmissionAsync(quest);string title="第五章提交预览 · "+quest;var flow=Panel(title,InputOwner.Confirmation);Label(flow,p.Ready?"当前来源、配置、实际工作与明确提议可共同提交。关闭将取消，稿件和世界变化会使预览失效。":"尚未满足当前交接条件。",22);foreach(var issue in p.Issues)Label(flow,issue.SafeMessage,20);Label(flow,"预览不发奖、不推进任务；语言评分和人审仍待完成。",18);if(!p.Ready)return;TrackChapterFivePreview(title,p.Token);Button(flow,"明确提交第五章当前结果",()=>Execute(async()=>{if(!await SaveChapterFiveDraft())throw new InvalidOperationException("当前正文尚未保存。");CheckCommit(await _chapterFive.ConfirmPreviewAsync(p.Token));CloseAllPanels();_status.Text="实际工作已交接，原固定奖励只结算一次。";}));
    }
    private Dictionary<string,CheckBox> ChapterFiveFields(VBoxContainer flow,string[] names,string[] labels)
    {var fields=new Dictionary<string,CheckBox>();for(int i=0;i<names.Length;i++){var check=new CheckBox{Text=labels[i],AccessibilityName=labels[i]+"字段"};flow.AddChild(check);fields.Add(names[i],check);}return fields;}
    private async ValueTask OpenChapterFiveEncounterEntry(string id)
    {
        if(_chapterFive is null)return;_chapterFiveSelectedEncounter=id;var old=await _chapterFive.EncounterAsync(id);if(old is not null&&old.Battle.Outcome!=CombatOutcome.Withdrawn){await OpenBattle();return;}var flow=Panel("第五章实际遭遇入口 · "+id,InputOwner.Confirmation);Label(flow,"使用真实当前队伍、装备、援助和共享战斗规则。没有击杀经验；看守制服不等于开门接应，塔站防卫不等于完成安全检查。",22);var difficulty=ChapterFiveChoice(flow,"第五章遭遇难度","标准","宽容","挑战");var v=await _chapterFive.ViewAsync();var supplies=new Dictionary<string,CheckBox>();foreach(var item in v.Snapshot.Items.Where(i=>i.OwnerKind=="player"&&i.OwnerId=="hero"&&i.DefinitionId.StartsWith("CON-",StringComparison.Ordinal))){var c=new CheckBox{Text=item.DefinitionId+" · "+item.Id};flow.AddChild(c);supplies.Add(item.Id,c);}Label(flow,"最多两件不同实际持有补给；配置与平衡仍待人工试玩。",18);Button(flow,"明确进入第五章所选遭遇",()=>Execute(async()=>{CheckCommit(await _chapterFive.BeginEncounterAsync(id,new[]{"standard","relaxed","challenge"}[difficulty.Selected],supplies.Where(p=>p.Value.ButtonPressed).Select(p=>p.Key).ToArray()));CloseAllPanels();await Refresh();await OpenBattle();}));
    }
    private sealed record ChapterFiveMeaningRow(string Key,string Name,string[] Labels,string[] Values);
    private static ChapterFiveMeaningRow ChapterFiveRow(string key,string name,string label,string value,string otherLabel,string other)=>new(key,name,["尚未确认",label,otherLabel],["",value,other]);
    private static ChapterFiveMeaningRow[] ChapterFiveMeaningRows(string quest)=>quest switch
    {
        "Q-501"=>[ChapterFiveRow("historical_route","历史桥线","已关闭，不授当前准入","closed_not_navigation_permission","旧线即准入","old_line_is_permission"),new("current_route","实际走过的当前路线",["尚未确认","检修核查通道","基础隧道"],["","CHECKPOINT-DUTY-PASSAGE","TUNNEL-SERVICE-PASSAGE"]),ChapterFiveRow("scope","本次入口止境","值守厅，不是扣押室","duty_hall_not_detention","全部扣押室","all_detention"),ChapterFiveRow("residents","住民和名录","行政未列名，住民记得道路","administratively_unlisted_not_amnesiac","住民失忆","amnesiac"),ChapterFiveRow("mara_current_status","导师当前状态","未知","unknown","已确认获救","rescued")],
        "Q-502"=>[ChapterFiveRow("mara_current_status","直接救援结果","已直接核验活着获救","rescued_alive_directly_verified","制服即可认定获救","guard_control_is_rescue"),ChapterFiveRow("return","真实返程和接应","已开内撤离路并实际接收","opened_inner_route_actual_reception","准备接应就是到达","prepared_is_received"),ChapterFiveRow("old_D1","旧D1记录","保留历史日期和范围","historical_not_rewritten","今日获救覆盖旧记载","rewritten"),ChapterFiveRow("testimony","证言和核心证据","补充，不作唯一核心来源","additional_not_sole_core_evidence","唯一最终证据","sole_authority")],
        "Q-503"=>[ChapterFiveRow("mara","玛拉责任","设计缺口与迟来的纠正","design_limits_and_delayed_correction","全部后来决定归玛拉","all_later_decisions"),ChapterFiveRow("voss","沃斯责任","已知风险配额、试验及限制流转","known_risk_quota_test_and_suppression","供给理由免除责任","no_responsibility"),ChapterFiveRow("rook","鲁克责任","取消复核及扩大扣押","cancelled_review_and_expanded_detention","执行没有独立责任","no_execution_responsibility"),ChapterFiveRow("copy","原设计副本","与原件同源","same_as_original","新的独立证人","independent_witness"),ChapterFiveRow("response","反证机会","实际记录渠道，沉默不作证明","opportunity_recorded_silence_not_proof","沉默即承认","silence_is_admission"),ChapterFiveRow("privacy","公开范围","仅脱敏受托范围","redacted_scope_only","公开所有身份","all_identities")],
        "Q-504"=>[new("custody","实际原件保管",["尚未确认","受限公共档案","共同封存"],["","public_archive_bounded","joint_sealed"]),new("publication","实际公开范围",["尚未确认","脱敏结论","脱敏结论及技术条件"],["","redacted_findings","redacted_findings_and_conditions"]),ChapterFiveRow("supervision","共同监督","操作、住民、档案三岗已分别接受","operation_resident_archive_accepted","单人无限授权","one_person_unlimited"),ChapterFiveRow("mara","导师现状","活着获救，直接核验","rescued_alive_directly_verified","目前未知","unknown"),ChapterFiveRow("responsibilities","旧责任","各自保留","all_retained_separately","救援销除旧责任","rescued_cancels_responsibility"),ChapterFiveRow("rook","实际交接回复","拒交并转入第七塔","refused_transfer_entered_seventh_tower","讨论即已接受交接","accepted"),ChapterFiveRow("policy","最终系统政策","尚未选择","not_selected","本章已决定终局","selected"),ChapterFiveRow("preparation","六塔准备","实际工作仍待完成","actual_work_pending","发布清单等于完成","all_complete")],
        "CQ-MIRA-03"=>[new("mode","本人接受的安排",["尚未确认","独立受托保管复核","受控双副件交叉复核"],["","independent_trustee_review","two_controlled_copies_cross_review"]),ChapterFiveRow("access","准入字段","角色数量和来源","role_counts_source_only","全部身份","all_identities"),ChapterFiveRow("correction","纠错","追加并保留旧版","append_keep_original","删除旧证据","delete_original"),ChapterFiveRow("exit","退出范围","未来自愿联系，不销毁合法证据","future_contact_only_not_destroy_evidence","销毁全部合法记录","destroy_evidence"),ChapterFiveRow("main_custody","主原件保管","不改变","unchanged","私人决定覆盖主线","overridden")],
        "CQ-OREN-03"=>[new("mode","实际接受岗位",["尚未确认","地方维护轮值","远程监督解答"],["","rotation_local_maintenance","supervised_remote_answers"]),ChapterFiveRow("responsibility","MC2旧责任","保留旁路签章","MC2_bypass_signature_preserved","道歉销案","cancelled"),ChapterFiveRow("repair","前修复范围","辅助修复和独立复核","separate_auxiliary_independent_review","补造主线新结算","reaward_mainline"),ChapterFiveRow("window","重编队窗口","下一安全交班","next_safe_reassignment","立即暗改编队","immediate_override"),ChapterFiveRow("occupation","岗位占用","不与出战或别的值守重叠","no_battle_or_other_duty_overlap","任意同时占岗","overlap")],
        _=>throw new InvalidOperationException("该提案尚无正式提交权益。")
    };
    private async ValueTask<bool> TryChapterFiveInteraction(string spot)
    {
        if(_chapterFive is null||!spot.StartsWith("R5-",StringComparison.Ordinal))return false;var materials=_chapterFive.Package.Materials.Where(m=>m.Spot==spot).ToArray();var stations=_chapterFive.Package.Stations.Where(s=>s.Spot==spot).ToArray();if(materials.Length==0&&stations.Length==0&&spot!="R5-S01.berth")return false;
        var view=await _chapterFive.ViewAsync();var flow=Panel("第五章现场 · "+spot,InputOwner.Workspace);AddScopedPersonalDetailButton(flow,spot);Label(flow,"原创候选，内容人审待完成。原文读取、实际救援、审查和原件保管分别记录。",18);
        foreach(var material in materials){string id=material.Id;Button(flow,(view.Materials.Any(m=>m.Id==id)?"查看已得材料：":"实际领取/读取：")+material.Title,()=>Execute(async()=>{CheckCommit(await _chapterFive.DiscoverAsync(id));await OpenChapterFiveMaterial(id);}));}
        foreach(var station in stations)Button(flow,station.Label,()=>Execute(async()=>await OpenChapterFiveStation(station.Action)));
        if(spot=="R5-S03.checkpoint_guard")Button(flow,"查看核查护卫独立战斗入口",()=>Execute(async()=>await OpenChapterFiveEncounterEntry("ENC-R5-01")));
        if(spot=="R5-S04.hazard")Button(flow,"查看隧道独立战斗入口",()=>Execute(async()=>await OpenChapterFiveEncounterEntry("ENC-R5-02")));return true;
    }
    private async ValueTask OpenChapterFiveStation(string action)
    {
        if(_chapterFive is null)return;switch(action)
        {
            case "submit_current_route":await OpenChapterFiveWorkspace("Q-501");return;
            case "submit_rescue_instruction":await OpenChapterFiveWorkspace("Q-502");return;
            case "submit_responsibility_summary":await OpenChapterFiveWorkspace("Q-503");return;
            case "submit_joint_handover":await OpenChapterFiveWorkspace("Q-504");return;
            case "submit_mira_access_arrangement":await OpenChapterFiveWorkspace("CQ-MIRA-03");return;
            case "submit_oren_duty_arrangement":await OpenChapterFiveWorkspace("CQ-OREN-03");return;
            case "compare_current_map":OpenChapterFiveMap();return;
            case "request_rescue_scope":OpenChapterFiveScope(false);return;
            case "request_responsibility_scope":OpenChapterFiveScope(true);return;
            case "compare_responsibilities":OpenChapterFiveResponsibilities();return;
            case "choose_review_procedure":OpenChapterFiveReview();return;
            case "record_response_opportunity":OpenChapterFiveResponse();return;
            case "choose_temporary_custody":OpenChapterFiveCustody();return;
            case "accept_joint_supervision":OpenChapterFiveSupervision();return;
            case "configure_archive_load":OpenChapterFiveLoad();return;
            case "decide_mira_review_invitation":OpenChapterFiveInvitation("CQ-MIRA-03");return;
            case "decide_oren_duty_invitation":OpenChapterFiveInvitation("CQ-OREN-03");return;
            case "run_personal_access_exercise":OpenChapterFiveAccessExercise();return;
            case "accept_personal_arrangement":OpenChapterFivePersonalDecision();return;
            case "arrange_oren_duty":OpenChapterFiveOrenDuty();return;
            case "handoff_oren_duty":CheckCommit(await _chapterFive.HandoffOrenDutyAsync());ClosePanel();return;
            case "begin_guard_group_encounter":await OpenChapterFiveEncounterEntry("ENC-R5-03");return;
            case "begin_archive_relay_encounter":await OpenChapterFiveEncounterEntry("ENC-R5-04");return;
            default:CheckCommit(await _chapterFive.ExecuteStationAsync(action));ClosePanel();_status.Text="实际动作已记录，当前检查及提交仍按真实条件核验。";return;
        }
    }
    private async ValueTask OpenChapterFiveQuests()
    {
        if(_chapterFive is null)return;var v=await _chapterFive.ViewAsync();var flow=Panel("第五章工作与实际记录",InputOwner.Workspace);Label(flow,"主线依次核验当前入口、实际救援、责任审查和有限共同交接。私人邀请不阻断主线；固定奖励只结算一次。",20);
        foreach(var q in v.Quests){Label(flow,ChapterFiveQuestName(q.Id)+" · "+q.Lifecycle,22);if(q.Lifecycle==QuestLifecycle.Active){string id=q.Id;var task=_chapterFive.Package.WritingTasks.Single(t=>t.Quest==id);Label(flow,"意思工作台 "+task.Spot+"；正式接收 "+_chapterFive.SubmissionSpot(id),18);Button(flow,"编辑 "+id+" 同一份正文",()=>Execute(async()=>await OpenChapterFiveWriter(id)));Button(flow,"核对 "+id+" 当前提交预览",()=>Execute(async()=>await OpenChapterFiveSubmission(id)));}}
        Label(flow,$"实际路线 {v.State.UsedRoute} · 玛拉直接接触 {v.State.MaraReleased} / 已接收 {v.State.MaraReceived} · 原件已交接 {v.State.OriginalCasePreserved} · 塔配置 {v.State.TowerRevision} / 核验 {v.State.TowerCheckRevision}",20);Label(flow,"导师当前状态只有实体接触才改变。当前归档安全不授六塔永久转换。",18);
        foreach(string id in new[]{"ENC-R5-01","ENC-R5-02","ENC-R5-03","ENC-R5-04"})if(await _chapterFive.EncounterAsync(id) is{} encounter){string key=id;Button(flow,"查看 "+id+" · "+encounter.Battle.Outcome,()=>Execute(async()=>{_chapterFiveSelectedEncounter=key;await OpenBattle();}));}
    }
    private void OpenChapterFiveMap()
    {
        if(_chapterFive is null)return;var flow=Panel("历史层与当前道路",InputOwner.Confirmation);var historical=ChapterFiveChoice(flow,"历史桥线","尚未确认","HIST-CENTRAL-SPAN 已关闭","旧桥仍开放");var route=ChapterFiveChoice(flow,"当前路线","尚未选择","CHECKPOINT-DUTY-PASSAGE","TUNNEL-SERVICE-PASSAGE");var scope=ChapterFiveChoice(flow,"入口止境","尚未确认","值守厅","全部扣押室");ChapterFiveButton(flow,"明确记录当前路线比较",async()=>{CheckCommit(await _chapterFive.CompareMapAsync(new(historical.Selected==1?"HIST-CENTRAL-SPAN":"still_open",ChapterFiveSelected(route,["","CHECKPOINT-DUTY-PASSAGE","TUNNEL-SERVICE-PASSAGE"]),scope.Selected==1?"duty_hall_only":"all_detention",historical.Selected!=1)));});
    }
    private void OpenChapterFiveScope(bool responsibility)
    {
        if(_chapterFive is null)return;var flow=Panel(responsibility?"三种责任限定原件范围":"实体救援限定门控范围",InputOwner.Confirmation);string[] names=responsibility?["design_limits","correction_history","policy_authorization","execution_order","version_source"]:["door_status","watch_arrangement","return_route_status","version_source"],labels=responsibility?["设计限制","纠正历史","政策授权","执行指令","来源版本"]:["门控状态","值守安排","返程路线状态","来源版本"];var fields=ChapterFiveFields(flow,names,labels);Label(flow,"接收：巡灯技术员。身份、住址、医疗档案不在本次用途。",18);ChapterFiveButton(flow,"明确申请本次必要字段",async()=>{var selected=fields.Where(p=>p.Value.ButtonPressed).Select(p=>p.Key).ToImmutableArray();CheckCommit(responsibility?await _chapterFive.RequestResponsibilityScopeAsync(selected,"patrol_technician","distinguish_design_policy_execution_responsibility","OC-responsibility-set@1"):await _chapterFive.RequestRescueScopeAsync(selected,"patrol_technician","rescue_and_safe_return","NORTH-DUTY-D0@1"));});
    }
    private void OpenChapterFiveResponsibilities()
    {
        if(_chapterFive is null)return;var flow=Panel("有版本的三种责任",InputOwner.Confirmation);Label(flow,"请依据已取得原件核对；执行副本同根，不增加独立证人。",20);var versions=ChapterFiveChoice(flow,"来源版本链","尚未确认","设计、修订、纠正、政策、试验、覆写、扣押七版分别保留","用一份口供代替全部原件");var copy=ChapterFiveChoice(flow,"执行副本来源","尚未确认","与原设计同源","新的独立证人");var mara=ChapterFiveChoice(flow,"设计责任","尚未确认","设计缺口与迟纠","全部后来指令");var voss=ChapterFiveChoice(flow,"政策责任","尚未确认","已知风险配额、试验与限制流转","供给理由免除责任");var rook=ChapterFiveChoice(flow,"执行责任","尚未确认","取消复核与扩大扣押","无执行责任");ChapterFiveButton(flow,"明确记录各自来源责任",async()=>{CheckCommit(await _chapterFive.CompareResponsibilitiesAsync(new(versions.Selected==1?"OC-D18.design@1":"testimony","OC-D14.revision@2","OC-D30.correction@1","VQ-D90.policy@1","VT-D7.authorization@1","RK-D6.override@2","RK-D3.detention@1",copy.Selected==1?"same_as_original":"independent",mara.Selected==1?"design_limits_and_delayed_correction":"all_later",voss.Selected==1?"known_risk_quota_test_and_suppression":"none",rook.Selected==1?"cancelled_review_and_expanded_detention":"none")));});
    }
    private void OpenChapterFiveReview()
    {
        if(_chapterFive is null)return;var flow=Panel("有限审查程序",InputOwner.Confirmation);var review=ChapterFiveChoice(flow,"审查程序","尚未选择","紧急脱敏公开审查","受保护审查");Label(flow,"两种办法都须逐方记录反证机会，不公开无关身份。",20);ChapterFiveButton(flow,"明确选择本次审查程序",async()=>CheckCommit(await _chapterFive.ChooseReviewAsync(ChapterFiveSelected(review,["","emergency_public_redacted","protected_review"]))));
    }
    private void OpenChapterFiveResponse()
    {
        if(_chapterFive is null)return;var flow=Panel("实际反证渠道",InputOwner.Confirmation);var party=ChapterFiveChoice(flow,"反证当事方","尚未选择","玛拉现场","沃斯限定执行审阅渠道","鲁克站务渠道");Label(flow,"逐方记录答复及范围。保护理由不取消原件责任；沉默不作证明。",20);ChapterFiveButton(flow,"实际记录本方反证机会",async()=>CheckCommit(await _chapterFive.RecordResponseOpportunityAsync(ChapterFiveSelected(party,["","N-MARA","N-VOSS","N-ROOK"]))));
    }
    private void OpenChapterFiveCustody()
    {
        if(_chapterFive is null)return;var flow=Panel("唯一原件临时保管",InputOwner.Confirmation);var custody=ChapterFiveChoice(flow,"原件保管","尚未选择","受限公共档案","共同封存");var publication=ChapterFiveChoice(flow,"脱敏公开","尚未选择","脱敏结论","脱敏结论及技术条件");var role=ChapterFiveChoice(flow,"沃斯有限岗位","尚未选择","受监督技术岗位","暂停岗位");Label(flow,"原件须另行实际交接。两种方案都保留原件与纠正；不授终局政策或无限权力。",20);ChapterFiveButton(flow,"明确选择临时保管安排",async()=>CheckCommit(await _chapterFive.ChooseCustodyAsync(ChapterFiveSelected(custody,["","public_archive_bounded","joint_sealed"]),ChapterFiveSelected(publication,["","redacted_findings","redacted_findings_and_conditions"]),ChapterFiveSelected(role,["","supervised_technical","suspended"]))));
    }
    private void OpenChapterFiveSupervision()
    {
        if(_chapterFive is null)return;var flow=Panel("实际共同监督接受",InputOwner.Confirmation);var person=ChapterFiveChoice(flow,"接受监督岗位","尚未选择","当地操作岗","阿妲住民代表","当地档案保管人");ChapterFiveButton(flow,"实际记录本岗接受",async()=>CheckCommit(await _chapterFive.AcceptSupervisionAsync(ChapterFiveSelected(person,["","ROLE-R5-OPERATOR","N-ADA","ROLE-R5-ARCHIVE-CUSTODIAN"]))));
    }
    private void OpenChapterFiveLoad()
    {
        if(_chapterFive is null)return;var flow=Panel("归档当前班次负荷",InputOwner.Confirmation);var protect=ChapterFiveChoice(flow,"保护负荷","尚未选择","3","2");var preserve=ChapterFiveChoice(flow,"保存负荷","尚未选择","1","0");var reproduce=ChapterFiveChoice(flow,"非必要复制","尚未选择","0","1");Label(flow,"容量4。当前配置改变会使旧测试失效，返程与安全须重新核验。",20);ChapterFiveButton(flow,"实际设置本次归档负荷",async()=>{if(new[]{protect,preserve,reproduce}.Any(x=>x.Selected==0))throw new InvalidOperationException("逐项明确选择负荷。");CheckCommit(await _chapterFive.ConfigureTowerLoadAsync(new(protect.Selected==1?3:2,preserve.Selected==1?1:0,reproduce.Selected==1?0:1)));});
    }
    private void OpenChapterFiveInvitation(string quest)
    {
        if(_chapterFive is null)return;var flow=Panel("第五章可撤回私人邀请 · "+quest,InputOwner.Dialogue);Label(flow,"需真实前一节点和第五章交接，不要求正式同行。撤回阻止以后私人访问及提交，保留合法证据；已经接受的岗位须真实交班。",20);foreach(var pair in new[]{("accept","接受本次私人工作"),("decline","拒绝本次私人工作"),("defer","暂缓本次私人工作"),("withdraw","撤回以后私人访问")}){string decision=pair.Item1;ChapterFiveButton(flow,pair.Item2,async()=>CheckCommit(await _chapterFive.DecidePersonalInvitationAsync(quest,decision)));}
    }
    private void OpenChapterFiveAccessExercise()
    {
        if(_chapterFive is null)return;var flow=Panel("限定副件真实访问演练",InputOwner.Confirmation);var mode=ChapterFiveChoice(flow,"个人副件安排","尚未选择","独立受托保管复核","受控双副件交叉复核");ChapterFiveButton(flow,"实际开始本次限定演练",async()=>CheckCommit(await _chapterFive.BeginPersonalAccessExerciseAsync(ChapterFiveSelected(mode,["","independent_trustee_review","two_controlled_copies_cross_review"]))));foreach(var pair in new[]{("check_role_fields","实际检查必要角色字段"),("test_optional_exit","实际演练退出未来联系"),("append_correction","实际追加纠错保留原版"),("test_restricted_review","实际演练指定接收者限阅")}){string step=pair.Item1;ChapterFiveButton(flow,pair.Item2,async()=>CheckCommit(await _chapterFive.PerformPersonalAccessStepAsync(step)));}
    }
    private void OpenChapterFivePersonalDecision()
    {
        if(_chapterFive is null)return;var flow=Panel("相关本人分别决定",InputOwner.Dialogue);var person=ChapterFiveChoice(flow,"个人安排本人","尚未选择","米拉","W04相关本人");var decision=ChapterFiveChoice(flow,"本人决定","尚未选择","接受","不接受");ChapterFiveButton(flow,"实际记录本人的安排决定",async()=>CheckCommit(await _chapterFive.RecordPersonalArrangementDecisionAsync(ChapterFiveSelected(person,["","CP-MIRA","W-04"]),ChapterFiveSelected(decision,["","accept","decline"]))));
    }
    private void OpenChapterFiveOrenDuty()
    {
        if(_chapterFive is null)return;var flow=Panel("奥伦有限岗位与交班",InputOwner.Confirmation);var mode=ChapterFiveChoice(flow,"维护岗位","尚未选择","地方维护轮值","远程监督解答");Label(flow,"接收 ROLE-R3-LOCAL-MAINTENANCE，监督 ROLE-R3-REMOTE-REVIEW；下一安全交班可重编队。当前出战或别的岗位占用须先处理。",20);ChapterFiveButton(flow,"实际安排本次有限岗位",async()=>CheckCommit(await _chapterFive.ArrangeOrenDutyAsync(ChapterFiveSelected(mode,["","rotation_local_maintenance","supervised_remote_answers"]),"ROLE-R3-LOCAL-MAINTENANCE","ROLE-R3-REMOTE-REVIEW","next_safe_reassignment")));
    }
}

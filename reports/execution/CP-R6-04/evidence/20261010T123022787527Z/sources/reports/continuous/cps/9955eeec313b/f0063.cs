using System.Collections.Immutable;
using Godot;
using Mist.Application.ChapterSix;
using Mist.Application.Drafts;
using Mist.Application.UI;
using Mist.Core.ChapterSix;
using Mist.Core.Engineering;
using Mist.Application.WorkOrders;
using Mist.Application.Storage;
using Mist.Application.Factions;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Quests;

public partial class Root
{
    private static T? RegionalSnapshotRead<T>(Mist.Application.Storage.BranchSnapshot snapshot,string id) where T:class=>snapshot.Entities.SingleOrDefault(e=>e.Id==id) is{} entity?StrictJson.Deserialize<T>(entity.Json):null;
    private string? _chapterSixSelectedEncounter;
    private bool ChapterSixEncounterSelected=>_chapterSix is not null&&_chapterSixSelectedEncounter is not null;
    private readonly Dictionary<string,Stack<Guid>> _chapterSixLaunchPreviews=new();
    private readonly Dictionary<string,DraftEditor> _chapterSixEditors=new();
    private readonly Dictionary<string,Stack<Guid>> _chapterSixUiPreviews=new();
    private TextEdit? _chapterSixWriter;
    private Label? _chapterSixSaveStatus;
    private Godot.Timer? _chapterSixSaveTimer;
    private Task<bool>? _chapterSixSaveTask;
    private static string ChapterSixWriterTitle(string quest)=>"第六章正文 · "+quest;
    private static string ChapterSixQuestName(string quest)=>quest switch{"Q-601"=>"风暴将至 · 当前测站调查","Q-602"=>"为明天留灯 · 六塔实际准备","Q-603"=>"风脊护送 · 实物与第六塔","Q-604"=>"最后起航 · 公开资格与交接","CQ-TESS-01"=>"两处现行接收联系人","CQ-TESS-02"=>"原补充运输记录","CQ-TESS-03"=>"下一班实际收到通知",_=>quest};
    private void TrackChapterSixPreview(string title,Guid token){if(!_chapterSixUiPreviews.TryGetValue(title,out var stack)){stack=new();_chapterSixUiPreviews[title]=stack;}stack.Push(token);}
    private void CloseChapterSixUiPreview(string title)
    {
        if(_chapterSixUiPreviews.TryGetValue(title,out var stack)&&stack.TryPop(out var token))_chapterSix?.ClosePreview(token);
        if(_chapterSixLaunchPreviews.TryGetValue(title,out var launches)&&launches.TryPop(out var launch))_finaleLaunch?.ClosePreview(launch);
        if(title.StartsWith("第六章正文 · ",StringComparison.Ordinal)){_chapterSixWriter=null;_chapterSixSaveStatus=null;_chapterSixSaveTimer=null;_=SaveChapterSixDraft();}
    }
    private void ResetChapterSixUiState()
    {
        foreach(var stack in _chapterSixUiPreviews.Values)foreach(var token in stack)_chapterSix?.ClosePreview(token);
        foreach(var stack in _chapterSixLaunchPreviews.Values)foreach(var token in stack)_finaleLaunch?.ClosePreview(token);_chapterSixLaunchPreviews.Clear();
        _chapterSixUiPreviews.Clear();_chapterSixEditors.Clear();_chapterSixSelectedEncounter=null;_chapterSixWriter=null;_chapterSixSaveStatus=null;_chapterSixSaveTimer=null;_chapterSixSaveTask=null;
    }
    private void ChapterSixButton(VBoxContainer flow,string caption,Func<ValueTask> action,bool close=true)=>Button(flow,caption,()=>Execute(async()=>{await action();if(close)ClosePanel();}));
    private static OptionButton ChapterSixChoice(VBoxContainer flow,string name,params string[] choices)
    {
        var option=new OptionButton{FitToLongestItem=false,AccessibilityName=name};foreach(string choice in choices)option.AddItem(choice);flow.AddChild(option);return option;
    }
    private static string ChapterSixSelected(OptionButton option,string[] values)=>option.Selected>=0&&option.Selected<values.Length?values[option.Selected]:"";
    private async ValueTask OpenChapterSixWriter(string quest)
    {
        if(_chapterSix is null)return;if(!await SaveChapterSixDraft())throw new InvalidOperationException("请先完成输入法候选并保存当前正文。");string title=ChapterSixWriterTitle(quest);
        if(_routing.Contains(title)){while(_routing.Top!=title)ClosePanel();_chapterSixWriter?.GrabFocus();return;}
        var task=_chapterSix.Package.WritingTasks.Single(t=>t.Quest==quest);if(!_chapterSixEditors.TryGetValue(quest,out var editor)){var head=await _store.GetDraftAsync(task.Id);var session=_store.Session;editor=new(_store,new(session.Profile,session.Branch,session.Load,task.Id,head?.Id,head?.Revision??0,head?.Body??"",_chapterSix.Package.Version));_chapterSixEditors.Add(quest,editor);}
        var flow=Panel(title,InputOwner.Editable);Label(flow,"意思确认站位 "+task.Spot+"；正式接收站位 "+(quest=="Q-604"?"R6-S08.qualification；最终确认须到HUB-S02.chart":_chapterSix.SubmissionSpot(quest)),18);Label(flow,task.EnglishPrompt,22);Button(flow,"查看任务中文辅助",()=>Label(flow,task.ChinesePrompt,20));Label(flow,"读、听、写目标各6.5；当前不自动理解英文，不生成能力分数。自由写作从空稿开始，结构化辅助须另行明确选择。",18);
        var writer=new TextEdit{Text=editor.Text,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,330),AccessibilityName=quest+"英文正文"};writer.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(writer);_chapterSixWriter=writer;_chapterSixSaveStatus=Label(flow,editor.IsSaved?"当前正文与保存版本一致。":"当前正文待保存。",18);
        var timer=new Godot.Timer{OneShot=true,WaitTime=.5};flow.AddChild(timer);_chapterSixSaveTimer=timer;timer.Timeout+=()=>{if(Composing()){timer.Start(.2);return;}_=SaveChapterSixDraft();};writer.TextChanged+=()=>{editor.Edit(writer.Text);if(_chapterSixSaveStatus is{} status&&IsInstanceValid(status))status.Text="有未保存修改。";foreach(var stack in _chapterSixUiPreviews.Values)foreach(var token in stack)_chapterSix.ClosePreview(token);_chapterSixUiPreviews.Clear();foreach(var stack in _chapterSixLaunchPreviews.Values)foreach(var token in stack)_finaleLaunch?.ClosePreview(token);_chapterSixLaunchPreviews.Clear();timer.Start();};
        writer.FocusExited+=()=>{foreach(var key in _keys.Values.Append(Key.Enter))if(Input.IsPhysicalKeyPressed(key))_releaseBarrier.Add(key);};
        var back=flow.GetChild<HBoxContainer>(0).GetChild<Button>(1);back.Pressed-=ClosePanel;back.Pressed+=()=>Execute(async()=>{if(!await SaveChapterSixDraft())throw new InvalidOperationException("正文尚未保存，完成输入法候选后重试或导出。");ClosePanel();});
        Button(flow,"保存第六章正文",()=>Execute(async()=>{if(!await SaveChapterSixDraft())throw new InvalidOperationException("正文保存尚未完成。");}));Button(flow,"导出当前第六章正文",()=>{string dir=ProjectSettings.GlobalizePath("user://exports");Directory.CreateDirectory(dir);string path=Path.Combine(dir,quest+"-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+".txt");File.WriteAllText(path,editor.Text,new System.Text.UTF8Encoding(false));_status.Text="当前正文已导出："+path;});Button(flow,"明确核对正文意思",()=>Execute(async()=>await OpenChapterSixMeaning(quest,false)));Button(flow,"明确使用结构化辅助",()=>Execute(async()=>await OpenChapterSixMeaning(quest,true)));Button(flow,"查看当前提交预览",()=>Execute(async()=>await OpenChapterSixSubmission(quest)));writer.GrabFocus();
    }
    private async ValueTask<bool> SaveChapterSixDraft()
    {
        if(_chapterSixEditors.Count==0)return true;if(Composing())return false;if(_chapterSixSaveTask is not null&&!_chapterSixSaveTask.IsCompleted)return await _chapterSixSaveTask;_chapterSixSaveTask=SaveChapterSixDraftCore();return await _chapterSixSaveTask;
    }
    private async Task<bool> SaveChapterSixDraftCore()
    {
        try{foreach(var editor in _chapterSixEditors.Values)if(!editor.IsSaved)await editor.SaveAsync();bool saved=_chapterSixEditors.Values.All(e=>e.IsSaved);if(_chapterSixSaveStatus is{} label&&IsInstanceValid(label))label.Text=saved?"正文已由实际保存回执确认。":"修改尚未全数保存，旧回执不会覆盖新稿。";if(!saved&&_chapterSixSaveTimer is{} timer&&IsInstanceValid(timer))timer.Start(.2);return saved;}
        catch(Exception error){if(_chapterSixSaveStatus is{} label&&IsInstanceValid(label))label.Text="保存失败，正文仍可导出："+error.Message;_status.Text="第六章正文保存失败："+error.Message;return false;}
    }
    private async ValueTask OpenChapterSixArchive()
    {
        if(_chapterSix is null)return;var v=await _chapterSix.ViewAsync();var flow=Panel("第六章已得档案",InputOwner.Workspace);if(v.Materials.Length==0)Label(flow,"尚未在实际现场取得当前获准材料。",22);foreach(var m in v.Materials){string id=m.Id;Button(flow,m.Title,()=>Execute(async()=>await OpenChapterSixMaterial(id)));}Button(flow,"工作与实际记录",()=>Execute(OpenChapterSixQuests));
    }
    private async ValueTask OpenChapterSixMaterial(string id)
    {
        if(_chapterSix is null)return;var m=await _chapterSix.OpenMaterialAsync(id);var flow=Panel("第六章材料 · "+m.Title,InputOwner.Workspace);Label(flow,m.Scene+" · "+m.Version+" · 原创未人审",18);var reader=new TextEdit{Text=m.English,Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,350),AccessibilityName=m.Title+"英文原文"};reader.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(reader);AddRegionalDictionary(flow,reader);
        Label(flow,m.Kind=="listening_script"?"当前查看书面脚本；候选音轨须另行打开并实际播放，本次读取不记作听录音。":"原文取得与世界核验分别记录；历史责任不随偏好或救援改变。",18);AddRegionalAudioButton(flow,m,()=>_chapterSix.OpenMaterialAsync(id));Button(flow,"查看中文辅助",()=>Label(flow,m.Chinese,20));if(_chapterSix.Package.WritingTasks.Any(t=>t.Quest==m.Quest))Button(flow,"编辑对应的同一份正文",()=>Execute(async()=>await OpenChapterSixWriter(m.Quest)));
    }
    private async ValueTask OpenChapterSixWorkspace(string quest)
    {
        await Task.CompletedTask;var flow=Panel("第六章工作台 · "+ChapterSixQuestName(quest),InputOwner.Workspace);Label(flow,"正文可提前保存；请到写作任务所示工作台确认意思，再到正式接收站位预览交接。",22);Button(flow,"编辑同一份正文",()=>Execute(async()=>await OpenChapterSixWriter(quest)));Button(flow,"明确核对正文意思",()=>Execute(async()=>await OpenChapterSixMeaning(quest,false)));Button(flow,"明确使用结构化辅助",()=>Execute(async()=>await OpenChapterSixMeaning(quest,true)));Button(flow,"查看当前提交预览",()=>Execute(async()=>await OpenChapterSixSubmission(quest)));
    }
    private async ValueTask OpenChapterSixMeaning(string quest,bool assisted)
    {
        if(_chapterSix is null)return;if(!await SaveChapterSixDraft())throw new InvalidOperationException("请先完成输入法候选并保存正文。");var flow=Panel("第六章意思确认 · "+quest,InputOwner.Confirmation);Label(flow,assisted?"这是明确结构化辅助，不计作独立英文理解；仍须核验实际来源、权限和操作结果。":"当前离线模式没有自动理解英文，请逐项明确确认你写下的意思。",22);var rows=ChapterSixMeaningRows(quest);var choices=new List<OptionButton>();foreach(var row in rows){Label(flow,row.Name,20);choices.Add(ChapterSixChoice(flow,row.Name,row.Labels));}
        Button(flow,"明确记录当前第六章提议",()=>Execute(async()=>{if(!await SaveChapterSixDraft())throw new InvalidOperationException("当前正文尚未保存。");var fields=rows.Select((r,i)=>(r.Key,Value:ChapterSixSelected(choices[i],r.Values))).ToImmutableDictionary(x=>x.Key,x=>x.Value);if(fields.Values.Any(string.IsNullOrWhiteSpace))throw new InvalidOperationException("请逐项明确选择，不能沿用未确认项。");CheckCommit(await _chapterSix.ConfirmMeaningAsync(quest,fields,assisted));ClosePanel();_status.Text="当前稿件意思已记录，世界结果尚未提交。";}));
    }
    private sealed record ChapterSixMeaningRow(string Key,string Name,string[] Labels,string[] Values);
    private static ChapterSixMeaningRow[] ChapterSixMeaningRows(string quest)=>quest switch
    {
        "Q-601"=>[
            new("risk","测量支持的风险",["尚未确认","本阶段过载放大","全部雾的唯一起源"],["","current_overload_amplification","all_fog_cause"]),
            new("supply","保留的供給边界",["尚未确认","受限基本供给","无限供给"],["","bounded_essential","unlimited"]),
            new("window","观察窗口",["尚未确认","当前调查阶段","准确风暴倒计时"],["","current_survey_stage","exact_storm_timer"]),
            new("return","实际返程",["尚未确认","已核验内侧道路","假定外侧道路"],["","inspected_inner_route","assumed_outer"]),
            new("causality","全雾因果",["尚未确认","本次尚未证明","已证明全部"],["","all_fog_not_proven","all_proven"])],
        "Q-602"=>[
            new("baseline","六塔基础",["尚未确认","六塔实际完成核验","只看清单即完成"],["","six_actual_verified","requirements_only"]),
            new("enhanced","增强准备",["尚未确认","只按当前实际回执判断","基础即全部增强"],["","current_receipts_not_assumed","baseline_equals_enhanced"]),
            new("private_resources","私人资源",["尚未确认","与工程物资分开","可挪用玩家零件与船电池"],["","separate","diverted"]),
            new("policy","最终政策",["尚未确认","此处未选择","自动选择分布"],["","not_selected","distributed"])],
        "Q-603"=>[
            new("destination","实际接收点",["尚未确认","风脊塔 R6-S07","船上仓库"],["","R6-S07","HUB-S01"]),
            new("components","本次实物",["尚未确认","四件实际基础序号","五箱可选补给"],["","four_actual_base_serials","optional_boxes"]),
            new("escorts","护送人员",["尚未确认","两个已声明地方岗","任意未招募同伴"],["","two_declared_local_posts","unjoined_companions"]),
            new("fallback","备用交接",["尚未确认","上一安全段","继续强行通过"],["","previous_safe_segment","continue_unchecked"]),
            new("enhanced","此次增强资格",["尚未确认","未预支","自动全部增强"],["","not_granted","all_granted"])],
        "Q-604"=>[
            new("policy","最终政策选择",["尚未确认","此处未选择","此处选择分布"],["","not_selected","distributed"]),
            new("risks","承诺与货载",["尚未确认","当前风险已实际交接或暂缓","只忽略旧承诺"],["","current_handed_over","ignored"]),
            new("preparation","明确准备方向",["尚未确认","接受两个基础方向","继续完成增强准备"],["","accept_base_directions","continue_preparation"])],
        "CQ-TESS-01"=>[
            new("shed","棚区接收方",["尚未确认","现行棚区接收岗","任意船员"],["","ROLE-R6-SHED-RECEIVER","N-DECKWATCH"]),
            new("ridge","风脊接收方",["尚未确认","现行风脊接收岗","原棚区岗兼任"],["","ROLE-R6-RIDGE-RECEIVER","ROLE-R6-SHED-RECEIVER"]),
            new("conditions","共同接收条件",["尚未确认","同一当前货单与完整标签","旧口头说明即可"],["","complete_labels_same_current_manifest","old_message"])],
        "CQ-TESS-02"=>[
            new("receipt","引用的可选补给状态",["尚未确认","本次未承诺","两份原运输抵达回执"],["","not_committed","two_original_arrivals"]),
            new("duplicate_supply","再次保留物资",["尚未确认","不二次保留","再次领五箱"],["","not_reserved","reserved_again"]),
            new("parent_reward","父任务奖励",["尚未确认","不重复结算","再发一次"],["","not_repeated","repeat"])],
        "CQ-TESS-03"=>[
            new("first","原联系人",["尚未确认","下一班现行接收岗","任意旧签字者"],["","ROLE-R6-NEXT-WATCH","old_signatory"]),
            new("second","第二联系人",["尚未确认","当前通知备用岗","原联系人兼任"],["","ROLE-R6-NOTICE-BACKUP","ROLE-R6-NEXT-WATCH"]),
            new("record","同版收货记录",["尚未确认","当前实际交付表","旧草表"],["","current_delivery_sheet","old_draft"]),
            new("protocol","通知范围",["尚未确认","只限地方通知","六地区正式协议"],["","local_notice_only","six_region_agreement"])],
        _=>throw new InvalidOperationException("当前写作没有注册世界提交合同。")
    };
    private async ValueTask OpenChapterSixSubmission(string quest)
    {
        if(_chapterSix is null)return;if(quest=="Q-604"){await OpenChapterSixQualification();return;}if(!await SaveChapterSixDraft())throw new InvalidOperationException("正文尚未保存。");var p=await _chapterSix.PreviewSubmissionAsync(quest);string title="第六章提交预览 · "+quest;var flow=Panel(title,InputOwner.Confirmation);Label(flow,p.Ready?"当前来源、实际工作和明确提议可共同提交。关闭取消，正文或世界改变会使旧预览失效。":"尚未满足当前交接条件。",22);foreach(var issue in p.Issues)Label(flow,issue.SafeMessage,20);Label(flow,"预览不发奖、不推进任务；语言评分和人审仍待完成。",18);if(!p.Ready)return;TrackChapterSixPreview(title,p.Token);Button(flow,"明确提交第六章当前结果",()=>Execute(async()=>{if(!await SaveChapterSixDraft())throw new InvalidOperationException("当前正文尚未保存。");CheckCommit(await _chapterSix.ConfirmPreviewAsync(p.Token));CloseAllPanels();_status.Text="当前工作结果已实际提交，固定奖励只结算一次。";}));
    }
    private async ValueTask<bool> TryChapterSixInteraction(string spot)
    {
        if(_chapterSix is null||!spot.StartsWith("R6-",StringComparison.Ordinal))return false;var materials=_chapterSix.Package.Materials.Where(m=>m.Spot==spot).ToArray();var stations=_chapterSix.Package.Stations.Where(s=>s.Spot==spot).ToArray();var tasks=_chapterSix.Package.WritingTasks.Where(t=>t.Spot==spot).ToArray();if(materials.Length+stations.Length+tasks.Length==0)return false;var view=await _chapterSix.ViewAsync();var flow=Panel("风暴边境现场 · "+spot,InputOwner.Workspace);AddTessLocalReceiptBridgeButton(flow,spot);
        foreach(var m in materials){string id=m.Id;Button(flow,(view.Materials.Any(x=>x.Id==id)?"查看已得材料：":"实际领取/读取：")+m.Title,()=>Execute(async()=>{CheckCommit(await _chapterSix.DiscoverAsync(id));await OpenChapterSixMaterial(id);}));}
        foreach(var s in stations){string action=s.Action;Button(flow,s.Label,()=>Execute(async()=>await OpenChapterSixStation(action)));}
        foreach(var t in tasks.Where(t=>view.Quests.Any(q=>q.Id==t.Quest&&q.Lifecycle==QuestLifecycle.Active))){string quest=t.Quest;Button(flow,"打开 "+quest+" 正文工作台",()=>Execute(async()=>await OpenChapterSixWorkspace(quest)));}
        if(spot=="R6-S07.delivery")Button(flow,"第六塔实际工程工作台",()=>Execute(async()=>await OpenChapterSixFacility("R6")));return true;
    }
    private async ValueTask OpenChapterSixStation(string action)
    {
        if(_chapterSix is null)return;switch(action)
        {
            case "choose_corridor":OpenChapterSixCorridor();return;
            case "configure_survey":OpenChapterSixSurvey();return;
            case "plan_cargo_segment":await OpenChapterSixCargoPlan();return;
            case "control_field_device":OpenChapterSixFieldDevice();return;
            case "verify_technical_package":OpenChapterSixTechnical();return;
            case "begin_pass_encounter":await OpenChapterSixEncounterEntry("ENC-R6-01");return;
            case "begin_station_encounter":await OpenChapterSixEncounterEntry("ENC-R6-02");return;
            case "begin_first_escort":await OpenChapterSixEncounterEntry("ENC-R6-03");return;
            case "begin_outer_escort":await OpenChapterSixEncounterEntry("ENC-R6-04");return;
            case "begin_platform_encounter":await OpenChapterSixEncounterEntry("ENC-R6-05");return;
            case "submit_survey":await OpenChapterSixWorkspace("Q-601");return;
            case "submit_six_tower_handover":await OpenChapterSixWorkspace("Q-602");return;
            case "submit_ridge_handover":await OpenChapterSixWorkspace("Q-603");return;
            case "submit_tess_contacts":await OpenChapterSixWorkspace("CQ-TESS-01");return;
            case "submit_tess_trip_choices":await OpenChapterSixWorkspace("CQ-TESS-02");return;
            case "submit_tess_notice":await OpenChapterSixWorkspace("CQ-TESS-03");return;
            case "verify_current_risk_register":CheckCommit(await _chapterSix.VerifyRiskRegisterAsync());ClosePanel();return;
            case "confirm_preparation_direction":await OpenChapterSixQualification();return;
            case "seal_pre_final_preparation":OpenChapterSixSeal();return;
            case "decide_tess_invitation":OpenChapterSixTess();return;
            case "confirm_tess_contacts":OpenChapterSixTessContacts();return;
            case "plan_optional_supplement":OpenChapterSixSupplement();return;
            case "deliver_optional_supplement_trip":OpenChapterSixSupplementDelivery();return;
            case "prepare_tess_notice":OpenChapterSixTessNotice();return;
            case "send_tess_notice":await OpenChapterSixTessNoticeSend();return;
            default:CheckCommit(await _chapterSix.PerformAsync(action));ClosePanel();_status.Text="当前真实现场操作已记录，配置改变后须重新检查。";return;
        }
    }
    private async ValueTask OpenChapterSixQuests()
    {
        if(_chapterSix is null)return;var view=await _chapterSix.ViewAsync();var flow=Panel("第六章工作与当前状态",InputOwner.Workspace);foreach(var q in view.Quests){Label(flow,ChapterSixQuestName(q.Id)+" · "+q.Lifecycle+(q.Outcome is{} outcome?" / "+outcome:""),22);if(q.Lifecycle==QuestLifecycle.Active){string id=q.Id;Button(flow,"编辑 "+id+" 同一份正文",()=>Execute(async()=>await OpenChapterSixWriter(id)));Button(flow,"核对 "+id+" 当前交接",()=>Execute(async()=>await OpenChapterSixSubmission(id)));}}
        Label(flow,$"当前调查有效 {view.State.SurveyCurrent} · 实际货组段 {view.State.CargoStage} · 平台当前安全 {view.State.RidgeSafe}",20);Label(flow,"测站闭合后六塔准备与风脊护送并行。第六塔地方收货不等待远地准备；最终起航等待两项实际闭合。",20);foreach(var ready in await _chapterSix.Engineering.ReadinessAsync())Label(flow,$"{ready.Tower} · Safe {ready.Safe} · 基础 {ready.Baseline} · 值守 {ready.Operators} · 增强 {ready.EnhancedVersion??"未核验"} · 过渡 {ready.Transition} · 试验 {ready.Trial} · 地方协议另在公开资格核对",18);Label(flow,"工程须去各塔实际工位核对；任务清单不会搬动角色或替你签名。",18);foreach(string id in new[]{"ENC-R6-01","ENC-R6-02","ENC-R6-03","ENC-R6-04","ENC-R6-05"})if(await _chapterSix.EncounterAsync(id) is{} enc){string key=id;Button(flow,"继续查看 "+id+" · "+enc.Battle.Outcome,()=>Execute(async()=>{_chapterSixSelectedEncounter=key;await OpenBattle();}));}
    }
    private void OpenChapterSixCorridor()
    {
        if(_chapterSix is null)return;var flow=Panel("当前预报与背风路线",InputOwner.Confirmation);Label(flow,"核对当前预报、已有防护与地标，再明确选择。旧天晴记录不能替代本阶段检查。",20);var route=ChapterSixChoice(flow,"本次调查路线","尚未选择","有地标的背风道","低切口","裸露山脊");ChapterSixButton(flow,"明确选择当前调查路线",async()=>{if(route.Selected==0)throw new InvalidOperationException("请选择对应当前预报的道路。");CheckCommit(await _chapterSix.ChooseCorridorAsync(ChapterSixSelected(route,["","marked_leeward","low_cut","exposed_ridge"])));});
    }
    private void OpenChapterSixSurvey()
    {
        if(_chapterSix is null)return;var flow=Panel("测站当前隔离与刻度",InputOwner.Confirmation);var zero=ChapterSixChoice(flow,"零位通道","尚未选择","Z","R");var reference=ChapterSixChoice(flow,"参考通道","尚未选择","R","Z");var z=ChapterSixCount(flow,"实际零位读数",20);var r=ChapterSixCount(flow,"实际参考读数",20);var risk=ChapterSixChoice(flow,"测量范围","尚未确认","本阶段过载放大","所有雾的唯一起源");var window=ChapterSixChoice(flow,"观察时间边界","尚未确认","当前调查阶段","准确风暴钟点");ChapterSixButton(flow,"实际设置所核对的测站配置",async()=>{if(new[]{zero,reference,risk,window}.Any(x=>x.Selected==0))throw new InvalidOperationException("请明确通道、范围与时间边界。");CheckCommit(await _chapterSix.ConfigureSurveyAsync(ChapterSixSelected(zero,["","Z","R"]),(int)z.Value,ChapterSixSelected(reference,["","R","Z"]),(int)r.Value,ChapterSixSelected(risk,["","current_overload_amplification","all_fog_cause"]),ChapterSixSelected(window,["","current_survey_stage","exact_storm_time"])));});
    }
    private SpinBox ChapterSixCount(VBoxContainer flow,string name,int max){Label(flow,name,20);var box=new SpinBox{MinValue=0,MaxValue=max,Value=0,Step=1,AccessibilityName=name};flow.AddChild(box);return box;}
    private async ValueTask OpenChapterSixCargoPlan()
    {
        if(_chapterSix is null)return;var view=await _chapterSix.ViewAsync();var flow=Panel("风脊护送当前四件货组",InputOwner.Confirmation);var cargo=RegionalSnapshotRead<FrontierCargoState>(view.Snapshot,"R6:cargo");if(cargo is null){Label(flow,"当前尚未实际核对这批货单。",22);return;}Label(flow,"逐件核对受保护货组，两个现行护送岗须分别确认。当前货载不得外区旅行，未完成时可到外线托管点实际交接。",20);var serials=new Dictionary<string,CheckBox>();foreach(string id in cargo.ItemIds){var check=new CheckBox{Text=id,AccessibilityName=id};flow.AddChild(check);serials[id]=check;}var driver=new CheckBox{Text="当前运输岗 ROLE-R6-CART-DRIVER",AccessibilityName="当前运输岗"};var watch=new CheckBox{Text="当前货载值守岗 ROLE-R6-CARGO-WATCH",AccessibilityName="当前货载值守岗"};flow.AddChild(driver);flow.AddChild(watch);var destination=ChapterSixChoice(flow,"本次接收目的地","尚未选择","风脊塔 R6-S07","船舱 HUB-S01");var fallback=ChapterSixChoice(flow,"备用交接方案","尚未选择","上一安全段","强行继续");ChapterSixButton(flow,"明确保留当前真实货组与护送段计划",async()=>{if(destination.Selected==0||fallback.Selected==0)throw new InvalidOperationException("请明确接收地与备用方案。");var escorts=new List<string>();if(driver.ButtonPressed)escorts.Add("ROLE-R6-CART-DRIVER");if(watch.ButtonPressed)escorts.Add("ROLE-R6-CARGO-WATCH");CheckCommit(await _chapterSix.PlanCargoAsync(serials.Where(x=>x.Value.ButtonPressed).Select(x=>x.Key).ToArray(),escorts.ToArray(),ChapterSixSelected(destination,["","R6-S07","HUB-S01"]),ChapterSixSelected(fallback,["","previous_safe_segment","continue_unchecked"])));});
    }
    private void OpenChapterSixFieldDevice()
    {
        if(_chapterSix is null)return;var flow=Panel("风脊平台护场器外侧实际操作",InputOwner.Confirmation);Label(flow,"依原设备标签先断输入、固定制动、核零能。主体战斗和压力不代这三个当前外侧步骤。",20);foreach(var pair in new[]{("disconnect_field_input","实际断开护场器输入"),("fix_external_brake","实际固定外侧制动"),("verify_zero_field_energy","实际核验当前零场能")}){string step=pair.Item1;ChapterSixButton(flow,pair.Item2,async()=>CheckCommit(await _chapterSix.ControlFieldDeviceAsync(step)));}
    }
    private async ValueTask OpenChapterSixEncounterEntry(string id)
    {
        if(_chapterSix is null)return;_chapterSixSelectedEncounter=id;var old=await _chapterSix.EncounterAsync(id);if(old is not null&&old.Battle.Outcome!=CombatOutcome.Withdrawn){await OpenBattle();return;}var flow=Panel("边境实际遭遇入口 · "+id,InputOwner.Confirmation);Label(flow,"使用当前装备、当前可援助人员和实际携带补给。难度进场冻结，撤回恢复战前入口；战斗不发击杀经验。",20);var difficulty=ChapterSixChoice(flow,"边境遭遇难度","标准","宽容","挑战");var supplies=new Dictionary<string,CheckBox>();foreach(var item in (await _chapterSix.ViewAsync()).Snapshot.Items.Where(i=>i.OwnerKind=="player"&&i.OwnerId=="hero"&&i.DefinitionId.StartsWith("CON-",StringComparison.Ordinal))){var check=new CheckBox{Text=item.DefinitionId+" · "+item.Id};flow.AddChild(check);supplies[item.Id]=check;}Button(flow,"明确进入边境所选遭遇",()=>Execute(async()=>{CheckCommit(await _chapterSix.BeginEncounterAsync(id,new[]{"standard","relaxed","challenge"}[difficulty.Selected],supplies.Where(x=>x.Value.ButtonPressed).Select(x=>x.Key).ToArray()));CloseAllPanels();await Refresh();await OpenBattle();}));
    }
    private void OpenChapterSixTechnical()
    {
        if(_chapterSix is null)return;var flow=Panel("有限技术包当前核对",InputOwner.Confirmation);Label(flow,"原失败记录保留。当前受限成功支持有限方案，不代表六塔已逐塔安装测试。",20);var controller=ChapterSixChoice(flow,"技术包控制器","尚未确认","C","A");var load=ChapterSixCount(flow,"技术包负荷",100);var contact=ChapterSixChoice(flow,"外网接触范围","尚未确认","受限接触","全部开放");var evidence=ChapterSixChoice(flow,"技术包来源范围","尚未确认","保留失败与当前受限成功","只删除失败记录");ChapterSixButton(flow,"明确核验当前有限技术包",async()=>{if(new[]{controller,contact,evidence}.Any(x=>x.Selected==0))throw new InvalidOperationException("请明确当前方案和证据边界。");CheckCommit(await _chapterSix.VerifyTechnicalPackageAsync(ChapterSixSelected(controller,["","C","A"]),(int)load.Value,ChapterSixSelected(contact,["","limited","open"]),ChapterSixSelected(evidence,["","retained_failures_and_current_bounded_success","delete_failures"])));});
    }
    private async ValueTask<bool> TryChapterSixFacilityInteraction(string spot)
    {
        if(_chapterSix is null||!_view.Snapshot.Entities.Any(e=>e.Id==ChapterSixService.StateId))return false;var f=_chapterSix.Package.Facilities.SingleOrDefault(x=>x.PhysicalSpot==spot);if(f is null||f.Region=="R6")return false;var flow=Panel("当前塔站与六塔实地准备 · "+f.Region,InputOwner.Workspace);Button(flow,"打开当前塔站实际工程工作台",()=>Execute(async()=>await OpenChapterSixFacility(f.Region)));Button(flow,"打开原有地方现场检查",()=>Execute(async()=>
        {
            bool handled=f.Region switch{"R1"=>await TryChapterInteraction(spot),"R2"=>await TryChapterTwoInteraction(spot),"R3"=>await TryChapterThreeInteraction(spot),"R4"=>await TryChapterFourInteraction(spot),"R5"=>await TryChapterFiveInteraction(spot),_=>false};if(!handled)throw new InvalidOperationException("该地方现场入口尚未接入。");
        }));return true;
    }
    private static Dictionary<string,CheckBox> ChapterSixChecks(VBoxContainer flow,IEnumerable<string> ids)
    {var checks=new Dictionary<string,CheckBox>();foreach(string id in ids){var check=new CheckBox{Text=id,AccessibilityName=id};flow.AddChild(check);checks[id]=check;}return checks;}
    private async ValueTask OpenChapterSixFacility(string region)
    {
        if(_chapterSix is null)return;var f=_chapterSix.Package.Facilities.Single(x=>x.Region==region);var snap=await _store.SnapshotAsync();var ready=RegionalSnapshotRead<TowerReadiness>(snap,"readiness:"+f.Tower);var flow=Panel("六塔实地工作台 · "+region,InputOwner.Workspace);Label(flow,f.Tower+" · 实际工位 "+f.PhysicalSpot+" · 合同 "+f.Version,22);Label(flow,"每项操作核对角色实际站位、当前安全来源与地方资格。物资来自独立有限合同，私人零件和船电池不能替代。",20);Label(flow,$"当前 Safe {ready?.Safe==true} · 基础 {ready?.Baseline==true} · 值守 {ready?.Operators==true} · 增强 {ready?.EnhancedVersion??"未核验"} · 试验 {ready?.Trial==true} · 过渡 {ready?.Transition==true} · 地方协议另在公开资格核对",18);
        Button(flow,"核对当前地方合同与基础四件实物",()=>OpenChapterSixFacilityInspection(region));if(region=="R4")ChapterSixButton(flow,"到场重新核验当前学城棱镜与C试验安全",async()=>CheckCommit(await _chapterSix.ReinspectCurrentFacilitySafetyAsync(region)));
        ChapterSixButton(flow,"登记已到场核验的安装班组与两个值守岗",async()=>CheckCommit(await _chapterSix.RegisterFacilityTeamAsync(region)));ChapterSixButton(flow,"登记已核验的当前塔站安全来源",async()=>CheckCommit(await _chapterSix.RegisterCurrentFacilitySafetyAsync(region)));
        foreach(var source in _chapterSix.Engineering.Rules.Sources.Values.Where(s=>s.Region==region)){string id=source.Id;var p=RegionalSnapshotRead<ProcurementProgress>(snap,"procurement:"+id);Label(flow,$"有限来源 {id} · {source.Resource} × {source.Quantity} · {(p?.Delivered==true?"已交付":$"步骤 {p?.Step??0}/{source.RequiredSteps}")}",18);if(p?.Delivered!=true)Button(flow,"核对下一规定实物步骤 · "+id,()=>OpenChapterSixProcurement(id));}
        Button(flow,"预留当前地方工作单",()=>OpenChapterSixReserve(region));foreach(var type in Enum.GetValues<WorkType>()){var work=RegionalSnapshotRead<TowerWork>(snap,"work:"+WorkOrderService.Preparation+":"+f.Tower+":"+type);if(work is null)continue;Label(flow,type+" · "+work.Stage+" · "+work.Step+"/4 · "+work.Configuration,18);if(work.Step<4&&work.Stage!=WorkStage.Cancelled){var actualType=type;int next=work.Step+1;string[] tasks=["transport_reserved_components","check_delivered_components","install_declared_configuration","verify_current_local_work"];ChapterSixButton(flow,"实际执行 "+type+" 第"+next+"步",async()=>CheckCommit(await _chapterSix.ExecuteWorkStepAsync(region,actualType,next,tasks[next-1])));}}
        Button(flow,"申请当前地方受限试验许可",()=>OpenChapterSixLocalTrialPermit(region));Button(flow,"打开当前地方夹具实地测试",()=>OpenChapterSixFieldTrial(region));Button(flow,"核对两个不同职能的正式条款签署",()=>OpenChapterSixProtocol(region));
    }
    private void OpenChapterSixFacilityInspection(string region)
    {
        if(_chapterSix is null)return;var f=_chapterSix.Package.Facilities.Single(x=>x.Region==region);var flow=Panel("实际地方合同与四件实物 · "+region,InputOwner.Confirmation);Label(flow,"核对原合同、安装资质、两岗资格与四件本地有限实物。该核对不能修复未安全的塔站。",20);var version=ChapterSixChoice(flow,"地方工程合同版本","尚未确认",f.Version,"旧合同未复核");var installer=ChapterSixChoice(flow,"当前安装班组","尚未确认",f.Installer,"未获资格班组");var posts=ChapterSixChecks(flow,f.WatchPosts);var ids=_chapterSix.Engineering.Rules.Sources.Values.Where(x=>x.Id.StartsWith("BASE-"+f.Tower+"-",StringComparison.Ordinal)).SelectMany(x=>Enumerable.Range(1,x.Quantity).Select(n=>"R6-finite:"+x.Id+":"+n));var serials=ChapterSixChecks(flow,ids);ChapterSixButton(flow,"明确核验这份地方合同与实际四件序号",async()=>{if(version.Selected==0||installer.Selected==0)throw new InvalidOperationException("请确认合同与安装班组。");CheckCommit(await _chapterSix.InspectFacilityAsync(region,ChapterSixSelected(version,["",f.Version,"stale"]),ChapterSixSelected(installer,["",f.Installer,"unqualified"]),posts.Where(x=>x.Value.ButtonPressed).Select(x=>x.Key).ToArray(),serials.Where(x=>x.Value.ButtonPressed).Select(x=>x.Key).ToArray()));});
    }
    private void OpenChapterSixProcurement(string sourceId)
    {
        if(_chapterSix is null)return;var source=_chapterSix.Engineering.Rules.Sources[sourceId];var flow=Panel("有限物资规定步骤 · "+sourceId,InputOwner.Confirmation);string[] tasks=sourceId.StartsWith("BASE-",StringComparison.Ordinal)?["match_contract_serials","check_and_deliver_components"]:sourceId switch{"SUP-CTRL"=>["inspect_retained_technical_record","match_controller_serials","local_controller_acceptance","deliver_accepted_controllers"],"SUP-COUP"=>["inspect_retained_spare_scope","match_coupler_serials","local_coupler_acceptance","deliver_accepted_couplers"],"SUP-TEST"=>["inspect_fixture_dedication","check_independent_fixture_serials","deliver_reusable_fixtures"],_=>["inspect_transition_stock","check_protected_supply_boundary","deliver_transition_crates"]};Label(flow,"须按来源合同次序核验。最终交付将原有限实物转入实际工程库存；不能再领同一批。",20);var serials=ChapterSixChecks(flow,Enumerable.Range(1,source.Quantity).Select(n=>"R6-finite:"+sourceId+":"+n));for(int n=1;n<=tasks.Length;n++){int step=n;string task=tasks[n-1];ChapterSixButton(flow,"实际核验第"+step+"步 · "+task,async()=>CheckCommit(await _chapterSix.InspectProcurementStepAsync(sourceId,step,task,serials.Where(x=>x.Value.ButtonPressed).Select(x=>x.Key).ToArray())));}
    }
    private void OpenChapterSixReserve(string region)
    {
        if(_chapterSix is null)return;var flow=Panel("地方实际工作单预留 · "+region,InputOwner.Confirmation);Label(flow,"先到場收齐实际工程物资和安装/值守资格。现场容量为一项；夹具须先预留、运输、交付和装夹，再测试。",20);var type=ChapterSixChoice(flow,"本次工作类型","尚未选择","基础核验","增强安装","地方过渡","夹具现场试验");var version=ChapterSixChoice(flow,"本次工作配置","尚未确认",WorkOrderService.Preparation,ChapterSixService.TechnicalVersion);ChapterSixButton(flow,"明确预留所选实际工作单",async()=>{if(type.Selected==0||version.Selected==0)throw new InvalidOperationException("请明确工作类型与配置。");WorkType[] types=[WorkType.BaselineCheck,WorkType.BaselineCheck,WorkType.EnhancedInstall,WorkType.TransitionSupply,WorkType.FieldTrial];CheckCommit(await _chapterSix.ReserveWorkAsync(region,types[type.Selected],ChapterSixSelected(version,["",WorkOrderService.Preparation,ChapterSixService.TechnicalVersion])));});
    }
    private void OpenChapterSixLocalTrialPermit(string region)
    {
        if(_chapterSix is null)return;var f=_chapterSix.Package.Facilities.Single(x=>x.Region==region);var flow=Panel("当前地方试验许可 · "+region,InputOwner.Confirmation);Label(flow,"核对当前增强安装与两个合格值守。最终共同协议不替代这份地方许可。",20);var config=ChapterSixChoice(flow,"地方试验配置","尚未确认",ChapterSixService.TechnicalVersion,"旧配置");var posts=ChapterSixChecks(flow,f.WatchPosts);ChapterSixButton(flow,"明确申请当前版本地方试验许可",async()=>{if(config.Selected==0)throw new InvalidOperationException("请核对当前试验配置。");CheckCommit(await _chapterSix.GrantLocalTrialAsync(region,ChapterSixSelected(config,["",ChapterSixService.TechnicalVersion,"stale"]),posts.Where(x=>x.Value.ButtonPressed).Select(x=>x.Key).ToArray()));});
    }
    private void OpenChapterSixFieldTrial(string region)
    {
        if(_chapterSix is null)return;var flow=Panel("当前地方受限现场测试 · "+region,InputOwner.Confirmation);Label(flow,"夹具须已实际装夹。先核对当前隔离、备用与独立返程，再观察受限地方输出；任何安装变化使旧测试失效。",20);var stage=ChapterSixChoice(flow,"当前测试阶段","尚未选择","核隔离与备用","观察受限地方输出");var mode=ChapterSixChoice(flow,"实际负荷边界","尚未确认","保留地方最低服务","无限增加负荷");var isolation=new CheckBox{Text="当前隔离已核对",AccessibilityName="当前隔离"};var backup=new CheckBox{Text="当前备用已核对",AccessibilityName="当前备用"};var local=new CheckBox{Text="当前独立返程已核对",AccessibilityName="当前独立返程"};flow.AddChild(isolation);flow.AddChild(backup);flow.AddChild(local);ChapterSixButton(flow,"实际执行当前地方测试阶段",async()=>{if(stage.Selected==0||mode.Selected==0)throw new InvalidOperationException("请选择测试阶段与负荷边界。");CheckCommit(await _chapterSix.FieldTrialStepAsync(region,ChapterSixSelected(stage,["","inspect_isolation_backup","verify_bounded_local_output"]),ChapterSixSelected(mode,["","bounded_local_preservation","unlimited"]),isolation.ButtonPressed,backup.ButtonPressed,local.ButtonPressed));});
    }
    private void OpenChapterSixProtocol(string region)
    {
        if(_chapterSix is null)return;var flow=Panel("同版地方正式职能签署 · "+region,InputOwner.Confirmation);Label(flow,"操作方和地方代表是两个不同职能，分别核对当前任职及同版条款。个人好感、战斗援勤和私人回复不能替代。",20);var role=ChapterSixChoice(flow,"本次正式签署职能","尚未选择","地方操作方","地方代表方");var holder=ChapterSixChoice(flow,"当前签署身份","尚未确认","ROLE-"+region+"-OP","ROLE-"+region+"-REP");var scope=ChapterSixChoice(flow,"当前任职范围","尚未确认","operate:"+region,"represent:"+region);var terms=ChapterSixChecks(flow,["bounded_contact","essential_protection","independent_review","mutual_aid"]);ChapterSixButton(flow,"明确核对并签署同版地方条款",async()=>{if(new[]{role,holder,scope}.Any(x=>x.Selected==0))throw new InvalidOperationException("请分别核对职能、身份与任职范围。");CheckCommit(await _chapterSix.SignRegionalProtocolAsync(region,role.Selected==1?MandateRole.Operator:MandateRole.Representative,ChapterSixSelected(holder,["","ROLE-"+region+"-OP","ROLE-"+region+"-REP"]),ChapterSixSelected(scope,["","operate:"+region,"represent:"+region]),terms.Where(x=>x.Value.ButtonPressed).Select(x=>x.Key).ToArray()));});
    }
    private async ValueTask OpenChapterSixQualification()
    {
        if(_chapterSix is null)return;var ready=await _chapterSix.Readiness.ViewAsync();var flow=Panel("公开起航资格与缺项",InputOwner.Workspace);Label(flow,"当前准备不选择最终政策。六塔实际基础齐全后可接受两个基础方向；继续增强另核真实安装、试验、物资和各地区条款。",22);foreach(string missing in ready.DepartureMissing)Label(flow,"当前起航缺项："+missing,18);if(ready.Options is{} options)foreach(var option in options.Options){Label(flow,option.Choice+" · 当前准备 "+option.Prepared,20);foreach(string missing in option.Missing)Label(flow,"  "+missing,18);}var direction=ChapterSixChoice(flow,"明确准备方向","尚未选择","接受两个基础方向","继续增强准备");ChapterSixButton(flow,"明确记录本次准备方向",async()=>{if(direction.Selected==0)throw new InvalidOperationException("请明确本次准备方向。");CheckCommit(await _chapterSix.ConfirmPreparationDirectionAsync(ChapterSixSelected(direction,["","accept_base_directions","continue_preparation"])));});Button(flow,"编辑Q604同一份交接正文",()=>Execute(async()=>await OpenChapterSixWriter("Q-604")));Button(flow,"查看保护档封存步骤",()=>OpenChapterSixSeal());Label(flow,"方向确认与封存须在实际 R6-S08.qualification；风险登记须在 commitments。最后走到本船HUB-S02海图站位明确起航。",18);
    }
    private void OpenChapterSixSeal()
    {
        if(_chapterSix is null)return;var flow=Panel("真实起航前保护档与准备封存",InputOwner.Confirmation);Label(flow,"封存保存实际当前准备和真实保护档，保留学习记录。封存后修改工程须继续起航前保护档。此操作仍未起航；请随后走到本船海图明确确认。",22);ChapterSixButton(flow,"明确保存保护档并封存当前准备",async()=>{if(!await SaveChapterSixDraft())throw new InvalidOperationException("当前交接正文尚未保存。");CheckCommit(await _chapterSix.SealPreparationAsync());});
    }
    private async ValueTask OpenChapterSixLaunchPreview()
    {
        if(_finaleLaunch is null)throw new InvalidOperationException("实际最终起航服务尚未加载。");if(!await SaveChapterSixDraft())throw new InvalidOperationException("当前交接正文尚未保存。");var p=await _finaleLaunch.PreviewAsync();string title="最后起航实际预览";var flow=Panel(title,InputOwner.Confirmation);Label(flow,p.Ready?"本船、当前源泊位、三个空间、保护档、冻结准备、风险与实际海图站位共同成立。确认将一次结算Q604并在最终区安全落点登陆。":"当前实际起航条件缺失。",22);foreach(string missing in p.Missing)Label(flow,missing,18);Label(flow,"关闭取消，不发奖励。此处不选择最终政策。",18);if(!p.Ready)return;if(!_chapterSixLaunchPreviews.TryGetValue(title,out var stack)){stack=new();_chapterSixLaunchPreviews[title]=stack;}stack.Push(p.Token);Button(flow,"明确最后起航并在最终区实际登陆",()=>Execute(async()=>{if(!await SaveChapterSixDraft())throw new InvalidOperationException("正文尚未保存。");if(_finale is null)throw new InvalidOperationException("最终区实际接待服务尚未加载。");CheckCommit(await _finaleLaunch.ConfirmAsync(p.Token));await _finale.InitializeRegionAsync();CloseAllPanels();await _store.CreateCheckpointAsync("automatic");_status.Text="Q604已实际起航结算。当前同船已到最终区，最终政策仍未选择。";}));
    }
    private void OpenChapterSixTess()
    {
        if(_chapterSix is null)return;var flow=Panel("泰丝的当前邀请与正式同行",InputOwner.Dialogue);Label(flow,"本人邀请在Q601实际闭合后；正式同行另作决定，须在Q603完成前。地方个人工作不要求先加入同行，拒绝或暂缓不会关闭基本政策方向。",20);var decision=ChapterSixChoice(flow,"本人地方联系决定","尚未选择","接受本次地方联系","拒绝本次地方联系","暂缓决定");var recruit=new CheckBox{Text="另明确接受正式同行邀请",AccessibilityName="正式同行决定"};flow.AddChild(recruit);ChapterSixButton(flow,"明确记录泰丝当前邀请决定",async()=>{if(decision.Selected==0)throw new InvalidOperationException("请明确本人联系决定。");CheckCommit(await _chapterSix.DecideTessInvitationAsync(ChapterSixSelected(decision,["","accept","decline","defer"]),recruit.ButtonPressed));});
    }
    private void OpenChapterSixTessContacts()
    {
        if(_chapterSix is null)return;var flow=Panel("两处当前实际接收联系人",InputOwner.Confirmation);var shed=ChapterSixChoice(flow,"棚区当前接收岗","尚未确认","ROLE-R6-SHED-RECEIVER","N-DECKWATCH");var ridge=ChapterSixChoice(flow,"风脊当前接收岗","尚未确认","ROLE-R6-RIDGE-RECEIVER","ROLE-R6-SHED-RECEIVER");var conditions=ChapterSixChoice(flow,"共同接收条件","尚未确认","同一当前货单与完整标签","旧口头说明");ChapterSixButton(flow,"明确核对两处现行联系人与条件",async()=>{if(new[]{shed,ridge,conditions}.Any(x=>x.Selected==0))throw new InvalidOperationException("请核对各现行联系人与共同条件。");CheckCommit(await _chapterSix.ConfirmTessContactsAsync(ChapterSixSelected(shed,["","ROLE-R6-SHED-RECEIVER","N-DECKWATCH"]),ChapterSixSelected(ridge,["","ROLE-R6-RIDGE-RECEIVER","ROLE-R6-SHED-RECEIVER"]),ChapterSixSelected(conditions,["","complete_labels_same_current_manifest","old_message"])));});
    }
    private void OpenChapterSixSupplement()
    {
        if(_chapterSix is null)return;var flow=Panel("独立可选补充两趟箱组",InputOwner.Confirmation);Label(flow,"每趟六格，五类整箱不能拆分。工具与安装件须早于照明，存在两种合法安排。拒绝时不保留实物；这批箱组不属于六塔基础或过渡库存。",20);var decision=ChapterSixChoice(flow,"可选运输承诺","尚未选择","明确拒绝","明确承接");var sizes=new Dictionary<string,int>{{"tools",3},{"fittings",1},{"lighting",4},{"wind_panels",2},{"archives",2}};var trips=new Dictionary<string,OptionButton>();foreach(var pair in sizes){Label(flow,pair.Key+" · "+pair.Value+"格",20);trips[pair.Key]=ChapterSixChoice(flow,pair.Key+"趟次","尚未选择","第一趟","第二趟");}ChapterSixButton(flow,"明确保留本次可选运输承诺与箱组",async()=>{if(decision.Selected==0)throw new InvalidOperationException("请明确承接或拒绝。");if(decision.Selected==2&&trips.Values.Any(x=>x.Selected==0))throw new InvalidOperationException("承接时须逐箱确认趟次。");var plan=decision.Selected==1?ImmutableDictionary<string,int>.Empty:trips.ToImmutableDictionary(x=>x.Key,x=>x.Value.Selected);CheckCommit(await _chapterSix.PlanSupplementAsync(decision.Selected==1?"decline":"accept",plan));});
    }
    private void OpenChapterSixSupplementDelivery()
    {
        if(_chapterSix is null)return;var flow=Panel("可选补充原运输段实际执行",InputOwner.Confirmation);Label(flow,"只执行当前声明箱组的下一趟。当前主线安全道路和地方接收已成立后，由已声明地方车夫执行受限委托，产生实物归属和原运输回执。不重发父任务奖励。",20);foreach(int n in new[]{1,2}){int trip=n;ChapterSixButton(flow,"明确执行本可选补充第"+trip+"趟",async()=>CheckCommit(await _chapterSix.DeliverSupplementTripAsync(trip)));}
    }
    private void OpenChapterSixTessNotice()
    {
        if(_chapterSix is null)return;var flow=Panel("下一班同版地方收货通知",InputOwner.Confirmation);var first=ChapterSixChoice(flow,"下一班原联系人","尚未确认","ROLE-R6-NEXT-WATCH","旧任职者");var second=ChapterSixChoice(flow,"地方第二联系人","尚未确认","ROLE-R6-NOTICE-BACKUP","ROLE-R6-NEXT-WATCH");var record=ChapterSixChoice(flow,"通知对应记录","尚未确认","当前实际交付表","旧草表");ChapterSixButton(flow,"明确准备当前同版地方通知",async()=>{if(new[]{first,second,record}.Any(x=>x.Selected==0))throw new InvalidOperationException("请明确同版记录与两个不同联系人。");CheckCommit(await _chapterSix.PrepareTessNoticeAsync(ChapterSixSelected(first,["","ROLE-R6-NEXT-WATCH","old_holder"]),ChapterSixSelected(second,["","ROLE-R6-NOTICE-BACKUP","ROLE-R6-NEXT-WATCH"]),ChapterSixSelected(record,["","current_delivery_sheet","old_draft"])));});
    }
    private async ValueTask OpenChapterSixTessNoticeSend()
    {
        if(_chapterSix is null)return;var revision=(await _chapterSix.ViewAsync()).State.TowerRevision;var flow=Panel("实际发送并核对地方接收确认",InputOwner.Confirmation);Label(flow,"当前实际交付记录版本 "+revision+"。确认收到与准备草稿分开；配置变化会拒绝旧通知。此通知不是六地区协议签署。",20);ChapterSixButton(flow,"明确发送当前通知并记录收到确认",async()=>CheckCommit(await _chapterSix.SendTessNoticeAsync(revision)));
    }
}

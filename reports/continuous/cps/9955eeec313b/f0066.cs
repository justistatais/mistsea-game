using System.Collections.Immutable;
using Godot;
using Mist.Application.Drafts;
using Mist.Application.Finale;
using Mist.Application.UI;
using Mist.Core.Combat;
using Mist.Core.Contracts;
using Mist.Core.Finale;
using Mist.Core.Quests;
using Mist.Core.ReleaseReadiness;

public partial class Root
{
    private string? _finaleSelectedEncounter;
    private bool FinaleEncounterSelected=>_finale is not null&&_finaleSelectedEncounter is not null;
    private readonly Dictionary<string,DraftEditor> _finaleEditors=new();
    private readonly Dictionary<string,Stack<Guid>> _finaleUiPreviews=new();
    private TextEdit? _finaleWriter;
    private Label? _finaleSaveStatus;
    private Godot.Timer? _finaleSaveTimer;
    private Task<bool>? _finaleSaveTask;
    private static string FinaleWriterTitle(string quest)=>"终章正文 · "+quest;
    private static string FinaleQuestName(string q)=>q switch{"Q-F01"=>"实际入场与边界消息","Q-F02"=>"局部指挥交接摘要","Q-F03"=>"同一尝试的安全停机","Q-F04"=>"当前可执行最终指令","Q-F05"=>"已提交快照的尾声",_=>q};
    private void TrackFinalePreview(string title,Guid token){if(!_finaleUiPreviews.TryGetValue(title,out var stack)){stack=new();_finaleUiPreviews[title]=stack;}stack.Push(token);}
    private void CloseFinaleUiPreview(string title)
    {
        if(_finaleUiPreviews.TryGetValue(title,out var stack)&&stack.TryPop(out var token))_finale?.ClosePreview(token);
        if(title.StartsWith("终章正文 · ",StringComparison.Ordinal)){_finaleWriter=null;_finaleSaveStatus=null;_finaleSaveTimer=null;_=SaveFinaleDraft();}
    }
    private void ResetFinaleUiState()
    {foreach(var stack in _finaleUiPreviews.Values)foreach(var token in stack)_finale?.ClosePreview(token);_finaleUiPreviews.Clear();_finaleEditors.Clear();_finaleSelectedEncounter=null;_finaleWriter=null;_finaleSaveStatus=null;_finaleSaveTimer=null;_finaleSaveTask=null;}
    private void FinaleButton(VBoxContainer flow,string label,Func<ValueTask> action,bool close=true)=>Button(flow,label,()=>Execute(async()=>{await action();if(close)ClosePanel();}));
    private static OptionButton FinaleChoice(VBoxContainer flow,string name,params string[] choices)
    {var option=new OptionButton{FitToLongestItem=false,AccessibilityName=name};foreach(string text in choices)option.AddItem(text);flow.AddChild(option);return option;}
    private async ValueTask OpenFinaleWriter(string quest)
    {
        if(_finale is null)return;if(!await SaveFinaleDraft())throw new InvalidOperationException("请完成输入法候选并保存当前正文。");string title=FinaleWriterTitle(quest);if(_routing.Contains(title)){while(_routing.Top!=title)ClosePanel();_finaleWriter?.GrabFocus();return;}
        var task=_finale.Package.WritingTasks.Single(t=>t.Quest==quest);if(!_finaleEditors.TryGetValue(quest,out var editor)){var head=await _store.GetDraftAsync(task.Id);var s=_store.Session;editor=new(_store,new(s.Profile,s.Branch,s.Load,task.Id,head?.Id,head?.Revision??0,head?.Body??"",_finale.Package.Version));_finaleEditors.Add(quest,editor);}
        var flow=Panel(title,InputOwner.Editable);Label(flow,task.EnglishPrompt,22);Button(flow,"查看任务中文辅助",()=>Label(flow,task.ChinesePrompt,20));Label(flow,"自由短稿从空白开始。英文写作与明确意思分别记录；不自动推断英文理解，不生成语言能力分数。正式交接站位："+FinaleService.SubmissionSpot(quest),18);
        var writer=new TextEdit{Text=editor.Text,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,330),AccessibilityName=quest+"英文正文"};writer.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(writer);_finaleWriter=writer;_finaleSaveStatus=Label(flow,editor.IsSaved?"当前正文与保存版本一致。":"当前正文待保存。",18);
        var timer=new Godot.Timer{OneShot=true,WaitTime=.5};flow.AddChild(timer);_finaleSaveTimer=timer;timer.Timeout+=()=>{if(Composing()){timer.Start(.2);return;}_=SaveFinaleDraft();};writer.TextChanged+=()=>{editor.Edit(writer.Text);if(_finaleSaveStatus is{} status&&IsInstanceValid(status))status.Text="有未保存修改。";foreach(var stack in _finaleUiPreviews.Values)foreach(var token in stack)_finale.ClosePreview(token);_finaleUiPreviews.Clear();timer.Start();};writer.FocusExited+=()=>{foreach(var key in _keys.Values.Append(Key.Enter))if(Input.IsPhysicalKeyPressed(key))_releaseBarrier.Add(key);};
        var back=flow.GetChild<HBoxContainer>(0).GetChild<Button>(1);back.Pressed-=ClosePanel;back.Pressed+=()=>Execute(async()=>{if(!await SaveFinaleDraft())throw new InvalidOperationException("当前正文尚未保存；可导出后重试。");ClosePanel();});
        Button(flow,"保存终章正文",()=>Execute(async()=>{if(!await SaveFinaleDraft())throw new InvalidOperationException("正文保存尚未完成。");}));Button(flow,"导出当前终章正文",()=>{string dir=ProjectSettings.GlobalizePath("user://exports");Directory.CreateDirectory(dir);string path=Path.Combine(dir,quest+"-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+".txt");File.WriteAllText(path,editor.Text,new System.Text.UTF8Encoding(false));_status.Text="当前正文已导出："+path;});
        Button(flow,"明确核对正文意思",()=>Execute(async()=>await OpenFinaleMeaning(quest,false)));Button(flow,"明确使用结构化辅助",()=>Execute(async()=>await OpenFinaleMeaning(quest,true)));Button(flow,"查看当前提交预览",()=>Execute(async()=>await OpenFinaleSubmission(quest)));writer.GrabFocus();
    }
    private async ValueTask<bool> SaveFinaleDraft()
    {if(_finaleEditors.Count==0)return true;if(Composing())return false;if(_finaleSaveTask is not null&&!_finaleSaveTask.IsCompleted)return await _finaleSaveTask;_finaleSaveTask=SaveFinaleDraftCore();return await _finaleSaveTask;}
    private async Task<bool> SaveFinaleDraftCore()
    {
        try{foreach(var editor in _finaleEditors.Values)if(!editor.IsSaved)await editor.SaveAsync();bool saved=_finaleEditors.Values.All(e=>e.IsSaved);if(_finaleSaveStatus is{} label&&IsInstanceValid(label))label.Text=saved?"正文已由实际保存回执确认。":"修改尚未全部保存，旧回执不会覆盖新稿。";if(!saved&&_finaleSaveTimer is{} timer&&IsInstanceValid(timer))timer.Start(.2);return saved;}
        catch(Exception error){if(_finaleSaveStatus is{} label&&IsInstanceValid(label))label.Text="保存失败，正文仍可导出："+error.Message;_status.Text="终章正文保存失败："+error.Message;return false;}
    }
    private async ValueTask<bool> TryFinaleInteraction(string spot)
    {
        if(_finale is null||!spot.StartsWith("RF-",StringComparison.Ordinal))return false;var materials=_finale.Package.Materials.Where(m=>m.Spot==spot).ToArray();var stations=_finale.Package.Stations.Where(s=>s.Spot==spot).ToArray();var writes=_finale.Package.WritingTasks.Where(t=>t.Spot==spot||FinaleService.SubmissionSpot(t.Quest)==spot).ToArray();if(materials.Length==0&&stations.Length==0&&writes.Length==0&&spot!="RF-S01.berth")return false;
        var v=await _finale.ViewAsync();var flow=Panel("终章现场 · "+spot,InputOwner.Workspace);Label(flow,"原创灰盒候选 · 内容人审待完成。实际进入、局部指挥交接、安全停机与政策提交分别核验。",18);
        foreach(var m in materials){string id=m.Id;Button(flow,(v.Materials.Any(x=>x.Id==id)?"查看已得材料：":"实际领取/读取：")+m.Title,()=>Execute(async()=>{CheckCommit(await _finale.DiscoverAsync(id));await OpenFinaleMaterial(id);}));}
        foreach(var station in stations){string action=station.Action;Button(flow,station.Label,()=>Execute(async()=>await OpenFinaleStation(action)));}
        foreach(var task in writes){string q=task.Quest;Button(flow,"打开短稿工作台 · "+q,()=>Execute(async()=>await OpenFinaleWorkspace(q)));}
        if(spot=="RF-S03.coil")Button(flow,"查看线圈哨卫独立遭遇",()=>Execute(async()=>await OpenFinaleEncounterEntry("ENC-RF-01")));if(spot=="RF-S01.berth")Label(flow,"本次抵达来源是原船真实起航与原封存准备。材料和进度须在各自现场取得。",20);return true;
    }
    private async ValueTask OpenFinaleStation(string action)
    {
        if(_finale is null)return;switch(action)
        {
            case "formal_handover":OpenFinaleHandover();return;
            case "configure_layer_isolation":OpenFinaleLayer();return;
            case "begin_rook":await OpenFinaleEncounterEntry("ENC-RF-02");return;
            case "begin_relay":await OpenFinaleEncounterEntry("ENC-RF-03");return;
            case "begin_core":await OpenFinaleEncounterEntry("ENC-RF-04");return;
            case "verify_final_control":CheckCommit(await _finale.VerifyFinalControlAsync());await OpenFinalePolicy(false);return;
            case "present_epilogue":await OpenFinaleEpilogue();return;
            case "safe_preparation":CheckCommit(await _finale.ExecuteStationAsync(action));ClosePanel();_status.Text="独立遭遇边界已核验；可在当前整备位调整构筑，不改变正在机组尝试内的资源。";return;
            default:CheckCommit(await _finale.ExecuteStationAsync(action));ClosePanel();_status.Text="实际终章操作已记录，稿件和政策须各自明确交接。";return;
        }
    }
    private void OpenFinaleHandover()
    {
        if(_finale is null)return;var flow=Panel("终章入场范围交接",InputOwner.Confirmation);Label(flow,"仅接受本次入场与有边界的干预。保留检修路线不会删除本地保全、地方值守或历史责任；入场不确认中央机组已稳定。",22);FinaleButton(flow,"明确接受本次正式入场交接",async()=>CheckCommit(await _finale.FormalHandoverAsync(true)));FinaleButton(flow,"明确保留基础检修入口",async()=>CheckCommit(await _finale.FormalHandoverAsync(false)));
    }
    private void OpenFinaleLayer()
    {
        if(_finale is null)return;var flow=Panel("当前继电层具体隔离对象",InputOwner.Confirmation);Label(flow,"只操作本层 ISO-L/R。这个动作不代替机组内部续启继电器，不关闭所有城市供给。改变后须重新核验当前配置。",22);var selector=FinaleChoice(flow,"本层具体对象","尚未选择","ISO-L","ISO-R");var position=FinaleChoice(flow,"本层隔离位置","尚未选择","隔离","重接");FinaleButton(flow,"实际改变本层所选隔离器",async()=>{if(selector.Selected==0||position.Selected==0)throw new InvalidOperationException("请明确选择具体对象和位置。");CheckCommit(await _finale.ConfigureLayerIsolationAsync(selector.Selected==1?"ISO-L":"ISO-R",position.Selected==1));});
    }
    private async ValueTask OpenFinaleArchive()
    {
        if(_finale is null)return;var v=await _finale.ViewAsync();var flow=Panel("终章已得档案",InputOwner.Workspace);Label(flow,"这里只显示实际取得且当前获准的只读玩家材料。书面脚本读取和候选音轨实际播放分别记录。",20);foreach(var m in v.Materials){string id=m.Id;Button(flow,m.Title,()=>Execute(async()=>await OpenFinaleMaterial(id)));}if(v.Materials.Length==0)Label(flow,"尚无终章现场材料；请到对应站位读取。",20);
    }
    private async ValueTask OpenFinaleMaterial(string id)
    {
        if(_finale is null)return;var m=await _finale.OpenMaterialAsync(id);var flow=Panel("终章材料 · "+m.Title,InputOwner.Workspace);Label(flow,m.Scene+" · "+m.Version+" · 原创未人审",18);var reader=new TextEdit{Text=m.English,Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,350),AccessibilityName=m.Title+"英文原文"};reader.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(reader);AddRegionalDictionary(flow,reader);Label(flow,m.Kind=="listening_script"?"当前查看书面脚本；候选音轨须另行打开并实际播放，本次读取不记作听录音。":"局部事实与历史记录保留原有范围；新的动作不会回写已有失败或免除历史责任。",18);AddRegionalAudioButton(flow,m,()=>_finale.OpenMaterialAsync(id));Button(flow,"查看中文辅助",()=>Label(flow,m.Chinese,20));if(_finale.Package.WritingTasks.Any(t=>t.Quest==m.Quest))Button(flow,"编辑对应的同一份正文",()=>Execute(async()=>await OpenFinaleWriter(m.Quest)));
    }
    private async ValueTask OpenFinaleQuests()
    {
        if(_finale is null)return;var v=await _finale.ViewAsync();var flow=Panel("终章工作与当前记录",InputOwner.Workspace);Label(flow,"两条实际入口可择一完成；之后解除局部指挥、停机和最终方向分别交接。推荐等级不作为额外硬门，同伴招募和语言分数不决定公共方案资格。",20);foreach(var q in v.Quests){Label(flow,FinaleQuestName(q.Id)+" · "+q.Lifecycle,22);if(q.Lifecycle==QuestLifecycle.Active&&q.Id!="Q-F05"){string id=q.Id;Button(flow,"编辑 "+id+" 同一份正文",()=>Execute(async()=>await OpenFinaleWriter(id)));Button(flow,"核对 "+id+" 当前提交预览",()=>Execute(async()=>await OpenFinaleSubmission(id)));}}
        Label(flow,$"原检查点登记 {v.State.CheckpointRegistered} · 实际入口 {v.State.ActualApproach??"尚未抵达"}\n鲁克看管 {v.State.RookCustody} / 指挥解除 {v.State.CommandChainReleased} / 独立状态核查 {v.State.RunningLoopChecked}\n当前本层版本 {v.State.LayerRevision} · 已核版本 {v.State.LayerCheckRevision?.ToString()??"尚未核验"}",20);foreach(string id in new[]{"ENC-RF-01","ENC-RF-02","ENC-RF-03","ENC-RF-04"}){var encounter=await _finale.EncounterAsync(id);if(encounter is not null)Label(flow,$"{id} · {encounter.Battle.Outcome} · 同一尝试 {encounter.Battle.Attempt} · 阶段 {encounter.Battle.Finale?.Phase.ToString()??"独立领域"}",18);}
    }
    private async ValueTask OpenFinaleWorkspace(string quest)
    {await Task.CompletedTask;var flow=Panel("终章工作台 · "+FinaleQuestName(quest),InputOwner.Workspace);Label(flow,"自由短稿可提前保存。实际来源、当前设备配置和明确意思在正式站位重新核对。",22);Button(flow,"编辑同一份正文",()=>Execute(async()=>await OpenFinaleWriter(quest)));Button(flow,"明确核对正文意思",()=>Execute(async()=>await OpenFinaleMeaning(quest,false)));Button(flow,"明确使用结构化辅助",()=>Execute(async()=>await OpenFinaleMeaning(quest,true)));Button(flow,"查看当前提交预览",()=>Execute(async()=>await OpenFinaleSubmission(quest)));}
    private sealed record FinaleMeaningRow(string Key,string Name,string[] Labels,string[] Values);
    private static FinaleMeaningRow FinaleRow(string key,string name,string first,string firstValue,string second,string secondValue)=>new(key,name,["尚未确认",first,second],["",firstValue,secondValue]);
    private static FinaleMeaningRow[] FinaleMeaningRows(string quest)=>quest switch
    {
        "Q-F01"=>[new("route","实际入口",["尚未确认","正式交接走廊","基础检修竖井"],["","formal","maintenance"]),FinaleRow("scope","本次进入的证明范围","进入，不证明机组安全","entry_only_not_core_safe","已证明所有机组安全","all_safe")],
        "Q-F02"=>[FinaleRow("command_scope","两个工作点的范围","本地作战指挥两点","local_combat_two_points","全部城市停机","global_shutdown"),FinaleRow("rook_state","鲁克当前处置","存活并由值守看管","alive_in_custody","已死或已获免责","dead_or_absolved"),FinaleRow("machine_state","机组当前核查","仍运行，须独立处置","running_requires_separate_work","指挥制止已完成停机","safe_by_rook"),FinaleRow("history","已记录历史责任","保留原记录","retained","由本次胜利删除","deleted")],
        "Q-F03"=>[FinaleRow("isolation","当前隔离范围","当前本层，非全城停机","current_layer_not_global_shutdown","所有塔保护归零","all_protection_off"),FinaleRow("core","机组实际完成条件","受控停止，自动续启禁用","safe_controlled_stop_restart_disabled","仅打掉进度即可","hp_only"),FinaleRow("resources","三段资源与尝试","同一尝试，不按段回满","one_attempt_no_phase_refill","每段重新补给回满","phase_refill"),FinaleRow("policy","最终方向状态","尚未选择","not_selected","已自动选择最优结局","automatically_selected")],
        _=>throw new InvalidOperationException("最终政策另有明确选择；尾声无强制作文。")
    };
    private async ValueTask OpenFinaleMeaning(string quest,bool structured)
    {
        if(_finale is null)return;if(!await SaveFinaleDraft())throw new InvalidOperationException("当前正文尚未保存。");if(quest=="Q-F04"){await OpenFinalePolicy(structured);return;}var flow=Panel("终章明确意思 · "+quest,InputOwner.Confirmation);Label(flow,structured?"本次明确使用结构化辅助，不声称自动理解自由英文。逐项选择你要表达的实际意思。":"本次手动核对已保存正文意思；不会自动理解英文或给出能力分数。",22);var rows=FinaleMeaningRows(quest);var choices=rows.Select(r=>FinaleChoice(flow,r.Name,r.Labels)).ToArray();FinaleButton(flow,"明确记录当前终章提议",async()=>{if(choices.Any(c=>c.Selected==0))throw new InvalidOperationException("仍有尚未确认字段，请逐项核对。");var fields=rows.Select((r,i)=>(r.Key,Value:r.Values[choices[i].Selected])).ToImmutableDictionary(x=>x.Key,x=>x.Value);CheckCommit(await _finale.ConfirmMeaningAsync(quest,fields,structured));});
    }
    private async ValueTask OpenFinaleSubmission(string quest)
    {
        if(_finale is null)return;if(!await SaveFinaleDraft())throw new InvalidOperationException("当前正文尚未保存。");if(quest=="Q-F04"){await OpenFinalePolicyPreview();return;}var preview=await _finale.PreviewSubmissionAsync(quest);string title="终章提交预览 · "+quest;var flow=Panel(title,InputOwner.Confirmation);Label(flow,preview.Ready?"当前来源、站位、实际操作和当前正文版本可提交。预览没有改变世界。":"当前提交条件尚未成立。",22);foreach(var issue in preview.Issues)Label(flow,issue.SafeMessage,20);Label(flow,"语言评估：未评。明确确认仅交接这次实际世界工作，不能成为英语理解或人工语言评分。",18);if(preview.Ready){TrackFinalePreview(title,preview.Token);FinaleButton(flow,"明确提交终章当前结果",async()=>{CheckCommit(await _finale.ConfirmPreviewAsync(preview.Token));_status.Text="终章实际成果已一次交接；原历史和独立领域保持各自范围。";});}
    }
    private async ValueTask OpenFinalePolicy(bool structured)
    {
        if(_finale is null)return;var ready=await _finale.FinalReadiness.ViewAsync();var flow=Panel("最终方向明确确认",InputOwner.Confirmation);Label(flow,"有限中央协调保留跨区互助但继续监督；解除中央强制调度保留本地保护并承担救援、贸易成本；分布切换须有实际增强设备、过渡供给、地方签署和当前试运行。未满足的方案仍显示缺项。",22);if(ready.Options is{} options)foreach(var option in options.Options)Label(flow,option.Choice+" · "+(option.Executable?"当前可执行":"当前缺项："+string.Join("、",option.Missing)),18);var choice=FinaleChoice(flow,"最终明确方向","尚未选择","保留有限中央协调","解除中央强制调度与主要联络","执行已准备分布切换");Label(flow,structured?"本次明确结构化辅助；自由英文理解仍未评。":"手动核对已保存最终短指令；英文不自动转为政策选择。",18);FinaleButton(flow,"明确记录最终方向",async()=>{if(choice.Selected==0)throw new InvalidOperationException("请明确选择最终方向。");CheckCommit(await _finale.ConfirmPolicyAsync(new[]{EndingChoice.Central,EndingChoice.Central,EndingChoice.Island,EndingChoice.Distributed}[choice.Selected],structured));});
    }
    private async ValueTask OpenFinalePolicyPreview()
    {
        if(_finale is null)return;var preview=await _finale.PreviewPolicyAsync();string title="最终指令执行预览";var flow=Panel(title,InputOwner.Confirmation);Label(flow,preview.Ready?"当前封存准备、原检查点、当前安全停机、正式控制台、明确政策和正文版本可执行。":"最终方向尚不可执行。",22);Label(flow,"方向："+(preview.Choice?.ToString()??"尚未确认"),20);foreach(string missing in preview.Missing)Label(flow,missing,18);Label(flow,"预览与关闭不发奖励、不改变保护回路。真正提交只形成一个结局快照，保留自然雾、责任与可复核历史。",18);if(preview.Ready){TrackFinalePreview(title,preview.Token);FinaleButton(flow,"明确执行当前最终指令",async()=>{CheckCommit(await _finale.ConfirmPreviewAsync(preview.Token));_status.Text="当前最终指令已一次提交。尾声来自该真实快照。";});}
    }
    private async ValueTask OpenFinaleEpilogue()
    {
        if(_finale is null)return;var epilogue=await _finale.PresentEpilogueAsync();var flow=Panel("已提交终局的尾声",InputOwner.Workspace);Label(flow,epilogue.EndingId+" · "+epilogue.SnapshotId+" · 原创未人审",18);Label(flow,epilogue.Immediate.English,22);Button(flow,"查看即刻结果中文辅助",()=>Label(flow,epilogue.Immediate.Chinese,20));foreach(var region in epilogue.Regions){Label(flow,region.Region+" · "+region.Stage+"\n"+region.Text.English,20);Button(flow,"查看 "+region.Region+" 中文辅助",()=>Label(flow,region.Text.Chinese,20));}foreach(var note in epilogue.Companions){Label(flow,note.Companion+" · 实际同行 "+note.ActuallyJoined+" / 个人链完成 "+note.PrivateArcCompleted+"\n"+note.Text.English,20);Button(flow,"查看 "+note.Companion+" 中文辅助",()=>Label(flow,note.Text.Chinese,20));}Label(flow,epilogue.Archive.English,20);Button(flow,"查看档案结语中文辅助",()=>Label(flow,epilogue.Archive.Chinese,20));Label(flow,"这份尾声使用结局快照保留的来源。没有地方公开选择来源的细节保持未知；未同行或未完成私人链不会补写和解与私信。语言、人审及声音权利仍待审核。",18);FinaleButton(flow,"明确记录已查看本次尾声",async()=>CheckCommit(await _finale.AcknowledgeEpilogueAsync(false)));FinaleButton(flow,"明确跳过表现并保留结局快照",async()=>CheckCommit(await _finale.AcknowledgeEpilogueAsync(true)));
    }
    private async ValueTask OpenFinaleEncounterEntry(string id)
    {
        if(_finale is null)return;_finaleSelectedEncounter=id;var old=await _finale.EncounterAsync(id);if(old is not null&&old.Battle.Outcome!=CombatOutcome.Withdrawn){await OpenBattle();return;}var flow=Panel("终章实际遭遇入口 · "+id,InputOwner.Confirmation);Label(flow,"使用真实当前构筑、装备、援助、已携补给与共享战斗规则。机组三段同一实例与尝试，不按段恢复HP、专注、冷却或共享配发；下一轮继电部件由真实队列加入。",22);var difficulty=FinaleChoice(flow,"终章遭遇难度","标准","宽容","挑战");var view=await _finale.ViewAsync();var supplies=new Dictionary<string,CheckBox>();foreach(var item in view.Snapshot.Items.Where(i=>i.OwnerKind=="player"&&i.OwnerId=="hero"&&i.DefinitionId.StartsWith("CON-",StringComparison.Ordinal))){var box=new CheckBox{Text=item.DefinitionId+" · "+item.Id};flow.AddChild(box);supplies.Add(item.Id,box);}Label(flow,"最多两份实际物品。这个领域的制止没有击杀经验，不替你选择最终政策。候选数值仍待人工试玩。",18);Button(flow,"明确进入终章所选遭遇",()=>Execute(async()=>{CheckCommit(await _finale.BeginEncounterAsync(id,new[]{"standard","relaxed","challenge"}[difficulty.Selected],supplies.Where(x=>x.Value.ButtonPressed).Select(x=>x.Key).ToArray()));CloseAllPanels();await Refresh();await OpenBattle();}));
    }
}

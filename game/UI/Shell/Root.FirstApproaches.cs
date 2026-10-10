using Godot;
using Mist.Application.CombatSessions;
using Mist.Application.Contracts;
using Mist.Application.FirstApproaches;
using Mist.Application.UI;
using Mist.Core.Exploration;
using Mist.Core.FirstApproaches;
using Mist.Core.Combat;
using System.Collections.Immutable;

public partial class Root
{
    private FirstApproachService? _firstApproaches;
    private string? _firstApproachSelectedEncounter;
    private bool FirstApproachEncounterSelected=>_firstApproaches is not null&&_firstApproachSelectedEncounter is FirstApproachRules.Side or FirstApproachRules.Outer;
    private string FirstApproachBattleTitle=>"塔基通行处置 · "+_firstApproachSelectedEncounter;
    private void LoadFirstApproaches(ImmutableDictionary<string,SceneGeometry> geometry)
    {
        using var file=Godot.FileAccess.Open("res://content/first_approaches/learner_package.json",Godot.FileAccess.ModeFlags.Read);if(file is null)throw new InvalidDataException("中继与塔基处置来源包缺失；当前档案保留，请修复安装。");
        _firstApproaches=new(_store,_store,_catalog,FirstApproachPackage.Parse(file.GetAsText()),geometry);
    }
    private void ResetFirstApproachUiState(){_firstApproachSelectedEncounter=null;}
    private async ValueTask RestoreFirstApproachSelection()
    {
        if(_firstApproaches is null)return;foreach(string id in new[]{FirstApproachRules.Side,FirstApproachRules.Outer}){var state=await _firstApproaches.EncounterAsync(id);if(state?.Battle.Outcome is CombatOutcome.Active or CombatOutcome.Defeat){_firstApproachSelectedEncounter=id;return;}}
        if(_view.Actor.Scene!="R1-S09")_firstApproachSelectedEncounter=null;
    }
    private async ValueTask<EncounterState?> FirstApproachCurrentEncounterAsync(){var state=await _firstApproaches!.EncounterAsync(_firstApproachSelectedEncounter!);return state is null?null:new(state.Battle,state.Entry,state.ReturnActor,state.ResultApplied);}
    private ValueTask<EncounterActionPreview> FirstApproachPreviewEncounter(BattleRequest request)=>_firstApproaches!.PreviewActionAsync(_firstApproachSelectedEncounter!,request);
    private ValueTask<CommandResult> FirstApproachConfirmEncounter(Guid token)=>_firstApproaches!.ConfirmAsync(token);
    private ValueTask<CommandResult> FirstApproachRetryEncounter()=>_firstApproaches!.RetryAsync(_firstApproachSelectedEncounter!);
    private ValueTask<CommandResult> FirstApproachWithdrawEncounter()=>_firstApproaches!.WithdrawAsync(_firstApproachSelectedEncounter!);
    private void CloseFirstApproachEncounterPreview(Guid token)=>_firstApproaches?.ClosePreview(token);
    private void AddFirstApproachSourceControls(VBoxContainer flow,string spot)
    {
        if(_firstApproaches is null)return;foreach(var source in _firstApproaches.Package.Materials.Where(m=>m.Scene=="R1-S09"&&m.Spot==spot)){string id=source.Id;Button(flow,id=="R1-APP-R-SIDE"?"取得塔基侧闸完整说明":"取得塔基外闸完整说明",()=>Execute(async()=>{CheckCommit(await _firstApproaches.DiscoverAsync(id));await OpenFirstApproachMaterial(id);}));}
    }
    private async ValueTask AddFirstApproachArchive(VBoxContainer flow)
    {if(_firstApproaches is null)return;foreach(var source in await _firstApproaches.MaterialsAsync()){string id=source.Id;Button(flow,"处置来源 · "+id,()=>Execute(async()=>await OpenFirstApproachMaterial(id)));}}
    private async ValueTask OpenFirstApproachMaterial(string id)
    {
        var source=await _firstApproaches!.OpenMaterialAsync(id);var flow=Panel("塔基通行完整说明 · "+id,InputOwner.Workspace);Label(flow,source.Version+" · 原创未人审；数值、语言校准仍待人工",18);var reader=new TextEdit{Text=string.Join("\n\n",source.Paragraphs.Select(p=>p.En)),Editable=false,WrapMode=TextEdit.LineWrappingMode.Boundary,CustomMinimumSize=new(0,350),AccessibilityName=id+"英文原文"};reader.AddThemeFontSizeOverride("font_size",20*_scale/100);flow.AddChild(reader);AddRegionalDictionary(flow,reader);Button(flow,"显示塔基说明中文辅助",()=>Label(flow,string.Join("\n\n",source.Paragraphs.Select(p=>p.Zh)),20));reader.GrabFocus();
    }
    private async ValueTask<bool> TryFirstApproachInteraction(string spot)
    {
        await Task.CompletedTask;if(_firstApproaches is null||spot is not("R1-S09.side_gate" or "R1-S09.outer_gate"))return false;var flow=Panel(spot=="R1-S09.side_gate"?"塔基侧闸 · 两种实际方法":"塔基外闸 · 正门防卫",InputOwner.Workspace);
        Label(flow,"两条路线只受理一个真实塔基通行结果。通行不会授予塔主机安全、双阀、隔离、校准、船产权或任务奖励。",22);
        if(spot=="R1-S09.side_gate")
        {
            foreach(var step in new[]{(Id:"isolate",Label:"实际隔离侧闸驱动"),(Id:"brace",Label:"实际固定侧闸支架"),(Id:"inspect_zero_energy",Label:"现场检查侧闸零能与零运动")}){string id=step.Id;Button(flow,step.Label,()=>Execute(async()=>{CheckCommit(await _firstApproaches.PerformSideStepAsync(id));ClosePanel();}));}
            Button(flow,"明确选择侧闸浮机战斗或继续",()=>Execute(async()=>await OpenFirstApproachEntrance(FirstApproachRules.Side)));
        }
        else Button(flow,"明确选择外闸机卫战斗或继续",()=>Execute(async()=>await OpenFirstApproachEntrance(FirstApproachRules.Outer)));
        return true;
    }
    private async ValueTask OpenFirstApproachEntrance(string id)
    {
        _firstApproachSelectedEncounter=id;var current=await _firstApproaches!.EncounterAsync(id);if(current is not null&&current.Battle.Outcome!=CombatOutcome.Withdrawn){await OpenBattle();return;}
        var flow=Panel("塔基实际遭遇入口 · "+id,InputOwner.Confirmation);Label(flow,"使用当前主角、装备和两个合法地方值守岗位。普通机卫保护其后排；不得跨过仍在岗的前排直接选择后排。没有击杀经验。",22);
        var difficulty=new OptionButton{AccessibilityName="塔基遭遇难度"};foreach(string label in new[]{"标准","宽容","挑战"})difficulty.AddItem(label);flow.AddChild(difficulty);var supplies=new Dictionary<string,CheckBox>();foreach(var item in _view.Snapshot.Items.Where(i=>i.OwnerKind=="player"&&i.OwnerId=="hero"&&i.DefinitionId is "CON-01" or "CON-02" or "CON-03" or "CON-04")){var c=new CheckBox{Text=item.DefinitionId+" · "+item.Id,AccessibilityName="塔基入口补给 "+item.Id};flow.AddChild(c);supplies.Add(item.Id,c);}Label(flow,"最多携带两件当前实际补给，默认不选。数值仍待人工试玩。",18);
        Button(flow,"明确进入塔基所选遭遇",()=>Execute(async()=>{CheckCommit(await _firstApproaches.BeginAsync(id,new[]{"standard","relaxed","challenge"}[difficulty.Selected],supplies.Where(s=>s.Value.ButtonPressed).Select(s=>s.Key).ToArray()));CloseAllPanels();await Refresh();await OpenBattle();}));
    }
}
